using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Host;

/// <summary>Owns one loopback TraceCollector process for the latest verified Junexis build.</summary>
public sealed class NarrativeSessionService : INarrativeSessionService
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(15);
    private readonly IEventBus _events;
    private readonly IOutput _output;
    private readonly string _collectorPath;
    private readonly Func<NarrativeCollectorStartInfo, IManagedNarrativeProcess> _processFactory;
    private readonly Func<HttpClient> _httpClientFactory;
    private readonly Func<int> _portProvider;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IManagedNarrativeProcess? _process;
    private LatestBuildReceipt? _lastReceipt;
    private CancellationTokenSource? _monitorCancellation;
    private Task? _monitorTask;
    private bool _expectedStop;
    private bool _disposed;
    private NarrativeSessionSnapshot _snapshot = new(
        NarrativeSessionState.Inactive,
        "Narrative session is inactive.",
        null,
        null,
        null,
        null,
        0);

    /// <summary>Creates a managed Narrative session using the configured or discovered TraceCollector.</summary>
    /// <param name="events">Studio event bus.</param>
    /// <param name="output">Studio output sink.</param>
    /// <param name="collectorPath">
    /// Optional executable or DLL path; defaults to <c>JUNEXIS_TRACE_COLLECTOR</c>,
    /// a sibling development build, or <c>junexis-trace-collector</c>.
    /// </param>
    public NarrativeSessionService(
        IEventBus events,
        IOutput output,
        string? collectorPath = null)
        : this(
            events,
            output,
            collectorPath ?? ResolveDevelopmentCollector() ?? "junexis-trace-collector",
            startInfo => ManagedNarrativeProcess.Start(startInfo, output),
            () => new HttpClient { Timeout = TimeSpan.FromSeconds(3) },
            GetFreeTcpPort)
    {
    }

    internal NarrativeSessionService(
        IEventBus events,
        IOutput output,
        string collectorPath,
        Func<NarrativeCollectorStartInfo, IManagedNarrativeProcess> processFactory,
        Func<HttpClient> httpClientFactory,
        Func<int> portProvider)
    {
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _collectorPath = string.IsNullOrWhiteSpace(collectorPath)
            ? throw new ArgumentException("TraceCollector path is required.", nameof(collectorPath))
            : collectorPath;
        _processFactory = processFactory ?? throw new ArgumentNullException(nameof(processFactory));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _portProvider = portProvider ?? throw new ArgumentNullException(nameof(portProvider));
        _events.Subscribe<LatestBuildUpdatedEvent>(OnLatestBuildUpdated);
    }

    /// <inheritdoc />
    public NarrativeSessionSnapshot Snapshot => _snapshot;

    /// <inheritdoc />
    public event Action<NarrativeSessionSnapshot>? SnapshotChanged;

    /// <inheritdoc />
    public async Task StartAsync(
        LatestBuildReceipt receipt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ObjectDisposedException.ThrowIf(_disposed, this);
        _lastReceipt = receipt;
        if (!File.Exists(receipt.DebugManifestPath))
            throw new FileNotFoundException("Verified build debug manifest is unavailable.", receipt.DebugManifestPath);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_snapshot.State == NarrativeSessionState.Ready
                && string.Equals(_snapshot.BuildId, receipt.BuildId, StringComparison.Ordinal)
                && _process is { HasExited: false })
            {
                await RefreshCoreAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            await StopCoreAsync(publishInactive: false, cancellationToken).ConfigureAwait(false);
            var port = _portProvider();
            var baseUrl = $"http://127.0.0.1:{port}";
            Update(new NarrativeSessionSnapshot(
                NarrativeSessionState.Starting,
                $"Starting Narrative for build {ShortId(receipt.BuildId)}...",
                baseUrl,
                receipt.BuildId,
                receipt.DebugManifestPath,
                null,
                0,
                NarrativeRuntimeActivity.Preparing));

            try
            {
                var startInfo = CreateStartInfo(
                    _collectorPath,
                    receipt.ProjectRoot,
                    receipt.DebugManifestPath,
                    port);
                _expectedStop = false;
                _process = _processFactory(startInfo);
                _process.Exited += OnProcessExited;
                await WaitUntilReadyAsync(baseUrl, cancellationToken).ConfigureAwait(false);
                await RefreshCoreAsync(cancellationToken).ConfigureAwait(false);
                StartMonitor();
                _output.Write(
                    "Narrative",
                    $"Managed TraceCollector is ready at {baseUrl} for build {receipt.BuildId}.");
            }
            catch (OperationCanceledException)
            {
                await StopCoreAsync(publishInactive: true, CancellationToken.None).ConfigureAwait(false);
                throw;
            }
            catch (Exception ex)
            {
                await StopCoreAsync(publishInactive: false, CancellationToken.None).ConfigureAwait(false);
                Update(new NarrativeSessionSnapshot(
                    NarrativeSessionState.Failed,
                    $"Narrative session failed: {ex.Message}",
                    baseUrl,
                    receipt.BuildId,
                    receipt.DebugManifestPath,
                    null,
                    0,
                    NarrativeRuntimeActivity.Disconnected));
                _output.Write("Narrative", Snapshot.Status, OutputSeverity.Warning);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task RestartAsync(CancellationToken cancellationToken = default)
    {
        var receipt = _lastReceipt
            ?? throw new InvalidOperationException(
                "Narrative cannot start before Studio observes a verified latest build.");
        await StopAsync(cancellationToken).ConfigureAwait(false);
        await StartAsync(receipt, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopCoreAsync(publishInactive: true, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await RefreshCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _events.Unsubscribe<LatestBuildUpdatedEvent>(OnLatestBuildUpdated);
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopCoreAsync(publishInactive: false, CancellationToken.None).ConfigureAwait(false);
            _disposed = true;
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }

    internal static string? ResolveDevelopmentCollector()
    {
        var configured = Environment.GetEnvironmentVariable("JUNEXIS_TRACE_COLLECTOR");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return configured;

        foreach (var candidate in EnumerateCommandCandidates("junexis-trace-collector", "Junexis.Debugger.TraceCollector.Server.dll"))
        {
            if (File.Exists(candidate))
                return candidate;
        }

        foreach (var root in CandidateRoots())
        foreach (var configuration in new[] { "Debug", "Release" })
        {
            var candidate = Path.Combine(
                root,
                "junexis-narrative",
                "Junexis.Debugger.TraceCollector.Server",
                "bin",
                configuration,
                "net10.0",
                "Junexis.Debugger.TraceCollector.Server.dll");
            if (File.Exists(candidate))
                return candidate;
        }
        return null;
    }

    private static IEnumerable<string> EnumerateCommandCandidates(string executableName, string dllName)
    {
        foreach (var directory in CandidateCommandDirectories())
        {
            if (!Directory.Exists(directory))
                continue;

            var fromPath = Directory.EnumerateFiles(directory, executableName + "*", SearchOption.TopDirectoryOnly)
                .Concat(Directory.EnumerateFiles(directory, dllName, SearchOption.TopDirectoryOnly))
                .Select(file => file.Trim())
                .Where(file => !string.IsNullOrWhiteSpace(file))
                .Distinct(StringComparer.OrdinalIgnoreCase);

            foreach (var candidate in fromPath)
                yield return candidate;
        }
    }

    private static IEnumerable<string> CandidateCommandDirectories()
    {
        foreach (var value in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator))
        {
            if (!string.IsNullOrWhiteSpace(value))
                yield return value;
        }

        foreach (var root in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "Local", "Programs"),
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".junexis", "bin")
                 })
        {
            if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
                yield return root;
        }
    }

    private async void OnLatestBuildUpdated(LatestBuildUpdatedEvent update)
    {
        try
        {
            await StartAsync(update.Receipt).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _output.Write(
                "Narrative",
                $"Automatic Narrative session could not follow build {update.Receipt.BuildId}: {ex.Message}",
                OutputSeverity.Warning);
        }
    }

    private async Task RefreshCoreAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_snapshot.BaseUrl)
            || string.IsNullOrWhiteSpace(_snapshot.BuildId)
            || _process is null
            || _process.HasExited)
        {
            return;
        }

        using var http = _httpClientFactory();
        var health = await http.GetFromJsonAsync<NarrativeHealthDto>(
            $"{_snapshot.BaseUrl}/health",
            cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("TraceCollector returned an empty health response.");
        ulong? latestEventId = null;
        using var latestResponse = await http.GetAsync(
            $"{_snapshot.BaseUrl}/events/latest?buildId={Uri.EscapeDataString(_snapshot.BuildId)}",
            cancellationToken).ConfigureAwait(false);
        if (latestResponse.StatusCode != HttpStatusCode.NotFound)
        {
            latestResponse.EnsureSuccessStatusCode();
            var latest = await latestResponse.Content.ReadFromJsonAsync<NarrativeEventDto>(
                cancellationToken: cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidDataException("TraceCollector returned an empty latest-event response.");
            latestEventId = latest.EventId;
        }

        var eventDelta = Math.Max(0, health.TotalEvents - _snapshot.TotalEvents);
        var observedAt = eventDelta > 0 || latestEventId != _snapshot.LatestEventId
            ? DateTimeOffset.UtcNow
            : _snapshot.LastEventObservedAt;
        Update(_snapshot with
        {
            State = NarrativeSessionState.Ready,
            Status = latestEventId is null
                ? $"Narrative is ready for build {ShortId(_snapshot.BuildId)}; waiting for runtime events."
                : $"Narrative is ready; latest event #{latestEventId}.",
            LatestEventId = latestEventId,
            TotalEvents = health.TotalEvents,
            Activity = latestEventId is null
                ? NarrativeRuntimeActivity.WaitingForEvents
                : NarrativeRuntimeActivity.Live,
            EventDelta = eventDelta,
            LastEventObservedAt = observedAt
        });
    }

    private async Task WaitUntilReadyAsync(string baseUrl, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + StartupTimeout;
        Exception? lastError = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_process is { HasExited: true })
                throw new InvalidOperationException(
                    $"TraceCollector exited during startup with code {_process.ExitCode?.ToString() ?? "unknown"}.");
            try
            {
                using var http = _httpClientFactory();
                using var response = await http.GetAsync($"{baseUrl}/health", cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                    return;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                lastError = ex;
            }
            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        }
        throw new TimeoutException(
            lastError is null
                ? "TraceCollector health check timed out."
                : $"TraceCollector health check timed out: {lastError.Message}");
    }

    private async Task StopCoreAsync(bool publishInactive, CancellationToken cancellationToken)
    {
        var monitorCancellation = _monitorCancellation;
        _monitorCancellation = null;
        _monitorTask = null;
        if (monitorCancellation is not null)
        {
            monitorCancellation.Cancel();
            monitorCancellation.Dispose();
        }

        var process = _process;
        if (process is null)
        {
            if (publishInactive)
                Update(InactiveSnapshot());
            return;
        }

        _expectedStop = true;
        if (publishInactive)
            Update(_snapshot with
            {
                State = NarrativeSessionState.Stopping,
                Status = "Stopping managed Narrative session.",
                Activity = NarrativeRuntimeActivity.Preparing,
                EventDelta = 0
            });
        process.Exited -= OnProcessExited;
        await process.StopAsync(cancellationToken).ConfigureAwait(false);
        process.Dispose();
        _process = null;
        if (publishInactive)
            Update(InactiveSnapshot());
    }

    private void OnProcessExited(int? exitCode)
    {
        if (_expectedStop || _disposed)
            return;
        Update(_snapshot with
        {
            State = NarrativeSessionState.Failed,
            Status = $"Managed TraceCollector exited unexpectedly with code {exitCode?.ToString() ?? "unknown"}.",
            Activity = NarrativeRuntimeActivity.Disconnected,
            EventDelta = 0
        });
    }

    private void StartMonitor()
    {
        _monitorCancellation?.Cancel();
        _monitorCancellation?.Dispose();
        _monitorCancellation = new CancellationTokenSource();
        var cancellationToken = _monitorCancellation.Token;
        _monitorTask = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            try
            {
                while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
                    await RefreshAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                Update(_snapshot with
                {
                    State = NarrativeSessionState.Failed,
                    Status = $"Narrative monitoring failed: {ex.Message}",
                    Activity = NarrativeRuntimeActivity.Disconnected,
                    EventDelta = 0
                });
            }
        }, cancellationToken);
    }

    private void Update(NarrativeSessionSnapshot snapshot)
    {
        _snapshot = snapshot;
        SnapshotChanged?.Invoke(snapshot);
    }

    private static NarrativeSessionSnapshot InactiveSnapshot() => new(
        NarrativeSessionState.Inactive,
        "Narrative session is inactive.",
        null,
        null,
        null,
        null,
        0);

    private static NarrativeCollectorStartInfo CreateStartInfo(
        string collectorPath,
        string workingDirectory,
        string debugManifestPath,
        int port)
    {
        var arguments = new List<string>();
        string fileName;
        if (collectorPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            fileName = "dotnet";
            arguments.Add(Path.GetFullPath(collectorPath));
        }
        else
        {
            fileName = collectorPath;
        }
        arguments.AddRange([
            "--host", "127.0.0.1",
            "--port", port.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--manifest", Path.GetFullPath(debugManifestPath)
        ]);
        return new NarrativeCollectorStartInfo(fileName, arguments, workingDirectory);
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static IEnumerable<string> CandidateRoots()
    {
        var current = new DirectoryInfo(Environment.CurrentDirectory);
        for (var depth = 0; current is not null && depth < 5; depth++, current = current.Parent)
            yield return current.FullName;
    }

    private static string ShortId(string value) => value[..Math.Min(12, value.Length)];

    private sealed record NarrativeHealthDto
    {
        public string Status { get; init; } = string.Empty;
        public long TotalEvents { get; init; }
    }

    private sealed record NarrativeEventDto
    {
        public ulong EventId { get; init; }
    }
}

internal sealed record NarrativeCollectorStartInfo(
    string FileName,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory);

internal interface IManagedNarrativeProcess : IDisposable
{
    bool HasExited { get; }
    int? ExitCode { get; }
    event Action<int?>? Exited;
    Task StopAsync(CancellationToken cancellationToken);
}

internal sealed class ManagedNarrativeProcess : IManagedNarrativeProcess
{
    private readonly Process _process;

    private ManagedNarrativeProcess(Process process)
    {
        _process = process;
        _process.Exited += (_, _) => Exited?.Invoke(ExitCode);
    }

    public bool HasExited => _process.HasExited;
    public int? ExitCode => _process.HasExited ? _process.ExitCode : null;
    public event Action<int?>? Exited;

    internal static ManagedNarrativeProcess Start(
        NarrativeCollectorStartInfo startInfo,
        IOutput output)
    {
        var processStartInfo = new ProcessStartInfo
        {
            FileName = startInfo.FileName,
            WorkingDirectory = startInfo.WorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in startInfo.Arguments)
            processStartInfo.ArgumentList.Add(argument);
        var process = new Process { StartInfo = processStartInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data))
                output.Write("Narrative", args.Data);
        };
        process.ErrorDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data))
                output.Write("Narrative", args.Data, OutputSeverity.Warning);
        };
        if (!process.Start())
            throw new InvalidOperationException("TraceCollector process could not be started.");
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return new ManagedNarrativeProcess(process);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_process.HasExited)
            return;
        try
        {
            _process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            return;
        }
        await _process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Dispose() => _process.Dispose();
}

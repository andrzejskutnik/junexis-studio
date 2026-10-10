using System.Diagnostics;
using System.Text.Json;
using System.Collections.Concurrent;
using Junexis.Studio.Abstractions;

namespace JunexisLang.Studio.CompilerExtension;

/// <summary>
/// Uruchamia zweryfikowaną aplikację Junexis przez publiczne CLI
/// i odwzorowuje jej stan w Studio.
/// </summary>
public sealed class JunexisReportApplicationSession : IReportApplicationSession, IDisposable
{
    private const string DiagnosticSource = "junexis-lang";
    private const string RuntimeDiagnosticSource = "junexis-report-runtime";
    private const string OutputChannel = "Report";
    private const string ReadyPrefix = "READY report · ";
    private bool _panelReceived;

    private readonly JunexisCompilerOptions _options;
    private readonly IProcessService _processes;
    private readonly ICompilerPreflightService _compiler;
    private readonly IHostContext _context;
    private readonly Action<Uri> _openBrowser;
    private readonly object _gate = new();
    private CancellationTokenSource? _sessionCancellation;
    private ReportApplicationSnapshot _current = new(
        ReportApplicationState.Idle,
        "Panel nie jest uruchomiony.");
    private bool _disposed;

    /// <summary>Tworzy sesję podłączoną do usług hosta Studio.</summary>
    /// <param name="options">Konfiguracja publicznego CLI Junexis.</param>
    /// <param name="processes">Usługa nadzorująca proces CLI.</param>
    /// <param name="compiler">Kompilator używany do preflightu diagnostycznego.</param>
    /// <param name="context">Kontekst usług i wyjścia Studio.</param>
    public JunexisReportApplicationSession(
        JunexisCompilerOptions options,
        IProcessService processes,
        ICompilerPreflightService compiler,
        IHostContext context)
        : this(options, processes, compiler, context, OpenWithSystemBrowser)
    {
    }

    internal JunexisReportApplicationSession(
        JunexisCompilerOptions options,
        IProcessService processes,
        ICompilerPreflightService compiler,
        IHostContext context,
        Action<Uri> openBrowser)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _processes = processes ?? throw new ArgumentNullException(nameof(processes));
        _compiler = compiler ?? throw new ArgumentNullException(nameof(compiler));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _openBrowser = openBrowser ?? throw new ArgumentNullException(nameof(openBrowser));
    }

    /// <inheritdoc />
    public ReportApplicationSnapshot Current
    {
        get
        {
            lock (_gate)
                return _current;
        }
    }

    /// <inheritdoc />
    public event Action<ReportApplicationSnapshot>? Changed;

    /// <inheritdoc />
    public async Task StartAsync(
        string workspaceRoot,
        string? activeDocumentPath,
        int durationSeconds = 300,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (durationSeconds is < 1 or > 300)
            throw new ArgumentOutOfRangeException(nameof(durationSeconds), "Czas sesji musi mieścić się w zakresie 1–300 sekund.");

        var buildContext = new BuildContext(workspaceRoot ?? string.Empty, activeDocumentPath);
        var input = JunexisLangCompilerService.ResolveBuildInput(buildContext);
        if (input is null)
        {
            Fail("Nie znaleziono projektu Junexis ani aktywnego pliku .jnx.");
            return;
        }

        CancellationTokenSource sessionCancellation;
        lock (_gate)
        {
            if (_sessionCancellation is not null)
            {
                _context.Output.Write(OutputChannel,
                    "Sesja już działa. Zatrzymaj ją przed ponownym uruchomieniem.",
                    OutputSeverity.Warning);
                return;
            }

            sessionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _sessionCancellation = sessionCancellation;
            _panelReceived = false;
        }

        SetSnapshot(ReportApplicationState.Starting, "Sprawdzanie projektu przed uruchomieniem…");
        _context.Output.Write(OutputChannel, "Preflight aplikacji Junexis…");
        _context.Diagnostics.ClearBySource(RuntimeDiagnosticSource);
        _context.EventBus.Publish(new DiagnosticsUpdatedEvent(RuntimeDiagnosticSource, []));

        try
        {
            var preflight = await _compiler.PreflightAsync(buildContext, sessionCancellation.Token)
                .ConfigureAwait(false);
            PublishDiagnostics(preflight);
            if (!preflight.Success)
            {
                Fail($"Preflight zatrzymał uruchomienie: {preflight.Diagnostics.Count} problemów.");
                return;
            }

            SetSnapshot(ReportApplicationState.Starting, "Budowanie i uruchamianie aplikacji Junexis…");
            var request = CreateRunRequest(input, durationSeconds);
            var runtimeErrors = new ConcurrentQueue<string>();
            var result = await _processes.RunAsync(
                request,
                line =>
                {
                    if (!sessionCancellation.IsCancellationRequested && ReferenceEquals(_sessionCancellation, sessionCancellation))
                        HandleOutput(line);
                    if (line.Kind == ProcessOutputKind.StdErr)
                        runtimeErrors.Enqueue(line.Text);
                },
                sessionCancellation.Token).ConfigureAwait(false);

            if (result.ExitCode == 0)
            {
                SetSnapshot(
                    ReportApplicationState.Idle,
                    "Sesja zakończyła się poprawnie. Uruchom ją ponownie, aby odtworzyć panel.");
            }
            else
            {
                PublishRuntimeFailure(runtimeErrors);
                Fail($"Aplikacja zakończyła się kodem {result.ExitCode}. Szczegóły są w kanale Report.");
            }
        }
        catch (OperationCanceledException) when (sessionCancellation.IsCancellationRequested)
        {
            SetSnapshot(ReportApplicationState.Idle, "Sesja została zatrzymana wraz z należącymi do niej procesami.");
            _context.Output.Write(OutputChannel, "Zatrzymano aplikację Junexis.");
        }
        catch (Exception ex)
        {
            Fail($"Nie udało się uruchomić aplikacji: {ex.Message}");
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_sessionCancellation, sessionCancellation))
                    _sessionCancellation = null;
            }
            sessionCancellation.Dispose();
        }
    }

    /// <inheritdoc />
    public bool Open()
    {
        var url = Current.Url;
        if (url is null)
        {
            _context.Output.Write(OutputChannel, "Panel nie ma jeszcze gotowego adresu.", OutputSeverity.Warning);
            return false;
        }

        try
        {
            _openBrowser(url);
            _context.Output.Write(OutputChannel, $"Otwarto panel: {url}");
            return true;
        }
        catch (Exception ex)
        {
            _context.Output.Write(
                OutputChannel,
                $"Panel działa, ale nie udało się otworzyć przeglądarki: {ex.Message}",
                OutputSeverity.Error);
            return false;
        }
    }

    /// <inheritdoc />
    public void Stop()
    {
        CancellationTokenSource? cancellation;
        lock (_gate)
            cancellation = _sessionCancellation;

        if (cancellation is null)
        {
            _context.Output.Write(OutputChannel, "Nie ma aktywnej sesji do zatrzymania.");
            return;
        }

        SetSnapshot(ReportApplicationState.Stopping, "Zatrzymywanie aplikacji Junexis…");
        cancellation.Cancel();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Stop();
    }

    internal ProcessRequest CreateRunRequest(string input, int durationSeconds)
    {
        var cliPath = _options.ResolvedCliPath;
        var arguments = new List<string>();
        var fileName = cliPath;
        if (cliPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            fileName = "dotnet";
            arguments.Add(Path.GetFullPath(cliPath));
        }
        arguments.AddRange([
            "run",
            input,
            "--duration",
            durationSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--no-open"
        ]);
        return new ProcessRequest(fileName, arguments, JunexisLangCompilerService.ResolveWorkingDirectory(input));
    }

    private void HandleOutput(ProcessOutputLine line)
    {
        var severity = line.Kind == ProcessOutputKind.StdErr
            ? OutputSeverity.Error
            : OutputSeverity.Info;
        _context.Output.Write(OutputChannel, line.Text, severity);

        if (line.Kind != ProcessOutputKind.StdOut || _panelReceived) return;
        if (line.Text == "RUNNING server without a declared panel")
        {
            SetSnapshot(ReportApplicationState.Ready, "Serwer działa bez zadeklarowanego panelu.");
            return;
        }
        var url = ParsePanelReady(line.Text);
        if (url is null) return;
        _panelReceived = true;
        SetSnapshot(ReportApplicationState.Ready, "Panel działa. Studio nadzoruje aplikację.", url);
        Open();
    }

    internal static Uri? ParsePanelReady(string line)
    {
        string? candidate;
        if (line.Length > 16384) return null;
        if (line.StartsWith("JUNEXIS_READY ", StringComparison.Ordinal))
        {
            try
            {
                using var document = JsonDocument.Parse(line[14..]);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object
                    || !root.TryGetProperty("schema", out var schema) || schema.GetString() != "junexis.panel-ready/1"
                    || !root.TryGetProperty("sessionId", out var id) || !Guid.TryParseExact(id.GetString(), "D", out _)
                    || !root.TryGetProperty("kind", out var kind) || kind.GetString() is not ("server" or "report")
                    || !root.TryGetProperty("url", out var value)) return null;
                candidate = value.GetString();
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException) { return null; }
        }
        else if (line.StartsWith(ReadyPrefix, StringComparison.Ordinal))
            candidate = line[ReadyPrefix.Length..].Trim();
        else return null;
        return Uri.TryCreate(candidate, UriKind.Absolute, out var url)
            && url.Scheme is "http" or "https"
            && url.Host is "127.0.0.1" or "localhost" or "[::1]"
            && string.IsNullOrEmpty(url.UserInfo) ? url : null;
    }

    private void PublishDiagnostics(CompilerPreflightResult preflight)
    {
        _context.Diagnostics.ClearBySource(preflight.CompilerId);
        foreach (var diagnostic in preflight.Diagnostics)
            _context.Diagnostics.Add(diagnostic);
        _context.EventBus.Publish(new DiagnosticsUpdatedEvent(
            preflight.CompilerId,
            preflight.Diagnostics,
            preflight.BuildId));

        foreach (var diagnostic in preflight.Diagnostics)
        {
            var severity = diagnostic.Severity switch
            {
                DiagnosticSeverity.Error => OutputSeverity.Error,
                DiagnosticSeverity.Warning => OutputSeverity.Warning,
                _ => OutputSeverity.Info,
            };
            _context.Output.Write(OutputChannel,
                $"{diagnostic.Code ?? diagnostic.Severity.ToString()}: {diagnostic.Message}",
                severity);
        }
        if (preflight.Diagnostics.Count > 0)
            _context.EventBus.Publish(new ActivatePanelRequested("problems"));
    }

    private void PublishRuntimeFailure(ConcurrentQueue<string> lines)
    {
        var details = string.Join(Environment.NewLine, lines).Trim();
        var message = string.IsNullOrWhiteSpace(details)
            ? "Budowanie lub uruchamianie aplikacji Junexis zakończyło się błędem."
            : details.Length <= 4000 ? details : details[..4000] + "…";
        var diagnostic = new Diagnostic(
            DiagnosticSeverity.Error,
            message,
            source: RuntimeDiagnosticSource,
            code: "RPT001");
        _context.Diagnostics.ClearBySource(RuntimeDiagnosticSource);
        _context.Diagnostics.Add(diagnostic);
        _context.EventBus.Publish(new DiagnosticsUpdatedEvent(
            RuntimeDiagnosticSource,
            [diagnostic]));
        _context.EventBus.Publish(new ActivatePanelRequested("problems"));
    }

    private void Fail(string status)
    {
        SetSnapshot(ReportApplicationState.Failed, status);
        _context.Output.Write(OutputChannel, status, OutputSeverity.Error);
    }

    private void SetSnapshot(ReportApplicationState state, string status, Uri? url = null)
    {
        var snapshot = new ReportApplicationSnapshot(state, status, url);
        lock (_gate)
            _current = snapshot;
        Changed?.Invoke(snapshot);
    }

    private static void OpenWithSystemBrowser(Uri url) =>
        Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true });
}

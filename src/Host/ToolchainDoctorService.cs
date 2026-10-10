using System.Collections.Concurrent;
using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Host;

/// <summary>Probes the command-line tools used by Junexis Studio.</summary>
public sealed class ToolchainDoctorService : IToolchainDoctorService
{
    private static readonly TimeSpan DefaultProbeTimeout = TimeSpan.FromSeconds(5);
    private readonly IProcessService _processes;
    private readonly IReadOnlyList<ToolProbe> _probes;
    private readonly TimeSpan _probeTimeout;
    private ToolchainDoctorSnapshot? _latestSnapshot;

    /// <summary>Creates the default Studio toolchain doctor.</summary>
    /// <param name="processes">External process runner.</param>
    public ToolchainDoctorService(IProcessService processes)
        : this(processes, new DebugAdapterRegistry(), new PlatformToolResolver())
    {
    }

    /// <summary>Creates a Studio toolchain doctor with a concrete adapter registry.</summary>
    /// <param name="processes">External process runner.</param>
    /// <param name="adapters">Concrete debug adapter registry.</param>
    public ToolchainDoctorService(IProcessService processes, IDebugAdapterRegistry adapters)
        : this(processes, adapters, new PlatformToolResolver())
    {
    }

    internal ToolchainDoctorService(
        IProcessService processes,
        IDebugAdapterRegistry adapters,
        IPlatformToolResolver tools)
        : this(processes, CreateDefaultProbes(adapters, tools), DefaultProbeTimeout)
    {
    }

    internal ToolchainDoctorService(
        IProcessService processes,
        IReadOnlyList<ToolProbe> probes,
        TimeSpan probeTimeout)
    {
        _processes = processes ?? throw new ArgumentNullException(nameof(processes));
        _probes = probes ?? throw new ArgumentNullException(nameof(probes));
        _probeTimeout = probeTimeout;
    }

    /// <inheritdoc />
    public ToolchainDoctorSnapshot? LatestSnapshot => Volatile.Read(ref _latestSnapshot);

    /// <inheritdoc />
    public async Task<ToolchainDoctorSnapshot> CheckAsync(CancellationToken cancellationToken = default)
    {
        var checks = _probes.Select(probe => ProbeAsync(probe, cancellationToken));
        var capabilities = await Task.WhenAll(checks).ConfigureAwait(false);
        var snapshot = new ToolchainDoctorSnapshot(DateTimeOffset.UtcNow, capabilities);
        Volatile.Write(ref _latestSnapshot, snapshot);
        return snapshot;
    }

    private async Task<ToolchainCapability> ProbeAsync(ToolProbe probe, CancellationToken cancellationToken)
    {
        if (probe.PresenceOnly)
        {
            var available = File.Exists(probe.FileName);
            return CreateCapability(probe, available, available ? probe.FileName : null,
                available ? $"Ready ({probe.Source})." : $"Adapter not found ({probe.Source}).");
        }
        var output = new ConcurrentQueue<string>();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_probeTimeout);
        try
        {
            var result = await _processes.RunAsync(
                new ProcessRequest(probe.FileName, probe.Arguments),
                line =>
                {
                    if (!string.IsNullOrWhiteSpace(line.Text))
                        output.Enqueue(line.Text.Trim());
                },
                timeout.Token).ConfigureAwait(false);
            var firstLine = output.FirstOrDefault();
            if (result.ExitCode == 0)
            {
                return CreateCapability(
                    probe,
                    true,
                    firstLine ?? "available",
                    "Ready.");
            }

            return CreateCapability(
                probe,
                false,
                null,
                firstLine ?? $"Command exited with code {result.ExitCode}.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return CreateCapability(probe, false, null, $"Probe timed out after {_probeTimeout.TotalSeconds:0.#} s.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return CreateCapability(probe, false, null, $"Unavailable: {ex.Message}");
        }
    }

    private static ToolchainCapability CreateCapability(
        ToolProbe probe,
        bool available,
        string? version,
        string diagnostic) =>
        new(probe.Id, probe.DisplayName, probe.Languages, probe.CommandText, available, version, diagnostic);

    private static IReadOnlyList<ToolProbe> CreateDefaultProbes(
        IDebugAdapterRegistry adapters,
        IPlatformToolResolver tools)
    {
        var junexis = tools.ResolveCommand("junexis", "JUNEXIS_CLI");
        var mentor = tools.ResolveCommand("junexis-mentor", "JUNEXIS_MENTOR_CLI");
        var narrative = tools.ResolveCommand("junexis-trace-collector", "JUNEXIS_TRACE_COLLECTOR");

        return
        [
            ToolProbe.ForToolOrDll("junexis", "Junexis Compiler", "Junexis", junexis, "--version"),
            ToolProbe.ForToolOrDll("mentor", "Junexis Mentor", "Junexis analysis", mentor, "--help"),
            ToolProbe.ForToolOrDll(
                "narrative",
                "Junexis TraceCollector",
                "Narrative",
                narrative,
                "--distribution-manifest studio-probe trace-collector false"),
            new ToolProbe("dotnet", ".NET SDK", "C# / Junexis tools", "dotnet", "--version"),
            new ToolProbe("cpp", "GNU C++", "C / C++", DebugToolLocator.ResolveGxx(), "--version"),
            new ToolProbe("go", "Go", "Go", "go", "version"),
            new ToolProbe("node", "Node.js", "JavaScript / TypeScript", "node", "--version"),
            new ToolProbe("typescript", "TypeScript Compiler", "TypeScript", "tsc", "--version"),
            new ToolProbe(
                "clangd-lsp",
                "clangd Language Server",
                "C / C++ language services",
                Environment.GetEnvironmentVariable("JUNEXIS_CLANGD") ?? "clangd",
                "--version"),
            new ToolProbe(
                "gopls-lsp",
                "gopls Language Server",
                "Go language services",
                Environment.GetEnvironmentVariable("JUNEXIS_GOPLS") ?? "gopls",
                "version"),
            new ToolProbe(
                "typescript-lsp",
                "TypeScript Language Server",
                "TypeScript / JavaScript language services",
                Environment.GetEnvironmentVariable("JUNEXIS_TYPESCRIPT_LSP") ?? "typescript-language-server",
                "--version"),
            new ToolProbe(
                "gdb",
                "GNU Debugger",
                "C / C++ debugging",
                DebugToolLocator.ResolveGdb(),
                "--version"),
            new ToolProbe("delve", "Delve Debugger", "Go debugging", DebugToolLocator.ResolveDelve(), "version"),
            CreateAdapterProbe(adapters, "cpp", "cpp-dap", "C++ DAP Adapter", "C / C++ DAP"),
            CreateAdapterProbe(adapters, "go", "go-dap", "Go DAP Adapter", "Go DAP"),
            CreateAdapterProbe(adapters, "typescript", "typescript-dap", "TypeScript DAP Adapter", "TypeScript DAP"),
        ];
    }

    private static ToolProbe CreateAdapterProbe(
        IDebugAdapterRegistry adapters,
        string profileId,
        string capabilityId,
        string displayName,
        string languages)
    {
        var adapter = adapters.Resolve(profileId);
        return adapter is null
            ? new ToolProbe(capabilityId, displayName, languages, $"missing:{profileId}", string.Empty,
                PresenceOnly: true, Source: "resolver")
            : new ToolProbe(capabilityId, displayName, languages, adapter.FileName, adapter.Arguments,
                PresenceOnly: true, Source: adapter.Source);
    }

    internal sealed record ToolProbe(
        string Id,
        string DisplayName,
        string Languages,
        string FileName,
        string Arguments,
        bool PresenceOnly = false,
        string Source = "process-probe")
    {
        public string CommandText => string.IsNullOrWhiteSpace(Arguments) ? FileName : $"{FileName} {Arguments}";

        public static ToolProbe ForToolOrDll(
            string id,
            string displayName,
            string languages,
            string toolOrDll,
            string arguments) =>
            toolOrDll.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                ? new ToolProbe(id, displayName, languages, "dotnet", $"{Quote(toolOrDll)} {arguments}")
                : new ToolProbe(id, displayName, languages, toolOrDll, arguments);

        private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
    }
}

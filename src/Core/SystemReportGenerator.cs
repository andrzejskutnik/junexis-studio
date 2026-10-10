using Junexis.Studio.Abstractions;
using System.Runtime.InteropServices;
using System.Text;

namespace Junexis.Studio.Core;

/// <summary>
/// Generates a system diagnostic report from the current IDE state.
/// Uses pluggable providers so it can work without full Host wiring.
/// </summary>
public sealed class SystemReportGenerator : ISystemReportGenerator
{
    private readonly IOutput? _output;
    private readonly Func<string?>? _workspaceRootProvider;
    private readonly Func<IReadOnlyList<ExtensionInfo>>? _extensionsProvider;
    private readonly Func<IReadOnlyList<ServiceStatus>>? _servicesProvider;
    private readonly Func<IReadOnlyList<string>>? _diagnosticsProvider;

    /// <summary>Output channel name for report messages.</summary>
    public const string OutputChannel = "Integrity";

    /// <summary>
    /// Creates a system report generator.
    /// </summary>
    /// <param name="output">Optional output service for logging the report.</param>
    /// <param name="workspaceRootProvider">Provides the workspace root path.</param>
    /// <param name="extensionsProvider">Provides the list of loaded extensions.</param>
    /// <param name="servicesProvider">Provides the list of service statuses.</param>
    /// <param name="diagnosticsProvider">Provides recent diagnostics messages.</param>
    public SystemReportGenerator(
        IOutput? output = null,
        Func<string?>? workspaceRootProvider = null,
        Func<IReadOnlyList<ExtensionInfo>>? extensionsProvider = null,
        Func<IReadOnlyList<ServiceStatus>>? servicesProvider = null,
        Func<IReadOnlyList<string>>? diagnosticsProvider = null)
    {
        _output = output;
        _workspaceRootProvider = workspaceRootProvider;
        _extensionsProvider = extensionsProvider;
        _servicesProvider = servicesProvider;
        _diagnosticsProvider = diagnosticsProvider;
    }

    /// <inheritdoc />
    public SystemReport Generate()
    {
        var env = new EnvironmentInfo(
            OsDescription: RuntimeInformation.OSDescription,
            RuntimeVersion: RuntimeInformation.FrameworkDescription,
            WorkspaceRoot: _workspaceRootProvider?.Invoke(),
            ProcessArchitecture: RuntimeInformation.ProcessArchitecture.ToString());

        var extensions = _extensionsProvider?.Invoke() ?? [];
        var services = _servicesProvider?.Invoke() ?? [];
        var diagnostics = _diagnosticsProvider?.Invoke() ?? [];

        var report = new SystemReport(
            GeneratedAtUtc: DateTime.UtcNow,
            Environment: env,
            Extensions: extensions,
            Services: services,
            RecentDiagnostics: diagnostics);

        _output?.Write(OutputChannel, "System report generated.", OutputSeverity.Info);

        return report;
    }

    /// <summary>
    /// Formats a <see cref="SystemReport"/> as a human-readable string.
    /// Deterministic: no secrets, stable format.
    /// </summary>
    /// <param name="report">The report to format.</param>
    /// <returns>Formatted text report.</returns>
    public static string Format(SystemReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var sb = new StringBuilder();
        sb.AppendLine("=== JunexisStudio System Report ===");
        sb.AppendLine($"Generated: {report.GeneratedAtUtc:O}");
        sb.AppendLine();

        sb.AppendLine("-- Environment --");
        sb.AppendLine($"OS:           {report.Environment.OsDescription}");
        sb.AppendLine($"Runtime:      {report.Environment.RuntimeVersion}");
        sb.AppendLine($"Architecture: {report.Environment.ProcessArchitecture}");
        sb.AppendLine($"Workspace:    {report.Environment.WorkspaceRoot ?? "(not opened)"}");
        sb.AppendLine();

        sb.AppendLine($"-- Extensions ({report.Extensions.Count}) --");
        if (report.Extensions.Count == 0)
        {
            sb.AppendLine("  (none)");
        }
        else
        {
            foreach (var ext in report.Extensions)
            {
                sb.AppendLine($"  [{(ext.IsActive ? "active" : "inactive")}] {ext.Id}");
            }
        }
        sb.AppendLine();

        sb.AppendLine($"-- Services ({report.Services.Count}) --");
        if (report.Services.Count == 0)
        {
            sb.AppendLine("  (none)");
        }
        else
        {
            foreach (var svc in report.Services)
            {
                var icon = svc.IsHealthy ? "OK" : "!!";
                sb.AppendLine($"  [{icon}] {svc.Name}: {svc.Status}");
            }
        }
        sb.AppendLine();

        sb.AppendLine($"-- Recent Diagnostics ({report.RecentDiagnostics.Count}) --");
        if (report.RecentDiagnostics.Count == 0)
        {
            sb.AppendLine("  (none)");
        }
        else
        {
            foreach (var d in report.RecentDiagnostics)
            {
                sb.AppendLine($"  - {d}");
            }
        }

        return sb.ToString();
    }
}

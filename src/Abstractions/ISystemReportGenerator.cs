namespace Junexis.Studio.Abstractions;

/// <summary>
/// Generates a system diagnostic report for troubleshooting and bug reporting.
/// The report is deterministic and must not contain secrets.
/// </summary>
public interface ISystemReportGenerator
{
    /// <summary>
    /// Generates a system report.
    /// </summary>
    /// <returns>The structured system report.</returns>
    SystemReport Generate();
}

/// <summary>
/// Structured system report containing diagnostic information.
/// </summary>
/// <param name="GeneratedAtUtc">UTC timestamp when the report was generated.</param>
/// <param name="Environment">Environment information (OS, runtime, etc.).</param>
/// <param name="Extensions">Loaded extensions and their statuses.</param>
/// <param name="Services">Service health statuses.</param>
/// <param name="RecentDiagnostics">Recent system diagnostics/issues.</param>
public sealed record SystemReport(
    DateTime GeneratedAtUtc,
    EnvironmentInfo Environment,
    IReadOnlyList<ExtensionInfo> Extensions,
    IReadOnlyList<ServiceStatus> Services,
    IReadOnlyList<string> RecentDiagnostics);

/// <summary>
/// Environment information (deterministic, no secrets).
/// </summary>
/// <param name="OsDescription">Operating system description.</param>
/// <param name="RuntimeVersion">.NET runtime version.</param>
/// <param name="WorkspaceRoot">Workspace root path (null if not opened).</param>
/// <param name="ProcessArchitecture">Process architecture (x64, ARM64, etc.).</param>
public sealed record EnvironmentInfo(
    string OsDescription,
    string RuntimeVersion,
    string? WorkspaceRoot,
    string ProcessArchitecture);

/// <summary>
/// Information about a loaded extension.
/// </summary>
/// <param name="Id">Extension identifier.</param>
/// <param name="IsActive">Whether the extension is currently active.</param>
public sealed record ExtensionInfo(string Id, bool IsActive);

/// <summary>
/// Health status of a service/subsystem.
/// </summary>
/// <param name="Name">Service name.</param>
/// <param name="Status">Human-readable status string.</param>
/// <param name="IsHealthy">Whether the service is functioning normally.</param>
public sealed record ServiceStatus(string Name, string Status, bool IsHealthy);

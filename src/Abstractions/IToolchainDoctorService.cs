namespace Junexis.Studio.Abstractions;

/// <summary>Describes the availability of one external development tool.</summary>
/// <param name="Id">Stable capability identifier.</param>
/// <param name="DisplayName">Human-readable tool name.</param>
/// <param name="Languages">Languages or workflows enabled by the tool.</param>
/// <param name="Command">Command actually probed by Studio.</param>
/// <param name="IsAvailable">Whether the probe completed successfully.</param>
/// <param name="Version">Version or identity reported by the tool.</param>
/// <param name="Diagnostic">Actionable probe result.</param>
public sealed record ToolchainCapability(
    string Id,
    string DisplayName,
    string Languages,
    string Command,
    bool IsAvailable,
    string? Version,
    string Diagnostic);

/// <summary>Immutable result of a Studio toolchain health check.</summary>
/// <param name="CheckedAt">UTC time at which the check completed.</param>
/// <param name="Capabilities">Tool capabilities in deterministic display order.</param>
public sealed record ToolchainDoctorSnapshot(
    DateTimeOffset CheckedAt,
    IReadOnlyList<ToolchainCapability> Capabilities);

/// <summary>Checks external compilers, runtimes, debuggers, and Junexis tools.</summary>
public interface IToolchainDoctorService
{
    /// <summary>Gets the latest completed snapshot, or null before the first check.</summary>
    ToolchainDoctorSnapshot? LatestSnapshot { get; }

    /// <summary>Runs all tool probes and returns a new capability snapshot.</summary>
    /// <param name="cancellationToken">Token used to cancel all probes.</param>
    /// <returns>The completed capability snapshot.</returns>
    Task<ToolchainDoctorSnapshot> CheckAsync(CancellationToken cancellationToken = default);
}

/// <summary>Published after Studio completes a toolchain health check.</summary>
/// <param name="Snapshot">The new toolchain capability snapshot.</param>
public sealed record ToolchainCapabilitiesUpdatedEvent(ToolchainDoctorSnapshot Snapshot);

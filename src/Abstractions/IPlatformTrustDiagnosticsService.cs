namespace Junexis.Studio.Abstractions;

/// <summary>Result of a Studio trust diagnostic check delegated to <c>junexis trust --json</c>.</summary>
/// <param name="SchemaVersion">Schema version matching <c>junexis.trust-diagnostics/0.1</c>.</param>
/// <param name="Status">Overall trust status (<c>valid</c>, <c>untrusted</c>, <c>invalid-signature</c>, etc.).</param>
/// <param name="Channel">Release channel (<c>preview</c>, <c>stable</c>, etc.).</param>
/// <param name="ReleaseVersion">Platform release version.</param>
/// <param name="Publisher">Identified publisher.</param>
/// <param name="FailureReason">Optional blockage reason code.</param>
/// <param name="Checks">Individual trust checks.</param>
public sealed record PlatformTrustSnapshot(
    string SchemaVersion,
    string Status,
    string Channel,
    string ReleaseVersion,
    string Publisher,
    string? FailureReason,
    IReadOnlyList<PlatformTrustCheckEntry> Checks);

/// <summary>Single check item within a <see cref="PlatformTrustSnapshot"/>.</summary>
/// <param name="Name">Diagnostic test name.</param>
/// <param name="Status">Check outcome (<c>passed</c>, <c>failed</c>, <c>warning</c>).</param>
/// <param name="Detail">Sanitized detail string without raw keys, secrets, or host paths.</param>
public sealed record PlatformTrustCheckEntry(
    string Name,
    string Status,
    string Detail);

/// <summary>Delegates platform trust diagnostics to the public <c>junexis trust</c> CLI.</summary>
public interface IPlatformTrustDiagnosticsService
{
    /// <summary>Gets the latest completed trust snapshot, or null before the first check.</summary>
    PlatformTrustSnapshot? LatestSnapshot { get; }

    /// <summary>Delegates to public CLI and returns a sanitized trust snapshot.</summary>
    /// <param name="catalogPath">Optional explicit release catalog path.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Completed trust diagnostic snapshot.</returns>
    Task<PlatformTrustSnapshot> CheckTrustAsync(string? catalogPath = null, CancellationToken cancellationToken = default);
}

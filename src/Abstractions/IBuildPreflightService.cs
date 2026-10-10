namespace Junexis.Studio.Abstractions;

/// <summary>Compiler-owned analysis produced before target code generation.</summary>
/// <param name="Success">Whether parsing and structural validation succeeded.</param>
/// <param name="Diagnostics">Compiler diagnostics produced by the preflight.</param>
/// <param name="CompilerId">Compiler that produced the result.</param>
/// <param name="BuildId">Stable identity to preserve in the subsequent build.</param>
/// <param name="AnalysisSnapshotPath">Snapshot intended for Mentor analysis.</param>
/// <param name="ProjectRoot">Resolved project root used to scope Mentor delta history.</param>
public sealed record CompilerPreflightResult(
    bool Success,
    IReadOnlyList<Diagnostic> Diagnostics,
    string CompilerId,
    string? BuildId = null,
    string? AnalysisSnapshotPath = null,
    string? ProjectRoot = null);

/// <summary>Optional compiler contract for analysis before code generation.</summary>
public interface ICompilerPreflightService
{
    /// <summary>Checks source and publishes an analysis snapshot without generating target code.</summary>
    Task<CompilerPreflightResult> PreflightAsync(
        BuildContext context,
        CancellationToken cancellationToken = default);

    /// <summary>Builds while preserving and validating the identity established by preflight.</summary>
    Task<CompilerResult> BuildAfterPreflightAsync(
        BuildContext context,
        CompilerPreflightResult preflight,
        CancellationToken cancellationToken = default);
}

/// <summary>Outcome of the optional, non-blocking Mentor build preflight.</summary>
/// <param name="Available">Whether Mentor returned a valid current result.</param>
/// <param name="Envelope">Current result and delta when available.</param>
/// <param name="Message">Compact human-readable outcome.</param>
public sealed record MentorBuildPreflightOutcome(
    bool Available,
    MentorAnalysisEnvelope? Envelope,
    string Message);

/// <summary>Coordinates Mentor analysis at the build decision point.</summary>
public interface IMentorBuildPreflightService
{
    /// <summary>Runs Mentor for a compiler preflight; tool failure never rejects the build.</summary>
    Task<MentorBuildPreflightOutcome> RunAsync(
        CompilerPreflightResult preflight,
        CancellationToken cancellationToken = default);
}

/// <summary>Publishes a completed Mentor preflight for subtle panel refresh.</summary>
/// <param name="Envelope">Current Mentor analysis and deterministic delta.</param>
/// <param name="Snapshot">Optional compiler-validated snapshot used by the preflight.</param>
public sealed record MentorBuildPreflightCompletedEvent(
    MentorAnalysisEnvelope Envelope,
    AnalysisSnapshot? Snapshot = null);

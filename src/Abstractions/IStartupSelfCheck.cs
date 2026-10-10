namespace Junexis.Studio.Abstractions;

/// <summary>
/// Runs a set of startup checks and reports issues to Output/Diagnostics.
/// In a clean environment, no false positives should be produced.
/// </summary>
public interface IStartupSelfCheck
{
    /// <summary>
    /// Executes all registered startup checks.
    /// </summary>
    /// <returns>Aggregated result with per-check entries.</returns>
    StartupCheckResult Run();
}

/// <summary>
/// Aggregated result of startup self-check.
/// </summary>
/// <param name="Entries">Individual check entries.</param>
/// <param name="AllPassed">True if every entry passed.</param>
public sealed record StartupCheckResult(
    IReadOnlyList<StartupCheckEntry> Entries,
    bool AllPassed);

/// <summary>
/// Single check entry produced by a startup check step.
/// </summary>
/// <param name="Category">Category label (e.g. "Runtime", "Service", "Extension").</param>
/// <param name="Description">Human-readable description of the check.</param>
/// <param name="Passed">Whether the check passed.</param>
/// <param name="Fix">Optional actionable fix hint (null when passed).</param>
public sealed record StartupCheckEntry(
    string Category,
    string Description,
    bool Passed,
    string? Fix);

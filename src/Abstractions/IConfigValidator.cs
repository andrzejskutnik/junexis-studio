namespace Junexis.Studio.Abstractions;

/// <summary>
/// Validates workspace configuration (e.g., <c>junexis.workspace.json</c>) against known schema and constraints.
/// </summary>
public interface IConfigValidator
{
    /// <summary>
    /// Validates the given raw JSON configuration.
    /// </summary>
    /// <param name="json">Raw JSON string to validate.</param>
    /// <returns>The validation result, including any issues and auto-fix hints.</returns>
    ConfigValidationResult Validate(string json);
}

/// <summary>
/// Result of workspace configuration validation.
/// </summary>
/// <param name="IsValid">Whether the configuration passed all checks.</param>
/// <param name="Issues">List of detected issues (empty if valid).</param>
public sealed record ConfigValidationResult(bool IsValid, IReadOnlyList<ConfigIssue> Issues)
{
    /// <summary>Creates a valid result with no issues.</summary>
    public static ConfigValidationResult Valid() => new(true, []);
}

/// <summary>
/// Represents a single issue found during configuration validation.
/// </summary>
/// <param name="Path">JSON path or property name where the issue was found (e.g., "compiler.id").</param>
/// <param name="Message">Human-readable description of the issue.</param>
/// <param name="Severity">Severity of the issue.</param>
/// <param name="FixHint">Optional auto-fix suggestion (null if no fix available).</param>
public sealed record ConfigIssue(
    string Path,
    string Message,
    ConfigIssueSeverity Severity,
    string? FixHint = null);

/// <summary>
/// Severity level for configuration issues.
/// </summary>
public enum ConfigIssueSeverity
{
    /// <summary>Informational hint — not a problem, but could be improved.</summary>
    Info,
    /// <summary>Warning — valid but potentially problematic.</summary>
    Warning,
    /// <summary>Error — invalid, will cause problems.</summary>
    Error
}

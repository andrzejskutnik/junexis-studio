namespace Junexis.Studio.Abstractions;

/// <summary>
/// Contract for a privacy redaction pipeline.
/// Processes text to remove or mask sensitive data (secrets, tokens, paths, env vars)
/// before sending to external providers.
/// </summary>
public interface IPrivacyFilter
{
    /// <summary>
    /// Applies all registered redaction rules to the input text.
    /// </summary>
    /// <param name="input">The text to redact.</param>
    /// <returns>The redacted result.</returns>
    RedactionResult Redact(string input);
}

/// <summary>
/// Result of a privacy redaction operation.
/// </summary>
/// <param name="Output">The redacted text.</param>
/// <param name="RedactionCount">Number of redactions applied.</param>
/// <param name="RedactedPatterns">Labels of the patterns that matched (no secret content exposed).</param>
public sealed record RedactionResult(string Output, int RedactionCount, IReadOnlyList<string> RedactedPatterns);

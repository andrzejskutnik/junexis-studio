using System.Text.RegularExpressions;
using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Core;

/// <summary>
/// A single redaction rule consisting of a pattern and a label.
/// </summary>
/// <param name="Label">Human-readable label for logging (e.g. "JWT token", "API key").</param>
/// <param name="Pattern">Compiled regex pattern to match sensitive content.</param>
/// <param name="Replacement">Replacement text (default: "[REDACTED]").</param>
public sealed record RedactionRule(string Label, Regex Pattern, string Replacement = "[REDACTED]");

/// <summary>
/// Configurable privacy redaction pipeline.
/// Applies a sequence of <see cref="RedactionRule"/> instances to input text.
/// Logs redaction events to <see cref="IOutput"/> without exposing secret content.
/// </summary>
public sealed class PrivacyFilter : IPrivacyFilter
{
    private readonly List<RedactionRule> _rules = new();
    private readonly object _lock = new();
    private readonly IOutput? _output;

    /// <summary>Output channel for privacy filter messages.</summary>
    public const string OutputChannel = "Privacy";

    /// <summary>
    /// Creates a new <see cref="PrivacyFilter"/> with optional output logging.
    /// </summary>
    /// <param name="output">Optional output service.</param>
    public PrivacyFilter(IOutput? output = null)
    {
        _output = output;
    }

    /// <summary>
    /// Adds a redaction rule to the pipeline.
    /// </summary>
    /// <param name="rule">The rule to add.</param>
    /// <exception cref="ArgumentNullException">When <paramref name="rule"/> is null.</exception>
    public void AddRule(RedactionRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        lock (_lock) _rules.Add(rule);
    }

    /// <summary>
    /// Removes all rules with the specified label.
    /// </summary>
    /// <param name="label">The label to match (case-insensitive).</param>
    /// <returns>Number of rules removed.</returns>
    public int RemoveRules(string label)
    {
        ArgumentNullException.ThrowIfNull(label);
        lock (_lock) return _rules.RemoveAll(r => string.Equals(r.Label, label, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Gets a snapshot of current rule labels.
    /// </summary>
    /// <returns>List of rule labels.</returns>
    public IReadOnlyList<string> GetRuleLabels()
    {
        lock (_lock) return _rules.Select(r => r.Label).ToList();
    }

    /// <summary>
    /// Creates a <see cref="PrivacyFilter"/> pre-configured with common security patterns.
    /// </summary>
    /// <param name="output">Optional output service.</param>
    /// <returns>A filter with built-in rules for common secrets.</returns>
    public static PrivacyFilter CreateDefault(IOutput? output = null)
    {
        var filter = new PrivacyFilter(output);

        // JWT tokens
        filter.AddRule(new RedactionRule("JWT",
            new Regex(@"eyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}", RegexOptions.Compiled)));

        // Generic API keys (key=... or token=... or api_key=...)
        filter.AddRule(new RedactionRule("API key",
            new Regex(@"(?i)(?:api[_-]?key|token|secret|password|passwd|authorization)\s*[:=]\s*[""']?([A-Za-z0-9_\-\.]{8,})[""']?", RegexOptions.Compiled),
            "[REDACTED_CREDENTIAL]"));

        // Bearer tokens
        filter.AddRule(new RedactionRule("Bearer token",
            new Regex(@"Bearer\s+[A-Za-z0-9_\-\.]{10,}", RegexOptions.Compiled),
            "Bearer [REDACTED]"));

        // AWS-style keys
        filter.AddRule(new RedactionRule("AWS key",
            new Regex(@"(?:AKIA|ASIA)[A-Z0-9]{16}", RegexOptions.Compiled)));

        // Connection strings with password
        filter.AddRule(new RedactionRule("Connection string password",
            new Regex(@"(?i)(?:password|pwd)\s*=\s*[^;""'\s]{3,}", RegexOptions.Compiled),
            "Password=[REDACTED]"));

        // Environment variable patterns (e.g. %SECRET_VAR% or $SECRET_VAR)
        filter.AddRule(new RedactionRule("Env variable reference",
            new Regex(@"(?:%[A-Z_]{3,}%|\$\{?[A-Z_]{3,}\}?)", RegexOptions.Compiled),
            "[REDACTED_ENV]"));

        return filter;
    }

    /// <inheritdoc />
    public RedactionResult Redact(string input)
    {
        if (string.IsNullOrEmpty(input))
            return new RedactionResult(input ?? string.Empty, 0, Array.Empty<string>());

        RedactionRule[] rules;
        lock (_lock) rules = _rules.ToArray();

        var result = input;
        var totalCount = 0;
        var matchedLabels = new List<string>();

        foreach (var rule in rules)
        {
            var matches = rule.Pattern.Matches(result);
            if (matches.Count > 0)
            {
                totalCount += matches.Count;
                matchedLabels.Add(rule.Label);
                result = rule.Pattern.Replace(result, rule.Replacement);
            }
        }

        if (totalCount > 0)
        {
            _output?.Write(OutputChannel,
                $"Redacted {totalCount} occurrence(s) matching: {string.Join(", ", matchedLabels)}.",
                OutputSeverity.Warning);
        }

        return new RedactionResult(result, totalCount, matchedLabels);
    }
}

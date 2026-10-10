using Junexis.Studio.Abstractions;
using System.Text.Json;

namespace Junexis.Studio.Core;

/// <summary>
/// Validates <c>junexis.workspace.json</c> against known schema constraints.
/// Supports pluggable rules for extensible validation.
/// </summary>
public sealed class WorkspaceConfigValidator : IConfigValidator
{
    private readonly List<ConfigRule> _rules = new();
    private readonly IOutput? _output;

    /// <summary>Output channel name for validation messages.</summary>
    public const string OutputChannel = "Integrity";

    /// <summary>
    /// Creates a validator with optional output logging.
    /// </summary>
    /// <param name="output">Optional output service for logging.</param>
    public WorkspaceConfigValidator(IOutput? output = null)
    {
        _output = output;
    }

    /// <summary>
    /// Adds a pluggable validation rule.
    /// </summary>
    /// <param name="rule">The rule to add.</param>
    public void AddRule(ConfigRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        _rules.Add(rule);
    }

    /// <summary>
    /// Gets the current number of registered rules.
    /// </summary>
    public int RuleCount => _rules.Count;

    /// <inheritdoc />
    public ConfigValidationResult Validate(string json)
    {
        var issues = new List<ConfigIssue>();

        // Basic JSON validity
        if (string.IsNullOrWhiteSpace(json))
        {
            issues.Add(new ConfigIssue("$", "Configuration is empty.", ConfigIssueSeverity.Error,
                "Provide a valid JSON object, e.g., {}"));
            return new ConfigValidationResult(false, issues);
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            issues.Add(new ConfigIssue("$", $"Invalid JSON: {ex.Message}", ConfigIssueSeverity.Error,
                "Fix the JSON syntax."));
            _output?.Write(OutputChannel, $"Config validation failed: invalid JSON.", OutputSeverity.Error);
            return new ConfigValidationResult(false, issues);
        }

        using (doc)
        {
            // Root must be an object
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                issues.Add(new ConfigIssue("$", "Root element must be a JSON object.",
                    ConfigIssueSeverity.Error, "Wrap the configuration in { }."));
                return new ConfigValidationResult(false, issues);
            }

            // Apply registered rules
            foreach (var rule in _rules)
            {
                try
                {
                    var ruleIssues = rule.Check(doc.RootElement);
                    issues.AddRange(ruleIssues);
                }
                catch (Exception ex)
                {
                    issues.Add(new ConfigIssue(rule.Name, $"Rule failed: {ex.Message}",
                        ConfigIssueSeverity.Warning));
                }
            }
        }

        var isValid = !issues.Any(i => i.Severity == ConfigIssueSeverity.Error);

        if (!isValid)
        {
            _output?.Write(OutputChannel,
                $"Config validation: {issues.Count(i => i.Severity == ConfigIssueSeverity.Error)} error(s), " +
                $"{issues.Count(i => i.Severity == ConfigIssueSeverity.Warning)} warning(s).",
                OutputSeverity.Warning);
        }
        else if (issues.Count > 0)
        {
            _output?.Write(OutputChannel, $"Config validation: {issues.Count} hint(s)/warning(s).");
        }

        return new ConfigValidationResult(isValid, issues);
    }

    /// <summary>
    /// Creates a validator with default built-in rules for <c>junexis.workspace.json</c>.
    /// </summary>
    /// <param name="output">Optional output service.</param>
    /// <returns>A pre-configured validator.</returns>
    public static WorkspaceConfigValidator CreateDefault(IOutput? output = null)
    {
        var v = new WorkspaceConfigValidator(output);

        // Rule: "name" should be present and non-empty
        v.AddRule(new ConfigRule("name", root =>
        {
            var issues = new List<ConfigIssue>();
            if (!root.TryGetProperty("name", out var name))
            {
                issues.Add(new ConfigIssue("name", "Missing 'name' property.",
                    ConfigIssueSeverity.Warning, "Add \"name\": \"my-project\" to the config."));
            }
            else if (name.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(name.GetString()))
            {
                issues.Add(new ConfigIssue("name", "'name' must be a non-empty string.",
                    ConfigIssueSeverity.Error));
            }
            return issues;
        }));

        // Rule: "compiler" should be an object if present
        v.AddRule(new ConfigRule("compiler", root =>
        {
            var issues = new List<ConfigIssue>();
            if (root.TryGetProperty("compiler", out var compiler))
            {
                if (compiler.ValueKind != JsonValueKind.Object)
                {
                    issues.Add(new ConfigIssue("compiler", "'compiler' must be a JSON object.",
                        ConfigIssueSeverity.Error, "Change to: \"compiler\": { \"id\": \"...\" }"));
                }
                else if (!compiler.TryGetProperty("id", out var id) ||
                         id.ValueKind != JsonValueKind.String ||
                         string.IsNullOrWhiteSpace(id.GetString()))
                {
                    issues.Add(new ConfigIssue("compiler.id", "'compiler.id' is missing or empty.",
                        ConfigIssueSeverity.Warning, "Add \"id\": \"gpp\" inside the compiler section."));
                }
            }
            return issues;
        }));

        // Rule: "extensions" must be an array if present
        v.AddRule(new ConfigRule("extensions", root =>
        {
            var issues = new List<ConfigIssue>();
            if (root.TryGetProperty("extensions", out var ext) && ext.ValueKind != JsonValueKind.Array)
            {
                issues.Add(new ConfigIssue("extensions", "'extensions' must be a JSON array.",
                    ConfigIssueSeverity.Error, "Change to: \"extensions\": []"));
            }
            return issues;
        }));

        return v;
    }
}

/// <summary>
/// A named validation rule that checks a specific aspect of the configuration.
/// </summary>
public sealed class ConfigRule
{
    /// <summary>
    /// Gets the rule name (used for diagnostics).
    /// </summary>
    public string Name { get; }

    private readonly Func<JsonElement, IReadOnlyList<ConfigIssue>> _check;

    /// <summary>
    /// Creates a validation rule.
    /// </summary>
    /// <param name="name">Rule name.</param>
    /// <param name="check">Function that checks a JSON root element and returns issues.</param>
    public ConfigRule(string name, Func<JsonElement, IReadOnlyList<ConfigIssue>> check)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(check);
        Name = name;
        _check = check;
    }

    /// <summary>
    /// Executes the rule against the given root element.
    /// </summary>
    /// <param name="root">The root JSON element.</param>
    /// <returns>List of issues found.</returns>
    public IReadOnlyList<ConfigIssue> Check(JsonElement root) => _check(root);
}

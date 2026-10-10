using System.Globalization;
using System.Text.RegularExpressions;
using Junexis.Studio.Abstractions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Junexis.Studio.Core;

/// <summary>Loads and atomically applies the project-owned Mentor policy.</summary>
public sealed partial class MentorProjectPolicyService : IMentorProjectPolicyService
{
    /// <summary>Relative location of the project Mentor policy.</summary>
    public static readonly string RelativePath = Path.Combine(".junexis", "mentor.yml");

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    private readonly IWorkspace _workspace;
    private readonly IOutput _output;
    private readonly Func<DateOnly> _today;

    /// <summary>Creates the project policy service.</summary>
    /// <param name="workspace">Open workspace that owns the policy file.</param>
    /// <param name="output">Studio output used for policy validation errors.</param>
    public MentorProjectPolicyService(IWorkspace workspace, IOutput output)
        : this(workspace, output, () => DateOnly.FromDateTime(DateTime.UtcNow))
    {
    }

    internal MentorProjectPolicyService(
        IWorkspace workspace,
        IOutput output,
        Func<DateOnly> today)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _today = today ?? throw new ArgumentNullException(nameof(today));
        _output.RegisterChannel("Mentor");
    }

    /// <inheritdoc />
    public Task<MentorProjectPolicyEvaluation> ApplyAsync(
        MentorAnalysisResult result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        cancellationToken.ThrowIfCancellationRequested();
        var root = _workspace.RootPath;
        var policyPath = string.IsNullOrWhiteSpace(root)
            ? Path.GetFullPath(RelativePath)
            : Path.GetFullPath(Path.Combine(root, RelativePath));
        if (string.IsNullOrWhiteSpace(root) || !File.Exists(policyPath))
        {
            return Task.FromResult(new MentorProjectPolicyEvaluation(
                result,
                EmptyPolicy(),
                policyPath,
                false,
                true,
                [],
                []));
        }

        PolicyDocument? document;
        try
        {
            var yaml = _workspace.ReadFile(policyPath);
            document = Deserializer.Deserialize<PolicyDocument>(yaml);
        }
        catch (Exception ex)
        {
            return Task.FromResult(Invalid(
                result,
                policyPath,
                [$"Cannot parse policy: {ex.Message}"]));
        }

        var validationErrors = Validate(document);
        if (validationErrors.Count > 0)
            return Task.FromResult(Invalid(result, policyPath, validationErrors));

        var policy = ToPolicy(document!);
        var activeSuppressions = policy.Suppressions
            .Where(suppression => suppression.ExpiresOn is null
                || suppression.ExpiresOn.Value >= _today())
            .ToArray();
        var applied = new List<MentorAppliedSuppression>();
        var visible = new List<MentorFinding>();
        foreach (var finding in result.Findings)
        {
            var suppression = activeSuppressions.FirstOrDefault(candidate =>
                string.Equals(candidate.RuleCode, finding.Code, StringComparison.Ordinal)
                && string.Equals(candidate.SymbolId, finding.RelatedSymbolId, StringComparison.Ordinal));
            if (suppression is null)
                visible.Add(finding);
            else
                applied.Add(new MentorAppliedSuppression(suppression, finding));
        }

        var summary = result.Summary is null
            ? null
            : result.Summary with { FindingCount = visible.Count };
        var filteredResult = result with
        {
            Findings = visible,
            Summary = summary
        };
        return Task.FromResult(new MentorProjectPolicyEvaluation(
            filteredResult,
            policy,
            policyPath,
            true,
            true,
            applied,
            []));
    }

    private MentorProjectPolicyEvaluation Invalid(
        MentorAnalysisResult result,
        string path,
        IReadOnlyList<string> errors)
    {
        foreach (var error in errors)
            _output.Write("Mentor", $"Invalid project policy '{path}': {error}", OutputSeverity.Warning);
        return new MentorProjectPolicyEvaluation(
            result,
            EmptyPolicy(),
            path,
            true,
            false,
            [],
            errors);
    }

    private static IReadOnlyList<string> Validate(PolicyDocument? document)
    {
        var errors = new List<string>();
        if (document is null)
            return ["Policy document is empty."];
        if (!string.Equals(document.SchemaVersion, "1.0", StringComparison.Ordinal))
            errors.Add("schemaVersion must be exactly \"1.0\".");
        if (!string.IsNullOrWhiteSpace(document.Presence)
            && !Enum.TryParse<MentorPresenceMode>(document.Presence, true, out _))
        {
            errors.Add("presence must be quiet, balanced, or teaching.");
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var decision in document.Decisions ?? [])
        {
            ValidateRequired(decision.Id, "Decision id", errors);
            ValidateRequired(decision.Title, $"Decision '{decision.Id}' title", errors);
            ValidateRequired(decision.Rationale, $"Decision '{decision.Id}' rationale", errors);
            if (!string.IsNullOrWhiteSpace(decision.Id) && !ids.Add(decision.Id))
                errors.Add($"Duplicate policy id '{decision.Id}'.");
        }

        var matches = new HashSet<string>(StringComparer.Ordinal);
        foreach (var suppression in document.Suppressions ?? [])
        {
            ValidateRequired(suppression.Id, "Suppression id", errors);
            ValidateRequired(suppression.RuleCode, $"Suppression '{suppression.Id}' ruleCode", errors);
            ValidateRequired(suppression.SymbolId, $"Suppression '{suppression.Id}' symbolId", errors);
            ValidateRequired(suppression.Rationale, $"Suppression '{suppression.Id}' rationale", errors);
            if (!string.IsNullOrWhiteSpace(suppression.Id) && !ids.Add(suppression.Id))
                errors.Add($"Duplicate policy id '{suppression.Id}'.");
            if (!string.IsNullOrWhiteSpace(suppression.RuleCode)
                && !MentorRuleCode().IsMatch(suppression.RuleCode))
            {
                errors.Add($"Suppression '{suppression.Id}' ruleCode must match Mxxxx.");
            }
            if (!string.IsNullOrWhiteSpace(suppression.ExpiresOn)
                && !DateOnly.TryParseExact(
                    suppression.ExpiresOn,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out _))
            {
                errors.Add($"Suppression '{suppression.Id}' expiresOn must use yyyy-MM-dd.");
            }
            var match = $"{suppression.RuleCode}\u001f{suppression.SymbolId}";
            if (!string.IsNullOrWhiteSpace(suppression.RuleCode)
                && !string.IsNullOrWhiteSpace(suppression.SymbolId)
                && !matches.Add(match))
            {
                errors.Add(
                    $"Multiple suppressions target '{suppression.RuleCode}' and '{suppression.SymbolId}'.");
            }
        }
        return errors;
    }

    private static MentorProjectPolicy ToPolicy(PolicyDocument document) =>
        new(
            document.SchemaVersion!,
            string.IsNullOrWhiteSpace(document.Presence)
                ? null
                : Enum.Parse<MentorPresenceMode>(document.Presence, true),
            (document.Decisions ?? [])
                .Select(decision => new MentorProjectDecision(
                    decision.Id!,
                    decision.Title!,
                    decision.Rationale!,
                    decision.RelatedSymbolIds ?? []))
                .ToArray(),
            (document.Suppressions ?? [])
                .Select(suppression => new MentorProjectSuppression(
                    suppression.Id!,
                    suppression.RuleCode!,
                    suppression.SymbolId!,
                    suppression.Rationale!,
                    string.IsNullOrWhiteSpace(suppression.ExpiresOn)
                        ? null
                        : DateOnly.ParseExact(
                            suppression.ExpiresOn,
                            "yyyy-MM-dd",
                            CultureInfo.InvariantCulture)))
                .ToArray());

    private static void ValidateRequired(string? value, string field, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
            errors.Add($"{field} is required.");
    }

    private static MentorProjectPolicy EmptyPolicy() =>
        new("1.0", null, [], []);

    [GeneratedRegex("^M\\d{4}$", RegexOptions.CultureInvariant)]
    private static partial Regex MentorRuleCode();

    private sealed class PolicyDocument
    {
        public string? SchemaVersion { get; set; }
        public string? Presence { get; set; }
        public List<DecisionDocument>? Decisions { get; set; }
        public List<SuppressionDocument>? Suppressions { get; set; }
    }

    private sealed class DecisionDocument
    {
        public string? Id { get; set; }
        public string? Title { get; set; }
        public string? Rationale { get; set; }
        public List<string>? RelatedSymbolIds { get; set; }
    }

    private sealed class SuppressionDocument
    {
        public string? Id { get; set; }
        public string? RuleCode { get; set; }
        public string? SymbolId { get; set; }
        public string? Rationale { get; set; }
        public string? ExpiresOn { get; set; }
    }
}

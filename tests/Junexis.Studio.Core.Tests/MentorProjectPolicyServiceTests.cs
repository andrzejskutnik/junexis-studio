using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;

namespace Junexis.Studio.Core.Tests;

public sealed class MentorProjectPolicyServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "junexis-mentor-policy-" + Guid.NewGuid().ToString("N"));
    private readonly WorkspaceService _workspace;
    private readonly OutputService _output = new();

    public MentorProjectPolicyServiceTests()
    {
        Directory.CreateDirectory(_root);
        _workspace = new WorkspaceService();
        _workspace.OpenFolder(_root);
    }

    [Fact]
    public async Task MissingPolicy_LeavesAllFindingsVisible()
    {
        var result = Result(Finding("M1002", "pipeline:P"));

        var evaluation = await Service().ApplyAsync(result);

        Assert.False(evaluation.IsConfigured);
        Assert.True(evaluation.IsValid);
        Assert.Single(evaluation.Result.Findings);
        Assert.Empty(evaluation.AppliedSuppressions);
    }

    [Fact]
    public async Task ValidPolicy_AppliesOnlyExactSuppressionAndPublishesDecisions()
    {
        WritePolicy("""
            schemaVersion: "1.0"
            presence: teaching
            decisions:
              - id: ADR-001
                title: Keep telemetry isolated
                rationale: The operational boundary is intentionally separate.
                relatedSymbolIds:
                  - pipeline:P
            suppressions:
              - id: ACCEPT-001
                ruleCode: M1002
                symbolId: pipeline:P
                rationale: Narrative is delivered by the parent system.
                expiresOn: 2099-12-31
            """);
        var result = Result(
            Finding("M1002", "pipeline:P"),
            Finding("M1002", "pipeline:Other"),
            Finding("M1001", "task:A"));

        var evaluation = await Service().ApplyAsync(result);

        Assert.True(evaluation.IsConfigured);
        Assert.True(evaluation.IsValid);
        Assert.Equal(MentorPresenceMode.Teaching, evaluation.Policy.Presence);
        Assert.Single(evaluation.Policy.Decisions);
        Assert.Single(evaluation.AppliedSuppressions);
        Assert.Equal("pipeline:P", evaluation.AppliedSuppressions[0].Finding.RelatedSymbolId);
        Assert.Equal(2, evaluation.Result.Findings.Count);
        Assert.Equal(2, evaluation.Result.Summary?.FindingCount);
    }

    [Fact]
    public async Task ExpiredSuppression_RemainsConfiguredButDoesNotHideFinding()
    {
        WritePolicy("""
            schemaVersion: "1.0"
            suppressions:
              - id: OLD-001
                ruleCode: M1002
                symbolId: pipeline:P
                rationale: Historical acceptance.
                expiresOn: 2000-01-01
            """);

        var evaluation = await Service().ApplyAsync(Result(Finding("M1002", "pipeline:P")));

        Assert.True(evaluation.IsValid);
        Assert.Single(evaluation.Policy.Suppressions);
        Assert.Empty(evaluation.AppliedSuppressions);
        Assert.Single(evaluation.Result.Findings);
    }

    [Fact]
    public async Task InvalidPolicy_IsAtomicAndNeverHidesFinding()
    {
        WritePolicy("""
            schemaVersion: "1.0"
            suppressions:
              - id: ACCEPT-001
                ruleCode: M1002
                symbolId: pipeline:P
            """);

        var evaluation = await Service().ApplyAsync(Result(Finding("M1002", "pipeline:P")));

        Assert.False(evaluation.IsValid);
        Assert.Single(evaluation.Result.Findings);
        Assert.Empty(evaluation.AppliedSuppressions);
        Assert.Contains(evaluation.ValidationErrors, error =>
            error.Contains("rationale is required", StringComparison.Ordinal));
        Assert.Contains(_output.GetSnapshot(), entry =>
            entry.Severity == OutputSeverity.Warning);
    }

    [Fact]
    public async Task UnknownYamlField_InvalidatesPolicyInsteadOfBeingIgnored()
    {
        WritePolicy("""
            schemaVersion: "1.0"
            suppressEverything: true
            """);

        var evaluation = await Service().ApplyAsync(Result(Finding("M1002", "pipeline:P")));

        Assert.False(evaluation.IsValid);
        Assert.Single(evaluation.Result.Findings);
        Assert.Contains(evaluation.ValidationErrors, error =>
            error.Contains("Cannot parse policy", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RuleWideSuppressionWithoutStableSymbol_IsRejected()
    {
        WritePolicy("""
            schemaVersion: "1.0"
            suppressions:
              - id: TOO-WIDE
                ruleCode: M1002
                rationale: Hide every instance.
            """);

        var evaluation = await Service().ApplyAsync(Result(Finding("M1002", "pipeline:P")));

        Assert.False(evaluation.IsValid);
        Assert.Single(evaluation.Result.Findings);
        Assert.Contains(evaluation.ValidationErrors, error =>
            error.Contains("symbolId is required", StringComparison.Ordinal));
    }

    public void Dispose()
    {
        _workspace.Dispose();
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private MentorProjectPolicyService Service() => new(_workspace, _output);

    private void WritePolicy(string yaml)
    {
        var directory = Path.Combine(_root, ".junexis");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "mentor.yml"), yaml);
    }

    private static MentorFinding Finding(string code, string symbolId) =>
        new(code, "warning", $"{code} finding.", symbolId, null);

    private static MentorAnalysisResult Result(params MentorFinding[] findings) =>
        new(
            "build-1",
            "analysis.json",
            findings,
            new MentorAnalysisSummary(2, 1, 0, 1, findings.Length, 0));
}

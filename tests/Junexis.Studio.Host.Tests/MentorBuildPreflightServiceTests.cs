using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;
using Junexis.Studio.Host;

namespace Junexis.Studio.Host.Tests;

public sealed class MentorBuildPreflightServiceTests
{
    [Fact]
    public async Task RunAsync_CurrentResult_PublishesEnvelopeAndCompactDelta()
    {
        var events = new EventBus();
        var output = new OutputService();
        MentorBuildPreflightCompletedEvent? published = null;
        events.Subscribe<MentorBuildPreflightCompletedEvent>(value => published = value);
        var finding = new MentorFinding(
            "M1001", "warning", "Unbounded fan-out.", "task:Ingress", null);
        var snapshot = new AnalysisSnapshot(
            "build-1",
            Path.GetFullPath("main.jnx"),
            Path.GetFullPath("analysis.json"),
            [],
            [],
            new AnalysisTopology(false, 0, [], new Dictionary<string, int>(),
                new Dictionary<string, int>(), new Dictionary<string, int>()));
        var service = new MentorBuildPreflightService(
            new StubMentor(new MentorAnalysisResult(
                "build-1", "analysis.json", [finding])),
            events,
            output,
            analysis: new StubAnalysis(snapshot));

        var outcome = await service.RunAsync(new CompilerPreflightResult(
            true, [], "junexis-lang", "build-1", "analysis.json"));

        Assert.True(outcome.Available);
        Assert.NotNull(published);
        Assert.Same(snapshot, published!.Snapshot);
        Assert.Equal(1, published.Envelope.Delta.AddedCount);
        Assert.Contains(output.GetSnapshot(), entry =>
            entry.Channel == "Mentor" && entry.Message.Contains("+1 new", StringComparison.Ordinal));
        Assert.Contains(output.GetSnapshot(), entry =>
            entry.Channel == "Mentor" && entry.Message.Contains("M1001", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_MentorFailure_ReturnsUnavailableWithoutThrowing()
    {
        var output = new OutputService();
        var service = new MentorBuildPreflightService(
            new ThrowingMentor(),
            new EventBus(),
            output);

        var outcome = await service.RunAsync(new CompilerPreflightResult(
            true, [], "junexis-lang", "build-1", "analysis.json"));

        Assert.False(outcome.Available);
        Assert.Null(outcome.Envelope);
        Assert.Contains("build continues", outcome.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(output.GetSnapshot(), entry =>
            entry.Channel == "Mentor" && entry.Severity == OutputSeverity.Warning);
    }

    [Fact]
    public async Task RunAsync_AppliesProjectPolicyBeforeDeltaAndPublication()
    {
        var events = new EventBus();
        MentorBuildPreflightCompletedEvent? published = null;
        events.Subscribe<MentorBuildPreflightCompletedEvent>(value => published = value);
        var finding = new MentorFinding(
            "M1002", "warning", "Pipeline has no Narrative.", "pipeline:P", null);
        var raw = new MentorAnalysisResult("build-1", "analysis.json", [finding]);
        var suppression = new MentorProjectSuppression(
            "ACCEPT-001", "M1002", "pipeline:P", "Owned by parent system.", null);
        var evaluation = new MentorProjectPolicyEvaluation(
            raw with { Findings = [] },
            new MentorProjectPolicy("1.0", MentorPresenceMode.Quiet, [], [suppression]),
            Path.GetFullPath(".junexis/mentor.yml"),
            true,
            true,
            [new MentorAppliedSuppression(suppression, finding)],
            []);
        var output = new OutputService();
        var service = new MentorBuildPreflightService(
            new StubMentor(raw),
            events,
            output,
            projectPolicy: new StubProjectPolicy(evaluation));

        var outcome = await service.RunAsync(new CompilerPreflightResult(
            true, [], "junexis-lang", "build-1", "analysis.json"));

        Assert.True(outcome.Available);
        Assert.NotNull(published);
        Assert.Empty(published!.Envelope.Result.Findings);
        Assert.Empty(published.Envelope.Delta.Changes);
        Assert.Same(evaluation, published.Envelope.ProjectPolicy);
        Assert.DoesNotContain(output.GetSnapshot(), entry => entry.Channel == "Mentor");
    }

    private sealed class StubMentor(MentorAnalysisResult result) : IMentorAnalysisService
    {
        public Task<MentorAnalysisResult> AnalyzeAsync(
            string snapshotPath,
            CancellationToken cancellationToken = default) => Task.FromResult(result);
    }

    private sealed class ThrowingMentor : IMentorAnalysisService
    {
        public Task<MentorAnalysisResult> AnalyzeAsync(
            string snapshotPath,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("not installed");
    }

    private sealed class StubAnalysis(AnalysisSnapshot snapshot) : IAnalysisSnapshotService
    {
        public Task<AnalysisSnapshot> LoadAsync(
            string snapshotPath,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(snapshot);
    }

    private sealed class StubProjectPolicy(MentorProjectPolicyEvaluation evaluation)
        : IMentorProjectPolicyService
    {
        public Task<MentorProjectPolicyEvaluation> ApplyAsync(
            MentorAnalysisResult result,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(evaluation);
    }
}

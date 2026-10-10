using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;
using Junexis.Studio.Host;
using Xunit;

namespace Junexis.Studio.Host.Tests;

public sealed class ProblemsPanelViewModelTests
{
    [Fact]
    [Trait("Issue", "22")]
    public void Diagnostics_RefreshesOnDiagnosticsChanged()
    {
        var diagnostics = new DiagnosticService();
        var bus = new EventBus();

        using var sut = new ProblemsPanelViewModel(diagnostics, bus);

        Assert.Empty(sut.Diagnostics);

        diagnostics.Add(new Diagnostic(DiagnosticSeverity.Error, "E1", filePath: "c:/x.cpp", line: 1, column: 2));

        Assert.Single(sut.Diagnostics);
        Assert.Equal("E1", sut.Diagnostics[0].Message);
    }

    [Fact]
    [Trait("Issue", "22")]
    public void NavigateTo_PublishesNavigateToLocationRequested()
    {
        var diagnostics = new DiagnosticService();
        var bus = new EventBus();

        NavigateToLocationRequested? published = null;
        bus.Subscribe<NavigateToLocationRequested>(e => published = e);

        using var sut = new ProblemsPanelViewModel(diagnostics, bus);

        var d = new Diagnostic(DiagnosticSeverity.Warning, "W1", filePath: "c:/y.cpp", line: 12, column: 3);
        sut.NavigateTo(d);

        Assert.NotNull(published);
        Assert.Equal("c:/y.cpp", published!.Path);
        Assert.Equal(12, published.Line);
        Assert.Equal(3, published.Column);
    }

    [Fact]
    public void CurrentBuildEvidence_ShowsSeparateMentorContextAndOpensFullReport()
    {
        var diagnostics = new DiagnosticService();
        var bus = new EventBus();
        var source = Path.GetFullPath("src/main.jnx");
        var snapshot = new AnalysisSnapshot(
            "build-1",
            source,
            "analysis.json",
            [
                new AnalysisSymbol(
                    "task:Ingress",
                    "task",
                    "Ingress",
                    new AnalysisSourceLocation(source, 12, 1),
                    ["server"],
                    null)
            ],
            [],
            new AnalysisTopology(false, 0, [], new Dictionary<string, int>(),
                new Dictionary<string, int>(), new Dictionary<string, int>()));
        using var sut = new ProblemsPanelViewModel(
            diagnostics,
            bus,
            new StubAnalysisService(snapshot));
        var finding = new MentorFinding(
            "M1015",
            "warning",
            "Missing isolation.",
            "task:Ingress",
            null,
            "Failure can cascade.",
            "Add a supervisor.");
        bus.Publish(new MentorBuildPreflightCompletedEvent(new MentorAnalysisEnvelope(
            new MentorAnalysisResult("build-1", "analysis.json", [finding]),
            DateTimeOffset.UtcNow,
            MentorAnalysisFreshness.Current,
            new MentorFindingDelta([]))));
        bus.Publish(new AnalysisSnapshotUpdatedEvent(
            "junexis-lang", "build-1", "analysis.json"));
        var diagnostic = new Diagnostic(
            DiagnosticSeverity.Error,
            "Compiler error.",
            source,
            12,
            3,
            "junexis-lang",
            "E5001");
        bus.Publish(new DiagnosticsUpdatedEvent(
            "junexis-lang", [diagnostic], "build-1"));
        diagnostics.Add(diagnostic);

        var item = Assert.Single(sut.Items);
        Assert.True(item.HasMentorContext);
        Assert.Equal("Compiler error.", item.Diagnostic.Message);
        Assert.Contains("Mentor M1015", item.MentorLabel, StringComparison.Ordinal);
        Assert.Equal("Next step: Add a supervisor.", item.MentorNextStep);
        sut.SelectedItem = item;
        Assert.True(sut.HasSelectedMentorContext);
        Assert.Contains("ExactDeclaration", sut.SelectedMentorEvidence, StringComparison.Ordinal);

        ActivatePanelRequested? activation = null;
        bus.Subscribe<ActivatePanelRequested>(value => activation = value);
        sut.OpenMentorReport();
        Assert.Equal("junexis.analysis.explorer", activation!.PanelId);
    }

    [Fact]
    public void QuietPresence_KeepsContextOutOfProblems()
    {
        var context = new MentorDiagnosticContext(
            "1.0",
            "build-1",
            "E5001",
            "task:Ingress",
            MentorDiagnosticMappingKind.ExactDeclaration,
            [new MentorFinding("M1015", "warning", "Risk.", "task:Ingress", null)],
            "Mentor M1015",
            "Review the task.");
        var item = new ProblemItemViewModel(
            new Diagnostic(DiagnosticSeverity.Error, "Compiler error."),
            context,
            MentorPresenceMode.Quiet);

        Assert.False(item.HasMentorContext);
    }

    private sealed class StubAnalysisService(AnalysisSnapshot snapshot) : IAnalysisSnapshotService
    {
        public Task<AnalysisSnapshot> LoadAsync(
            string snapshotPath,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(snapshot);
    }
}

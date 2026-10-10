using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;
using Junexis.Studio.Host;

namespace Junexis.Studio.Host.Tests;

public sealed class AnalysisExplorerViewModelTests
{
    [Fact]
    public async Task LoadAsync_ShowsSymbolsRelationsAndTopology()
    {
        var snapshot = CreateSnapshot();
        var viewModel = new AnalysisExplorerViewModel(
            new StubAnalysisService(snapshot),
            new EventBus(),
            new OutputService());

        await viewModel.LoadAsync("analysis.json");

        Assert.Equal(3, viewModel.Symbols.Count);
        Assert.Single(viewModel.Relations);
        Assert.Contains("max depth: 1", viewModel.TopologyText, StringComparison.Ordinal);
        Assert.Contains("build-1", viewModel.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_ShowsConformanceStatusAndNonBlockingGaps()
    {
        var conformance = new MentorConformanceOpinion(
            "partial", "S3", 6, 5, 1,
            [new MentorConformanceGap("boundary.open", "S3", "S4")],
            Blocking: false,
            "One non-blocking gap remains.",
            Bound: true,
            BuildId: new string('a', 64),
            BundleId: new string('b', 64),
            BindingSha256: new string('c', 64));
        var viewModel = new AnalysisExplorerViewModel(
            new StubAnalysisService(CreateSnapshot()),
            new EventBus(),
            new OutputService(),
            new StubMentorService(new MentorAnalysisResult(
                "build-1", "analysis.json", [], Conformance: conformance)));

        await viewModel.LoadAsync("analysis.json");

        Assert.Contains("PARTIAL", viewModel.ConformanceStatusText, StringComparison.Ordinal);
        Assert.Contains("non-blocking", viewModel.ConformanceStatusText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("BOUND build aaaaaaaaaaaa / bundle bbbbbbbbbbbb", viewModel.ConformanceStatusText, StringComparison.Ordinal);
        Assert.Equal("boundary.open", Assert.Single(viewModel.ConformanceGaps).Id);
        Assert.Empty(viewModel.Findings);
    }

    [Fact]
    public async Task NavigateToRelation_UsesRelationSourceAndPublishesEditorRequest()
    {
        var bus = new EventBus();
        NavigateToLocationRequested? request = null;
        bus.Subscribe<NavigateToLocationRequested>(value => request = value);
        var viewModel = new AnalysisExplorerViewModel(
            new StubAnalysisService(CreateSnapshot()),
            bus,
            new OutputService());
        await viewModel.LoadAsync("analysis.json");

        viewModel.NavigateTo(viewModel.Relations[0]);

        Assert.NotNull(request);
        Assert.Equal(Path.GetFullPath("src/main.jnx"), request!.Path);
        Assert.Equal(12, request.Line);
        Assert.Equal(5, request.Column);
    }

    [Fact]
    public async Task LoadAndNavigate_MentorFindingUsesSharedSourceNavigation()
    {
        var bus = new EventBus();
        NavigateToLocationRequested? request = null;
        bus.Subscribe<NavigateToLocationRequested>(value => request = value);
        var finding = new MentorFinding(
            "M1001",
            "warning",
            "Task A is disconnected.",
            "task:A",
            null);
        var viewModel = new AnalysisExplorerViewModel(
            new StubAnalysisService(CreateSnapshot()),
            bus,
            new OutputService(),
            new StubMentorService(new MentorAnalysisResult("build-1", "analysis.json", [finding])));

        await viewModel.LoadAsync("analysis.json");
        viewModel.NavigateTo(Assert.Single(viewModel.Findings));

        Assert.Contains("1 deterministic finding", viewModel.MentorStatusText, StringComparison.Ordinal);
        Assert.NotNull(request);
        Assert.Equal(Path.GetFullPath("src/main.jnx"), request!.Path);
        Assert.Equal(1, request.Line);
    }

    [Fact]
    public async Task LoadPreflightAsync_ReusesMentorResultWithoutSecondProcessCall()
    {
        var mentor = new CountingMentor();
        var finding = Finding("M1001", "Preflight finding.", "task:A");
        var envelope = new MentorAnalysisEnvelope(
            new MentorAnalysisResult("build-1", "analysis.json", [finding]),
            DateTimeOffset.UtcNow,
            MentorAnalysisFreshness.Current,
            new MentorFindingDelta([
                new MentorFindingChange(MentorFindingChangeKind.Added, finding, null)
            ]));
        var viewModel = new AnalysisExplorerViewModel(
            new StubAnalysisService(CreateSnapshot()),
            new EventBus(),
            new OutputService(),
            mentor);

        await viewModel.LoadPreflightAsync(envelope);
        await viewModel.LoadAsync("analysis.json");

        Assert.Equal(0, mentor.CallCount);
        Assert.Equal("M1001", Assert.Single(viewModel.Findings).Code);
        Assert.Equal(1, viewModel.MentorEnvelope!.Delta.AddedCount);
    }

    [Fact]
    public async Task LoadAsync_CurrentMentorResultPublishesFreshEnvelope()
    {
        var bus = new EventBus();
        MentorAnalysisCompletedEvent? completed = null;
        bus.Subscribe<MentorAnalysisCompletedEvent>(value => completed = value);
        var finding = Finding("M1001", "Original risk.", "task:A");
        var viewModel = new AnalysisExplorerViewModel(
            new StubAnalysisService(CreateSnapshot()),
            bus,
            new OutputService(),
            new StubMentorService(new MentorAnalysisResult(
                "build-1",
                "analysis.json",
                [finding],
                SchemaVersion: "1.1")));

        await viewModel.LoadAsync("analysis.json");

        Assert.NotNull(viewModel.MentorEnvelope);
        Assert.Equal(MentorAnalysisFreshness.Current, viewModel.MentorEnvelope!.Freshness);
        Assert.Equal("1.1", viewModel.MentorEnvelope.Result.SchemaVersion);
        Assert.Equal(1, viewModel.MentorEnvelope.Delta.AddedCount);
        Assert.Contains("Current", viewModel.MentorFreshnessText, StringComparison.Ordinal);
        Assert.Contains("+1 new", viewModel.MentorDeltaText, StringComparison.Ordinal);
        Assert.NotNull(completed);
        Assert.Equal("build-1", completed!.BuildId);
    }

    [Fact]
    public async Task LoadAsync_SecondCurrentResultComputesFindingDelta()
    {
        var first = new MentorAnalysisResult(
            "build-1",
            "analysis.json",
            [
                Finding("M1001", "Original risk.", "task:A"),
                Finding("M1002", "Resolved risk.", "task:B")
            ]);
        var second = new MentorAnalysisResult(
            "build-1",
            "analysis.json",
            [
                Finding("M1001", "Updated risk.", "task:A"),
                Finding("M1003", "New risk.", "pipeline:P")
            ]);
        var viewModel = new AnalysisExplorerViewModel(
            new StubAnalysisService(CreateSnapshot()),
            new EventBus(),
            new OutputService(),
            new SequenceMentorService(first, second));

        await viewModel.LoadAsync("analysis.json");
        await viewModel.LoadAsync("analysis.json");

        var delta = Assert.IsType<MentorFindingDelta>(viewModel.MentorEnvelope?.Delta);
        Assert.Equal(1, delta.AddedCount);
        Assert.Equal(1, delta.ChangedCount);
        Assert.Equal(1, delta.ResolvedCount);
        Assert.Equal(0, delta.UnchangedCount);
        Assert.Contains("~1 changed", viewModel.MentorDeltaText, StringComparison.Ordinal);
        Assert.Contains("✓1 resolved", viewModel.MentorDeltaText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_StaleMentorResultIsVisibleButNotPublishedAsCurrent()
    {
        var bus = new EventBus();
        var completionCount = 0;
        bus.Subscribe<MentorAnalysisCompletedEvent>(_ => completionCount++);
        var viewModel = new AnalysisExplorerViewModel(
            new StubAnalysisService(CreateSnapshot()),
            bus,
            new OutputService(),
            new StubMentorService(new MentorAnalysisResult(
                "older-build",
                "analysis.json",
                [Finding("M1001", "Old risk.", "task:A")])));

        await viewModel.LoadAsync("analysis.json");

        Assert.Equal(MentorAnalysisFreshness.Stale, viewModel.MentorEnvelope?.Freshness);
        Assert.Empty(viewModel.MentorEnvelope!.Delta.Changes);
        Assert.Contains("Stale", viewModel.MentorFreshnessText, StringComparison.Ordinal);
        Assert.Contains("non-current", viewModel.MentorDeltaText, StringComparison.Ordinal);
        Assert.Equal(0, completionCount);
    }

    [Fact]
    public async Task LoadAsync_MissingMentorBuildIdentityIsUnknown()
    {
        var viewModel = new AnalysisExplorerViewModel(
            new StubAnalysisService(CreateSnapshot()),
            new EventBus(),
            new OutputService(),
            new StubMentorService(new MentorAnalysisResult(
                string.Empty,
                "analysis.json",
                [Finding("M1001", "Unidentified risk.", "task:A")])));

        await viewModel.LoadAsync("analysis.json");

        Assert.Equal(MentorAnalysisFreshness.Unknown, viewModel.MentorEnvelope?.Freshness);
        Assert.Contains("Unknown", viewModel.MentorFreshnessText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_StaleResultDoesNotReplaceCurrentDeltaBaseline()
    {
        var current = new MentorAnalysisResult(
            "build-1",
            "analysis.json",
            [Finding("M1001", "Stable risk.", "task:A")]);
        var stale = new MentorAnalysisResult(
            "older-build",
            "analysis.json",
            [Finding("M9000", "Historical risk.", "task:B")]);
        var viewModel = new AnalysisExplorerViewModel(
            new StubAnalysisService(CreateSnapshot()),
            new EventBus(),
            new OutputService(),
            new SequenceMentorService(current, stale, current));

        await viewModel.LoadAsync("analysis.json");
        await viewModel.LoadAsync("analysis.json");
        await viewModel.LoadAsync("analysis.json");

        Assert.Equal(MentorAnalysisFreshness.Current, viewModel.MentorEnvelope?.Freshness);
        Assert.Equal(1, viewModel.MentorEnvelope!.Delta.UnchangedCount);
        Assert.Equal(0, viewModel.MentorEnvelope.Delta.AddedCount);
        Assert.Equal(0, viewModel.MentorEnvelope.Delta.ResolvedCount);
    }

    [Fact]
    public async Task LoadAsync_UsesConfiguredMentorPresence()
    {
        var preferences = new StubPreferencesService();
        preferences.Current.Mentor.Presence = MentorPresenceMode.Quiet;
        var viewModel = new AnalysisExplorerViewModel(
            new StubAnalysisService(CreateSnapshot()),
            new EventBus(),
            new OutputService(),
            new StubMentorService(new MentorAnalysisResult("build-1", "analysis.json", [])),
            preferences);

        await viewModel.LoadAsync("analysis.json");

        Assert.Equal(MentorPresenceMode.Quiet, viewModel.MentorPresence);
        Assert.Equal("Mentor · Quiet", viewModel.MentorPresenceText);
    }

    [Fact]
    public async Task M1002_RequiresPreviewApplyCompilerCheckAndExplicitKeep()
    {
        var finding = Finding("M1002", "Pipeline has no narrative.", "pipeline:P");
        var refactorings = new StubRefactoringService();
        var viewModel = new AnalysisExplorerViewModel(
            new StubAnalysisService(CreateSnapshot()),
            new EventBus(),
            new OutputService(),
            new StubMentorService(new MentorAnalysisResult("build-1", "analysis.json", [finding])),
            refactorings: refactorings);

        await viewModel.LoadAsync("analysis.json");
        viewModel.SelectedFinding = Assert.Single(viewModel.Findings);

        Assert.True(viewModel.CanPreviewRefactoring);
        Assert.False(viewModel.CanApplyRefactoring);

        viewModel.PreviewSelectedRefactoring();
        Assert.True(viewModel.CanApplyRefactoring);
        Assert.True(viewModel.HasRefactoringPreview);
        Assert.Contains("applies_to", viewModel.RefactoringPatch, StringComparison.Ordinal);
        Assert.Contains("Assumption:", viewModel.RefactoringEvidenceText, StringComparison.Ordinal);

        await viewModel.ApplyRefactoringAsync();
        Assert.True(viewModel.CanValidateRefactoring);
        Assert.False(viewModel.CanKeepRefactoring);

        await viewModel.ValidateRefactoringAsync();
        Assert.True(viewModel.CanKeepRefactoring);
        Assert.True(viewModel.CanRevertRefactoring);

        viewModel.KeepRefactoring();
        Assert.False(viewModel.CanKeepRefactoring);
        Assert.False(viewModel.CanRevertRefactoring);
        Assert.Contains("No VCS", viewModel.RefactoringStatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task M1012_IsOfferedOnlyWhenSnapshotHasExactlyOnePipeline()
    {
        var finding = Finding("M1012", "Narrative is not connected.", "narrative:N");
        var snapshot = CreateSnapshot();
        var viewModel = new AnalysisExplorerViewModel(
            new StubAnalysisService(snapshot),
            new EventBus(),
            new OutputService(),
            new StubMentorService(new MentorAnalysisResult("build-1", "analysis.json", [finding])),
            refactorings: new StubRefactoringService());

        await viewModel.LoadAsync("analysis.json");
        viewModel.SelectedFinding = Assert.Single(viewModel.Findings);

        Assert.True(viewModel.CanPreviewRefactoring);

        var ambiguous = snapshot with
        {
            Symbols =
            [
                .. snapshot.Symbols,
                new AnalysisSymbol(
                    "pipeline:Other",
                    "pipeline",
                    "Other",
                    new AnalysisSourceLocation(Path.GetFullPath("src/main.jnx"), 30, 1),
                    [],
                    null)
            ]
        };
        var ambiguousViewModel = new AnalysisExplorerViewModel(
            new StubAnalysisService(ambiguous),
            new EventBus(),
            new OutputService(),
            new StubMentorService(new MentorAnalysisResult("build-1", "analysis.json", [finding])),
            refactorings: new StubRefactoringService());

        await ambiguousViewModel.LoadAsync("analysis.json");
        ambiguousViewModel.SelectedFinding = Assert.Single(ambiguousViewModel.Findings);

        Assert.False(ambiguousViewModel.CanPreviewRefactoring);
    }

    [Fact]
    public async Task ProjectPolicy_ShowsDecisionAndAppliedSuppressionWithoutFinding()
    {
        var finding = Finding("M1002", "Pipeline has no narrative.", "pipeline:P");
        var result = new MentorAnalysisResult("build-1", "analysis.json", [finding]);
        var suppression = new MentorProjectSuppression(
            "ACCEPT-001",
            "M1002",
            "pipeline:P",
            "Covered by the parent Narrative.",
            null);
        var evaluation = new MentorProjectPolicyEvaluation(
            result with { Findings = [] },
            new MentorProjectPolicy(
                "1.0",
                MentorPresenceMode.Quiet,
                [
                    new MentorProjectDecision(
                        "ADR-001",
                        "Keep parent Narrative",
                        "The boundary is owned by the parent system.",
                        ["pipeline:P"])
                ],
                [suppression]),
            Path.GetFullPath(".junexis/mentor.yml"),
            true,
            true,
            [new MentorAppliedSuppression(suppression, finding)],
            []);
        var viewModel = new AnalysisExplorerViewModel(
            new StubAnalysisService(CreateSnapshot()),
            new EventBus(),
            new OutputService(),
            new StubMentorService(result),
            projectPolicy: new StubProjectPolicyService(evaluation));

        await viewModel.LoadAsync("analysis.json");

        Assert.Empty(viewModel.Findings);
        Assert.Single(viewModel.ProjectDecisions);
        Assert.Single(viewModel.ProjectSuppressions);
        Assert.Single(viewModel.AppliedSuppressions);
        Assert.Equal(MentorPresenceMode.Quiet, viewModel.MentorPresence);
        Assert.Contains("project", viewModel.MentorPresenceText, StringComparison.Ordinal);
        Assert.Contains("1 applied", viewModel.ProjectPolicyStatusText, StringComparison.Ordinal);
    }

    private static MentorFinding Finding(string code, string message, string symbolId) =>
        new(code, "warning", message, symbolId, null, "Evidence-backed explanation.", "Review the system boundary.");

    private static AnalysisSnapshot CreateSnapshot() => new(
        "build-1",
        Path.GetFullPath("src/main.jnx"),
        Path.GetFullPath("analysis.json"),
        [
            new AnalysisSymbol("task:A", "task", "A", new AnalysisSourceLocation(Path.GetFullPath("src/main.jnx"), 1, 1), ["server"], "chan<Text>"),
            new AnalysisSymbol("task:B", "task", "B", new AnalysisSourceLocation(Path.GetFullPath("src/main.jnx"), 20, 1), ["report"], null),
            new AnalysisSymbol("pipeline:P", "pipeline", "P", new AnalysisSourceLocation(Path.GetFullPath("src/main.jnx"), 10, 1), [], null)
        ],
        [
            new AnalysisRelation(
                "communication:P:A->B", "communication", "chan", "pipeline:P",
                "task:A", null, "task:B", null,
                new AnalysisSourceLocation(Path.GetFullPath("src/main.jnx"), 12, 5))
        ],
        new AnalysisTopology(
            false,
            1,
            [["task:B"], ["task:A"]],
            new Dictionary<string, int> { ["task:A"] = 0, ["task:B"] = 1 },
            new Dictionary<string, int> { ["task:A"] = 1, ["task:B"] = 0 },
            new Dictionary<string, int> { ["task:A"] = 0, ["task:B"] = 1 }));

    private sealed class StubAnalysisService(AnalysisSnapshot snapshot) : IAnalysisSnapshotService
    {
        public Task<AnalysisSnapshot> LoadAsync(string snapshotPath, CancellationToken cancellationToken = default) =>
            Task.FromResult(snapshot);
    }

    private sealed class StubMentorService(MentorAnalysisResult result) : IMentorAnalysisService
    {
        public Task<MentorAnalysisResult> AnalyzeAsync(string snapshotPath, CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }

    private sealed class CountingMentor : IMentorAnalysisService
    {
        public int CallCount { get; private set; }

        public Task<MentorAnalysisResult> AnalyzeAsync(
            string snapshotPath,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(new MentorAnalysisResult("build-1", snapshotPath, []));
        }
    }

    private sealed class SequenceMentorService(params MentorAnalysisResult[] results) : IMentorAnalysisService
    {
        private int _index;

        public Task<MentorAnalysisResult> AnalyzeAsync(
            string snapshotPath,
            CancellationToken cancellationToken = default)
        {
            var index = Interlocked.Increment(ref _index) - 1;
            return Task.FromResult(results[Math.Min(index, results.Length - 1)]);
        }
    }

    private sealed class StubPreferencesService : IPreferencesService
    {
        public JunexisPreferences Current { get; } = new();
        public string GlobalFilePath => string.Empty;
        public string? WorkspaceFilePath => null;
        public void Reload() { }
        public void SaveGlobal() { }
        public void SaveWorkspace() { }
    }

    private sealed class StubRefactoringService : IMentorRefactoringService
    {
        public MentorRefactoringProposal CreateProposal(AnalysisSnapshot snapshot, MentorFinding finding) =>
            new(
                "mentor:M1002:pipeline:P",
                "M1002",
                "Add Narrative boundary",
                snapshot.SourceFile,
                "pipeline:P",
                "pipeline P {}",
                "pipeline P {}\nnarrative \"P intent\" { applies_to { P } }",
                "@@\n+narrative \"P intent\" { applies_to { P } }",
                ["Complete the intent."],
                "Pipeline becomes covered.");

        public Task<MentorRefactoringSession> ApplyAsync(
            MentorRefactoringProposal proposal,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new MentorRefactoringSession(
                proposal,
                MentorRefactoringState.Applied,
                "Applied.",
                []));

        public Task<MentorRefactoringSession> ValidateAsync(
            MentorRefactoringSession session,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(session with
            {
                State = MentorRefactoringState.Validated,
                Message = "Validated."
            });

        public MentorRefactoringSession Keep(MentorRefactoringSession session) =>
            session with
            {
                State = MentorRefactoringState.Kept,
                Message = "No VCS staging or commit was performed."
            };

        public Task<MentorRefactoringSession> RevertAsync(
            MentorRefactoringSession session,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(session with
            {
                State = MentorRefactoringState.Reverted,
                Message = "Reverted."
            });
    }

    private sealed class StubProjectPolicyService(MentorProjectPolicyEvaluation evaluation)
        : IMentorProjectPolicyService
    {
        public Task<MentorProjectPolicyEvaluation> ApplyAsync(
            MentorAnalysisResult result,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(evaluation);
    }
}

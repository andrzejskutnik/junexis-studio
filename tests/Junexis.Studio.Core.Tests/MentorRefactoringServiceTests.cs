using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;
using System.Text.Json;

namespace Junexis.Studio.Core.Tests;

public sealed class MentorRefactoringServiceTests
{
    [Fact]
    public void M1002_ProposalMatchesSharedClientContract()
    {
        using var fixture = new Fixture(preflightSuccess: true);
        using var contract = JsonDocument.Parse(File.ReadAllText(FindSharedContract()));

        var proposal = fixture.Service.CreateProposal(fixture.Snapshot, fixture.Finding);
        var root = contract.RootElement;

        Assert.Equal(root.GetProperty("expectedUpdatedContent").GetString(), proposal.UpdatedContent);
        Assert.Equal(
            root.GetProperty("expectedPatch").GetString()!.Replace("\n", Environment.NewLine),
            proposal.Patch.TrimEnd());
    }

    [Fact]
    public void M1012_ProposalMatchesSharedClientContract()
    {
        using var fixture = new Fixture(preflightSuccess: true, ruleCode: "M1012");
        using var contract = JsonDocument.Parse(File.ReadAllText(FindSharedContract("M1012")));

        var proposal = fixture.Service.CreateProposal(fixture.Snapshot, fixture.Finding);
        var root = contract.RootElement;

        Assert.Equal(root.GetProperty("expectedUpdatedContent").GetString(), proposal.UpdatedContent);
        Assert.Equal(
            root.GetProperty("expectedPatch").GetString()!.Replace("\n", Environment.NewLine),
            proposal.Patch.TrimEnd());
        Assert.Contains("exactly one pipeline", proposal.Assumptions[0], StringComparison.Ordinal);
        Assert.Contains("must confirm", proposal.Assumptions[1], StringComparison.Ordinal);
    }

    [Fact]
    public void M1012_RefusesAmbiguousPipelineSelection()
    {
        using var fixture = new Fixture(preflightSuccess: true, ruleCode: "M1012");
        var snapshot = fixture.Snapshot with
        {
            Symbols =
            [
                .. fixture.Snapshot.Symbols,
                new AnalysisSymbol(
                    "pipeline:Other",
                    "pipeline",
                    "Other",
                    new AnalysisSourceLocation(fixture.SourcePath, 1, 1),
                    [],
                    null)
            ]
        };

        var error = Assert.Throws<InvalidOperationException>(
            () => fixture.Service.CreateProposal(snapshot, fixture.Finding));

        Assert.Contains("exactly one pipeline", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void M1012_RefusesSourceWhoseNarrativeNoLongerMatchesSnapshot()
    {
        using var fixture = new Fixture(preflightSuccess: true, ruleCode: "M1012");
        File.WriteAllText(
            fixture.SourcePath,
            fixture.Original.Replace(
                "Telemetry operations",
                "Renamed Narrative",
                StringComparison.Ordinal));

        var error = Assert.Throws<InvalidOperationException>(
            () => fixture.Service.CreateProposal(fixture.Snapshot, fixture.Finding));

        Assert.Contains("no longer matches", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task M1002_FlowsThroughPreviewApplyValidateAndKeep()
    {
        using var fixture = new Fixture(preflightSuccess: true);
        var reloads = new List<FileChangedExternally>();
        fixture.EventBus.Subscribe<FileChangedExternally>(reloads.Add);
        var proposal = fixture.Service.CreateProposal(fixture.Snapshot, fixture.Finding);

        Assert.Contains("narrative \"Telemetry intent\"", proposal.Patch, StringComparison.Ordinal);
        Assert.Contains("applies_to { Telemetry }", proposal.Patch, StringComparison.Ordinal);
        Assert.Contains("does not invent stakeholders", proposal.Assumptions[1], StringComparison.Ordinal);
        Assert.Equal(fixture.Original, File.ReadAllText(fixture.SourcePath));

        var applied = await fixture.Service.ApplyAsync(proposal);
        Assert.Equal(MentorRefactoringState.Applied, applied.State);
        Assert.Contains("narrative \"Telemetry intent\"", File.ReadAllText(fixture.SourcePath), StringComparison.Ordinal);
        Assert.Equal(fixture.SourcePath, Assert.Single(reloads).Path);

        var validated = await fixture.Service.ValidateAsync(applied);
        Assert.Equal(MentorRefactoringState.Validated, validated.State);
        Assert.Equal(fixture.SourcePath, fixture.Compiler.LastContext?.ActiveDocumentPath);

        var kept = fixture.Service.Keep(validated);
        Assert.Equal(MentorRefactoringState.Kept, kept.State);
        Assert.Contains("No VCS staging", kept.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FailedCompilerCheck_AllowsExactRevert()
    {
        using var fixture = new Fixture(preflightSuccess: false);
        var proposal = fixture.Service.CreateProposal(fixture.Snapshot, fixture.Finding);
        var applied = await fixture.Service.ApplyAsync(proposal);

        var rejected = await fixture.Service.ValidateAsync(applied);
        var reverted = await fixture.Service.RevertAsync(rejected);

        Assert.Equal(MentorRefactoringState.ValidationFailed, rejected.State);
        Assert.Equal(MentorRefactoringState.Reverted, reverted.State);
        Assert.Equal(fixture.Original, File.ReadAllText(fixture.SourcePath));
    }

    [Fact]
    public async Task Apply_RefusesDirtyOpenDocument()
    {
        using var fixture = new Fixture(preflightSuccess: true);
        var proposal = fixture.Service.CreateProposal(fixture.Snapshot, fixture.Finding);
        fixture.OpenDocuments.Document = new Document(
            DocumentId.New(),
            fixture.SourcePath,
            fixture.Original + "// unsaved",
            true,
            null);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Service.ApplyAsync(proposal));

        Assert.Contains("Save the open document", error.Message, StringComparison.Ordinal);
        Assert.Equal(fixture.Original, File.ReadAllText(fixture.SourcePath));
    }

    [Fact]
    public async Task Revert_RefusesToOverwriteLaterSourceChange()
    {
        using var fixture = new Fixture(preflightSuccess: true);
        var proposal = fixture.Service.CreateProposal(fixture.Snapshot, fixture.Finding);
        var applied = await fixture.Service.ApplyAsync(proposal);
        File.AppendAllText(fixture.SourcePath, "// later edit");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Service.RevertAsync(applied));

        Assert.Contains("source changed", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("// later edit", File.ReadAllText(fixture.SourcePath), StringComparison.Ordinal);
    }

    [Fact]
    public void CreateProposal_RejectsUnsupportedFinding()
    {
        using var fixture = new Fixture(preflightSuccess: true);
        var finding = fixture.Finding with { Code = "M1001" };

        Assert.Throws<NotSupportedException>(
            () => fixture.Service.CreateProposal(fixture.Snapshot, finding));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(),
            "junexis-mentor-refactoring-" + Guid.NewGuid().ToString("N"));

        public Fixture(bool preflightSuccess, string ruleCode = "M1002")
        {
            Directory.CreateDirectory(_root);
            Directory.CreateDirectory(Path.Combine(_root, "src"));
            SourcePath = Path.Combine(_root, "src", "main.jnx");
            Original = ruleCode == "M1012"
                ? "pipeline Telemetry {\n}\n\nnarrative \"Telemetry operations\" {\n    intent \"Observe telemetry behavior\"\n}\n"
                : "pipeline Telemetry {\n}\n";
            File.WriteAllText(SourcePath, Original);
            var workspace = new WorkspaceService();
            workspace.OpenFolder(_root);
            Compiler = new StubCompiler(preflightSuccess);
            var selector = new CompilerSelector();
            selector.Register(Compiler);
            Service = new MentorRefactoringService(
                workspace,
                OpenDocuments,
                selector,
                new OutputService(),
                EventBus);
            var symbols = new List<AnalysisSymbol>
            {
                new(
                    "pipeline:Telemetry",
                    "pipeline",
                    "Telemetry",
                    new AnalysisSourceLocation(SourcePath, 1, 1),
                    [],
                    null)
            };
            if (ruleCode == "M1012")
            {
                symbols.Add(new AnalysisSymbol(
                    "narrative:Telemetry operations",
                    "narrative",
                    "Telemetry operations",
                    new AnalysisSourceLocation(SourcePath, 4, 1),
                    [],
                    null));
            }
            Snapshot = new AnalysisSnapshot(
                "build-1",
                SourcePath,
                Path.Combine(_root, "junexis.analysis.json"),
                symbols,
                [],
                new AnalysisTopology(
                    false,
                    0,
                    [],
                    new Dictionary<string, int>(),
                    new Dictionary<string, int>(),
                    new Dictionary<string, int>()));
            Finding = new MentorFinding(
                ruleCode,
                "warning",
                ruleCode == "M1012"
                    ? "Narrative is not connected."
                    : "Pipeline has no narrative.",
                ruleCode == "M1012"
                    ? "narrative:Telemetry operations"
                    : "pipeline:Telemetry",
                new AnalysisSourceLocation(SourcePath, ruleCode == "M1012" ? 4 : 1, 1));
        }

        public string Original { get; }
        public string SourcePath { get; }
        public StubOpenDocuments OpenDocuments { get; } = new();
        public EventBus EventBus { get; } = new();
        public StubCompiler Compiler { get; }
        public MentorRefactoringService Service { get; }
        public AnalysisSnapshot Snapshot { get; }
        public MentorFinding Finding { get; }

        public void Dispose()
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
    }

    private static string FindSharedContract(string ruleCode = "M1002")
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "junexis-mentor",
                "docs",
                $"mentor-refactoring-{ruleCode.ToLowerInvariant()}.fixture.json");
            if (File.Exists(candidate))
                return candidate;
        }
        throw new FileNotFoundException(
            $"Cannot locate the shared Mentor {ruleCode} client contract.");
    }

    private sealed class StubOpenDocuments : IOpenDocumentRepository
    {
        public Document? Document { get; set; }

        public IReadOnlyList<Document> GetOpenDocuments() =>
            Document is null ? [] : [Document];

        public bool TryGetByPath(string path, out Document document)
        {
            if (Document is not null
                && string.Equals(Document.FilePath, path, StringComparison.OrdinalIgnoreCase))
            {
                document = Document;
                return true;
            }
            document = null!;
            return false;
        }
    }

    private sealed class StubCompiler(bool success) : ICompilerService, ICompilerPreflightService
    {
        public string CompilerId => "junexis-test";
        public BuildContext? LastContext { get; private set; }

        public bool SupportsFile(string fileExtension) =>
            string.Equals(fileExtension, ".jnx", StringComparison.OrdinalIgnoreCase);

        public Task<CompilerPreflightResult> PreflightAsync(
            BuildContext context,
            CancellationToken cancellationToken = default)
        {
            LastContext = context;
            var diagnostics = success
                ? Array.Empty<Diagnostic>()
                : [new Diagnostic(DiagnosticSeverity.Error, "Rejected", context.ActiveDocumentPath)];
            return Task.FromResult(new CompilerPreflightResult(
                success,
                diagnostics,
                CompilerId,
                "build-refactor"));
        }

        public Task<CompilerResult> BuildAfterPreflightAsync(
            BuildContext context,
            CompilerPreflightResult preflight,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new CompilerResult(preflight.Success, preflight.Diagnostics, CompilerId));

        public Task<CompilerResult> BuildAsync(BuildContext context) =>
            Task.FromResult(new CompilerResult(true, [], CompilerId));

        public Task<CompilerResult> CleanAsync(BuildContext context) =>
            Task.FromResult(new CompilerResult(true, [], CompilerId));

        public IReadOnlyList<Diagnostic> GetDiagnostics() => [];
    }
}

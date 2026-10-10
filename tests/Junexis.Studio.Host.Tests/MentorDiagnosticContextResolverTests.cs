using Junexis.Studio.Abstractions;
using Junexis.Studio.Host;

namespace Junexis.Studio.Host.Tests;

public sealed class MentorDiagnosticContextResolverTests
{
    [Fact]
    public void Resolve_CurrentExactSymbol_ReturnsVersionedSeparateContext()
    {
        var source = Path.GetFullPath("src/main.jnx");
        var diagnostic = new Diagnostic(
            DiagnosticSeverity.Error,
            "Compiler message remains authoritative.",
            source,
            12,
            4,
            "junexis-lang",
            "E5001");
        var finding = new MentorFinding(
            "M1015",
            "warning",
            "Missing resilience boundary.",
            "task:Ingress",
            new AnalysisSourceLocation(source, 12, 1),
            "The task has no visible failure isolation.",
            "Add a supervisor around Ingress.");
        var snapshot = Snapshot("build-1", source);
        var mentor = Envelope("build-1", finding);

        var context = MentorDiagnosticContextResolver.Resolve(
            diagnostic,
            snapshot,
            mentor,
            "build-1");

        Assert.NotNull(context);
        Assert.Equal("1.0", context!.SchemaVersion);
        Assert.Equal("E5001", context.DiagnosticCode);
        Assert.Equal("task:Ingress", context.SymbolId);
        Assert.Equal(MentorDiagnosticMappingKind.ExactDeclaration, context.MappingKind);
        Assert.Equal("Mentor M1015", context.ExplanationReference);
        Assert.Equal("Add a supervisor around Ingress.", context.SmallestSafeNextStep);
        Assert.Equal("Compiler message remains authoritative.", diagnostic.Message);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
    }

    [Theory]
    [InlineData("different-build", 12, "E5001")]
    [InlineData("build-1", 13, "E5001")]
    [InlineData("build-1", 12, null)]
    public void Resolve_WithoutCurrentExactEvidence_ReturnsNull(
        string diagnosticsBuildId,
        int line,
        string? code)
    {
        var source = Path.GetFullPath("src/main.jnx");
        var diagnostic = new Diagnostic(
            DiagnosticSeverity.Error,
            "Compiler message.",
            source,
            line,
            1,
            "junexis-lang",
            code);
        var finding = new MentorFinding(
            "M1015", "warning", "Risk.", "task:Ingress", null);

        var context = MentorDiagnosticContextResolver.Resolve(
            diagnostic,
            Snapshot("build-1", source),
            Envelope("build-1", finding),
            diagnosticsBuildId);

        Assert.Null(context);
    }

    private static AnalysisSnapshot Snapshot(string buildId, string source) => new(
        buildId,
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

    private static MentorAnalysisEnvelope Envelope(
        string buildId,
        MentorFinding finding) => new(
        new MentorAnalysisResult(buildId, "analysis.json", [finding]),
        DateTimeOffset.UtcNow,
        MentorAnalysisFreshness.Current,
        new MentorFindingDelta([]));
}

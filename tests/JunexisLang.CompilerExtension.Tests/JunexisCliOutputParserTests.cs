using JunexisLang.Studio.CompilerExtension;
using Junexis.Studio.Abstractions;

namespace JunexisLang.Studio.CompilerExtension.Tests;

/// <summary>
/// Testy jednostkowe dla <see cref="JunexisCliOutputParser"/>.
/// </summary>
public sealed class JunexisCliOutputParserTests
{
    // --- V1 envelope ---

    [Fact]
    [Trait("Issue", "CONT-07")]
    public void Parse_V1Envelope_MapsCorrectly()
    {
        const string json = """
            {
              "schemaVersion": "1.0",
              "command": "compile",
              "success": true,
              "durationMs": 42,
              "inputFile": "app.jnx",
              "diagnostics": [
                { "file": "app.jnx", "line": 3, "column": 5, "level": "warning", "code": "W001", "message": "unused var" }
              ]
            }
            """;

        var (success, diagnostics) = JunexisCliOutputParser.Parse(json);

        Assert.True(success);
        Assert.Single(diagnostics);
        var d = diagnostics[0];
        Assert.Equal("app.jnx",              d.FilePath);
        Assert.Equal(3,                       d.Line);
        Assert.Equal(5,                       d.Column);
        Assert.Equal(DiagnosticSeverity.Warning, d.Severity);
        Assert.Equal("unused var",            d.Message);
        Assert.Equal("junexis-lang",          d.Source);
        Assert.Equal("W001",                  d.Code);
    }

    [Fact]
    [Trait("Issue", "31.2")]
    public void ParseBuildResult_V2Envelope_MapsArtifactContracts()
    {
        const string json = """
            {
              "schemaVersion": "2.0",
              "command": "build",
              "success": true,
              "durationMs": 42,
              "inputFile": "src/main.jnx",
              "diagnostics": [],
              "mode": "project",
              "projectRoot": "C:/project",
              "outputRoot": "C:/project/out",
              "buildId": "abc123",
              "targets": ["server", "report"],
              "bundleManifest": "C:/project/out/junexis.bundle.json",
              "artifactManifest": "C:/project/out/junexis.artifacts.json",
              "debugManifest": "C:/project/out/debug/junexis.debug.toml",
              "analysisSnapshot": "C:/project/out/analysis/junexis.analysis.json",
              "latestBuildReceipt": "C:/project/.junexis/state/latest-build.json"
            }
            """;

        var result = JunexisCliOutputParser.ParseBuildResult(json);

        Assert.True(result.Success);
        Assert.Equal("abc123", result.BuildId);
        Assert.Equal("C:/project", result.ProjectRoot);
        Assert.Equal(["server", "report"], result.Targets);
        Assert.EndsWith("junexis.bundle.json", result.BundleManifestPath);
        Assert.EndsWith("junexis.artifacts.json", result.ArtifactManifestPath);
        Assert.EndsWith("junexis.debug.toml", result.DebugManifestPath);
        Assert.EndsWith("junexis.analysis.json", result.AnalysisSnapshotPath);
        Assert.EndsWith("latest-build.json", result.LatestBuildReceiptPath);
    }

    [Fact]
    [Trait("Issue", "CONT-07")]
    public void Parse_V1Envelope_SuccessTrue_WithWarnings()
    {
        const string json = """
            {
              "schemaVersion": "1.0",
              "success": true,
              "diagnostics": [
                { "level": "warning", "message": "w" }
              ]
            }
            """;

        var (success, _) = JunexisCliOutputParser.Parse(json);

        Assert.True(success);
    }

    [Fact]
    [Trait("Issue", "CONT-07")]
    public void Parse_V1Envelope_SuccessFalse_WithErrors()
    {
        const string json = """
            {
              "schemaVersion": "1.0",
              "success": false,
              "diagnostics": [
                { "level": "error", "message": "E" }
              ]
            }
            """;

        var (success, _) = JunexisCliOutputParser.Parse(json);

        Assert.False(success);
    }

    // --- Legacy array ---

    [Fact]
    [Trait("Issue", "CONT-07")]
    public void Parse_LegacyArray_NoErrors_ReturnsSuccessTrue()
    {
        const string json = """
            [
              { "file": "a.jnx", "line": 1, "column": 1, "level": "warning", "code": "W1", "message": "w" }
            ]
            """;

        var (success, diagnostics) = JunexisCliOutputParser.Parse(json);

        Assert.True(success);
        Assert.Single(diagnostics);
    }

    [Fact]
    [Trait("Issue", "CONT-07")]
    public void Parse_LegacyArray_WithError_ReturnsSuccessFalse()
    {
        const string json = """
            [
              { "level": "error", "message": "err" }
            ]
            """;

        var (success, _) = JunexisCliOutputParser.Parse(json);

        Assert.False(success);
    }

    // --- Edge cases ---

    [Fact]
    [Trait("Issue", "CONT-07")]
    public void Parse_InvalidJson_ReturnsErrorDiagnostic()
    {
        var (success, diagnostics) = JunexisCliOutputParser.Parse("not json {{{");

        Assert.False(success);
        Assert.Single(diagnostics);
        Assert.Equal(DiagnosticSeverity.Error, diagnostics[0].Severity);
    }

    [Fact]
    [Trait("Issue", "CONT-07")]
    public void Parse_Empty_ReturnsSuccessTrue_NoDiagnostics()
    {
        var (success, diagnostics) = JunexisCliOutputParser.Parse("");

        Assert.True(success);
        Assert.Empty(diagnostics);
    }

    [Fact]
    [Trait("Issue", "CONT-07")]
    public void Parse_DiagnosticLevel_MappedCorrectly()
    {
        const string json = """
            {
              "schemaVersion": "1.0",
              "success": false,
              "diagnostics": [
                { "level": "error",   "message": "E" },
                { "level": "warning", "message": "W" },
                { "level": "info",    "message": "I" },
                { "level": "hint",    "message": "H" }
              ]
            }
            """;

        var (_, diagnostics) = JunexisCliOutputParser.Parse(json);

        Assert.Equal(4, diagnostics.Count);
        Assert.Equal(DiagnosticSeverity.Error,   diagnostics[0].Severity);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostics[1].Severity);
        Assert.Equal(DiagnosticSeverity.Info,    diagnostics[2].Severity);
        Assert.Equal(DiagnosticSeverity.Info,    diagnostics[3].Severity);
    }

    [Fact]
    [Trait("Issue", "CONT-07")]
    public void Parse_LineZero_MapsToNull()
    {
        const string json = """
            {
              "schemaVersion": "1.0",
              "success": true,
              "diagnostics": [
                { "level": "info", "line": 0,  "column": 0, "message": "A" },
                { "level": "info", "line": 5,  "column": 3, "message": "B" }
              ]
            }
            """;

        var (_, diagnostics) = JunexisCliOutputParser.Parse(json);

        Assert.Null(diagnostics[0].Line);
        Assert.Null(diagnostics[0].Column);
        Assert.Equal(5, diagnostics[1].Line);
        Assert.Equal(3, diagnostics[1].Column);
    }
}

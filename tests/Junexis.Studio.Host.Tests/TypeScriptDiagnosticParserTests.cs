using Junexis.Studio.Abstractions;
using Junexis.Studio.Host;

namespace Junexis.Studio.Host.Tests;

public sealed class TypeScriptDiagnosticParserTests
{
    [Fact]
    public void TryParse_StandardTscError_ReturnsSourceDiagnostic()
    {
        var root = Path.Combine(Path.GetTempPath(), "junexis-ts-diagnostics");

        var parsed = TypeScriptDiagnosticParser.TryParse(
            "src/main.ts(7,13): error TS2322: Type 'string' is not assignable to type 'number'.",
            root,
            out var diagnostic);

        Assert.True(parsed);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(7, diagnostic.Line);
        Assert.Equal(13, diagnostic.Column);
        Assert.Equal("typescript", diagnostic.Source);
        Assert.Contains("TS2322", diagnostic.Message, StringComparison.Ordinal);
        Assert.EndsWith(Path.Combine("src", "main.ts"), diagnostic.FilePath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryParse_UnrelatedOutput_ReturnsFalse()
    {
        Assert.False(TypeScriptDiagnosticParser.TryParse(
            "Version 6.0.0",
            Path.GetTempPath(),
            out var diagnostic));
        Assert.Null(diagnostic);
    }
}

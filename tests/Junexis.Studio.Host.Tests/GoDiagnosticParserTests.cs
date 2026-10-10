using Junexis.Studio.Abstractions;
using Junexis.Studio.Host;
using System;
using System.IO;
using Xunit;

namespace Junexis.Studio.Host.Tests;

public sealed class GoDiagnosticParserTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "junexis-studio-go-root");

    [Fact]
    [Trait("Issue", "95")]
    public void TryParse_StandardErrorLine_ReturnsDiagnostic()
    {
        var line = "./main.go:12:5: undefined: foo";

        var ok = GoDiagnosticParser.TryParse(line, Root, out var diag);

        Assert.True(ok);
        Assert.NotNull(diag);
        Assert.Equal(DiagnosticSeverity.Error, diag.Severity);
        Assert.Equal("undefined: foo", diag.Message);
        Assert.Equal(12, diag.Line);
        Assert.Equal(5, diag.Column);
        Assert.EndsWith(Path.Combine("junexis-studio-go-root", "main.go"), diag.FilePath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Issue", "95")]
    public void TryParse_WarningLine_ReturnsDiagnosticWithWarning()
    {
        var line = "pkg/util.go:3:1: warning: unused import";

        var ok = GoDiagnosticParser.TryParse(line, Root, out var diag);

        Assert.True(ok);
        Assert.Equal(DiagnosticSeverity.Warning, diag.Severity);
        Assert.Contains("unused import", diag.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Issue", "95")]
    public void TryParse_UnrecognizedLine_ReturnsFalse()
    {
        var line = "ok  github.com/user/project";

        var ok = GoDiagnosticParser.TryParse(line, Root, out var diag);

        Assert.False(ok);
        Assert.Null(diag);
    }

    [Fact]
    [Trait("Issue", "95")]
    public void TryParse_NullOrEmpty_ReturnsFalse()
    {
        Assert.False(GoDiagnosticParser.TryParse(null!, Root, out _));
        Assert.False(GoDiagnosticParser.TryParse("", Root, out _));
        Assert.False(GoDiagnosticParser.TryParse("   ", Root, out _));
    }

    [Fact]
    [Trait("Issue", "95")]
    public void TryParse_RelativePath_ResolvedAgainstRoot()
    {
        // go build zwykle emituje ścieżki względne (./pkg/file.go lub pkg/file.go)
        var line = "pkg/util.go:8:10: undefined: helper";

        var ok = GoDiagnosticParser.TryParse(line, Root, out var diag);

        Assert.True(ok);
        Assert.EndsWith(Path.Combine("junexis-studio-go-root", "pkg", "util.go"), diag.FilePath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(8, diag.Line);
        Assert.Equal(10, diag.Column);
    }
}

using Junexis.Studio.Abstractions;
using Junexis.Studio.Host;
using System;
using System.IO;
using Xunit;

namespace Junexis.Studio.Host.Tests;

public sealed class GppDiagnosticParserTests
{
    [Fact]
    [Trait("Issue", "21")]
    public void TryParse_ErrorLine_ReturnsDiagnostic()
    {
        var root = Path.Combine(Path.GetTempPath(), "junexis-studio-root");
        var line = "main.cpp:10:5: error: expected ';' before '}' token";

        var ok = GppDiagnosticParser.TryParse(line, root, out var diag);

        Assert.True(ok);
        Assert.NotNull(diag);
        Assert.Equal(DiagnosticSeverity.Error, diag.Severity);
        Assert.Equal("expected ';' before '}' token", diag.Message);
        Assert.EndsWith(Path.Combine("junexis-studio-root", "main.cpp"), diag.FilePath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(10, diag.Line);
        Assert.Equal(5, diag.Column);
    }

    [Theory]
    [Trait("Issue", "21")]
    [InlineData("main.cpp:1:1: warning: unused variable 'x'", DiagnosticSeverity.Warning)]
    [InlineData("main.cpp:1:1: note: here", DiagnosticSeverity.Info)]
    [InlineData("main.cpp:1:1: fatal error: missing", DiagnosticSeverity.Error)]
    public void TryParse_SeverityMapping_Works(string line, DiagnosticSeverity expected)
    {
        var ok = GppDiagnosticParser.TryParse(line, Directory.GetCurrentDirectory(), out var diag);

        Assert.True(ok);
        Assert.Equal(expected, diag.Severity);
    }
}

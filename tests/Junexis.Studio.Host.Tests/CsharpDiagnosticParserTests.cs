using Junexis.Studio.Abstractions;
using Junexis.Studio.Host;
using System;
using System.IO;
using Xunit;

namespace Junexis.Studio.Host.Tests;

public sealed class CsharpDiagnosticParserTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "junexis-studio-cs-root");

    [Fact]
    [Trait("Issue", "94")]
    public void TryParse_ErrorLine_ReturnsDiagnosticWithError()
    {
        var line = @"src\Program.cs(10,5): error CS0001: Type 'Foo' not found";

        var ok = CsharpDiagnosticParser.TryParse(line, Root, out var diag);

        Assert.True(ok);
        Assert.NotNull(diag);
        Assert.Equal(DiagnosticSeverity.Error, diag.Severity);
        Assert.Equal("Type 'Foo' not found", diag.Message);
        Assert.Equal(10, diag.Line);
        Assert.Equal(5, diag.Column);
    }

    [Fact]
    [Trait("Issue", "94")]
    public void TryParse_WarningLine_ReturnsDiagnosticWithWarning()
    {
        var line = @"src\Program.cs(3,1): warning CS0168: The variable 'ex' is declared but never used";

        var ok = CsharpDiagnosticParser.TryParse(line, Root, out var diag);

        Assert.True(ok);
        Assert.NotNull(diag);
        Assert.Equal(DiagnosticSeverity.Warning, diag.Severity);
        Assert.Contains("declared but never used", diag.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(3, diag.Line);
        Assert.Equal(1, diag.Column);
    }

    [Fact]
    [Trait("Issue", "94")]
    public void TryParse_UnrecognizedLine_ReturnsFalse()
    {
        var line = "Build succeeded.";

        var ok = CsharpDiagnosticParser.TryParse(line, Root, out var diag);

        Assert.False(ok);
        Assert.Null(diag);
    }

    [Fact]
    [Trait("Issue", "94")]
    public void TryParse_AbsolutePath_IsPreserved()
    {
        var absPath = OperatingSystem.IsWindows() ? @"C:\repos\project\Main.cs" : "/repos/project/Main.cs";
        var line = $"{absPath}(7,2): error CS0117: 'Console' does not contain a definition for 'Writ'";

        var ok = CsharpDiagnosticParser.TryParse(line, Root, out var diag);

        Assert.True(ok);
        Assert.StartsWith(OperatingSystem.IsWindows() ? "C:" : "/repos", diag.FilePath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Issue", "94")]
    public void TryParse_NullOrEmpty_ReturnsFalse()
    {
        Assert.False(CsharpDiagnosticParser.TryParse(null!, Root, out _));
        Assert.False(CsharpDiagnosticParser.TryParse("", Root, out _));
        Assert.False(CsharpDiagnosticParser.TryParse("  ", Root, out _));
    }
}

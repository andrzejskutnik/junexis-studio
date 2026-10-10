using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;
using Xunit;

namespace Junexis.Studio.Core.Tests;

public sealed class DiagnosticServiceTests
{
    [Fact]
    [Trait("Issue", "20")]
    public void Add_AppendsDiagnosticToSnapshot()
    {
        var sut = new DiagnosticService();
        var diagnostic = new Diagnostic(DiagnosticSeverity.Error, "boom", filePath: "a.txt", line: 1, column: 2);

        sut.Add(diagnostic);

        var snapshot = sut.GetDiagnostics();
        Assert.Single(snapshot);
        Assert.Equal(diagnostic, snapshot[0]);
    }

    [Fact]
    [Trait("Issue", "20")]
    public void DiagnosticsChanged_FiresOnAdd()
    {
        var sut = new DiagnosticService();
        var calls = 0;
        sut.DiagnosticsChanged += () => calls++;

        sut.Add(new Diagnostic(DiagnosticSeverity.Warning, "warn"));

        Assert.Equal(1, calls);
    }

    [Fact]
    [Trait("Issue", "20")]
    public void Clear_WhenNotEmpty_ClearsAndFires()
    {
        var sut = new DiagnosticService();
        sut.Add(new Diagnostic(DiagnosticSeverity.Info, "i"));

        var calls = 0;
        sut.DiagnosticsChanged += () => calls++;

        sut.Clear();

        Assert.Empty(sut.GetDiagnostics());
        Assert.Equal(1, calls);
    }

    [Fact]
    [Trait("Issue", "20")]
    public void Clear_WhenEmpty_DoesNotFire()
    {
        var sut = new DiagnosticService();
        var calls = 0;
        sut.DiagnosticsChanged += () => calls++;

        sut.Clear();

        Assert.Equal(0, calls);
    }
}

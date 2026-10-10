using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;
using Xunit;

namespace Junexis.Studio.Core.Tests;

/// <summary>
/// Tests for compiler diagnostics domain features:
/// <see cref="Diagnostic.Source"/>, <see cref="IDiagnosticService.GetBySource"/>,
/// <see cref="IDiagnosticService.ClearBySource"/>, and <see cref="DiagnosticsUpdatedEvent"/>.
/// </summary>
[Trait("Issue", "57")]
public class CompilerDiagnosticsTests
{
    [Fact]
    public void Diagnostic_Source_DefaultIsNull()
    {
        var d = new Diagnostic(DiagnosticSeverity.Error, "err");
        Assert.Null(d.Source);
    }

    [Fact]
    public void Diagnostic_Source_CanBeSet()
    {
        var d = new Diagnostic(DiagnosticSeverity.Error, "err", source: "gcc");
        Assert.Equal("gcc", d.Source);
    }

    [Fact]
    public void GetBySource_ReturnsOnlyMatchingDiagnostics()
    {
        var sut = new DiagnosticService();
        sut.Add(new Diagnostic(DiagnosticSeverity.Error, "e1", source: "gcc"));
        sut.Add(new Diagnostic(DiagnosticSeverity.Warning, "w1", source: "clang"));
        sut.Add(new Diagnostic(DiagnosticSeverity.Error, "e2", source: "gcc"));

        var gccDiags = sut.GetBySource("gcc");

        Assert.Equal(2, gccDiags.Count);
        Assert.All(gccDiags, d => Assert.Equal("gcc", d.Source));
    }

    [Fact]
    public void GetBySource_IsCaseInsensitive()
    {
        var sut = new DiagnosticService();
        sut.Add(new Diagnostic(DiagnosticSeverity.Error, "e1", source: "GCC"));

        var result = sut.GetBySource("gcc");
        Assert.Single(result);
    }

    [Fact]
    public void GetBySource_ReturnsEmpty_WhenNoMatch()
    {
        var sut = new DiagnosticService();
        sut.Add(new Diagnostic(DiagnosticSeverity.Error, "e1", source: "gcc"));

        var result = sut.GetBySource("clang");
        Assert.Empty(result);
    }

    [Fact]
    public void ClearBySource_RemovesOnlyMatchingDiagnostics()
    {
        var sut = new DiagnosticService();
        sut.Add(new Diagnostic(DiagnosticSeverity.Error, "e1", source: "gcc"));
        sut.Add(new Diagnostic(DiagnosticSeverity.Warning, "w1", source: "clang"));
        sut.Add(new Diagnostic(DiagnosticSeverity.Error, "e2", source: "gcc"));

        sut.ClearBySource("gcc");

        var all = sut.GetDiagnostics();
        Assert.Single(all);
        Assert.Equal("clang", all[0].Source);
    }

    [Fact]
    public void ClearBySource_FiresDiagnosticsChanged()
    {
        var sut = new DiagnosticService();
        sut.Add(new Diagnostic(DiagnosticSeverity.Error, "e1", source: "gcc"));

        var changed = 0;
        sut.DiagnosticsChanged += () => changed++;

        sut.ClearBySource("gcc");

        Assert.Equal(1, changed);
    }

    [Fact]
    public void ClearBySource_DoesNotFire_WhenNoMatch()
    {
        var sut = new DiagnosticService();
        sut.Add(new Diagnostic(DiagnosticSeverity.Error, "e1", source: "gcc"));

        var changed = 0;
        sut.DiagnosticsChanged += () => changed++;

        sut.ClearBySource("clang");

        Assert.Equal(0, changed);
    }

    [Fact]
    public void ClearBySource_IsCaseInsensitive()
    {
        var sut = new DiagnosticService();
        sut.Add(new Diagnostic(DiagnosticSeverity.Error, "e1", source: "GCC"));

        sut.ClearBySource("gcc");

        Assert.Empty(sut.GetDiagnostics());
    }

    [Fact]
    public void DiagnosticsUpdatedEvent_Properties()
    {
        var diags = new[] { new Diagnostic(DiagnosticSeverity.Error, "e", source: "gcc") };
        var evt = new DiagnosticsUpdatedEvent("gcc", diags);

        Assert.Equal("gcc", evt.Source);
        Assert.Single(evt.Diagnostics);
    }

    [Fact]
    public void DiagnosticsUpdatedEvent_RecordEquality()
    {
        var list = Array.Empty<Diagnostic>();
        var a = new DiagnosticsUpdatedEvent("gcc", list);
        var b = new DiagnosticsUpdatedEvent("gcc", list);
        Assert.Equal(a, b);
    }
}

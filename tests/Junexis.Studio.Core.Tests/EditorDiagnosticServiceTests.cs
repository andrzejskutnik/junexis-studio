using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;

namespace Junexis.Studio.Core.Tests;

public sealed class EditorDiagnosticServiceTests
{
    [Fact]
    public void GetForPath_FiltersAndOrdersLocatedDiagnostics()
    {
        var source = new DiagnosticService();
        var path = Path.GetFullPath("main.jnx");
        source.Add(new Diagnostic(DiagnosticSeverity.Warning, "later", path, 8, 2));
        source.Add(new Diagnostic(DiagnosticSeverity.Error, "other", Path.GetFullPath("other.jnx"), 1, 1));
        source.Add(new Diagnostic(DiagnosticSeverity.Error, "first", path, 3, 7));
        source.Add(new Diagnostic(DiagnosticSeverity.Info, "no location"));
        using var service = new EditorDiagnosticService(source);

        var result = service.GetForPath(path);

        Assert.Equal(["first", "later"], result.Select(item => item.Message));
    }

    [Fact]
    public void GetAt_PrefersDiagnosticsCloseToColumn()
    {
        var source = new DiagnosticService();
        var path = Path.GetFullPath("main.jnx");
        source.Add(new Diagnostic(DiagnosticSeverity.Error, "near", path, 5, 10));
        source.Add(new Diagnostic(DiagnosticSeverity.Warning, "far", path, 5, 30));
        using var service = new EditorDiagnosticService(source);

        var result = service.GetAt(path, 5, 11);

        Assert.Equal("near", Assert.Single(result).Message);
    }

    [Fact]
    public void FindAdjacent_NavigatesAndWrapsWithinOrderedSet()
    {
        var source = new DiagnosticService();
        var path = Path.GetFullPath("main.jnx");
        source.Add(new Diagnostic(DiagnosticSeverity.Error, "first", path, 2, 1));
        source.Add(new Diagnostic(DiagnosticSeverity.Warning, "second", path, 9, 3));
        using var service = new EditorDiagnosticService(source);

        Assert.Equal("second", service.FindAdjacent(path, 2, 1, previous: false)?.Message);
        Assert.Equal("first", service.FindAdjacent(path, 9, 3, previous: false)?.Message);
        Assert.Equal("second", service.FindAdjacent(path, 2, 1, previous: true)?.Message);
    }

    [Fact]
    public void DiagnosticsChanged_IsForwardedUntilDispose()
    {
        var source = new DiagnosticService();
        var service = new EditorDiagnosticService(source);
        var changes = 0;
        service.DiagnosticsChanged += () => changes++;

        source.Add(new Diagnostic(DiagnosticSeverity.Error, "one", "main.jnx", 1, 1));
        service.Dispose();
        source.Add(new Diagnostic(DiagnosticSeverity.Error, "two", "main.jnx", 2, 1));

        Assert.Equal(1, changes);
    }
}

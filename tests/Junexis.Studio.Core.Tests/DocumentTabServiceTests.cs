using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;
using Xunit;

namespace Junexis.Studio.Core.Tests;

public sealed class DocumentTabServiceTests
{
    // ─── Helpers ─────────────────────────────────────────

    private static (DocumentTabService sut, EventBus bus, ActiveDocumentService active) Create()
    {
        var bus = new EventBus();
        var active = new ActiveDocumentService();
        var sut = new DocumentTabService(bus, active);
        return (sut, bus, active);
    }

    // ─── Constructor ─────────────────────────────────────

    [Fact]
    [Trait("Issue", "38")]
    public void Ctor_NullEventBus_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new DocumentTabService(null!, new ActiveDocumentService()));
    }

    [Fact]
    [Trait("Issue", "38")]
    public void Ctor_NullActiveDocument_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new DocumentTabService(new EventBus(), null!));
    }

    // ─── Open ────────────────────────────────────────────

    [Fact]
    [Trait("Issue", "38")]
    public void Open_AddsTab()
    {
        var (sut, _, _) = Create();

        sut.Open("/file.txt");

        Assert.Single(sut.OpenTabs);
        Assert.Equal("/file.txt", sut.OpenTabs[0].Path);
        Assert.Equal("file.txt", sut.OpenTabs[0].Title);
    }

    [Fact]
    [Trait("Issue", "38")]
    public void Open_SetsActiveTab()
    {
        var (sut, _, active) = Create();

        sut.Open("/file.txt");

        Assert.NotNull(sut.ActiveTab);
        Assert.Equal("/file.txt", sut.ActiveTab!.Path);
        Assert.Equal("/file.txt", active.ActivePath);
    }

    [Fact]
    [Trait("Issue", "38")]
    public void Open_DuplicatePath_ActivatesExisting_NoDuplicate()
    {
        var (sut, _, _) = Create();

        sut.Open("/file.txt");
        sut.Open("/file.txt");

        Assert.Single(sut.OpenTabs);
    }

    [Fact]
    [Trait("Issue", "38")]
    public void Open_CaseInsensitiveDuplicate_ActivatesExisting()
    {
        var (sut, _, _) = Create();

        sut.Open("/File.TXT");
        sut.Open("/file.txt");

        Assert.Single(sut.OpenTabs);
    }

    [Fact]
    [Trait("Issue", "38")]
    public void Open_EmptyPath_Ignored()
    {
        var (sut, _, _) = Create();

        sut.Open("");
        sut.Open("   ");

        Assert.Empty(sut.OpenTabs);
    }

    [Fact]
    [Trait("Issue", "38")]
    public void Open_MultipleFiles_AllTracked()
    {
        var (sut, _, _) = Create();

        sut.Open("/a.txt");
        sut.Open("/b.txt");
        sut.Open("/c.txt");

        Assert.Equal(3, sut.OpenTabs.Count);
    }

    [Fact]
    [Trait("Issue", "38")]
    public void Open_LastOpened_BecomesActive()
    {
        var (sut, _, active) = Create();

        sut.Open("/a.txt");
        sut.Open("/b.txt");

        Assert.Equal("/b.txt", sut.ActiveTab!.Path);
        Assert.Equal("/b.txt", active.ActivePath);
    }

    [Fact]
    [Trait("Issue", "38")]
    public void Open_EmitsTabsChanged()
    {
        var (sut, _, _) = Create();
        var fired = false;
        sut.TabsChanged += () => fired = true;

        sut.Open("/file.txt");

        Assert.True(fired);
    }

    [Fact]
    [Trait("Issue", "38")]
    public void Open_EmitsActiveTabChanged()
    {
        var (sut, _, _) = Create();
        var fired = false;
        sut.ActiveTabChanged += () => fired = true;

        sut.Open("/file.txt");

        Assert.True(fired);
    }

    // ─── Activate ────────────────────────────────────────

    [Fact]
    [Trait("Issue", "38")]
    public void Activate_SetsActiveToExistingTab()
    {
        var (sut, _, active) = Create();
        sut.Open("/a.txt");
        sut.Open("/b.txt");

        sut.Activate("/a.txt");

        Assert.Equal("/a.txt", sut.ActiveTab!.Path);
        Assert.Equal("/a.txt", active.ActivePath);
    }

    [Fact]
    [Trait("Issue", "38")]
    public void Activate_UnknownPath_Ignored()
    {
        var (sut, _, _) = Create();
        sut.Open("/a.txt");

        sut.Activate("/not-open.txt");

        Assert.Equal("/a.txt", sut.ActiveTab!.Path);
    }

    [Fact]
    [Trait("Issue", "38")]
    public void Activate_AlreadyActive_NoChange()
    {
        var (sut, _, _) = Create();
        sut.Open("/a.txt");
        var count = 0;
        sut.ActiveTabChanged += () => count++;

        sut.Activate("/a.txt");

        Assert.Equal(0, count);
    }

    [Fact]
    [Trait("Issue", "38")]
    public void Activate_EmptyPath_Ignored()
    {
        var (sut, _, _) = Create();
        sut.Open("/a.txt");

        sut.Activate("");

        Assert.Equal("/a.txt", sut.ActiveTab!.Path);
    }

    // ─── Close ───────────────────────────────────────────

    [Fact]
    [Trait("Issue", "38")]
    public void Close_RemovesTab()
    {
        var (sut, _, _) = Create();
        sut.Open("/a.txt");

        sut.Close("/a.txt");

        Assert.Empty(sut.OpenTabs);
    }

    [Fact]
    [Trait("Issue", "38")]
    public void Close_ActiveTab_ActivatesNeighbor()
    {
        var (sut, _, active) = Create();
        sut.Open("/a.txt");
        sut.Open("/b.txt");
        sut.Open("/c.txt");
        sut.Activate("/b.txt");

        sut.Close("/b.txt");

        Assert.Equal(2, sut.OpenTabs.Count);
        Assert.NotNull(sut.ActiveTab);
        Assert.Equal("/c.txt", sut.ActiveTab!.Path);
        Assert.Equal("/c.txt", active.ActivePath);
    }

    [Fact]
    [Trait("Issue", "38")]
    public void Close_LastTab_ClearsActive()
    {
        var (sut, _, active) = Create();
        sut.Open("/a.txt");

        sut.Close("/a.txt");

        Assert.Null(sut.ActiveTab);
        Assert.Null(active.ActivePath);
    }

    [Fact]
    [Trait("Issue", "38")]
    public void Close_NonActiveTab_ActiveUnchanged()
    {
        var (sut, _, _) = Create();
        sut.Open("/a.txt");
        sut.Open("/b.txt");

        sut.Close("/a.txt");

        Assert.Equal("/b.txt", sut.ActiveTab!.Path);
    }

    [Fact]
    [Trait("Issue", "38")]
    public void Close_EmitsTabsChanged()
    {
        var (sut, _, _) = Create();
        sut.Open("/a.txt");
        var fired = false;
        sut.TabsChanged += () => fired = true;

        sut.Close("/a.txt");

        Assert.True(fired);
    }

    [Fact]
    [Trait("Issue", "38")]
    public void Close_PublishesDocumentClosed()
    {
        var (sut, bus, _) = Create();
        sut.Open("/a.txt");
        DocumentClosed? received = null;
        bus.Subscribe<DocumentClosed>(e => received = e);

        sut.Close("/a.txt");

        Assert.NotNull(received);
        Assert.Equal("/a.txt", received!.Path);
    }

    [Fact]
    [Trait("Issue", "38")]
    public void Close_UnknownPath_Ignored()
    {
        var (sut, _, _) = Create();
        sut.Open("/a.txt");

        sut.Close("/not-open.txt");

        Assert.Single(sut.OpenTabs);
    }

    [Fact]
    [Trait("Issue", "38")]
    public void Close_LastTabAtEnd_ActivatesPrevious()
    {
        var (sut, _, _) = Create();
        sut.Open("/a.txt");
        sut.Open("/b.txt");
        sut.Open("/c.txt");

        sut.Close("/c.txt");

        Assert.Equal("/b.txt", sut.ActiveTab!.Path);
    }

    // ─── FileOpenRequested integration ───────────────────

    [Fact]
    [Trait("Issue", "38")]
    public void FileOpenRequested_OpensTab()
    {
        var (sut, bus, _) = Create();

        bus.Publish(new FileOpenRequested("/auto.txt"));

        Assert.Single(sut.OpenTabs);
        Assert.Equal("/auto.txt", sut.OpenTabs[0].Path);
    }

    [Fact]
    [Trait("Issue", "38")]
    public void FileOpenRequested_Duplicate_ActivatesExisting()
    {
        var (sut, bus, _) = Create();
        bus.Publish(new FileOpenRequested("/auto.txt"));
        bus.Publish(new FileOpenRequested("/auto.txt"));

        Assert.Single(sut.OpenTabs);
    }

    // ─── Dispose ─────────────────────────────────────────

    [Fact]
    [Trait("Issue", "38")]
    public void Dispose_UnsubscribesFromEventBus()
    {
        var (sut, bus, _) = Create();
        sut.Dispose();

        bus.Publish(new FileOpenRequested("/after-dispose.txt"));

        Assert.Empty(sut.OpenTabs);
    }

    // ─── Title extraction ────────────────────────────────

    [Fact]
    [Trait("Issue", "38")]
    public void Open_ExtractsTitleFromFilename()
    {
        var (sut, _, _) = Create();

        sut.Open(@"C:\work\src\main.cs");

        Assert.Equal("main.cs", sut.OpenTabs[0].Title);
    }

    [Fact]
    [Trait("Issue", "38")]
    public void Open_PathWithoutFilename_UsesFullPath()
    {
        var (sut, _, _) = Create();

        sut.Open("/");

        Assert.Single(sut.OpenTabs);
    }

    // ─── Focus routing ───────────────────────────────────

    [Fact]
    [Trait("Issue", "38")]
    public void ActiveTab_SyncsActiveDocumentService()
    {
        var (sut, _, active) = Create();

        sut.Open("/a.txt");
        Assert.Equal("/a.txt", active.ActivePath);

        sut.Open("/b.txt");
        Assert.Equal("/b.txt", active.ActivePath);

        sut.Activate("/a.txt");
        Assert.Equal("/a.txt", active.ActivePath);

        sut.Close("/a.txt");
        Assert.Equal("/b.txt", active.ActivePath);

        sut.Close("/b.txt");
        Assert.Null(active.ActivePath);
    }
}

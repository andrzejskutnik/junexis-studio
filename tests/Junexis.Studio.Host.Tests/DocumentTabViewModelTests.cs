using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;
using Junexis.Studio.Host;
using Xunit;

namespace Junexis.Studio.Host.Tests;

/// <summary>
/// Testy DocumentTabViewModel — ViewModel pojedynczej zakładki dokumentu.
/// </summary>
public sealed class DocumentTabViewModelTests
{
    private static (DocumentTabViewModel sut, EventBus bus, OutputService output) CreateTab(
        string filePath, string content)
    {
        var tmp = Directory.CreateTempSubdirectory("junexis_studio_tab_tests_");
        File.WriteAllText(filePath, content);

        var workspace = new WorkspaceService();
        workspace.OpenFolder(tmp.FullName);

        var bus = new EventBus();
        var output = new OutputService();

        var sut = new DocumentTabViewModel(filePath, content, workspace, bus, output);
        return (sut, bus, output);
    }

    [Fact]
    [Trait("Issue", "103")]
    public void Constructor_SetsFilePathAndFileName()
    {
        var tmp = Directory.CreateTempSubdirectory("junexis_studio_tab_tests_");
        try
        {
            var filePath = Path.Combine(tmp.FullName, "hello.txt");
            File.WriteAllText(filePath, "test");

            var workspace = new WorkspaceService();
            workspace.OpenFolder(tmp.FullName);
            var bus = new EventBus();
            var output = new OutputService();

            var sut = new DocumentTabViewModel(filePath, "test", workspace, bus, output);

            Assert.Equal(filePath, sut.FilePath);
            Assert.Equal("hello.txt", sut.FileName);
            Assert.Equal("test", sut.Content);
            Assert.False(sut.IsDirty);
            Assert.False(sut.IsOutOfDate);
        }
        finally
        {
            try { tmp.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    [Trait("Issue", "103")]
    public void ContentChange_SetsDirty_AndPublishesDocumentChanged()
    {
        var tmp = Directory.CreateTempSubdirectory("junexis_studio_tab_tests_");
        try
        {
            var filePath = Path.Combine(tmp.FullName, "a.txt");
            File.WriteAllText(filePath, "orig");

            var workspace = new WorkspaceService();
            workspace.OpenFolder(tmp.FullName);
            var bus = new EventBus();
            var output = new OutputService();

            var sut = new DocumentTabViewModel(filePath, "orig", workspace, bus, output);

            var changed = 0;
            bus.Subscribe<DocumentChanged>(e => { if (e.Path == filePath) changed++; });

            sut.Content = "modified";

            Assert.True(sut.IsDirty);
            Assert.True(changed >= 1, "DocumentChanged should be published on Content change.");
        }
        finally
        {
            try { tmp.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    [Trait("Issue", "103")]
    public void Save_WritesFile_ClearsDirty_PublishesDocumentSaved()
    {
        var tmp = Directory.CreateTempSubdirectory("junexis_studio_tab_tests_");
        try
        {
            var filePath = Path.Combine(tmp.FullName, "a.txt");
            File.WriteAllText(filePath, "orig");

            var workspace = new WorkspaceService();
            workspace.OpenFolder(tmp.FullName);
            var bus = new EventBus();
            var output = new OutputService();

            var sut = new DocumentTabViewModel(filePath, "orig", workspace, bus, output);

            var saved = 0;
            bus.Subscribe<DocumentSaved>(e => { if (e.Path == filePath) saved++; });

            sut.Content = "changed";
            Assert.True(sut.IsDirty);

            sut.Save();

            Assert.False(sut.IsDirty);
            Assert.Equal("changed", File.ReadAllText(filePath));
            Assert.Equal(1, saved);
        }
        finally
        {
            try { tmp.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    [Trait("Issue", "103")]
    public void NavigateTo_SetsRequestedLineAndColumn()
    {
        var tmp = Directory.CreateTempSubdirectory("junexis_studio_tab_tests_");
        try
        {
            var filePath = Path.Combine(tmp.FullName, "a.txt");
            File.WriteAllText(filePath, "line1\nline2\n");

            var workspace = new WorkspaceService();
            workspace.OpenFolder(tmp.FullName);
            var bus = new EventBus();
            var output = new OutputService();

            var sut = new DocumentTabViewModel(filePath, "line1\nline2\n", workspace, bus, output);

            sut.NavigateTo(2, 5);

            Assert.Equal(2, sut.RequestedLine);
            Assert.Equal(5, sut.RequestedColumn);
            Assert.Equal(1, sut.NavigationRequestVersion);
        }
        finally
        {
            try { tmp.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    [Trait("Issue", "103")]
    public void RequestClose_RaisesCloseRequestedEvent()
    {
        var tmp = Directory.CreateTempSubdirectory("junexis_studio_tab_tests_");
        try
        {
            var filePath = Path.Combine(tmp.FullName, "a.txt");
            File.WriteAllText(filePath, "");

            var workspace = new WorkspaceService();
            workspace.OpenFolder(tmp.FullName);
            var bus = new EventBus();
            var output = new OutputService();

            var sut = new DocumentTabViewModel(filePath, "", workspace, bus, output);

            DocumentTabViewModel? closedTab = null;
            sut.CloseRequested += t => closedTab = t;

            sut.RequestClose();

            Assert.Same(sut, closedTab);
        }
        finally
        {
            try { tmp.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    [Trait("Issue", "103")]
    public void HandleExternalChange_WhenNotDirty_ReloadsContent()
    {
        var tmp = Directory.CreateTempSubdirectory("junexis_studio_tab_tests_");
        try
        {
            var filePath = Path.Combine(tmp.FullName, "a.txt");
            File.WriteAllText(filePath, "v1");

            var workspace = new WorkspaceService();
            workspace.OpenFolder(tmp.FullName);
            var bus = new EventBus();
            var output = new OutputService();

            var sut = new DocumentTabViewModel(filePath, "v1", workspace, bus, output);

            File.WriteAllText(filePath, "v2");
            sut.HandleExternalChange();

            Assert.Equal("v2", sut.Content);
            Assert.False(sut.IsDirty);
            Assert.False(sut.IsOutOfDate);
        }
        finally
        {
            try { tmp.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    [Trait("Issue", "103")]
    public void HandleExternalChange_WhenDirty_MarksOutOfDate()
    {
        var tmp = Directory.CreateTempSubdirectory("junexis_studio_tab_tests_");
        try
        {
            var filePath = Path.Combine(tmp.FullName, "a.txt");
            File.WriteAllText(filePath, "v1");

            var workspace = new WorkspaceService();
            workspace.OpenFolder(tmp.FullName);
            var bus = new EventBus();
            var output = new OutputService();

            var sut = new DocumentTabViewModel(filePath, "v1", workspace, bus, output);

            sut.Content = "local";
            Assert.True(sut.IsDirty);

            File.WriteAllText(filePath, "external");
            sut.HandleExternalChange();

            Assert.Equal("local", sut.Content);
            Assert.True(sut.IsOutOfDate);
        }
        finally
        {
            try { tmp.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    [Trait("Issue", "103")]
    public void HandleExternalDelete_MarksOutOfDate()
    {
        var tmp = Directory.CreateTempSubdirectory("junexis_studio_tab_tests_");
        try
        {
            var filePath = Path.Combine(tmp.FullName, "a.txt");
            File.WriteAllText(filePath, "hello");

            var workspace = new WorkspaceService();
            workspace.OpenFolder(tmp.FullName);
            var bus = new EventBus();
            var output = new OutputService();

            var sut = new DocumentTabViewModel(filePath, "hello", workspace, bus, output);

            sut.HandleExternalDelete();

            Assert.True(sut.IsOutOfDate);
        }
        finally
        {
            try { tmp.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    [Trait("Issue", "103")]
    public void HandleExternalRename_WhenNotDirty_UpdatesFilePath()
    {
        var tmp = Directory.CreateTempSubdirectory("junexis_studio_tab_tests_");
        try
        {
            var oldPath = Path.Combine(tmp.FullName, "old.txt");
            var newPath = Path.Combine(tmp.FullName, "new.txt");
            File.WriteAllText(oldPath, "data");
            File.Move(oldPath, newPath);

            var workspace = new WorkspaceService();
            workspace.OpenFolder(tmp.FullName);
            var bus = new EventBus();
            var output = new OutputService();

            var sut = new DocumentTabViewModel(oldPath, "data", workspace, bus, output);

            sut.HandleExternalRename(newPath);

            Assert.Equal(newPath, sut.FilePath);
            Assert.Equal("new.txt", sut.FileName);
            Assert.Equal("data", sut.Content);
            Assert.False(sut.IsDirty);
        }
        finally
        {
            try { tmp.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    [Trait("Issue", "103")]
    public void Constructor_ThrowsOnNullArguments()
    {
        var workspace = new WorkspaceService();
        var bus = new EventBus();
        var output = new OutputService();

        Assert.Throws<ArgumentNullException>(() =>
            new DocumentTabViewModel(null!, "c", workspace, bus, output));
        Assert.Throws<ArgumentNullException>(() =>
            new DocumentTabViewModel("p", "c", null!, bus, output));
        Assert.Throws<ArgumentNullException>(() =>
            new DocumentTabViewModel("p", "c", workspace, null!, output));
        Assert.Throws<ArgumentNullException>(() =>
            new DocumentTabViewModel("p", "c", workspace, bus, null!));
    }

    [Fact]
    [Trait("Issue", "104")]
    public void IsSearchVisible_DefaultIsFalse()
    {
        var workspace = new WorkspaceService();
        var bus = new EventBus();
        var output = new OutputService();

        var sut = new DocumentTabViewModel("/tmp/f.txt", "content", workspace, bus, output);

        Assert.False(sut.IsSearchVisible);
    }

    [Fact]
    [Trait("Issue", "104")]
    public void ToggleSearch_ShowsThenHides()
    {
        var workspace = new WorkspaceService();
        var bus = new EventBus();
        var output = new OutputService();

        var sut = new DocumentTabViewModel("/tmp/f.txt", "content", workspace, bus, output);

        sut.ToggleSearch();
        Assert.True(sut.IsSearchVisible);

        sut.ToggleSearch();
        Assert.False(sut.IsSearchVisible);
    }

    [Fact]
    [Trait("Issue", "104")]
    public void IsSearchVisible_RaisesPropertyChanged()
    {
        var workspace = new WorkspaceService();
        var bus = new EventBus();
        var output = new OutputService();

        var sut = new DocumentTabViewModel("/tmp/f.txt", "content", workspace, bus, output);
        var changes = new System.Collections.Generic.List<string?>();
        sut.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        sut.IsSearchVisible = true;

        Assert.Contains(nameof(DocumentTabViewModel.IsSearchVisible), changes);
    }
}

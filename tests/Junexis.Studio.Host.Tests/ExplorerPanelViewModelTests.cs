using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;
using Junexis.Studio.Host;
using Xunit;

namespace Junexis.Studio.Host.Tests;

public sealed class ExplorerPanelViewModelTests
{
    [Fact]
    public void Constructor_WithoutOpenFolder_ExposesEmptyWorkspaceState()
    {
        var sut = new ExplorerPanelViewModel(
            new WorkspaceService(),
            new EventBus(),
            new OutputService(),
            versionControl: null);

        Assert.True(sut.IsEmptyWorkspace);
        Assert.False(sut.HasOpenFolder);
        Assert.Empty(sut.Nodes);
    }

    [Fact]
    public void Constructor_WithRecentFolders_ExposesNamesPathsAndGitContext()
    {
        var root = Directory.CreateTempSubdirectory("junexis_recent_workspaces_");
        try
        {
            var repository = Directory.CreateDirectory(Path.Combine(root.FullName, "repository"));
            Directory.CreateDirectory(Path.Combine(repository.FullName, ".git"));
            var folder = Directory.CreateDirectory(Path.Combine(root.FullName, "folder"));

            var sut = new ExplorerPanelViewModel(
                new WorkspaceService(),
                new EventBus(),
                new OutputService(),
                versionControl: null,
                recentFolders: [repository.FullName, folder.FullName]);

            Assert.True(sut.HasRecentWorkspaces);
            Assert.Collection(
                sut.RecentWorkspaces,
                item =>
                {
                    Assert.Equal("repository", item.DisplayName);
                    Assert.Equal(repository.FullName, item.FullPath);
                    Assert.True(item.IsGitRepository);
                },
                item =>
                {
                    Assert.Equal("folder", item.DisplayName);
                    Assert.Equal(folder.FullName, item.FullPath);
                    Assert.False(item.IsGitRepository);
                });
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void OpeningFolder_RefreshesEmptyWorkspaceStateAndTree()
    {
        var root = Directory.CreateTempSubdirectory("junexis_open_workspace_");
        try
        {
            var workspace = new WorkspaceService();
            using var sut = new ExplorerPanelViewModel(
                workspace,
                new EventBus(),
                new OutputService(),
                versionControl: null);

            workspace.OpenFolder(root.FullName);

            Assert.True(sut.HasOpenFolder);
            Assert.False(sut.IsEmptyWorkspace);
            Assert.Single(sut.Nodes);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    /// <summary>
    /// Regresja #102: DoubleTapped może dostarczyć węzeł katalogu (nie pliku).
    /// RequestOpen dla katalogu NIE powinien publikować zdarzenia (IsDirectory = true → return early).
    /// </summary>
    [Fact]
    [Trait("Issue", "102")]
    public void RequestOpen_ForDirectory_DoesNotPublishFileOpenRequested()
    {
        var tmp = Directory.CreateTempSubdirectory("junexis_studio_tests_");
        try
        {
            var subDir = Directory.CreateDirectory(Path.Combine(tmp.FullName, "subdir"));

            var workspace = new WorkspaceService();
            workspace.OpenFolder(tmp.FullName);

            var bus = new EventBus();
            var output = new OutputService();

            var published = false;
            bus.Subscribe<FileOpenRequested>(_ => published = true);

            var sut = new ExplorerPanelViewModel(workspace, bus, output, versionControl: null);

            var dirNode = FindNodeByPath(sut.Nodes, subDir.FullName);
            Assert.NotNull(dirNode);
            Assert.True(dirNode!.IsDirectory);

            sut.RequestOpen(dirNode);

            Assert.False(published, "RequestOpen dla katalogu nie powinien publikować FileOpenRequested");
        }
        finally
        {
            try { tmp.Delete(recursive: true); } catch { }
        }
    }

    /// <summary>
    /// Regresja #102: RequestOpen dla pliku zagnieżdżonego w podkatalogu publikuje poprawną ścieżkę.
    /// Weryfikuje scenariusz, w którym ExplorerPanelView rozwiązuje węzeł z e.Source (DataContext).
    /// </summary>
    [Fact]
    [Trait("Issue", "102")]
    public void RequestOpen_ForNestedFile_PublishesCorrectFullPath()
    {
        var tmp = Directory.CreateTempSubdirectory("junexis_studio_tests_");
        try
        {
            var subDir = Directory.CreateDirectory(Path.Combine(tmp.FullName, "sub"));
            var filePath = Path.Combine(subDir.FullName, "nested.txt");
            File.WriteAllText(filePath, "content");

            var workspace = new WorkspaceService();
            workspace.OpenFolder(tmp.FullName);

            var bus = new EventBus();
            var output = new OutputService();

            FileOpenRequested? received = null;
            bus.Subscribe<FileOpenRequested>(e => received = e);

            var sut = new ExplorerPanelViewModel(workspace, bus, output, versionControl: null);

            var fileNode = FindNodeByPath(sut.Nodes, filePath);
            Assert.NotNull(fileNode);
            Assert.False(fileNode!.IsDirectory);

            sut.RequestOpen(fileNode);

            Assert.NotNull(received);
            Assert.Equal(filePath, received!.Path, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            try { tmp.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    [Trait("Issue", "11")]
    public void RequestOpen_ForFile_PublishesFileOpenRequested()
    {
        var tmp = Directory.CreateTempSubdirectory("junexis_studio_tests_");
        try
        {
            var filePath = Path.Combine(tmp.FullName, "a.txt");
            File.WriteAllText(filePath, "x");

            var workspace = new WorkspaceService();
            workspace.OpenFolder(tmp.FullName);

            var bus = new EventBus();
            var output = new OutputService();

            FileOpenRequested? received = null;
            bus.Subscribe<FileOpenRequested>(e => received = e);

            var sut = new ExplorerPanelViewModel(workspace, bus, output, versionControl: null);

            var fileNode = FindNodeByPath(sut.Nodes, filePath);
            Assert.NotNull(fileNode);

            sut.RequestOpen(fileNode!);

            Assert.NotNull(received);
            Assert.Equal(filePath, received!.Path);
        }
        finally
        {
            try
            {
                tmp.Delete(recursive: true);
            }
            catch
            {
                // best-effort
            }
        }
    }

    private static ExplorerNode? FindNodeByPath(IReadOnlyList<ExplorerNode> nodes, string fullPath)
    {
        foreach (var node in nodes)
        {
            if (string.Equals(node.FullPath, fullPath, StringComparison.OrdinalIgnoreCase))
            {
                return node;
            }

            var found = FindNodeByPath(node.Children, fullPath);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }
}

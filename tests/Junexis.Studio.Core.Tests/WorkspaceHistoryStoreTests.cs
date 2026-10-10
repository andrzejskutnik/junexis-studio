using Junexis.Studio.Core;
using Xunit;

namespace Junexis.Studio.Core.Tests;

/// <summary>
/// Testy persystencji ostatnio otwartego folderu workspace.
/// </summary>
public sealed class WorkspaceHistoryStoreTests
{
    [Fact]
    public void LoadLastFolder_WithoutState_ReturnsNull()
    {
        var path = TempStatePath();

        Assert.Null(new WorkspaceHistoryStore(path).LoadLastFolder());
    }

    [Fact]
    public void SaveAndLoadLastFolder_RestoresExistingDirectory()
    {
        using var folder = new TemporaryDirectory();
        var statePath = Path.Combine(folder.Path, "state", "workspace-state.json");
        var workspacePath = Directory.CreateDirectory(Path.Combine(folder.Path, "workspace")).FullName;
        var store = new WorkspaceHistoryStore(statePath);

        store.SaveLastFolder(workspacePath);

        Assert.Equal(Path.GetFullPath(workspacePath), store.LoadLastFolder());
    }

    [Fact]
    public void LoadLastFolder_WhenDirectoryWasRemoved_ReturnsNull()
    {
        using var folder = new TemporaryDirectory();
        var statePath = Path.Combine(folder.Path, "workspace-state.json");
        var workspacePath = Directory.CreateDirectory(Path.Combine(folder.Path, "workspace")).FullName;
        var store = new WorkspaceHistoryStore(statePath);
        store.SaveLastFolder(workspacePath);
        Directory.Delete(workspacePath);

        Assert.Null(store.LoadLastFolder());
    }

    [Fact]
    public void LoadLastFolder_WithInvalidJson_ReturnsNull()
    {
        using var folder = new TemporaryDirectory();
        var statePath = Path.Combine(folder.Path, "workspace-state.json");
        File.WriteAllText(statePath, "{invalid");

        Assert.Null(new WorkspaceHistoryStore(statePath).LoadLastFolder());
    }

    [Fact]
    public void SaveLastFolder_KeepsMostRecentFolderFirstWithoutDuplicates()
    {
        using var folder = new TemporaryDirectory();
        var first = Directory.CreateDirectory(Path.Combine(folder.Path, "first")).FullName;
        var second = Directory.CreateDirectory(Path.Combine(folder.Path, "second")).FullName;
        var store = new WorkspaceHistoryStore(Path.Combine(folder.Path, "workspace-state.json"));

        store.SaveLastFolder(first);
        store.SaveLastFolder(second);
        store.SaveLastFolder(first);

        Assert.Equal(
            [Path.GetFullPath(first), Path.GetFullPath(second)],
            store.LoadRecentFolders());
    }

    [Fact]
    public void LoadRecentFolders_RemovesFoldersThatNoLongerExist()
    {
        using var folder = new TemporaryDirectory();
        var first = Directory.CreateDirectory(Path.Combine(folder.Path, "first")).FullName;
        var second = Directory.CreateDirectory(Path.Combine(folder.Path, "second")).FullName;
        var store = new WorkspaceHistoryStore(Path.Combine(folder.Path, "workspace-state.json"));
        store.SaveLastFolder(first);
        store.SaveLastFolder(second);
        Directory.Delete(second);

        Assert.Equal([Path.GetFullPath(first)], store.LoadRecentFolders());
        Assert.Equal(Path.GetFullPath(first), store.LoadLastFolder());
    }

    private static string TempStatePath() =>
        Path.Combine(Path.GetTempPath(), $"junexis-workspace-state-{Guid.NewGuid():N}.json");

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = Directory.CreateTempSubdirectory("junexis-workspace-history-").FullName;
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch
            {
                // Best effort cleanup.
            }
        }
    }
}

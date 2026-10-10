using Junexis.Studio.Host;

namespace Junexis.Studio.Host.Tests;

public sealed class WorkspaceWelcomeStateTests
{
    [Fact]
    public void Create_DescribesCurrentGitWorkspace()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, ".git"));

            var state = WorkspaceWelcomeState.Create(root, []);

            Assert.True(state.HasWorkspace);
            Assert.Equal(new DirectoryInfo(root).Name, state.WorkspaceName);
            Assert.Equal(Path.GetFullPath(root), state.WorkspacePath);
            Assert.True(state.IsGitRepository);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Create_FiltersMissingAndDuplicateRecentFolders()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var missing = Path.Combine(root, "missing");

            var state = WorkspaceWelcomeState.Create(
                workspaceRoot: null,
                [root, root, missing]);

            Assert.False(state.HasWorkspace);
            Assert.Equal("No folder open", state.WorkspaceName);
            Assert.Null(state.WorkspacePath);
            Assert.Single(state.RecentWorkspaces);
            Assert.Equal(Path.GetFullPath(root), state.RecentWorkspaces[0].FullPath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"junexis-welcome-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}

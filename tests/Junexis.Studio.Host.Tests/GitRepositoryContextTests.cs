using Junexis.Studio.Host;

namespace Junexis.Studio.Host.Tests;

public sealed class GitRepositoryContextTests
{
    [Fact]
    public void HasGitMarker_WithGitDirectory_ReturnsTrue()
    {
        using var root = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(root.Path, ".git"));

        Assert.True(GitRepositoryContext.HasGitMarker(root.Path));
    }

    [Fact]
    public void HasGitMarker_WithGitFile_ReturnsTrue()
    {
        using var root = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(root.Path, ".git"), "gitdir: ../repo/.git/worktrees/example");

        Assert.True(GitRepositoryContext.HasGitMarker(root.Path));
    }

    [Fact]
    public void HasGitMarker_WithoutMarker_ReturnsFalse()
    {
        using var root = new TemporaryDirectory();

        Assert.False(GitRepositoryContext.HasGitMarker(root.Path));
    }

    [Fact]
    public void HasGitMarker_MarkerOnlyInParent_ReturnsFalse()
    {
        using var root = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(root.Path, ".git"));
        var child = Directory.CreateDirectory(Path.Combine(root.Path, "src"));

        Assert.False(GitRepositoryContext.HasGitMarker(child.FullName));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = Directory.CreateTempSubdirectory("junexis-git-context-").FullName;
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

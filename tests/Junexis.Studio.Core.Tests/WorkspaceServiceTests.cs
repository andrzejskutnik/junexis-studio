using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;
using Xunit;

namespace Junexis.Studio.Core.Tests;

public sealed class WorkspaceServiceTests
{
    [Fact]
    [Trait("Issue", "10")]
    public void OpenFolder_WithEmptyPath_Throws()
    {
        var sut = new WorkspaceService();
        Assert.Throws<ArgumentException>(() => sut.OpenFolder(" "));
    }

    [Fact]
    [Trait("Issue", "10")]
    public void OpenFolder_WithMissingFolder_Throws()
    {
        var sut = new WorkspaceService();
        var missing = Path.Combine(Path.GetTempPath(), "junexis-studio-missing-" + Guid.NewGuid());
        Assert.Throws<DirectoryNotFoundException>(() => sut.OpenFolder(missing));
    }

    [Fact]
    [Trait("Issue", "10")]
    public void OpenFolder_WithValidFolder_SetsRootPathToFullPath()
    {
        var sut = new WorkspaceService();
        var tempDir = CreateTempDir();

        try
        {
            sut.OpenFolder(tempDir);
            Assert.Equal(Path.GetFullPath(tempDir), sut.RootPath);
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    [Trait("Issue", "10")]
    public void OpenFolder_WithValidFolder_FiresFolderOpened()
    {
        var sut = new WorkspaceService();
        var tempDir = CreateTempDir();

        try
        {
            string? opened = null;
            sut.FolderOpened += p => opened = p;

            sut.OpenFolder(tempDir);

            Assert.Equal(Path.GetFullPath(tempDir), opened);
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void OpenFolder_WithEventBus_PublishesWorkspaceOpenedEvent()
    {
        var bus = new EventBus();
        using var sut = new WorkspaceService(bus);
        var tempDir = CreateTempDir();
        WorkspaceOpenedEvent? published = null;
        bus.Subscribe<WorkspaceOpenedEvent>(value => published = value);

        try
        {
            sut.OpenFolder(tempDir);

            Assert.NotNull(published);
            Assert.Equal(Path.GetFullPath(tempDir), published!.RootPath);
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    [Trait("Issue", "10")]
    public void ReadFile_WithRelativePathBeforeOpenFolder_Throws()
    {
        var sut = new WorkspaceService();
        Assert.Throws<InvalidOperationException>(() => sut.ReadFile("a.txt"));
    }

    [Fact]
    [Trait("Issue", "10")]
    public void WriteFile_WithRelativePathBeforeOpenFolder_Throws()
    {
        var sut = new WorkspaceService();
        Assert.Throws<InvalidOperationException>(() => sut.WriteFile("a.txt", "x"));
    }

    [Fact]
    [Trait("Issue", "10")]
    public void ReadFile_WithAbsolutePath_ReadsAndFiresFileOpened()
    {
        var sut = new WorkspaceService();
        var tempDir = CreateTempDir();
        var filePath = Path.Combine(tempDir, "a.txt");
        File.WriteAllText(filePath, "hello");

        try
        {
            string? opened = null;
            sut.FileOpened += p => opened = p;

            var content = sut.ReadFile(filePath);

            Assert.Equal("hello", content);
            Assert.Equal(Path.GetFullPath(filePath), opened);
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    [Trait("Issue", "45")]
    public void ReadFile_WithPathTraversalOutsideRoot_Throws()
    {
        var sut = new WorkspaceService();
        var root = CreateTempDir();
        var outside = CreateTempDir();

        try
        {
            var outsideFile = Path.Combine(outside, "outside.txt");
            File.WriteAllText(outsideFile, "nope");

            sut.OpenFolder(root);

            var traversal = Path.Combine("..", Path.GetFileName(outside), "outside.txt");
            Assert.Throws<UnauthorizedAccessException>(() => sut.ReadFile(traversal));
        }
        finally
        {
            TryDeleteDirectory(root);
            TryDeleteDirectory(outside);
        }
    }

    [Fact]
    [Trait("Issue", "45")]
    public void ReadFile_WithAbsolutePathOutsideRootAfterOpenFolder_Throws()
    {
        var sut = new WorkspaceService();
        var root = CreateTempDir();
        var outside = CreateTempDir();

        try
        {
            var outsideFile = Path.Combine(outside, "outside.txt");
            File.WriteAllText(outsideFile, "nope");

            sut.OpenFolder(root);
            Assert.Throws<UnauthorizedAccessException>(() => sut.ReadFile(outsideFile));
        }
        finally
        {
            TryDeleteDirectory(root);
            TryDeleteDirectory(outside);
        }
    }

    [Fact]
    [Trait("Issue", "45")]
    public void ReadFile_WithAbsolutePathInsideRootAfterOpenFolder_Works()
    {
        var sut = new WorkspaceService();
        var root = CreateTempDir();

        try
        {
            var filePath = Path.Combine(root, "a.txt");
            File.WriteAllText(filePath, "ok");

            sut.OpenFolder(root);
            var content = sut.ReadFile(filePath);

            Assert.Equal("ok", content);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    [Trait("Issue", "10")]
    public void WriteFile_NormalizesNullContentToEmptyString()
    {
        var sut = new WorkspaceService();
        var tempDir = CreateTempDir();

        try
        {
            sut.OpenFolder(tempDir);
            sut.WriteFile("a.txt", null!);

            var content = File.ReadAllText(Path.Combine(tempDir, "a.txt"));
            Assert.Equal(string.Empty, content);
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    [Trait("Issue", "10")]
    public void WriteFile_ThenReadFile_WorksWithRelativePaths()
    {
        var sut = new WorkspaceService();
        var tempDir = CreateTempDir();

        try
        {
            sut.OpenFolder(tempDir);

            string? opened = null;
            sut.FileOpened += p => opened = p;

            sut.WriteFile("a.txt", "hello");
            var content = sut.ReadFile("a.txt");

            Assert.Equal("hello", content);
            Assert.Equal(Path.GetFullPath(Path.Combine(tempDir, "a.txt")), opened);
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    [Trait("Issue", "10")]
    public void WriteFile_FiresFileSaved_WithFullPath()
    {
        var sut = new WorkspaceService();
        var tempDir = CreateTempDir();

        try
        {
            sut.OpenFolder(tempDir);

            string? saved = null;
            sut.FileSaved += p => saved = p;

            sut.WriteFile("a.txt", "hello");

            Assert.Equal(Path.GetFullPath(Path.Combine(tempDir, "a.txt")), saved);
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    [Trait("Issue", "43")]
    public void ExternalFileChange_PublishesFileChangedExternally()
    {
        var tempDir = CreateTempDir();
        var filePath = Path.Combine(tempDir, "a.txt");
        File.WriteAllText(filePath, "v1");

        try
        {
            var bus = new EventBus();
            using (var sut = new WorkspaceService(bus))
            {
                sut.OpenFolder(tempDir);

                var changed = 0;
                bus.Subscribe<FileChangedExternally>(e =>
                {
                    if (string.Equals(e.Path, Path.GetFullPath(filePath), StringComparison.OrdinalIgnoreCase))
                    {
                        changed++;
                    }
                });

                Thread.Sleep(50);
                File.AppendAllText(filePath, "x");

                var received = SpinWait.SpinUntil(() => changed > 0, TimeSpan.FromSeconds(2));
                Assert.True(received, "Expected FileChangedExternally to be published.");
            }
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    [Trait("Issue", "44")]
    public void Workspace_CanUseInjectedFileSystem()
    {
        var root = Path.Combine(Path.GetTempPath(), "junexis-studio-fs-" + Guid.NewGuid());
        var fs = new FakeFileSystem();

        var sut = new WorkspaceService(fileSystem: fs, eventBus: null);
        sut.OpenFolder(root);

        sut.WriteFile("a.txt", "hello");
        var content = sut.ReadFile("a.txt");

        Assert.Equal("hello", content);
        Assert.True(fs.Writes.Count >= 1);
        Assert.Contains(Path.Combine(Path.GetFullPath(root), "a.txt"), fs.Writes.Keys);
    }

    [Fact]
    [Trait("Issue", "46")]
    public void GetWorkspaceConfigRaw_WhenWorkspaceNotOpened_ReturnsNull()
    {
        var sut = new WorkspaceService();

        var raw = sut.GetWorkspaceConfigRaw();

        Assert.Null(raw);
    }

    [Fact]
    [Trait("Issue", "46")]
    public void GetWorkspaceConfigRaw_WhenConfigMissing_ReturnsNull()
    {
        var sut = new WorkspaceService();
        var tempDir = CreateTempDir();

        try
        {
            sut.OpenFolder(tempDir);

            var raw = sut.GetWorkspaceConfigRaw();

            Assert.Null(raw);
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    [Trait("Issue", "46")]
    public void SetWorkspaceConfigRaw_ThenGetWorkspaceConfigRaw_RoundTrips()
    {
        var sut = new WorkspaceService();
        var tempDir = CreateTempDir();

        try
        {
            sut.OpenFolder(tempDir);
            sut.SetWorkspaceConfigRaw("{\"k\":1}");

            var raw = sut.GetWorkspaceConfigRaw();

            Assert.Equal("{\"k\":1}", raw);
            Assert.Equal("{\"k\":1}", File.ReadAllText(Path.Combine(tempDir, WorkspaceConfig.FileName)));
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    private sealed class FakeFileSystem : IFileSystem
    {
        public Dictionary<string, string> Writes { get; } = new(StringComparer.OrdinalIgnoreCase);

        public string ReadFile(string path)
        {
            if (Writes.TryGetValue(path, out var content))
            {
                return content;
            }

            return string.Empty;
        }

        public void WriteFile(string path, string content)
        {
            Writes[path] = content ?? string.Empty;
        }

        public bool Exists(string path) => true;

        public IEnumerable<string> EnumerateDirectory(string path) => Array.Empty<string>();

        public IDisposable Watch(string rootPath, bool includeSubdirectories, Action<FileSystemChange> onChange)
            => new DummyDisposable();

        private sealed class DummyDisposable : IDisposable
        {
            public void Dispose() { }
        }
    }

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "junexis-studio-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup.
        }
    }
}

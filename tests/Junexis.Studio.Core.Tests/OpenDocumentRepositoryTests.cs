using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;
using Xunit;

namespace Junexis.Studio.Core.Tests;

public sealed class OpenDocumentRepositoryTests
{
    [Fact]
    [Trait("Issue", "41")]
    public void TracksDocumentOpenedChangedAndSaved()
    {
        var tmp = Directory.CreateTempSubdirectory("junexis_studio_docs_");
        try
        {
            var filePath = Path.Combine(tmp.FullName, "a.txt");
            File.WriteAllText(filePath, "hello");

            var workspace = new WorkspaceService();
            workspace.OpenFolder(tmp.FullName);

            var bus = new EventBus();
            using var repo = new OpenDocumentRepository(bus, workspace);

            bus.Publish(new DocumentOpened(filePath, "hello"));

            Assert.True(repo.TryGetByPath(filePath, out var opened));
            Assert.Equal(filePath, opened.FilePath);
            Assert.Equal("hello", opened.Content);
            Assert.False(opened.IsDirty);
            Assert.NotEqual(default, opened.Id);

            bus.Publish(new DocumentChanged(filePath, "changed"));

            Assert.True(repo.TryGetByPath(filePath, out var changed));
            Assert.Equal("changed", changed.Content);
            Assert.True(changed.IsDirty);
            Assert.Equal(opened.Id, changed.Id);

            bus.Publish(new DocumentSaved(filePath));

            Assert.True(repo.TryGetByPath(filePath, out var saved));
            Assert.False(saved.IsDirty);
            Assert.Equal("changed", saved.Content);
            Assert.Equal(opened.Id, saved.Id);
            Assert.NotNull(saved.LastSavedTimestamp);
        }
        finally
        {
            try { tmp.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    [Trait("Issue", "41")]
    public void FileChangedExternally_WhenNotDirty_RefreshesContentFromDisk()
    {
        var tmp = Directory.CreateTempSubdirectory("junexis_studio_docs_");
        try
        {
            var filePath = Path.Combine(tmp.FullName, "a.txt");
            File.WriteAllText(filePath, "v1");

            var workspace = new WorkspaceService();
            workspace.OpenFolder(tmp.FullName);

            var bus = new EventBus();
            using var repo = new OpenDocumentRepository(bus, workspace);

            bus.Publish(new DocumentOpened(filePath, "v1"));

            File.WriteAllText(filePath, "v2");
            bus.Publish(new FileChangedExternally(filePath));

            Assert.True(repo.TryGetByPath(filePath, out var doc));
            Assert.Equal("v2", doc.Content);
            Assert.False(doc.IsDirty);
        }
        finally
        {
            try { tmp.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    [Trait("Issue", "41")]
    public void FileChangedExternally_WhenDirty_DoesNotOverwriteContent()
    {
        var tmp = Directory.CreateTempSubdirectory("junexis_studio_docs_");
        try
        {
            var filePath = Path.Combine(tmp.FullName, "a.txt");
            File.WriteAllText(filePath, "v1");

            var workspace = new WorkspaceService();
            workspace.OpenFolder(tmp.FullName);

            var bus = new EventBus();
            using var repo = new OpenDocumentRepository(bus, workspace);

            bus.Publish(new DocumentOpened(filePath, "v1"));
            bus.Publish(new DocumentChanged(filePath, "local"));

            File.WriteAllText(filePath, "external");
            bus.Publish(new FileChangedExternally(filePath));

            Assert.True(repo.TryGetByPath(filePath, out var doc));
            Assert.Equal("local", doc.Content);
            Assert.True(doc.IsDirty);
        }
        finally
        {
            try { tmp.Delete(recursive: true); } catch { }
        }
    }
}

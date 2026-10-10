using Junexis.Studio.Core;
using Xunit;

namespace Junexis.Studio.Core.Tests;

public sealed class ExtensionPathsTests
{
    [Fact]
    [Trait("Issue", "30")]
    public void GetExtensionsRoot_ReturnsExpectedPath()
    {
        var root = ExtensionPaths.GetExtensionsRoot("/app");
        Assert.EndsWith("extensions", root);
    }

    [Fact]
    [Trait("Issue", "30")]
    public void GetManifestPath_ReturnsExpectedPath()
    {
        var path = ExtensionPaths.GetManifestPath("/app", "my-ext");
        Assert.Contains("my-ext", path);
        Assert.EndsWith("extension.json", path);
    }

    [Fact]
    [Trait("Issue", "30")]
    public void GetManifestPath_EmptyFolder_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            ExtensionPaths.GetManifestPath("/app", ""));
    }

    [Fact]
    [Trait("Issue", "30")]
    public void EnumerateExtensionFolders_NonExistentRoot_ReturnsEmpty()
    {
        var folders = ExtensionPaths.EnumerateExtensionFolders("/nonexistent_" + Guid.NewGuid());
        Assert.Empty(folders);
    }

    [Fact]
    [Trait("Issue", "30")]
    public void EnumerateExtensionFolders_WithSubfolders_ReturnsThem()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "junexis-test-" + Guid.NewGuid());
        var extRoot = Path.Combine(tmp, "extensions");
        Directory.CreateDirectory(Path.Combine(extRoot, "ext1"));
        Directory.CreateDirectory(Path.Combine(extRoot, "ext2"));

        try
        {
            var folders = ExtensionPaths.EnumerateExtensionFolders(tmp);
            Assert.Equal(2, folders.Count);
        }
        finally
        {
            Directory.Delete(tmp, true);
        }
    }

    [Fact]
    [Trait("Issue", "30")]
    public void FolderName_IsExtensions()
    {
        Assert.Equal("extensions", ExtensionPaths.FolderName);
    }

    [Fact]
    [Trait("Issue", "30")]
    public void ManifestFileName_IsExtensionJson()
    {
        Assert.Equal("extension.json", ExtensionPaths.ManifestFileName);
    }
}

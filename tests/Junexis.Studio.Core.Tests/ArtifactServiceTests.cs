using Junexis.Studio.Core;

namespace Junexis.Studio.Core.Tests;

public sealed class ArtifactServiceTests
{
    [Fact]
    public async Task LoadAsync_ValidInventory_ResolvesPathsWithinOutput()
    {
        var root = CreateRoot();
        try
        {
            var manifest = Path.Combine(root, "junexis.artifacts.json");
            await File.WriteAllTextAsync(manifest,
                """
                {
                  "schemaVersion": "1.0",
                  "buildId": "build-1",
                  "artifacts": [
                    {
                      "path": "server/main.go",
                      "target": "server",
                      "kind": "source",
                      "ownership": "generated",
                      "sha256": "abc"
                    }
                  ]
                }
                """);

            var result = await new ArtifactService().LoadAsync(manifest);

            Assert.Equal("build-1", result.BuildId);
            var item = Assert.Single(result.Items);
            Assert.Equal("server/main.go", item.RelativePath);
            Assert.Equal(Path.Combine(root, "server", "main.go"), item.FullPath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("../secret.txt")]
    [InlineData("C:/secret.txt")]
    public async Task LoadAsync_UnsafePath_IsRejected(string artifactPath)
    {
        var root = CreateRoot();
        try
        {
            var manifest = Path.Combine(root, "junexis.artifacts.json");
            await File.WriteAllTextAsync(manifest,
                $$"""
                {
                  "schemaVersion": "1.0",
                  "buildId": "build-1",
                  "artifacts": [
                    {
                      "path": "{{artifactPath.Replace("\\", "\\\\")}}",
                      "target": "server",
                      "kind": "source",
                      "ownership": "generated",
                      "sha256": "abc"
                    }
                  ]
                }
                """);

            await Assert.ThrowsAsync<InvalidDataException>(
                () => new ArtifactService().LoadAsync(manifest));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"junexis-artifacts-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }
}

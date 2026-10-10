using System.Text.Json;
using Junexis.Studio.Core;

namespace Junexis.Studio.Core.Tests;

public sealed class LatestBuildReceiptServiceTests
{
    [Fact]
    [Trait("Issue", "31.2")]
    public async Task LoadAsync_ValidProjectReceipt_ReturnsCanonicalBuildState()
    {
        var fixture = await ReceiptFixture.CreateAsync("project");
        try
        {
            var receipt = await new LatestBuildReceiptService().LoadAsync(fixture.ReceiptPath);

            Assert.Equal(fixture.BuildId, receipt.BuildId);
            Assert.Equal("project", receipt.Mode);
            Assert.Equal(fixture.Root, receipt.ProjectRoot);
            Assert.Equal(fixture.ArtifactManifest, receipt.ArtifactManifestPath);
            Assert.Equal(["server"], receipt.Targets);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    [Trait("Issue", "31.2")]
    public async Task TryLoadForContextAsync_ProjectWorkspace_RestoresDeterministicReceipt()
    {
        var fixture = await ReceiptFixture.CreateAsync("project");
        try
        {
            var receipt = await new LatestBuildReceiptService().TryLoadForContextAsync(
                fixture.Root,
                fixture.EntryFile);

            Assert.NotNull(receipt);
            Assert.Equal(fixture.ReceiptPath, receipt!.ReceiptPath);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    [Trait("Issue", "31.2")]
    public async Task TryLoadForContextAsync_FileMode_RestoresIsolatedEntryReceipt()
    {
        var fixture = await ReceiptFixture.CreateAsync("file");
        try
        {
            var receipt = await new LatestBuildReceiptService().TryLoadForContextAsync(
                fixture.Root,
                fixture.EntryFile);

            Assert.NotNull(receipt);
            Assert.Equal("file", receipt!.Mode);
            Assert.Contains(
                Path.Combine(".junexis", "state", "main", "latest-build.json"),
                receipt.ReceiptPath);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    [Trait("Issue", "31.2")]
    public async Task LoadAsync_ArtifactOutsideOutputRoot_IsRejected()
    {
        var fixture = await ReceiptFixture.CreateAsync("project", artifactOutsideOutput: true);
        try
        {
            var error = await Assert.ThrowsAsync<InvalidDataException>(
                () => new LatestBuildReceiptService().LoadAsync(fixture.ReceiptPath));

            Assert.Contains("artifactManifest escapes", error.Message);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    [Trait("Issue", "31.2")]
    public async Task LoadAsync_AnalysisFromDifferentBuild_IsRejected()
    {
        var fixture = await ReceiptFixture.CreateAsync("project");
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(fixture.Root, "out", "analysis", "junexis.analysis.json"),
                JsonSerializer.Serialize(new { buildId = new string('f', 64) }));

            var error = await Assert.ThrowsAsync<InvalidDataException>(
                () => new LatestBuildReceiptService().LoadAsync(fixture.ReceiptPath));

            Assert.Contains("analysisSnapshot buildId does not match", error.Message);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    private sealed record ReceiptFixture(
        string Root,
        string EntryFile,
        string ReceiptPath,
        string ArtifactManifest,
        string BuildId) : IDisposable
    {
        public static async Task<ReceiptFixture> CreateAsync(
            string mode,
            bool artifactOutsideOutput = false)
        {
            var root = Path.GetFullPath(Path.Combine(
                Path.GetTempPath(),
                $"junexis-studio-receipt-{Guid.NewGuid():N}"));
            var entry = Path.Combine(root, "src", "main.jnx");
            var output = Path.Combine(root, "out");
            Directory.CreateDirectory(Path.GetDirectoryName(entry)!);
            Directory.CreateDirectory(Path.Combine(output, "debug"));
            Directory.CreateDirectory(Path.Combine(output, "analysis"));
            await File.WriteAllTextAsync(entry, "task Main() @server {}");
            if (mode == "project")
                await File.WriteAllTextAsync(Path.Combine(root, "junexis.project.toml"), "entry = \"src/main.jnx\"");

            var bundle = Path.Combine(output, "junexis.bundle.json");
            var artifact = artifactOutsideOutput
                ? entry
                : Path.Combine(output, "junexis.artifacts.json");
            var debug = Path.Combine(output, "debug", "junexis.debug.toml");
            var analysis = Path.Combine(output, "analysis", "junexis.analysis.json");
            var buildId = new string('a', 64);
            var identityJson = JsonSerializer.Serialize(new { buildId });
            await File.WriteAllTextAsync(bundle, identityJson);
            await File.WriteAllTextAsync(debug, "{}");
            await File.WriteAllTextAsync(analysis, identityJson);
            if (!artifactOutsideOutput)
                await File.WriteAllTextAsync(artifact, identityJson);

            var receiptPath = mode == "project"
                ? Path.Combine(root, ".junexis", "state", "latest-build.json")
                : Path.Combine(
                    Path.GetDirectoryName(entry)!,
                    ".junexis",
                    "state",
                    "main",
                    "latest-build.json");
            Directory.CreateDirectory(Path.GetDirectoryName(receiptPath)!);
            var projectRoot = mode == "project" ? root : Path.GetDirectoryName(entry)!;
            var json = JsonSerializer.Serialize(new
            {
                schemaVersion = "1.0",
                buildId,
                mode,
                projectRoot,
                entryFile = entry,
                outputRoot = output,
                targets = new[] { "server" },
                bundleManifest = bundle,
                artifactManifest = artifact,
                debugManifest = debug,
                analysisSnapshot = analysis,
            });
            await File.WriteAllTextAsync(receiptPath, json);
            return new ReceiptFixture(root, entry, receiptPath, artifact, buildId);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
    }
}

using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;
using Xunit;

namespace Junexis.Studio.Core.Tests;

[Trait("Issue", "34")]
public sealed class ExtensionDiscoveryServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly OutputService _output;

    public ExtensionDiscoveryServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"junexis_discovery_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _output = new OutputService();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    [Fact]
    public void Discover_NoExtensionsDir_ReturnsEmpty()
    {
        var svc = new ExtensionDiscoveryService(_output);

        var results = svc.Discover(_tempDir);

        Assert.Empty(results);
        var logs = _output.GetSnapshot();
        Assert.Contains(logs, e => e.Message.Contains("No extension folders found"));
    }

    [Fact]
    public void Discover_EmptyExtensionsDir_ReturnsEmpty()
    {
        Directory.CreateDirectory(Path.Combine(_tempDir, "extensions"));
        var svc = new ExtensionDiscoveryService(_output);

        var results = svc.Discover(_tempDir);

        Assert.Empty(results);
    }

    [Fact]
    public void Discover_FolderWithoutManifest_Rejected()
    {
        var extDir = Path.Combine(_tempDir, "extensions", "my-ext");
        Directory.CreateDirectory(extDir);
        var svc = new ExtensionDiscoveryService(_output);

        var results = svc.Discover(_tempDir);

        Assert.Single(results);
        Assert.False(results[0].Accepted);
        Assert.Contains("Missing extension.json", results[0].RejectReason);
    }

    [Fact]
    public void Discover_ValidManifest_Accepted()
    {
        CreateManifest("hello-ext", """
        {
            "id": "hello-ext",
            "version": "0.1.0",
            "entryType": "Hello.Extension"
        }
        """);
        var svc = new ExtensionDiscoveryService(_output);

        var results = svc.Discover(_tempDir);

        Assert.Single(results);
        Assert.True(results[0].Accepted);
        Assert.NotNull(results[0].Manifest);
        Assert.Equal("hello-ext", results[0].Manifest!.Id);
    }

    [Fact]
    public void Discover_InvalidJson_Rejected()
    {
        CreateManifest("broken-ext", "NOT JSON");
        var svc = new ExtensionDiscoveryService(_output);

        var results = svc.Discover(_tempDir);

        Assert.Single(results);
        Assert.False(results[0].Accepted);
        Assert.Contains("Invalid JSON", results[0].RejectReason);
    }

    [Fact]
    public void Discover_MissingRequiredFields_Rejected()
    {
        CreateManifest("no-id", """{ "version": "0.1.0", "entryType": "X.Y" }""");
        var svc = new ExtensionDiscoveryService(_output);

        var results = svc.Discover(_tempDir);

        Assert.Single(results);
        Assert.False(results[0].Accepted);
        Assert.Contains("Missing required field: Id", results[0].RejectReason);
    }

    [Fact]
    public void Discover_IdeVersionTooLow_Rejected()
    {
        CreateManifest("new-ext", """
        {
            "id": "new-ext",
            "version": "2.0.0",
            "entryType": "New.Extension",
            "minimalIdeVersion": "5.0.0"
        }
        """);
        var svc = new ExtensionDiscoveryService(_output, currentIdeVersion: "0.10.0");

        var results = svc.Discover(_tempDir);

        Assert.Single(results);
        Assert.False(results[0].Accepted);
        Assert.Contains("below required", results[0].RejectReason);
    }

    [Fact]
    public void Discover_IdeVersionOk_Accepted()
    {
        CreateManifest("ok-ext", """
        {
            "id": "ok-ext",
            "version": "0.1.0",
            "entryType": "Ok.Extension",
            "minimalIdeVersion": "0.1.0"
        }
        """);
        var svc = new ExtensionDiscoveryService(_output, currentIdeVersion: "0.2.0");

        var results = svc.Discover(_tempDir);

        Assert.Single(results);
        Assert.True(results[0].Accepted);
    }

    [Fact]
    public void Discover_MultipleExtensions_MixedResults()
    {
        CreateManifest("good", """
        {
            "id": "good",
            "version": "0.1.0",
            "entryType": "Good.Extension"
        }
        """);
        CreateManifest("bad", "INVALID");
        CreateManifest("missing", null); // no manifest file

        var svc = new ExtensionDiscoveryService(_output);

        var results = svc.Discover(_tempDir);

        Assert.Equal(3, results.Count);
        Assert.Equal(1, results.Count(r => r.Accepted));
        Assert.Equal(2, results.Count(r => !r.Accepted));
    }

    [Fact]
    public void Discover_ReportsToOutput()
    {
        CreateManifest("ext-a", """
        {
            "id": "ext-a",
            "version": "0.1.0",
            "entryType": "A.Extension"
        }
        """);
        var svc = new ExtensionDiscoveryService(_output);

        svc.Discover(_tempDir);

        var logs = _output.GetSnapshot();
        Assert.Contains(logs, e => e.Message.Contains("Discovered extension 'ext-a'"));
        Assert.Contains(logs, e => e.Message.Contains("Discovery complete"));
    }

    private void CreateManifest(string folderName, string? json)
    {
        var dir = Path.Combine(_tempDir, "extensions", folderName);
        Directory.CreateDirectory(dir);

        if (json is not null)
        {
            File.WriteAllText(Path.Combine(dir, "extension.json"), json);
        }
    }
}

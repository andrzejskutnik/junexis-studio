using Junexis.Studio.Core;
using Xunit;

namespace Junexis.Studio.Core.Tests;

public sealed class ExtensionManifestParserTests
{
    private const string ValidJson = """
        {
            "Id": "ext.hello",
            "Version": "0.1.0",
            "EntryType": "MyNs.HelloExtension",
            "MinimalIdeVersion": "0.1.0",
            "ExposedCapabilities": ["output", "commands"]
        }
        """;

    [Fact]
    [Trait("Issue", "32")]
    public void Parse_ValidManifest_ReturnsManifest()
    {
        var result = ExtensionManifestParser.Parse(ValidJson, "0.1.0");

        Assert.NotNull(result.Manifest);
        Assert.Empty(result.Errors);
        Assert.Equal("ext.hello", result.Manifest!.Id);
        Assert.Equal("0.1.0", result.Manifest.Version);
        Assert.Equal("MyNs.HelloExtension", result.Manifest.EntryType);
        Assert.Equal("0.1.0", result.Manifest.MinimalIdeVersion);
        Assert.Equal(2, result.Manifest.ExposedCapabilities!.Count);
    }

    [Fact]
    [Trait("Issue", "32")]
    public void Parse_EmptyJson_ReturnsError()
    {
        var result = ExtensionManifestParser.Parse("");

        Assert.Null(result.Manifest);
        Assert.Single(result.Errors);
        Assert.Contains("empty", result.Errors[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Issue", "32")]
    public void Parse_InvalidJson_ReturnsError()
    {
        var result = ExtensionManifestParser.Parse("{not valid json}");

        Assert.Null(result.Manifest);
        Assert.Single(result.Errors);
        Assert.Contains("Invalid JSON", result.Errors[0]);
    }

    [Fact]
    [Trait("Issue", "32")]
    public void Parse_MissingRequiredFields_ReturnsErrors()
    {
        var result = ExtensionManifestParser.Parse("{}");

        Assert.Null(result.Manifest);
        Assert.Equal(3, result.Errors.Count); // Id, Version, EntryType
    }

    [Fact]
    [Trait("Issue", "32")]
    public void Parse_IdeVersionTooLow_ReturnsError()
    {
        var result = ExtensionManifestParser.Parse(ValidJson, "0.0.1");

        Assert.Null(result.Manifest);
        Assert.Single(result.Errors);
        Assert.Contains("below required", result.Errors[0]);
    }

    [Fact]
    [Trait("Issue", "32")]
    public void Parse_NoMinimalIdeVersion_SkipsCheck()
    {
        const string json = """
            {
                "Id": "ext.simple",
                "Version": "0.1.0",
                "EntryType": "MyNs.SimpleExtension"
            }
            """;

        var result = ExtensionManifestParser.Parse(json, "0.0.1");

        Assert.NotNull(result.Manifest);
        Assert.Empty(result.Errors);
        Assert.Null(result.Manifest!.MinimalIdeVersion);
    }

    [Fact]
    [Trait("Issue", "32")]
    public void Parse_IsCaseInsensitive()
    {
        const string json = """
            {
                "id": "ext.ci",
                "version": "0.1.0",
                "entrytype": "Ns.CiExt"
            }
            """;

        var result = ExtensionManifestParser.Parse(json);

        Assert.NotNull(result.Manifest);
        Assert.Empty(result.Errors);
        Assert.Equal("ext.ci", result.Manifest!.Id);
    }
}

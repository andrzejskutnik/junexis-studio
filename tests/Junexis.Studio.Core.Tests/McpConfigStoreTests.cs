using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;
using Xunit;

namespace Junexis.Studio.Core.Tests;

/// <summary>
/// Tests for <see cref="McpConfigStore"/>, <see cref="McpConnectorConfig"/>
/// and <see cref="IMcpConfigStore"/>.
/// </summary>
[Trait("Issue", "62")]
public class McpConfigStoreTests
{
    private static McpConnectorConfig Cfg(string id, string ep = "http://localhost:8080")
        => new(id, ep);

    [Fact]
    public void Get_ReturnsNull_WhenEmpty()
    {
        var store = new McpConfigStore();
        Assert.Null(store.Get("test"));
    }

    [Fact]
    public void SetGlobal_MakesConfigAvailable()
    {
        var store = new McpConfigStore();
        store.SetGlobal(Cfg("my-mcp", "http://host:1234"));

        var cfg = store.Get("my-mcp");
        Assert.NotNull(cfg);
        Assert.Equal("http://host:1234", cfg!.Endpoint);
    }

    [Fact]
    public void SetWorkspace_OverridesGlobal()
    {
        var store = new McpConfigStore();
        store.SetGlobal(Cfg("conn", "http://global"));
        store.SetWorkspace(Cfg("conn", "http://workspace"));

        var cfg = store.Get("conn");
        Assert.Equal("http://workspace", cfg!.Endpoint);
    }

    [Fact]
    public void RemoveWorkspaceOverride_FallsBackToGlobal()
    {
        var store = new McpConfigStore();
        store.SetGlobal(Cfg("conn", "http://global"));
        store.SetWorkspace(Cfg("conn", "http://workspace"));
        store.RemoveWorkspaceOverride("conn");

        var cfg = store.Get("conn");
        Assert.Equal("http://global", cfg!.Endpoint);
    }

    [Fact]
    public void RemoveWorkspaceOverride_NoOp_WhenMissing()
    {
        var store = new McpConfigStore();
        store.RemoveWorkspaceOverride("nope"); // should not throw
    }

    [Fact]
    public void Get_IsCaseInsensitive()
    {
        var store = new McpConfigStore();
        store.SetGlobal(Cfg("My-Mcp"));

        Assert.NotNull(store.Get("my-mcp"));
        Assert.NotNull(store.Get("MY-MCP"));
    }

    [Fact]
    public void GetAllConnectorIds_ReturnsBothTiers()
    {
        var store = new McpConfigStore();
        store.SetGlobal(Cfg("a"));
        store.SetWorkspace(Cfg("b"));

        var ids = store.GetAllConnectorIds();
        Assert.Contains("a", ids, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("b", ids, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetAllConnectorIds_NoDuplicates()
    {
        var store = new McpConfigStore();
        store.SetGlobal(Cfg("x"));
        store.SetWorkspace(Cfg("x"));

        var ids = store.GetAllConnectorIds();
        Assert.Single(ids);
    }

    [Fact]
    public void McpConnectorConfig_DefaultValues()
    {
        var cfg = new McpConnectorConfig("id", "http://ep");
        Assert.Equal("dev", cfg.Mode);
        Assert.Null(cfg.Token);
        Assert.Null(cfg.TransportParameters);
    }

    [Fact]
    public void McpConnectorConfig_WithToken()
    {
        var cfg = new McpConnectorConfig("id", "http://ep", Token: "secret", Mode: "prod");
        Assert.Equal("secret", cfg.Token);
        Assert.Equal("prod", cfg.Mode);
    }

    [Fact]
    public void McpConnectorConfig_WithTransportParams()
    {
        var tp = new Dictionary<string, string> { ["timeout"] = "30" };
        var cfg = new McpConnectorConfig("id", "http://ep", TransportParameters: tp);
        Assert.NotNull(cfg.TransportParameters);
        Assert.Equal("30", cfg.TransportParameters!["timeout"]);
    }

    [Fact]
    public void SetGlobal_Overwrites_PreviousGlobal()
    {
        var store = new McpConfigStore();
        store.SetGlobal(Cfg("id", "http://v1"));
        store.SetGlobal(Cfg("id", "http://v2"));

        Assert.Equal("http://v2", store.Get("id")!.Endpoint);
    }
}

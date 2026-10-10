using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;
using Xunit;

namespace Junexis.Studio.Core.Tests;

[Trait("Issue", "86")]
public sealed class ConsentStoreTests
{
    [Fact]
    public void GetDecision_Unknown_ByDefault()
    {
        var store = new ConsentStore();

        Assert.Equal(ConsentDecision.Unknown, store.GetDecision("ext-1", "fs.read"));
    }

    [Fact]
    public void SetDecision_Allow_ThenIsAllowed()
    {
        var store = new ConsentStore();

        store.SetDecision("ext-1", "fs.read", ConsentDecision.Allow);

        Assert.True(store.IsAllowed("ext-1", "fs.read"));
    }

    [Fact]
    public void SetDecision_Deny_ThenNotAllowed()
    {
        var store = new ConsentStore();

        store.SetDecision("ext-1", "fs.read", ConsentDecision.Deny);

        Assert.False(store.IsAllowed("ext-1", "fs.read"));
    }

    [Fact]
    public void IsAllowed_Unknown_ReturnsFalse()
    {
        var store = new ConsentStore();

        Assert.False(store.IsAllowed("ext-1", "fs.read"));
    }

    [Fact]
    public void SetDecision_Overwrite()
    {
        var store = new ConsentStore();

        store.SetDecision("ext-1", "fs.read", ConsentDecision.Allow);
        store.SetDecision("ext-1", "fs.read", ConsentDecision.Deny);

        Assert.Equal(ConsentDecision.Deny, store.GetDecision("ext-1", "fs.read"));
    }

    [Fact]
    public void GetDecisions_ReturnsAllForExtension()
    {
        var store = new ConsentStore();
        store.SetDecision("ext-1", "fs.read", ConsentDecision.Allow);
        store.SetDecision("ext-1", "process.exec", ConsentDecision.Deny);
        store.SetDecision("ext-2", "fs.read", ConsentDecision.Allow);

        var decisions = store.GetDecisions("ext-1");

        Assert.Equal(2, decisions.Count);
        Assert.Equal(ConsentDecision.Allow, decisions["fs.read"]);
        Assert.Equal(ConsentDecision.Deny, decisions["process.exec"]);
    }

    [Fact]
    public void ResetDecisions_ClearsForExtension()
    {
        var store = new ConsentStore();
        store.SetDecision("ext-1", "fs.read", ConsentDecision.Allow);
        store.SetDecision("ext-1", "process.exec", ConsentDecision.Allow);
        store.SetDecision("ext-2", "fs.read", ConsentDecision.Allow);

        store.ResetDecisions("ext-1");

        Assert.Equal(ConsentDecision.Unknown, store.GetDecision("ext-1", "fs.read"));
        Assert.Equal(ConsentDecision.Unknown, store.GetDecision("ext-1", "process.exec"));
        // ext-2 unaffected.
        Assert.True(store.IsAllowed("ext-2", "fs.read"));
    }

    [Fact]
    public void Decisions_CaseInsensitive()
    {
        var store = new ConsentStore();

        store.SetDecision("Ext-1", "FS.Read", ConsentDecision.Allow);

        Assert.True(store.IsAllowed("ext-1", "fs.read"));
    }

    [Fact]
    public void IndependentExtensions()
    {
        var store = new ConsentStore();

        store.SetDecision("ext-a", "fs.read", ConsentDecision.Allow);
        store.SetDecision("ext-b", "fs.read", ConsentDecision.Deny);

        Assert.True(store.IsAllowed("ext-a", "fs.read"));
        Assert.False(store.IsAllowed("ext-b", "fs.read"));
    }
}

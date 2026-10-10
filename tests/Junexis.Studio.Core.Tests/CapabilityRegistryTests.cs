using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;
using Xunit;

namespace Junexis.Studio.Core.Tests;

[Trait("Issue", "15")]
public sealed class CapabilityRegistryTests
{
    [Fact]
    public void Register_ThenGet_ReturnsInstance()
    {
        var reg = new CapabilityRegistry();
        var instance = new FakeService();

        reg.Register<IFakeCapability>(instance);

        var result = reg.Get<IFakeCapability>();
        Assert.Same(instance, result);
    }

    [Fact]
    public void TryGet_Registered_ReturnsTrue()
    {
        var reg = new CapabilityRegistry();
        reg.Register<IFakeCapability>(new FakeService());

        var found = reg.TryGet<IFakeCapability>(out var cap);

        Assert.True(found);
        Assert.NotNull(cap);
    }

    [Fact]
    public void TryGet_NotRegistered_ReturnsFalse()
    {
        var reg = new CapabilityRegistry();

        var found = reg.TryGet<IFakeCapability>(out var cap);

        Assert.False(found);
        Assert.Null(cap);
    }

    [Fact]
    public void Get_NotRegistered_Throws()
    {
        var reg = new CapabilityRegistry();

        Assert.Throws<InvalidOperationException>(() => reg.Get<IFakeCapability>());
    }

    [Fact]
    public void Has_Registered_ReturnsTrue()
    {
        var reg = new CapabilityRegistry();
        reg.Register<IFakeCapability>(new FakeService());

        Assert.True(reg.Has<IFakeCapability>());
    }

    [Fact]
    public void Has_NotRegistered_ReturnsFalse()
    {
        var reg = new CapabilityRegistry();

        Assert.False(reg.Has<IFakeCapability>());
    }

    [Fact]
    public void Register_OverwritesPrevious()
    {
        var reg = new CapabilityRegistry();
        var first = new FakeService();
        var second = new FakeService();

        reg.Register<IFakeCapability>(first);
        reg.Register<IFakeCapability>(second);

        Assert.Same(second, reg.Get<IFakeCapability>());
    }

    [Fact]
    public void Register_Null_Throws()
    {
        var reg = new CapabilityRegistry();

        Assert.Throws<ArgumentNullException>(() => reg.Register<IFakeCapability>(null!));
    }

    [Fact]
    public void MultipleCapabilities_Independent()
    {
        var reg = new CapabilityRegistry();
        var fake = new FakeService();
        var other = new OtherService();

        reg.Register<IFakeCapability>(fake);
        reg.Register<IOtherCapability>(other);

        Assert.Same(fake, reg.Get<IFakeCapability>());
        Assert.Same(other, reg.Get<IOtherCapability>());
    }

    // --- helpers ---

    private interface IFakeCapability { }
    private interface IOtherCapability { }
    private sealed class FakeService : IFakeCapability { }
    private sealed class OtherService : IOtherCapability { }
}

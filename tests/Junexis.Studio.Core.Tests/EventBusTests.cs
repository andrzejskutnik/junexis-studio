using System.Reflection;
using Junexis.Studio.Core;
using Xunit;

namespace Junexis.Studio.Core.Tests;

public sealed class EventBusTests
{
    [Fact]
    [Trait("Issue", "3")]
    public void Publish_WithNullEvent_Throws()
    {
        var sut = new EventBus();
        Assert.Throws<ArgumentNullException>(() => sut.Publish(null!));
    }

    [Fact]
    [Trait("Issue", "3")]
    public void Subscribe_WithNullHandler_Throws()
    {
        var sut = new EventBus();
        Assert.Throws<ArgumentNullException>(() => sut.Subscribe<int>(null!));
    }

    [Fact]
    [Trait("Issue", "3")]
    public void Unsubscribe_WithNullHandler_Throws()
    {
        var sut = new EventBus();
        Assert.Throws<ArgumentNullException>(() => sut.Unsubscribe<int>(null!));
    }

    [Fact]
    [Trait("Issue", "3")]
    public void Publish_WithNoSubscribers_DoesNothing()
    {
        var sut = new EventBus();

        sut.Publish(123);
    }

    [Fact]
    [Trait("Issue", "3")]
    public void Publish_InvokesSubscriberForRuntimeType()
    {
        var sut = new EventBus();
        var seen = 0;
        sut.Subscribe<int>(_ => seen++);

        sut.Publish(123);

        Assert.Equal(1, seen);
    }

    [Fact]
    [Trait("Issue", "3")]
    public void Publish_DoesNotInvokeSubscriberForBaseType()
    {
        var sut = new EventBus();
        var seen = 0;
        sut.Subscribe<object>(_ => seen++);

        sut.Publish(123);

        Assert.Equal(0, seen);
    }

    [Fact]
    [Trait("Issue", "3")]
    public void Publish_UsesSnapshot_WhenHandlersAreRemovedDuringCallback()
    {
        var sut = new EventBus();
        var h1Calls = 0;
        var h2Calls = 0;

        Action<int> handler2 = _ => h2Calls++;
        Action<int> handler1 = _ =>
        {
            h1Calls++;
            sut.Unsubscribe(handler2);
        };

        sut.Subscribe(handler1);
        sut.Subscribe(handler2);

        sut.Publish(1);
        sut.Publish(2);

        Assert.Equal(2, h1Calls);
        Assert.Equal(1, h2Calls);
    }

    [Fact]
    [Trait("Issue", "3")]
    public void Publish_WhenHandlerThrows_AndNoCallback_PropagatesException()
    {
        var sut = new EventBus();
        sut.Subscribe<int>(_ => throw new InvalidOperationException("boom"));

        var ex = Assert.Throws<TargetInvocationException>(() => sut.Publish(1));
        Assert.IsType<InvalidOperationException>(ex.InnerException);
    }

    [Fact]
    [Trait("Issue", "40")]
    public void Publish_WhenHandlerThrows_WithCallback_DoesNotPropagate()
    {
        Exception? captured = null;
        var sut = new EventBus(ex => captured = ex);
        sut.Subscribe<int>(_ => throw new InvalidOperationException("boom"));

        sut.Publish(1);

        Assert.NotNull(captured);
        Assert.IsType<InvalidOperationException>(captured);
        Assert.Equal("boom", captured!.Message);
    }

    [Fact]
    [Trait("Issue", "40")]
    public void Publish_WhenFirstHandlerThrows_SecondStillRuns()
    {
        var secondCalled = false;
        var sut = new EventBus(_ => { });
        sut.Subscribe<int>(_ => throw new InvalidOperationException("fail"));
        sut.Subscribe<int>(_ => secondCalled = true);

        sut.Publish(42);

        Assert.True(secondCalled);
    }
}

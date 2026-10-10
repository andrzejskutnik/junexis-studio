using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;
using Xunit;

namespace Junexis.Studio.Core.Tests;

[Trait("Issue", "85")]
public sealed class CircuitBreakerServiceTests
{
    private static Exception MakeException(string msg = "boom") => new InvalidOperationException(msg);

    [Fact]
    public void NewInstance_NoDisabled()
    {
        var cb = new CircuitBreakerService();

        Assert.Empty(cb.GetDisabledExtensions());
        Assert.False(cb.IsDisabled("ext-1"));
    }

    [Fact]
    public void RecordFailure_BelowThreshold_NotDisabled()
    {
        var cb = new CircuitBreakerService(threshold: 3);

        cb.RecordFailure("ext-1", MakeException());
        cb.RecordFailure("ext-1", MakeException());

        Assert.False(cb.IsDisabled("ext-1"));
    }

    [Fact]
    public void RecordFailure_AtThreshold_Disabled()
    {
        var cb = new CircuitBreakerService(threshold: 3);

        cb.RecordFailure("ext-1", MakeException());
        cb.RecordFailure("ext-1", MakeException());
        cb.RecordFailure("ext-1", MakeException());

        Assert.True(cb.IsDisabled("ext-1"));
        Assert.Contains("ext-1", cb.GetDisabledExtensions());
    }

    [Fact]
    public void RecordFailure_IndependentPerExtension()
    {
        var cb = new CircuitBreakerService(threshold: 2);

        cb.RecordFailure("ext-a", MakeException());
        cb.RecordFailure("ext-a", MakeException());
        cb.RecordFailure("ext-b", MakeException());

        Assert.True(cb.IsDisabled("ext-a"));
        Assert.False(cb.IsDisabled("ext-b"));
    }

    [Fact]
    public void Reset_ReenablesExtension()
    {
        var cb = new CircuitBreakerService(threshold: 1);

        cb.RecordFailure("ext-1", MakeException());
        Assert.True(cb.IsDisabled("ext-1"));

        cb.Reset("ext-1");
        Assert.False(cb.IsDisabled("ext-1"));
    }

    [Fact]
    public void Reset_AllowsNewFailuresAgain()
    {
        var cb = new CircuitBreakerService(threshold: 2);

        cb.RecordFailure("ext-1", MakeException());
        cb.RecordFailure("ext-1", MakeException());
        Assert.True(cb.IsDisabled("ext-1"));

        cb.Reset("ext-1");

        // Needs threshold failures again to disable.
        cb.RecordFailure("ext-1", MakeException());
        Assert.False(cb.IsDisabled("ext-1"));

        cb.RecordFailure("ext-1", MakeException());
        Assert.True(cb.IsDisabled("ext-1"));
    }

    [Fact]
    public void RecordFailure_OldFailuresExpire()
    {
        // Use a very short window so we can test expiration.
        var cb = new CircuitBreakerService(threshold: 3, window: TimeSpan.FromMilliseconds(50));

        cb.RecordFailure("ext-1", MakeException());
        cb.RecordFailure("ext-1", MakeException());

        // Wait for window to expire.
        Thread.Sleep(100);

        // These failures are fresh, count starts over.
        cb.RecordFailure("ext-1", MakeException());

        Assert.False(cb.IsDisabled("ext-1"));
    }

    [Fact]
    public void OnDisable_WritesToOutput()
    {
        var output = new OutputService();
        var cb = new CircuitBreakerService(threshold: 2, output: output);

        cb.RecordFailure("ext-1", MakeException());
        cb.RecordFailure("ext-1", MakeException());

        var logs = output.GetSnapshot();
        Assert.Contains(logs, e => e.Message.Contains("auto-disabled") && e.Message.Contains("ext-1"));
    }

    [Fact]
    public void OnDisable_PublishesSystemDiagnostic()
    {
        var eventBus = new EventBus();
        SystemDiagnostic? captured = null;
        eventBus.Subscribe<SystemDiagnostic>(d => captured = d);

        var cb = new CircuitBreakerService(threshold: 1, eventBus: eventBus);

        cb.RecordFailure("ext-1", MakeException());

        Assert.NotNull(captured);
        Assert.Equal(SystemDiagnosticCategory.ExtensionException, captured!.Category);
        Assert.Contains("auto-disabled", captured.Message);
    }

    [Fact]
    public void Threshold_LessThan1_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CircuitBreakerService(threshold: 0));
    }

    [Fact]
    public void IsDisabled_CaseInsensitive()
    {
        var cb = new CircuitBreakerService(threshold: 1);

        cb.RecordFailure("Ext-A", MakeException());

        Assert.True(cb.IsDisabled("ext-a"));
        Assert.True(cb.IsDisabled("EXT-A"));
    }
}

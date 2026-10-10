using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;
using Xunit;

namespace Junexis.Studio.Core.Tests;

/// <summary>
/// Tests for <see cref="McpContextPublisher"/>, <see cref="McpContextUpdatedEvent"/>.
/// </summary>
[Trait("Issue", "63")]
public class McpContextPublisherTests
{
    // ── Helpers ─────────────────────────────────────────────────

    private sealed class RecordingBus : IEventBus
    {
        public List<object> Events { get; } = new();
        public void Publish(object evt) => Events.Add(evt);
        public void Subscribe<T>(Action<T> handler) { }
        public void Unsubscribe<T>(Action<T> handler) { }
    }

    private sealed class RecordingOutput : IOutput
    {
        public List<string> Lines { get; } = new();
        public string RegisterChannel(string ch) => ch;
        public IReadOnlyList<string> GetChannelsSnapshot() => Array.Empty<string>();
        public void Write(string ch, string msg) => Lines.Add($"[{ch}] {msg}");
        public void Write(string ch, string msg, OutputSeverity sev) => Lines.Add($"[{ch}] {msg}");
        public void Write(string ch, string msg, OutputSeverity sev, string? src, string? ex = null) => Lines.Add($"[{ch}] {msg}");
        public IReadOnlyList<OutputEntry> GetSnapshot() => Array.Empty<OutputEntry>();
        public event Action<OutputEntry>? EntryWritten;
    }

    private static (RecordingBus bus, McpContextPublisher pub, RecordingOutput output) Create()
    {
        var bus = new RecordingBus();
        var output = new RecordingOutput();
        var validator = new McpMessageValidator(output);
        var pub = new McpContextPublisher(bus, validator, output);
        return (bus, pub, output);
    }

    // ── Tests ───────────────────────────────────────────────────

    [Fact]
    public void TryPublish_ValidPayload_PublishesEventAndReturnsTrue()
    {
        var (bus, pub, _) = Create();

        var result = pub.TryPublish("my-mcp", "doc-index", "{\"docs\":[]}");

        Assert.True(result);
        Assert.Contains(bus.Events, e => e is McpContextUpdatedEvent ctx
            && ctx.ConnectorId == "my-mcp"
            && ctx.ContextType == "doc-index"
            && ctx.Payload == "{\"docs\":[]}");
    }

    [Fact]
    public void TryPublish_InvalidPayload_ReturnsFalse_NoEvent()
    {
        var (bus, pub, _) = Create();

        var result = pub.TryPublish("x", "type", "not-json");

        Assert.False(result);
        Assert.DoesNotContain(bus.Events, e => e is McpContextUpdatedEvent);
    }

    [Fact]
    public void TryPublish_EmptyConnectorId_ReturnsFalse()
    {
        var (bus, pub, output) = Create();

        var result = pub.TryPublish("", "type", "{}");

        Assert.False(result);
        Assert.Contains(output.Lines, l => l.Contains("connectorId"));
    }

    [Fact]
    public void TryPublish_EmptyContextType_ReturnsFalse()
    {
        var (_, pub, output) = Create();

        var result = pub.TryPublish("conn", "", "{}");

        Assert.False(result);
        Assert.Contains(output.Lines, l => l.Contains("contextType"));
    }

    [Fact]
    public void TryPublish_LogsPublishedContext()
    {
        var (_, pub, output) = Create();

        pub.TryPublish("conn", "runtime-state", "{\"status\":\"ok\"}");

        Assert.Contains(output.Lines, l => l.Contains("Published") && l.Contains("runtime-state"));
    }

    [Fact]
    public void TryPublish_LogsRejection_OnInvalidPayload()
    {
        var (_, pub, output) = Create();

        pub.TryPublish("conn", "type", "bad-payload");

        Assert.Contains(output.Lines, l => l.Contains("Rejected"));
    }

    [Fact]
    public void McpContextUpdatedEvent_Properties()
    {
        var now = DateTime.UtcNow;
        var evt = new McpContextUpdatedEvent("conn", "docs", "{}", now);

        Assert.Equal("conn", evt.ConnectorId);
        Assert.Equal("docs", evt.ContextType);
        Assert.Equal("{}", evt.Payload);
        Assert.Equal(now, evt.TimestampUtc);
    }

    [Fact]
    public void McpContextUpdatedEvent_RecordEquality()
    {
        var t = DateTime.UtcNow;
        var a = new McpContextUpdatedEvent("c", "t", "{}", t);
        var b = new McpContextUpdatedEvent("c", "t", "{}", t);
        Assert.Equal(a, b);
    }

    [Fact]
    public void Constructor_ThrowsOnNull()
    {
        var bus = new RecordingBus();
        var val = new McpMessageValidator();
        Assert.Throws<ArgumentNullException>(() => new McpContextPublisher(null!, val));
        Assert.Throws<ArgumentNullException>(() => new McpContextPublisher(bus, null!));
    }
}

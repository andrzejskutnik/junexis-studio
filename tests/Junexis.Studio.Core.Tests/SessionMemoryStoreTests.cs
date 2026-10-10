using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;
using Xunit;

namespace Junexis.Studio.Core.Tests;

/// <summary>
/// Tests for <see cref="SessionMemoryStore"/> — append, history, clear, retention, serialization.
/// </summary>
[Trait("Issue", "80")]
public class SessionMemoryStoreTests
{
    // ── Fake ────────────────────────────────────────────────────

    private sealed class RecordingOutput : IOutput
    {
        public List<string> Lines { get; } = new();
        public string RegisterChannel(string ch) => ch;
        public IReadOnlyList<string> GetChannelsSnapshot() => Array.Empty<string>();
        public void Write(string ch, string msg) => Lines.Add($"[{ch}] {msg}");
        public void Write(string ch, string msg, OutputSeverity sev) => Lines.Add($"[{ch}][{sev}] {msg}");
        public void Write(string ch, string msg, OutputSeverity sev, string? src, string? ex = null) => Lines.Add($"[{ch}][{sev}] {msg}");
        public IReadOnlyList<OutputEntry> GetSnapshot() => Array.Empty<OutputEntry>();
        public event Action<OutputEntry>? EntryWritten;
    }

    // ── Helpers ─────────────────────────────────────────────────

    private const string WsRoot = "/workspace/project";

    private static StoredMessage Msg(string content, AssistantRole role = AssistantRole.User, DateTime? ts = null,
        string? sessionId = null) =>
        new(role, content, ts ?? DateTime.UtcNow, sessionId);

    private (SessionMemoryStore store, RecordingOutput output) Create(RetentionPolicy? policy = null)
    {
        var output = new RecordingOutput();
        var store = new SessionMemoryStore(output);
        if (policy != null) store.Policy = policy;
        return (store, output);
    }

    // ── GetHistory ──────────────────────────────────────────────

    [Fact]
    public void GetHistory_Empty_ReturnsEmptyList()
    {
        var (store, _) = Create();
        Assert.Empty(store.GetHistory(WsRoot));
    }

    [Fact]
    public void GetHistory_NullWorkspace_Throws()
    {
        var (store, _) = Create();
        Assert.Throws<ArgumentNullException>(() => store.GetHistory(null!));
    }

    // ── Append ──────────────────────────────────────────────────

    [Fact]
    public void Append_AddsMessageToHistory()
    {
        var (store, _) = Create();
        store.Append(WsRoot, Msg("hello"));
        Assert.Single(store.GetHistory(WsRoot));
    }

    [Fact]
    public void Append_PreservesOrder()
    {
        var (store, _) = Create();
        store.Append(WsRoot, Msg("first"));
        store.Append(WsRoot, Msg("second"));
        var history = store.GetHistory(WsRoot);
        Assert.Equal("first", history[0].Content);
        Assert.Equal("second", history[1].Content);
    }

    [Fact]
    public void Append_NullMessage_Throws()
    {
        var (store, _) = Create();
        Assert.Throws<ArgumentNullException>(() => store.Append(WsRoot, null!));
    }

    [Fact]
    public void Append_SeparateWorkspaces_Isolated()
    {
        var (store, _) = Create();
        store.Append("/ws1", Msg("msg1"));
        store.Append("/ws2", Msg("msg2"));
        Assert.Single(store.GetHistory("/ws1"));
        Assert.Single(store.GetHistory("/ws2"));
        Assert.Equal("msg1", store.GetHistory("/ws1")[0].Content);
    }

    [Fact]
    public void Append_LogsToOutput()
    {
        var (store, output) = Create();
        store.Append(WsRoot, Msg("hello world"));
        Assert.Contains(output.Lines, l => l.Contains("appended") && l.Contains("User"));
    }

    // ── Clear ───────────────────────────────────────────────────

    [Fact]
    public void Clear_RemovesAllMessages()
    {
        var (store, _) = Create();
        store.Append(WsRoot, Msg("a"));
        store.Append(WsRoot, Msg("b"));
        store.Clear(WsRoot);
        Assert.Empty(store.GetHistory(WsRoot));
    }

    [Fact]
    public void Clear_DoesNotAffectOtherWorkspaces()
    {
        var (store, _) = Create();
        store.Append("/ws1", Msg("a"));
        store.Append("/ws2", Msg("b"));
        store.Clear("/ws1");
        Assert.Empty(store.GetHistory("/ws1"));
        Assert.Single(store.GetHistory("/ws2"));
    }

    [Fact]
    public void Clear_OnEmpty_NoError()
    {
        var (store, _) = Create();
        store.Clear(WsRoot); // no exception
        Assert.Empty(store.GetHistory(WsRoot));
    }

    [Fact]
    public void Clear_LogsRemovedCount()
    {
        var (store, output) = Create();
        store.Append(WsRoot, Msg("a"));
        store.Append(WsRoot, Msg("b"));
        store.Clear(WsRoot);
        Assert.Contains(output.Lines, l => l.Contains("cleared") && l.Contains("2"));
    }

    // ── Retention: MaxMessages ──────────────────────────────────

    [Fact]
    public void Retention_MaxMessages_TrimsOldest()
    {
        var (store, _) = Create(new RetentionPolicy(MaxMessages: 2));
        store.Append(WsRoot, Msg("old1"));
        store.Append(WsRoot, Msg("old2"));
        store.Append(WsRoot, Msg("new"));
        store.ApplyRetention(WsRoot);
        var history = store.GetHistory(WsRoot);
        Assert.Equal(2, history.Count);
        Assert.Equal("old2", history[0].Content);
        Assert.Equal("new", history[1].Content);
    }

    [Fact]
    public void Retention_UnlimitedMessages_NoTrim()
    {
        var (store, _) = Create(new RetentionPolicy(MaxMessages: 0));
        for (int i = 0; i < 10; i++) store.Append(WsRoot, Msg($"msg{i}"));
        store.ApplyRetention(WsRoot);
        Assert.Equal(10, store.GetHistory(WsRoot).Count);
    }

    // ── Retention: MaxAgeDays ───────────────────────────────────

    [Fact]
    public void Retention_MaxAge_RemovesExpired()
    {
        var (store, _) = Create(new RetentionPolicy(MaxAgeDays: 7));
        store.Append(WsRoot, Msg("old", ts: DateTime.UtcNow.AddDays(-10)));
        store.Append(WsRoot, Msg("recent", ts: DateTime.UtcNow));
        store.ApplyRetention(WsRoot);
        var history = store.GetHistory(WsRoot);
        Assert.Single(history);
        Assert.Equal("recent", history[0].Content);
    }

    [Fact]
    public void Retention_Combined_AgeAndCount()
    {
        var (store, _) = Create(new RetentionPolicy(MaxMessages: 1, MaxAgeDays: 7));
        store.Append(WsRoot, Msg("expired", ts: DateTime.UtcNow.AddDays(-10)));
        store.Append(WsRoot, Msg("recent1", ts: DateTime.UtcNow));
        store.Append(WsRoot, Msg("recent2", ts: DateTime.UtcNow));
        store.ApplyRetention(WsRoot);
        var history = store.GetHistory(WsRoot);
        Assert.Single(history);
        Assert.Equal("recent2", history[0].Content);
    }

    [Fact]
    public void Retention_LogsWhenMessagesRemoved()
    {
        var (store, output) = Create(new RetentionPolicy(MaxMessages: 1));
        store.Append(WsRoot, Msg("a"));
        store.Append(WsRoot, Msg("b"));
        store.ApplyRetention(WsRoot);
        Assert.Contains(output.Lines, l => l.Contains("Retention") && l.Contains("removed"));
    }

    [Fact]
    public void Retention_EmptyStore_NoOp()
    {
        var (store, output) = Create(new RetentionPolicy(MaxMessages: 1));
        store.ApplyRetention(WsRoot); // no exception, no log about removal
        Assert.DoesNotContain(output.Lines, l => l.Contains("Retention"));
    }

    // ── Serialization ───────────────────────────────────────────

    [Fact]
    public void Serialize_ProducesValidJson()
    {
        var (store, _) = Create();
        store.Append(WsRoot, Msg("hello", AssistantRole.User, DateTime.UtcNow, "sess1"));
        var json = store.Serialize(WsRoot);
        Assert.Contains("hello", json);
        Assert.Contains("user", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Deserialize_RestoresMessages()
    {
        var (store, _) = Create();
        store.Append(WsRoot, Msg("one"));
        store.Append(WsRoot, Msg("two", AssistantRole.Assistant));
        var json = store.Serialize(WsRoot);

        var (store2, _) = Create();
        var count = store2.Deserialize(WsRoot, json);
        Assert.Equal(2, count);
        var history = store2.GetHistory(WsRoot);
        Assert.Equal("one", history[0].Content);
        Assert.Equal(AssistantRole.Assistant, history[1].Role);
    }

    [Fact]
    public void Deserialize_EmptyJson_ReturnsZero()
    {
        var (store, _) = Create();
        Assert.Equal(0, store.Deserialize(WsRoot, ""));
        Assert.Equal(0, store.Deserialize(WsRoot, "  "));
    }

    [Fact]
    public void Deserialize_NullWorkspace_Throws()
    {
        var (store, _) = Create();
        Assert.Throws<ArgumentNullException>(() => store.Deserialize(null!, "[]"));
    }

    [Fact]
    public void Serialize_EmptyStore_ReturnsEmptyArray()
    {
        var (store, _) = Create();
        var json = store.Serialize(WsRoot);
        Assert.Equal("[]", json);
    }

    // ── OutputChannel ───────────────────────────────────────────

    [Fact]
    public void OutputChannel_IsOMWiA()
    {
        Assert.Equal("OMWiA", SessionMemoryStore.OutputChannel);
    }

    // ── Policy defaults ─────────────────────────────────────────

    [Fact]
    public void DefaultPolicy_HasReasonableDefaults()
    {
        var policy = new RetentionPolicy();
        Assert.Equal(200, policy.MaxMessages);
        Assert.Equal(30, policy.MaxAgeDays);
    }

    // ── StoredMessage record ────────────────────────────────────

    [Fact]
    public void StoredMessage_PreservesAllFields()
    {
        var ts = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);
        var msg = new StoredMessage(AssistantRole.System, "system prompt", ts, "sess42");
        Assert.Equal(AssistantRole.System, msg.Role);
        Assert.Equal("system prompt", msg.Content);
        Assert.Equal(ts, msg.TimestampUtc);
        Assert.Equal("sess42", msg.SessionId);
    }
}

using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;
using Xunit;

namespace Junexis.Studio.Core.Tests;

/// <summary>
/// Tests for <see cref="AssistantService"/>, <see cref="IAssistantService"/>,
/// and the assistant model types.
/// </summary>
[Trait("Issue", "81")]
public class AssistantServiceTests
{
    // ── Helpers ─────────────────────────────────────────────────

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

    private static Task<AssistantResponse> EchoBackend(IReadOnlyList<AssistantMessage> history, CancellationToken ct)
    {
        var lastUser = history.LastOrDefault(m => m.Role == AssistantRole.User);
        var reply = AssistantMessage.FromAssistant($"echo: {lastUser?.Content}");
        return Task.FromResult(new AssistantResponse(reply, new TokenUsage(10, 5, 15)));
    }

    private AssistantService CreateService(AssistantBackend? backend = null, RecordingOutput? output = null)
        => new AssistantService(backend ?? EchoBackend, output: output);

    // ── Tests ───────────────────────────────────────────────────

    [Fact]
    public void StartSession_CreatesSession()
    {
        var svc = CreateService();
        var session = svc.StartSession("c:\\work");

        Assert.NotNull(session);
        Assert.NotNull(session.Id);
        Assert.Equal("c:\\work", session.WorkspaceRoot);
        Assert.Same(session, svc.ActiveSession);
    }

    [Fact]
    public void StartSession_WithoutWorkspace()
    {
        var svc = CreateService();
        var session = svc.StartSession();

        Assert.NotNull(session);
        Assert.Null(session.WorkspaceRoot);
    }

    [Fact]
    public async Task StartSession_ClearsPreviousHistory()
    {
        var svc = CreateService();
        svc.StartSession();
        await svc.SendMessageAsync("hello");

        svc.StartSession();
        Assert.Empty(svc.GetHistory());
    }

    [Fact]
    public async Task SendMessage_ReturnsResponse()
    {
        var svc = CreateService();
        svc.StartSession();

        var response = await svc.SendMessageAsync("hello");

        Assert.NotNull(response);
        Assert.Equal(AssistantRole.Assistant, response.Message.Role);
        Assert.Contains("echo: hello", response.Message.Content);
    }

    [Fact]
    public async Task SendMessage_IncludesTokenUsage()
    {
        var svc = CreateService();
        svc.StartSession();

        var response = await svc.SendMessageAsync("test");

        Assert.NotNull(response.Usage);
        Assert.Equal(15, response.Usage.TotalTokens);
    }

    [Fact]
    public async Task SendMessage_AddsToHistory()
    {
        var svc = CreateService();
        svc.StartSession();

        await svc.SendMessageAsync("hello");

        var history = svc.GetHistory();
        Assert.Equal(2, history.Count); // user + assistant
        Assert.Equal(AssistantRole.User, history[0].Role);
        Assert.Equal(AssistantRole.Assistant, history[1].Role);
    }

    [Fact]
    public async Task SendMessage_WithoutSession_Throws()
    {
        var svc = CreateService();
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.SendMessageAsync("hello"));
    }

    [Fact]
    public async Task SendMessage_NullMessage_Throws()
    {
        var svc = CreateService();
        svc.StartSession();
        await Assert.ThrowsAsync<ArgumentNullException>(() => svc.SendMessageAsync(null!));
    }

    [Fact]
    public void Cancel_DoesNotThrow_WithoutSession()
    {
        var svc = CreateService();
        svc.Cancel(); // no-op
    }

    [Fact]
    public async Task Cancel_CancelsInProgress()
    {
        var tcs = new TaskCompletionSource<AssistantResponse>();

        Task<AssistantResponse> SlowBackend(IReadOnlyList<AssistantMessage> h, CancellationToken ct)
        {
            ct.Register(() => tcs.TrySetCanceled());
            return tcs.Task;
        }

        var svc = new AssistantService(SlowBackend);
        svc.StartSession();

        var task = svc.SendMessageAsync("hello");

        svc.Cancel();

        await Assert.ThrowsAsync<TaskCanceledException>(() => task);
    }

    [Fact]
    public void GetHistory_ReturnsEmpty_WithoutSession()
    {
        var svc = CreateService();
        Assert.Empty(svc.GetHistory());
    }

    [Fact]
    public async Task EndSession_ClearsSessionAndHistory()
    {
        var svc = CreateService();
        svc.StartSession();
        await svc.SendMessageAsync("hello");

        svc.EndSession();

        Assert.Null(svc.ActiveSession);
        Assert.Empty(svc.GetHistory());
    }

    [Fact]
    public void EndSession_WhenNoSession_IsNoop()
    {
        var svc = CreateService();
        svc.EndSession(); // Should not throw
    }

    [Fact]
    public void Constructor_NullBackend_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new AssistantService(null!));
    }

    [Fact]
    public async Task Output_LogsMessages()
    {
        var output = new RecordingOutput();
        var svc = CreateService(output: output);
        svc.StartSession();

        await svc.SendMessageAsync("hello");

        Assert.Contains(output.Lines, l => l.Contains("Session started"));
        Assert.Contains(output.Lines, l => l.Contains("User: hello"));
        Assert.Contains(output.Lines, l => l.Contains("Assistant:") && l.Contains("echo"));
    }

    // ── Model tests ─────────────────────────────────────────────

    [Fact]
    public void AssistantMessage_FromUser_SetsRole()
    {
        var msg = AssistantMessage.FromUser("hi");
        Assert.Equal(AssistantRole.User, msg.Role);
        Assert.Equal("hi", msg.Content);
    }

    [Fact]
    public void AssistantMessage_FromAssistant_SetsRole()
    {
        var msg = AssistantMessage.FromAssistant("reply");
        Assert.Equal(AssistantRole.Assistant, msg.Role);
    }

    [Fact]
    public void AssistantMessage_FromSystem_SetsRole()
    {
        var msg = AssistantMessage.FromSystem("ctx");
        Assert.Equal(AssistantRole.System, msg.Role);
    }

    [Fact]
    public void AssistantSession_RecordEquality()
    {
        var s1 = new AssistantSession("a", "c:\\w", DateTime.UtcNow);
        var s2 = s1 with { };
        Assert.Equal(s1, s2);
    }

    [Fact]
    public void TokenUsage_Properties()
    {
        var u = new TokenUsage(10, 5, 15);
        Assert.Equal(10, u.PromptTokens);
        Assert.Equal(5, u.CompletionTokens);
        Assert.Equal(15, u.TotalTokens);
    }

    [Fact]
    public void AssistantResponse_RecordEquality()
    {
        var msg = AssistantMessage.FromAssistant("hi");
        var r1 = new AssistantResponse(msg);
        var r2 = new AssistantResponse(msg);
        Assert.Equal(r1, r2);
        Assert.Null(r1.Usage);
    }
}

using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;
using Junexis.Studio.Host;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Junexis.Studio.Host.Tests;

public sealed class OmwiaChatViewModelTests
{
    private readonly FakeAssistantService _assistant = new();
    private readonly OutputService _output = new();

    private OmwiaChatViewModel CreateSut() => new(_assistant, _output);

    // --- ctor ---

    [Fact]
    [Trait("Issue", "78")]
    public void Constructor_NullAssistant_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new OmwiaChatViewModel(null!, _output));
    }

    [Fact]
    [Trait("Issue", "78")]
    public void Constructor_NullOutput_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new OmwiaChatViewModel(_assistant, null!));
    }

    // --- EnsureSession ---

    [Fact]
    [Trait("Issue", "78")]
    public void EnsureSession_StartsSessionWhenNone()
    {
        var sut = CreateSut();
        sut.EnsureSession("/ws");

        Assert.NotNull(_assistant.ActiveSession);
        Assert.Equal("/ws", _assistant.ActiveSession!.WorkspaceRoot);
    }

    [Fact]
    [Trait("Issue", "78")]
    public void EnsureSession_DoesNotStartSecondSession()
    {
        var sut = CreateSut();
        sut.EnsureSession("/ws");
        var firstId = _assistant.ActiveSession!.Id;
        sut.EnsureSession("/ws2");

        Assert.Equal(firstId, _assistant.ActiveSession.Id);
    }

    [Fact]
    [Trait("Issue", "78")]
    public void EnsureSession_RaisesMessagesChanged()
    {
        var sut = CreateSut();
        var raised = false;
        sut.MessagesChanged += () => raised = true;

        sut.EnsureSession();

        Assert.True(raised);
    }

    [Fact]
    [Trait("Issue", "78")]
    public void EnsureSession_WritesToOutput()
    {
        var sut = CreateSut();
        sut.EnsureSession();

        var snapshot = _output.GetSnapshot();
        Assert.Contains(snapshot, e => e.Channel == "OMWiA" && e.Message.Contains("session started"));
    }

    // --- Messages property ---

    [Fact]
    [Trait("Issue", "78")]
    public async Task Messages_ReturnsHistoryFromAssistant()
    {
        var sut = CreateSut();
        sut.EnsureSession();
        _assistant.EnqueueResponse("reply1");

        await sut.SendAsync("hello");

        Assert.Equal(2, sut.Messages.Count); // user + assistant
        Assert.Equal(AssistantRole.User, sut.Messages[0].Role);
        Assert.Equal(AssistantRole.Assistant, sut.Messages[1].Role);
    }

    // --- SendAsync ---

    [Fact]
    [Trait("Issue", "78")]
    public async Task SendAsync_ReturnsResponseContent()
    {
        var sut = CreateSut();
        _assistant.EnqueueResponse("world");

        var result = await sut.SendAsync("hello");

        Assert.Equal("world", result);
    }

    [Fact]
    [Trait("Issue", "78")]
    public async Task SendAsync_EmptyMessage_Throws()
    {
        var sut = CreateSut();
        await Assert.ThrowsAsync<ArgumentException>(() => sut.SendAsync(""));
    }

    [Fact]
    [Trait("Issue", "78")]
    public async Task SendAsync_WhitespaceMessage_Throws()
    {
        var sut = CreateSut();
        await Assert.ThrowsAsync<ArgumentException>(() => sut.SendAsync("   "));
    }

    [Fact]
    [Trait("Issue", "78")]
    public async Task SendAsync_SetsIsBusyDuringSend()
    {
        bool wasBusyDuringSend = false;
        var sut = CreateSut();
        _assistant.OnSendCallback = () => { wasBusyDuringSend = sut.IsBusy; };
        _assistant.EnqueueResponse("ok");

        await sut.SendAsync("x");

        Assert.True(wasBusyDuringSend);
        Assert.False(sut.IsBusy);
    }

    [Fact]
    [Trait("Issue", "78")]
    public async Task SendAsync_IsBusyFalseAfterCompletion()
    {
        var sut = CreateSut();
        _assistant.EnqueueResponse("ok");
        await sut.SendAsync("x");

        Assert.False(sut.IsBusy);
    }

    [Fact]
    [Trait("Issue", "78")]
    public async Task SendAsync_RaisesMessagesChanged()
    {
        var sut = CreateSut();
        _assistant.EnqueueResponse("ok");
        sut.EnsureSession();

        var count = 0;
        sut.MessagesChanged += () => count++;
        await sut.SendAsync("x");

        Assert.True(count >= 1);
    }

    [Fact]
    [Trait("Issue", "78")]
    public async Task SendAsync_WhenServiceThrows_SetsLastError()
    {
        _assistant.ThrowOnSend = new InvalidOperationException("provider down");
        var sut = CreateSut();

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.SendAsync("x"));
        Assert.Equal("provider down", sut.LastError);
    }

    [Fact]
    [Trait("Issue", "78")]
    public async Task SendAsync_WhenServiceThrows_WritesToOutput()
    {
        _assistant.ThrowOnSend = new InvalidOperationException("provider down");
        var sut = CreateSut();

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.SendAsync("x"));

        var snapshot = _output.GetSnapshot();
        Assert.Contains(snapshot, e => e.Channel == "OMWiA" && e.Message.Contains("provider down"));
    }

    [Fact]
    [Trait("Issue", "78")]
    public async Task SendAsync_Cancellation_Rethrows()
    {
        var cts = new CancellationTokenSource();
        _assistant.CancelOnSend = true;

        var sut = CreateSut();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => sut.SendAsync("x", cts.Token));
    }

    // --- Cancel ---

    [Fact]
    [Trait("Issue", "78")]
    public void Cancel_DelegatestoAssistant()
    {
        var sut = CreateSut();
        sut.Cancel();
        Assert.True(_assistant.CancelCalled);
    }

    // --- EndSession ---

    [Fact]
    [Trait("Issue", "78")]
    public void EndSession_DelegatesAndRaisesMessagesChanged()
    {
        var sut = CreateSut();
        sut.EnsureSession();

        var raised = false;
        sut.MessagesChanged += () => raised = true;

        sut.EndSession();

        Assert.Null(_assistant.ActiveSession);
        Assert.True(raised);
    }

    [Fact]
    [Trait("Issue", "78")]
    public void EndSession_WritesToOutput()
    {
        var sut = CreateSut();
        sut.EnsureSession();
        sut.EndSession();

        var snapshot = _output.GetSnapshot();
        Assert.Contains(snapshot, e => e.Channel == "OMWiA" && e.Message.Contains("session ended"));
    }
}

public sealed class OmwiaChatExtensionTests
{
    private IHostContext CreateContext(ICapabilityRegistry? capabilities = null)
    {
        var caps = capabilities ?? new CapabilityRegistry();
        return new HostContext(
            eventBus: new EventBus(),
            commands: new CommandRegistry(),
            panels: new PanelRegistry(),
            output: new OutputService(),
            processes: new TestProcessService(),
            workspace: new WorkspaceService(),
            diagnostics: new DiagnosticService(),
            activeDocument: new ActiveDocumentService(),
            openDocuments: new TestOpenDocumentRepository(),
            versionControl: null,
            views: new TestViewRegistry(),
            capabilities: caps);
    }

    [Fact]
    [Trait("Issue", "78")]
    public void Id_ReturnsExpected()
    {
        var ext = new OmwiaChatExtension();
        Assert.Equal("junexis.omwia.chat", ext.Id);
    }

    [Fact]
    [Trait("Issue", "78")]
    public void Initialize_WithoutAssistant_DoesNotRegisterPanel()
    {
        var ctx = CreateContext();
        var ext = new OmwiaChatExtension();

        ext.Initialize(ctx);

        Assert.Empty(ctx.Panels.GetRegisteredPanels());
    }

    [Fact]
    [Trait("Issue", "78")]
    public void Initialize_WithoutAssistant_LogsWarning()
    {
        var ctx = CreateContext();
        var ext = new OmwiaChatExtension();

        ext.Initialize(ctx);

        var snapshot = ((OutputService)ctx.Output).GetSnapshot();
        Assert.Contains(snapshot, e => e.Channel == "OMWiA" && e.Message.Contains("not available"));
    }

    [Fact]
    [Trait("Issue", "78")]
    public void Initialize_WithAssistant_RegistersPanel()
    {
        var caps = new CapabilityRegistry();
        caps.Register<IAssistantService>(new FakeAssistantService());
        var ctx = CreateContext(caps);
        var ext = new OmwiaChatExtension();

        ext.Initialize(ctx);

        var panels = ctx.Panels.GetRegisteredPanels();
        Assert.Contains(panels, p => p.Id == "junexis.omwia.chat");
    }

    [Fact]
    [Trait("Issue", "78")]
    public void Initialize_RegistersOpenCommand()
    {
        var caps = new CapabilityRegistry();
        caps.Register<IAssistantService>(new FakeAssistantService());
        var ctx = CreateContext(caps);
        var ext = new OmwiaChatExtension();

        ext.Initialize(ctx);

        Assert.True(ctx.Commands.CanExecute("omwia.chat.open"));
    }

    [Fact]
    [Trait("Issue", "78")]
    public void Initialize_RegistersEndSessionCommand()
    {
        var caps = new CapabilityRegistry();
        caps.Register<IAssistantService>(new FakeAssistantService());
        var ctx = CreateContext(caps);
        var ext = new OmwiaChatExtension();

        ext.Initialize(ctx);

        Assert.True(ctx.Commands.CanExecute("omwia.chat.endSession"));
    }

    [Fact]
    [Trait("Issue", "78")]
    public void Shutdown_DoesNotThrow()
    {
        var ext = new OmwiaChatExtension();
        ext.Shutdown(); // before init — should not throw
    }

    // ── Context menu targets (Issue #101) ───────────────────────

    [Fact]
    [Trait("Issue", "101")]
    public void Initialize_Commands_HaveContextTarget_JunexisOmwiaChat()
    {
        var caps = new CapabilityRegistry();
        caps.Register<IAssistantService>(new FakeAssistantService());
        var ctx = CreateContext(caps);
        var ext = new OmwiaChatExtension();

        ext.Initialize(ctx);

        var cmds = ctx.Commands.GetRegisteredCommands();
        foreach (var id in new[] { "omwia.chat.open", "omwia.chat.endSession" })
        {
            var cmd = Assert.Single(cmds, c => c.Id == id);
            Assert.Equal("junexis.omwia.chat", cmd.ContextTarget);
        }
    }
}

// --- Fake assistant service for tests ---

internal sealed class FakeAssistantService : IAssistantService
{
    private AssistantSession? _session;
    private readonly List<AssistantMessage> _history = new();
    private readonly Queue<string> _responses = new();

    public Exception? ThrowOnSend { get; set; }
    public bool CancelOnSend { get; set; }
    public bool CancelCalled { get; private set; }
    public Action? OnSendCallback { get; set; }

    public AssistantSession? ActiveSession => _session;

    public AssistantSession StartSession(string? workspaceRoot = null)
    {
        _session = new AssistantSession(Guid.NewGuid().ToString(), workspaceRoot, DateTime.UtcNow);
        _history.Clear();
        return _session;
    }

    public void EnqueueResponse(string content)
    {
        _responses.Enqueue(content);
    }

    public Task<AssistantResponse> SendMessageAsync(string message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        OnSendCallback?.Invoke();

        if (ThrowOnSend is not null) throw ThrowOnSend;
        if (CancelOnSend) throw new OperationCanceledException();

        _history.Add(AssistantMessage.FromUser(message));

        var reply = _responses.Count > 0 ? _responses.Dequeue() : "default-reply";
        var assistantMsg = AssistantMessage.FromAssistant(reply);
        _history.Add(assistantMsg);

        return Task.FromResult(new AssistantResponse(assistantMsg));
    }

    public void Cancel()
    {
        CancelCalled = true;
    }

    public IReadOnlyList<AssistantMessage> GetHistory()
    {
        return _history.AsReadOnly();
    }

    public void EndSession()
    {
        _session = null;
        _history.Clear();
    }
}

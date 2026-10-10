using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;
using Xunit;

namespace Junexis.Studio.Core.Tests;

/// <summary>
/// Tests for <see cref="TerminalEventPublisher"/> and terminal event records.
/// </summary>
[Trait("Issue", "49")]
public class TerminalEventPublisherTests
{
    // ── Fakes ───────────────────────────────────────────────────

    private sealed class RecordingEventBus : IEventBus
    {
        public List<object> Published { get; } = new();
        public void Publish(object evt) => Published.Add(evt);
        public void Subscribe<T>(Action<T> handler) { }
        public void Unsubscribe<T>(Action<T> handler) { }
    }

    private sealed class FakeProcessService : IProcessService
    {
        public int ExitCode { get; set; }
        public TimeSpan Duration { get; set; } = TimeSpan.FromMilliseconds(100);
        public List<ProcessOutputLine> OutputLines { get; set; } = new();
        public bool ThrowOnRun { get; set; }

        public Task<ProcessResult> RunAsync(
            ProcessRequest request,
            Action<ProcessOutputLine>? listener,
            CancellationToken cancellationToken)
        {
            if (ThrowOnRun)
                throw new InvalidOperationException("boom");

            foreach (var line in OutputLines)
            {
                listener?.Invoke(line);
            }

            return Task.FromResult(new ProcessResult(ExitCode, Duration));
        }
    }

    // ── Constructor ─────────────────────────────────────────────

    [Fact]
    public void Ctor_NullProcesses_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new TerminalEventPublisher(null!, new RecordingEventBus()));
    }

    [Fact]
    public void Ctor_NullEventBus_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new TerminalEventPublisher(new FakeProcessService(), null!));
    }

    // ── RunAsync: publishes Started + Exited ────────────────────

    [Fact]
    public async Task RunAsync_PublishesStartedAndExited()
    {
        var bus = new RecordingEventBus();
        var proc = new FakeProcessService { ExitCode = 0 };
        var sut = new TerminalEventPublisher(proc, bus);

        var req = new ProcessRequest("echo", "hello");
        await sut.RunAsync(req, null, CancellationToken.None);

        Assert.Equal(2, bus.Published.Count);
        var started = Assert.IsType<ProcessStartedEvent>(bus.Published[0]);
        var exited = Assert.IsType<ProcessExitedEvent>(bus.Published[1]);

        Assert.Equal("echo", started.FileName);
        Assert.Equal("hello", started.Arguments);
        Assert.Equal(0, exited.ExitCode);
        Assert.Equal(started.ProcessId, exited.ProcessId);
    }

    // ── RunAsync: publishes OutputReceived ──────────────────────

    [Fact]
    public async Task RunAsync_WithOutput_PublishesOutputEvents()
    {
        var bus = new RecordingEventBus();
        var proc = new FakeProcessService
        {
            OutputLines = new()
            {
                new ProcessOutputLine(ProcessOutputKind.StdOut, "line1"),
                new ProcessOutputLine(ProcessOutputKind.StdErr, "err1")
            }
        };
        var sut = new TerminalEventPublisher(proc, bus);

        await sut.RunAsync(new ProcessRequest("cmd"), null, CancellationToken.None);

        // Started + 2 outputs + Exited = 4
        Assert.Equal(4, bus.Published.Count);
        var out1 = Assert.IsType<ProcessOutputReceivedEvent>(bus.Published[1]);
        var out2 = Assert.IsType<ProcessOutputReceivedEvent>(bus.Published[2]);
        Assert.Equal("line1", out1.Text);
        Assert.Equal(ProcessOutputKind.StdOut, out1.Kind);
        Assert.Equal("err1", out2.Text);
        Assert.Equal(ProcessOutputKind.StdErr, out2.Kind);
    }

    // ── RunAsync: original listener still invoked ───────────────

    [Fact]
    public async Task RunAsync_OriginalListenerInvoked()
    {
        var bus = new RecordingEventBus();
        var proc = new FakeProcessService
        {
            OutputLines = new() { new ProcessOutputLine(ProcessOutputKind.StdOut, "hello") }
        };
        var sut = new TerminalEventPublisher(proc, bus);
        var received = new List<string>();

        await sut.RunAsync(
            new ProcessRequest("cmd"),
            line => received.Add(line.Text),
            CancellationToken.None);

        Assert.Single(received);
        Assert.Equal("hello", received[0]);
    }

    // ── RunAsync: null request throws ───────────────────────────

    [Fact]
    public async Task RunAsync_NullRequest_Throws()
    {
        var sut = new TerminalEventPublisher(new FakeProcessService(), new RecordingEventBus());
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            sut.RunAsync(null!, null, CancellationToken.None));
    }

    // ── ProcessId: unique per run ───────────────────────────────

    [Fact]
    public async Task RunAsync_UniqueProcessIds()
    {
        var bus = new RecordingEventBus();
        var proc = new FakeProcessService();
        var sut = new TerminalEventPublisher(proc, bus);

        await sut.RunAsync(new ProcessRequest("a"), null, CancellationToken.None);
        await sut.RunAsync(new ProcessRequest("b"), null, CancellationToken.None);

        var started1 = Assert.IsType<ProcessStartedEvent>(bus.Published[0]);
        var started2 = Assert.IsType<ProcessStartedEvent>(bus.Published[2]);
        Assert.NotEqual(started1.ProcessId, started2.ProcessId);
    }

    // ── ProcessId: format ───────────────────────────────────────

    [Fact]
    public async Task RunAsync_ProcessIdFormat()
    {
        var bus = new RecordingEventBus();
        var sut = new TerminalEventPublisher(new FakeProcessService(), bus);
        await sut.RunAsync(new ProcessRequest("cmd"), null, CancellationToken.None);
        var started = Assert.IsType<ProcessStartedEvent>(bus.Published[0]);
        Assert.StartsWith("proc-", started.ProcessId);
    }

    // ── ExitCode propagation ────────────────────────────────────

    [Fact]
    public async Task RunAsync_ExitCode_Propagated()
    {
        var bus = new RecordingEventBus();
        var proc = new FakeProcessService { ExitCode = 42 };
        var sut = new TerminalEventPublisher(proc, bus);

        var result = await sut.RunAsync(new ProcessRequest("cmd"), null, CancellationToken.None);
        Assert.Equal(42, result.ExitCode);

        var exited = bus.Published.OfType<ProcessExitedEvent>().Single();
        Assert.Equal(42, exited.ExitCode);
    }

    // ── Duration propagation ────────────────────────────────────

    [Fact]
    public async Task RunAsync_Duration_Propagated()
    {
        var bus = new RecordingEventBus();
        var proc = new FakeProcessService { Duration = TimeSpan.FromSeconds(5) };
        var sut = new TerminalEventPublisher(proc, bus);

        var result = await sut.RunAsync(new ProcessRequest("cmd"), null, CancellationToken.None);
        Assert.Equal(TimeSpan.FromSeconds(5), result.Duration);
    }

    // ── Process exception: no hiding ────────────────────────────

    [Fact]
    public async Task RunAsync_ProcessThrows_PropagatesException()
    {
        var bus = new RecordingEventBus();
        var proc = new FakeProcessService { ThrowOnRun = true };
        var sut = new TerminalEventPublisher(proc, bus);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.RunAsync(new ProcessRequest("cmd"), null, CancellationToken.None));

        // Started was published before the run
        Assert.Single(bus.Published);
        Assert.IsType<ProcessStartedEvent>(bus.Published[0]);
    }

    // ── Event record fields ─────────────────────────────────────

    [Fact]
    public void ProcessStartedEvent_PreservesFields()
    {
        var ts = DateTimeOffset.UtcNow;
        var e = new ProcessStartedEvent("p1", "cmd", "--flag", ts);
        Assert.Equal("p1", e.ProcessId);
        Assert.Equal("cmd", e.FileName);
        Assert.Equal("--flag", e.Arguments);
        Assert.Equal(ts, e.TimestampUtc);
    }

    [Fact]
    public void ProcessOutputReceivedEvent_PreservesFields()
    {
        var ts = DateTimeOffset.UtcNow;
        var e = new ProcessOutputReceivedEvent("p1", ProcessOutputKind.StdErr, "text", ts);
        Assert.Equal("p1", e.ProcessId);
        Assert.Equal(ProcessOutputKind.StdErr, e.Kind);
        Assert.Equal("text", e.Text);
    }

    [Fact]
    public void ProcessExitedEvent_PreservesFields()
    {
        var ts = DateTimeOffset.UtcNow;
        var dur = TimeSpan.FromSeconds(3);
        var e = new ProcessExitedEvent("p1", 1, dur, ts);
        Assert.Equal("p1", e.ProcessId);
        Assert.Equal(1, e.ExitCode);
        Assert.Equal(dur, e.Duration);
    }

    // ── No output lines: only Started + Exited ──────────────────

    [Fact]
    public async Task RunAsync_NoOutput_OnlyStartedAndExited()
    {
        var bus = new RecordingEventBus();
        var sut = new TerminalEventPublisher(new FakeProcessService(), bus);
        await sut.RunAsync(new ProcessRequest("cmd"), null, CancellationToken.None);
        Assert.Equal(2, bus.Published.Count);
    }

    // ── Subscriber independence: events decouple consumer ───────

    [Fact]
    public async Task Events_CanBeSubscribedIndependentlyOfTerminal()
    {
        // This test documents the design: consumer subscribes to events
        // without knowing about IProcessService or TerminalEventPublisher.
        var bus = new EventBus();
        var received = new List<ProcessStartedEvent>();
        bus.Subscribe<ProcessStartedEvent>(e => received.Add(e));

        var proc = new FakeProcessService();
        var sut = new TerminalEventPublisher(proc, bus);
        await sut.RunAsync(new ProcessRequest("echo"), null, CancellationToken.None);

        Assert.Single(received);
        Assert.Equal("echo", received[0].FileName);
    }
}

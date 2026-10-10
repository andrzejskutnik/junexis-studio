using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;

namespace Junexis.Studio.Core.Tests;

public sealed class NarrativeRuntimeEnvironmentServiceTests
{
    [Fact]
    [Trait("Issue", "26")]
    public void GetCurrent_ReadyLoopbackSession_ReturnsAtomicEnvironment()
    {
        var session = new StubSession(new NarrativeSessionSnapshot(
            NarrativeSessionState.Ready,
            "Ready",
            "http://127.0.0.1:43125/",
            "build-42",
            "debug.toml",
            null,
            0));

        var environment = new NarrativeRuntimeEnvironmentService(session).GetCurrent();

        Assert.NotNull(environment);
        Assert.Equal("http://127.0.0.1:43125", environment.TraceUrl);
        Assert.Equal("build-42", environment.BuildId);
        Assert.Equal("http://127.0.0.1:43125",
            environment.ToProcessVariables()[NarrativeRuntimeEnvironment.TraceUrlVariable]);
        Assert.Equal("build-42",
            environment.ToProcessVariables()[NarrativeRuntimeEnvironment.BuildIdVariable]);
    }

    [Theory]
    [Trait("Issue", "26")]
    [InlineData(NarrativeSessionState.Starting, "http://127.0.0.1:43125", "build-42")]
    [InlineData(NarrativeSessionState.Failed, "http://127.0.0.1:43125", "build-42")]
    [InlineData(NarrativeSessionState.Ready, "http://collector.example:43125", "build-42")]
    [InlineData(NarrativeSessionState.Ready, "http://127.0.0.1:43125", "")]
    public void GetCurrent_UnverifiedOrIncompleteSession_ReturnsNull(
        NarrativeSessionState state,
        string url,
        string buildId)
    {
        var session = new StubSession(new NarrativeSessionSnapshot(
            state, state.ToString(), url, buildId, "debug.toml", null, 0));

        Assert.Null(new NarrativeRuntimeEnvironmentService(session).GetCurrent());
    }

    [Fact]
    [Trait("Issue", "27")]
    public async Task WaitForCurrentAsync_StartingSession_ReturnsEnvironmentAfterReady()
    {
        var session = new StubSession(new NarrativeSessionSnapshot(
            NarrativeSessionState.Starting,
            "Starting",
            "http://127.0.0.1:43125",
            "build-42",
            "debug.toml",
            null,
            0));
        var sut = new NarrativeRuntimeEnvironmentService(session);

        var waiting = sut.WaitForCurrentAsync(TimeSpan.FromSeconds(1));
        session.Publish(session.Snapshot with
        {
            State = NarrativeSessionState.Ready,
            Status = "Ready"
        });
        var environment = await waiting;

        Assert.NotNull(environment);
        Assert.Equal("build-42", environment.BuildId);
    }

    [Fact]
    [Trait("Issue", "27")]
    public async Task WaitForCurrentAsync_Timeout_ReturnsNullWithoutChangingSession()
    {
        var session = new StubSession(new NarrativeSessionSnapshot(
            NarrativeSessionState.Starting,
            "Starting",
            "http://127.0.0.1:43125",
            "build-42",
            "debug.toml",
            null,
            0));

        var environment = await new NarrativeRuntimeEnvironmentService(session)
            .WaitForCurrentAsync(TimeSpan.FromMilliseconds(20));

        Assert.Null(environment);
        Assert.Equal(NarrativeSessionState.Starting, session.Snapshot.State);
    }

    [Fact]
    [Trait("Issue", "27")]
    public async Task WaitForCurrentAsync_CallerCancellation_Throws()
    {
        var session = new StubSession(new NarrativeSessionSnapshot(
            NarrativeSessionState.Starting,
            "Starting",
            "http://127.0.0.1:43125",
            "build-42",
            "debug.toml",
            null,
            0));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new NarrativeRuntimeEnvironmentService(session)
                .WaitForCurrentAsync(TimeSpan.FromSeconds(1), cancellation.Token));
    }

    private sealed class StubSession(NarrativeSessionSnapshot snapshot) : INarrativeSessionService
    {
        public NarrativeSessionSnapshot Snapshot { get; private set; } = snapshot;
        public event Action<NarrativeSessionSnapshot>? SnapshotChanged;
        public void Publish(NarrativeSessionSnapshot next)
        {
            Snapshot = next;
            SnapshotChanged?.Invoke(next);
        }
        public Task StartAsync(LatestBuildReceipt receipt, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
        public Task RestartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

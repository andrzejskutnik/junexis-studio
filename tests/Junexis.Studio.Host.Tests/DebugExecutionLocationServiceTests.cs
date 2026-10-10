using Junexis.Studio.Abstractions;
using Junexis.Studio.Host;

namespace Junexis.Studio.Host.Tests;

public sealed class DebugExecutionLocationServiceTests
{
    [Fact]
    public void Current_FollowsFirstMappedFrameOnlyWhileStopped()
    {
        var session = new StubSession();
        var service = new DebugExecutionLocationService(session);
        var changes = 0;
        service.LocationChanged += () => changes++;
        var path = Path.GetFullPath("current.jnx");

        session.Publish(new(DebugSessionState.Stopped, "Stopped", 1, [],
            [new DebugStackFrame(42, "task", path, 17, 4)]));

        Assert.Equal(new DebugExecutionLocation(path, 17, 4), service.Current);
        session.Publish(new(DebugSessionState.Running, "Running", 1, [], []));
        Assert.Null(service.Current);
        Assert.Equal(2, changes);
    }

    private sealed class StubSession : IDebugSessionService
    {
        public DebugSessionSnapshot Snapshot { get; private set; } =
            new(DebugSessionState.Inactive, "Inactive", null, [], []);
        public IReadOnlyList<DebugBreakpointStatus> BreakpointStatuses => [];
        public event Action<DebugSessionSnapshot>? SnapshotChanged;
        public event Action<DebugOutputEvent>? OutputReceived { add { } remove { } }
        public event Action<IReadOnlyList<DebugBreakpointStatus>>? BreakpointStatusesChanged { add { } remove { } }
        public void Publish(DebugSessionSnapshot snapshot)
        {
            Snapshot = snapshot;
            SnapshotChanged?.Invoke(snapshot);
        }
        public Task StartAsync(DebugSessionConfiguration configuration, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetBreakpointsAsync(string path, IReadOnlyList<DebugSourceBreakpoint> breakpoints, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ContinueAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task NextAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StepInAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StepOutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task PauseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<DebugScope>> GetScopesAsync(int frameId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DebugScope>>([]);
        public Task<IReadOnlyList<DebugVariable>> GetVariablesAsync(int variablesReference, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DebugVariable>>([]);
        public Task<DebugEvaluation> EvaluateAsync(string expression, int? frameId = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

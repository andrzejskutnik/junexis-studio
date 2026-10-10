using Junexis.Studio.Abstractions;
using Junexis.Studio.Host;
using Junexis.Studio.Shell;

namespace Junexis.Studio.Host.Tests;

public sealed class DebugHoverServiceTests
{
    [Fact]
    public void Host_RegistersEditorDebugHoverCapability()
    {
        var host = new JunexisStudioHost();

        Assert.True(host.Context.Capabilities.TryGet<IDebugHoverService>(out var service));
        Assert.NotNull(service);
    }

    [Fact]
    public async Task EvaluateAsync_UsesFirstStoppedFrame()
    {
        var session = new StubSession
        {
            CurrentSnapshot = new(DebugSessionState.Stopped, "Stopped", 1, [],
                [new DebugStackFrame(42, "main", "main.cpp", 3, 1)]),
        };
        var service = new DebugHoverService(session);

        var result = await service.EvaluateAsync("sensor.value");

        Assert.NotNull(result);
        Assert.Equal("sensor.value", result.Expression);
        Assert.Equal(42, session.LastFrameId);
    }

    [Fact]
    public async Task EvaluateAsync_WhenSessionIsRunning_ReturnsNull()
    {
        var session = new StubSession
        {
            CurrentSnapshot = new(DebugSessionState.Running, "Running", null, [], []),
        };

        var result = await new DebugHoverService(session).EvaluateAsync("answer");

        Assert.Null(result);
        Assert.Null(session.LastFrameId);
    }

    [Theory]
    [InlineData("var result = sensor.value + 1;", 20, "sensor.value")]
    [InlineData("answer", 6, "answer")]
    [InlineData("41 + answer", 1, null)]
    public void ExtractHoverExpression_UsesIdentifierOrMemberChain(string text, int offset, string? expected) =>
        Assert.Equal(expected, DocumentTabView.ExtractHoverExpression(text, offset));

    private sealed class StubSession : IDebugSessionService
    {
        public DebugSessionSnapshot CurrentSnapshot { get; set; } =
            new(DebugSessionState.Inactive, "Inactive", null, [], []);
        public int? LastFrameId { get; private set; }
        public DebugSessionSnapshot Snapshot => CurrentSnapshot;
        public IReadOnlyList<DebugBreakpointStatus> BreakpointStatuses => [];
        public event Action<DebugSessionSnapshot>? SnapshotChanged { add { } remove { } }
        public event Action<DebugOutputEvent>? OutputReceived { add { } remove { } }
        public event Action<IReadOnlyList<DebugBreakpointStatus>>? BreakpointStatusesChanged { add { } remove { } }
        public Task StartAsync(DebugSessionConfiguration configuration, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetBreakpointsAsync(string path, IReadOnlyList<DebugSourceBreakpoint> breakpoints, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ContinueAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task NextAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StepInAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StepOutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task PauseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<DebugScope>> GetScopesAsync(int frameId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DebugScope>>([]);
        public Task<IReadOnlyList<DebugVariable>> GetVariablesAsync(int variablesReference, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DebugVariable>>([]);
        public Task<DebugEvaluation> EvaluateAsync(string expression, int? frameId = null, CancellationToken cancellationToken = default)
        {
            LastFrameId = frameId;
            return Task.FromResult(new DebugEvaluation(expression, "7", "int", 0));
        }
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

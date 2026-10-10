using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;
using Junexis.Studio.Host;

namespace Junexis.Studio.Host.Tests;

public sealed class BreakpointGlyphServiceTests
{
    [Theory]
    [InlineData(true, 20, 20, BreakpointGlyphState.Verified)]
    [InlineData(true, 20, 21, BreakpointGlyphState.Adjusted)]
    [InlineData(false, 20, 20, BreakpointGlyphState.Rejected)]
    public void GetGlyphs_ProjectsSingleAdapterResult(
        bool verified,
        int requestedLine,
        int actualLine,
        BreakpointGlyphState expected)
    {
        var path = Path.GetFullPath("sample.jnx");
        var breakpoints = new BreakpointService();
        breakpoints.Toggle(path, 12);
        var session = new StubSession
        {
            Statuses = [Status(path, 12, verified, requestedLine, actualLine)],
        };
        var service = new BreakpointGlyphService(breakpoints, session);

        var glyph = Assert.Single(service.GetGlyphs(path));

        Assert.Equal(expected, glyph.State);
        Assert.Contains(verified ? "1/1" : "0/1", glyph.ToolTip, StringComparison.Ordinal);
    }

    [Fact]
    public void GetGlyphs_AggregatesOneToManyPartialVerification()
    {
        var path = Path.GetFullPath("sample.jnx");
        var breakpoints = new BreakpointService();
        breakpoints.Toggle(path, 12);
        var session = new StubSession
        {
            Statuses =
            [
                Status(path, 12, true, 80, 80),
                Status(path, 12, false, 82, 82, "no executable code"),
            ],
        };
        var service = new BreakpointGlyphService(breakpoints, session);

        var glyph = Assert.Single(service.GetGlyphs(path));

        Assert.Equal(BreakpointGlyphState.PartiallyVerified, glyph.State);
        Assert.Contains("1/2", glyph.ToolTip, StringComparison.Ordinal);
        Assert.Contains("no executable code", glyph.ToolTip, StringComparison.Ordinal);
    }

    [Fact]
    public void GlyphsChanged_FollowsBreakpointAndAdapterChanges()
    {
        var breakpoints = new BreakpointService();
        var session = new StubSession();
        var service = new BreakpointGlyphService(breakpoints, session);
        var changes = 0;
        service.GlyphsChanged += () => changes++;

        breakpoints.Toggle(Path.GetFullPath("sample.jnx"), 12);
        session.Publish([]);

        Assert.Equal(2, changes);
    }

    [Fact]
    public void GetGlyphs_DescribesConditionalLogpointBehavior()
    {
        var path = Path.GetFullPath("sample.jnx");
        var breakpoints = new BreakpointService();
        breakpoints.Set(new DebugSourceBreakpoint(path, 12, null, "ready", ">= 2", "value={value}"));
        var service = new BreakpointGlyphService(breakpoints, new StubSession());

        var glyph = Assert.Single(service.GetGlyphs(path));

        Assert.True(glyph.IsLogpoint);
        Assert.True(glyph.HasCondition);
        Assert.Contains("Logpoint: value={value}", glyph.ToolTip, StringComparison.Ordinal);
        Assert.Contains("Condition: ready", glyph.ToolTip, StringComparison.Ordinal);
        Assert.Contains("Hit condition: >= 2", glyph.ToolTip, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ToggleAsync_SynchronizesRunningAdapter()
    {
        var path = Path.GetFullPath("sample.jnx");
        var breakpoints = new BreakpointService();
        var session = new StubSession
        {
            CurrentSnapshot = new(DebugSessionState.Running, "", null, [], []),
        };
        var service = new BreakpointGlyphService(breakpoints, session);

        await service.ToggleAsync(path, 12);

        var synchronized = Assert.Single(session.SynchronizedBreakpoints);
        Assert.Equal(path, synchronized.Path);
        Assert.Equal(12, synchronized.Line);
    }

    private static DebugBreakpointStatus Status(
        string path,
        int sourceLine,
        bool verified,
        int requestedLine,
        int actualLine,
        string? message = null) =>
        new(path, sourceLine, 1, "main.go", requestedLine, 1, verified, actualLine, 1, message, 1);

    private sealed class StubSession : IDebugSessionService
    {
        public IReadOnlyList<DebugBreakpointStatus> Statuses { get; set; } = [];
        public DebugSessionSnapshot CurrentSnapshot { get; set; } = new(DebugSessionState.Inactive, "", null, [], []);
        public IReadOnlyList<DebugSourceBreakpoint> SynchronizedBreakpoints { get; private set; } = [];
        public DebugSessionSnapshot Snapshot => CurrentSnapshot;
        public IReadOnlyList<DebugBreakpointStatus> BreakpointStatuses => Statuses;
        public event Action<DebugSessionSnapshot>? SnapshotChanged { add { } remove { } }
        public event Action<DebugOutputEvent>? OutputReceived { add { } remove { } }
        public event Action<IReadOnlyList<DebugBreakpointStatus>>? BreakpointStatusesChanged;
        public void Publish(IReadOnlyList<DebugBreakpointStatus> statuses)
        {
            Statuses = statuses;
            BreakpointStatusesChanged?.Invoke(statuses);
        }
        public Task StartAsync(DebugSessionConfiguration configuration, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetBreakpointsAsync(string path, IReadOnlyList<DebugSourceBreakpoint> breakpoints, CancellationToken cancellationToken = default)
        {
            SynchronizedBreakpoints = breakpoints;
            return Task.CompletedTask;
        }
        public Task ContinueAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task NextAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StepInAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StepOutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task PauseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<DebugScope>> GetScopesAsync(int frameId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DebugScope>>([]);
        public Task<IReadOnlyList<DebugVariable>> GetVariablesAsync(int variablesReference, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DebugVariable>>([]);
        public Task<DebugEvaluation> EvaluateAsync(string expression, int? frameId = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new DebugEvaluation(expression, string.Empty, null, 0));
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

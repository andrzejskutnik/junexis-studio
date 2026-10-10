using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;
using Junexis.Studio.Host;

namespace Junexis.Studio.Host.Tests;

public sealed class DebugPanelViewModelTests
{
    [Fact]
    public async Task ToggleBreakpointAsync_UsesActiveDocumentAndEnteredLine()
    {
        var active = new ActiveDocumentService();
        var path = Path.Combine(Path.GetTempPath(), "main.go");
        active.SetActivePath(path);
        var breakpoints = new BreakpointService();
        var viewModel = new DebugPanelViewModel(
            new StubDebugSession(), breakpoints, active, new EventBus(), new OutputService())
        {
            BreakpointLineText = "17",
        };

        await viewModel.ToggleBreakpointAsync();

        var breakpoint = Assert.Single(viewModel.SourceBreakpoints);
        Assert.Equal(Path.GetFullPath(path), breakpoint.Path);
        Assert.Equal(17, breakpoint.Line);
    }

    [Fact]
    public async Task ApplyBreakpointOptionsAsync_CreatesConditionalLogpointAndSynchronizesSession()
    {
        var session = new StubDebugSession();
        var active = new ActiveDocumentService();
        var path = Path.Combine(Path.GetTempPath(), "instrumented.cpp");
        active.SetActivePath(path);
        session.PublishStopped(new DebugStackFrame(1, "main", path, 8, 1));
        var viewModel = new DebugPanelViewModel(
            session, new BreakpointService(), active, new EventBus(), new OutputService())
        {
            BreakpointLineText = "21",
            BreakpointConditionText = "count > 3",
            BreakpointHitConditionText = ">= 5",
            BreakpointLogMessageText = "count={count}",
        };

        await viewModel.ApplyBreakpointOptionsAsync();

        var breakpoint = Assert.Single(viewModel.SourceBreakpoints);
        Assert.Equal("count > 3", breakpoint.Condition);
        Assert.Equal(">= 5", breakpoint.HitCondition);
        Assert.Equal("count={count}", breakpoint.LogMessage);
        Assert.Equal(breakpoint, Assert.Single(session.LastBreakpoints));
    }

    [Fact]
    public async Task ApplyExceptionBreakpointsAsync_UsesAdvertisedAdapterFilters()
    {
        var session = new StubDebugSession();
        session.PublishStopped(new DebugStackFrame(1, "main", "main.cpp", 8, 1));
        var viewModel = new DebugPanelViewModel(
            session, new BreakpointService(), new ActiveDocumentService(), new EventBus(), new OutputService())
        {
            ExceptionBreakpointFiltersText = "uncaught, all; uncaught",
        };

        await viewModel.ApplyExceptionBreakpointsAsync();

        Assert.Equal(["uncaught", "all"], session.LastExceptionFilters);
        Assert.Contains("uncaught (Uncaught exceptions)",
            viewModel.SupportedExceptionBreakpointFiltersText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DebugConsole_RetainsResultsAndNavigatesCommandHistory()
    {
        var session = new StubDebugSession();
        var viewModel = new DebugPanelViewModel(
            session, new BreakpointService(), new ActiveDocumentService(), new EventBus(), new OutputService());
        session.PublishStopped(new DebugStackFrame(23, "main", "main.cpp", 8, 1));
        await WaitUntilAsync(() => viewModel.VariableTree.Count == 1);
        viewModel.DebugConsoleInput = "answer";
        await viewModel.ExecuteDebugConsoleAsync();
        viewModel.DebugConsoleInput = "answer + 1";
        await viewModel.ExecuteDebugConsoleAsync();

        Assert.Equal(2, viewModel.DebugConsoleEntries.Count);
        Assert.Equal("answer + 1", viewModel.DebugConsoleEntries[1].Expression);
        Assert.Equal("repl: answer + 1", viewModel.DebugConsoleEntries[1].Result);
        Assert.Equal(23, session.LastReplFrameId);
        viewModel.PreviousDebugConsoleCommand();
        Assert.Equal("answer + 1", viewModel.DebugConsoleInput);
        viewModel.PreviousDebugConsoleCommand();
        Assert.Equal("answer", viewModel.DebugConsoleInput);
        viewModel.NextDebugConsoleCommand();
        Assert.Equal("answer + 1", viewModel.DebugConsoleInput);
    }

    [Fact]
    public async Task SelectedVariable_CanBeChangedAndWatchedByDataBreakpoint()
    {
        var session = new StubDebugSession();
        var viewModel = new DebugPanelViewModel(
            session, new BreakpointService(), new ActiveDocumentService(), new EventBus(), new OutputService());
        session.PublishStopped(new DebugStackFrame(23, "main", "main.cpp", 8, 1));
        await WaitUntilAsync(() => viewModel.VariableTree.Count == 1);
        var answer = Assert.Single(viewModel.VariableTree);
        viewModel.SelectedVariableNode = answer;
        viewModel.VariableValueText = "50";

        await viewModel.SetSelectedVariableAsync();
        await viewModel.AddDataBreakpointAsync();

        Assert.Equal("50", answer.Value);
        Assert.Equal(7, session.LastSetVariableReference);
        Assert.Equal("answer", session.LastSetVariableName);
        var dataBreakpoint = Assert.Single(viewModel.DataBreakpoints);
        Assert.Equal("local.answer", dataBreakpoint.DataId);
        Assert.Equal("write", dataBreakpoint.AccessType);
        Assert.Single(session.LastDataBreakpoints);
        viewModel.SelectedDataBreakpoint = dataBreakpoint;
        await viewModel.RemoveDataBreakpointAsync();
        Assert.Empty(viewModel.DataBreakpoints);
        Assert.Empty(session.LastDataBreakpoints);
    }

    [Fact]
    public async Task DebugConfigurations_CanSelectProcessPersistAndStartAttach()
    {
        var configurations = new StubDebugConfigurationService();
        var processes = new StubDebugProcessService();
        DebugLaunchConfiguration? started = null;
        var viewModel = new DebugPanelViewModel(
            new StubDebugSession(), new BreakpointService(), new ActiveDocumentService(),
            new EventBus(), new OutputService(), debugConfigurations: configurations,
            debugProcesses: processes, startConfiguration: configuration =>
            {
                started = configuration;
                return Task.CompletedTask;
            }, debugTargetAssessment: new StubDebugTargetAssessmentService());
        viewModel.RefreshProcesses();
        viewModel.NewDebugConfiguration();
        viewModel.ConfigurationNameText = "Attach Node";
        viewModel.ConfigurationProfileText = "cpp";
        viewModel.ConfigurationRequestText = "Attach";
        viewModel.SelectedProcess = Assert.Single(viewModel.Processes);
        Assert.Contains("Narrative", viewModel.DebugTargetNarrativeText, StringComparison.Ordinal);
        viewModel.UseRecommendedDebugProfile();
        Assert.Equal("typescript", viewModel.ConfigurationProfileText);

        viewModel.SaveDebugConfiguration();
        await viewModel.StartSelectedConfigurationAsync();

        var saved = Assert.Single(configurations.GetConfigurations(), item => item.Name == "Attach Node");
        Assert.Equal(DebugConfigurationRequest.Attach, saved.Request);
        Assert.Equal(4242, saved.ProcessId);
        Assert.Equal(saved, started);
    }

    [Fact]
    public void ExplainStoppedFrame_ActivatesWhyPanelAndPublishesMappedEvidence()
    {
        var session = new StubDebugSession();
        var bus = new EventBus();
        ActivatePanelRequested? activation = null;
        WhyForStoppedFrameRequested? request = null;
        bus.Subscribe<ActivatePanelRequested>(value => activation = value);
        bus.Subscribe<WhyForStoppedFrameRequested>(value => request = value);
        var viewModel = new DebugPanelViewModel(
            session, new BreakpointService(), new ActiveDocumentService(), bus, new OutputService());
        var frame = new DebugStackFrame(
            7, "Worker", Path.GetFullPath("main.jnx"), 12, 4,
            JunexisSymbolId: "task:Worker");
        session.PublishStopped(frame);

        viewModel.ExplainStoppedFrame();

        Assert.Equal("junexis.narrative.why", activation?.PanelId);
        Assert.Equal(frame, request?.Frame);
        Assert.True(request?.LoadWhy);
    }

    [Fact]
    public async Task SelectFrameAsync_NavigatesAndLoadsScopes()
    {
        var bus = new EventBus();
        NavigateToLocationRequested? navigation = null;
        bus.Subscribe<NavigateToLocationRequested>(value => navigation = value);
        var viewModel = new DebugPanelViewModel(
            new StubDebugSession(), new BreakpointService(), new ActiveDocumentService(), bus, new OutputService());
        var frame = new DebugStackFrame(42, "main", Path.Combine(Path.GetTempPath(), "main.cpp"), 9, 2);

        await viewModel.SelectFrameAsync(frame);

        Assert.Equal(frame.SourcePath, navigation?.Path);
        Assert.Equal(9, navigation?.Line);
        Assert.Single(viewModel.Scopes);
        Assert.Single(viewModel.Variables);
    }

    [Fact]
    public async Task StoppedSnapshot_AutomaticallyLoadsFirstFrameAndLocals()
    {
        var session = new StubDebugSession();
        var bus = new EventBus();
        NavigateToLocationRequested? navigation = null;
        bus.Subscribe<NavigateToLocationRequested>(value => navigation = value);
        var viewModel = new DebugPanelViewModel(
            session, new BreakpointService(), new ActiveDocumentService(), bus, new OutputService());
        var frame = new DebugStackFrame(17, "DebugProbe", Path.GetFullPath("smoke.jnx"), 4, 5);

        session.PublishStopped(frame);
        await WaitUntilAsync(() => viewModel.Variables.Count == 1);

        Assert.Equal(frame.SourcePath, navigation?.Path);
        Assert.Equal(17, session.LastScopesFrameId);
        Assert.Equal("answer", Assert.Single(viewModel.Variables).Name);
    }

    [Fact]
    public async Task AddWatchAsync_EvaluatesInSelectedFrameAndCanRemove()
    {
        var session = new StubDebugSession();
        var viewModel = new DebugPanelViewModel(
            session, new BreakpointService(), new ActiveDocumentService(), new EventBus(), new OutputService());
        session.PublishStopped(new DebugStackFrame(23, "main", "main.cpp", 8, 1));
        await WaitUntilAsync(() => viewModel.Variables.Count == 1);
        viewModel.WatchExpressionText = "answer + 1";

        await viewModel.AddWatchAsync();

        var watch = Assert.Single(viewModel.Watches);
        Assert.Equal("43", watch.Result);
        Assert.Equal(23, session.LastEvaluationFrameId);
        viewModel.RemoveWatch(watch);
        Assert.Empty(viewModel.Watches);
    }

    [Fact]
    public async Task ExpandNodeAsync_LazilyLoadsNestedVariablesOnce()
    {
        var session = new StubDebugSession();
        var viewModel = new DebugPanelViewModel(
            session, new BreakpointService(), new ActiveDocumentService(), new EventBus(), new OutputService());
        session.PublishStopped(new DebugStackFrame(23, "main", "main.cpp", 8, 1));
        await WaitUntilAsync(() => viewModel.VariableTree.Count == 1);
        var answer = Assert.Single(viewModel.VariableTree);
        Assert.Single(answer.Children);

        await viewModel.ExpandNodeAsync(answer);
        await viewModel.ExpandNodeAsync(answer);

        Assert.True(answer.IsLoaded);
        Assert.Equal("field", Assert.Single(answer.Children).Name);
        Assert.Equal(1, session.NestedVariableRequests);
    }

    [Fact]
    public async Task Watches_ArePersistedPerWorkspace()
    {
        var root = Directory.CreateTempSubdirectory("junexis-watch-store-");
        try
        {
            var workspace = new WorkspaceService();
            workspace.OpenFolder(root.FullName);
            var first = new DebugPanelViewModel(
                new StubDebugSession(), new BreakpointService(), new ActiveDocumentService(),
                new EventBus(), new OutputService(), workspace)
            {
                WatchExpressionText = "sensor.value",
            };
            await first.AddWatchAsync();
            first.Detach();

            var second = new DebugPanelViewModel(
                new StubDebugSession(), new BreakpointService(), new ActiveDocumentService(),
                new EventBus(), new OutputService(), workspace);

            Assert.Equal("sensor.value", Assert.Single(second.Watches).Expression);
            Assert.True(File.Exists(Path.Combine(root.FullName, ".junexis", "studio-watches.json")));
            second.Detach();
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    private sealed class StubDebugSession : IDebugSessionService
    {
        private DebugSessionSnapshot _snapshot =
            new(DebugSessionState.Inactive, "Inactive", null, [], []);
        public DebugSessionSnapshot Snapshot => _snapshot;
        public int? LastScopesFrameId { get; private set; }
        public int? LastEvaluationFrameId { get; private set; }
        public int NestedVariableRequests { get; private set; }
        public IReadOnlyList<DebugSourceBreakpoint> LastBreakpoints { get; private set; } = [];
        public IReadOnlyList<string> LastExceptionFilters { get; private set; } = [];
        public int? LastReplFrameId { get; private set; }
        public int? LastSetVariableReference { get; private set; }
        public string? LastSetVariableName { get; private set; }
        public IReadOnlyList<DebugDataBreakpoint> LastDataBreakpoints { get; private set; } = [];
        public IReadOnlyList<DebugBreakpointStatus> BreakpointStatuses { get; } = [];
        public IReadOnlyList<DebugExceptionBreakpointFilter> ExceptionBreakpointFilters { get; } =
            [new("uncaught", "Uncaught exceptions", true), new("all", "All exceptions", false)];
        public bool SupportsSetVariable => true;
        public bool SupportsDataBreakpoints => true;
        public event Action<DebugSessionSnapshot>? SnapshotChanged;
        public event Action<DebugOutputEvent>? OutputReceived { add { } remove { } }
        public event Action<IReadOnlyList<DebugBreakpointStatus>>? BreakpointStatusesChanged { add { } remove { } }
        public void PublishStopped(DebugStackFrame frame)
        {
            _snapshot = new(DebugSessionState.Stopped, "Stopped", 1, [new DebugThread(1, "main")], [frame]);
            SnapshotChanged?.Invoke(_snapshot);
        }
        public Task StartAsync(DebugSessionConfiguration configuration, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetBreakpointsAsync(string path, IReadOnlyList<DebugSourceBreakpoint> breakpoints, CancellationToken cancellationToken = default)
        {
            LastBreakpoints = breakpoints;
            return Task.CompletedTask;
        }
        public Task SetExceptionBreakpointsAsync(IReadOnlyList<string> filters, CancellationToken cancellationToken = default)
        {
            LastExceptionFilters = filters;
            return Task.CompletedTask;
        }
        public Task ContinueAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task NextAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StepInAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StepOutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task PauseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<DebugScope>> GetScopesAsync(int frameId, CancellationToken cancellationToken = default)
        {
            LastScopesFrameId = frameId;
            return Task.FromResult<IReadOnlyList<DebugScope>>([new DebugScope("Locals", 7, false)]);
        }
        public Task<IReadOnlyList<DebugVariable>> GetVariablesAsync(int variablesReference, CancellationToken cancellationToken = default)
        {
            if (variablesReference == 88)
            {
                NestedVariableRequests++;
                return Task.FromResult<IReadOnlyList<DebugVariable>>(
                    [new DebugVariable("field", "7", "int", 0)]);
            }
            return Task.FromResult<IReadOnlyList<DebugVariable>>(
                [new DebugVariable("answer", "42", "object", 88)]);
        }
        public Task<DebugEvaluation> EvaluateAsync(string expression, int? frameId = null, CancellationToken cancellationToken = default)
        {
            LastEvaluationFrameId = frameId;
            return Task.FromResult(new DebugEvaluation(expression, "43", "int", 0));
        }
        public Task<DebugEvaluation> EvaluateReplAsync(string expression, int? frameId = null, CancellationToken cancellationToken = default)
        {
            LastReplFrameId = frameId;
            return Task.FromResult(new DebugEvaluation(expression, $"repl: {expression}", "int", 0));
        }
        public Task<DebugVariable> SetVariableAsync(int variablesReference, string name, string value, CancellationToken cancellationToken = default)
        {
            LastSetVariableReference = variablesReference;
            LastSetVariableName = name;
            return Task.FromResult(new DebugVariable(name, value, "int", 0));
        }
        public Task<DebugDataBreakpointInfo> GetDataBreakpointInfoAsync(string name, int? variablesReference = null, int? frameId = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new DebugDataBreakpointInfo("local.answer", name, ["write", "readWrite"], false));
        public Task SetDataBreakpointsAsync(IReadOnlyList<DebugDataBreakpoint> breakpoints, CancellationToken cancellationToken = default)
        {
            LastDataBreakpoints = breakpoints;
            return Task.CompletedTask;
        }
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class StubDebugConfigurationService : IDebugConfigurationService
    {
        private readonly List<DebugLaunchConfiguration> _items =
            [new("launch-cpp", "Launch C++", "cpp", DebugConfigurationRequest.Launch)];
        public string? SelectedId { get; private set; } = "launch-cpp";
        public event Action? ConfigurationsChanged;
        public IReadOnlyList<DebugLaunchConfiguration> GetConfigurations() => _items.ToArray();
        public void Upsert(DebugLaunchConfiguration configuration)
        {
            _items.RemoveAll(item => item.Id == configuration.Id);
            _items.Add(configuration);
            SelectedId = configuration.Id;
            ConfigurationsChanged?.Invoke();
        }
        public void Remove(string id)
        {
            _items.RemoveAll(item => item.Id == id);
            SelectedId = _items.FirstOrDefault()?.Id;
            ConfigurationsChanged?.Invoke();
        }
        public void Select(string? id)
        {
            SelectedId = id;
            ConfigurationsChanged?.Invoke();
        }
    }

    private sealed class StubDebugProcessService : IDebugProcessService
    {
        public IReadOnlyList<DebugProcessInfo> GetProcesses() => [new(4242, "node", "node.exe")];
    }

    private sealed class StubDebugTargetAssessmentService : IDebugTargetAssessmentService
    {
        public DebugTargetAssessment Assess(DebugProcessInfo process, string? requestedProfileId = null) => new(
            process,
            "typescript",
            DebugTargetConfidence.Heuristic,
            requestedProfileId is null ? null : requestedProfileId == "typescript",
            null,
            false,
            "Recommended: typescript. Narrative WHY requires trace evidence.",
            ["Node runtime heuristic."]);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var timeout = DateTimeOffset.UtcNow.AddSeconds(2);
        while (!condition())
        {
            if (DateTimeOffset.UtcNow >= timeout)
                throw new TimeoutException("Debug panel did not finish automatic inspection.");
            await Task.Delay(10);
        }
    }
}

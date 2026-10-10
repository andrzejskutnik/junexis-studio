using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;
using Junexis.Studio.Host;

namespace Junexis.Studio.Host.Tests;

public sealed class WorkbenchRunViewModelTests
{
    [Fact]
    public void ActiveCppDocument_SelectsCppProfileAndAvailableOperations()
    {
        var activeDocument = new ActiveDocumentService();
        activeDocument.SetActivePath(Path.GetFullPath("main.cpp"));
        using var workspace = new WorkspaceService();
        using var viewModel = CreateViewModel(
            activeDocument,
            workspace,
            Snapshot("cpp", "cpp-dap", "gdb"),
            out _,
            out _,
            out _);

        Assert.Equal("cpp", viewModel.SelectedProfile?.Id);
        Assert.Equal("main.cpp", viewModel.ActiveFileName);
        Assert.True(viewModel.CanBuild);
        Assert.True(viewModel.CanRun);
        Assert.True(viewModel.CanDebug);
        Assert.False(viewModel.CanStop);
        Assert.Contains("ready", viewModel.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EmptyWorkbench_DisablesStopAndShowsActionableStatus()
    {
        var activeDocument = new ActiveDocumentService();
        using var workspace = new WorkspaceService();
        using var viewModel = CreateViewModel(
            activeDocument,
            workspace,
            Snapshot(),
            out _,
            out _,
            out _);

        viewModel.Stop();

        Assert.False(viewModel.CanStop);
        Assert.Equal("Select a source file", viewModel.StatusText);
    }

    [Fact]
    public async Task Build_ValidatesProfileAndDispatchesItsCommand()
    {
        var activeDocument = new ActiveDocumentService();
        activeDocument.SetActivePath(Path.GetFullPath("main.go"));
        using var workspace = new WorkspaceService();
        using var viewModel = CreateViewModel(
            activeDocument,
            workspace,
            Snapshot("go"),
            out var commands,
            out _,
            out _);
        var executed = false;
        commands.Register("build.go", () => executed = true);

        await viewModel.BuildAsync();

        Assert.True(executed);
        Assert.Equal("Build: Go", viewModel.StatusText);
    }

    [Fact]
    public async Task MissingToolchain_BlocksLaunchAndOpensDoctor()
    {
        var activeDocument = new ActiveDocumentService();
        activeDocument.SetActivePath(Path.GetFullPath("main.cpp"));
        using var workspace = new WorkspaceService();
        using var viewModel = CreateViewModel(
            activeDocument,
            workspace,
            Snapshot(),
            out var commands,
            out var eventBus,
            out var output);
        var executed = false;
        commands.Register("build.gpp", () => executed = true);
        ActivatePanelRequested? activation = null;
        eventBus.Subscribe<ActivatePanelRequested>(request => activation = request);

        await viewModel.BuildAsync();

        Assert.False(executed);
        Assert.Equal("junexis.toolchains.doctor", activation?.PanelId);
        Assert.Contains(output.GetSnapshot(), entry =>
            entry.Channel == "Workbench" && entry.Severity == OutputSeverity.Warning);
    }

    [Fact]
    public void Why_BecomesAvailableOnlyForStoppedDebugSession()
    {
        var activeDocument = new ActiveDocumentService();
        using var workspace = new WorkspaceService();
        using var viewModel = CreateViewModel(
            activeDocument,
            workspace,
            Snapshot(),
            out _,
            out var eventBus,
            out _,
            out var debug);
        ActivatePanelRequested? activation = null;
        eventBus.Subscribe<ActivatePanelRequested>(request => activation = request);

        Assert.False(viewModel.IsDebugStopped);
        debug.Publish(new(DebugSessionState.Stopped, "Paused at breakpoint", 1, [], []));
        viewModel.OpenWhy();

        Assert.True(viewModel.IsDebugStopped);
        Assert.Equal("junexis.narrative.why", activation?.PanelId);
        Assert.Equal("Debug: Paused at breakpoint", viewModel.StatusText);
    }

    [Fact]
    public void OperationLifecycle_DrivesStopStatusAndFailurePanel()
    {
        var activeDocument = new ActiveDocumentService();
        activeDocument.SetActivePath(Path.GetFullPath("main.go"));
        using var workspace = new WorkspaceService();
        var eventBus = new EventBus();
        var operations = new WorkbenchOperationService();
        var debug = new StubDebugSession();
        using var viewModel = new WorkbenchRunViewModel(
            new LaunchProfileRegistry(),
            new StubDoctor(Snapshot("go")),
            new CommandRegistry(),
            activeDocument,
            workspace,
            eventBus,
            new OutputService(),
            debug,
            operations);
        ActivatePanelRequested? activation = null;
        eventBus.Subscribe<ActivatePanelRequested>(request => activation = request);

        var id = operations.Begin("go", "Go", LaunchOperation.Build, "Starting Go build…");
        operations.MarkRunning(id, "Building Go…");

        Assert.True(viewModel.CanStop);
        Assert.Equal("Building Go…", viewModel.StatusText);

        operations.Fail(id, "Go build failed", exitCode: 1, diagnosticCount: 3);

        Assert.False(viewModel.CanStop);
        Assert.Contains("3 diagnostics", viewModel.StatusText);
        Assert.Equal("problems", activation?.PanelId);
    }

    private static WorkbenchRunViewModel CreateViewModel(
        ActiveDocumentService activeDocument,
        WorkspaceService workspace,
        ToolchainDoctorSnapshot snapshot,
        out CommandRegistry commands,
        out EventBus eventBus,
        out OutputService output) =>
        CreateViewModel(activeDocument, workspace, snapshot, out commands, out eventBus, out output, out _);

    private static WorkbenchRunViewModel CreateViewModel(
        ActiveDocumentService activeDocument,
        WorkspaceService workspace,
        ToolchainDoctorSnapshot snapshot,
        out CommandRegistry commands,
        out EventBus eventBus,
        out OutputService output,
        out StubDebugSession debug)
    {
        commands = new CommandRegistry();
        eventBus = new EventBus();
        output = new OutputService();
        debug = new StubDebugSession();
        return new WorkbenchRunViewModel(
            new LaunchProfileRegistry(),
            new StubDoctor(snapshot),
            commands,
            activeDocument,
            workspace,
            eventBus,
            output,
            debug,
            new WorkbenchOperationService());
    }

    private static ToolchainDoctorSnapshot Snapshot(params string[] availableIds) =>
        new(
            DateTimeOffset.UtcNow,
            availableIds.Select(id => new ToolchainCapability(
                id,
                id,
                id,
                id,
                true,
                "test",
                "Available")).ToArray());

    private sealed class StubDoctor(ToolchainDoctorSnapshot snapshot) : IToolchainDoctorService
    {
        public ToolchainDoctorSnapshot? LatestSnapshot { get; private set; } = snapshot;

        public Task<ToolchainDoctorSnapshot> CheckAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(LatestSnapshot!);
    }

    private sealed class StubDebugSession : IDebugSessionService
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

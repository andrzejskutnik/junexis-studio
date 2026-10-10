using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;
using Junexis.Studio.Host;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Junexis.Studio.Host.Tests;

/// <summary>
/// Tests for run.gpp, run.csharp, run.go commands and project.run dispatch.
/// </summary>
public sealed class RunCommandTests
{
    // ─── run.gpp ────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Issue", "113")]
    public void GppExtension_RegistersRunGppCommand()
    {
        var (context, _) = BuildMinimalContext();
        var sut = new GppCompilerExtension();
        sut.Initialize(context);

        Assert.True(context.Commands.CanExecute("run.gpp"));
    }

    [Fact]
    [Trait("Issue", "113")]
    public async Task RunGpp_WhenExeExists_ExecutesExeFromWorkspaceRoot()
    {
        var tempRoot = Directory.CreateTempSubdirectory("junexis-studio-run-gpp-");
        try
        {
            // Create fake executable so the guard passes
            var exeName = OperatingSystem.IsWindows() ? "junexis_build.exe" : "junexis_build";
            var exePath = Path.Combine(tempRoot.FullName, exeName);
            await File.WriteAllTextAsync(exePath, "fake");

            var workspace = new WorkspaceService();
            workspace.OpenFolder(tempRoot.FullName);

            var processes = new CapturingRunProcessService();
            var (context, _) = BuildMinimalContext(
                workspace: workspace,
                processes: processes,
                narrativeEnvironment: ManagedNarrativeEnvironment());

            var sut = new GppCompilerExtension();
            sut.Initialize(context);

            context.Commands.Execute("run.gpp");

            var request = await processes.Request.Task.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.Equal(exePath, request.FileName);
            Assert.Equal(tempRoot.FullName, request.WorkingDirectory);
            AssertManagedNarrativeEnvironment(request);
        }
        finally
        {
            tempRoot.Delete(recursive: true);
        }
    }

    [Fact]
    [Trait("Issue", "113")]
    public async Task RunGpp_WhenExeMissing_WritesWarningAndSkipsProcess()
    {
        var tempRoot = Directory.CreateTempSubdirectory("junexis-studio-run-gpp-missing-");
        try
        {
            var workspace = new WorkspaceService();
            workspace.OpenFolder(tempRoot.FullName);

            var output = new OutputService();
            var (context, _) = BuildMinimalContext(workspace: workspace, output: output);

            var sut = new GppCompilerExtension();
            sut.Initialize(context);

            context.Commands.Execute("run.gpp");

            await WaitUntilAsync(() => output.GetSnapshot().Any(e =>
                e.Channel == "Build" && e.Message.Contains("not found", StringComparison.OrdinalIgnoreCase)));

            var snapshot = output.GetSnapshot();
            Assert.Contains(snapshot, e =>
                e.Channel == "Build" &&
                e.Message.Contains("not found", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            tempRoot.Delete(recursive: true);
        }
    }

    // ─── run.csharp ─────────────────────────────────────────────────────────

    [Fact]
    [Trait("Issue", "113")]
    public void CsharpExtension_RegistersRunCsharpCommand()
    {
        var (context, _) = BuildMinimalContext();
        var sut = new CsharpCompilerExtension();
        sut.Initialize(context);

        Assert.True(context.Commands.CanExecute("run.csharp"));
    }

    [Fact]
    [Trait("Issue", "113")]
    public async Task RunCsharp_ExecutesDotnetRun()
    {
        var workspace = new WorkspaceService();
        workspace.OpenFolder(Directory.GetCurrentDirectory());

        var processes = new CapturingRunProcessService();
        var (context, _) = BuildMinimalContext(workspace: workspace, processes: processes);

        var sut = new CsharpCompilerExtension();
        sut.Initialize(context);

        context.Commands.Execute("run.csharp");

        var request = await processes.Request.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal("dotnet", request.FileName);
        Assert.Equal("run", request.Arguments);
    }

    // ─── run.go ─────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Issue", "113")]
    public void GoExtension_RegistersRunGoCommand()
    {
        var (context, _) = BuildMinimalContext();
        var sut = new GoCompilerExtension();
        sut.Initialize(context);

        Assert.True(context.Commands.CanExecute("run.go"));
    }

    [Fact]
    [Trait("Issue", "113")]
    public async Task RunGo_ExecutesGoRunDotDotDot()
    {
        var workspace = new WorkspaceService();
        workspace.OpenFolder(Directory.GetCurrentDirectory());

        var processes = new CapturingRunProcessService();
        var (context, _) = BuildMinimalContext(
            workspace: workspace,
            processes: processes,
            narrativeEnvironment: ManagedNarrativeEnvironment());

        var sut = new GoCompilerExtension();
        sut.Initialize(context);

        context.Commands.Execute("run.go");

        var request = await processes.Request.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal("go", request.FileName);
        Assert.Equal("run ./...", request.Arguments);
        AssertManagedNarrativeEnvironment(request);
    }

    [Fact]
    [Trait("Issue", "27")]
    public async Task RunGo_StartingNarrative_WaitsBeforeStartingChildProcess()
    {
        var workspace = new WorkspaceService();
        workspace.OpenFolder(Directory.GetCurrentDirectory());
        var processes = new CapturingRunProcessService();
        var narrative = new PreparingNarrativeRuntimeEnvironmentService();
        var (context, _) = BuildMinimalContext(
            workspace: workspace,
            processes: processes,
            narrativeEnvironment: narrative);
        var sut = new GoCompilerExtension();
        sut.Initialize(context);

        context.Commands.Execute("run.go");
        await narrative.WaitStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(processes.Request.Task.IsCompleted);

        narrative.Complete();
        var request = await processes.Request.Task.WaitAsync(TimeSpan.FromSeconds(2));

        AssertManagedNarrativeEnvironment(request);
    }

    [Fact]
    [Trait("Issue", "26")]
    public async Task RunTypeScript_AttachesManagedNarrativeEnvironment()
    {
        var root = Directory.CreateTempSubdirectory("junexis-studio-run-ts-");
        try
        {
            var source = Path.Combine(root.FullName, "main.ts");
            var outputDirectory = Path.Combine(root.FullName, ".junexis", "build", "typescript");
            var output = Path.Combine(outputDirectory, "main.js");
            Directory.CreateDirectory(outputDirectory);
            await File.WriteAllTextAsync(source, "console.log('source');");
            await File.WriteAllTextAsync(output, "console.log('generated');");
            var workspace = new WorkspaceService();
            workspace.OpenFolder(root.FullName);
            var activeDocument = new ActiveDocumentService();
            activeDocument.SetActivePath(source);
            var processes = new CapturingRunProcessService();
            var (context, _) = BuildMinimalContext(
                workspace: workspace,
                processes: processes,
                activeDocument: activeDocument,
                narrativeEnvironment: ManagedNarrativeEnvironment());
            var sut = new TypeScriptCompilerExtension();
            sut.Initialize(context);

            context.Commands.Execute("run.typescript");
            var request = await processes.Request.Task.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.Equal("node", request.FileName);
            AssertManagedNarrativeEnvironment(request);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    // ─── F5 keybinding ──────────────────────────────────────────────────────

    [Fact]
    [Trait("Issue", "113")]
    public void JunexisStudioHost_F5_BoundToProjectRun()
    {
        var host = new JunexisStudioHost();
        var found = host.Context.Keybindings.TryGetCommand(new KeyGesture("F5"), out var commandId);
        Assert.True(found);
        Assert.Equal("project.run", commandId);
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    private static (IHostContext context, OutputService output) BuildMinimalContext(
        WorkspaceService? workspace = null,
        OutputService? output = null,
        IProcessService? processes = null,
        CommandRegistry? commands = null,
        ActiveDocumentService? activeDocument = null,
        INarrativeRuntimeEnvironmentService? narrativeEnvironment = null)
    {
        var o = output ?? new OutputService();
        var w = workspace ?? new WorkspaceService();
        var p = processes ?? new NoOpProcessService();
        var c = commands ?? new CommandRegistry();
        var a = activeDocument ?? new ActiveDocumentService();

        var capabilities = new CapabilityRegistry();
        if (narrativeEnvironment is not null)
            capabilities.Register(narrativeEnvironment);
        var context = new HostContext(
            eventBus: new EventBus(),
            commands: c,
            panels: new PanelRegistry(),
            output: o,
            processes: p,
            workspace: w,
            diagnostics: new DiagnosticService(),
            activeDocument: a,
            openDocuments: new TestOpenDocumentRepository(),
            versionControl: null,
            views: new NullViewRegistry(),
            capabilities: capabilities);

        return (context, o);
    }

    private static INarrativeRuntimeEnvironmentService ManagedNarrativeEnvironment() =>
        new FixedNarrativeRuntimeEnvironmentService(
            new NarrativeRuntimeEnvironment("http://127.0.0.1:43125", "build-42"));

    private static void AssertManagedNarrativeEnvironment(ProcessRequest request)
    {
        Assert.Equal("http://127.0.0.1:43125",
            request.EnvironmentVariables[NarrativeRuntimeEnvironment.TraceUrlVariable]);
        Assert.Equal("build-42",
            request.EnvironmentVariables[NarrativeRuntimeEnvironment.BuildIdVariable]);
    }

    private sealed class FixedNarrativeRuntimeEnvironmentService(
        NarrativeRuntimeEnvironment environment) : INarrativeRuntimeEnvironmentService
    {
        public bool IsPreparationPending => false;
        public NarrativeRuntimeEnvironment? GetCurrent() => environment;
        public Task<NarrativeRuntimeEnvironment?> WaitForCurrentAsync(
            TimeSpan timeout,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<NarrativeRuntimeEnvironment?>(environment);
    }

    private sealed class PreparingNarrativeRuntimeEnvironmentService
        : INarrativeRuntimeEnvironmentService
    {
        private readonly NarrativeRuntimeEnvironment _environment =
            new("http://127.0.0.1:43125", "build-42");
        private readonly TaskCompletionSource _ready =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _isReady;

        public TaskCompletionSource WaitStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool IsPreparationPending => !_isReady;
        public NarrativeRuntimeEnvironment? GetCurrent() => _isReady ? _environment : null;

        public async Task<NarrativeRuntimeEnvironment?> WaitForCurrentAsync(
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            WaitStarted.TrySetResult();
            await _ready.Task.WaitAsync(timeout, cancellationToken);
            return GetCurrent();
        }

        public void Complete()
        {
            _isReady = true;
            _ready.TrySetResult();
        }
    }

    private sealed class CapturingRunProcessService : IProcessService
    {
        public TaskCompletionSource<ProcessRequest> Request { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<ProcessResult> RunAsync(ProcessRequest request, Action<ProcessOutputLine>? listener, CancellationToken cancellationToken)
        {
            Request.TrySetResult(request);
            return Task.FromResult(new ProcessResult(exitCode: 0, duration: TimeSpan.FromMilliseconds(1)));
        }
    }

    private sealed class NoOpProcessService : IProcessService
    {
        public Task<ProcessResult> RunAsync(ProcessRequest request, Action<ProcessOutputLine>? listener, CancellationToken cancellationToken)
            => Task.FromResult(new ProcessResult(exitCode: 0, duration: TimeSpan.FromMilliseconds(1)));
    }

    private sealed class NullViewRegistry : IViewRegistry
    {
        public void Register<TViewModel, TView>()
            where TViewModel : class
            where TView : class, new() { }

        public object? Resolve(object viewModel) => null;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var timeout = DateTimeOffset.UtcNow.AddSeconds(2);
        while (!condition())
        {
            if (DateTimeOffset.UtcNow >= timeout)
                throw new TimeoutException("Expected asynchronous run-command output did not arrive.");
            await Task.Delay(10);
        }
    }
}

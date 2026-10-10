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
/// Tests for build.stop / build.*.stop cancellation commands.
/// </summary>
public sealed class BuildStopTests
{
    // ─── Registration ────────────────────────────────────────────────────────

    [Fact]
    [Trait("Issue", "118")]
    public void GppExtension_RegistersBuildStopCommand()
    {
        var context = BuildContext();
        var sut = new GppCompilerExtension();
        sut.Initialize(context);

        Assert.True(context.Commands.CanExecute("build.gpp.stop"));
    }

    [Fact]
    [Trait("Issue", "118")]
    public void CsharpExtension_RegistersBuildStopCommand()
    {
        var context = BuildContext();
        var sut = new CsharpCompilerExtension();
        sut.Initialize(context);

        Assert.True(context.Commands.CanExecute("build.csharp.stop"));
    }

    [Fact]
    [Trait("Issue", "118")]
    public void GoExtension_RegistersBuildStopCommand()
    {
        var context = BuildContext();
        var sut = new GoCompilerExtension();
        sut.Initialize(context);

        Assert.True(context.Commands.CanExecute("build.go.stop"));
    }

    [Fact]
    [Trait("Issue", "118")]
    public void JunexisStudioHost_RegistersBuildStopCommand()
    {
        var host = new JunexisStudioHost(viewRegistry: null);
        host.Start();
        Assert.True(host.Context.Commands.CanExecute("build.stop"));
    }

    [Fact]
    [Trait("Issue", "118")]
    public void JunexisStudioHost_ShiftF5_BoundToBuildStop()
    {
        var host = new JunexisStudioHost(viewRegistry: null);
        host.Start();
        var bindings = host.Context.Keybindings;

        var found = bindings!.TryGetCommand(new KeyGesture("F5", KeyModifiers.Shift), out var commandId);
        Assert.True(found);
        Assert.Equal("build.stop", commandId);
    }

    [Fact]
    [Trait("Issue", "118")]
    public void JunexisStudioHost_BuildStop_WhenIdle_DelegatesToAllRegisteredStopCommands()
    {
        var host = new JunexisStudioHost(viewRegistry: null);
        host.Start();

        host.Context.Commands.Execute("build.stop");

        var buildEntries = host.Context.Output
            .GetSnapshot()
            .Where(e => e.Channel == "Build" && e.Message == "No build in progress.")
            .ToList();

        Assert.Equal(4, buildEntries.Count);
    }

    // ─── Cancellation ────────────────────────────────────────────────────────

    [Fact]
    [Trait("Issue", "118")]
    public async Task GppExtension_StopWhenIdle_WritesNoActiveBuildMessage()
    {
        var output = new OutputService();
        var context = BuildContext(output: output);
        var sut = new GppCompilerExtension();
        sut.Initialize(context);

        context.Commands.Execute("build.gpp.stop");
        await Task.Delay(50);

        var snapshot = output.GetSnapshot();
        Assert.Contains(snapshot, e =>
            e.Channel == "Build" &&
            e.Message.Contains("No build in progress", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    [Trait("Issue", "118")]
    public async Task GppExtension_BuildStop_CancelsActiveBuild()
    {
        // Arrange: process service that blocks until token is cancelled
        var buildStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var processes = new BlockingProcessService(buildStarted);

        var output = new OutputService();
        var context = BuildContextWithWorkspace(processes, output);

        var sut = new GppCompilerExtension();
        sut.Initialize(context);

        // Start build (async, it will block inside BlockingProcessService)
        context.Commands.Execute("build.gpp");

        // Wait for build to actually start
        await buildStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        // Act: cancel
        context.Commands.Execute("build.gpp.stop");

        // Assert: "Build cancelled." in output within timeout
        await WaitUntilAsync(
            () =>
            {
                var snap = output.GetSnapshot();
                return snap.Any(e => e.Channel == "Build"
                    && e.Message.Contains("cancelled", StringComparison.OrdinalIgnoreCase));
            },
            TimeSpan.FromSeconds(3));
    }

    [Fact]
    [Trait("Issue", "118")]
    public async Task CsharpExtension_StopWhenIdle_WritesNoActiveBuildMessage()
    {
        var output = new OutputService();
        var context = BuildContext(output: output);
        var sut = new CsharpCompilerExtension();
        sut.Initialize(context);

        context.Commands.Execute("build.csharp.stop");
        await Task.Delay(50);

        var snapshot = output.GetSnapshot();
        Assert.Contains(snapshot, e =>
            e.Channel == "Build" &&
            e.Message.Contains("No build in progress", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    [Trait("Issue", "118")]
    public async Task GoExtension_StopWhenIdle_WritesNoActiveBuildMessage()
    {
        var output = new OutputService();
        var context = BuildContext(output: output);
        var sut = new GoCompilerExtension();
        sut.Initialize(context);

        context.Commands.Execute("build.go.stop");
        await Task.Delay(50);

        var snapshot = output.GetSnapshot();
        Assert.Contains(snapshot, e =>
            e.Channel == "Build" &&
            e.Message.Contains("No build in progress", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    [Trait("Issue", "118")]
    public async Task GppExtension_Shutdown_CancelsActiveBuild()
    {
        var buildStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var processes = new BlockingProcessService(buildStarted);

        var output = new OutputService();
        var context = BuildContextWithWorkspace(processes, output);

        var sut = new GppCompilerExtension();
        sut.Initialize(context);

        context.Commands.Execute("build.gpp");
        await buildStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        // Shutdown should cancel the build
        sut.Shutdown();

        await WaitUntilAsync(
            () =>
            {
                var snap = output.GetSnapshot();
                return snap.Any(e => e.Channel == "Build"
                    && e.Message.Contains("cancelled", StringComparison.OrdinalIgnoreCase));
            },
            TimeSpan.FromSeconds(3));
    }

    [Fact]
    [Trait("Issue", "118")]
    public async Task GppExtension_Shutdown_CancelsActiveRun()
    {
        var runStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var processes = new BlockingProcessService(runStarted);

        var output = new OutputService();
        var context = BuildContextWithWorkspace(processes, output);
        var exeName = OperatingSystem.IsWindows() ? "junexis_build.exe" : "junexis_build";
        File.WriteAllText(Path.Combine(context.Workspace.RootPath!, exeName), string.Empty);

        var sut = new GppCompilerExtension();
        sut.Initialize(context);

        context.Commands.Execute("run.gpp");
        await runStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        sut.Shutdown();

        await WaitUntilAsync(
            () =>
            {
                var snap = output.GetSnapshot();
                return snap.Any(e => e.Channel == "Build"
                    && e.Message.Contains("Run cancelled.", StringComparison.OrdinalIgnoreCase));
            },
            TimeSpan.FromSeconds(3));
    }

    [Fact]
    [Trait("Issue", "118")]
    public async Task CsharpExtension_Shutdown_CancelsActiveRun()
    {
        var runStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var processes = new BlockingProcessService(runStarted);

        var output = new OutputService();
        var context = BuildContextWithWorkspace(processes, output);

        var sut = new CsharpCompilerExtension();
        sut.Initialize(context);

        context.Commands.Execute("run.csharp");
        await runStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        sut.Shutdown();

        await WaitUntilAsync(
            () =>
            {
                var snap = output.GetSnapshot();
                return snap.Any(e => e.Channel == "Build"
                    && e.Message.Contains("Run cancelled.", StringComparison.OrdinalIgnoreCase));
            },
            TimeSpan.FromSeconds(3));
    }

    [Fact]
    [Trait("Issue", "118")]
    public async Task GoExtension_Shutdown_CancelsActiveRun()
    {
        var runStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var processes = new BlockingProcessService(runStarted);

        var output = new OutputService();
        var context = BuildContextWithWorkspace(processes, output);

        var sut = new GoCompilerExtension();
        sut.Initialize(context);

        context.Commands.Execute("run.go");
        await runStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        sut.Shutdown();

        await WaitUntilAsync(
            () =>
            {
                var snap = output.GetSnapshot();
                return snap.Any(e => e.Channel == "Build"
                    && e.Message.Contains("Run cancelled.", StringComparison.OrdinalIgnoreCase));
            },
            TimeSpan.FromSeconds(3));
    }

    [Fact]
    [Trait("Issue", "118")]
    public async Task CsharpExtension_BuildStop_CancelsActiveRun()
    {
        var runStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var processes = new BlockingProcessService(runStarted);

        var output = new OutputService();
        var context = BuildContextWithWorkspace(processes, output);

        var sut = new CsharpCompilerExtension();
        sut.Initialize(context);

        context.Commands.Execute("run.csharp");
        await runStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        context.Commands.Execute("build.csharp.stop");

        await WaitUntilAsync(
            () =>
            {
                var snap = output.GetSnapshot();
                return snap.Any(e => e.Channel == "Build"
                    && e.Message.Contains("Run cancelled.", StringComparison.OrdinalIgnoreCase));
            },
            TimeSpan.FromSeconds(3));
    }

    [Fact]
    [Trait("Issue", "118")]
    public async Task GoExtension_BuildStop_CancelsActiveRun()
    {
        var runStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var processes = new BlockingProcessService(runStarted);

        var output = new OutputService();
        var context = BuildContextWithWorkspace(processes, output);

        var sut = new GoCompilerExtension();
        sut.Initialize(context);

        context.Commands.Execute("run.go");
        await runStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        context.Commands.Execute("build.go.stop");

        await WaitUntilAsync(
            () =>
            {
                var snap = output.GetSnapshot();
                return snap.Any(e => e.Channel == "Build"
                    && e.Message.Contains("Run cancelled.", StringComparison.OrdinalIgnoreCase));
            },
            TimeSpan.FromSeconds(3));
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static HostContext BuildContext(OutputService? output = null, IProcessService? processes = null)
        => new(
            eventBus: new EventBus(),
            commands: new CommandRegistry(),
            panels: new PanelRegistry(),
            output: output ?? new OutputService(),
            processes: processes ?? new NullProcessService(),
            workspace: new WorkspaceService(),
            diagnostics: new DiagnosticService(),
            activeDocument: new ActiveDocumentService(),
            openDocuments: new TestOpenDocumentRepository(),
            versionControl: null,
            views: new NullViewRegistry());

    private static HostContext BuildContextWithWorkspace(IProcessService processes, OutputService output)
    {
        var tempRoot = Directory.CreateTempSubdirectory("junexis-stop-test-");
        // Create a dummy .cpp file so the guard passes
        File.WriteAllText(Path.Combine(tempRoot.FullName, "main.cpp"), "int main(){}");

        var workspace = new WorkspaceService();
        workspace.OpenFolder(tempRoot.FullName);

        return new HostContext(
            eventBus: new EventBus(),
            commands: new CommandRegistry(),
            panels: new PanelRegistry(),
            output: output,
            processes: processes,
            workspace: workspace,
            diagnostics: new DiagnosticService(),
            activeDocument: new ActiveDocumentService(),
            openDocuments: new TestOpenDocumentRepository(),
            versionControl: null,
            views: new NullViewRegistry());
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (!condition())
        {
            if (DateTimeOffset.UtcNow > deadline)
                throw new TimeoutException("Condition not met within timeout.");
            await Task.Delay(20);
        }
    }

    // ─── Test doubles ─────────────────────────────────────────────────────────

    /// <summary>
    /// Process service that signals when started and then blocks until CancellationToken fires.
    /// </summary>
    private sealed class BlockingProcessService : IProcessService
    {
        private readonly TaskCompletionSource _started;

        public BlockingProcessService(TaskCompletionSource started)
            => _started = started;

        public async Task<ProcessResult> RunAsync(
            ProcessRequest request,
            Action<ProcessOutputLine>? listener,
            CancellationToken cancellationToken)
        {
            _started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            return new ProcessResult(exitCode: 0, duration: TimeSpan.Zero);
        }
    }

    private sealed class NullProcessService : IProcessService
    {
        public Task<ProcessResult> RunAsync(ProcessRequest request, Action<ProcessOutputLine>? listener, CancellationToken cancellationToken)
            => Task.FromResult(new ProcessResult(exitCode: 0, duration: TimeSpan.Zero));
    }

    private sealed class NullViewRegistry : IViewRegistry
    {
        public void Register<TViewModel, TView>()
            where TViewModel : class
            where TView : class, new()
        { }
        public object? Resolve(object viewModel) => null;
    }
}

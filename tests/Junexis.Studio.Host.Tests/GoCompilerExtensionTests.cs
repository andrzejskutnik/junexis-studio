using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;
using Junexis.Studio.Host;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Junexis.Studio.Host.Tests;

public sealed class GoCompilerExtensionTests
{
    [Fact]
    [Trait("Issue", "98")]
    public async Task BuildGo_UsesProcessServiceWithGoBuild()
    {
        var tempRoot = Directory.CreateTempSubdirectory("junexis-studio-go-");
        try
        {
            var workspace = new WorkspaceService();
            workspace.OpenFolder(tempRoot.FullName);

            var output = new OutputService();
            var processes = new GoCapturingProcessService(
                onRun: listener =>
                {
                    listener?.Invoke(new ProcessOutputLine(ProcessOutputKind.StdOut, "build started"));
                    listener?.Invoke(new ProcessOutputLine(ProcessOutputKind.StdErr, "./main.go:5:3: undefined: bar"));
                });

            var context = new HostContext(
                eventBus: new EventBus(),
                commands: new CommandRegistry(),
                panels: new PanelRegistry(),
                output: output,
                processes: processes,
                workspace: workspace,
                diagnostics: new DiagnosticService(),
                activeDocument: new ActiveDocumentService(),
                openDocuments: new GoTestOpenDocumentRepository(),
                versionControl: null,
                views: new GoNullViewRegistry());

            var sut = new GoCompilerExtension();
            sut.Initialize(context);

            Assert.True(context.Commands.CanExecute("build.go"));

            context.Commands.Execute("build.go");

            var request = await processes.Request.Task.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.Equal("go", request.FileName);
            Assert.Equal("build ./...", request.Arguments);
            Assert.Equal(tempRoot.FullName, request.WorkingDirectory);

            await WaitUntilAsync(
                () => output.GetSnapshot().Any(e => e.Channel == "Build"
                    && e.Message.Contains("Running:", StringComparison.OrdinalIgnoreCase)),
                timeout: TimeSpan.FromSeconds(2));

            await WaitUntilAsync(
                () => context.Diagnostics.GetDiagnostics().Any(d =>
                    d.Severity == DiagnosticSeverity.Error && d.Message.Contains("undefined: bar", StringComparison.OrdinalIgnoreCase)),
                timeout: TimeSpan.FromSeconds(2));
        }
        finally
        {
            tempRoot.Delete(recursive: true);
        }
    }

    [Fact]
    [Trait("Issue", "98")]
    public async Task BuildGo_WhenBuildRunning_SkipsSecondBuild()
    {
        var tempRoot = Directory.CreateTempSubdirectory("junexis-studio-go-skip-");
        try
        {
            var buildStarted = new SemaphoreSlim(0, 1);
            var buildRelease = new SemaphoreSlim(0, 1);

            var workspace = new WorkspaceService();
            workspace.OpenFolder(tempRoot.FullName);

            var output = new OutputService();
            var processes = new GoCapturingProcessService(
                onRun: async listener =>
                {
                    buildStarted.Release();
                    await buildRelease.WaitAsync();
                    listener?.Invoke(new ProcessOutputLine(ProcessOutputKind.StdOut, "done"));
                });

            var context = new HostContext(
                eventBus: new EventBus(),
                commands: new CommandRegistry(),
                panels: new PanelRegistry(),
                output: output,
                processes: processes,
                workspace: workspace,
                diagnostics: new DiagnosticService(),
                activeDocument: new ActiveDocumentService(),
                openDocuments: new GoTestOpenDocumentRepository(),
                versionControl: null,
                views: new GoNullViewRegistry());

            var sut = new GoCompilerExtension();
            sut.Initialize(context);

            context.Commands.Execute("build.go");
            await buildStarted.WaitAsync(TimeSpan.FromSeconds(2));

            // drugi build powinien być zignorowany
            context.Commands.Execute("build.go");

            buildRelease.Release();

            await WaitUntilAsync(
                () => output.GetSnapshot().Any(e => e.Channel == "Build"
                    && e.Message.Contains("already running", StringComparison.OrdinalIgnoreCase)),
                timeout: TimeSpan.FromSeconds(2));
        }
        finally
        {
            tempRoot.Delete(recursive: true);
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var started = DateTimeOffset.UtcNow;
        while (!condition())
        {
            if (DateTimeOffset.UtcNow - started > timeout)
            {
                throw new TimeoutException("Condition was not met within timeout.");
            }

            await Task.Delay(20);
        }
    }

    private sealed class GoCapturingProcessService : IProcessService
    {
        private readonly Func<Action<ProcessOutputLine>?, Task> _onRun;

        public GoCapturingProcessService(Action<Action<ProcessOutputLine>?> onRun)
        {
            _onRun = l => { onRun(l); return Task.CompletedTask; };
        }

        public GoCapturingProcessService(Func<Action<ProcessOutputLine>?, Task> onRun)
        {
            _onRun = onRun;
        }

        public TaskCompletionSource<ProcessRequest> Request { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ProcessResult> RunAsync(ProcessRequest request, Action<ProcessOutputLine>? listener, CancellationToken cancellationToken)
        {
            Request.TrySetResult(request);
            await _onRun(listener);
            return new ProcessResult(exitCode: 0, duration: TimeSpan.FromMilliseconds(1));
        }
    }

    private sealed class GoNullViewRegistry : IViewRegistry
    {
        public void Register<TViewModel, TView>()
            where TViewModel : class
            where TView : class, new()
        {
            // no-op
        }

        public object? Resolve(object viewModel) => null;
    }

    private sealed class GoTestOpenDocumentRepository : IOpenDocumentRepository
    {
        public IReadOnlyList<Document> GetOpenDocuments() => Array.Empty<Document>();

        public bool TryGetByPath(string path, out Document document)
        {
            document = null!;
            return false;
        }
    }
}

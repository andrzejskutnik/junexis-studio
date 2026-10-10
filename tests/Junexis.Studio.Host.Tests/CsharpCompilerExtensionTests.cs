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

public sealed class CsharpCompilerExtensionTests
{
    [Fact]
    [Trait("Issue", "97")]
    public async Task BuildCsharp_UsesProcessServiceWithDotnetBuild()
    {
        var tempRoot = Directory.CreateTempSubdirectory("junexis-studio-cs-");
        try
        {
            var workspace = new WorkspaceService();
            workspace.OpenFolder(tempRoot.FullName);

            var output = new OutputService();
            var processes = new CsharpCapturingProcessService(
                onRun: listener =>
                {
                    listener?.Invoke(new ProcessOutputLine(ProcessOutputKind.StdOut, "Build succeeded."));
                    listener?.Invoke(new ProcessOutputLine(ProcessOutputKind.StdOut, @"src\Program.cs(5,3): error CS0001: Type not found"));
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
                openDocuments: new CsharpTestOpenDocumentRepository(),
                versionControl: null,
                views: new CsharpNullViewRegistry());

            var sut = new CsharpCompilerExtension();
            sut.Initialize(context);

            Assert.True(context.Commands.CanExecute("build.csharp"));

            context.Commands.Execute("build.csharp");

            var request = await processes.Request.Task.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.Equal("dotnet", request.FileName);
            Assert.Equal("build", request.Arguments);
            Assert.Equal(tempRoot.FullName, request.WorkingDirectory);

            await WaitUntilAsync(
                () => output.GetSnapshot().Any(e => e.Channel == "Build"
                    && e.Message.Contains("Running:", StringComparison.OrdinalIgnoreCase)),
                timeout: TimeSpan.FromSeconds(2));

            await WaitUntilAsync(
                () => context.Diagnostics.GetDiagnostics().Any(d =>
                    d.Severity == DiagnosticSeverity.Error && d.Message.Contains("Type not found", StringComparison.OrdinalIgnoreCase)),
                timeout: TimeSpan.FromSeconds(2));
        }
        finally
        {
            tempRoot.Delete(recursive: true);
        }
    }

    [Fact]
    [Trait("Issue", "97")]
    public async Task BuildCsharp_WhenBuildRunning_SkipsSecondBuild()
    {
        var tempRoot = Directory.CreateTempSubdirectory("junexis-studio-cs-skip-");
        try
        {
            var buildStarted = new SemaphoreSlim(0, 1);
            var buildRelease = new SemaphoreSlim(0, 1);

            var workspace = new WorkspaceService();
            workspace.OpenFolder(tempRoot.FullName);

            var output = new OutputService();
            var processes = new CsharpCapturingProcessService(
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
                openDocuments: new CsharpTestOpenDocumentRepository(),
                versionControl: null,
                views: new CsharpNullViewRegistry());

            var sut = new CsharpCompilerExtension();
            sut.Initialize(context);

            context.Commands.Execute("build.csharp");
            await buildStarted.WaitAsync(TimeSpan.FromSeconds(2));

            // drugi build powinien być zignorowany
            context.Commands.Execute("build.csharp");

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

    private sealed class CsharpCapturingProcessService : IProcessService
    {
        private readonly Func<Action<ProcessOutputLine>?, Task> _onRun;

        public CsharpCapturingProcessService(Action<Action<ProcessOutputLine>?> onRun)
        {
            _onRun = l => { onRun(l); return Task.CompletedTask; };
        }

        public CsharpCapturingProcessService(Func<Action<ProcessOutputLine>?, Task> onRun)
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

    private sealed class CsharpNullViewRegistry : IViewRegistry
    {
        public void Register<TViewModel, TView>()
            where TViewModel : class
            where TView : class, new()
        {
            // no-op
        }

        public object? Resolve(object viewModel) => null;
    }

    private sealed class CsharpTestOpenDocumentRepository : IOpenDocumentRepository
    {
        public IReadOnlyList<Document> GetOpenDocuments() => Array.Empty<Document>();

        public bool TryGetByPath(string path, out Document document)
        {
            document = null!;
            return false;
        }
    }
}

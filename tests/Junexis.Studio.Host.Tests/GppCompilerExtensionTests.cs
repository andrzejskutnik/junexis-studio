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

public sealed class GppCompilerExtensionTests
{
    [Fact]
    [Trait("Issue", "18")]
    public async Task BuildGpp_UsesProcessServiceAndStreamsOutput()
    {
        var tempRoot = Directory.CreateTempSubdirectory("junexis-studio-gpp-");
        try
        {
            var cppPath = Path.Combine(tempRoot.FullName, "main.cpp");
            await File.WriteAllTextAsync(cppPath, "int main(){return 0;}");

            var workspace = new WorkspaceService();
            workspace.OpenFolder(tempRoot.FullName);

            var output = new OutputService();
            var processes = new CapturingProcessService(
                onRun: listener =>
                {
                    listener?.Invoke(new ProcessOutputLine(ProcessOutputKind.StdOut, "hello"));
                    listener?.Invoke(new ProcessOutputLine(ProcessOutputKind.StdErr, "main.cpp:2:3: error: boom"));
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
                openDocuments: new TestOpenDocumentRepository(),
                versionControl: null,
                views: new NullViewRegistry());

            var sut = new GppCompilerExtension();
            sut.Initialize(context);

            Assert.True(context.Commands.CanExecute("build.gpp"));

            context.Commands.Execute("build.gpp");

            var request = await processes.Request.Task.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.EndsWith(OperatingSystem.IsWindows() ? "g++.exe" : "g++", request.FileName,
                StringComparison.OrdinalIgnoreCase);
            Assert.Equal(tempRoot.FullName, request.WorkingDirectory);
            Assert.Contains("main.cpp", request.Arguments, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("-o", request.Arguments, StringComparison.Ordinal);
            Assert.Contains("-g", request.Arguments, StringComparison.Ordinal);
            Assert.Contains("-O0", request.Arguments, StringComparison.Ordinal);

            var snapshot = output.GetSnapshot();
            Assert.Contains(snapshot, e => e.Channel == "Build" && e.Message.Contains("Running:", StringComparison.OrdinalIgnoreCase));

            await WaitUntilAsync(
                () => output.GetSnapshot().Any(e => e.Channel == "Build" && e.Message.Contains("hello", StringComparison.OrdinalIgnoreCase)),
                timeout: TimeSpan.FromSeconds(10));

            await WaitUntilAsync(
                () => context.Diagnostics.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("boom", StringComparison.OrdinalIgnoreCase)),
                timeout: TimeSpan.FromSeconds(10));
        }
        finally
        {
            tempRoot.Delete(recursive: true);
        }
    }

    [Fact]
    [Trait("Issue", "23")]
    public async Task BuildGpp_WhenActiveCppIsSet_BuildsOnlyThatFile()
    {
        var tempRoot = Directory.CreateTempSubdirectory("junexis-studio-gpp-");
        try
        {
            var a = Path.Combine(tempRoot.FullName, "a.cpp");
            var b = Path.Combine(tempRoot.FullName, "b.cpp");
            await File.WriteAllTextAsync(a, "int a(){return 0;}");
            await File.WriteAllTextAsync(b, "int main(){return 0;}");

            var workspace = new WorkspaceService();
            workspace.OpenFolder(tempRoot.FullName);

            var output = new OutputService();
            var processes = new CapturingProcessService(onRun: _ => { });

            var active = new ActiveDocumentService();
            active.SetActivePath(b);

            var context = new HostContext(
                eventBus: new EventBus(),
                commands: new CommandRegistry(),
                panels: new PanelRegistry(),
                output: output,
                processes: processes,
                workspace: workspace,
                diagnostics: new DiagnosticService(),
                activeDocument: active,
                openDocuments: new TestOpenDocumentRepository(),
                versionControl: null,
                views: new NullViewRegistry());

            var sut = new GppCompilerExtension();
            sut.Initialize(context);

            context.Commands.Execute("build.gpp");

            var request = await processes.Request.Task.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Contains("b.cpp", request.Arguments, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("a.cpp", request.Arguments, StringComparison.OrdinalIgnoreCase);
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

    private sealed class CapturingProcessService : IProcessService
    {
        private readonly Action<Action<ProcessOutputLine>?> _onRun;

        public CapturingProcessService(Action<Action<ProcessOutputLine>?> onRun)
        {
            _onRun = onRun;
        }

        public TaskCompletionSource<ProcessRequest> Request { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<ProcessResult> RunAsync(ProcessRequest request, Action<ProcessOutputLine>? listener, CancellationToken cancellationToken)
        {
            Request.TrySetResult(request);
            _onRun(listener);
            return Task.FromResult(new ProcessResult(exitCode: 0, duration: TimeSpan.FromMilliseconds(1)));
        }
    }

    private sealed class NullViewRegistry : IViewRegistry
    {
        public void Register<TViewModel, TView>()
            where TViewModel : class
            where TView : class, new()
        {
            // no-op
        }

        public object? Resolve(object viewModel) => null;
    }
}

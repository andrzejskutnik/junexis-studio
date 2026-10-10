using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;
using Junexis.Studio.Host;

namespace Junexis.Studio.Host.Tests;

public sealed class TypeScriptCompilerExtensionTests
{
    [Fact]
    public async Task BuildTypeScript_CompilesActiveFileIntoWorkspaceBuildDirectory()
    {
        var root = Directory.CreateTempSubdirectory("junexis-studio-ts-");
        try
        {
            var source = Path.Combine(root.FullName, "src", "main.ts");
            Directory.CreateDirectory(Path.GetDirectoryName(source)!);
            await File.WriteAllTextAsync(source, "console.log('Junexis');");
            var process = new CapturingProcessService();
            var context = CreateContext(root.FullName, source, process);
            var extension = new TypeScriptCompilerExtension();
            extension.Initialize(context);

            context.Commands.Execute("build.typescript");
            var request = await process.Request.Task.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.Equal("tsc", request.FileName);
            Assert.Contains(".junexis", request.Arguments, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("main.ts", request.Arguments, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task RunTypeScript_UsesCompiledJavaScriptWithNode()
    {
        var root = Directory.CreateTempSubdirectory("junexis-studio-ts-run-");
        try
        {
            var source = Path.Combine(root.FullName, "main.ts");
            await File.WriteAllTextAsync(source, "console.log('Junexis');");
            var compiled = Path.Combine(root.FullName, ".junexis", "build", "typescript", "main.js");
            Directory.CreateDirectory(Path.GetDirectoryName(compiled)!);
            await File.WriteAllTextAsync(compiled, "console.log('Junexis');");
            var process = new CapturingProcessService();
            var context = CreateContext(root.FullName, source, process);
            var extension = new TypeScriptCompilerExtension();
            extension.Initialize(context);

            context.Commands.Execute("run.typescript");
            var request = await process.Request.Task.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.Equal("node", request.FileName);
            Assert.Contains("main.js", request.Arguments, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    private static HostContext CreateContext(string root, string source, IProcessService process)
    {
        var workspace = new WorkspaceService();
        workspace.OpenFolder(root);
        var active = new ActiveDocumentService();
        active.SetActivePath(source);
        return new HostContext(
            new EventBus(), new CommandRegistry(), new PanelRegistry(), new OutputService(), process,
            workspace, new DiagnosticService(), active, new TestOpenDocumentRepository(), null, new TestViewRegistry());
    }

    private sealed class CapturingProcessService : IProcessService
    {
        public TaskCompletionSource<ProcessRequest> Request { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<ProcessResult> RunAsync(
            ProcessRequest request,
            Action<ProcessOutputLine>? listener,
            CancellationToken cancellationToken)
        {
            Request.TrySetResult(request);
            return Task.FromResult(new ProcessResult(0, TimeSpan.Zero));
        }
    }
}

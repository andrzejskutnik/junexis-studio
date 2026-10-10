using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Host;

/// <summary>Builds, runs, and debug-launches TypeScript sources with tsc and Node.js.</summary>
internal sealed class TypeScriptCompilerExtension : IJunexisExtension
{
    private const string OutputChannel = "Build";
    private CancellationTokenSource? _operation;
    private int _isRunning;

    public string Id => "junexis.compiler.typescript";

    public void Initialize(IHostContext context)
    {
        context.Commands.Register("build.typescript", () => Start(context, LaunchOperation.Build),
            "Build/Build (TypeScript)", "Build (TypeScript)", "Build");
        context.Commands.Register("run.typescript", () => Start(context, LaunchOperation.Run),
            "Build/Run (TypeScript)", "Run (TypeScript)", "Build");
        context.Commands.Register("debug.typescript", () => Start(context, LaunchOperation.Debug),
            "Build/Debug (TypeScript)", "Debug (TypeScript)", "Debug");
        context.Commands.Register("build.typescript.stop", () => Stop(context),
            "Build/Stop TypeScript", "Stop TypeScript", "Build");
        context.Output.Write("Extensions", "TypeScriptCompilerExtension loaded");
    }

    public void Shutdown() => _operation?.Cancel();

    private void Start(IHostContext context, LaunchOperation operation)
    {
        if (Interlocked.Exchange(ref _isRunning, 1) == 1)
        {
            context.Output.Write(OutputChannel, "A TypeScript operation is already running.");
            return;
        }

        var cancellation = new CancellationTokenSource();
        _operation = cancellation;
        var lifecycle = WorkbenchOperationHandle.Begin(
            context,
            "typescript",
            "TypeScript",
            operation,
            $"Starting TypeScript {operation.ToString().ToLowerInvariant()}…");
        _ = Task.Run(async () =>
        {
            try
            {
                lifecycle.Running(
                    operation != LaunchOperation.Build
                    && NarrativeRuntimeLaunch.IsPreparationPending(context)
                        ? "Preparing Narrative for TypeScript launch…"
                        : $"{operation}: TypeScript…");
                var exitCode = await ExecuteAsync(
                    context,
                    operation,
                    lifecycle,
                    cancellation.Token).ConfigureAwait(false);
                if (exitCode is null)
                    lifecycle.Fail($"TypeScript {operation.ToString().ToLowerInvariant()} could not start");
                else if (exitCode == 0)
                    lifecycle.Complete(
                        0,
                        operation == LaunchOperation.Build ? context.Diagnostics.GetDiagnostics().Count : 0,
                        $"TypeScript {operation.ToString().ToLowerInvariant()} completed");
                else
                    lifecycle.Fail(
                        $"TypeScript {operation.ToString().ToLowerInvariant()} failed",
                        exitCode,
                        operation == LaunchOperation.Build ? context.Diagnostics.GetDiagnostics().Count : 0);
            }
            catch (OperationCanceledException)
            {
                context.Output.Write(OutputChannel, "TypeScript operation cancelled.");
                lifecycle.Cancel($"TypeScript {operation.ToString().ToLowerInvariant()} cancelled");
            }
            catch (Exception ex)
            {
                context.Output.Write(OutputChannel, $"TypeScript {operation.ToString().ToLowerInvariant()} failed: {ex.Message}",
                    OutputSeverity.Error);
                lifecycle.Fail($"TypeScript {operation.ToString().ToLowerInvariant()} failed: {ex.Message}");
            }
            finally
            {
                _operation = null;
                Volatile.Write(ref _isRunning, 0);
                cancellation.Dispose();
            }
        });
    }

    private static async Task<int?> ExecuteAsync(
        IHostContext context,
        LaunchOperation operation,
        WorkbenchOperationHandle lifecycle,
        CancellationToken cancellationToken)
    {
        if (!TryResolvePaths(context, out var root, out var source, out var output))
            return null;

        if (operation == LaunchOperation.Build)
        {
            context.Diagnostics.Clear();
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            var config = Path.Combine(root, "tsconfig.json");
            var arguments = File.Exists(config)
                ? $"--pretty false --project {Quote(config)} --outDir {Quote(Path.Combine(root, ".junexis", "build", "typescript"))}"
                : $"--pretty false --outDir {Quote(Path.Combine(root, ".junexis", "build", "typescript"))} {Quote(source)}";
            return await RunAsync(
                context, "tsc", arguments, root, parseDiagnostics: true, lifecycle, cancellationToken).ConfigureAwait(false);
        }

        if (!File.Exists(output))
        {
            context.Output.Write(OutputChannel,
                $"Compiled JavaScript not found: {output}. Run build.typescript first.", OutputSeverity.Warning);
            return null;
        }

        var nodeArguments = operation == LaunchOperation.Debug
            ? $"--inspect {Quote(output)}"
            : Quote(output);
        return await RunAsync(
            context, "node", nodeArguments, root, parseDiagnostics: false, lifecycle, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<int> RunAsync(
        IHostContext context,
        string fileName,
        string arguments,
        string root,
        bool parseDiagnostics,
        WorkbenchOperationHandle lifecycle,
        CancellationToken cancellationToken)
    {
        context.Output.Write(OutputChannel, $"Running: {fileName} {arguments}");
        var request = new ProcessRequest(fileName, arguments, root);
        if (!parseDiagnostics)
        {
            request = await NarrativeRuntimeLaunch
                .AttachAsync(context, request, cancellationToken)
                .ConfigureAwait(false);
            lifecycle.Running("Running TypeScript…");
        }
        var result = await context.Processes.RunAsync(
            request,
            line =>
            {
                if (parseDiagnostics && TypeScriptDiagnosticParser.TryParse(line.Text, root, out var diagnostic))
                    context.Diagnostics.Add(diagnostic);
                context.Output.Write(OutputChannel, line.Text,
                    line.Kind == ProcessOutputKind.StdErr ? OutputSeverity.Warning : OutputSeverity.Info);
            },
            cancellationToken).ConfigureAwait(false);
        context.Output.Write(OutputChannel, $"Process finished (exit {result.ExitCode}).");
        return result.ExitCode;
    }

    private static bool TryResolvePaths(
        IHostContext context,
        out string root,
        out string source,
        out string output)
    {
        root = context.Workspace.RootPath ?? string.Empty;
        source = context.ActiveDocument.ActivePath ?? string.Empty;
        output = string.Empty;
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            context.Output.Write(OutputChannel, "No workspace root. Open a folder first.", OutputSeverity.Warning);
            return false;
        }
        if (string.IsNullOrWhiteSpace(source)
            || !new[] { ".ts", ".tsx" }.Contains(Path.GetExtension(source), StringComparer.OrdinalIgnoreCase)
            || !File.Exists(source))
        {
            context.Output.Write(OutputChannel, "Open an existing .ts or .tsx file first.", OutputSeverity.Warning);
            return false;
        }

        var relative = Path.GetRelativePath(root, source);
        if (relative.StartsWith("..", StringComparison.Ordinal))
        {
            context.Output.Write(OutputChannel, "The active TypeScript file is outside the workspace.", OutputSeverity.Warning);
            return false;
        }
        output = Path.Combine(root, ".junexis", "build", "typescript", Path.ChangeExtension(relative, ".js"));
        return true;
    }

    private void Stop(IHostContext context)
    {
        if (_operation is null)
            context.Output.Write(OutputChannel, "No TypeScript operation in progress.");
        else
        {
            _operation.Cancel();
            context.Output.Write(OutputChannel, "TypeScript stop requested.");
        }
    }

    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
}

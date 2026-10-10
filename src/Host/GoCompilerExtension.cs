using Junexis.Studio.Abstractions;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Junexis.Studio.Host;

/// <summary>
/// Rozszerzenie kompilatora Go: rejestruje komendy <c>build.go</c> i <c>run.go</c>
/// i streamuje output do <see cref="IOutput"/>.
/// </summary>
internal sealed class GoCompilerExtension : IJunexisExtension
{
    private const string BuildCommandId = "build.go";
    private const string StopCommandId = "build.go.stop";
    private const string RunCommandId = "run.go";
    private const string OutputChannel = "Build";

    private int _isBuilding;
    private int _isRunning;
    private CancellationTokenSource? _buildCts;
    private CancellationTokenSource? _runCts;

    /// <inheritdoc />
    public string Id => "junexis.compiler.go";

    /// <inheritdoc />
    public void Initialize(IHostContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        context.Commands.Register(
            id: BuildCommandId,
            execute: () => StartBuild(context),
            menuPath: "Run/Build (go)");

        context.Commands.Register(
            id: RunCommandId,
            execute: () => StartRun(context),
            menuPath: "Run/Run (go)",
            displayName: "Run (go)",
            category: "Build");

        context.Commands.Register(
            id: StopCommandId,
            execute: () => CancelBuild(context),
            menuPath: "Run/Stop Build (go)",
            displayName: "Stop Build (go)",
            category: "Build");

        context.Output.Write("Extensions", "GoCompilerExtension loaded");
    }

    /// <inheritdoc />
    public void Shutdown()
    {
        _buildCts?.Cancel();
        _runCts?.Cancel();
    }

    private void CancelBuild(IHostContext context)
    {
        var cts = _buildCts;
        var rcts = _runCts;
        if (cts is null && rcts is null)
        {
            context.Output.Write(OutputChannel, "No build in progress.");
            return;
        }
        cts?.Cancel();
        rcts?.Cancel();
        context.Output.Write(OutputChannel, "Stop requested.");
    }

    private void StartBuild(IHostContext context)
    {
        if (Interlocked.Exchange(ref _isBuilding, 1) == 1)
        {
            context.Output.Write(OutputChannel, "Build already running");
            return;
        }

        var cts = new CancellationTokenSource();
        _buildCts = cts;
        var lifecycle = WorkbenchOperationHandle.Begin(
            context, "go", "Go", LaunchOperation.Build, "Starting Go build…");

        _ = Task.Run(async () =>
        {
            try
            {
                lifecycle.Running("Building Go…");
                var exitCode = await BuildAsync(context, cts.Token).ConfigureAwait(false);
                if (exitCode is null)
                    lifecycle.Fail("Go build could not start", diagnostics: context.Diagnostics.GetDiagnostics().Count);
                else if (exitCode == 0)
                    lifecycle.Complete(0, context.Diagnostics.GetDiagnostics().Count, "Go build succeeded");
                else
                    lifecycle.Fail("Go build failed", exitCode, context.Diagnostics.GetDiagnostics().Count);
            }
            catch (OperationCanceledException)
            {
                context.Output.Write(OutputChannel, "Build cancelled.");
                lifecycle.Cancel("Go build cancelled");
            }
            catch (Exception ex)
            {
                context.Output.Write(OutputChannel, $"build.go failed: {ex.Message}");
                lifecycle.Fail($"Go build failed: {ex.Message}");
            }
            finally
            {
                Volatile.Write(ref _isBuilding, 0);
                _buildCts = null;
                cts.Dispose();
            }
        });
    }

    private static async Task<int?> BuildAsync(IHostContext context, CancellationToken cancellationToken)
    {
        var root = context.Workspace.RootPath;
        if (string.IsNullOrWhiteSpace(root))
        {
            context.Output.Write(OutputChannel, "No workspace root. Open a folder first.");
            return null;
        }

        if (!Directory.Exists(root))
        {
            context.Output.Write(OutputChannel, $"Workspace root not found: {root}");
            return null;
        }

        context.Diagnostics.Clear();
        context.Output.Write(OutputChannel, "Running: go build ./...");

        var request = new ProcessRequest(
            fileName: "go",
            arguments: "build ./...",
            workingDirectory: root);

        var result = await context.Processes.RunAsync(
            request,
            listener: line =>
            {
                context.Output.Write(OutputChannel, line.Text);

                // go build wysyła błędy na stderr
                if (line.Kind == ProcessOutputKind.StdErr
                    && GoDiagnosticParser.TryParse(line.Text, root, out var diag))
                {
                    context.Diagnostics.Add(diag);
                }
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        context.Output.Write(OutputChannel, $"Build finished (exit {result.ExitCode})");
        return result.ExitCode;
    }

    private void StartRun(IHostContext context)
    {
        if (Interlocked.Exchange(ref _isRunning, 1) == 1)
        {
            context.Output.Write(OutputChannel, "Run already in progress");
            return;
        }

        var cts = new CancellationTokenSource();
        _runCts = cts;
        var lifecycle = WorkbenchOperationHandle.Begin(
            context, "go", "Go", LaunchOperation.Run, "Starting Go program…");

        _ = Task.Run(async () =>
        {
            try
            {
                lifecycle.Running(NarrativeRuntimeLaunch.IsPreparationPending(context)
                    ? "Preparing Narrative for Go run…"
                    : "Running Go…");
                var exitCode = await RunAsync(context, lifecycle, cts.Token).ConfigureAwait(false);
                if (exitCode is null)
                    lifecycle.Fail("Go run could not start");
                else if (exitCode == 0)
                    lifecycle.Complete(0, 0, "Go run completed");
                else
                    lifecycle.Fail("Go run failed", exitCode);
            }
            catch (OperationCanceledException)
            {
                context.Output.Write(OutputChannel, "Run cancelled.");
                lifecycle.Cancel("Go run cancelled");
            }
            catch (Exception ex)
            {
                context.Output.Write(OutputChannel, $"run.go failed: {ex.Message}");
                lifecycle.Fail($"Go run failed: {ex.Message}");
            }
            finally
            {
                Volatile.Write(ref _isRunning, 0);
                _runCts = null;
                cts.Dispose();
            }
        });
    }

    private static async Task<int?> RunAsync(
        IHostContext context,
        WorkbenchOperationHandle lifecycle,
        CancellationToken cancellationToken)
    {
        var root = context.Workspace.RootPath;
        if (string.IsNullOrWhiteSpace(root))
        {
            context.Output.Write(OutputChannel, "No workspace root. Open a folder first.");
            return null;
        }

        context.Output.Write(OutputChannel, "Running: go run ./...");

        var request = await NarrativeRuntimeLaunch.AttachAsync(context, new ProcessRequest(
            fileName: "go",
            arguments: "run ./...",
            workingDirectory: root), cancellationToken).ConfigureAwait(false);
        lifecycle.Running("Running Go…");

        var result = await context.Processes.RunAsync(
            request,
            listener: line => context.Output.Write(OutputChannel, line.Text),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        context.Output.Write(OutputChannel, $"Run finished (exit {result.ExitCode})");
        return result.ExitCode;
    }
}

using Junexis.Studio.Abstractions;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Junexis.Studio.Host;

/// <summary>
/// Rozszerzenie kompilatora C# (dotnet build): rejestruje komendy <c>build.csharp</c> i <c>run.csharp</c>
/// i streamuje output do <see cref="IOutput"/>.
/// </summary>
internal sealed class CsharpCompilerExtension : IJunexisExtension
{
    private const string BuildCommandId = "build.csharp";
    private const string StopCommandId = "build.csharp.stop";
    private const string RunCommandId = "run.csharp";
    private const string OutputChannel = "Build";

    private int _isBuilding;
    private int _isRunning;
    private CancellationTokenSource? _buildCts;
    private CancellationTokenSource? _runCts;

    /// <inheritdoc />
    public string Id => "junexis.compiler.csharp";

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
            menuPath: "Run/Build (dotnet)");

        context.Commands.Register(
            id: RunCommandId,
            execute: () => StartRun(context),
            menuPath: "Run/Run (dotnet)",
            displayName: "Run (dotnet)",
            category: "Build");

        context.Commands.Register(
            id: StopCommandId,
            execute: () => CancelBuild(context),
            menuPath: "Run/Stop Build (dotnet)",
            displayName: "Stop Build (dotnet)",
            category: "Build");

        context.Output.Write("Extensions", "CsharpCompilerExtension loaded");
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
            context, "csharp", "C# / .NET", LaunchOperation.Build, "Starting .NET build…");

        _ = Task.Run(async () =>
        {
            try
            {
                lifecycle.Running("Building C# / .NET…");
                var exitCode = await BuildAsync(context, cts.Token).ConfigureAwait(false);
                if (exitCode is null)
                    lifecycle.Fail(".NET build could not start", diagnostics: context.Diagnostics.GetDiagnostics().Count);
                else if (exitCode == 0)
                    lifecycle.Complete(0, context.Diagnostics.GetDiagnostics().Count, ".NET build succeeded");
                else
                    lifecycle.Fail(".NET build failed", exitCode, context.Diagnostics.GetDiagnostics().Count);
            }
            catch (OperationCanceledException)
            {
                context.Output.Write(OutputChannel, "Build cancelled.");
                lifecycle.Cancel(".NET build cancelled");
            }
            catch (Exception ex)
            {
                context.Output.Write(OutputChannel, $"build.csharp failed: {ex.Message}");
                lifecycle.Fail($".NET build failed: {ex.Message}");
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
        context.Output.Write(OutputChannel, "Running: dotnet build");

        var request = new ProcessRequest(
            fileName: "dotnet",
            arguments: "build",
            workingDirectory: root);

        var result = await context.Processes.RunAsync(
            request,
            listener: line =>
            {
                context.Output.Write(OutputChannel, line.Text);

                // dotnet build wysyła diagnostyki na stdout
                if (CsharpDiagnosticParser.TryParse(line.Text, root, out var diag))
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
            context, "csharp", "C# / .NET", LaunchOperation.Run, "Starting .NET program…");

        _ = Task.Run(async () =>
        {
            try
            {
                lifecycle.Running("Running C# / .NET…");
                var exitCode = await RunAsync(context, cts.Token).ConfigureAwait(false);
                if (exitCode is null)
                    lifecycle.Fail(".NET run could not start");
                else if (exitCode == 0)
                    lifecycle.Complete(0, 0, ".NET run completed");
                else
                    lifecycle.Fail(".NET run failed", exitCode);
            }
            catch (OperationCanceledException)
            {
                context.Output.Write(OutputChannel, "Run cancelled.");
                lifecycle.Cancel(".NET run cancelled");
            }
            catch (Exception ex)
            {
                context.Output.Write(OutputChannel, $"run.csharp failed: {ex.Message}");
                lifecycle.Fail($".NET run failed: {ex.Message}");
            }
            finally
            {
                Volatile.Write(ref _isRunning, 0);
                _runCts = null;
                cts.Dispose();
            }
        });
    }

    private static async Task<int?> RunAsync(IHostContext context, CancellationToken cancellationToken)
    {
        var root = context.Workspace.RootPath;
        if (string.IsNullOrWhiteSpace(root))
        {
            context.Output.Write(OutputChannel, "No workspace root. Open a folder first.");
            return null;
        }

        context.Output.Write(OutputChannel, "Running: dotnet run");

        var request = new ProcessRequest(
            fileName: "dotnet",
            arguments: "run",
            workingDirectory: root);

        var result = await context.Processes.RunAsync(
            request,
            listener: line => context.Output.Write(OutputChannel, line.Text),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        context.Output.Write(OutputChannel, $"Run finished (exit {result.ExitCode})");
        return result.ExitCode;
    }
}

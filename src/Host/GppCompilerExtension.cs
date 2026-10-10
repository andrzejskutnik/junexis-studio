using Junexis.Studio.Abstractions;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Junexis.Studio.Host;

/// <summary>
/// Rozszerzenie kompilatora C++ (g++): rejestruje komendy <c>build.gpp</c> i <c>run.gpp</c>
/// i streamuje output do <see cref="IOutput"/>.
/// </summary>
internal sealed class GppCompilerExtension : IJunexisExtension
{
    private const string BuildCommandId = "build.gpp";
    private const string StopCommandId = "build.gpp.stop";
    private const string RunCommandId = "run.gpp";
    private const string OutputChannel = "Build";

    private int _isBuilding;
    private int _isRunning;
    private CancellationTokenSource? _buildCts;
    private CancellationTokenSource? _runCts;

    /// <inheritdoc />
    public string Id => "junexis.compiler.gpp";

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
            menuPath: "Run/Build (g++)");

        context.Commands.Register(
            id: RunCommandId,
            execute: () => StartRun(context),
            menuPath: "Run/Run (g++)",
            displayName: "Run (g++)",
            category: "Build");

        context.Commands.Register(
            id: StopCommandId,
            execute: () => CancelBuild(context),
            menuPath: "Run/Stop Build (g++)",
            displayName: "Stop Build (g++)",
            category: "Build");

        context.Output.Write("Extensions", "GppCompilerExtension loaded");
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
            context, "cpp", "C / C++", LaunchOperation.Build, "Starting C++ build…");

        _ = Task.Run(async () =>
        {
            try
            {
                lifecycle.Running("Building C / C++…");
                var exitCode = await BuildAsync(context, cts.Token).ConfigureAwait(false);
                if (exitCode is null)
                    lifecycle.Fail("C++ build could not start", diagnostics: context.Diagnostics.GetDiagnostics().Count);
                else if (exitCode == 0)
                    lifecycle.Complete(0, context.Diagnostics.GetDiagnostics().Count, "C++ build succeeded");
                else
                    lifecycle.Fail("C++ build failed", exitCode, context.Diagnostics.GetDiagnostics().Count);
            }
            catch (OperationCanceledException)
            {
                context.Output.Write(OutputChannel, "Build cancelled.");
                lifecycle.Cancel("C++ build cancelled");
            }
            catch (Exception ex)
            {
                context.Output.Write(OutputChannel, $"build.gpp failed: {ex.Message}");
                lifecycle.Fail($"C++ build failed: {ex.Message}");
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

        var active = context.ActiveDocument.ActivePath;

        var cppFiles = !string.IsNullOrWhiteSpace(active)
            && Path.GetExtension(active).Equals(".cpp", StringComparison.OrdinalIgnoreCase)
            && File.Exists(active)
            ? new[] { active }
            : Directory
                .EnumerateFiles(root, "*.cpp", SearchOption.TopDirectoryOnly)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToArray();

        if (cppFiles.Length == 0)
        {
            context.Output.Write(OutputChannel, "No .cpp files found in workspace root.");
            return null;
        }

        var outputFileName = OperatingSystem.IsWindows() ? "junexis_build.exe" : "junexis_build";
        var outputPath = Path.Combine(root, outputFileName);

        var compiler = DebugToolLocator.ResolveGxx();
        var args = "-g -O0 " + string.Join(' ', cppFiles.Select(Quote)) + $" -o {Quote(outputPath)}";

        context.Diagnostics.Clear();
        context.Output.Write(OutputChannel, $"Running: {compiler} {args}");

        var request = new ProcessRequest(
            fileName: compiler,
            arguments: args,
            workingDirectory: root);

        var result = await context.Processes.RunAsync(
            request,
            listener: line =>
            {
                context.Output.Write(OutputChannel, line.Text);

                if (line.Kind == ProcessOutputKind.StdErr
                    && GppDiagnosticParser.TryParse(line.Text, root, out var diag))
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
            context, "cpp", "C / C++", LaunchOperation.Run, "Starting C++ program…");

        _ = Task.Run(async () =>
        {
            try
            {
                lifecycle.Running(NarrativeRuntimeLaunch.IsPreparationPending(context)
                    ? "Preparing Narrative for C++ run…"
                    : "Running C / C++…");
                var exitCode = await RunAsync(context, lifecycle, cts.Token).ConfigureAwait(false);
                if (exitCode is null)
                    lifecycle.Fail("C++ run could not start");
                else if (exitCode == 0)
                    lifecycle.Complete(0, 0, "C++ run completed");
                else
                    lifecycle.Fail("C++ run failed", exitCode);
            }
            catch (OperationCanceledException)
            {
                context.Output.Write(OutputChannel, "Run cancelled.");
                lifecycle.Cancel("C++ run cancelled");
            }
            catch (Exception ex)
            {
                context.Output.Write(OutputChannel, $"run.gpp failed: {ex.Message}");
                lifecycle.Fail($"C++ run failed: {ex.Message}");
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

        var exeName = OperatingSystem.IsWindows() ? "junexis_build.exe" : "junexis_build";
        var exePath = Path.Combine(root, exeName);

        if (!File.Exists(exePath))
        {
            context.Output.Write(OutputChannel, $"Executable not found: {exePath}. Run build.gpp first.");
            return null;
        }

        context.Output.Write(OutputChannel, $"Running: {exePath}");

        var request = await NarrativeRuntimeLaunch.AttachAsync(context, new ProcessRequest(
            fileName: exePath,
            arguments: string.Empty,
            workingDirectory: root), cancellationToken).ConfigureAwait(false);
        lifecycle.Running("Running C / C++…");

        var result = await context.Processes.RunAsync(
            request,
            listener: line => context.Output.Write(OutputChannel, line.Text),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        context.Output.Write(OutputChannel, $"Run finished (exit {result.ExitCode})");
        return result.ExitCode;
    }

    private static string Quote(string path)
    {
        return $"\"{path}\"";
    }
}

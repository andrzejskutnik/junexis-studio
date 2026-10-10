using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Host;

/// <summary>
/// View-model for the terminal panel. Collects command output lines
/// and exposes an async <see cref="RunAsync"/> method backed by <see cref="IProcessService"/>.
/// </summary>
public sealed class TerminalPanelViewModel
{
    private readonly IProcessService _processes;
    private readonly IOutput _output;
    private readonly List<ProcessOutputLine> _lines = new();
    private CancellationTokenSource? _cts;

    /// <summary>
    /// Collected output lines (stdout + stderr) from the last / current run.
    /// </summary>
    public IReadOnlyList<ProcessOutputLine> Lines => _lines;

    /// <summary>
    /// Whether a process is currently running.
    /// </summary>
    public bool IsRunning { get; private set; }

    /// <summary>
    /// Last process exit code (null if never run).
    /// </summary>
    public int? LastExitCode { get; private set; }

    /// <summary>
    /// Raised when a new output line is received. UI can subscribe to update in real time.
    /// </summary>
    public event Action<ProcessOutputLine>? OutputReceived;

    /// <summary>
    /// Raised when the process finishes.
    /// </summary>
    public event Action<ProcessResult>? ProcessFinished;

    /// <summary>
    /// Initializes a new <see cref="TerminalPanelViewModel"/>.
    /// </summary>
    /// <param name="processes">Process service for running commands.</param>
    /// <param name="output">Output service for logging.</param>
    /// <exception cref="ArgumentNullException">When a required parameter is null.</exception>
    public TerminalPanelViewModel(IProcessService processes, IOutput output)
    {
        _processes = processes ?? throw new ArgumentNullException(nameof(processes));
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    /// <summary>
    /// Runs a command asynchronously. Clears previous output.
    /// </summary>
    /// <param name="fileName">Executable file name (e.g. "cmd", "bash").</param>
    /// <param name="arguments">Command arguments (optional).</param>
    /// <param name="workingDirectory">Working directory (optional).</param>
    /// <returns>The <see cref="ProcessResult"/> when the process exits.</returns>
    /// <exception cref="ArgumentException">When <paramref name="fileName"/> is empty.</exception>
    /// <exception cref="InvalidOperationException">When a process is already running.</exception>
    public async Task<ProcessResult> RunAsync(string fileName, string? arguments = null, string? workingDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("File name must not be empty.", nameof(fileName));
        if (IsRunning)
            throw new InvalidOperationException("A process is already running.");

        _lines.Clear();
        IsRunning = true;
        LastExitCode = null;
        _cts = new CancellationTokenSource();

        _output.Write("Terminal", $"> {fileName} {arguments}".TrimEnd());

        try
        {
            var request = new ProcessRequest(fileName, arguments, workingDirectory);
            var result = await _processes.RunAsync(request, OnLine, _cts.Token);

            LastExitCode = result.ExitCode;
            _output.Write("Terminal", $"Process exited with code {result.ExitCode} ({result.Duration.TotalMilliseconds:F0}ms)");
            ProcessFinished?.Invoke(result);
            return result;
        }
        catch (OperationCanceledException)
        {
            _output.Write("Terminal", "Process cancelled.");
            throw;
        }
        finally
        {
            IsRunning = false;
            _cts.Dispose();
            _cts = null;
        }
    }

    /// <summary>
    /// Cancels the currently running process, if any.
    /// </summary>
    public void Cancel()
    {
        _cts?.Cancel();
    }

    /// <summary>
    /// Runs a raw command-line string. Splits on the first space into
    /// <c>fileName</c> and optional <c>arguments</c>, then delegates to <see cref="RunAsync"/>.
    /// </summary>
    /// <param name="commandLine">Full command line, e.g. <c>"dotnet build"</c>.</param>
    /// <param name="workingDirectory">Optional working directory.</param>
    /// <returns>The <see cref="ProcessResult"/> when the process exits.</returns>
    /// <exception cref="ArgumentException">When <paramref name="commandLine"/> is empty.</exception>
    public Task<ProcessResult> RunCommandLineAsync(string commandLine, string? workingDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
            throw new ArgumentException("Command line must not be empty.", nameof(commandLine));

        // Wrap in a system shell so built-in commands (dir, echo, ls) work correctly.
        // Multi-line blocks are joined with the shell's native line separator.
        string fileName;
        string arguments;
        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
        {
            // cmd.exe uses " & " to chain commands; replace CR/LF variants with it.
            var joined = commandLine.Trim().Replace("\r\n", "\n").Replace('\r', '\n');
            var singleLine = string.Join(" & ", joined.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            fileName = "cmd.exe";
            arguments = $"/c {singleLine}";
        }
        else
        {
            fileName = "/bin/sh";
            arguments = $"-c {commandLine.Trim()}";
        }

        return RunAsync(fileName, arguments, workingDirectory);
    }

    private void OnLine(ProcessOutputLine line)
    {
        _lines.Add(line);
        OutputReceived?.Invoke(line);
    }
}

/// <summary>
/// Terminal extension that registers a terminal tool panel and a "terminal.run" command.
/// Uses <see cref="IProcessService"/> from <see cref="IHostContext"/> to run commands
/// and stream stdout/stderr output.
/// </summary>
public sealed class TerminalExtension : IJunexisExtension
{
    private TerminalPanelViewModel? _viewModel;

    /// <inheritdoc />
    public string Id => "junexis.terminal";

    /// <inheritdoc />
    public void Initialize(IHostContext context)
    {
        context.Output.RegisterChannel("Terminal");

        _viewModel = new TerminalPanelViewModel(context.Processes, context.Output);

        context.Panels.RegisterPanel(
            "junexis.terminal",
            PanelType.Tool,
            () => _viewModel,
            title: "Terminal",
            dockGroup: DockGroup.Bottom);

        context.Commands.Register("terminal.run", () =>
        {
            // Command stub — actual invocation requires arguments from UI.
            // This registers the command so it appears in command palette.
        });

        context.Output.Write("Terminal", "TerminalExtension loaded.");
    }

    /// <inheritdoc />
    public void Shutdown()
    {
        _viewModel?.Cancel();
        _viewModel = null;
    }
}

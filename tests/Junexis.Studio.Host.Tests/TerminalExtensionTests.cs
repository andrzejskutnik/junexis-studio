using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;
using Junexis.Studio.Host;
using Xunit;

namespace Junexis.Studio.Host.Tests;

/// <summary>
/// Tests for <see cref="TerminalExtension"/> and <see cref="TerminalPanelViewModel"/>.
/// </summary>
[Trait("Issue", "50")]
public class TerminalExtensionTests
{
    // ── Fakes ───────────────────────────────────────────────────

    private sealed class FakeProcessService : IProcessService
    {
        public Func<ProcessRequest, Action<ProcessOutputLine>?, CancellationToken, Task<ProcessResult>>?
            Handler
        { get; set; }

        public Task<ProcessResult> RunAsync(
            ProcessRequest request,
            Action<ProcessOutputLine>? listener,
            CancellationToken cancellationToken)
        {
            if (Handler is not null)
                return Handler(request, listener, cancellationToken);
            return Task.FromResult(new ProcessResult(0, TimeSpan.FromMilliseconds(100)));
        }
    }

    private static HostContext CreateContext(
        OutputService? output = null,
        IProcessService? processes = null)
    {
        return new HostContext(
            eventBus: new EventBus(),
            commands: new CommandRegistry(),
            panels: new PanelRegistry(),
            output: output ?? new OutputService(),
            processes: processes ?? new FakeProcessService(),
            workspace: new WorkspaceService(),
            diagnostics: new DiagnosticService(),
            activeDocument: new ActiveDocumentService(),
            openDocuments: new TestOpenDocumentRepository(),
            versionControl: null,
            views: new TestViewRegistry());
    }

    // ── TerminalPanelViewModel: constructor ─────────────────────

    [Fact]
    public void ViewModel_NullProcesses_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new TerminalPanelViewModel(null!, new OutputService()));
    }

    [Fact]
    public void ViewModel_NullOutput_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new TerminalPanelViewModel(new FakeProcessService(), null!));
    }

    // ── TerminalPanelViewModel: RunAsync ────────────────────────

    [Fact]
    public async Task ViewModel_RunAsync_CollectsOutputLines()
    {
        var proc = new FakeProcessService
        {
            Handler = (req, listener, ct) =>
            {
                listener?.Invoke(new ProcessOutputLine(ProcessOutputKind.StdOut, "hello"));
                listener?.Invoke(new ProcessOutputLine(ProcessOutputKind.StdErr, "warn"));
                return Task.FromResult(new ProcessResult(0, TimeSpan.FromMilliseconds(50)));
            }
        };
        var vm = new TerminalPanelViewModel(proc, new OutputService());

        var result = await vm.RunAsync("echo", "hello");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(2, vm.Lines.Count);
        Assert.Equal("hello", vm.Lines[0].Text);
        Assert.Equal(ProcessOutputKind.StdErr, vm.Lines[1].Kind);
    }

    [Fact]
    public async Task ViewModel_RunAsync_SetsLastExitCode()
    {
        var proc = new FakeProcessService
        {
            Handler = (_, _, _) => Task.FromResult(new ProcessResult(42, TimeSpan.Zero))
        };
        var vm = new TerminalPanelViewModel(proc, new OutputService());

        await vm.RunAsync("test");

        Assert.Equal(42, vm.LastExitCode);
    }

    [Fact]
    public async Task ViewModel_RunAsync_RaisesOutputReceivedEvent()
    {
        var received = new List<ProcessOutputLine>();
        var proc = new FakeProcessService
        {
            Handler = (_, listener, _) =>
            {
                listener?.Invoke(new ProcessOutputLine(ProcessOutputKind.StdOut, "line1"));
                return Task.FromResult(new ProcessResult(0, TimeSpan.Zero));
            }
        };
        var vm = new TerminalPanelViewModel(proc, new OutputService());
        vm.OutputReceived += line => received.Add(line);

        await vm.RunAsync("test");

        Assert.Single(received);
        Assert.Equal("line1", received[0].Text);
    }

    [Fact]
    public async Task ViewModel_RunAsync_RaisesProcessFinishedEvent()
    {
        ProcessResult? finished = null;
        var proc = new FakeProcessService();
        var vm = new TerminalPanelViewModel(proc, new OutputService());
        vm.ProcessFinished += r => finished = r;

        await vm.RunAsync("test");

        Assert.NotNull(finished);
        Assert.Equal(0, finished!.ExitCode);
    }

    [Fact]
    public async Task ViewModel_RunAsync_ClearsLinesOnNewRun()
    {
        var callCount = 0;
        var proc = new FakeProcessService
        {
            Handler = (_, listener, _) =>
            {
                callCount++;
                listener?.Invoke(new ProcessOutputLine(ProcessOutputKind.StdOut, $"run{callCount}"));
                return Task.FromResult(new ProcessResult(0, TimeSpan.Zero));
            }
        };
        var vm = new TerminalPanelViewModel(proc, new OutputService());

        await vm.RunAsync("test");
        Assert.Single(vm.Lines);

        await vm.RunAsync("test");
        Assert.Single(vm.Lines);
        Assert.Equal("run2", vm.Lines[0].Text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ViewModel_RunAsync_EmptyFileName_Throws(string? fileName)
    {
        var vm = new TerminalPanelViewModel(new FakeProcessService(), new OutputService());
        await Assert.ThrowsAsync<ArgumentException>(() => vm.RunAsync(fileName!));
    }

    [Fact]
    public async Task ViewModel_RunAsync_WhileRunning_Throws()
    {
        var tcs = new TaskCompletionSource<ProcessResult>();
        var proc = new FakeProcessService
        {
            Handler = (_, _, _) => tcs.Task
        };
        var vm = new TerminalPanelViewModel(proc, new OutputService());

        _ = vm.RunAsync("long"); // starts but doesn't finish

        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.RunAsync("second"));

        tcs.SetResult(new ProcessResult(0, TimeSpan.Zero)); // cleanup
    }

    [Fact]
    public async Task ViewModel_RunAsync_IsRunning_TrueDuringExecution()
    {
        var tcs = new TaskCompletionSource<ProcessResult>();
        var proc = new FakeProcessService
        {
            Handler = (_, _, _) => tcs.Task
        };
        var vm = new TerminalPanelViewModel(proc, new OutputService());

        var task = vm.RunAsync("test");

        Assert.True(vm.IsRunning);

        tcs.SetResult(new ProcessResult(0, TimeSpan.Zero));
        await task;

        Assert.False(vm.IsRunning);
    }

    // ── TerminalPanelViewModel: Cancel ──────────────────────────

    [Fact]
    public async Task ViewModel_Cancel_CancelsRunningProcess()
    {
        var proc = new FakeProcessService
        {
            Handler = async (_, _, ct) =>
            {
                await Task.Delay(10000, ct); // long wait — will be cancelled
                return new ProcessResult(0, TimeSpan.Zero);
            }
        };
        var vm = new TerminalPanelViewModel(proc, new OutputService());

        var task = vm.RunAsync("test");
        vm.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.False(vm.IsRunning);
    }

    [Fact]
    public void ViewModel_Cancel_WhenNotRunning_DoesNotThrow()
    {
        var vm = new TerminalPanelViewModel(new FakeProcessService(), new OutputService());
        vm.Cancel(); // Should not throw
    }

    // ── TerminalPanelViewModel: Output logging ──────────────────

    [Fact]
    public async Task ViewModel_RunAsync_LogsToOutput()
    {
        var output = new OutputService();
        output.RegisterChannel("Terminal");
        var proc = new FakeProcessService();
        var vm = new TerminalPanelViewModel(proc, output);

        await vm.RunAsync("echo", "hello");

        var snapshot = output.GetSnapshot();
        Assert.Contains(snapshot, e => e.Channel == "Terminal" && e.Message.Contains("echo"));
        Assert.Contains(snapshot, e => e.Channel == "Terminal" && e.Message.Contains("exited"));
    }

    // ── TerminalExtension: initialize ───────────────────────────

    [Fact]
    public void Extension_Initialize_RegistersTerminalPanel()
    {
        var ctx = CreateContext();
        var ext = new TerminalExtension();

        ext.Initialize(ctx);

        var panels = ctx.Panels.GetRegisteredPanels();
        Assert.Contains(panels, p => p.Id == "junexis.terminal");
    }

    [Fact]
    public void Extension_Initialize_RegistersRunCommand()
    {
        var ctx = CreateContext();
        var ext = new TerminalExtension();

        ext.Initialize(ctx);

        Assert.True(ctx.Commands.CanExecute("terminal.run"));
    }

    [Fact]
    public void Extension_Initialize_OutputsLoadedMessage()
    {
        var output = new OutputService();
        var ctx = CreateContext(output: output);
        var ext = new TerminalExtension();

        ext.Initialize(ctx);

        var snapshot = output.GetSnapshot();
        Assert.Contains(snapshot, e =>
            e.Channel == "Terminal" && e.Message.Contains("loaded"));
    }

    [Fact]
    public void Extension_Id_IsJunexisTerminal()
    {
        var ext = new TerminalExtension();
        Assert.Equal("junexis.terminal", ext.Id);
    }

    // ── TerminalExtension: shutdown ─────────────────────────────

    [Fact]
    public void Extension_Shutdown_IsSafe()
    {
        var ext = new TerminalExtension();
        ext.Shutdown(); // no-op without Init
    }

    [Fact]
    public void Extension_Shutdown_AfterInit_CancelsRunningProcess()
    {
        var ctx = CreateContext();
        var ext = new TerminalExtension();
        ext.Initialize(ctx);
        ext.Shutdown(); // Should not throw
    }

    // ── RunCommandLineAsync (#108) ───────────────────────────────

    [Fact]
    [Trait("Issue", "108")]
    public async Task RunCommandLineAsync_SimpleCommand_DelegatesToRunAsync()
    {
        ProcessRequest? captured = null;
        var proc = new FakeProcessService
        {
            Handler = (req, _, _) =>
            {
                captured = req;
                return Task.FromResult(new ProcessResult(0, TimeSpan.Zero));
            }
        };
        var vm = new TerminalPanelViewModel(proc, new OutputService());

        await vm.RunCommandLineAsync("dotnet build");

        Assert.NotNull(captured);
        // RunCommandLineAsync owrapowuje polecenie w powłokę systemową (cmd.exe na Windows, /bin/sh na innych)
        var isWindows = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
            System.Runtime.InteropServices.OSPlatform.Windows);
        Assert.Equal(isWindows ? "cmd.exe" : "/bin/sh", captured!.FileName);
        Assert.Contains("dotnet build", captured.Arguments);
    }

    [Fact]
    [Trait("Issue", "108")]
    public async Task RunCommandLineAsync_CommandWithMultipleArgs_PassesRestAsArguments()
    {
        ProcessRequest? captured = null;
        var proc = new FakeProcessService
        {
            Handler = (req, _, _) =>
            {
                captured = req;
                return Task.FromResult(new ProcessResult(0, TimeSpan.Zero));
            }
        };
        var vm = new TerminalPanelViewModel(proc, new OutputService());

        await vm.RunCommandLineAsync("dotnet test --nologo -v q");

        Assert.NotNull(captured);
        var isWindows = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
            System.Runtime.InteropServices.OSPlatform.Windows);
        Assert.Equal(isWindows ? "cmd.exe" : "/bin/sh", captured!.FileName);
        Assert.Contains("dotnet test --nologo -v q", captured.Arguments);
    }

    [Fact]
    [Trait("Issue", "108")]
    public async Task RunCommandLineAsync_NoArguments_PassesRestAsArguments()
    {
        ProcessRequest? captured = null;
        var proc = new FakeProcessService
        {
            Handler = (req, _, _) =>
            {
                captured = req;
                return Task.FromResult(new ProcessResult(0, TimeSpan.Zero));
            }
        };
        var vm = new TerminalPanelViewModel(proc, new OutputService());

        await vm.RunCommandLineAsync("ls");

        Assert.NotNull(captured);
        var isWindows = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
            System.Runtime.InteropServices.OSPlatform.Windows);
        Assert.Equal(isWindows ? "cmd.exe" : "/bin/sh", captured!.FileName);
        Assert.Contains("ls", captured.Arguments);
    }

    [Fact]
    [Trait("Issue", "108")]
    public async Task RunCommandLineAsync_EmptyString_Throws()
    {
        var vm = new TerminalPanelViewModel(new FakeProcessService(), new OutputService());
        await Assert.ThrowsAsync<ArgumentException>(() => vm.RunCommandLineAsync(""));
        await Assert.ThrowsAsync<ArgumentException>(() => vm.RunCommandLineAsync("   "));
    }

    [Fact]
    [Trait("Issue", "108")]
    public async Task RunCommandLineAsync_CollectsOutputLines()
    {
        var proc = new FakeProcessService
        {
            Handler = (_, listener, _) =>
            {
                listener?.Invoke(new ProcessOutputLine(ProcessOutputKind.StdOut, "hello"));
                listener?.Invoke(new ProcessOutputLine(ProcessOutputKind.StdErr, "warning"));
                return Task.FromResult(new ProcessResult(0, TimeSpan.Zero));
            }
        };
        var vm = new TerminalPanelViewModel(proc, new OutputService());

        var result = await vm.RunCommandLineAsync("echo hello");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(2, vm.Lines.Count);
        Assert.Equal("hello", vm.Lines[0].Text);
        Assert.Equal("warning", vm.Lines[1].Text);
        Assert.Equal(ProcessOutputKind.StdErr, vm.Lines[1].Kind);
    }
}

using Junexis.Studio.Abstractions;
using Junexis.Studio.Host;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Junexis.Studio.Host.Tests;

public sealed class ProcessServiceTests
{
    [Fact]
    [Trait("Issue", "17")]
    public async Task RunAsync_StreamsStdOutAndStdErr()
    {
        var sut = new ProcessService();
        var lines = new ConcurrentQueue<ProcessOutputLine>();

        var request = CreateEchoRequest(stdout: "out", stderr: "err");

        var result = await sut.RunAsync(request, l => lines.Enqueue(l), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);

        var stdout = lines.Where(l => l.Kind == ProcessOutputKind.StdOut).Select(l => l.Text).ToArray();
        var stderr = lines.Where(l => l.Kind == ProcessOutputKind.StdErr).Select(l => l.Text).ToArray();

        Assert.Contains(stdout, s => s.Contains("out", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(stderr, s => s.Contains("err", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    [Trait("Issue", "17")]
    public async Task RunAsync_WhenCancelled_StopsProcess()
    {
        var sut = new ProcessService();
        var request = CreateSleepRequest(seconds: 10);

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await sut.RunAsync(request, listener: null, cts.Token));
    }

    [Fact]
    [Trait("Milestone", "M17.11.3")]
    public async Task RunAsync_WhenCancelled_StopsEntireProcessTree()
    {
        var sut = new ProcessService();
        var childPid = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = new CancellationTokenSource();
        Process? child = null;
        try
        {
            var run = sut.RunAsync(
                CreateParentWithChildRequest(),
                line =>
                {
                    if (line.Kind == ProcessOutputKind.StdOut && int.TryParse(line.Text, out var pid))
                        childPid.TrySetResult(pid);
                },
                cts.Token);
            child = Process.GetProcessById(await childPid.Task.WaitAsync(TimeSpan.FromSeconds(5)));

            await cts.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
            for (var attempt = 0; attempt < 100 && !child.HasExited; attempt++)
                await Task.Delay(20);

            Assert.True(child.HasExited);
        }
        finally
        {
            if (child is { HasExited: false })
                child.Kill(entireProcessTree: true);
            child?.Dispose();
        }
    }

    private static ProcessRequest CreateEchoRequest(string stdout, string stderr)
    {
        if (OperatingSystem.IsWindows())
        {
            // cmd: echo out & echo err 1>&2
            return new ProcessRequest(
                fileName: "cmd.exe",
                arguments: $"/c \"echo {stdout} & echo {stderr} 1>&2\"");
        }

        return new ProcessRequest(
            fileName: "/bin/bash",
            arguments: $"-c \"echo {stdout}; echo {stderr} 1>&2\"");
    }

    private static ProcessRequest CreateSleepRequest(int seconds)
    {
        if (OperatingSystem.IsWindows())
        {
            return new ProcessRequest(
                fileName: "powershell",
                arguments: $"-NoProfile -Command \"Start-Sleep -Seconds {seconds}\"");
        }

        return new ProcessRequest(
            fileName: "/bin/bash",
            arguments: $"-c \"sleep {seconds}\"");
    }

    private static ProcessRequest CreateParentWithChildRequest()
    {
        if (OperatingSystem.IsWindows())
        {
            const string script =
                "$child = Start-Process -FilePath 'powershell.exe' -ArgumentList '-NoProfile -Command Start-Sleep -Seconds 30' -PassThru -WindowStyle Hidden; " +
                "Write-Output $child.Id; Start-Sleep -Seconds 30";
            return new ProcessRequest("powershell.exe", ["-NoProfile", "-Command", script]);
        }

        return new ProcessRequest("/bin/bash", ["-c", "sleep 30 & echo $!; wait"]);
    }
}

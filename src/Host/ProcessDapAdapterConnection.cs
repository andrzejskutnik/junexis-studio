using System.Diagnostics;
using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Host;

internal interface IDapAdapterConnection : IAsyncDisposable
{
    Stream Input { get; }
    Stream Output { get; }
}

internal interface IMultiplexDapAdapterConnection : IDapAdapterConnection
{
    Task<IDapAdapterConnection> OpenAdditionalConnectionAsync(CancellationToken cancellationToken);
}

internal sealed class ProcessDapAdapterConnection : IDapAdapterConnection
{
    private readonly Process _process;
    private readonly CancellationTokenSource _stderrLifetime = new();
    private readonly Task _stderrReader;

    private ProcessDapAdapterConnection(Process process, Action<DebugOutputEvent>? output)
    {
        _process = process;
        Input = process.StandardOutput.BaseStream;
        Output = process.StandardInput.BaseStream;
        _stderrReader = ReadStderrAsync(process.StandardError, output, _stderrLifetime.Token);
    }

    public Stream Input { get; }

    public Stream Output { get; }

    public static Task<ProcessDapAdapterConnection> StartAsync(
        DebugSessionConfiguration configuration,
        Action<DebugOutputEvent>? output,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var startInfo = new ProcessStartInfo
        {
            FileName = configuration.AdapterFileName,
            Arguments = configuration.AdapterArguments,
            WorkingDirectory = configuration.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        if (!process.Start())
        {
            process.Dispose();
            throw new InvalidOperationException("Failed to start DAP adapter process.");
        }
        return Task.FromResult(new ProcessDapAdapterConnection(process, output));
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!_process.HasExited)
                _process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Best effort during debugger shutdown.
        }
        _stderrLifetime.Cancel();
        try
        {
            await _stderrReader.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        _stderrLifetime.Dispose();
        _process.Dispose();
    }

    private static async Task ReadStderrAsync(
        StreamReader reader,
        Action<DebugOutputEvent>? output,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
                return;
            output?.Invoke(new DebugOutputEvent("adapter.stderr", line));
        }
    }
}

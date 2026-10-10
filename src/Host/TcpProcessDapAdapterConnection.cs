using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Host;

internal sealed class TcpProcessDapAdapterConnection : IMultiplexDapAdapterConnection
{
    private readonly Process _process;
    private readonly TcpClient _client;
    private readonly CancellationTokenSource _logLifetime = new();
    private readonly Task _stdoutReader;
    private readonly Task _stderrReader;
    private readonly int _port;

    private TcpProcessDapAdapterConnection(
        Process process,
        TcpClient client,
        int port,
        Action<DebugOutputEvent>? output)
    {
        _process = process;
        _client = client;
        _port = port;
        Input = client.GetStream();
        Output = client.GetStream();
        _stdoutReader = ReadLogAsync(process.StandardOutput, "adapter.stdout", output, _logLifetime.Token);
        _stderrReader = ReadLogAsync(process.StandardError, "adapter.stderr", output, _logLifetime.Token);
    }

    public Stream Input { get; }

    public Stream Output { get; }

    public static async Task<IDapAdapterConnection> StartAsync(
        DebugSessionConfiguration configuration,
        Action<DebugOutputEvent>? output,
        CancellationToken cancellationToken)
    {
        if (!configuration.AdapterArguments.Contains("{port}", StringComparison.Ordinal))
            throw new InvalidOperationException("TCP DAP adapter arguments must contain the {port} placeholder.");
        var port = ReservePort();
        var arguments = configuration.AdapterArguments.Replace("{port}", port.ToString(), StringComparison.Ordinal);
        var process = new Process
        {
            EnableRaisingEvents = true,
            StartInfo = new ProcessStartInfo
            {
                FileName = configuration.AdapterFileName,
                Arguments = arguments,
                WorkingDirectory = configuration.WorkingDirectory,
                UseShellExecute = false,
                RedirectStandardInput = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
        };
        if (!process.Start())
        {
            process.Dispose();
            throw new InvalidOperationException("Failed to start TCP DAP adapter process.");
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));
            var client = await ConnectWithRetryAsync(port, process, timeout.Token).ConfigureAwait(false);
            return new TcpProcessDapAdapterConnection(process, client, port, output);
        }
        catch
        {
            TryKill(process);
            process.Dispose();
            throw;
        }
    }

    public async Task<IDapAdapterConnection> OpenAdditionalConnectionAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        var client = await ConnectWithRetryAsync(_port, _process, timeout.Token).ConfigureAwait(false);
        return new ChildTcpDapAdapterConnection(client);
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        TryKill(_process);
        _logLifetime.Cancel();
        try
        {
            await Task.WhenAll(_stdoutReader, _stderrReader).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        _logLifetime.Dispose();
        _process.Dispose();
    }

    private static int ReservePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static async Task<TcpClient> ConnectWithRetryAsync(
        int port,
        Process process,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (process.HasExited)
                throw new InvalidOperationException($"DAP adapter exited before opening TCP port {port}.");
            foreach (var address in LoopbackAddresses())
            {
                var client = new TcpClient(address.AddressFamily);
                try
                {
                    await client.ConnectAsync(address, port, cancellationToken).ConfigureAwait(false);
                    return client;
                }
                catch (SocketException)
                {
                    client.Dispose();
                }
            }
            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
        }
    }

    private static IEnumerable<IPAddress> LoopbackAddresses()
    {
        yield return IPAddress.Loopback;
        if (Socket.OSSupportsIPv6)
            yield return IPAddress.IPv6Loopback;
    }

    private static async Task ReadLogAsync(
        StreamReader reader,
        string category,
        Action<DebugOutputEvent>? output,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
                return;
            output?.Invoke(new DebugOutputEvent(category, line));
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Best effort during connection failure and shutdown.
        }
    }

    private sealed class ChildTcpDapAdapterConnection(TcpClient client) : IDapAdapterConnection
    {
        public Stream Input { get; } = client.GetStream();
        public Stream Output { get; } = client.GetStream();
        public ValueTask DisposeAsync()
        {
            client.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}

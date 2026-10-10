using Junexis.Studio.Abstractions;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Junexis.Studio.Host;

/// <summary>
/// Implementacja <see cref="IProcessService"/> oparta o <see cref="Process"/>.
/// </summary>
public sealed class ProcessService : IProcessService
{
    /// <inheritdoc />
    public async Task<ProcessResult> RunAsync(
        ProcessRequest request,
        Action<ProcessOutputLine>? listener,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var startedAt = Stopwatch.StartNew();

        var psi = new ProcessStartInfo
        {
            FileName = request.FileName,
            WorkingDirectory = string.IsNullOrWhiteSpace(request.WorkingDirectory) ? string.Empty : request.WorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = request.CreateNoWindow,
        };
        if (request.ArgumentList is { } argumentList)
        {
            foreach (var argument in argumentList)
                psi.ArgumentList.Add(argument);
        }
        else
        {
            psi.Arguments = request.Arguments;
        }
        foreach (var variable in request.EnvironmentVariables)
            psi.Environment[variable.Key] = variable.Value;

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to start process.");
        }

        var stdoutTask = ReadLinesAsync(process.StandardOutput, ProcessOutputKind.StdOut, listener, CancellationToken.None);
        var stderrTask = ReadLinesAsync(process.StandardError, ProcessOutputKind.StdErr, listener, CancellationToken.None);

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);

            // Dokończ czytanie strumieni, żeby nie zostawić wiszących tasków.
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);

            throw;
        }

        await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);

        startedAt.Stop();
        return new ProcessResult(process.ExitCode, startedAt.Elapsed);
    }

    private static async Task ReadLinesAsync(
        StreamReader reader,
        ProcessOutputKind kind,
        Action<ProcessOutputLine>? listener,
        CancellationToken cancellationToken)
    {
        if (listener is null)
        {
            // Jeśli nikt nie słucha, czytamy do końca (żeby nie zablokować procesu), ale nic nie emitujemy.
            while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is not null)
            {
            }

            return;
        }

        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                return;
            }

            listener(new ProcessOutputLine(kind, line));
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (process.HasExited)
            {
                return;
            }

            process.Kill(entireProcessTree: true);
        }
        catch
        {
            // best-effort
        }
    }
}

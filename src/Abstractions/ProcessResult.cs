using System;

namespace Junexis.Studio.Abstractions;

/// <summary>
/// Wynik uruchomienia procesu.
/// </summary>
public sealed record ProcessResult
{
    /// <summary>
    /// Tworzy wynik procesu.
    /// </summary>
    /// <param name="exitCode">Kod wyjścia procesu.</param>
    /// <param name="duration">Czas wykonania.</param>
    public ProcessResult(int exitCode, TimeSpan duration)
    {
        ExitCode = exitCode;
        Duration = duration;
    }

    /// <summary>
    /// Kod wyjścia procesu.
    /// </summary>
    public int ExitCode { get; }

    /// <summary>
    /// Czas wykonania.
    /// </summary>
    public TimeSpan Duration { get; }
}

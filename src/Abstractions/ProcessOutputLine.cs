using System;

namespace Junexis.Studio.Abstractions;

/// <summary>
/// Pojedyncza linia outputu procesu.
/// </summary>
public sealed record ProcessOutputLine
{
    /// <summary>
    /// Tworzy nową linię outputu.
    /// </summary>
    /// <param name="kind">Źródło linii (stdout/stderr).</param>
    /// <param name="text">Tekst linii (bez znaku końca linii).</param>
    public ProcessOutputLine(ProcessOutputKind kind, string text)
    {
        Kind = kind;
        Text = text ?? throw new ArgumentNullException(nameof(text));
        TimestampUtc = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Źródło linii (stdout/stderr).
    /// </summary>
    public ProcessOutputKind Kind { get; }

    /// <summary>
    /// Tekst linii (bez znaku końca linii).
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// Znacznik czasu (UTC) w momencie odebrania linii.
    /// </summary>
    public DateTimeOffset TimestampUtc { get; }
}

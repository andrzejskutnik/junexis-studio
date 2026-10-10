using System;

namespace Junexis.Studio.Abstractions;

/// <summary>
/// Żądanie nawigacji do konkretnej lokacji w pliku (np. po aktywacji wpisu w Problems).
/// </summary>
public sealed record NavigateToLocationRequested
{
    /// <summary>
    /// Tworzy żądanie nawigacji.
    /// </summary>
    /// <param name="path">Pełna ścieżka do pliku.</param>
    /// <param name="line">Linia (1-based).</param>
    /// <param name="column">Kolumna (1-based).</param>
    /// <exception cref="ArgumentException">Gdy <paramref name="path"/> jest puste.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Gdy <paramref name="line"/> lub <paramref name="column"/> jest mniejsze niż 1.</exception>
    public NavigateToLocationRequested(string path, int line, int column)
    {
        Path = !string.IsNullOrWhiteSpace(path)
            ? path
            : throw new ArgumentException("Path cannot be empty.", nameof(path));

        Line = line >= 1 ? line : throw new ArgumentOutOfRangeException(nameof(line));
        Column = column >= 1 ? column : throw new ArgumentOutOfRangeException(nameof(column));
    }

    /// <summary>
    /// Pełna ścieżka do pliku.
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// Linia (1-based).
    /// </summary>
    public int Line { get; }

    /// <summary>
    /// Kolumna (1-based).
    /// </summary>
    public int Column { get; }
}

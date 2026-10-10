using System;

namespace Junexis.Studio.Abstractions;

/// <summary>
/// Wpis statusu pliku w repozytorium.
/// </summary>
public sealed record VersionControlFileEntry
{
    /// <summary>
    /// Tworzy wpis statusu.
    /// </summary>
    /// <param name="path">Ścieżka (względem root repozytorium).</param>
    /// <param name="status">Status pliku.</param>
    public VersionControlFileEntry(string path, VersionControlFileStatus status)
    {
        Path = path ?? throw new ArgumentNullException(nameof(path));
        Status = status;
    }

    /// <summary>
    /// Ścieżka względna do root repozytorium.
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// Status pliku.
    /// </summary>
    public VersionControlFileStatus Status { get; }
}

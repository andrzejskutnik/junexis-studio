namespace Junexis.Studio.Abstractions;

/// <summary>
/// Provides blame and history information for files under version control.
/// </summary>
public interface IBlameHistoryService
{
    /// <summary>
    /// Gets blame information (line-by-line authorship) for a file.
    /// </summary>
    /// <param name="filePath">Relative or absolute path to the file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Blame result with per-line entries.</returns>
    Task<BlameResult> GetBlameAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets commit history (log) for a file.
    /// </summary>
    /// <param name="filePath">Relative or absolute path to the file.</param>
    /// <param name="maxEntries">Maximum number of log entries to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>History result with log entries.</returns>
    Task<HistoryResult> GetHistoryAsync(string filePath, int maxEntries = 50, CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of a blame operation.
/// </summary>
/// <param name="FilePath">The file that was blamed.</param>
/// <param name="Entries">Per-line blame entries.</param>
/// <param name="Error">Error message if the operation failed (null on success).</param>
public sealed record BlameResult(
    string FilePath,
    IReadOnlyList<BlameEntry> Entries,
    string? Error = null)
{
    /// <summary>Whether the blame succeeded.</summary>
    public bool Success => Error is null;
}

/// <summary>
/// A single blame entry (one line or range of lines).
/// </summary>
/// <param name="LineNumber">1-based line number.</param>
/// <param name="CommitId">Short commit hash.</param>
/// <param name="Author">Author name.</param>
/// <param name="Date">Commit date.</param>
/// <param name="LineContent">Content of the line.</param>
public sealed record BlameEntry(
    int LineNumber,
    string CommitId,
    string Author,
    DateTime Date,
    string LineContent);

/// <summary>
/// Result of a history (log) operation.
/// </summary>
/// <param name="FilePath">The file whose history was queried.</param>
/// <param name="Entries">Log entries (newest first).</param>
/// <param name="Error">Error message if the operation failed (null on success).</param>
public sealed record HistoryResult(
    string FilePath,
    IReadOnlyList<HistoryEntry> Entries,
    string? Error = null)
{
    /// <summary>Whether the history query succeeded.</summary>
    public bool Success => Error is null;
}

/// <summary>
/// A single commit history entry.
/// </summary>
/// <param name="CommitId">Full or short commit hash.</param>
/// <param name="Author">Author name.</param>
/// <param name="Date">Commit date.</param>
/// <param name="Message">Commit message (first line).</param>
public sealed record HistoryEntry(
    string CommitId,
    string Author,
    DateTime Date,
    string Message);

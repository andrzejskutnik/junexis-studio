namespace Junexis.Studio.Abstractions;

/// <summary>
/// Specifies which reference to diff against.
/// </summary>
public enum DiffTarget
{
    /// <summary>Working tree vs index (staged).</summary>
    WorkingTreeVsIndex,
    /// <summary>Working tree vs HEAD commit.</summary>
    WorkingTreeVsHead,
    /// <summary>Index (staged) vs HEAD commit.</summary>
    IndexVsHead
}

/// <summary>
/// Provides diff (unified patch) information for files under version control.
/// </summary>
public interface IDiffService
{
    /// <summary>
    /// Gets a unified diff for the given file.
    /// </summary>
    /// <param name="filePath">Relative or absolute path to the file.</param>
    /// <param name="target">Which comparison to perform.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="DiffResult"/> containing the hunks or an error.</returns>
    Task<DiffResult> GetDiffAsync(string filePath, DiffTarget target = DiffTarget.WorkingTreeVsHead, CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of a diff operation for a single file.
/// </summary>
/// <param name="FilePath">The file that was diffed.</param>
/// <param name="Target">The comparison target used.</param>
/// <param name="Hunks">Diff hunks (empty if no changes or binary).</param>
/// <param name="IsBinary">Whether the file was detected as binary.</param>
/// <param name="Error">Error message if the operation failed (null on success).</param>
public sealed record DiffResult(
    string FilePath,
    DiffTarget Target,
    IReadOnlyList<DiffHunk> Hunks,
    bool IsBinary = false,
    string? Error = null)
{
    /// <summary>Whether the diff succeeded (no error).</summary>
    public bool Success => Error is null;

    /// <summary>Whether any changes were detected.</summary>
    public bool HasChanges => Hunks.Count > 0 || IsBinary;
}

/// <summary>
/// A single hunk from a unified diff.
/// </summary>
/// <param name="OldStart">Start line in the old (base) version.</param>
/// <param name="OldCount">Number of lines in the old version.</param>
/// <param name="NewStart">Start line in the new (working) version.</param>
/// <param name="NewCount">Number of lines in the new version.</param>
/// <param name="Lines">Individual diff lines within the hunk.</param>
public sealed record DiffHunk(
    int OldStart,
    int OldCount,
    int NewStart,
    int NewCount,
    IReadOnlyList<DiffLine> Lines);

/// <summary>
/// Represents a single line inside a diff hunk.
/// </summary>
/// <param name="Kind">Whether the line is context, added, or removed.</param>
/// <param name="Content">Text content of the line.</param>
public sealed record DiffLine(DiffLineKind Kind, string Content);

/// <summary>
/// The kind of a line within a diff hunk.
/// </summary>
public enum DiffLineKind
{
    /// <summary>Unchanged context line.</summary>
    Context,
    /// <summary>Added line.</summary>
    Added,
    /// <summary>Removed line.</summary>
    Removed
}

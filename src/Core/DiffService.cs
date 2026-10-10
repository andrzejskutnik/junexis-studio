namespace Junexis.Studio.Core;

using Junexis.Studio.Abstractions;

/// <summary>
/// Default <see cref="IDiffService"/> implementation with a pluggable diff backend.
/// Reports progress and errors through <see cref="IOutput"/> on the "VCS" channel.
/// </summary>
public sealed class DiffService : IDiffService
{
    /// <summary>
    /// Backend delegate that produces a <see cref="DiffResult"/> for a given path and target.
    /// </summary>
    public Func<string, DiffTarget, CancellationToken, Task<DiffResult>>? DiffBackend { get; set; }

    private readonly IOutput _output;

    /// <summary>
    /// Initializes a new <see cref="DiffService"/>.
    /// </summary>
    /// <param name="output">Output service for logging.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="output"/> is null.</exception>
    public DiffService(IOutput output)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _output.RegisterChannel("VCS");
    }

    /// <inheritdoc />
    public async Task<DiffResult> GetDiffAsync(string filePath, DiffTarget target = DiffTarget.WorkingTreeVsHead, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path must not be empty.", nameof(filePath));

        cancellationToken.ThrowIfCancellationRequested();

        if (DiffBackend is null)
        {
            var error = "No diff backend configured.";
            _output.Write("VCS", error);
            return new DiffResult(filePath, target, Array.Empty<DiffHunk>(), Error: error);
        }

        try
        {
            _output.Write("VCS", $"Diff: {filePath} ({target})");
            var result = await DiffBackend(filePath, target, cancellationToken).ConfigureAwait(false);

            if (result.IsBinary)
                _output.Write("VCS", $"Binary file detected: {filePath}");
            else if (!result.HasChanges)
                _output.Write("VCS", $"No changes: {filePath}");
            else
                _output.Write("VCS", $"Diff ready: {filePath} — {result.Hunks.Count} hunk(s)");

            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var error = $"Diff failed for {filePath}: {ex.Message}";
            _output.Write("VCS", error);
            return new DiffResult(filePath, target, Array.Empty<DiffHunk>(), Error: error);
        }
    }
}

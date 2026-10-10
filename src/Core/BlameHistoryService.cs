using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Core;

/// <summary>
/// Provides blame and history information by delegating to a pluggable backend
/// (e.g. git CLI). Results are logged to the Output "VCS" channel.
/// </summary>
public sealed class BlameHistoryService : IBlameHistoryService
{
    private readonly IOutput? _output;

    /// <summary>
    /// Delegate that executes "git blame" or equivalent and returns per-line entries.
    /// </summary>
    public Func<string, CancellationToken, Task<IReadOnlyList<BlameEntry>>>? BlameBackend { get; set; }

    /// <summary>
    /// Delegate that executes "git log" or equivalent and returns history entries.
    /// </summary>
    public Func<string, int, CancellationToken, Task<IReadOnlyList<HistoryEntry>>>? HistoryBackend { get; set; }

    /// <summary>Output channel for blame/history messages.</summary>
    public const string OutputChannel = "VCS";

    /// <summary>
    /// Creates the blame/history service.
    /// </summary>
    /// <param name="output">Optional output for logging.</param>
    public BlameHistoryService(IOutput? output = null)
    {
        _output = output;
    }

    /// <inheritdoc />
    public async Task<BlameResult> GetBlameAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path is required.", nameof(filePath));

        if (BlameBackend is null)
        {
            return new BlameResult(filePath, [], "No blame backend configured.");
        }

        try
        {
            var entries = await BlameBackend(filePath, cancellationToken);
            _output?.Write(OutputChannel,
                $"Blame: {filePath} ({entries.Count} line(s)).",
                OutputSeverity.Info);
            return new BlameResult(filePath, entries);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _output?.Write(OutputChannel,
                $"Blame failed for {filePath}: {ex.Message}",
                OutputSeverity.Error);
            return new BlameResult(filePath, [], ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<HistoryResult> GetHistoryAsync(string filePath, int maxEntries = 50, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path is required.", nameof(filePath));
        if (maxEntries <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxEntries), "Must be positive.");

        if (HistoryBackend is null)
        {
            return new HistoryResult(filePath, [], "No history backend configured.");
        }

        try
        {
            var entries = await HistoryBackend(filePath, maxEntries, cancellationToken);
            _output?.Write(OutputChannel,
                $"History: {filePath} ({entries.Count} commit(s)).",
                OutputSeverity.Info);
            return new HistoryResult(filePath, entries);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _output?.Write(OutputChannel,
                $"History failed for {filePath}: {ex.Message}",
                OutputSeverity.Error);
            return new HistoryResult(filePath, [], ex.Message);
        }
    }
}

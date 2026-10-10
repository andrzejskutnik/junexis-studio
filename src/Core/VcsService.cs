using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Core;

/// <summary>
/// In-memory implementation of <see cref="IVcsService"/> for v0.1.
/// Validates commit operations and publishes events to <see cref="IEventBus"/>.
/// Uses a pluggable backend delegate for the actual VCS operations.
/// </summary>
public sealed class VcsService : IVcsService
{
    /// <summary>
    /// Delegate for executing the underlying VCS commit operation.
    /// </summary>
    /// <param name="stagedFiles">List of staged file paths.</param>
    /// <param name="message">The commit message.</param>
    /// <returns>The commit result from the backend.</returns>
    public delegate VcsCommitResult CommitBackend(IReadOnlyList<string> stagedFiles, string message);

    private readonly CommitBackend _backend;
    private readonly IEventBus? _eventBus;
    private readonly IOutput? _output;
    private readonly List<VcsFileChange> _changes = new();
    private readonly object _lock = new();

    /// <summary>Output channel name for VCS messages.</summary>
    public const string OutputChannel = "VCS";

    /// <summary>
    /// Creates a new <see cref="VcsService"/>.
    /// </summary>
    /// <param name="backend">Backend delegate for actual commit operations.</param>
    /// <param name="eventBus">Optional event bus for publishing VCS events.</param>
    /// <param name="output">Optional output service for logging.</param>
    /// <exception cref="ArgumentNullException">When <paramref name="backend"/> is null.</exception>
    public VcsService(CommitBackend backend, IEventBus? eventBus = null, IOutput? output = null)
    {
        ArgumentNullException.ThrowIfNull(backend);
        _backend = backend;
        _eventBus = eventBus;
        _output = output;
    }

    /// <summary>
    /// Sets the current list of file changes (typically called by a file watcher or status refresh).
    /// </summary>
    /// <param name="changes">The current file changes.</param>
    public void SetChanges(IEnumerable<VcsFileChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        lock (_lock)
        {
            _changes.Clear();
            _changes.AddRange(changes);
        }
        _eventBus?.Publish(new VcsStatusChangedEvent());
    }

    /// <inheritdoc />
    public IReadOnlyList<VcsFileChange> GetChanges()
    {
        lock (_lock) return _changes.ToList();
    }

    /// <inheritdoc />
    public void Stage(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        lock (_lock)
        {
            var idx = _changes.FindIndex(c => string.Equals(c.Path, path, StringComparison.OrdinalIgnoreCase));
            if (idx < 0)
                throw new InvalidOperationException($"File '{path}' not found in changes.");

            var change = _changes[idx];
            if (change.IsStaged) return;

            _changes[idx] = change with { IsStaged = true };
        }
        _output?.Write(OutputChannel, $"Staged: {path}.");
        _eventBus?.Publish(new VcsStatusChangedEvent());
    }

    /// <inheritdoc />
    public void Unstage(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        lock (_lock)
        {
            var idx = _changes.FindIndex(c => string.Equals(c.Path, path, StringComparison.OrdinalIgnoreCase));
            if (idx < 0)
                throw new InvalidOperationException($"File '{path}' not found in changes.");

            var change = _changes[idx];
            if (!change.IsStaged) return;

            _changes[idx] = change with { IsStaged = false };
        }
        _output?.Write(OutputChannel, $"Unstaged: {path}.");
        _eventBus?.Publish(new VcsStatusChangedEvent());
    }

    /// <inheritdoc />
    public VcsCommitResult Commit(string message)
    {
        // Validate message
        if (string.IsNullOrWhiteSpace(message))
        {
            var emptyMsg = "Commit message cannot be empty.";
            _output?.Write(OutputChannel, emptyMsg, OutputSeverity.Warning);
            return new VcsCommitResult(false, Error: emptyMsg);
        }

        // Check for staged files
        IReadOnlyList<string> stagedPaths;
        lock (_lock)
        {
            stagedPaths = _changes.Where(c => c.IsStaged).Select(c => c.Path).ToList();
        }

        if (stagedPaths.Count == 0)
        {
            var nothingStaged = "Nothing staged for commit.";
            _output?.Write(OutputChannel, nothingStaged, OutputSeverity.Warning);
            return new VcsCommitResult(false, Error: nothingStaged);
        }

        _output?.Write(OutputChannel, $"Committing {stagedPaths.Count} file(s): \"{message}\".", OutputSeverity.Info);

        VcsCommitResult result;
        try
        {
            result = _backend(stagedPaths, message);
        }
        catch (Exception ex)
        {
            var errMsg = $"Commit failed: {ex.Message}";
            _output?.Write(OutputChannel, errMsg, OutputSeverity.Error);
            return new VcsCommitResult(false, Error: errMsg);
        }

        if (result.Success)
        {
            _output?.Write(OutputChannel, $"Commit created: {result.CommitId}.", OutputSeverity.Info);

            // Remove committed files from the changes list
            lock (_lock)
            {
                _changes.RemoveAll(c => c.IsStaged);
            }

            _eventBus?.Publish(new VcsCommitCreatedEvent(result.CommitId!, message, DateTime.UtcNow));
            _eventBus?.Publish(new VcsStatusChangedEvent());
        }
        else
        {
            _output?.Write(OutputChannel, $"Commit failed: {result.Error}.", OutputSeverity.Error);
        }

        return result;
    }
}

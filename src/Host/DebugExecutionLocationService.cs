using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Host;

/// <summary>Tracks the first stack frame while a DAP session is stopped.</summary>
public sealed class DebugExecutionLocationService : IDebugExecutionLocationService
{
    private DebugExecutionLocation? _current;

    /// <summary>Creates an execution-location projection for a debug session.</summary>
    /// <param name="session">Debug session whose snapshots are observed.</param>
    public DebugExecutionLocationService(IDebugSessionService session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _current = Project(session.Snapshot);
        session.SnapshotChanged += OnSnapshotChanged;
    }

    /// <inheritdoc />
    public DebugExecutionLocation? Current => Volatile.Read(ref _current);

    /// <inheritdoc />
    public event Action? LocationChanged;

    private void OnSnapshotChanged(DebugSessionSnapshot snapshot)
    {
        var next = Project(snapshot);
        var previous = Interlocked.Exchange(ref _current, next);
        if (previous != next)
            LocationChanged?.Invoke();
    }

    private static DebugExecutionLocation? Project(DebugSessionSnapshot snapshot)
    {
        var frame = snapshot.State == DebugSessionState.Stopped
            ? snapshot.StackFrames.FirstOrDefault(item => !string.IsNullOrWhiteSpace(item.SourcePath))
            : null;
        return frame is null || frame.Line < 1 || string.IsNullOrWhiteSpace(frame.SourcePath)
            ? null
            : new DebugExecutionLocation(Path.GetFullPath(frame.SourcePath), frame.Line, Math.Max(frame.Column, 1));
    }
}

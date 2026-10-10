using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Core;

/// <summary>Thread-safe authoritative lifecycle store for the latest Workbench operation.</summary>
public sealed class WorkbenchOperationService : IWorkbenchOperationService
{
    private readonly object _gate = new();
    private WorkbenchOperationSnapshot _snapshot = WorkbenchOperationSnapshot.Idle;

    public WorkbenchOperationSnapshot Snapshot
    {
        get { lock (_gate) return _snapshot; }
    }

    public event Action<WorkbenchOperationSnapshot>? SnapshotChanged;

    public Guid Begin(string profileId, string profileName, LaunchOperation operation, string status)
    {
        var id = Guid.NewGuid();
        Publish(new(
            id, profileId, profileName, operation, WorkbenchOperationState.Starting,
            DateTimeOffset.UtcNow, null, 0, null, 0, status));
        return id;
    }

    public void MarkRunning(Guid operationId, string status) =>
        Update(operationId, current => current with
        {
            State = WorkbenchOperationState.Running,
            Status = status,
        });

    public void Complete(Guid operationId, int exitCode, int diagnosticCount, string status) =>
        Finish(
            operationId,
            exitCode == 0 ? WorkbenchOperationState.Succeeded : WorkbenchOperationState.Failed,
            status,
            exitCode,
            diagnosticCount);

    public void Fail(Guid operationId, string status, int? exitCode = null, int diagnosticCount = 0) =>
        Finish(operationId, WorkbenchOperationState.Failed, status, exitCode, diagnosticCount);

    public void Cancel(Guid operationId, string status) =>
        Finish(operationId, WorkbenchOperationState.Cancelled, status, null, 0);

    private void Finish(
        Guid operationId,
        WorkbenchOperationState state,
        string status,
        int? exitCode,
        int diagnosticCount)
    {
        var completed = DateTimeOffset.UtcNow;
        Update(operationId, current => current with
        {
            State = state,
            CompletedAt = completed,
            DurationMs = Math.Max(0, (long)(completed - current.StartedAt).TotalMilliseconds),
            ExitCode = exitCode,
            DiagnosticCount = Math.Max(0, diagnosticCount),
            Status = status,
        });
    }

    private void Update(
        Guid operationId,
        Func<WorkbenchOperationSnapshot, WorkbenchOperationSnapshot> update)
    {
        WorkbenchOperationSnapshot next;
        lock (_gate)
        {
            if (_snapshot.OperationId != operationId || operationId == Guid.Empty)
                return;
            next = update(_snapshot);
            _snapshot = next;
        }
        Notify(next);
    }

    private void Publish(WorkbenchOperationSnapshot snapshot)
    {
        lock (_gate)
            _snapshot = snapshot;
        Notify(snapshot);
    }

    private void Notify(WorkbenchOperationSnapshot snapshot)
    {
        var handlers = SnapshotChanged;
        if (handlers is null)
            return;
        foreach (Action<WorkbenchOperationSnapshot> handler in handlers.GetInvocationList())
        {
            try { handler(snapshot); }
            catch { /* One observer cannot corrupt the operation lifecycle. */ }
        }
    }
}

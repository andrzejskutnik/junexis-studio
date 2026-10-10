namespace Junexis.Studio.Abstractions;

/// <summary>Lifecycle state of the latest Build, Run, or Debug operation.</summary>
public enum WorkbenchOperationState
{
    Idle,
    Starting,
    Running,
    Succeeded,
    Failed,
    Cancelled,
}

/// <summary>Immutable authoritative snapshot of a Workbench operation.</summary>
public sealed record WorkbenchOperationSnapshot(
    Guid OperationId,
    string ProfileId,
    string ProfileName,
    LaunchOperation Operation,
    WorkbenchOperationState State,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    long DurationMs,
    int? ExitCode,
    int DiagnosticCount,
    string Status)
{
    public bool IsActive => State is WorkbenchOperationState.Starting or WorkbenchOperationState.Running;

    public static WorkbenchOperationSnapshot Idle { get; } = new(
        Guid.Empty, string.Empty, string.Empty, LaunchOperation.Build,
        WorkbenchOperationState.Idle, DateTimeOffset.MinValue, null, 0, null, 0, "Ready");
}

/// <summary>Tracks the latest operation independently of process and UI implementations.</summary>
public interface IWorkbenchOperationService
{
    WorkbenchOperationSnapshot Snapshot { get; }
    event Action<WorkbenchOperationSnapshot>? SnapshotChanged;
    Guid Begin(string profileId, string profileName, LaunchOperation operation, string status);
    void MarkRunning(Guid operationId, string status);
    void Complete(Guid operationId, int exitCode, int diagnosticCount, string status);
    void Fail(Guid operationId, string status, int? exitCode = null, int diagnosticCount = 0);
    void Cancel(Guid operationId, string status);
}

/// <summary>Small no-op-safe reporter used by compiler and debugger extensions.</summary>
public sealed class WorkbenchOperationHandle
{
    private readonly IWorkbenchOperationService? _service;

    private WorkbenchOperationHandle(IWorkbenchOperationService? service, Guid operationId)
    {
        _service = service;
        OperationId = operationId;
    }

    public Guid OperationId { get; }

    public static WorkbenchOperationHandle Begin(
        IHostContext context,
        string profileId,
        string profileName,
        LaunchOperation operation,
        string status)
    {
        context.Capabilities.TryGet<IWorkbenchOperationService>(out var service);
        var id = service?.Begin(profileId, profileName, operation, status) ?? Guid.Empty;
        return new WorkbenchOperationHandle(service, id);
    }

    public void Running(string status) => _service?.MarkRunning(OperationId, status);
    public void Complete(int exitCode, int diagnostics, string status) =>
        _service?.Complete(OperationId, exitCode, diagnostics, status);
    public void Fail(string status, int? exitCode = null, int diagnostics = 0) =>
        _service?.Fail(OperationId, status, exitCode, diagnostics);
    public void Cancel(string status) => _service?.Cancel(OperationId, status);
}

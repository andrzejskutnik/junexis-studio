using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;

namespace Junexis.Studio.Core.Tests;

public sealed class WorkbenchOperationServiceTests
{
    [Fact]
    public void Lifecycle_PublishesAuthoritativeTerminalSnapshot()
    {
        var service = new WorkbenchOperationService();
        var observed = new List<WorkbenchOperationSnapshot>();
        service.SnapshotChanged += observed.Add;

        var id = service.Begin("go", "Go", LaunchOperation.Build, "Starting Go build");
        service.MarkRunning(id, "Building Go");
        service.Complete(id, 0, 2, "Build succeeded");

        Assert.Equal(3, observed.Count);
        Assert.Equal(WorkbenchOperationState.Succeeded, service.Snapshot.State);
        Assert.Equal(2, service.Snapshot.DiagnosticCount);
        Assert.False(service.Snapshot.IsActive);
        Assert.True(service.Snapshot.DurationMs >= 0);
    }

    [Fact]
    public void StaleCompletion_CannotOverwriteNewerOperation()
    {
        var service = new WorkbenchOperationService();
        var oldId = service.Begin("cpp", "C / C++", LaunchOperation.Run, "Starting");
        var currentId = service.Begin("go", "Go", LaunchOperation.Run, "Starting");

        service.Complete(oldId, 0, 0, "Old completed");

        Assert.Equal(currentId, service.Snapshot.OperationId);
        Assert.Equal("go", service.Snapshot.ProfileId);
        Assert.True(service.Snapshot.IsActive);
    }
}

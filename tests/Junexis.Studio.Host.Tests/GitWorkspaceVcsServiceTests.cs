using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;
using Junexis.Studio.Host;

namespace Junexis.Studio.Host.Tests;

public sealed class GitWorkspaceVcsServiceTests
{
    [Fact]
    public async Task RefreshAsync_WithoutGitMarker_DoesNotActivateRepositoryContext()
    {
        var processes = CreateRepositoryProcess();
        using var workspace = new WorkspaceService();
        workspace.OpenFolder(Path.GetTempPath());
        using var service = new GitWorkspaceVcsService(
            workspace,
            processes,
            new EventBus(),
            new OutputService(),
            repositoryContextDetector: _ => false);

        await service.RefreshAsync();

        Assert.False(service.Snapshot.IsRepository);
        Assert.Equal(Path.GetFullPath(Path.GetTempPath()), service.Snapshot.RootPath);
        Assert.Empty(processes.Requests);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.StageAsync("file.cs"));
        Assert.Empty(processes.Requests);
    }

    [Fact]
    public async Task RefreshAsync_PublishesOneSnapshotWithIndependentIndexAndWorkTreeStates()
    {
        var processes = CreateRepositoryProcess();
        processes.Setup(
            "status --porcelain=v2",
            exit: 0,
            stdout: "# branch.oid abc1234\0# branch.head main\0"
                + "1 M. N... 100644 100644 100644 aaa bbb staged.cs\0"
                + "1 .M N... 100644 100644 100644 aaa bbb work.cs\0"
                + "1 MM N... 100644 100644 100644 aaa bbb both.cs\0"
                + "? new.cs\0");
        using var workspace = new WorkspaceService();
        workspace.OpenFolder(Path.GetTempPath());
        var bus = new EventBus();
        using var service = new GitWorkspaceVcsService(
            workspace,
            processes,
            bus,
            new OutputService(),
            repositoryContextDetector: _ => true);

        await service.RefreshAsync();

        Assert.True(service.Snapshot.IsRepository);
        Assert.Equal("main", service.Snapshot.Branch);
        Assert.Equal(4, service.Snapshot.Files.Count);
        var both = Assert.Single(service.Snapshot.Files, file => file.Path == "both.cs");
        Assert.Equal(VcsFileStatus.Modified, both.IndexStatus);
        Assert.Equal(VcsFileStatus.Modified, both.WorkTreeStatus);
        var untracked = Assert.Single(service.Snapshot.Files, file => file.Path == "new.cs");
        Assert.Null(untracked.IndexStatus);
        Assert.Equal(VcsFileStatus.Untracked, untracked.WorkTreeStatus);
    }

    [Fact]
    public async Task StageAsync_UsesRealGitIndexAndRefreshesSnapshot()
    {
        var processes = CreateRepositoryProcess();
        processes.Setup(
            "status --porcelain=v2",
            exit: 0,
            stdout: "1 .M N... 100644 100644 100644 aaa bbb file.cs\0");
        processes.Setup("add --", exit: 0);
        using var workspace = new WorkspaceService();
        workspace.OpenFolder(Path.GetTempPath());
        using var service = new GitWorkspaceVcsService(
            workspace,
            processes,
            new EventBus(),
            new OutputService(),
            repositoryContextDetector: _ => true);

        await service.StageAsync("file.cs");

        Assert.Contains(processes.Requests, request =>
            request.ArgumentList?.SequenceEqual(["add", "--", "file.cs"]) == true);
        Assert.Contains(processes.Requests, request =>
            request.Arguments.Contains("status --porcelain", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CommitAsync_NeverStagesAdditionalFiles()
    {
        var processes = CreateRepositoryProcess();
        processes.Setup("commit", exit: 0, stdout: "[main abc1234] safe\n");
        processes.Setup(
            "status --porcelain=v2",
            exit: 0,
            stdout: "1 M. N... 100644 100644 100644 aaa bbb staged.cs\0");
        processes.Setup("config --get user.name", exit: 0, stdout: "Junexis User");
        processes.Setup("config --get user.email", exit: 0, stdout: "user@example.test");
        processes.Setup("diff --cached --stat", exit: 0, stdout: "1 file changed, 1 insertion(+)");
        processes.SetupSequence(
            "rev-parse --verify HEAD",
            (0, "before", ""),
            (0, "after", ""));
        using var workspace = new WorkspaceService();
        workspace.OpenFolder(Path.GetTempPath());
        using var service = new GitWorkspaceVcsService(
            workspace,
            processes,
            new EventBus(),
            new OutputService(),
            repositoryContextDetector: _ => true);

        var result = await service.CommitAsync("safe");

        Assert.True(result.Success);
        Assert.DoesNotContain(processes.Requests, request =>
            request.Arguments.Contains("add -A", StringComparison.Ordinal));
        Assert.Contains(processes.Requests, request =>
            request.Arguments.Contains("commit -m", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OpeningWorkspace_RefreshesRepositoryLifecycle()
    {
        var processes = CreateRepositoryProcess();
        processes.Setup("status --porcelain=v2", exit: 0, stdout: "? opened.cs\0");
        using var workspace = new WorkspaceService();
        using var service = new GitWorkspaceVcsService(
            workspace,
            processes,
            new EventBus(),
            new OutputService(),
            repositoryContextDetector: _ => true);

        workspace.OpenFolder(Path.GetTempPath());
        await WaitUntilAsync(() => service.Snapshot.IsRepository);

        Assert.Equal(Path.GetFullPath(Path.GetTempPath()), service.Snapshot.RootPath);
        Assert.Contains(service.Snapshot.Files, file => file.Path == "opened.cs");
    }

    [Fact]
    public async Task RapidFileSaves_AreDebouncedIntoOneStatusRefresh()
    {
        var processes = CreateRepositoryProcess();
        processes.Setup("status --porcelain=v2", exit: 0, stdout: "");
        using var workspace = new WorkspaceService();
        workspace.OpenFolder(Path.GetTempPath());
        using var service = new GitWorkspaceVcsService(
            workspace,
            processes,
            new EventBus(),
            new OutputService(),
            repositoryContextDetector: _ => true);
        var fileName = $"junexis-vcs-{Guid.NewGuid():N}.tmp";

        try
        {
            workspace.WriteFile(fileName, "one");
            workspace.WriteFile(fileName, "two");
            workspace.WriteFile(fileName, "three");
            await WaitUntilAsync(() => processes.Requests.Count(request =>
                request.Arguments.Contains("status --porcelain=v2", StringComparison.Ordinal)) == 1);

            Assert.Equal(1, processes.Requests.Count(request =>
                request.Arguments.Contains("status --porcelain=v2", StringComparison.Ordinal)));
        }
        finally
        {
            File.Delete(Path.Combine(Path.GetTempPath(), fileName));
        }
    }

    [Fact]
    public async Task PrepareCommitAsync_ReportsAuthorAndCachedDiffStatistics()
    {
        var processes = CreateCommitReadyProcess(
            "1 M. N... 100644 100644 100644 aaa bbb staged.cs\0",
            "1 file changed, 4 insertions(+), 1 deletion(-)");
        using var workspace = new WorkspaceService();
        workspace.OpenFolder(Path.GetTempPath());
        using var service = new GitWorkspaceVcsService(
            workspace, processes, new EventBus(), new OutputService(),
            repositoryContextDetector: _ => true);

        var preflight = await service.PrepareCommitAsync("feat: validated");

        Assert.True(preflight.CanCommit);
        Assert.Equal(1, preflight.StagedFileCount);
        Assert.Equal("Junexis User", preflight.AuthorName);
        Assert.Equal("user@example.test", preflight.AuthorEmail);
        Assert.Contains("4 insertions", preflight.Statistics);
    }

    [Fact]
    public async Task PrepareCommitAsync_RejectsConflictAndMissingAuthor()
    {
        var conflictProcesses = CreateCommitReadyProcess(
            "u UU N... 100644 100644 100644 100644 aaa bbb ccc conflict.cs\0",
            "");
        using var workspace = new WorkspaceService();
        workspace.OpenFolder(Path.GetTempPath());
        using var conflictService = new GitWorkspaceVcsService(
            workspace, conflictProcesses, new EventBus(), new OutputService(),
            repositoryContextDetector: _ => true);

        var conflict = await conflictService.PrepareCommitAsync("fix: conflict");
        Assert.False(conflict.CanCommit);
        Assert.Contains("conflict", conflict.Error, StringComparison.OrdinalIgnoreCase);

        var missingAuthorProcesses = CreateRepositoryProcess();
        missingAuthorProcesses.Setup(
            "status --porcelain=v2",
            exit: 0,
            stdout: "1 M. N... 100644 100644 100644 aaa bbb staged.cs\0");
        missingAuthorProcesses.Setup("diff --cached --stat", exit: 0, stdout: "1 file changed");
        using var missingAuthorService = new GitWorkspaceVcsService(
            workspace, missingAuthorProcesses, new EventBus(), new OutputService(),
            repositoryContextDetector: _ => true);

        var missingAuthor = await missingAuthorService.PrepareCommitAsync("feat: author");
        Assert.False(missingAuthor.CanCommit);
        Assert.Contains("author", missingAuthor.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PrepareCommitAsync_ValidatesSubjectLengthAndStagedScope()
    {
        var processes = CreateCommitReadyProcess("", "");
        using var workspace = new WorkspaceService();
        workspace.OpenFolder(Path.GetTempPath());
        using var service = new GitWorkspaceVcsService(
            workspace, processes, new EventBus(), new OutputService(),
            repositoryContextDetector: _ => true);

        var noScope = await service.PrepareCommitAsync("feat: nothing staged");
        var longSubject = await service.PrepareCommitAsync(new string('x', 73));

        Assert.False(noScope.CanCommit);
        Assert.Contains("no staged", noScope.Error, StringComparison.OrdinalIgnoreCase);
        Assert.False(longSubject.CanCommit);
        Assert.Contains("72", longSubject.Error);
    }

    [Fact]
    public async Task LargeCommit_RequiresExplicitConfirmationBeforeGitCommit()
    {
        var processes = CreateCommitReadyProcess(
            "1 M. N... 100644 100644 100644 aaa bbb staged.cs\0",
            "1 file changed, 1000 insertions(+)");
        using var workspace = new WorkspaceService();
        workspace.OpenFolder(Path.GetTempPath());
        using var service = new GitWorkspaceVcsService(
            workspace, processes, new EventBus(), new OutputService(),
            repositoryContextDetector: _ => true);

        var preflight = await service.PrepareCommitAsync("feat: large");
        var result = await service.CommitAsync(new VcsCommitRequest("feat: large"));

        Assert.True(preflight.RequiresLargeChangeConfirmation);
        Assert.True(result.RequiresConfirmation);
        Assert.DoesNotContain(processes.Requests, request =>
            request.ArgumentList?.FirstOrDefault() == "commit");
    }

    [Fact]
    public async Task CommitTimeout_CancelsProcessAndReportsUncertainUnchangedHead()
    {
        var processes = CreateCommitReadyProcess(
            "1 M. N... 100644 100644 100644 aaa bbb staged.cs\0",
            "1 file changed, 1 insertion(+)");
        processes.BlockOnArgumentsContaining = "commit -m";
        processes.SetupSequence(
            "rev-parse --verify HEAD",
            (0, "same-head", ""),
            (0, "same-head", ""));
        using var workspace = new WorkspaceService();
        workspace.OpenFolder(Path.GetTempPath());
        using var service = new GitWorkspaceVcsService(
            workspace, processes, new EventBus(), new OutputService(),
            repositoryContextDetector: _ => true);

        var result = await service.CommitAsync(new VcsCommitRequest(
            "feat: timeout",
            Timeout: TimeSpan.FromMilliseconds(30)));

        Assert.False(result.Success);
        Assert.True(result.IsUncertain);
        Assert.Contains("timed out", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.True(processes.BlockedProcessStarted.Task.IsCompleted);
    }

    private static ScriptableProcessService CreateRepositoryProcess()
    {
        var processes = new ScriptableProcessService();
        processes.Setup("--version", exit: 0);
        processes.Setup("rev-parse --is-inside-work-tree", exit: 0);
        return processes;
    }

    private static ScriptableProcessService CreateCommitReadyProcess(
        string status,
        string statistics)
    {
        var processes = CreateRepositoryProcess();
        processes.Setup("status --porcelain=v2", exit: 0, stdout: status);
        processes.Setup("config --get user.name", exit: 0, stdout: "Junexis User");
        processes.Setup("config --get user.email", exit: 0, stdout: "user@example.test");
        processes.Setup("diff --cached --stat", exit: 0, stdout: statistics);
        return processes;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var timeout = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (!condition() && DateTime.UtcNow < timeout)
            await Task.Delay(10);
        Assert.True(condition());
    }
}

using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;
using Junexis.Studio.Host;

namespace Junexis.Studio.Host.Tests;

public sealed class GitRepositoryP4Tests
{
    [Fact]
    public async Task HistoryAndDetails_PreserveMetadataAndRenamePaths()
    {
        var processes = new ScriptableProcessService();
        var record = "abcdef1234567890\u001fabcdef1\u001ffeat: history"
            + "\u001fAlice\u001falice@example.test\u001f2026-07-24T10:00:00+02:00"
            + "\u001fparent1 parent2\u001fHEAD -> main\u001e";
        processes.Setup("log --format=", exit: 0, stdout: record);
        processes.Setup(
            "show -s --format=%H%x1f",
            exit: 0,
            stdout: record.TrimEnd('\u001e'));
        processes.Setup(
            "show -s --format=%B",
            exit: 0,
            stdout: "feat: history\n\nDetailed body");
        processes.Setup(
            "show --format= --stat",
            exit: 0,
            stdout: "1 file changed, 2 insertions(+)");
        processes.Setup(
            "diff-tree",
            exit: 0,
            stdout: "R100\0old name.cs\0new name.cs\0");
        var provider = new GitCliProvider(processes, Path.GetTempPath());

        var history = await provider.GetHistoryAsync();
        var details = await provider.GetCommitDetailsAsync("abcdef1234567890");

        var commit = Assert.Single(history);
        Assert.Equal("Alice", commit.Author);
        Assert.Equal(2, commit.Parents.Count);
        Assert.NotNull(details);
        Assert.Contains("Detailed body", details!.Body);
        var path = Assert.Single(details.ChangedPaths);
        Assert.Equal("old name.cs", path.OriginalPath);
        Assert.Equal("new name.cs", path.Path);
    }

    [Fact]
    public async Task BranchesComparisonAndCommands_UseExplicitStructuralRefs()
    {
        var processes = new ScriptableProcessService();
        processes.Setup(
            "for-each-ref",
            exit: 0,
            stdout: "main\tabc\torigin/main\t*\trefs/heads/main\n"
                + "feature\tdef\t\t \trefs/heads/feature\n");
        processes.Setup("rev-list --left-right --count", exit: 0, stdout: "2\t3");
        processes.Setup("diff --stat main...feature", exit: 0, stdout: "2 files changed");
        processes.Setup("check-ref-format", exit: 0);
        processes.Setup("branch --", exit: 0);
        processes.Setup("switch --", exit: 0);
        var provider = new GitCliProvider(processes, Path.GetTempPath());

        var branches = await provider.GetBranchesAsync();
        var comparison = await provider.CompareBranchesAsync("main", "feature");
        var created = await provider.CreateBranchAsync("feature/new", "abc123");
        var switched = await provider.SwitchBranchAsync("feature/new");

        Assert.Equal(2, branches.Count);
        Assert.True(branches[0].IsCurrent);
        Assert.Equal(3, comparison!.Ahead);
        Assert.Equal(2, comparison.Behind);
        Assert.True(created.Success);
        Assert.True(switched.Success);
        Assert.Contains(processes.Requests, request =>
            request.ArgumentList?.SequenceEqual(
                ["branch", "--", "feature/new", "abc123"]) == true);
        Assert.Contains(processes.Requests, request =>
            request.ArgumentList?.SequenceEqual(
                ["switch", "--", "feature/new"]) == true);
    }

    [Fact]
    public async Task StashOperations_TargetOnlySelectedReference()
    {
        var processes = new ScriptableProcessService();
        processes.Setup(
            "stash list",
            exit: 0,
            stdout: "stash@{0}\u001fOn main: safe\u001f2026-07-24T10:00:00+02:00\u001e");
        processes.Setup("stash apply", exit: 0);
        processes.Setup("stash pop", exit: 0);
        var provider = new GitCliProvider(processes, Path.GetTempPath());

        var stashes = await provider.GetStashesAsync();
        var applied = await provider.ApplyStashAsync("stash@{0}");
        var popped = await provider.PopStashAsync("stash@{0}");

        Assert.Single(stashes);
        Assert.True(applied.Success);
        Assert.True(popped.Success);
        Assert.Contains(processes.Requests, request =>
            request.ArgumentList?.SequenceEqual(
                ["stash", "apply", "--index", "stash@{0}"]) == true);
    }

    [Fact]
    public async Task RemotesAreRedactedAndNetworkTargetsAreExplicit()
    {
        var processes = new ScriptableProcessService();
        processes.Setup("remote get-url --push origin", exit: 0, stdout: "https://token@example.com/repo.git");
        processes.Setup("remote get-url origin", exit: 0, stdout: "https://user:secret@example.com/repo.git");
        processes.Setup("remote", exit: 0, stdout: "origin");
        processes.Setup("fetch --prune", exit: 0);
        processes.Setup("pull --ff-only", exit: 0);
        processes.Setup("push --set-upstream", exit: 0);
        var provider = new GitCliProvider(processes, Path.GetTempPath());

        var remotes = await provider.GetRemotesAsync();
        await provider.FetchAsync("origin");
        await provider.PullAsync("origin", "main");
        await provider.PushAsync("origin", "main", setUpstream: true);

        var remote = Assert.Single(remotes);
        Assert.DoesNotContain("secret", remote.FetchUrl);
        Assert.DoesNotContain("token", remote.PushUrl);
        Assert.Contains(processes.Requests, request =>
            request.ArgumentList?.SequenceEqual(
                ["pull", "--ff-only", "--", "origin", "main"]) == true);
        Assert.Contains(processes.Requests, request =>
            request.ArgumentList?.SequenceEqual(
                ["push", "--set-upstream", "--", "origin", "main"]) == true);
    }

    [Fact]
    public async Task SwitchBranch_IsBlockedBeforeGitWhenWorktreeIsDirty()
    {
        var processes = CreateRepositoryProcess(
            "1 .M N... 100644 100644 100644 aaa bbb dirty.cs\0");
        processes.Setup("switch --", exit: 0);
        using var workspace = new WorkspaceService();
        workspace.OpenFolder(Path.GetTempPath());
        using var service = new GitWorkspaceVcsService(
            workspace,
            processes,
            new EventBus(),
            new OutputService(),
            repositoryContextDetector: _ => true);

        var result = await service.SwitchBranchAsync("feature");

        Assert.False(result.Success);
        Assert.Contains("clean", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(processes.Requests, request =>
            request.ArgumentList?.FirstOrDefault() == "switch");
    }

    [Fact]
    public async Task Pull_IsBlockedWhenExplicitTargetDoesNotMatchCurrentBranch()
    {
        var processes = CreateRepositoryProcess(
            "# branch.oid abc\0# branch.head feature\0");
        processes.Setup("pull --ff-only", exit: 0);
        using var workspace = new WorkspaceService();
        workspace.OpenFolder(Path.GetTempPath());
        using var service = new GitWorkspaceVcsService(
            workspace,
            processes,
            new EventBus(),
            new OutputService(),
            repositoryContextDetector: _ => true);

        var result = await service.PullAsync("origin", "main");

        Assert.False(result.Success);
        Assert.Contains("current branch", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(processes.Requests, request =>
            request.ArgumentList?.FirstOrDefault() == "pull");
    }

    [Fact]
    public async Task TemporaryRepository_HistoryBranchAndStashWorkEndToEnd()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"junexis-p4-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var processes = new ProcessService();
        try
        {
            try
            {
                var version = await processes.RunAsync(
                    new ProcessRequest("git", new[] { "--version" }, root),
                    null,
                    CancellationToken.None);
                if (version.ExitCode != 0)
                    return;
            }
            catch
            {
                return;
            }

            await RunGitAsync(processes, root, "init");
            await RunGitAsync(processes, root, "config", "user.name", "Junexis Test");
            await RunGitAsync(processes, root, "config", "user.email", "test@example.invalid");
            await File.WriteAllTextAsync(Path.Combine(root, "sample.txt"), "initial\n");
            await RunGitAsync(processes, root, "add", "--", "sample.txt");
            await RunGitAsync(processes, root, "commit", "-m", "test: initial");

            var provider = new GitCliProvider(processes, root);
            var history = await provider.GetHistoryAsync();
            var details = await provider.GetCommitDetailsAsync(history[0].Hash);
            var create = await provider.CreateBranchAsync("feature/p4");
            var change = await provider.SwitchBranchAsync("feature/p4");
            await File.WriteAllTextAsync(Path.Combine(root, "sample.txt"), "changed\n");
            var stash = await provider.CreateStashAsync("p4 smoke");
            var stashes = await provider.GetStashesAsync();

            Assert.NotEmpty(history);
            Assert.NotNull(details);
            Assert.Contains(details!.ChangedPaths, path => path.Path == "sample.txt");
            Assert.True(create.Success, create.Message);
            Assert.True(change.Success, change.Message);
            Assert.True(stash.Success, stash.Message);
            Assert.Contains(stashes, entry => entry.Message.Contains("p4 smoke"));
        }
        finally
        {
            DeleteTemporaryRepository(root);
        }
    }

    private static ScriptableProcessService CreateRepositoryProcess(string status)
    {
        var processes = new ScriptableProcessService();
        processes.Setup("--version", exit: 0);
        processes.Setup("rev-parse --is-inside-work-tree", exit: 0);
        processes.Setup("status --porcelain=v2", exit: 0, stdout: status);
        return processes;
    }

    private static async Task RunGitAsync(
        IProcessService processes,
        string root,
        params string[] arguments)
    {
        var result = await processes.RunAsync(
            new ProcessRequest("git", arguments, root),
            null,
            CancellationToken.None);
        Assert.Equal(0, result.ExitCode);
    }

    private static void DeleteTemporaryRepository(string root)
    {
        foreach (var file in Directory.EnumerateFiles(
            root,
            "*",
            SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(root, recursive: true);
    }
}

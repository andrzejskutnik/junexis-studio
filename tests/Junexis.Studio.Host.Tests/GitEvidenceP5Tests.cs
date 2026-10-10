using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;
using Junexis.Studio.Host;

namespace Junexis.Studio.Host.Tests;

public sealed class GitEvidenceP5Tests
{
    [Fact]
    public async Task WorkspaceMutation_RecordsEvidenceThroughSharedAuditPipeline()
    {
        var processes = new ScriptableProcessService();
        processes.Setup("--version", exit: 0);
        processes.Setup("rev-parse --is-inside-work-tree", exit: 0);
        processes.Setup(
            "status --porcelain=v2",
            exit: 0,
            stdout: "# branch.oid abcdef1234567890\0# branch.head main\0"
                + "1 .M N... 100644 100644 100644 aaa bbb src/main.jnx\0");
        processes.Setup("add --", exit: 0);
        using var workspace = new WorkspaceService();
        workspace.OpenFolder(Path.GetTempPath());
        var bus = new EventBus();
        var audit = new AuditService();
        using var evidence = new GitVcsEvidenceService(bus, audit);
        using var service = new GitWorkspaceVcsService(
            workspace,
            processes,
            bus,
            new OutputService(),
            evidence,
            repositoryContextDetector: _ => true);

        await service.StageAsync("src/main.jnx");

        var entry = Assert.Single(service.GetEvidence());
        Assert.Equal("stage", entry.Operation);
        Assert.Equal("main", entry.BranchBefore);
        Assert.Equal("src/main.jnx", Assert.Single(entry.Paths));
        Assert.True(entry.Success);
        Assert.Equal("vcs.stage", Assert.Single(audit.GetEntries()).Action);
    }

    [Fact]
    public void CommitEvidence_CorrelatesOnlyMatchingBuildMentorAndDiagnosticStory()
    {
        var bus = new EventBus();
        var audit = new AuditService();
        using var sut = new GitVcsEvidenceService(bus, audit);
        var published = new List<VcsOperationEvidence>();
        bus.Subscribe<VcsOperationEvidenceRecordedEvent>(message => published.Add(message.Evidence));
        bus.Publish(new AnalysisSnapshotUpdatedEvent("junexis", "build-42", "private/analysis.json"));
        bus.Publish(new MentorAnalysisCompletedEvent("older-build", 99));
        bus.Publish(new MentorAnalysisCompletedEvent("build-42", 3));
        bus.Publish(new DiagnosticStoryLoadedEvent("older-build", 100, "old", 8));
        bus.Publish(new DiagnosticStoryLoadedEvent("build-42", 101, "HomeNarrative", 2));
        var before = Snapshot("C:/work/demo", "main", "aaaaaaaaaaaaaaaa");
        var after = Snapshot("C:/work/demo", "main", "bbbbbbbbbbbbbbbb");

        sut.Record(
            "commit",
            DateTimeOffset.UtcNow.AddMilliseconds(-25),
            "C:/work/demo",
            before,
            after,
            ["src/main.jnx"],
            null,
            true,
            "succeeded");

        var evidence = Assert.Single(sut.GetEntries());
        Assert.Same(evidence, Assert.Single(published));
        Assert.Equal("build-42", evidence.BuildId);
        Assert.Equal(3, evidence.MentorFindingCount);
        Assert.Equal("HomeNarrative:101", evidence.DiagnosticStoryId);
        Assert.Equal("aaaaaaaaaaaa", evidence.HeadBefore);
        Assert.Equal("bbbbbbbbbbbb", evidence.HeadAfter);
        Assert.Single(audit.GetEntries());
    }

    [Fact]
    public void Evidence_RedactsSecretsAndDoesNotPersistRepositoryRootOrPayloads()
    {
        var bus = new EventBus();
        var audit = new AuditService();
        using var sut = new GitVcsEvidenceService(bus, audit);
        const string secret = "super-secret-value";
        var root = $"C:/users/private/password={secret}";
        var snapshot = Snapshot(root, "main", "aaaaaaaaaaaaaaaa");

        sut.Record(
            "push",
            DateTimeOffset.UtcNow,
            root,
            snapshot,
            snapshot,
            [$"token={secret}.txt"],
            $"https://user:{secret}@example.test/repo.git?token={secret}",
            false,
            "failed");

        var evidence = Assert.Single(sut.GetEntries());
        var detail = Assert.Single(audit.GetEntries()).Detail;
        Assert.Equal("(redacted)", evidence.RepositoryName);
        Assert.Equal("(redacted)", evidence.Target);
        Assert.Equal("(redacted)", Assert.Single(evidence.Paths));
        Assert.DoesNotContain(secret, detail, StringComparison.Ordinal);
        Assert.DoesNotContain("C:/users/private", detail, StringComparison.Ordinal);
        Assert.DoesNotContain("https://", detail, StringComparison.Ordinal);
    }

    [Fact]
    public void EvidenceHistory_IsNewestFirstAndAuditPreservesCompletionOrder()
    {
        var bus = new EventBus();
        var audit = new AuditService();
        using var sut = new GitVcsEvidenceService(bus, audit);
        var snapshot = Snapshot("C:/work/demo", "main", "aaaaaaaaaaaaaaaa");

        sut.Record("stage", DateTimeOffset.UtcNow, "C:/work/demo", snapshot, snapshot, ["a.jnx"], null, true, "succeeded");
        sut.Record("unstage", DateTimeOffset.UtcNow, "C:/work/demo", snapshot, snapshot, ["a.jnx"], null, true, "succeeded");

        Assert.Equal(["unstage", "stage"], sut.GetEntries().Select(entry => entry.Operation));
        Assert.Equal(["vcs.stage", "vcs.unstage"], audit.GetEntries().Select(entry => entry.Action));
    }

    private static VcsRepositorySnapshot Snapshot(
        string root,
        string branch,
        string head) =>
        new(root, true, branch, [])
        {
            HeadOid = head,
        };
}

using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;
using Xunit;

namespace Junexis.Studio.Core.Tests;

/// <summary>
/// Tests for <see cref="VcsService"/> — staging, committing, validation and events.
/// </summary>
[Trait("Issue", "77")]
public class VcsServiceTests
{
    // ── Fakes ───────────────────────────────────────────────────

    private sealed class RecordingEventBus : IEventBus
    {
        public List<object> Published { get; } = new();
        public void Publish(object evt) => Published.Add(evt);
        public void Subscribe<T>(Action<T> handler) { }
        public void Unsubscribe<T>(Action<T> handler) { }
    }

    private sealed class RecordingOutput : IOutput
    {
        public List<string> Lines { get; } = new();
        public string RegisterChannel(string ch) => ch;
        public IReadOnlyList<string> GetChannelsSnapshot() => Array.Empty<string>();
        public void Write(string ch, string msg) => Lines.Add($"[{ch}] {msg}");
        public void Write(string ch, string msg, OutputSeverity sev) => Lines.Add($"[{ch}][{sev}] {msg}");
        public void Write(string ch, string msg, OutputSeverity sev, string? src, string? ex = null) => Lines.Add($"[{ch}][{sev}] {msg}");
        public IReadOnlyList<OutputEntry> GetSnapshot() => Array.Empty<OutputEntry>();
        public event Action<OutputEntry>? EntryWritten;
    }

    // ── Helpers ─────────────────────────────────────────────────

    private static VcsService.CommitBackend SuccessBackend(string commitId = "abc123") =>
        (_, _) => new VcsCommitResult(true, commitId);

    private static VcsService.CommitBackend FailBackend(string error = "git error") =>
        (_, _) => new VcsCommitResult(false, Error: error);

    private static VcsService.CommitBackend ThrowBackend(string msg = "crash") =>
        (_, _) => throw new InvalidOperationException(msg);

    private (VcsService svc, RecordingEventBus bus, RecordingOutput output) Create(
        VcsService.CommitBackend? backend = null)
    {
        var bus = new RecordingEventBus();
        var output = new RecordingOutput();
        var svc = new VcsService(backend ?? SuccessBackend(), bus, output);
        return (svc, bus, output);
    }

    // ── Constructor ─────────────────────────────────────────────

    [Fact]
    public void Constructor_NullBackend_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new VcsService(null!, null, null));
    }

    // ── SetChanges / GetChanges ─────────────────────────────────

    [Fact]
    public void GetChanges_InitiallyEmpty()
    {
        var (svc, _, _) = Create();
        Assert.Empty(svc.GetChanges());
    }

    [Fact]
    public void SetChanges_PopulatesList()
    {
        var (svc, _, _) = Create();
        svc.SetChanges(new[]
        {
            new VcsFileChange("a.cs", VcsFileStatus.Modified),
            new VcsFileChange("b.cs", VcsFileStatus.Added)
        });

        var changes = svc.GetChanges();
        Assert.Equal(2, changes.Count);
    }

    [Fact]
    public void SetChanges_NullThrows()
    {
        var (svc, _, _) = Create();
        Assert.Throws<ArgumentNullException>(() => svc.SetChanges(null!));
    }

    [Fact]
    public void SetChanges_PublishesVcsStatusChangedEvent()
    {
        var (svc, bus, _) = Create();
        svc.SetChanges(new[] { new VcsFileChange("x.cs", VcsFileStatus.Untracked) });
        Assert.Contains(bus.Published, e => e is VcsStatusChangedEvent);
    }

    [Fact]
    public void SetChanges_ReplacesExisting()
    {
        var (svc, _, _) = Create();
        svc.SetChanges(new[] { new VcsFileChange("a.cs", VcsFileStatus.Modified) });
        svc.SetChanges(new[] { new VcsFileChange("b.cs", VcsFileStatus.Added), new VcsFileChange("c.cs", VcsFileStatus.Deleted) });
        Assert.Equal(2, svc.GetChanges().Count);
    }

    // ── Stage ───────────────────────────────────────────────────

    [Fact]
    public void Stage_MarksFileAsStaged()
    {
        var (svc, _, _) = Create();
        svc.SetChanges(new[] { new VcsFileChange("x.cs", VcsFileStatus.Modified) });
        svc.Stage("x.cs");

        var change = Assert.Single(svc.GetChanges());
        Assert.True(change.IsStaged);
    }

    [Fact]
    public void Stage_AlreadyStaged_NoOp()
    {
        var (svc, bus, _) = Create();
        svc.SetChanges(new[] { new VcsFileChange("x.cs", VcsFileStatus.Modified, IsStaged: true) });
        var countBefore = bus.Published.Count;
        svc.Stage("x.cs");
        // No additional event published (idempotent)
        Assert.Equal(countBefore, bus.Published.Count);
    }

    [Fact]
    public void Stage_UnknownFile_Throws()
    {
        var (svc, _, _) = Create();
        Assert.Throws<InvalidOperationException>(() => svc.Stage("nope.cs"));
    }

    [Fact]
    public void Stage_NullPath_Throws()
    {
        var (svc, _, _) = Create();
        Assert.Throws<ArgumentNullException>(() => svc.Stage(null!));
    }

    [Fact]
    public void Stage_PublishesVcsStatusChangedEvent()
    {
        var (svc, bus, _) = Create();
        svc.SetChanges(new[] { new VcsFileChange("x.cs", VcsFileStatus.Modified) });
        bus.Published.Clear();
        svc.Stage("x.cs");
        Assert.Contains(bus.Published, e => e is VcsStatusChangedEvent);
    }

    [Fact]
    public void Stage_CaseInsensitive()
    {
        var (svc, _, _) = Create();
        svc.SetChanges(new[] { new VcsFileChange("Dir/File.cs", VcsFileStatus.Modified) });
        svc.Stage("dir/file.cs");
        Assert.True(svc.GetChanges()[0].IsStaged);
    }

    // ── Unstage ─────────────────────────────────────────────────

    [Fact]
    public void Unstage_UnstageStagedFile()
    {
        var (svc, _, _) = Create();
        svc.SetChanges(new[] { new VcsFileChange("x.cs", VcsFileStatus.Modified, IsStaged: true) });
        svc.Unstage("x.cs");

        var change = Assert.Single(svc.GetChanges());
        Assert.False(change.IsStaged);
    }

    [Fact]
    public void Unstage_AlreadyUnstaged_NoOp()
    {
        var (svc, bus, _) = Create();
        svc.SetChanges(new[] { new VcsFileChange("x.cs", VcsFileStatus.Modified, IsStaged: false) });
        var countBefore = bus.Published.Count;
        svc.Unstage("x.cs");
        Assert.Equal(countBefore, bus.Published.Count);
    }

    [Fact]
    public void Unstage_UnknownFile_Throws()
    {
        var (svc, _, _) = Create();
        Assert.Throws<InvalidOperationException>(() => svc.Unstage("nope.cs"));
    }

    [Fact]
    public void Unstage_NullPath_Throws()
    {
        var (svc, _, _) = Create();
        Assert.Throws<ArgumentNullException>(() => svc.Unstage(null!));
    }

    // ── Commit: validation ──────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Commit_EmptyMessage_ReturnsFailure(string? message)
    {
        var (svc, _, output) = Create();
        svc.SetChanges(new[] { new VcsFileChange("x.cs", VcsFileStatus.Modified, IsStaged: true) });
        var result = svc.Commit(message!);
        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Contains("empty", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Commit_NothingStaged_ReturnsFailure()
    {
        var (svc, _, _) = Create();
        svc.SetChanges(new[] { new VcsFileChange("x.cs", VcsFileStatus.Modified, IsStaged: false) });
        var result = svc.Commit("some message");
        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Contains("staged", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    // ── Commit: success path ────────────────────────────────────

    [Fact]
    public void Commit_Success_ReturnsCommitId()
    {
        var (svc, _, _) = Create(SuccessBackend("deadbeef"));
        svc.SetChanges(new[] { new VcsFileChange("x.cs", VcsFileStatus.Modified, IsStaged: true) });
        var result = svc.Commit("init");
        Assert.True(result.Success);
        Assert.Equal("deadbeef", result.CommitId);
    }

    [Fact]
    public void Commit_Success_PublishesVcsCommitCreatedEvent()
    {
        var (svc, bus, _) = Create();
        svc.SetChanges(new[] { new VcsFileChange("x.cs", VcsFileStatus.Modified, IsStaged: true) });
        svc.Commit("message");
        var evt = Assert.Single(bus.Published.OfType<VcsCommitCreatedEvent>());
        Assert.Equal("message", evt.Message);
    }

    [Fact]
    public void Commit_Success_PublishesVcsStatusChangedEvent()
    {
        var (svc, bus, _) = Create();
        svc.SetChanges(new[] { new VcsFileChange("x.cs", VcsFileStatus.Modified, IsStaged: true) });
        bus.Published.Clear();
        svc.Commit("msg");
        Assert.Contains(bus.Published, e => e is VcsStatusChangedEvent);
    }

    [Fact]
    public void Commit_Success_RemovesStagedFilesFromChanges()
    {
        var (svc, _, _) = Create();
        svc.SetChanges(new[]
        {
            new VcsFileChange("a.cs", VcsFileStatus.Modified, IsStaged: true),
            new VcsFileChange("b.cs", VcsFileStatus.Modified, IsStaged: false)
        });
        svc.Commit("msg");
        var remaining = svc.GetChanges();
        Assert.Single(remaining);
        Assert.Equal("b.cs", remaining[0].Path);
    }

    [Fact]
    public void Commit_Success_LogsToOutput()
    {
        var (svc, _, output) = Create();
        svc.SetChanges(new[] { new VcsFileChange("x.cs", VcsFileStatus.Modified, IsStaged: true) });
        svc.Commit("init commit");
        Assert.Contains(output.Lines, l => l.Contains("Committing"));
        Assert.Contains(output.Lines, l => l.Contains("Commit created"));
    }

    // ── Commit: failure path ────────────────────────────────────

    [Fact]
    public void Commit_BackendFails_ReturnsError()
    {
        var (svc, _, _) = Create(FailBackend("conflict"));
        svc.SetChanges(new[] { new VcsFileChange("x.cs", VcsFileStatus.Modified, IsStaged: true) });
        var result = svc.Commit("msg");
        Assert.False(result.Success);
        Assert.Contains("conflict", result.Error!);
    }

    [Fact]
    public void Commit_BackendFails_DoesNotPublishCommitEvent()
    {
        var (svc, bus, _) = Create(FailBackend());
        svc.SetChanges(new[] { new VcsFileChange("x.cs", VcsFileStatus.Modified, IsStaged: true) });
        svc.Commit("msg");
        Assert.DoesNotContain(bus.Published, e => e is VcsCommitCreatedEvent);
    }

    [Fact]
    public void Commit_BackendFails_DoesNotRemoveFiles()
    {
        var (svc, _, _) = Create(FailBackend());
        svc.SetChanges(new[] { new VcsFileChange("x.cs", VcsFileStatus.Modified, IsStaged: true) });
        svc.Commit("msg");
        Assert.Single(svc.GetChanges());
    }

    // ── Commit: exception path ──────────────────────────────────

    [Fact]
    public void Commit_BackendThrows_ReturnsErrorWithMessage()
    {
        var (svc, _, _) = Create(ThrowBackend("boom"));
        svc.SetChanges(new[] { new VcsFileChange("x.cs", VcsFileStatus.Modified, IsStaged: true) });
        var result = svc.Commit("msg");
        Assert.False(result.Success);
        Assert.Contains("boom", result.Error!);
    }

    [Fact]
    public void Commit_BackendThrows_LogsError()
    {
        var (svc, _, output) = Create(ThrowBackend("boom"));
        svc.SetChanges(new[] { new VcsFileChange("x.cs", VcsFileStatus.Modified, IsStaged: true) });
        svc.Commit("msg");
        Assert.Contains(output.Lines, l => l.Contains("boom") && l.Contains("Error"));
    }

    // ── Commit: backend receives staged paths correctly ─────────

    [Fact]
    public void Commit_BackendReceivesOnlyStagedPaths()
    {
        IReadOnlyList<string>? receivedPaths = null;
        VcsService.CommitBackend capturingBackend = (files, _) =>
        {
            receivedPaths = files;
            return new VcsCommitResult(true, "ok");
        };

        var svc = new VcsService(capturingBackend);
        svc.SetChanges(new[]
        {
            new VcsFileChange("staged.cs", VcsFileStatus.Added, IsStaged: true),
            new VcsFileChange("unstaged.cs", VcsFileStatus.Modified, IsStaged: false),
            new VcsFileChange("also-staged.cs", VcsFileStatus.Modified, IsStaged: true)
        });
        svc.Commit("msg");

        Assert.NotNull(receivedPaths);
        Assert.Equal(2, receivedPaths!.Count);
        Assert.Contains("staged.cs", receivedPaths);
        Assert.Contains("also-staged.cs", receivedPaths);
    }

    // ── OutputChannel constant ──────────────────────────────────

    [Fact]
    public void OutputChannel_IsVCS()
    {
        Assert.Equal("VCS", VcsService.OutputChannel);
    }
}

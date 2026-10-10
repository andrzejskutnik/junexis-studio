using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;
using Junexis.Studio.Host;
using System.ComponentModel;
using Xunit;

namespace Junexis.Studio.Host.Tests;

/// <summary>
/// Tests for <see cref="VcsExtension"/> and <see cref="VcsChangesViewModel"/>.
/// </summary>
[Trait("Issue", "71")]
public class VcsExtensionTests
{
    // ── Fakes ───────────────────────────────────────────────────

    private sealed class FakeVcsService : IVcsService
    {
        private readonly List<VcsFileChange> _changes = new();

        public IReadOnlyList<VcsFileChange> Changes => _changes;

        public DiffResult? NextDiff { get; set; }

        public VcsCommitPreflight? NextPreflight { get; set; }

        public VcsCommitRequest? LastCommitRequest { get; private set; }

        public void SetChanges(params VcsFileChange[] changes)
        {
            _changes.Clear();
            _changes.AddRange(changes);
        }

        public IReadOnlyList<VcsFileChange> GetChanges() => _changes.ToList();

        public void Stage(string path)
        {
            var idx = _changes.FindIndex(c => c.Path == path && !c.IsStaged);
            if (idx >= 0)
                _changes[idx] = _changes[idx] with { IsStaged = true };
        }

        public Task StageAsync(string path, CancellationToken cancellationToken = default)
        {
            Stage(path);
            return Task.CompletedTask;
        }

        public void Unstage(string path)
        {
            var idx = _changes.FindIndex(c => c.Path == path && c.IsStaged);
            if (idx >= 0)
                _changes[idx] = _changes[idx] with { IsStaged = false };
        }

        public Task UnstageAsync(string path, CancellationToken cancellationToken = default)
        {
            Unstage(path);
            return Task.CompletedTask;
        }

        public VcsCommitResult Commit(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return new VcsCommitResult(false, null, "empty message");
            return new VcsCommitResult(true, "abc123");
        }

        public Task<DiffResult> GetDiffAsync(
            string path,
            DiffTarget target = DiffTarget.WorkingTreeVsHead,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(NextDiff
                ?? new DiffResult(path, target, [], Error: "No test diff configured."));

        public Task<VcsCommitPreflight> PrepareCommitAsync(
            string message,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(NextPreflight
                ?? new VcsCommitPreflight(
                    true,
                    null,
                    _changes.Count(change => change.IsStaged),
                    "1 file changed",
                    "Test User",
                    "test@example.test",
                    false));

        public Task<VcsCommitResult> CommitAsync(
            VcsCommitRequest request,
            CancellationToken cancellationToken = default)
        {
            LastCommitRequest = request;
            return Task.FromResult(Commit(request.Message));
        }
    }

    private sealed class BlockingCommitVcsService : IVcsService
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyList<VcsFileChange> GetChanges() =>
            [new VcsFileChange("a.cs", VcsFileStatus.Modified, IsStaged: true)];

        public void Stage(string path) { }

        public void Unstage(string path) { }

        public VcsCommitResult Commit(string message)
        {
            Started.TrySetResult();
            Release.Task.GetAwaiter().GetResult();
            return new VcsCommitResult(true, "abc123");
        }

    }

    private sealed class CancellableCommitVcsService : IVcsService
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyList<VcsFileChange> GetChanges() =>
            [new VcsFileChange("a.cs", VcsFileStatus.Modified, IsStaged: true)];

        public void Stage(string path) { }

        public void Unstage(string path) { }

        public VcsCommitResult Commit(string message) =>
            new(false, Error: "Synchronous commit is not used.");

        public Task<VcsCommitPreflight> PrepareCommitAsync(
            string message,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new VcsCommitPreflight(
                true,
                null,
                1,
                "1 file changed",
                "Test User",
                "test@example.test",
                false));

        public async Task<VcsCommitResult> CommitAsync(
            VcsCommitRequest request,
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new VcsCommitResult(true, "unreachable");
        }
    }

    private static HostContext CreateContext(
        OutputService? output = null,
        CapabilityRegistry? capabilities = null)
    {
        return new HostContext(
            eventBus: new EventBus(),
            commands: new CommandRegistry(),
            panels: new PanelRegistry(),
            output: output ?? new OutputService(),
            processes: new TestProcessService(),
            workspace: new WorkspaceService(),
            diagnostics: new DiagnosticService(),
            activeDocument: new ActiveDocumentService(),
            openDocuments: new TestOpenDocumentRepository(),
            versionControl: null,
            views: new TestViewRegistry(),
            capabilities: capabilities);
    }

    // ── VcsChangesViewModel: constructor ────────────────────────

    [Fact]
    public void ViewModel_NullVcs_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new VcsChangesViewModel(null!, new EventBus()));
    }

    [Fact]
    public void ViewModel_NullEventBus_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new VcsChangesViewModel(new FakeVcsService(), null!));
    }

    // ── VcsChangesViewModel: refresh ────────────────────────────

    [Fact]
    public void ViewModel_Refresh_SeparatesStagedAndUnstaged()
    {
        var vcs = new FakeVcsService();
        vcs.SetChanges(
            new VcsFileChange("a.cs", VcsFileStatus.Modified, IsStaged: false),
            new VcsFileChange("b.cs", VcsFileStatus.Added, IsStaged: true));

        var vm = new VcsChangesViewModel(vcs, new EventBus());

        Assert.Single(vm.Unstaged);
        Assert.Equal("a.cs", vm.Unstaged[0].Path);
        Assert.Single(vm.Staged);
        Assert.Equal("b.cs", vm.Staged[0].Path);
    }

    [Fact]
    public void ViewModel_Refresh_EmptyChanges_BothListsEmpty()
    {
        var vm = new VcsChangesViewModel(new FakeVcsService(), new EventBus());
        Assert.Empty(vm.Unstaged);
        Assert.Empty(vm.Staged);
    }

    // ── VcsChangesViewModel: stage ──────────────────────────────

    [Fact]
    public void ViewModel_Stage_MovesFileToStaged()
    {
        var vcs = new FakeVcsService();
        vcs.SetChanges(new VcsFileChange("a.cs", VcsFileStatus.Modified));
        var vm = new VcsChangesViewModel(vcs, new EventBus());

        vm.Stage("a.cs");

        Assert.Empty(vm.Unstaged);
        Assert.Single(vm.Staged);
        Assert.Equal("a.cs", vm.Staged[0].Path);
    }

    [Fact]
    public void ViewModel_Stage_EmptyPath_Throws()
    {
        var vm = new VcsChangesViewModel(new FakeVcsService(), new EventBus());
        Assert.Throws<ArgumentException>(() => vm.Stage(""));
    }

    // ── VcsChangesViewModel: unstage ────────────────────────────

    [Fact]
    public void ViewModel_Unstage_MovesFileToUnstaged()
    {
        var vcs = new FakeVcsService();
        vcs.SetChanges(new VcsFileChange("b.cs", VcsFileStatus.Added, IsStaged: true));
        var vm = new VcsChangesViewModel(vcs, new EventBus());

        vm.Unstage("b.cs");

        Assert.Single(vm.Unstaged);
        Assert.Empty(vm.Staged);
    }

    [Fact]
    public void ViewModel_Unstage_EmptyPath_Throws()
    {
        var vm = new VcsChangesViewModel(new FakeVcsService(), new EventBus());
        Assert.Throws<ArgumentException>(() => vm.Unstage(""));
    }

    // ── VcsChangesViewModel: stage all / unstage all ────────────

    [Fact]
    public void ViewModel_StageAll_StagesAllUnstagedFiles()
    {
        var vcs = new FakeVcsService();
        vcs.SetChanges(
            new VcsFileChange("a.cs", VcsFileStatus.Modified),
            new VcsFileChange("b.cs", VcsFileStatus.Added));
        var vm = new VcsChangesViewModel(vcs, new EventBus());

        vm.StageAll();

        Assert.Empty(vm.Unstaged);
        Assert.Equal(2, vm.Staged.Count);
    }

    [Fact]
    public void ViewModel_UnstageAll_UnstagesAllStagedFiles()
    {
        var vcs = new FakeVcsService();
        vcs.SetChanges(
            new VcsFileChange("a.cs", VcsFileStatus.Modified, IsStaged: true),
            new VcsFileChange("b.cs", VcsFileStatus.Added, IsStaged: true));
        var vm = new VcsChangesViewModel(vcs, new EventBus());

        vm.UnstageAll();

        Assert.Equal(2, vm.Unstaged.Count);
        Assert.Empty(vm.Staged);
    }

    // ── VcsChangesViewModel: event-driven refresh ───────────────

    [Fact]
    public void ViewModel_VcsStatusChangedEvent_TriggersRefresh()
    {
        var vcs = new FakeVcsService();
        var bus = new EventBus();
        var vm = new VcsChangesViewModel(vcs, bus);

        Assert.Empty(vm.Unstaged);

        // Add a change externally
        vcs.SetChanges(new VcsFileChange("new.cs", VcsFileStatus.Untracked));

        // Publish status changed event — should auto-refresh
        bus.Publish(new VcsStatusChangedEvent());

        Assert.Single(vm.Unstaged);
        Assert.Equal("new.cs", vm.Unstaged[0].Path);
    }

    [Fact]
    public void ViewModel_Dispose_UnsubscribesFromEvents()
    {
        var vcs = new FakeVcsService();
        var bus = new EventBus();
        var vm = new VcsChangesViewModel(vcs, bus);

        vm.Dispose();

        // After dispose, adding changes + publishing should NOT refresh
        vcs.SetChanges(new VcsFileChange("late.cs", VcsFileStatus.Modified));
        bus.Publish(new VcsStatusChangedEvent());

        // ViewModel still holds old (empty) lists
        Assert.Empty(vm.Unstaged);
    }

    // ── VcsExtension: initialize without IVcsService ────────────

    [Fact]
    public void Extension_Initialize_NoVcsService_WritesWarningToOutput()
    {
        var output = new OutputService();
        var ctx = CreateContext(output: output);
        var ext = new VcsExtension();

        ext.Initialize(ctx);

        var snapshot = output.GetSnapshot();
        Assert.Contains(snapshot, e =>
            e.Channel == "VCS" && e.Message.Contains("not available"));
    }

    [Fact]
    public void Extension_Initialize_NoVcsService_DoesNotRegisterPanel()
    {
        var ctx = CreateContext();
        var ext = new VcsExtension();

        ext.Initialize(ctx);

        Assert.Empty(ctx.Panels.GetRegisteredPanels());
    }

    // ── VcsExtension: initialize with IVcsService ───────────────

    [Fact]
    public void Extension_Initialize_WithVcsService_RegistersChangesPanel()
    {
        var caps = new CapabilityRegistry();
        caps.Register<IVcsService>(new FakeVcsService());
        var ctx = CreateContext(capabilities: caps);
        var ext = new VcsExtension();

        ext.Initialize(ctx);

        var panels = ctx.Panels.GetRegisteredPanels();
        var changes = Assert.Single(panels, panel => panel.Id == "junexis.vcs.changes");
        Assert.Equal(DockGroup.Right, changes.DockGroup);
    }

    [Fact]
    public void Extension_Initialize_RegistersCommands()
    {
        var caps = new CapabilityRegistry();
        caps.Register<IVcsService>(new FakeVcsService());
        var ctx = CreateContext(capabilities: caps);
        var ext = new VcsExtension();

        ext.Initialize(ctx);

        Assert.True(ctx.Commands.CanExecute("vcs.stageAll"));
        Assert.True(ctx.Commands.CanExecute("vcs.unstageAll"));
        Assert.True(ctx.Commands.CanExecute("vcs.refresh"));
    }

    [Fact]
    public void Extension_Initialize_OutputsLoadedMessage()
    {
        var output = new OutputService();
        var caps = new CapabilityRegistry();
        caps.Register<IVcsService>(new FakeVcsService());
        var ctx = CreateContext(output: output, capabilities: caps);
        var ext = new VcsExtension();

        ext.Initialize(ctx);

        var snapshot = output.GetSnapshot();
        Assert.Contains(snapshot, e =>
            e.Channel == "VCS" && e.Message.Contains("loaded"));
    }

    // ── VcsExtension: commands work ─────────────────────────────

    [Fact]
    public void Extension_StageAllCommand_StagesFiles()
    {
        var vcs = new FakeVcsService();
        vcs.SetChanges(new VcsFileChange("a.cs", VcsFileStatus.Modified));
        var caps = new CapabilityRegistry();
        caps.Register<IVcsService>(vcs);
        var ctx = CreateContext(capabilities: caps);
        var ext = new VcsExtension();

        ext.Initialize(ctx);
        ctx.Commands.Execute("vcs.stageAll");

        // Get the panel factory result to check the view model state
        var panel = ctx.Panels.GetRegisteredPanels().First(p => p.Id == "junexis.vcs.changes");
        var vm = (VcsChangesViewModel)panel.Factory();
        Assert.Single(vm.Staged);
    }

    [Fact]
    public void Extension_UnstageAllCommand_UnstagesFiles()
    {
        var vcs = new FakeVcsService();
        vcs.SetChanges(new VcsFileChange("a.cs", VcsFileStatus.Modified, IsStaged: true));
        var caps = new CapabilityRegistry();
        caps.Register<IVcsService>(vcs);
        var ctx = CreateContext(capabilities: caps);
        var ext = new VcsExtension();

        ext.Initialize(ctx);
        ctx.Commands.Execute("vcs.unstageAll");

        var panel = ctx.Panels.GetRegisteredPanels().First(p => p.Id == "junexis.vcs.changes");
        var vm = (VcsChangesViewModel)panel.Factory();
        Assert.Single(vm.Unstaged);
    }

    // ── VcsExtension: shutdown ──────────────────────────────────

    [Fact]
    public void Extension_Shutdown_IsSafe()
    {
        var ext = new VcsExtension();
        ext.Shutdown(); // Should not throw
    }

    [Fact]
    public void Extension_Shutdown_AfterInit_DisposesViewModel()
    {
        var vcs = new FakeVcsService();
        var caps = new CapabilityRegistry();
        caps.Register<IVcsService>(vcs);
        var ctx = CreateContext(capabilities: caps);
        var ext = new VcsExtension();

        ext.Initialize(ctx);
        ext.Shutdown();

        // After shutdown, VcsStatusChangedEvent should not refresh (unsubscribed)
        vcs.SetChanges(new VcsFileChange("late.cs", VcsFileStatus.Modified));
        ctx.EventBus.Publish(new VcsStatusChangedEvent());

        // ViewModel was disposed, but we can't directly check its state
        // The key guarantee is no exceptions thrown
    }

    [Fact]
    public void Extension_Id_IsJunexisVcs()
    {
        var ext = new VcsExtension();
        Assert.Equal("junexis.vcs", ext.Id);
    }

    // ── Context menu targets (Issue #101) ───────────────────────

    [Fact]
    [Trait("Issue", "101")]
    public void Extension_Commands_HaveContextTarget_JunexisVcsChanges()
    {
        var caps = new CapabilityRegistry();
        caps.Register<IVcsService>(new FakeVcsService());
        var ctx = CreateContext(capabilities: caps);
        var ext = new VcsExtension();

        ext.Initialize(ctx);

        var cmds = ctx.Commands.GetRegisteredCommands();
        foreach (var id in new[] { "vcs.stageAll", "vcs.unstageAll", "vcs.refresh" })
        {
            var cmd = Assert.Single(cmds, c => c.Id == id);
            Assert.Equal("junexis.vcs.changes", cmd.ContextTarget);
        }
    }

    // ── VcsChangesViewModel: INPC + CommitMessage + Commit (#119) ──

    [Fact]
    [Trait("Issue", "119")]
    public void ViewModel_ImplementsINotifyPropertyChanged()
    {
        var vm = new VcsChangesViewModel(new FakeVcsService(), new EventBus());
        Assert.IsAssignableFrom<INotifyPropertyChanged>(vm);
    }

    [Fact]
    [Trait("Issue", "119")]
    public void CommitMessage_DefaultIsEmpty()
    {
        var vm = new VcsChangesViewModel(new FakeVcsService(), new EventBus());
        Assert.Equal("", vm.CommitMessage);
    }

    [Fact]
    [Trait("Issue", "119")]
    public void CommitMessage_SetFiresPropertyChanged()
    {
        var vm = new VcsChangesViewModel(new FakeVcsService(), new EventBus());
        var fired = new List<string>();
        vm.PropertyChanged += (_, e) => fired.Add(e.PropertyName ?? "");

        vm.CommitMessage = "feat: hello";

        Assert.Contains("CommitMessage", fired);
        Assert.Contains("CanCommit", fired);
    }

    [Fact]
    [Trait("Issue", "119")]
    public void CanCommit_FalseWhenNoMessage()
    {
        var vcs = new FakeVcsService();
        vcs.SetChanges(new VcsFileChange("a.cs", VcsFileStatus.Modified, IsStaged: true));
        var vm = new VcsChangesViewModel(vcs, new EventBus());
        vm.CommitMessage = "";

        Assert.False(vm.CanCommit);
    }

    [Fact]
    [Trait("Issue", "119")]
    public void CanCommit_FalseWhenNoStagedFiles()
    {
        var vm = new VcsChangesViewModel(new FakeVcsService(), new EventBus());
        vm.CommitMessage = "some message";

        Assert.False(vm.CanCommit);
    }

    [Fact]
    [Trait("Issue", "119")]
    public void CanCommit_TrueWhenMessageAndStagedFiles()
    {
        var vcs = new FakeVcsService();
        vcs.SetChanges(new VcsFileChange("a.cs", VcsFileStatus.Modified, IsStaged: true));
        var vm = new VcsChangesViewModel(vcs, new EventBus());
        vm.CommitMessage = "feat: add something";

        Assert.True(vm.CanCommit);
    }

    [Fact]
    [Trait("Issue", "119")]
    public void Commit_Success_ClearsCommitMessage()
    {
        var vcs = new FakeVcsService();
        vcs.SetChanges(new VcsFileChange("a.cs", VcsFileStatus.Modified, IsStaged: true));
        var vm = new VcsChangesViewModel(vcs, new EventBus());
        vm.CommitMessage = "feat: hello";

        var result = vm.Commit();

        Assert.True(result.Success);
        Assert.Equal("", vm.CommitMessage);
    }

    [Fact]
    [Trait("Issue", "119")]
    public void Commit_EmptyMessage_ReturnsFailure_PreservesMessage()
    {
        var vcs = new FakeVcsService();
        vcs.SetChanges(new VcsFileChange("a.cs", VcsFileStatus.Modified, IsStaged: true));
        var vm = new VcsChangesViewModel(vcs, new EventBus());
        vm.CommitMessage = " "; // whitespace only

        var result = vm.Commit();

        Assert.False(result.Success);
        Assert.Equal(" ", vm.CommitMessage); // preserved
    }

    [Fact]
    public async Task CommitAsync_DoesNotBlockCaller_AndRejectsConcurrentCommit()
    {
        var vcs = new BlockingCommitVcsService();
        var vm = new VcsChangesViewModel(vcs, new EventBus())
        {
            CommitMessage = "feat: safe commit",
        };

        var commit = vm.CommitAsync();
        await vcs.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(vm.IsBusy);
        Assert.False(vm.CanCommit);
        Assert.False(commit.IsCompleted);

        var concurrent = await vm.CommitAsync();
        Assert.False(concurrent.Success);
        Assert.Contains("already running", concurrent.Error, StringComparison.OrdinalIgnoreCase);

        vcs.Release.TrySetResult();
        var result = await commit.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(result.Success);
        Assert.False(vm.IsBusy);
        Assert.Equal("", vm.CommitMessage);
    }

    [Fact]
    [Trait("Issue", "119")]
    public void Staged_PropertyChanged_FiredOnRefresh()
    {
        var vcs = new FakeVcsService();
        var vm = new VcsChangesViewModel(vcs, new EventBus());
        var fired = new List<string>();
        vm.PropertyChanged += (_, e) => fired.Add(e.PropertyName ?? "");

        vcs.SetChanges(new VcsFileChange("x.cs", VcsFileStatus.Modified, IsStaged: true));
        vm.Refresh();

        Assert.Contains("Staged", fired);
        Assert.Contains("Unstaged", fired);
    }

    [Fact]
    public void Refresh_SeparatesConflictsChangesUntrackedAndStaged()
    {
        var vcs = new FakeVcsService();
        vcs.SetChanges(
            new VcsFileChange("conflict.cs", VcsFileStatus.Conflict),
            new VcsFileChange("tracked.cs", VcsFileStatus.Modified),
            new VcsFileChange("new.cs", VcsFileStatus.Untracked),
            new VcsFileChange("staged.cs", VcsFileStatus.Added, IsStaged: true));

        var vm = new VcsChangesViewModel(vcs, new EventBus());

        Assert.Single(vm.Conflicts);
        Assert.Single(vm.Changes);
        Assert.Single(vm.Untracked);
        Assert.Single(vm.Staged);
    }

    [Fact]
    public async Task ShowDiffAsync_FormatsSelectedComparisonForPreview()
    {
        var vcs = new FakeVcsService
        {
            NextDiff = new DiffResult(
                "file.cs",
                DiffTarget.IndexVsHead,
                [new DiffHunk(
                    1,
                    1,
                    1,
                    1,
                    [
                        new DiffLine(DiffLineKind.Removed, "old"),
                        new DiffLine(DiffLineKind.Added, "new"),
                    ])]),
        };
        var vm = new VcsChangesViewModel(vcs, new EventBus());

        await vm.ShowDiffAsync(
            new VcsFileChange("file.cs", VcsFileStatus.Modified, true),
            DiffTarget.IndexVsHead);

        Assert.Contains("IndexVsHead", vm.DiffTitle);
        Assert.Contains("-old", vm.DiffPreview);
        Assert.Contains("+new", vm.DiffPreview);
    }

    [Fact]
    public async Task CommitAsync_LargeScopeRequiresSecondExplicitAction()
    {
        var vcs = new FakeVcsService
        {
            NextPreflight = new VcsCommitPreflight(
                true,
                null,
                50,
                "50 files changed, 1200 insertions(+)",
                "Test User",
                "test@example.test",
                true),
        };
        vcs.SetChanges(new VcsFileChange(
            "staged.cs",
            VcsFileStatus.Modified,
            IsStaged: true));
        var vm = new VcsChangesViewModel(vcs, new EventBus())
        {
            CommitMessage = "feat: large scope",
        };

        var first = await vm.CommitAsync();

        Assert.True(first.RequiresConfirmation);
        Assert.Null(vcs.LastCommitRequest);
        Assert.Equal("Confirm large commit", vm.CommitButtonLabel);
        Assert.Equal("feat: large scope", vm.CommitMessage);

        var second = await vm.CommitAsync();

        Assert.True(second.Success);
        Assert.True(vcs.LastCommitRequest!.ConfirmLargeChange);
        Assert.Empty(vm.CommitMessage);
    }

    [Fact]
    public async Task CommitAsync_PreflightFailurePreservesMessageAndShowsReason()
    {
        var vcs = new FakeVcsService
        {
            NextPreflight = new VcsCommitPreflight(
                false,
                "Git author is not configured.",
                1,
                "",
                null,
                null,
                false),
        };
        vcs.SetChanges(new VcsFileChange(
            "staged.cs",
            VcsFileStatus.Modified,
            IsStaged: true));
        var vm = new VcsChangesViewModel(vcs, new EventBus())
        {
            CommitMessage = "feat: preserve me",
        };

        var result = await vm.CommitAsync();

        Assert.False(result.Success);
        Assert.Equal("feat: preserve me", vm.CommitMessage);
        Assert.Contains("author", vm.CommitSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CancelCommit_StopsActiveRequestAndPreservesMessage()
    {
        var vcs = new CancellableCommitVcsService();
        var vm = new VcsChangesViewModel(vcs, new EventBus())
        {
            CommitMessage = "feat: cancellable",
        };

        var commit = vm.CommitAsync();
        await vcs.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(vm.CanCancelCommit);

        vm.CancelCommit();
        var result = await commit.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(result.Success);
        Assert.True(result.IsUncertain);
        Assert.Equal("feat: cancellable", vm.CommitMessage);
        Assert.False(vm.CanCancelCommit);
    }
}

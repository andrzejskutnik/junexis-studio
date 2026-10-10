using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;
using Junexis.Studio.Host;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Junexis.Studio.Host.Tests;

// ============================================================
// GitStatusParser tests
// ============================================================

public sealed class GitStatusParserTests
{
    [Fact]
    [Trait("Issue", "72")]
    public void Parse_EmptyString_ReturnsEmpty()
    {
        Assert.Empty(GitStatusParser.Parse(""));
    }

    [Fact]
    [Trait("Issue", "72")]
    public void Parse_Null_ReturnsEmpty()
    {
        Assert.Empty(GitStatusParser.Parse(null!));
    }

    [Fact]
    [Trait("Issue", "72")]
    public void ParseLine_UntrackedFile()
    {
        var result = GitStatusParser.ParseLine("?? newfile.txt");
        Assert.Single(result);
        Assert.Equal("newfile.txt", result[0].Path);
        Assert.Equal(VcsFileStatus.Untracked, result[0].Status);
        Assert.False(result[0].IsStaged);
    }

    [Fact]
    [Trait("Issue", "72")]
    public void ParseLine_ModifiedUnstaged()
    {
        var result = GitStatusParser.ParseLine(" M src/file.cs");
        Assert.Single(result);
        Assert.Equal("src/file.cs", result[0].Path);
        Assert.Equal(VcsFileStatus.Modified, result[0].Status);
        Assert.False(result[0].IsStaged);
    }

    [Fact]
    [Trait("Issue", "72")]
    public void ParseLine_ModifiedStaged()
    {
        var result = GitStatusParser.ParseLine("M  staged.cs");
        Assert.Single(result);
        Assert.Equal("staged.cs", result[0].Path);
        Assert.Equal(VcsFileStatus.Modified, result[0].Status);
        Assert.True(result[0].IsStaged);
    }

    [Fact]
    [Trait("Issue", "72")]
    public void ParseLine_ModifiedBothStagedAndUnstaged()
    {
        var result = GitStatusParser.ParseLine("MM both.cs");
        Assert.Equal(2, result.Count);
        Assert.True(result[0].IsStaged);
        Assert.False(result[1].IsStaged);
    }

    [Fact]
    [Trait("Issue", "72")]
    public void ParseLine_AddedStaged()
    {
        var result = GitStatusParser.ParseLine("A  new.cs");
        Assert.Single(result);
        Assert.Equal(VcsFileStatus.Added, result[0].Status);
        Assert.True(result[0].IsStaged);
    }

    [Fact]
    [Trait("Issue", "72")]
    public void ParseLine_DeletedUnstaged()
    {
        var result = GitStatusParser.ParseLine(" D gone.cs");
        Assert.Single(result);
        Assert.Equal(VcsFileStatus.Deleted, result[0].Status);
        Assert.False(result[0].IsStaged);
    }

    [Fact]
    [Trait("Issue", "72")]
    public void ParseLine_RenamedStaged()
    {
        var result = GitStatusParser.ParseLine("R  old.cs -> new.cs");
        Assert.Single(result);
        Assert.Equal("new.cs", result[0].Path);
        Assert.Equal(VcsFileStatus.Renamed, result[0].Status);
        Assert.True(result[0].IsStaged);
    }

    [Fact]
    [Trait("Issue", "72")]
    public void ParseLine_Conflict_UU()
    {
        var result = GitStatusParser.ParseLine("UU conflict.cs");
        Assert.Single(result);
        Assert.Equal(VcsFileStatus.Conflict, result[0].Status);
    }

    [Fact]
    [Trait("Issue", "72")]
    public void ParseLine_Conflict_AA()
    {
        var result = GitStatusParser.ParseLine("AA both-added.cs");
        Assert.Single(result);
        Assert.Equal(VcsFileStatus.Conflict, result[0].Status);
    }

    [Fact]
    [Trait("Issue", "72")]
    public void ParseLine_Conflict_DD()
    {
        var result = GitStatusParser.ParseLine("DD both-deleted.cs");
        Assert.Single(result);
        Assert.Equal(VcsFileStatus.Conflict, result[0].Status);
    }

    [Fact]
    [Trait("Issue", "72")]
    public void ParseLine_ShortLine_ReturnsEmpty()
    {
        Assert.Empty(GitStatusParser.ParseLine("??"));
    }

    [Fact]
    [Trait("Issue", "72")]
    public void Parse_MultipleLines()
    {
        var output = "M  staged.cs\n?? untracked.txt\n D deleted.cs";
        var result = GitStatusParser.Parse(output);

        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void ParseV2_PreservesUnicodeSpacesAndNewlinesInNulDelimitedPath()
    {
        var path = "folder/zażółć file\nsecond.cs";
        var result = GitStatusParser.ParseV2($"? {path}\0");

        var file = Assert.Single(result.Files);
        Assert.Equal(path, file.Path);
        Assert.Equal(VcsFileStatus.Untracked, file.WorkTreeStatus);
    }

    [Fact]
    public void ParseV2_RenamePreservesCurrentAndOriginalPath()
    {
        var output =
            "2 R. N... 100644 100644 100644 aaa bbb R100 new name.cs\0"
            + "old name.cs\0";

        var file = Assert.Single(GitStatusParser.ParseV2(output).Files);

        Assert.Equal("new name.cs", file.Path);
        Assert.Equal("old name.cs", file.OriginalPath);
        Assert.Equal(VcsFileStatus.Renamed, file.IndexStatus);
    }

    [Fact]
    public void ParseV2_CopyAndConflictHaveExplicitStates()
    {
        var output =
            "2 C. N... 100644 100644 100644 aaa bbb C100 copy.cs\0source.cs\0"
            + "u UU N... 100644 100644 100644 100644 aaa bbb ccc conflict.cs\0";

        var result = GitStatusParser.ParseV2(output);

        Assert.Equal(VcsFileStatus.Copied, result.Files[0].IndexStatus);
        Assert.Equal("source.cs", result.Files[0].OriginalPath);
        Assert.Equal(VcsFileStatus.Conflict, result.Files[1].WorkTreeStatus);
    }

    [Fact]
    public void ParseV2_ParsesBranchDetachedAndTrackingMetadata()
    {
        var attached = GitStatusParser.ParseV2(
            "# branch.oid abcdef123\0# branch.head feature/p2\0"
            + "# branch.upstream origin/feature/p2\0# branch.ab +3 -2\0");
        var detached = GitStatusParser.ParseV2(
            "# branch.oid 123456789\0# branch.head (detached)\0");

        Assert.Equal("feature/p2", attached.Branch);
        Assert.Equal("origin/feature/p2", attached.Upstream);
        Assert.Equal(3, attached.Ahead);
        Assert.Equal(2, attached.Behind);
        Assert.False(attached.IsDetachedHead);
        Assert.Null(detached.Branch);
        Assert.True(detached.IsDetachedHead);
    }
}

// ============================================================
// GitCliProvider tests (with fake process service)
// ============================================================

public sealed class GitCliProviderTests
{
    private readonly ScriptableProcessService _processes = new();
    private readonly OutputService _output = new();

    private GitCliProvider CreateSut(string workDir = "/repo") =>
        new(_processes, workDir, _output);

    [Fact]
    [Trait("Issue", "72")]
    public void Constructor_NullProcesses_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new GitCliProvider(null!, "/repo"));
    }

    [Fact]
    [Trait("Issue", "72")]
    public void Constructor_NullWorkingDirectory_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new GitCliProvider(_processes, null!));
    }

    [Fact]
    [Trait("Issue", "72")]
    public async Task IsGitAvailableAsync_ReturnsTrue_WhenExitCode0()
    {
        _processes.Setup("--version", exit: 0);
        Assert.True(await CreateSut().IsGitAvailableAsync());
    }

    [Fact]
    [Trait("Issue", "72")]
    public async Task IsGitAvailableAsync_ReturnsFalse_WhenExitCodeNonZero()
    {
        _processes.Setup("--version", exit: 1);
        Assert.False(await CreateSut().IsGitAvailableAsync());
    }

    [Fact]
    [Trait("Issue", "72")]
    public async Task IsGitAvailableAsync_ReturnsFalse_WhenProcessThrows()
    {
        _processes.ThrowOnRun = true;
        Assert.False(await CreateSut().IsGitAvailableAsync());
    }

    [Fact]
    [Trait("Issue", "72")]
    public async Task IsRepositoryAsync_ReturnsTrue_WhenExitCode0()
    {
        _processes.Setup("rev-parse --is-inside-work-tree", exit: 0);
        Assert.True(await CreateSut().IsRepositoryAsync());
    }

    [Fact]
    [Trait("Issue", "72")]
    public async Task GetCurrentBranchAsync_ReturnsBranchName()
    {
        _processes.Setup("rev-parse --abbrev-ref HEAD", exit: 0, stdout: "main\n");
        Assert.Equal("main", await CreateSut().GetCurrentBranchAsync());
    }

    [Fact]
    [Trait("Issue", "72")]
    public async Task GetCurrentBranchAsync_ReturnsNull_OnError()
    {
        _processes.Setup("rev-parse --abbrev-ref HEAD", exit: 1);
        Assert.Null(await CreateSut().GetCurrentBranchAsync());
    }

    [Fact]
    [Trait("Issue", "72")]
    public async Task GetChangesAsync_ParsesStatus()
    {
        _processes.Setup(
            "status --porcelain=v2",
            exit: 0,
            stdout: "1 M. N... 100644 100644 100644 abc def file.cs\0? new.txt\0");
        var changes = await CreateSut().GetChangesAsync();

        Assert.Equal(2, changes.Count);
    }

    [Fact]
    [Trait("Issue", "72")]
    public async Task GetChangesAsync_ReturnsEmpty_OnError()
    {
        _processes.Setup("status --porcelain=v2", exit: 128);
        var changes = await CreateSut().GetChangesAsync();
        Assert.Empty(changes);
    }

    [Fact]
    public async Task StageAsync_PassesUnusualPathAsOneStructuralArgument()
    {
        const string path = "folder/zażółć \"quoted\" file.cs";
        _processes.Setup("add", exit: 0);

        await CreateSut().StageAsync(path);

        var request = Assert.Single(_processes.Requests);
        Assert.Equal(["add", "--", path], request.ArgumentList);
    }

    [Fact]
    public async Task CommitAsync_PassesMessageAsOneStructuralArgument()
    {
        const string message = "feat: preserve \"quotes\" and spaces";
        _processes.Setup("commit", exit: 0, stdout: "[main abc1234] commit");

        await CreateSut().CommitAsync(message);

        var request = Assert.Single(_processes.Requests);
        Assert.Equal(["commit", "-m", message], request.ArgumentList);
    }

    [Fact]
    public async Task GetRepositoryOperationAsync_DetectsRebaseMarker()
    {
        var gitDirectory = Path.Combine(
            Path.GetTempPath(),
            $"junexis-git-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(gitDirectory, "rebase-merge"));
        _processes.Setup(
            "rev-parse --absolute-git-dir",
            exit: 0,
            stdout: gitDirectory);

        try
        {
            var operation = await CreateSut().GetRepositoryOperationAsync();
            Assert.Equal(VcsRepositoryOperation.Rebase, operation);
        }
        finally
        {
            Directory.Delete(gitDirectory, recursive: true);
        }
    }

    [Fact]
    [Trait("Issue", "72")]
    public async Task CommitAsync_EmptyMessage_Fails()
    {
        var result = await CreateSut().CommitAsync("");
        Assert.False(result.Success);
        Assert.Contains("empty", result.Error!);
    }

    [Fact]
    [Trait("Issue", "72")]
    public async Task CommitAsync_Success_ReturnsHash()
    {
        _processes.Setup("commit", exit: 0, stdout: "[main abc1234] test commit\n");
        var result = await CreateSut().CommitAsync("test commit");
        Assert.True(result.Success);
        Assert.Equal("abc1234", result.CommitId);
    }

    [Fact]
    [Trait("Issue", "72")]
    public async Task CommitAsync_Failure_HasError()
    {
        _processes.Setup("commit", exit: 1);
        var result = await CreateSut().CommitAsync("msg");
        Assert.False(result.Success);
    }

    [Fact]
    public async Task CommitAsync_HookFailurePreservesStandardError()
    {
        _processes.Setup(
            "commit",
            exit: 1,
            stderr: "pre-commit hook rejected the staged scope");

        var result = await CreateSut().CommitAsync("feat: safe");

        Assert.False(result.Success);
        Assert.Contains("pre-commit hook rejected", result.Error);
    }

    [Fact]
    public async Task CommitAsync_DisablesInteractiveCredentialPrompts()
    {
        _processes.Setup("commit", exit: 1);

        await CreateSut().CommitAsync("feat: safe");

        var request = Assert.Single(_processes.Requests);
        Assert.Equal("0", request.EnvironmentVariables["GIT_TERMINAL_PROMPT"]);
        Assert.Equal("Never", request.EnvironmentVariables["GCM_INTERACTIVE"]);
    }

    // --- ExtractCommitHash ---

    [Fact]
    [Trait("Issue", "72")]
    public void ExtractCommitHash_ValidOutput_ReturnsHash()
    {
        Assert.Equal("abc1234", GitCliProvider.ExtractCommitHash("[main abc1234] message"));
    }

    [Fact]
    [Trait("Issue", "72")]
    public void ExtractCommitHash_NoBrackets_ReturnsNull()
    {
        Assert.Null(GitCliProvider.ExtractCommitHash("no brackets here"));
    }
}

// ============================================================
// GitProviderExtension tests
// ============================================================

public sealed class GitProviderExtensionTests
{
    [Fact]
    [Trait("Issue", "72")]
    public void Id_ReturnsExpected()
    {
        Assert.Equal("junexis.vcs.git", new GitProviderExtension().Id);
    }

    [Fact]
    [Trait("Issue", "72")]
    public void Initialize_NoWorkspaceRoot_DoesNotThrow()
    {
        var ctx = CreateContext(rootPath: null);
        new GitProviderExtension().Initialize(ctx);

        var snapshot = ((OutputService)ctx.Output).GetSnapshot();
        Assert.Contains(snapshot, e => e.Channel == "VCS" && e.Message.Contains("No workspace root"));
    }

    private static readonly string TestDir = Path.GetTempPath();

    [Fact]
    [Trait("Issue", "72")]
    public void Initialize_GitNotAvailable_StillRegistersWorkspaceService()
    {
        var processes = new ScriptableProcessService();
        processes.ThrowOnRun = true; // git not found
        var caps = new CapabilityRegistry();
        var ctx = CreateContext(rootPath: TestDir, processes: processes, capabilities: caps);

        new GitProviderExtension().Initialize(ctx);

        Assert.True(caps.TryGet<IVcsService>(out var service));
        Assert.NotNull(service);
        Assert.False(service.Snapshot.IsRepository);
    }

    [Fact]
    [Trait("Issue", "72")]
    public void Initialize_ValidRepo_RegistersVcsCapability()
    {
        var processes = new ScriptableProcessService();
        processes.Setup("--version", exit: 0);
        processes.Setup("rev-parse --is-inside-work-tree", exit: 0);
        processes.Setup("status --porcelain=v2", exit: 0, stdout: "");
        var caps = new CapabilityRegistry();
        var ctx = CreateContext(rootPath: TestDir, processes: processes, capabilities: caps);

        new GitProviderExtension().Initialize(ctx);

        Assert.True(caps.TryGet<IVcsService>(out _));
    }

    [Fact]
    [Trait("Issue", "72")]
    public void Initialize_ValidRepo_RegistersRefreshCommand()
    {
        var processes = new ScriptableProcessService();
        processes.Setup("--version", exit: 0);
        processes.Setup("rev-parse --is-inside-work-tree", exit: 0);
        processes.Setup("status --porcelain=v2", exit: 0, stdout: "");
        var caps = new CapabilityRegistry();
        var ctx = CreateContext(rootPath: TestDir, processes: processes, capabilities: caps);

        new GitProviderExtension().Initialize(ctx);

        Assert.True(ctx.Commands.CanExecute("vcs.git.refresh"));
    }

    [Fact]
    public void Initialize_RegistersRepositoryPanelAndActivationCommand()
    {
        var caps = new CapabilityRegistry();
        var ctx = CreateContext(rootPath: null, capabilities: caps);

        new GitProviderExtension().Initialize(ctx);

        Assert.True(ctx.Commands.CanExecute("vcs.git.repository"));
        Assert.Contains(
            ctx.Panels.GetRegisteredPanels(),
            panel => panel.Id == "junexis.vcs.repository");
    }

    [Fact]
    [Trait("Issue", "72")]
    public void Shutdown_DoesNotThrow()
    {
        new GitProviderExtension().Shutdown();
    }

    [Fact]
    [Trait("Issue", "120")]
    public async Task Initialize_ValidRepo_PublishesBranchChangedEvent()
    {
        var processes = new ScriptableProcessService();
        processes.Setup("--version", exit: 0);
        processes.Setup("rev-parse --is-inside-work-tree", exit: 0);
        processes.Setup(
            "status --porcelain=v2",
            exit: 0,
            stdout: "# branch.oid abc1234\0# branch.head main\0");

        var bus = new EventBus();
        VcsBranchChangedEvent? received = null;
        bus.Subscribe<VcsBranchChangedEvent>(e => received = e);

        var repositoryRoot = Directory.CreateTempSubdirectory("junexis_git_context_");
        try
        {
            Directory.CreateDirectory(Path.Combine(repositoryRoot.FullName, ".git"));
            var ctx = CreateContextWithBus(
                rootPath: repositoryRoot.FullName,
                processes: processes,
                bus: bus);
            new GitProviderExtension().Initialize(ctx);
            var timeout = DateTime.UtcNow + TimeSpan.FromSeconds(2);
            while (received is null && DateTime.UtcNow < timeout)
                await Task.Delay(10);

            Assert.NotNull(received);
            Assert.Equal("main", received!.BranchName);
        }
        finally
        {
            try { repositoryRoot.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    [Trait("Issue", "120")]
    public void Initialize_GitNotFound_DoesNotPublishBranchEvent()
    {
        var processes = new ScriptableProcessService();
        processes.Setup("--version", exit: 1);  // git not found

        var bus = new EventBus();
        VcsBranchChangedEvent? received = null;
        bus.Subscribe<VcsBranchChangedEvent>(e => received = e);

        var ctx = CreateContextWithBus(rootPath: TestDir, processes: processes, bus: bus);
        new GitProviderExtension().Initialize(ctx);

        Assert.Null(received);
    }

    private IHostContext CreateContextWithBus(
        string? rootPath = null,
        IProcessService? processes = null,
        EventBus? bus = null,
        ICapabilityRegistry? capabilities = null)
    {
        var ws = new WorkspaceService();
        if (rootPath is not null) ws.OpenFolder(rootPath);

        return new HostContext(
            eventBus: bus ?? new EventBus(),
            commands: new CommandRegistry(),
            panels: new PanelRegistry(),
            output: new OutputService(),
            processes: processes ?? new TestProcessService(),
            workspace: ws,
            diagnostics: new DiagnosticService(),
            activeDocument: new ActiveDocumentService(),
            openDocuments: new TestOpenDocumentRepository(),
            versionControl: null,
            views: new TestViewRegistry(),
            capabilities: capabilities ?? new CapabilityRegistry());
    }

    private IHostContext CreateContext(
        string? rootPath = null,
        IProcessService? processes = null,
        IDiagnosticService? diagnostics = null,
        ICapabilityRegistry? capabilities = null)
    {
        var ws = new WorkspaceService();
        // Use a real existing directory to satisfy WorkspaceService.OpenFolder validation
        if (rootPath is not null) ws.OpenFolder(rootPath);

        return new HostContext(
            eventBus: new EventBus(),
            commands: new CommandRegistry(),
            panels: new PanelRegistry(),
            output: new OutputService(),
            processes: processes ?? new TestProcessService(),
            workspace: ws,
            diagnostics: diagnostics ?? new DiagnosticService(),
            activeDocument: new ActiveDocumentService(),
            openDocuments: new TestOpenDocumentRepository(),
            versionControl: null,
            views: new TestViewRegistry(),
            capabilities: capabilities ?? new CapabilityRegistry());
    }
}

// ============================================================
// Test helper: scriptable process service
// ============================================================

internal sealed class ScriptableProcessService : IProcessService
{
    private readonly Dictionary<string, (int ExitCode, string StdOut, string StdErr)> _commands = new();
    private readonly Dictionary<string, Queue<(int ExitCode, string StdOut, string StdErr)>> _sequences = new();

    /// <summary>Gets all process requests observed by the fake.</summary>
    public List<ProcessRequest> Requests { get; } = new();

    /// <summary>
    /// When true, all RunAsync calls throw NotSupportedException (simulates git not found).
    /// </summary>
    public bool ThrowOnRun { get; set; }

    /// <summary>Optional argument fragment whose process blocks until cancellation.</summary>
    public string? BlockOnArgumentsContaining { get; set; }

    /// <summary>Signals that the configured blocking process started.</summary>
    public TaskCompletionSource BlockedProcessStarted { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Registers a canned response for a git command containing the given argument prefix.
    /// </summary>
    public void Setup(string argPrefix, int exit, string stdout = "", string stderr = "")
    {
        _commands[argPrefix] = (exit, stdout, stderr);
    }

    /// <summary>Registers ordered responses for repeated matching requests.</summary>
    public void SetupSequence(
        string argPrefix,
        params (int ExitCode, string StdOut, string StdErr)[] responses)
    {
        _sequences[argPrefix] = new Queue<(int, string, string)>(responses);
    }

    public async Task<ProcessResult> RunAsync(
        ProcessRequest request,
        Action<ProcessOutputLine>? listener,
        CancellationToken cancellationToken)
    {
        Requests.Add(request);
        if (ThrowOnRun) throw new NotSupportedException("Process not found.");

        var args = request.Arguments ?? "";
        if (BlockOnArgumentsContaining is { } blocked
            && args.Contains(blocked, StringComparison.OrdinalIgnoreCase))
        {
            BlockedProcessStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        // Find matching setup (longest-prefix match)
        (int ExitCode, string StdOut, string StdErr)? match = null;
        foreach (var sequence in _sequences.OrderByDescending(item => item.Key.Length))
        {
            if (!args.Contains(sequence.Key, StringComparison.OrdinalIgnoreCase)
                || sequence.Value.Count == 0)
                continue;
            match = sequence.Value.Count > 1
                ? sequence.Value.Dequeue()
                : sequence.Value.Peek();
            break;
        }
        foreach (var kvp in _commands.OrderByDescending(item => item.Key.Length))
        {
            if (match is not null)
                break;
            if (args.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
            {
                match = kvp.Value;
                break;
            }
        }

        match ??= (1, "", ""); // default: failure

        // Feed stdout lines to listener
        if (listener is not null && !string.IsNullOrEmpty(match.Value.StdOut))
        {
            foreach (var line in match.Value.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                listener(new ProcessOutputLine(ProcessOutputKind.StdOut, line));
            }
        }
        if (listener is not null && !string.IsNullOrEmpty(match.Value.StdErr))
        {
            foreach (var line in match.Value.StdErr.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                listener(new ProcessOutputLine(ProcessOutputKind.StdErr, line));
        }

        return new ProcessResult(match.Value.ExitCode, TimeSpan.Zero);
    }
}

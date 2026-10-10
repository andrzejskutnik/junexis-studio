using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;
using Junexis.Studio.Host;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Junexis.Studio.Host.Tests;

/// <summary>
/// Tests for <see cref="UnifiedDiffParser"/> (#125).
/// </summary>
[Trait("Issue", "125")]
public sealed class UnifiedDiffParserTests
{
    // ── Edge cases ───────────────────────────────────────────────

    [Fact]
    public void Parse_Null_ReturnsEmpty()
    {
        Assert.Empty(UnifiedDiffParser.Parse(null!));
    }

    [Fact]
    public void Parse_EmptyString_ReturnsEmpty()
    {
        Assert.Empty(UnifiedDiffParser.Parse(string.Empty));
    }

    [Fact]
    public void Parse_WhitespaceOnly_ReturnsEmpty()
    {
        Assert.Empty(UnifiedDiffParser.Parse("   \n  \n"));
    }

    [Fact]
    public void Parse_NoHunkHeaders_ReturnsEmpty()
    {
        var input = "diff --git a/foo.cs b/foo.cs\nindex abc..def 100644\n--- a/foo.cs\n+++ b/foo.cs";
        Assert.Empty(UnifiedDiffParser.Parse(input));
    }

    // ── Single hunk ──────────────────────────────────────────────

    [Fact]
    public void Parse_SingleHunk_ReturnsOneHunk()
    {
        var input = "@@ -1,3 +1,4 @@\n context\n-removed\n+added\n context2";
        var hunks = UnifiedDiffParser.Parse(input);

        Assert.Single(hunks);
        var hunk = hunks[0];
        Assert.Equal(1, hunk.OldStart);
        Assert.Equal(3, hunk.OldCount);
        Assert.Equal(1, hunk.NewStart);
        Assert.Equal(4, hunk.NewCount);
    }

    [Fact]
    public void Parse_SingleHunk_CorrectLineKinds()
    {
        var input = "@@ -1,3 +1,3 @@\n context\n-removed\n+added";
        var hunks = UnifiedDiffParser.Parse(input);

        Assert.Single(hunks);
        var lines = hunks[0].Lines;
        Assert.Equal(3, lines.Count);
        Assert.Equal(DiffLineKind.Context,  lines[0].Kind);
        Assert.Equal(DiffLineKind.Removed,  lines[1].Kind);
        Assert.Equal(DiffLineKind.Added,    lines[2].Kind);
    }

    [Fact]
    public void Parse_SingleHunk_LineContentsStripped()
    {
        var input = "@@ -1,1 +1,1 @@\n-old value\n+new value";
        var hunks = UnifiedDiffParser.Parse(input);

        var lines = hunks[0].Lines;
        Assert.Equal("old value", lines[0].Content);
        Assert.Equal("new value", lines[1].Content);
    }

    [Fact]
    public void Parse_HunkHeaderWithoutLineCounts_DefaultsToOne()
    {
        // @@ -1 +1 @@ — no ,N part
        var input = "@@ -1 +1 @@\n context\n+added";
        var hunks = UnifiedDiffParser.Parse(input);

        Assert.Single(hunks);
        Assert.Equal(1, hunks[0].OldCount);
        Assert.Equal(1, hunks[0].NewCount);
    }

    // ── Multiple hunks ───────────────────────────────────────────

    [Fact]
    public void Parse_MultipleHunks_ReturnsAll()
    {
        var input =
            "@@ -1,2 +1,2 @@\n context\n-old1\n+new1\n" +
            "@@ -10,2 +10,2 @@\n ctx2\n-old2\n+new2";

        var hunks = UnifiedDiffParser.Parse(input);

        Assert.Equal(2, hunks.Count);
        Assert.Equal(1, hunks[0].OldStart);
        Assert.Equal(10, hunks[1].OldStart);
    }

    [Fact]
    public void Parse_BackslashNoNewlineLines_Skipped()
    {
        var input = "@@ -1,1 +1,1 @@\n-old\n\\ No newline at end of file\n+new";
        var hunks = UnifiedDiffParser.Parse(input);

        Assert.Single(hunks);
        var lines = hunks[0].Lines;
        // The "\ No newline" line must not appear as a diff line
        Assert.DoesNotContain(lines, l => l.Content.Contains("No newline"));
    }
}

/// <summary>
/// Tests for <c>vcs.git.diff</c> command wired through <see cref="GitProviderExtension"/> (#125).
/// </summary>
[Trait("Issue", "125")]
public sealed class VcsGitDiffCommandTests
{
    private static HostContext CreateContext(
        string? rootPath = null,
        ScriptableProcessService? processes = null,
        CapabilityRegistry? capabilities = null,
        ActiveDocumentService? activeDocument = null)
    {
        var ws = new WorkspaceService();
        if (rootPath is not null) ws.OpenFolder(rootPath);

        return new HostContext(
            eventBus: new EventBus(),
            commands: new CommandRegistry(),
            panels: new PanelRegistry(),
            output: new OutputService(),
            processes: (IProcessService?)processes ?? new TestProcessService(),
            workspace: ws,
            diagnostics: new DiagnosticService(),
            activeDocument: activeDocument ?? new ActiveDocumentService(),
            openDocuments: new TestOpenDocumentRepository(),
            versionControl: null,
            views: new TestViewRegistry(),
            capabilities: capabilities ?? new CapabilityRegistry());
    }

    private static ScriptableProcessService MakeValidGitProcesses(string? diffStdout = null)
    {
        var p = new ScriptableProcessService();
        p.Setup("--version", exit: 0);
        p.Setup("rev-parse --is-inside-work-tree", exit: 0);
        p.Setup("status --porcelain", exit: 0, stdout: "");
        p.Setup("rev-parse --abbrev-ref HEAD", exit: 0, stdout: "main");
        p.Setup("diff", exit: 0, stdout: diffStdout ?? "");
        return p;
    }

    // ── Command registration ─────────────────────────────────────

    [Fact]
    public void Initialize_ValidRepo_RegistersVcsGitDiffCommand()
    {
        var processes = MakeValidGitProcesses();
        var caps = new CapabilityRegistry();
        var ctx = CreateContext(rootPath: Path.GetTempPath(), processes: processes, capabilities: caps);

        new GitProviderExtension().Initialize(ctx);

        Assert.True(ctx.Commands.CanExecute("vcs.git.diff"));
    }

    [Fact]
    public void Initialize_ValidRepo_RegistersIDiffServiceCapability()
    {
        var processes = MakeValidGitProcesses();
        var caps = new CapabilityRegistry();
        var ctx = CreateContext(rootPath: Path.GetTempPath(), processes: processes, capabilities: caps);

        new GitProviderExtension().Initialize(ctx);

        Assert.True(caps.TryGet<IDiffService>(out _));
    }

    [Fact]
    public void VcsGitDiff_NoActiveDocument_WritesWarning()
    {
        var processes = MakeValidGitProcesses();
        var output = new OutputService();
        var caps = new CapabilityRegistry();
        var ws = new WorkspaceService();
        ws.OpenFolder(Path.GetTempPath());

        var ctx = new HostContext(
            eventBus: new EventBus(),
            commands: new CommandRegistry(),
            panels: new PanelRegistry(),
            output: output,
            processes: processes,
            workspace: ws,
            diagnostics: new DiagnosticService(),
            activeDocument: new ActiveDocumentService(), // no active path
            openDocuments: new TestOpenDocumentRepository(),
            versionControl: null,
            views: new TestViewRegistry(),
            capabilities: caps);

        new GitProviderExtension().Initialize(ctx);
        ctx.Commands.Execute("vcs.git.diff");

        var snapshot = output.GetSnapshot();
        Assert.Contains(snapshot,
            e => e.Channel == "VCS" && e.Message.Contains("brak aktywnego pliku"));
    }

    [Fact]
    public void VcsGitDiff_NoDiff_WritesNoChanges()
    {
        var processes = MakeValidGitProcesses(diffStdout: ""); // empty diff = no changes
        var output = new OutputService();
        var caps = new CapabilityRegistry();
        var active = new ActiveDocumentService();

        var tmp = Directory.CreateTempSubdirectory("junexis_studio_125_");
        try
        {
            Directory.CreateDirectory(Path.Combine(tmp.FullName, ".git"));
            var filePath = Path.Combine(tmp.FullName, "test.cs");
            File.WriteAllText(filePath, "x");
            active.SetActivePath(filePath);

            var ws = new WorkspaceService();
            ws.OpenFolder(tmp.FullName);

            var ctx = new HostContext(
                eventBus: new EventBus(),
                commands: new CommandRegistry(),
                panels: new PanelRegistry(),
                output: output,
                processes: processes,
                workspace: ws,
                diagnostics: new DiagnosticService(),
                activeDocument: active,
                openDocuments: new TestOpenDocumentRepository(),
                versionControl: null,
                views: new TestViewRegistry(),
                capabilities: caps);

            new GitProviderExtension().Initialize(ctx);
            ctx.Commands.Execute("vcs.git.diff");

            var snapshot = output.GetSnapshot();
            Assert.Contains(snapshot,
                e => e.Channel == "VCS" && e.Message.Contains("Brak zmian"));
        }
        finally
        {
            try { tmp.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void VcsGitDiff_WithHunks_WritesHunkLines()
    {
        const string diffOutput =
            "@@ -1,2 +1,2 @@\n context\n-old line\n+new line\n";
        var processes = MakeValidGitProcesses(diffStdout: diffOutput);
        var output = new OutputService();
        var caps = new CapabilityRegistry();
        var active = new ActiveDocumentService();

        var tmp = Directory.CreateTempSubdirectory("junexis_studio_125_");
        try
        {
            Directory.CreateDirectory(Path.Combine(tmp.FullName, ".git"));
            var filePath = Path.Combine(tmp.FullName, "changed.cs");
            File.WriteAllText(filePath, "content");
            active.SetActivePath(filePath);

            var ws = new WorkspaceService();
            ws.OpenFolder(tmp.FullName);

            var ctx = new HostContext(
                eventBus: new EventBus(),
                commands: new CommandRegistry(),
                panels: new PanelRegistry(),
                output: output,
                processes: processes,
                workspace: ws,
                diagnostics: new DiagnosticService(),
                activeDocument: active,
                openDocuments: new TestOpenDocumentRepository(),
                versionControl: null,
                views: new TestViewRegistry(),
                capabilities: caps);

            new GitProviderExtension().Initialize(ctx);
            ctx.Commands.Execute("vcs.git.diff");

            var snapshot = output.GetSnapshot();
            Assert.Contains(snapshot,
                e => e.Channel == "VCS" && e.Message.StartsWith("@@"));
            Assert.Contains(snapshot,
                e => e.Channel == "VCS" && e.Message.StartsWith("-old"));
            Assert.Contains(snapshot,
                e => e.Channel == "VCS" && e.Message.StartsWith("+new"));
        }
        finally
        {
            try { tmp.Delete(recursive: true); } catch { }
        }
    }
}

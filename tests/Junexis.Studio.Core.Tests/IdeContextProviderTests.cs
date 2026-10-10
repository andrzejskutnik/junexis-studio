using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;
using Xunit;

namespace Junexis.Studio.Core.Tests;

/// <summary>
/// Tests for <see cref="IdeContextProvider"/> — snapshot building, policy enforcement.
/// </summary>
[Trait("Issue", "79")]
public class IdeContextProviderTests
{
    // ── Helpers ─────────────────────────────────────────────────

    private static IdeContextProvider CreateFull()
    {
        var p = new IdeContextProvider
        {
            WorkspaceRootProvider = () => "/ws/root",
            ActiveFilePathProvider = () => "/ws/root/main.nx",
            FileContentReader = path => $"content of {path}",
            SelectionProvider = () => "selected text",
            OpenEditorsProvider = () => new[] { "/ws/root/main.nx", "/ws/root/util.nx" }
        };
        return p;
    }

    // ── Default policy ──────────────────────────────────────────

    [Fact]
    public void DefaultPolicy_IncludesExpectedFields()
    {
        var policy = new ContextPolicy();
        Assert.True(policy.IncludeWorkspaceRoot);
        Assert.True(policy.IncludeActiveFilePath);
        Assert.False(policy.IncludeActiveFileContent); // off by default (privacy)
        Assert.True(policy.IncludeSelection);
        Assert.True(policy.IncludeOpenEditors);
        Assert.Equal(10_000, policy.MaxContentLength);
    }

    // ── Snapshot with all providers ─────────────────────────────

    [Fact]
    public void Snapshot_DefaultPolicy_NoContent()
    {
        var ctx = CreateFull();
        var snap = ctx.GetSnapshot();
        Assert.Equal("/ws/root", snap.WorkspaceRoot);
        Assert.Equal("/ws/root/main.nx", snap.ActiveFilePath);
        Assert.Null(snap.ActiveFileContent); // default: IncludeActiveFileContent = false
        Assert.Equal("selected text", snap.Selection);
        Assert.Equal(2, snap.OpenEditorPaths.Count);
    }

    [Fact]
    public void Snapshot_WithContentEnabled_IncludesContent()
    {
        var ctx = CreateFull();
        ctx.Policy = new ContextPolicy(IncludeActiveFileContent: true);
        var snap = ctx.GetSnapshot();
        Assert.NotNull(snap.ActiveFileContent);
        Assert.Contains("content of", snap.ActiveFileContent);
    }

    // ── Policy: disable fields ──────────────────────────────────

    [Fact]
    public void Policy_DisableWorkspaceRoot_ReturnsNull()
    {
        var ctx = CreateFull();
        ctx.Policy = new ContextPolicy(IncludeWorkspaceRoot: false);
        Assert.Null(ctx.GetSnapshot().WorkspaceRoot);
    }

    [Fact]
    public void Policy_DisableActiveFilePath_ReturnsNull()
    {
        var ctx = CreateFull();
        ctx.Policy = new ContextPolicy(IncludeActiveFilePath: false);
        Assert.Null(ctx.GetSnapshot().ActiveFilePath);
    }

    [Fact]
    public void Policy_DisableSelection_ReturnsNull()
    {
        var ctx = CreateFull();
        ctx.Policy = new ContextPolicy(IncludeSelection: false);
        Assert.Null(ctx.GetSnapshot().Selection);
    }

    [Fact]
    public void Policy_DisableOpenEditors_ReturnsEmpty()
    {
        var ctx = CreateFull();
        ctx.Policy = new ContextPolicy(IncludeOpenEditors: false);
        Assert.Empty(ctx.GetSnapshot().OpenEditorPaths);
    }

    // ── MaxContentLength truncation ─────────────────────────────

    [Fact]
    public void ContentTruncated_WhenExceedsMaxLength()
    {
        var ctx = new IdeContextProvider
        {
            ActiveFilePathProvider = () => "file.txt",
            FileContentReader = _ => new string('x', 500),
            Policy = new ContextPolicy(IncludeActiveFileContent: true, MaxContentLength: 50)
        };
        var snap = ctx.GetSnapshot();
        Assert.NotNull(snap.ActiveFileContent);
        Assert.Equal(50, snap.ActiveFileContent!.Length);
    }

    [Fact]
    public void ContentNotTruncated_WhenUnlimited()
    {
        var longContent = new string('y', 20_000);
        var ctx = new IdeContextProvider
        {
            ActiveFilePathProvider = () => "file.txt",
            FileContentReader = _ => longContent,
            Policy = new ContextPolicy(IncludeActiveFileContent: true, MaxContentLength: 0)
        };
        Assert.Equal(20_000, ctx.GetSnapshot().ActiveFileContent!.Length);
    }

    [Fact]
    public void ContentNotTruncated_WhenWithinLimit()
    {
        var ctx = new IdeContextProvider
        {
            ActiveFilePathProvider = () => "file.txt",
            FileContentReader = _ => "short",
            Policy = new ContextPolicy(IncludeActiveFileContent: true, MaxContentLength: 1000)
        };
        Assert.Equal("short", ctx.GetSnapshot().ActiveFileContent);
    }

    // ── No providers set ────────────────────────────────────────

    [Fact]
    public void Snapshot_NoProviders_AllNull()
    {
        var ctx = new IdeContextProvider();
        ctx.Policy = new ContextPolicy(IncludeActiveFileContent: true);
        var snap = ctx.GetSnapshot();
        Assert.Null(snap.WorkspaceRoot);
        Assert.Null(snap.ActiveFilePath);
        Assert.Null(snap.ActiveFileContent);
        Assert.Null(snap.Selection);
        Assert.Empty(snap.OpenEditorPaths);
    }

    // ── Content requires active file ────────────────────────────

    [Fact]
    public void Content_Null_WhenNoActiveFile()
    {
        var ctx = new IdeContextProvider
        {
            ActiveFilePathProvider = () => null,
            FileContentReader = _ => "should not be called",
            Policy = new ContextPolicy(IncludeActiveFileContent: true)
        };
        Assert.Null(ctx.GetSnapshot().ActiveFileContent);
    }

    [Fact]
    public void Content_Null_WhenNoFileReader()
    {
        var ctx = new IdeContextProvider
        {
            ActiveFilePathProvider = () => "file.txt",
            FileContentReader = null,
            Policy = new ContextPolicy(IncludeActiveFileContent: true)
        };
        Assert.Null(ctx.GetSnapshot().ActiveFileContent);
    }

    // ── IdeContextSnapshot record ───────────────────────────────

    [Fact]
    public void IdeContextSnapshot_PreservesAllFields()
    {
        var snap = new IdeContextSnapshot("/ws", "/ws/f.cs", "abc", "sel", new[] { "/ws/f.cs", "/ws/g.cs" });
        Assert.Equal("/ws", snap.WorkspaceRoot);
        Assert.Equal("/ws/f.cs", snap.ActiveFilePath);
        Assert.Equal("abc", snap.ActiveFileContent);
        Assert.Equal("sel", snap.Selection);
        Assert.Equal(2, snap.OpenEditorPaths.Count);
    }

    // ── Policy runtime update ───────────────────────────────────

    [Fact]
    public void Policy_CanBeUpdatedAtRuntime()
    {
        var ctx = CreateFull();
        ctx.Policy = new ContextPolicy(IncludeActiveFileContent: true);
        Assert.NotNull(ctx.GetSnapshot().ActiveFileContent);

        ctx.Policy = new ContextPolicy(IncludeActiveFileContent: false);
        Assert.Null(ctx.GetSnapshot().ActiveFileContent);
    }

    // ── ContentPolicy disabled, active file path still works ────

    [Fact]
    public void ActiveFilePath_Independent_OfContentPolicy()
    {
        var ctx = CreateFull();
        ctx.Policy = new ContextPolicy(IncludeActiveFileContent: false, IncludeActiveFilePath: true);
        var snap = ctx.GetSnapshot();
        Assert.NotNull(snap.ActiveFilePath);
        Assert.Null(snap.ActiveFileContent);
    }
}

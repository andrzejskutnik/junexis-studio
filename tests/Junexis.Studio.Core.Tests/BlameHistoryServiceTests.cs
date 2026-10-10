using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;
using Xunit;

namespace Junexis.Studio.Core.Tests;

/// <summary>
/// Tests for <see cref="BlameHistoryService"/> and blame/history records.
/// </summary>
[Trait("Issue", "76")]
public class BlameHistoryServiceTests
{
    // ── Fake ────────────────────────────────────────────────────

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

    // ── Blame: no backend ───────────────────────────────────────

    [Fact]
    public async Task GetBlame_NoBackend_ReturnsError()
    {
        var sut = new BlameHistoryService();
        var result = await sut.GetBlameAsync("file.cs");
        Assert.False(result.Success);
        Assert.Contains("No blame backend", result.Error!);
    }

    // ── Blame: success ──────────────────────────────────────────

    [Fact]
    public async Task GetBlame_WithBackend_ReturnsEntries()
    {
        var sut = new BlameHistoryService
        {
            BlameBackend = (path, ct) => Task.FromResult<IReadOnlyList<BlameEntry>>(new[]
            {
                new BlameEntry(1, "abc123", "Alice", DateTime.UtcNow, "line 1"),
                new BlameEntry(2, "abc123", "Alice", DateTime.UtcNow, "line 2")
            })
        };

        var result = await sut.GetBlameAsync("file.cs");
        Assert.True(result.Success);
        Assert.Equal(2, result.Entries.Count);
        Assert.Equal("file.cs", result.FilePath);
    }

    // ── Blame: backend throws ───────────────────────────────────

    [Fact]
    public async Task GetBlame_BackendThrows_ReturnsError()
    {
        var sut = new BlameHistoryService
        {
            BlameBackend = (_, _) => throw new InvalidOperationException("git not found")
        };

        var result = await sut.GetBlameAsync("file.cs");
        Assert.False(result.Success);
        Assert.Contains("git not found", result.Error!);
    }

    // ── Blame: empty path ───────────────────────────────────────

    [Fact]
    public async Task GetBlame_EmptyPath_Throws()
    {
        var sut = new BlameHistoryService();
        await Assert.ThrowsAsync<ArgumentException>(() => sut.GetBlameAsync(""));
        await Assert.ThrowsAsync<ArgumentException>(() => sut.GetBlameAsync(null!));
    }

    // ── Blame: logging ──────────────────────────────────────────

    [Fact]
    public async Task GetBlame_Success_LogsToOutput()
    {
        var output = new RecordingOutput();
        var sut = new BlameHistoryService(output)
        {
            BlameBackend = (_, _) => Task.FromResult<IReadOnlyList<BlameEntry>>(Array.Empty<BlameEntry>())
        };

        await sut.GetBlameAsync("test.cs");
        Assert.Contains(output.Lines, l => l.Contains("Blame") && l.Contains("test.cs"));
    }

    [Fact]
    public async Task GetBlame_Error_LogsError()
    {
        var output = new RecordingOutput();
        var sut = new BlameHistoryService(output)
        {
            BlameBackend = (_, _) => throw new Exception("fail")
        };

        await sut.GetBlameAsync("test.cs");
        Assert.Contains(output.Lines, l => l.Contains("Error") && l.Contains("fail"));
    }

    // ── History: no backend ─────────────────────────────────────

    [Fact]
    public async Task GetHistory_NoBackend_ReturnsError()
    {
        var sut = new BlameHistoryService();
        var result = await sut.GetHistoryAsync("file.cs");
        Assert.False(result.Success);
        Assert.Contains("No history backend", result.Error!);
    }

    // ── History: success ────────────────────────────────────────

    [Fact]
    public async Task GetHistory_WithBackend_ReturnsEntries()
    {
        var sut = new BlameHistoryService
        {
            HistoryBackend = (path, max, ct) => Task.FromResult<IReadOnlyList<HistoryEntry>>(new[]
            {
                new HistoryEntry("abc123", "Alice", DateTime.UtcNow, "initial commit"),
                new HistoryEntry("def456", "Bob", DateTime.UtcNow, "fix bug")
            })
        };

        var result = await sut.GetHistoryAsync("file.cs", 10);
        Assert.True(result.Success);
        Assert.Equal(2, result.Entries.Count);
    }

    // ── History: backend throws ─────────────────────────────────

    [Fact]
    public async Task GetHistory_BackendThrows_ReturnsError()
    {
        var sut = new BlameHistoryService
        {
            HistoryBackend = (_, _, _) => throw new Exception("oom")
        };

        var result = await sut.GetHistoryAsync("file.cs");
        Assert.False(result.Success);
        Assert.Contains("oom", result.Error!);
    }

    // ── History: validation ─────────────────────────────────────

    [Fact]
    public async Task GetHistory_EmptyPath_Throws()
    {
        var sut = new BlameHistoryService();
        await Assert.ThrowsAsync<ArgumentException>(() => sut.GetHistoryAsync(""));
    }

    [Fact]
    public async Task GetHistory_ZeroMaxEntries_Throws()
    {
        var sut = new BlameHistoryService();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => sut.GetHistoryAsync("f.cs", 0));
    }

    [Fact]
    public async Task GetHistory_NegativeMaxEntries_Throws()
    {
        var sut = new BlameHistoryService();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => sut.GetHistoryAsync("f.cs", -5));
    }

    // ── History: logging ────────────────────────────────────────

    [Fact]
    public async Task GetHistory_Success_LogsToOutput()
    {
        var output = new RecordingOutput();
        var sut = new BlameHistoryService(output)
        {
            HistoryBackend = (_, _, _) => Task.FromResult<IReadOnlyList<HistoryEntry>>(Array.Empty<HistoryEntry>())
        };

        await sut.GetHistoryAsync("test.cs");
        Assert.Contains(output.Lines, l => l.Contains("History") && l.Contains("test.cs"));
    }

    // ── History: cancellation ───────────────────────────────────

    [Fact]
    public async Task GetHistory_Cancelled_Throws()
    {
        var sut = new BlameHistoryService
        {
            HistoryBackend = (_, _, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                return Task.FromResult<IReadOnlyList<HistoryEntry>>(Array.Empty<HistoryEntry>());
            }
        };

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            sut.GetHistoryAsync("f.cs", cancellationToken: cts.Token));
    }

    [Fact]
    public async Task GetBlame_Cancelled_Throws()
    {
        var sut = new BlameHistoryService
        {
            BlameBackend = (_, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                return Task.FromResult<IReadOnlyList<BlameEntry>>(Array.Empty<BlameEntry>());
            }
        };

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            sut.GetBlameAsync("f.cs", cts.Token));
    }

    // ── Records ─────────────────────────────────────────────────

    [Fact]
    public void BlameEntry_PreservesFields()
    {
        var dt = DateTime.UtcNow;
        var e = new BlameEntry(42, "abc", "Author", dt, "code");
        Assert.Equal(42, e.LineNumber);
        Assert.Equal("abc", e.CommitId);
        Assert.Equal("Author", e.Author);
        Assert.Equal(dt, e.Date);
        Assert.Equal("code", e.LineContent);
    }

    [Fact]
    public void HistoryEntry_PreservesFields()
    {
        var dt = DateTime.UtcNow;
        var e = new HistoryEntry("def", "Bob", dt, "msg");
        Assert.Equal("def", e.CommitId);
        Assert.Equal("Bob", e.Author);
        Assert.Equal("msg", e.Message);
    }

    [Fact]
    public void BlameResult_Success_Property()
    {
        var r = new BlameResult("f.cs", Array.Empty<BlameEntry>());
        Assert.True(r.Success);

        var r2 = new BlameResult("f.cs", Array.Empty<BlameEntry>(), "err");
        Assert.False(r2.Success);
    }

    [Fact]
    public void HistoryResult_Success_Property()
    {
        var r = new HistoryResult("f.cs", Array.Empty<HistoryEntry>());
        Assert.True(r.Success);

        var r2 = new HistoryResult("f.cs", Array.Empty<HistoryEntry>(), "err");
        Assert.False(r2.Success);
    }

    // ── OutputChannel ───────────────────────────────────────────

    [Fact]
    public void OutputChannel_IsVCS()
    {
        Assert.Equal("VCS", BlameHistoryService.OutputChannel);
    }
}

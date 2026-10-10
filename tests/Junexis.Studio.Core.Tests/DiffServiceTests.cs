using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;
using Xunit;

namespace Junexis.Studio.Core.Tests;

/// <summary>
/// Tests for <see cref="DiffService"/> and diff-related records.
/// </summary>
[Trait("Issue", "75")]
public class DiffServiceTests
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

    // ── Constructor ─────────────────────────────────────────────

    [Fact]
    public void Ctor_NullOutput_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new DiffService(null!));
    }

    // ── No backend ──────────────────────────────────────────────

    [Fact]
    public async Task GetDiff_NoBackend_ReturnsError()
    {
        var sut = new DiffService(new RecordingOutput());
        var result = await sut.GetDiffAsync("file.cs");
        Assert.False(result.Success);
        Assert.Contains("No diff backend", result.Error!);
    }

    [Fact]
    public async Task GetDiff_NoBackend_OutputsMessage()
    {
        var output = new RecordingOutput();
        var sut = new DiffService(output);
        await sut.GetDiffAsync("file.cs");
        Assert.Contains(output.Lines, l => l.Contains("No diff backend"));
    }

    // ── Validation ──────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetDiff_InvalidPath_Throws(string? path)
    {
        var sut = new DiffService(new RecordingOutput());
        await Assert.ThrowsAsync<ArgumentException>(() => sut.GetDiffAsync(path!));
    }

    // ── Success scenario ────────────────────────────────────────

    [Fact]
    public async Task GetDiff_WithBackend_ReturnsHunks()
    {
        var output = new RecordingOutput();
        var sut = new DiffService(output)
        {
            DiffBackend = (path, target, ct) =>
            {
                var lines = new DiffLine[]
                {
                    new(DiffLineKind.Context, "  unchanged"),
                    new(DiffLineKind.Removed, "- old line"),
                    new(DiffLineKind.Added,   "+ new line")
                };
                var hunk = new DiffHunk(1, 2, 1, 2, lines);
                return Task.FromResult(new DiffResult(path, target, new[] { hunk }));
            }
        };

        var result = await sut.GetDiffAsync("file.cs", DiffTarget.WorkingTreeVsHead);

        Assert.True(result.Success);
        Assert.Single(result.Hunks);
        Assert.Equal(3, result.Hunks[0].Lines.Count);
        Assert.True(result.HasChanges);
    }

    [Fact]
    public async Task GetDiff_Success_OutputsHunkCount()
    {
        var output = new RecordingOutput();
        var sut = new DiffService(output)
        {
            DiffBackend = (path, target, ct) =>
            {
                var hunk = new DiffHunk(1, 1, 1, 1, new[] { new DiffLine(DiffLineKind.Added, "+x") });
                return Task.FromResult(new DiffResult(path, target, new[] { hunk }));
            }
        };

        await sut.GetDiffAsync("file.cs");
        Assert.Contains(output.Lines, l => l.Contains("1 hunk(s)"));
    }

    // ── No changes ──────────────────────────────────────────────

    [Fact]
    public async Task GetDiff_NoChanges_ReturnsEmptyHunks()
    {
        var sut = new DiffService(new RecordingOutput())
        {
            DiffBackend = (path, target, ct) =>
                Task.FromResult(new DiffResult(path, target, Array.Empty<DiffHunk>()))
        };

        var result = await sut.GetDiffAsync("file.cs");
        Assert.True(result.Success);
        Assert.Empty(result.Hunks);
        Assert.False(result.HasChanges);
    }

    [Fact]
    public async Task GetDiff_NoChanges_OutputsNoChanges()
    {
        var output = new RecordingOutput();
        var sut = new DiffService(output)
        {
            DiffBackend = (path, target, ct) =>
                Task.FromResult(new DiffResult(path, target, Array.Empty<DiffHunk>()))
        };

        await sut.GetDiffAsync("file.cs");
        Assert.Contains(output.Lines, l => l.Contains("No changes"));
    }

    // ── Binary detection ────────────────────────────────────────

    [Fact]
    public async Task GetDiff_BinaryFile_ReturnsBinaryFlag()
    {
        var sut = new DiffService(new RecordingOutput())
        {
            DiffBackend = (path, target, ct) =>
                Task.FromResult(new DiffResult(path, target, Array.Empty<DiffHunk>(), IsBinary: true))
        };

        var result = await sut.GetDiffAsync("image.png");
        Assert.True(result.Success);
        Assert.True(result.IsBinary);
        Assert.True(result.HasChanges);
    }

    [Fact]
    public async Task GetDiff_BinaryFile_OutputsBinaryMessage()
    {
        var output = new RecordingOutput();
        var sut = new DiffService(output)
        {
            DiffBackend = (path, target, ct) =>
                Task.FromResult(new DiffResult(path, target, Array.Empty<DiffHunk>(), IsBinary: true))
        };

        await sut.GetDiffAsync("image.png");
        Assert.Contains(output.Lines, l => l.Contains("Binary file"));
    }

    // ── DiffTarget variants ─────────────────────────────────────

    [Theory]
    [InlineData(DiffTarget.WorkingTreeVsIndex)]
    [InlineData(DiffTarget.WorkingTreeVsHead)]
    [InlineData(DiffTarget.IndexVsHead)]
    public async Task GetDiff_AllTargets_PassedToBackend(DiffTarget expected)
    {
        DiffTarget? captured = null;
        var sut = new DiffService(new RecordingOutput())
        {
            DiffBackend = (path, target, ct) =>
            {
                captured = target;
                return Task.FromResult(new DiffResult(path, target, Array.Empty<DiffHunk>()));
            }
        };

        await sut.GetDiffAsync("file.cs", expected);
        Assert.Equal(expected, captured);
    }

    // ── Cancellation ────────────────────────────────────────────

    [Fact]
    public async Task GetDiff_Cancelled_ThrowsOperationCancelled()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();
        var sut = new DiffService(new RecordingOutput());
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            sut.GetDiffAsync("file.cs", cancellationToken: cts.Token));
    }

    [Fact]
    public async Task GetDiff_BackendThrowsCancellation_Rethrows()
    {
        var sut = new DiffService(new RecordingOutput())
        {
            DiffBackend = (_, _, ct) => throw new OperationCanceledException()
        };

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            sut.GetDiffAsync("file.cs"));
    }

    // ── Backend error ───────────────────────────────────────────

    [Fact]
    public async Task GetDiff_BackendThrows_ReturnsError()
    {
        var sut = new DiffService(new RecordingOutput())
        {
            DiffBackend = (_, _, _) => throw new InvalidOperationException("git failed")
        };

        var result = await sut.GetDiffAsync("file.cs");
        Assert.False(result.Success);
        Assert.Contains("git failed", result.Error!);
    }

    [Fact]
    public async Task GetDiff_BackendThrows_OutputsError()
    {
        var output = new RecordingOutput();
        var sut = new DiffService(output)
        {
            DiffBackend = (_, _, _) => throw new InvalidOperationException("git failed")
        };

        await sut.GetDiffAsync("file.cs");
        Assert.Contains(output.Lines, l => l.Contains("Diff failed"));
    }

    // ── Record tests ────────────────────────────────────────────

    [Fact]
    public void DiffResult_Success_WhenNoError()
    {
        var r = new DiffResult("a.cs", DiffTarget.WorkingTreeVsHead, Array.Empty<DiffHunk>());
        Assert.True(r.Success);
        Assert.Null(r.Error);
    }

    [Fact]
    public void DiffResult_NotSuccess_WhenError()
    {
        var r = new DiffResult("a.cs", DiffTarget.WorkingTreeVsHead, Array.Empty<DiffHunk>(), Error: "fail");
        Assert.False(r.Success);
        Assert.False(r.HasChanges);
    }

    [Fact]
    public void DiffHunk_StoresLinesProperly()
    {
        var lines = new[] { new DiffLine(DiffLineKind.Added, "hello") };
        var hunk = new DiffHunk(10, 1, 10, 2, lines);
        Assert.Equal(10, hunk.OldStart);
        Assert.Equal(1, hunk.OldCount);
        Assert.Equal(10, hunk.NewStart);
        Assert.Equal(2, hunk.NewCount);
        Assert.Single(hunk.Lines);
    }

    [Fact]
    public void DiffLine_StoresKindAndContent()
    {
        var line = new DiffLine(DiffLineKind.Removed, "old text");
        Assert.Equal(DiffLineKind.Removed, line.Kind);
        Assert.Equal("old text", line.Content);
    }

    [Theory]
    [InlineData(DiffLineKind.Context)]
    [InlineData(DiffLineKind.Added)]
    [InlineData(DiffLineKind.Removed)]
    public void DiffLineKind_AllValuesValid(DiffLineKind kind)
    {
        Assert.True(Enum.IsDefined(kind));
    }

    [Theory]
    [InlineData(DiffTarget.WorkingTreeVsIndex)]
    [InlineData(DiffTarget.WorkingTreeVsHead)]
    [InlineData(DiffTarget.IndexVsHead)]
    public void DiffTarget_AllValuesValid(DiffTarget target)
    {
        Assert.True(Enum.IsDefined(target));
    }
}

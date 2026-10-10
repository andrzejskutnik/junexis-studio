using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;
using Xunit;

namespace Junexis.Studio.Core.Tests;

/// <summary>
/// Tests for <see cref="SafeFileSystem"/> — root-jail, path traversal blocking, audit logging.
/// </summary>
[Trait("Issue", "88")]
public class SafeFileSystemTests
{
    // ── Fakes ───────────────────────────────────────────────────

    private sealed class InMemoryFileSystem : IFileSystem
    {
        private readonly Dictionary<string, string> _files = new(StringComparer.OrdinalIgnoreCase);

        public void AddFile(string path, string content) => _files[Path.GetFullPath(path)] = content;

        public string ReadFile(string path) =>
            _files.TryGetValue(Path.GetFullPath(path), out var c)
                ? c
                : throw new FileNotFoundException("Not found", path);

        public void WriteFile(string path, string content) => _files[Path.GetFullPath(path)] = content;

        public bool Exists(string path) => _files.ContainsKey(Path.GetFullPath(path));

        public IEnumerable<string> EnumerateDirectory(string path) =>
            _files.Keys.Where(k => k.StartsWith(Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase));

        public IDisposable Watch(string rootPath, bool includeSubdirectories, Action<FileSystemChange> onChange)
            => new NoopDisposable();

        private sealed class NoopDisposable : IDisposable { public void Dispose() { } }
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

    private static string TestRoot => Path.GetFullPath(Path.Combine(Path.GetTempPath(), "safe_fs_test"));

    private (SafeFileSystem safe, InMemoryFileSystem inner, RecordingOutput output) Create(string? root = null)
    {
        var inner = new InMemoryFileSystem();
        var output = new RecordingOutput();
        var r = root ?? TestRoot;
        var safe = new SafeFileSystem(inner, r, output);
        return (safe, inner, output);
    }

    // ── Constructor ─────────────────────────────────────────────

    [Fact]
    public void Constructor_NullInner_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new SafeFileSystem(null!, TestRoot));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_EmptyRoot_Throws(string? root)
    {
        var inner = new InMemoryFileSystem();
        Assert.Throws<ArgumentException>(() => new SafeFileSystem(inner, root!));
    }

    [Fact]
    public void RootPath_ReturnsNormalizedPath()
    {
        var (safe, _, _) = Create();
        Assert.Equal(TestRoot, safe.RootPath);
    }

    // ── ReadFile — valid paths ──────────────────────────────────

    [Fact]
    public void ReadFile_RelativePath_ResolvesWithinRoot()
    {
        var (safe, inner, _) = Create();
        var filePath = Path.Combine(TestRoot, "sub", "file.txt");
        inner.AddFile(filePath, "hello");

        var content = safe.ReadFile(Path.Combine("sub", "file.txt"));
        Assert.Equal("hello", content);
    }

    [Fact]
    public void ReadFile_AbsolutePathWithinRoot_Succeeds()
    {
        var (safe, inner, _) = Create();
        var filePath = Path.Combine(TestRoot, "file.txt");
        inner.AddFile(filePath, "data");

        var content = safe.ReadFile(filePath);
        Assert.Equal("data", content);
    }

    // ── ReadFile — traversal blocked ────────────────────────────

    [Fact]
    public void ReadFile_DotDotTraversal_Throws()
    {
        var (safe, _, _) = Create();
        Assert.Throws<UnauthorizedAccessException>(() => safe.ReadFile("..\\secret.txt"));
    }

    [Fact]
    public void ReadFile_AbsolutePathOutsideRoot_Throws()
    {
        var (safe, _, _) = Create();
        var outside = Path.GetFullPath(Path.Combine(TestRoot, "..", "secret.txt"));
        Assert.Throws<UnauthorizedAccessException>(() => safe.ReadFile(outside));
    }

    [Fact]
    public void ReadFile_DeeplyNestedTraversal_Throws()
    {
        var (safe, _, _) = Create();
        Assert.Throws<UnauthorizedAccessException>(() => safe.ReadFile("sub\\..\\..\\..\\etc\\passwd"));
    }

    [Fact]
    public void ReadFile_EmptyPath_Throws()
    {
        var (safe, _, _) = Create();
        Assert.Throws<ArgumentException>(() => safe.ReadFile(""));
    }

    // ── WriteFile — valid and blocked ───────────────────────────

    [Fact]
    public void WriteFile_RelativePath_Succeeds()
    {
        var (safe, inner, _) = Create();
        safe.WriteFile("newfile.txt", "content");
        Assert.True(inner.Exists(Path.Combine(TestRoot, "newfile.txt")));
    }

    [Fact]
    public void WriteFile_Traversal_Throws()
    {
        var (safe, _, _) = Create();
        Assert.Throws<UnauthorizedAccessException>(() => safe.WriteFile("..\\attack.txt", "payload"));
    }

    // ── Exists — valid and blocked ──────────────────────────────

    [Fact]
    public void Exists_RelativePath_Succeeds()
    {
        var (safe, inner, _) = Create();
        inner.AddFile(Path.Combine(TestRoot, "lib.dll"), "bin");
        Assert.True(safe.Exists("lib.dll"));
    }

    [Fact]
    public void Exists_Traversal_Throws()
    {
        var (safe, _, _) = Create();
        Assert.Throws<UnauthorizedAccessException>(() => safe.Exists("..\\..\\Windows\\system32\\cmd.exe"));
    }

    // ── EnumerateDirectory ──────────────────────────────────────

    [Fact]
    public void EnumerateDirectory_WithinRoot_Succeeds()
    {
        var (safe, inner, _) = Create();
        inner.AddFile(Path.Combine(TestRoot, "a.txt"), "a");
        inner.AddFile(Path.Combine(TestRoot, "b.txt"), "b");
        var entries = safe.EnumerateDirectory(TestRoot).ToList();
        Assert.Equal(2, entries.Count);
    }

    [Fact]
    public void EnumerateDirectory_TraversalBlocked()
    {
        var (safe, _, _) = Create();
        var outside = Path.GetFullPath(Path.Combine(TestRoot, ".."));
        Assert.Throws<UnauthorizedAccessException>(() => safe.EnumerateDirectory(outside).ToList());
    }

    // ── Watch ───────────────────────────────────────────────────

    [Fact]
    public void Watch_WithinRoot_Succeeds()
    {
        var (safe, _, _) = Create();
        using var subscription = safe.Watch(TestRoot, true, _ => { });
        Assert.NotNull(subscription);
    }

    [Fact]
    public void Watch_Traversal_Throws()
    {
        var (safe, _, _) = Create();
        var outside = Path.GetFullPath(Path.Combine(TestRoot, ".."));
        Assert.Throws<UnauthorizedAccessException>(() => safe.Watch(outside, true, _ => { }));
    }

    // ── Audit logging ───────────────────────────────────────────

    [Fact]
    public void TraversalBlocked_LogsToSecurityChannel()
    {
        var (safe, _, output) = Create();
        Assert.Throws<UnauthorizedAccessException>(() => safe.ReadFile("..\\secret.txt"));
        Assert.Contains(output.Lines, l => l.Contains("Security") && l.Contains("traversal"));
    }

    [Fact]
    public void TraversalBlocked_IncludesPathInAuditMessage()
    {
        var (safe, _, output) = Create();
        Assert.Throws<UnauthorizedAccessException>(() => safe.ReadFile("..\\secret.txt"));
        Assert.Contains(output.Lines, l => l.Contains("secret.txt"));
    }

    // ── Root path itself is valid ───────────────────────────────

    [Fact]
    public void ReadFile_ExactRootPath_IsAllowed()
    {
        var (safe, inner, _) = Create();
        // Root directory itself should be accessible (e.g., for Exists check)
        Assert.False(safe.Exists(TestRoot)); // doesn't exist in in-memory fs, but no traversal error
    }

    // ── AuditChannel constant ───────────────────────────────────

    [Fact]
    public void AuditChannel_IsSecurity()
    {
        Assert.Equal("Security", SafeFileSystem.AuditChannel);
    }

    // ── Mixed traversal patterns ────────────────────────────────

    [Theory]
    [InlineData("..")]
    [InlineData("..\\..")]
    [InlineData("sub\\..\\..")]
    [InlineData("sub\\..\\..\\..\\etc")]
    public void ReadFile_VariousTraversalPatterns_AllBlocked(string traversal)
    {
        var (safe, _, _) = Create();
        Assert.Throws<UnauthorizedAccessException>(() => safe.ReadFile(traversal));
    }

    [Fact]
    public void WriteFile_AbsoluteOutsideRoot_Throws()
    {
        var (safe, _, _) = Create();
        var outside = Path.GetFullPath(Path.Combine(TestRoot, "..", "evil.txt"));
        Assert.Throws<UnauthorizedAccessException>(() => safe.WriteFile(outside, "payload"));
    }

    // ── DotDot within valid relative path (stays inside root) ───

    [Fact]
    public void ReadFile_DotDotButStaysInRoot_Succeeds()
    {
        var (safe, inner, _) = Create();
        var filePath = Path.Combine(TestRoot, "file.txt");
        inner.AddFile(filePath, "ok");

        // "sub/../file.txt" resolves to root/file.txt — stays within root
        var content = safe.ReadFile(Path.Combine("sub", "..", "file.txt"));
        Assert.Equal("ok", content);
    }
}

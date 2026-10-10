using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;
using Xunit;

namespace Junexis.Studio.Core.Tests;

/// <summary>
/// Tests for <see cref="StartupSelfCheck"/> — startup self-check, built-in checks, reporting.
/// </summary>
[Trait("Issue", "89")]
public class StartupSelfCheckTests
{
    // ── Fakes ───────────────────────────────────────────────────

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

    private sealed class RecordingDiagnostics : IDiagnosticService
    {
        public List<Diagnostic> Added { get; } = new();
        public event Action? DiagnosticsChanged;
        public void Clear() { }
        public void Add(Diagnostic d) => Added.Add(d);
        public IReadOnlyList<Diagnostic> GetDiagnostics() => Added;
        public IReadOnlyList<Diagnostic> GetBySource(string source) =>
            Added.Where(d => string.Equals(d.Source, source, StringComparison.OrdinalIgnoreCase)).ToList();
        public void ClearBySource(string source) { }
    }

    // ── Run: no checks ──────────────────────────────────────────

    [Fact]
    public void Run_NoChecks_AllPassedTrue()
    {
        var sut = new StartupSelfCheck();
        var result = sut.Run();
        Assert.True(result.AllPassed);
        Assert.Empty(result.Entries);
    }

    // ── Run: single passing check ───────────────────────────────

    [Fact]
    public void Run_SinglePassingCheck_AllPassed()
    {
        var sut = new StartupSelfCheck();
        sut.AddCheck(() => new StartupCheckEntry("Test", "ok", true, null));
        var result = sut.Run();
        Assert.True(result.AllPassed);
        Assert.Single(result.Entries);
        Assert.Null(result.Entries[0].Fix);
    }

    // ── Run: single failing check ───────────────────────────────

    [Fact]
    public void Run_SingleFailingCheck_AllPassedFalse()
    {
        var sut = new StartupSelfCheck();
        sut.AddCheck(() => new StartupCheckEntry("Test", "bad", false, "Fix it"));
        var result = sut.Run();
        Assert.False(result.AllPassed);
        Assert.Single(result.Entries);
        Assert.Equal("Fix it", result.Entries[0].Fix);
    }

    // ── Run: mixed ──────────────────────────────────────────────

    [Fact]
    public void Run_MixedChecks_AllPassedFalse()
    {
        var sut = new StartupSelfCheck();
        sut.AddCheck(() => new StartupCheckEntry("A", "good", true, null));
        sut.AddCheck(() => new StartupCheckEntry("B", "bad", false, "fix"));
        var result = sut.Run();
        Assert.False(result.AllPassed);
        Assert.Equal(2, result.Entries.Count);
    }

    // ── Run: throwing check ─────────────────────────────────────

    [Fact]
    public void Run_ThrowingCheck_CapturedAsError()
    {
        var sut = new StartupSelfCheck();
        sut.AddCheck(() => throw new InvalidOperationException("boom"));
        var result = sut.Run();
        Assert.False(result.AllPassed);
        Assert.Single(result.Entries);
        Assert.Equal("Error", result.Entries[0].Category);
        Assert.Contains("boom", result.Entries[0].Description);
    }

    // ── AddCheck: null ──────────────────────────────────────────

    [Fact]
    public void AddCheck_Null_Throws()
    {
        var sut = new StartupSelfCheck();
        Assert.Throws<ArgumentNullException>(() => sut.AddCheck(null!));
    }

    // ── Output logging: all passed ──────────────────────────────

    [Fact]
    public void Run_AllPassed_LogsSuccess()
    {
        var output = new RecordingOutput();
        var sut = new StartupSelfCheck(output);
        sut.AddCheck(() => new StartupCheckEntry("T", "ok", true, null));
        sut.Run();
        Assert.Contains(output.Lines, l => l.Contains("passed"));
    }

    // ── Output logging: failure ─────────────────────────────────

    [Fact]
    public void Run_Failure_LogsWarning()
    {
        var output = new RecordingOutput();
        var sut = new StartupSelfCheck(output);
        sut.AddCheck(() => new StartupCheckEntry("T", "bad", false, "do X"));
        sut.Run();
        Assert.Contains(output.Lines, l => l.Contains("issue(s) found"));
        Assert.Contains(output.Lines, l => l.Contains("do X"));
    }

    // ── Diagnostics: failure adds diagnostic ────────────────────

    [Fact]
    public void Run_Failure_AddsDiagnostic()
    {
        var diag = new RecordingDiagnostics();
        var sut = new StartupSelfCheck(diagnostics: diag);
        sut.AddCheck(() => new StartupCheckEntry("Svc", "missing", false, "add it"));
        sut.Run();
        Assert.Single(diag.Added);
        Assert.Equal("StartupSelfCheck", diag.Added[0].Source);
        Assert.Equal(DiagnosticSeverity.Warning, diag.Added[0].Severity);
    }

    // ── Diagnostics: pass does not add diagnostic ───────────────

    [Fact]
    public void Run_AllPassed_NoDiagnostics()
    {
        var diag = new RecordingDiagnostics();
        var sut = new StartupSelfCheck(diagnostics: diag);
        sut.AddCheck(() => new StartupCheckEntry("T", "ok", true, null));
        sut.Run();
        Assert.Empty(diag.Added);
    }

    // ── CreateDefault: runtime check passes (we are on .NET 10+) ─

    [Fact]
    public void CreateDefault_RuntimeCheck_PassesOnCurrentRuntime()
    {
        var sut = StartupSelfCheck.CreateDefault(minimumRuntimeMajor: 1);
        var result = sut.Run();
        var runtime = result.Entries.FirstOrDefault(e => e.Category == "Runtime");
        Assert.NotNull(runtime);
        Assert.True(runtime.Passed);
    }

    // ── CreateDefault: runtime check fails on high requirement ──

    [Fact]
    public void CreateDefault_RuntimeCheck_FailsOnHighRequirement()
    {
        var sut = StartupSelfCheck.CreateDefault(minimumRuntimeMajor: 999);
        var result = sut.Run();
        var runtime = result.Entries.FirstOrDefault(e => e.Category == "Runtime");
        Assert.NotNull(runtime);
        Assert.False(runtime.Passed);
        Assert.Contains("999", runtime.Fix!);
    }

    // ── CreateDefault: required services ─────────────────────────

    [Fact]
    public void CreateDefault_RequiredServices_AllPresent()
    {
        var sut = StartupSelfCheck.CreateDefault(
            requiredServices: new[] { "EventBus", "Output" },
            availableServices: new[] { "eventbus", "output", "extra" });
        var result = sut.Run();
        var svcEntries = result.Entries.Where(e => e.Category == "Service").ToList();
        Assert.Equal(2, svcEntries.Count);
        Assert.True(svcEntries.All(e => e.Passed));
    }

    [Fact]
    public void CreateDefault_RequiredServices_Missing()
    {
        var sut = StartupSelfCheck.CreateDefault(
            requiredServices: new[] { "EventBus", "MCP" },
            availableServices: new[] { "EventBus" });
        var result = sut.Run();
        var missing = result.Entries.FirstOrDefault(e => e.Category == "Service" && !e.Passed);
        Assert.NotNull(missing);
        Assert.Contains("MCP", missing.Description);
        Assert.Contains("missing", missing.Fix!, StringComparison.OrdinalIgnoreCase);
    }

    // ── CreateDefault: extension duplicates ──────────────────────

    [Fact]
    public void CreateDefault_NoDuplicateExtensions_Passes()
    {
        var sut = StartupSelfCheck.CreateDefault(
            extensionIds: new[] { "ext1", "ext2", "ext3" });
        var result = sut.Run();
        var extEntry = result.Entries.FirstOrDefault(e => e.Category == "Extension");
        Assert.NotNull(extEntry);
        Assert.True(extEntry.Passed);
    }

    [Fact]
    public void CreateDefault_DuplicateExtensions_Fails()
    {
        var sut = StartupSelfCheck.CreateDefault(
            extensionIds: new[] { "ext1", "EXT1", "ext2" });
        var result = sut.Run();
        var extEntry = result.Entries.FirstOrDefault(e => e.Category == "Extension");
        Assert.NotNull(extEntry);
        Assert.False(extEntry.Passed);
        Assert.Contains("ext1", extEntry.Description, StringComparison.OrdinalIgnoreCase);
    }

    // ── CreateDefault: clean run (no false positives) ────────────

    [Fact]
    public void CreateDefault_CleanRun_AllPassed()
    {
        var sut = StartupSelfCheck.CreateDefault(
            requiredServices: new[] { "A" },
            availableServices: new[] { "A" },
            extensionIds: new[] { "e1" },
            minimumRuntimeMajor: 1);
        var result = sut.Run();
        Assert.True(result.AllPassed);
    }

    // ── Constants ────────────────────────────────────────────────

    [Fact]
    public void OutputChannel_IsIntegrity()
    {
        Assert.Equal("Integrity", StartupSelfCheck.OutputChannel);
    }

    [Fact]
    public void DiagnosticSource_IsStartupSelfCheck()
    {
        Assert.Equal("StartupSelfCheck", StartupSelfCheck.DiagnosticSource);
    }

    // ── Records ──────────────────────────────────────────────────

    [Fact]
    public void StartupCheckEntry_PreservesFields()
    {
        var e = new StartupCheckEntry("Cat", "Desc", true, "hint");
        Assert.Equal("Cat", e.Category);
        Assert.Equal("Desc", e.Description);
        Assert.True(e.Passed);
        Assert.Equal("hint", e.Fix);
    }

    [Fact]
    public void StartupCheckResult_PreservesFields()
    {
        var entries = new List<StartupCheckEntry>
        {
            new("A", "ok", true, null)
        };
        var r = new StartupCheckResult(entries, true);
        Assert.True(r.AllPassed);
        Assert.Single(r.Entries);
    }

    // ── Multiple failures: all reported ──────────────────────────

    [Fact]
    public void Run_MultipleFailures_AllReported()
    {
        var diag = new RecordingDiagnostics();
        var output = new RecordingOutput();
        var sut = new StartupSelfCheck(output, diag);
        sut.AddCheck(() => new StartupCheckEntry("A", "fail1", false, "f1"));
        sut.AddCheck(() => new StartupCheckEntry("B", "fail2", false, "f2"));
        sut.Run();
        Assert.Equal(2, diag.Added.Count);
        // Output: 1 summary + 2 individual warnings = 3 total
        Assert.True(output.Lines.Count >= 3);
    }
}

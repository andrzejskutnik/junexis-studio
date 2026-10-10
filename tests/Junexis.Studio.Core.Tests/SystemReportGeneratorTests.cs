using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;
using Xunit;

namespace Junexis.Studio.Core.Tests;

/// <summary>
/// Tests for <see cref="SystemReportGenerator"/> — report generation, formatting, output.
/// </summary>
[Trait("Issue", "92")]
public class SystemReportGeneratorTests
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

    // ── Generate: minimal (no providers) ────────────────────────

    [Fact]
    public void Generate_NoProviders_ReturnsValidReport()
    {
        var gen = new SystemReportGenerator();
        var report = gen.Generate();
        Assert.NotNull(report);
        Assert.NotNull(report.Environment);
        Assert.NotNull(report.Environment.OsDescription);
        Assert.NotNull(report.Environment.RuntimeVersion);
        Assert.NotNull(report.Environment.ProcessArchitecture);
        Assert.Empty(report.Extensions);
        Assert.Empty(report.Services);
        Assert.Empty(report.RecentDiagnostics);
    }

    [Fact]
    public void Generate_Timestamp_IsRecent()
    {
        var gen = new SystemReportGenerator();
        var before = DateTime.UtcNow.AddSeconds(-1);
        var report = gen.Generate();
        Assert.True(report.GeneratedAtUtc >= before);
        Assert.True(report.GeneratedAtUtc <= DateTime.UtcNow.AddSeconds(1));
    }

    // ── Generate: with providers ────────────────────────────────

    [Fact]
    public void Generate_WithWorkspaceRoot()
    {
        var gen = new SystemReportGenerator(workspaceRootProvider: () => "/ws/project");
        var report = gen.Generate();
        Assert.Equal("/ws/project", report.Environment.WorkspaceRoot);
    }

    [Fact]
    public void Generate_WithExtensions()
    {
        var exts = new List<ExtensionInfo>
        {
            new("gpp-compiler", true),
            new("vcs", false)
        };
        var gen = new SystemReportGenerator(extensionsProvider: () => exts);
        var report = gen.Generate();
        Assert.Equal(2, report.Extensions.Count);
        Assert.True(report.Extensions[0].IsActive);
        Assert.False(report.Extensions[1].IsActive);
    }

    [Fact]
    public void Generate_WithServices()
    {
        var svcs = new List<ServiceStatus>
        {
            new("EventBus", "Running", true),
            new("MCP", "Disconnected", false)
        };
        var gen = new SystemReportGenerator(servicesProvider: () => svcs);
        var report = gen.Generate();
        Assert.Equal(2, report.Services.Count);
    }

    [Fact]
    public void Generate_WithDiagnostics()
    {
        var diags = new List<string> { "Warning: config missing name", "Error: build failed" };
        var gen = new SystemReportGenerator(diagnosticsProvider: () => diags);
        var report = gen.Generate();
        Assert.Equal(2, report.RecentDiagnostics.Count);
    }

    // ── Generate: output logging ────────────────────────────────

    [Fact]
    public void Generate_LogsToOutput()
    {
        var output = new RecordingOutput();
        var gen = new SystemReportGenerator(output);
        gen.Generate();
        Assert.Contains(output.Lines, l => l.Contains("report generated"));
    }

    // ── Format: empty report ────────────────────────────────────

    [Fact]
    public void Format_EmptyReport_ContainsHeader()
    {
        var report = new SystemReport(
            DateTime.UtcNow,
            new EnvironmentInfo("TestOS", ".NET 10", null, "x64"),
            [], [], []);
        var text = SystemReportGenerator.Format(report);
        Assert.Contains("JunexisStudio System Report", text);
        Assert.Contains("TestOS", text);
        Assert.Contains(".NET 10", text);
        Assert.Contains("(not opened)", text);
        Assert.Contains("(none)", text);
    }

    // ── Format: with data ───────────────────────────────────────

    [Fact]
    public void Format_WithExtensions_ShowsActiveInactive()
    {
        var report = new SystemReport(
            DateTime.UtcNow,
            new EnvironmentInfo("OS", "RT", "/ws", "ARM64"),
            new[] { new ExtensionInfo("ext1", true), new ExtensionInfo("ext2", false) },
            [], []);
        var text = SystemReportGenerator.Format(report);
        Assert.Contains("[active] ext1", text);
        Assert.Contains("[inactive] ext2", text);
    }

    [Fact]
    public void Format_WithServices_ShowsHealthStatus()
    {
        var report = new SystemReport(
            DateTime.UtcNow,
            new EnvironmentInfo("OS", "RT", null, "x64"),
            [],
            new[] { new ServiceStatus("Bus", "Running", true), new ServiceStatus("MCP", "Down", false) },
            []);
        var text = SystemReportGenerator.Format(report);
        Assert.Contains("[OK] Bus: Running", text);
        Assert.Contains("[!!] MCP: Down", text);
    }

    [Fact]
    public void Format_WithDiagnostics_ShowsList()
    {
        var report = new SystemReport(
            DateTime.UtcNow,
            new EnvironmentInfo("OS", "RT", null, "x64"),
            [], [],
            new[] { "diag1", "diag2" });
        var text = SystemReportGenerator.Format(report);
        Assert.Contains("- diag1", text);
        Assert.Contains("- diag2", text);
    }

    [Fact]
    public void Format_NullReport_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => SystemReportGenerator.Format(null!));
    }

    // ── Format: workspace shown ─────────────────────────────────

    [Fact]
    public void Format_WithWorkspace_ShowsPath()
    {
        var report = new SystemReport(
            DateTime.UtcNow,
            new EnvironmentInfo("OS", "RT", "/my/workspace", "x64"),
            [], [], []);
        var text = SystemReportGenerator.Format(report);
        Assert.Contains("/my/workspace", text);
        Assert.DoesNotContain("(not opened)", text);
    }

    // ── Format: deterministic ───────────────────────────────────

    [Fact]
    public void Format_IsDeterministic()
    {
        var ts = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);
        var report = new SystemReport(ts,
            new EnvironmentInfo("OS", "RT", null, "x64"),
            new[] { new ExtensionInfo("e1", true) },
            new[] { new ServiceStatus("s1", "ok", true) },
            new[] { "d1" });
        var text1 = SystemReportGenerator.Format(report);
        var text2 = SystemReportGenerator.Format(report);
        Assert.Equal(text1, text2);
    }

    // ── Records ─────────────────────────────────────────────────

    [Fact]
    public void EnvironmentInfo_PreservesFields()
    {
        var env = new EnvironmentInfo("Win11", ".NET 10", "/ws", "x64");
        Assert.Equal("Win11", env.OsDescription);
        Assert.Equal("/ws", env.WorkspaceRoot);
    }

    [Fact]
    public void ExtensionInfo_PreservesFields()
    {
        var ext = new ExtensionInfo("my-ext", true);
        Assert.Equal("my-ext", ext.Id);
        Assert.True(ext.IsActive);
    }

    [Fact]
    public void ServiceStatus_PreservesFields()
    {
        var svc = new ServiceStatus("EventBus", "Running", true);
        Assert.Equal("EventBus", svc.Name);
        Assert.True(svc.IsHealthy);
    }

    // ── OutputChannel constant ──────────────────────────────────

    [Fact]
    public void OutputChannel_IsIntegrity()
    {
        Assert.Equal("Integrity", SystemReportGenerator.OutputChannel);
    }

    // ── Sections count in formatted output ──────────────────────

    [Fact]
    public void Format_ContainsAllSections()
    {
        var report = new SystemReport(
            DateTime.UtcNow,
            new EnvironmentInfo("OS", "RT", null, "x64"),
            [], [], []);
        var text = SystemReportGenerator.Format(report);
        Assert.Contains("Environment", text);
        Assert.Contains("Extensions", text);
        Assert.Contains("Services", text);
        Assert.Contains("Diagnostics", text);
    }
}

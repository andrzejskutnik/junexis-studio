using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;
using Xunit;

namespace Junexis.Studio.Core.Tests;

/// <summary>
/// Tests for <see cref="AutoBuildService"/>, <see cref="WorkspaceOpenedEvent"/>
/// and <see cref="AutoBuildConfig"/>.
/// </summary>
[Trait("Issue", "58")]
public class AutoBuildServiceTests
{
    // ── Fakes ───────────────────────────────────────────────────

    private sealed class FakeCompiler : ICompilerService
    {
        public string CompilerId => "fake";
        public int BuildCallCount;
        public bool SupportsFile(string ext) => true;

        public Task<CompilerResult> BuildAsync(BuildContext ctx)
        {
            Interlocked.Increment(ref BuildCallCount);
            return Task.FromResult(new CompilerResult(true, Array.Empty<Diagnostic>(), CompilerId));
        }

        public Task<CompilerResult> CleanAsync(BuildContext ctx)
            => Task.FromResult(new CompilerResult(true, Array.Empty<Diagnostic>(), CompilerId));

        public IReadOnlyList<Diagnostic> GetDiagnostics() => Array.Empty<Diagnostic>();
    }

    private sealed class FakeWorkspace : IWorkspace
    {
        public string? RootPath { get; set; } = "c:\\work";
        public event Action<string>? FileOpened;
        public event Action<string>? FolderOpened;
        public event Action<string>? FileSaved;
        public void OpenFolder(string path) { }
        public string ReadFile(string path) => "";
        public void WriteFile(string path, string content) { }
        public string? ReadWorkspaceConfig() => null;
        public string? GetWorkspaceConfigRaw() => null;
        public void SetWorkspaceConfigRaw(string content) { }
    }

    private sealed class RecordingOutput : IOutput
    {
        public List<string> Lines { get; } = new();
        public string RegisterChannel(string ch) => ch;
        public IReadOnlyList<string> GetChannelsSnapshot() => Array.Empty<string>();
        public void Write(string ch, string msg) => Lines.Add($"[{ch}] {msg}");
        public void Write(string ch, string msg, OutputSeverity sev) => Lines.Add($"[{ch}] {msg}");
        public void Write(string ch, string msg, OutputSeverity sev, string? src, string? ex = null) => Lines.Add($"[{ch}] {msg}");
        public IReadOnlyList<OutputEntry> GetSnapshot() => Array.Empty<OutputEntry>();
        public event Action<OutputEntry>? EntryWritten;
    }

    private (AutoBuildService svc, EventBus bus, FakeCompiler compiler, RecordingOutput output) CreateEnabled(int debounceMs = 50)
    {
        var bus = new EventBus();
        var output = new RecordingOutput();
        var ws = new FakeWorkspace();
        var compiler = new FakeCompiler();
        var sel = new CompilerSelector(output);
        sel.Register(compiler);
        var orch = new BuildOrchestrator(sel, bus, output);
        var config = new AutoBuildConfig(Enabled: true, DebounceMs: debounceMs);
        var svc = new AutoBuildService(orch, bus, ws, output, config);
        return (svc, bus, compiler, output);
    }

    // ── Tests ───────────────────────────────────────────────────

    [Fact]
    public async Task DocumentSaved_WhenEnabled_TriggersBuild()
    {
        var (svc, bus, compiler, _) = CreateEnabled(debounceMs: 10);
        using var _ = svc;

        bus.Publish(new DocumentSaved("file.nx"));

        // Wait for debounce + build
        await Task.Delay(200);

        Assert.True(compiler.BuildCallCount >= 1);
        Assert.True(svc.BuildCount >= 1);
    }

    [Fact]
    public async Task DocumentSaved_WhenDisabled_DoesNotBuild()
    {
        var bus = new EventBus();
        var ws = new FakeWorkspace();
        var output = new RecordingOutput();
        var compiler = new FakeCompiler();
        var sel = new CompilerSelector(output);
        sel.Register(compiler);
        var orch = new BuildOrchestrator(sel, bus, output);
        // Disabled by default
        using var svc = new AutoBuildService(orch, bus, ws, output);

        bus.Publish(new DocumentSaved("file.nx"));

        await Task.Delay(200);

        Assert.Equal(0, compiler.BuildCallCount);
        Assert.Equal(0, svc.BuildCount);
    }

    [Fact]
    public async Task WorkspaceOpened_WhenEnabled_TriggersBuild()
    {
        var (svc, bus, compiler, _) = CreateEnabled(debounceMs: 10);
        using var _ = svc;

        bus.Publish(new WorkspaceOpenedEvent("c:\\project"));

        await Task.Delay(200);

        Assert.True(compiler.BuildCallCount >= 1);
    }

    [Fact]
    public async Task Debounce_MultipleRapidSaves_OnlyOneBuild()
    {
        var (svc, bus, compiler, _) = CreateEnabled(debounceMs: 100);
        using var _ = svc;

        // Fire 5 rapid saves
        for (int i = 0; i < 5; i++)
        {
            bus.Publish(new DocumentSaved($"file{i}.nx"));
            await Task.Delay(10);
        }

        // Wait for debounce + build
        await Task.Delay(400);

        // Should have triggered only 1 build due to debounce
        Assert.Equal(1, compiler.BuildCallCount);
    }

    [Fact]
    public void UpdateConfig_SetsNewConfig()
    {
        var (svc, _, _, output) = CreateEnabled();
        using var _ = svc;

        var newConfig = new AutoBuildConfig(Enabled: false, DebounceMs: 1000);
        svc.UpdateConfig(newConfig);

        Assert.Equal(newConfig, svc.Config);
        Assert.Contains(output.Lines, l => l.Contains("config updated"));
    }

    [Fact]
    public async Task UpdateConfig_ToDisabled_StopsTriggeringBuilds()
    {
        var (svc, bus, compiler, _) = CreateEnabled(debounceMs: 10);
        using var _ = svc;

        svc.UpdateConfig(new AutoBuildConfig(Enabled: false));

        bus.Publish(new DocumentSaved("file.nx"));

        await Task.Delay(200);

        Assert.Equal(0, compiler.BuildCallCount);
    }

    [Fact]
    public void Dispose_UnsubscribesFromEvents()
    {
        var (svc, bus, compiler, _) = CreateEnabled(debounceMs: 10);

        svc.Dispose();
        // Double dispose should be safe
        svc.Dispose();

        // Publishing after dispose should not trigger build
        bus.Publish(new DocumentSaved("file.nx"));
    }

    [Fact]
    public void Constructor_NullOrchestrator_Throws()
    {
        var bus = new EventBus();
        var ws = new FakeWorkspace();
        Assert.Throws<ArgumentNullException>(() => new AutoBuildService(null!, bus, ws));
    }

    [Fact]
    public void Constructor_NullEventBus_Throws()
    {
        var output = new RecordingOutput();
        var ws = new FakeWorkspace();
        var sel = new CompilerSelector(output);
        var orch = new BuildOrchestrator(sel);
        Assert.Throws<ArgumentNullException>(() => new AutoBuildService(orch, null!, ws));
    }

    [Fact]
    public void Constructor_NullWorkspace_Throws()
    {
        var bus = new EventBus();
        var output = new RecordingOutput();
        var sel = new CompilerSelector(output);
        var orch = new BuildOrchestrator(sel);
        Assert.Throws<ArgumentNullException>(() => new AutoBuildService(orch, bus, null!));
    }

    [Fact]
    public void UpdateConfig_Null_Throws()
    {
        var (svc, _, _, _) = CreateEnabled();
        using var _ = svc;
        Assert.Throws<ArgumentNullException>(() => svc.UpdateConfig(null!));
    }

    [Fact]
    public void AutoBuildConfig_DefaultValues()
    {
        var config = new AutoBuildConfig();
        Assert.False(config.Enabled);
        Assert.Equal(500, config.DebounceMs);
    }

    [Fact]
    public void WorkspaceOpenedEvent_Properties()
    {
        var evt = new WorkspaceOpenedEvent("c:\\myproject");
        Assert.Equal("c:\\myproject", evt.RootPath);
    }

    [Fact]
    public async Task Output_LogsAutoBuildMessages()
    {
        var (svc, bus, _, output) = CreateEnabled(debounceMs: 10);
        using var _ = svc;

        bus.Publish(new DocumentSaved("file.nx"));

        await Task.Delay(200);

        Assert.Contains(output.Lines, l => l.Contains("Document saved") && l.Contains("file.nx"));
        Assert.Contains(output.Lines, l => l.Contains("Auto-build triggered"));
    }
}

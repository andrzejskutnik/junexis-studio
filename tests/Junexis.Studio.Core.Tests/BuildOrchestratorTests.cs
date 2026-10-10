using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;
using Xunit;

namespace Junexis.Studio.Core.Tests;

/// <summary>
/// Tests for <see cref="BuildOrchestrator"/>, <see cref="BuildRequestedEvent"/>,
/// <see cref="BuildStartedEvent"/> and <see cref="BuildFinishedEvent"/>.
/// </summary>
[Trait("Issue", "56")]
public class BuildOrchestratorTests
{
    // ── Fakes ───────────────────────────────────────────────────

    private sealed class FakeCompiler : ICompilerService
    {
        public string CompilerId { get; }
        public bool BuildSuccess { get; set; } = true;
        public bool ThrowOnBuild { get; set; }
        private readonly string[] _extensions;

        public FakeCompiler(string id, params string[] extensions)
        {
            CompilerId = id;
            _extensions = extensions;
        }

        public bool SupportsFile(string fileExtension)
            => _extensions.Contains(fileExtension, StringComparer.OrdinalIgnoreCase);

        public Task<CompilerResult> BuildAsync(BuildContext context)
        {
            if (ThrowOnBuild) throw new InvalidOperationException("build-crash");
            var diags = BuildSuccess
                ? Array.Empty<Diagnostic>()
                : new[] { new Diagnostic(DiagnosticSeverity.Error, "err") };
            return Task.FromResult(new CompilerResult(BuildSuccess, diags, CompilerId));
        }

        public Task<CompilerResult> CleanAsync(BuildContext context)
            => Task.FromResult(new CompilerResult(true, Array.Empty<Diagnostic>(), CompilerId));
        public IReadOnlyList<Diagnostic> GetDiagnostics() => Array.Empty<Diagnostic>();
    }

    private sealed class RecordingBus : IEventBus
    {
        public List<object> Events { get; } = new();
        public void Publish(object evt) => Events.Add(evt);
        public void Subscribe<T>(Action<T> handler) { }
        public void Unsubscribe<T>(Action<T> handler) { }
    }

    private sealed class CancellableCompiler : ICompilerService
    {
        public string CompilerId => "cancellable";
        public bool CancellationObserved { get; private set; }
        public bool SupportsFile(string fileExtension) => true;
        public Task<CompilerResult> BuildAsync(BuildContext context) =>
            Task.FromResult(new CompilerResult(true, [], CompilerId));
        public async Task<CompilerResult> BuildAsync(
            BuildContext context,
            CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return new CompilerResult(true, [], CompilerId);
            }
            catch (OperationCanceledException)
            {
                CancellationObserved = true;
                throw;
            }
        }
        public Task<CompilerResult> CleanAsync(BuildContext context) =>
            Task.FromResult(new CompilerResult(true, [], CompilerId));
        public IReadOnlyList<Diagnostic> GetDiagnostics() => [];
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

    private sealed class PreflightCompiler(List<string> phases) :
        ICompilerService,
        ICompilerPreflightService
    {
        public string CompilerId => "preflight";
        public bool PreflightSuccess { get; init; } = true;
        public bool SupportsFile(string fileExtension) => fileExtension == ".jnx";
        public Task<CompilerResult> BuildAsync(BuildContext context) =>
            throw new InvalidOperationException("Legacy build path must not be used.");
        public Task<CompilerResult> CleanAsync(BuildContext context) =>
            Task.FromResult(new CompilerResult(true, [], CompilerId));
        public IReadOnlyList<Diagnostic> GetDiagnostics() => [];

        public Task<CompilerPreflightResult> PreflightAsync(
            BuildContext context,
            CancellationToken cancellationToken = default)
        {
            phases.Add("compiler-preflight");
            var diagnostics = PreflightSuccess
                ? Array.Empty<Diagnostic>()
                : [new Diagnostic(DiagnosticSeverity.Error, "invalid system")];
            return Task.FromResult(new CompilerPreflightResult(
                PreflightSuccess, diagnostics, CompilerId, "build-1", "analysis.json"));
        }

        public Task<CompilerResult> BuildAfterPreflightAsync(
            BuildContext context,
            CompilerPreflightResult preflight,
            CancellationToken cancellationToken = default)
        {
            phases.Add("codegen");
            return Task.FromResult(new CompilerResult(
                true, [], CompilerId, preflight.BuildId,
                AnalysisSnapshotPath: preflight.AnalysisSnapshotPath));
        }
    }

    private sealed class RecordingMentorPreflight(List<string> phases) : IMentorBuildPreflightService
    {
        public Task<MentorBuildPreflightOutcome> RunAsync(
            CompilerPreflightResult preflight,
            CancellationToken cancellationToken = default)
        {
            phases.Add("mentor");
            return Task.FromResult(new MentorBuildPreflightOutcome(
                false, null, "Mentor unavailable; build continues."));
        }
    }

    private static BuildContext Ctx(string? doc = null) => new("c:\\work", doc);

    // ── Tests ───────────────────────────────────────────────────

    [Fact]
    public async Task BuildAsync_FullLifecycle_Success()
    {
        var bus = new RecordingBus();
        var output = new RecordingOutput();
        var sel = new CompilerSelector(output);
        sel.Register(new FakeCompiler("gcc", ".c", ".cpp"));
        var orch = new BuildOrchestrator(sel, bus, output);

        var result = await orch.BuildAsync(Ctx("main.cpp"));

        Assert.NotNull(result);
        Assert.True(result!.Success);
        Assert.Equal("gcc", result.CompilerId);

        Assert.Contains(bus.Events, e => e is BuildRequestedEvent);
        Assert.Contains(bus.Events, e => e is BuildStartedEvent s && s.CompilerId == "gcc");
        Assert.Contains(bus.Events, e => e is BuildFinishedEvent f && f.Success);
    }

    [Fact]
    public async Task BuildAsync_FullLifecycle_Failure()
    {
        var bus = new RecordingBus();
        var sel = new CompilerSelector();
        var fc = new FakeCompiler("gcc", ".c") { BuildSuccess = false };
        sel.Register(fc);
        var orch = new BuildOrchestrator(sel, bus);

        var result = await orch.BuildAsync(Ctx("main.c"));

        Assert.NotNull(result);
        Assert.False(result!.Success);
        Assert.Contains(bus.Events, e => e is BuildFinishedEvent f && !f.Success && f.DiagnosticCount == 1);
    }

    [Fact]
    public async Task BuildAsync_NoCompiler_ReturnsNull_PublishesFinishedWithError()
    {
        var bus = new RecordingBus();
        var output = new RecordingOutput();
        var sel = new CompilerSelector(output);
        var orch = new BuildOrchestrator(sel, bus, output);

        var result = await orch.BuildAsync(Ctx("main.xyz"));

        Assert.Null(result);
        Assert.Contains(bus.Events, e => e is BuildFinishedEvent f && !f.Success && f.Error != null);
        Assert.Contains(output.Lines, l => l.Contains("No compiler"));
    }

    [Fact]
    public async Task BuildAsync_CompilerThrows_CatchesAndPublishesError()
    {
        var bus = new RecordingBus();
        var output = new RecordingOutput();
        var sel = new CompilerSelector(output);
        sel.Register(new FakeCompiler("crash", ".c") { ThrowOnBuild = true });
        var orch = new BuildOrchestrator(sel, bus, output);

        var result = await orch.BuildAsync(Ctx("main.c"));

        Assert.NotNull(result);
        Assert.False(result!.Success);
        Assert.Contains(bus.Events, e => e is BuildFinishedEvent f && !f.Success && f.Error!.Contains("build-crash"));
        Assert.Contains(output.Lines, l => l.Contains("exception"));
    }

    [Fact]
    public async Task BuildAsync_ExplicitCompilerOverride()
    {
        var bus = new RecordingBus();
        var sel = new CompilerSelector();
        sel.Register(new FakeCompiler("gcc", ".c"));
        sel.Register(new FakeCompiler("clang", ".c"));
        var orch = new BuildOrchestrator(sel, bus);

        var result = await orch.BuildAsync(Ctx("main.c"), requestedCompilerId: "clang");

        Assert.NotNull(result);
        Assert.Equal("clang", result!.CompilerId);
    }

    [Fact]
    public async Task BuildAsync_OutputLogsAllPhases()
    {
        var output = new RecordingOutput();
        var sel = new CompilerSelector(output);
        sel.Register(new FakeCompiler("gcc", ".c"));
        var orch = new BuildOrchestrator(sel, output: output);

        await orch.BuildAsync(Ctx("test.c"));

        Assert.Contains(output.Lines, l => l.Contains("Build requested"));
        Assert.Contains(output.Lines, l => l.Contains("Build started"));
        Assert.Contains(output.Lines, l => l.Contains("succeeded"));
    }

    [Fact]
    public async Task BuildAsync_NullDocumentPath_UsesEmptyExtension()
    {
        var bus = new RecordingBus();
        var sel = new CompilerSelector();
        sel.Register(new FakeCompiler("default", ".c")); // won't match empty ext, but fallback to default
        var orch = new BuildOrchestrator(sel, bus);

        var result = await orch.BuildAsync(Ctx(null));

        // default compiler as fallback (first registered)
        Assert.NotNull(result);
        Assert.Equal("default", result!.CompilerId);
    }

    [Fact]
    public async Task BuildAsync_ForwardsCancellationToCompiler()
    {
        var compiler = new CancellableCompiler();
        var selector = new CompilerSelector();
        selector.Register(compiler);
        var orchestrator = new BuildOrchestrator(selector);
        using var cancellation = new CancellationTokenSource();
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(50));

        var result = await orchestrator.BuildAsync(Ctx("main.jnx"), cancellationToken: cancellation.Token);

        Assert.True(compiler.CancellationObserved);
        Assert.NotNull(result);
        Assert.False(result!.Success);
    }

    [Fact]
    public async Task BuildAsync_PreflightAndMentorRunBeforeCodeGeneration()
    {
        var phases = new List<string>();
        var selector = new CompilerSelector();
        selector.Register(new PreflightCompiler(phases));
        var orchestrator = new BuildOrchestrator(
            selector,
            mentorPreflight: new RecordingMentorPreflight(phases));

        var result = await orchestrator.BuildAsync(Ctx("main.jnx"));

        Assert.True(result!.Success);
        Assert.Equal(["compiler-preflight", "mentor", "codegen"], phases);
        Assert.Equal("build-1", result.BuildId);
    }

    [Fact]
    public async Task BuildAsync_FailedCompilerPreflight_DoesNotGenerateCode()
    {
        var phases = new List<string>();
        var selector = new CompilerSelector();
        selector.Register(new PreflightCompiler(phases) { PreflightSuccess = false });
        var orchestrator = new BuildOrchestrator(
            selector,
            mentorPreflight: new RecordingMentorPreflight(phases));

        var result = await orchestrator.BuildAsync(Ctx("main.jnx"));

        Assert.False(result!.Success);
        Assert.Equal(["compiler-preflight"], phases);
    }

    [Fact]
    public void BuildRequestedEvent_Properties()
    {
        var ctx = Ctx("x.c");
        var evt = new BuildRequestedEvent(ctx, "gcc");
        Assert.Equal(ctx, evt.Context);
        Assert.Equal("gcc", evt.RequestedCompilerId);
    }

    [Fact]
    public void BuildStartedEvent_Properties()
    {
        var ctx = Ctx("x.c");
        var now = DateTime.UtcNow;
        var evt = new BuildStartedEvent(ctx, "gcc", now);
        Assert.Equal("gcc", evt.CompilerId);
        Assert.Equal(now, evt.StartedAtUtc);
    }

    [Fact]
    public void BuildFinishedEvent_Properties()
    {
        var evt = new BuildFinishedEvent(Ctx(), "gcc", true, 2, 500, null);
        Assert.True(evt.Success);
        Assert.Equal(2, evt.DiagnosticCount);
        Assert.Equal(500, evt.DurationMs);
        Assert.Null(evt.Error);
    }

    [Fact]
    public void Constructor_ThrowsOnNullSelector()
    {
        Assert.Throws<ArgumentNullException>(() => new BuildOrchestrator(null!));
    }
}

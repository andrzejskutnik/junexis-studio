using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;
using Junexis.Studio.Host;
using Xunit;

namespace Junexis.Studio.Host.Tests;

/// <summary>
/// Tests for <see cref="IsolatedExtensionLoader"/> and <see cref="ExtensionLoadContext"/>.
/// </summary>
[Trait("Issue", "87")]
public class IsolatedExtensionLoaderTests
{
    // ── Helpers ──────────────────────────────────────────────────

    private static HostContext CreateContext(OutputService? output = null) =>
        new(
            eventBus: new EventBus(),
            commands: new CommandRegistry(),
            panels: new PanelRegistry(),
            output: output ?? new OutputService(),
            processes: new TestProcessService(),
            workspace: new WorkspaceService(),
            diagnostics: new DiagnosticService(),
            activeDocument: new ActiveDocumentService(),
            openDocuments: new TestOpenDocumentRepository(),
            versionControl: null,
            views: new TestViewRegistry());

    private sealed class FakeExtension : IJunexisExtension
    {
        private readonly Action<IHostContext>? _onInit;
        private readonly Action? _onShutdown;

        public FakeExtension(string id,
            Action<IHostContext>? onInit = null,
            Action? onShutdown = null)
        {
            Id = id;
            _onInit = onInit;
            _onShutdown = onShutdown;
        }

        public string Id { get; }
        public bool Initialized { get; private set; }
        public bool ShutDown { get; private set; }

        public void Initialize(IHostContext context)
        {
            _onInit?.Invoke(context);
            Initialized = true;
        }

        public void Shutdown()
        {
            _onShutdown?.Invoke();
            ShutDown = true;
        }
    }

    // ── Register ────────────────────────────────────────────────

    [Fact]
    public void Register_AddsToExtensions()
    {
        var sut = new IsolatedExtensionLoader();
        var ext = new FakeExtension("test-ext");
        var isolated = sut.Register(ext);

        Assert.Single(sut.Extensions);
        Assert.Same(ext, isolated.Extension);
        Assert.NotNull(isolated.Context);
        Assert.False(isolated.IsActive);
    }

    [Fact]
    public void Register_Null_Throws()
    {
        var sut = new IsolatedExtensionLoader();
        Assert.Throws<ArgumentNullException>(() => sut.Register(null!));
    }

    [Fact]
    public void Register_LogsToOutput()
    {
        var output = new OutputService();
        var sut = new IsolatedExtensionLoader(output);
        sut.Register(new FakeExtension("my-ext"));

        var snapshot = output.GetSnapshot();
        Assert.Contains(snapshot, e =>
            e.Channel == IsolatedExtensionLoader.OutputChannel &&
            e.Message.Contains("my-ext"));
    }

    // ── Initialize ──────────────────────────────────────────────

    [Fact]
    public void Initialize_SetsIsActive()
    {
        var sut = new IsolatedExtensionLoader();
        var ext = new FakeExtension("ext1");
        var isolated = sut.Register(ext);
        var ctx = CreateContext();

        sut.Initialize(isolated, ctx);

        Assert.True(isolated.IsActive);
        Assert.True(ext.Initialized);
    }

    [Fact]
    public void Initialize_ExtensionThrows_StaysInactive()
    {
        var output = new OutputService();
        var sut = new IsolatedExtensionLoader(output);
        var ext = new FakeExtension("ext-bad", onInit: _ => throw new InvalidOperationException("boom"));
        var isolated = sut.Register(ext);

        sut.Initialize(isolated, CreateContext(output));

        Assert.False(isolated.IsActive);
        Assert.Contains(output.GetSnapshot(), e => e.Message.Contains("boom"));
    }

    [Fact]
    public void Initialize_Null_Throws()
    {
        var sut = new IsolatedExtensionLoader();
        Assert.Throws<ArgumentNullException>(() => sut.Initialize(null!, CreateContext()));
        var isolated = sut.Register(new FakeExtension("x"));
        Assert.Throws<ArgumentNullException>(() => sut.Initialize(isolated, null!));
    }

    // ── InitializeAll ───────────────────────────────────────────

    [Fact]
    public void InitializeAll_InitializesAllExtensions()
    {
        var sut = new IsolatedExtensionLoader();
        var e1 = new FakeExtension("e1");
        var e2 = new FakeExtension("e2");
        sut.Register(e1);
        sut.Register(e2);

        sut.InitializeAll(CreateContext());

        Assert.True(sut.Extensions.All(e => e.IsActive));
    }

    // ── Unload ──────────────────────────────────────────────────

    [Fact]
    public void Unload_RemovesExtension()
    {
        var sut = new IsolatedExtensionLoader();
        var ext = new FakeExtension("to-unload");
        sut.Register(ext);
        sut.InitializeAll(CreateContext());

        var result = sut.Unload("to-unload");

        Assert.True(result);
        Assert.Empty(sut.Extensions);
        Assert.True(ext.ShutDown);
    }

    [Fact]
    public void Unload_NotFound_ReturnsFalse()
    {
        var sut = new IsolatedExtensionLoader();
        Assert.False(sut.Unload("nonexistent"));
    }

    [Fact]
    public void Unload_CaseInsensitive()
    {
        var sut = new IsolatedExtensionLoader();
        sut.Register(new FakeExtension("MyExt"));
        Assert.True(sut.Unload("myext"));
        Assert.Empty(sut.Extensions);
    }

    [Fact]
    public void Unload_NotActive_SkipsShutdown()
    {
        var sut = new IsolatedExtensionLoader();
        var ext = new FakeExtension("ext1");
        sut.Register(ext);

        // No InitializeAll, so not active
        sut.Unload("ext1");

        Assert.False(ext.ShutDown);
    }

    [Fact]
    public void Unload_ShutdownThrows_StillRemoves()
    {
        var output = new OutputService();
        var sut = new IsolatedExtensionLoader(output);
        var ext = new FakeExtension("oops", onShutdown: () => throw new InvalidOperationException("shutdown-fail"));
        sut.Register(ext);
        sut.InitializeAll(CreateContext(output));

        var result = sut.Unload("oops");

        Assert.True(result);
        Assert.Empty(sut.Extensions);
        Assert.Contains(output.GetSnapshot(), e => e.Message.Contains("shutdown-fail"));
    }

    [Fact]
    public void Unload_LogsToOutput()
    {
        var output = new OutputService();
        var sut = new IsolatedExtensionLoader(output);
        sut.Register(new FakeExtension("ext1"));
        sut.Unload("ext1");

        Assert.Contains(output.GetSnapshot(), e => e.Message.Contains("ALC unloaded"));
    }

    // ── UnloadAll ───────────────────────────────────────────────

    [Fact]
    public void UnloadAll_UnloadsAllExtensions()
    {
        var sut = new IsolatedExtensionLoader();
        sut.Register(new FakeExtension("e1"));
        sut.Register(new FakeExtension("e2"));
        sut.InitializeAll(CreateContext());

        sut.UnloadAll();

        Assert.Empty(sut.Extensions);
    }

    // ── LoadFromAssembly: invalid path ──────────────────────────

    [Fact]
    public void LoadFromAssembly_EmptyPath_Throws()
    {
        var sut = new IsolatedExtensionLoader();
        Assert.Throws<ArgumentException>(() => sut.LoadFromAssembly(""));
        Assert.Throws<ArgumentException>(() => sut.LoadFromAssembly(null!));
    }

    [Fact]
    public void LoadFromAssembly_NonExistentFile_LogsError()
    {
        var output = new OutputService();
        var sut = new IsolatedExtensionLoader(output);
        var result = sut.LoadFromAssembly(@"C:\nonexistent\fake.dll");

        Assert.Empty(result);
        Assert.Contains(output.GetSnapshot(), e =>
            e.Severity == OutputSeverity.Error && e.Message.Contains("fake.dll"));
    }

    // ── ExtensionLoadContext ────────────────────────────────────

    [Fact]
    public void ExtensionLoadContext_IsCollectible()
    {
        var alc = new ExtensionLoadContext("test");
        Assert.True(alc.IsCollectible);
        alc.Unload();
    }

    [Fact]
    public void ExtensionLoadContext_Name_IsSet()
    {
        var alc = new ExtensionLoadContext("my-extension");
        Assert.Equal("my-extension", alc.Name);
        alc.Unload();
    }

    // ── IsolatedExtension record ────────────────────────────────

    [Fact]
    public void IsolatedExtension_Ctor_NullExtension_Throws()
    {
        var alc = new ExtensionLoadContext("t");
        Assert.Throws<ArgumentNullException>(() => new IsolatedExtension(null!, alc));
        alc.Unload();
    }

    [Fact]
    public void IsolatedExtension_Ctor_NullContext_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new IsolatedExtension(new FakeExtension("x"), null!));
    }

    // ── OutputChannel constant ──────────────────────────────────

    [Fact]
    public void OutputChannel_IsHost()
    {
        Assert.Equal("Host", IsolatedExtensionLoader.OutputChannel);
    }
}

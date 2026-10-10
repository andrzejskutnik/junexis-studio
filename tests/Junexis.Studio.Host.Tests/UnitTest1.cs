using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;
using Junexis.Studio.Host;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Junexis.Studio.Host.Tests;

public sealed class HostContextTests
{
    [Fact]
    [Trait("Issue", "2")]
    public void Constructor_WithNullEventBus_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new HostContext(
            eventBus: null!,
            commands: new CommandRegistry(),
            panels: new PanelRegistry(),
            output: new OutputService(),
            processes: new TestProcessService(),
            workspace: new WorkspaceService(),
            diagnostics: new DiagnosticService(),
            activeDocument: new ActiveDocumentService(),
            openDocuments: new TestOpenDocumentRepository(),
            versionControl: null,
            views: new TestViewRegistry()));
    }
}

internal sealed class TestProcessService : IProcessService
{
    public Task<ProcessResult> RunAsync(
        ProcessRequest request,
        Action<ProcessOutputLine>? listener,
        CancellationToken cancellationToken)
        => throw new NotSupportedException("Test stub.");
}

internal sealed class TestViewRegistry : IViewRegistry
{
    public void Register<TViewModel, TView>()
        where TViewModel : class
        where TView : class, new()
    {
        // no-op
    }

    public object? Resolve(object viewModel) => null;
}

public sealed class ExtensionLoaderTests
{
    [Fact]
    [Trait("Issue", "4")]
    public void Add_WithNullExtension_Throws()
    {
        var sut = new ExtensionLoader();
        Assert.Throws<ArgumentNullException>(() => sut.Add(null!));
    }

    [Fact]
    [Trait("Issue", "4")]
    public void InitializeAll_WhenExtensionThrows_ContinuesAndLogsToOutput()
    {
        var output = new OutputService();
        var context = new HostContext(
            eventBus: new EventBus(),
            commands: new CommandRegistry(),
            panels: new PanelRegistry(),
            output: output,
            processes: new TestProcessService(),
            workspace: new WorkspaceService(),
            diagnostics: new DiagnosticService(),
            activeDocument: new ActiveDocumentService(),
            openDocuments: new TestOpenDocumentRepository(),
            versionControl: null,
            views: new TestViewRegistry());

        var loader = new ExtensionLoader();

        loader.Add(new TestExtension(
            id: "ext.throw",
            onInit: _ => throw new InvalidOperationException("boom"),
            onShutdown: () => { }));

        var okInitCalls = 0;
        loader.Add(new TestExtension(
            id: "ext.ok",
            onInit: _ => okInitCalls++,
            onShutdown: () => { }));

        loader.InitializeAll(context);

        Assert.Equal(1, okInitCalls);

        var snapshot = output.GetSnapshot();
        Assert.Contains(snapshot, e => e.Channel == "Extensions" && e.Message.Contains("failed to initialize"));
    }

    [Fact]
    [Trait("Issue", "68")]
    public void InitializeAll_WhenExtensionThrows_PublishesSystemDiagnosticOnEventBus()
    {
        var eventBus = new EventBus();
        SystemDiagnostic? captured = null;
        eventBus.Subscribe<SystemDiagnostic>(d => captured = d);

        var context = new HostContext(
            eventBus: eventBus,
            commands: new CommandRegistry(),
            panels: new PanelRegistry(),
            output: new OutputService(),
            processes: new TestProcessService(),
            workspace: new WorkspaceService(),
            diagnostics: new DiagnosticService(),
            activeDocument: new ActiveDocumentService(),
            openDocuments: new TestOpenDocumentRepository(),
            versionControl: null,
            views: new TestViewRegistry());

        var loader = new ExtensionLoader();
        loader.Add(new TestExtension("ext.bad", _ => throw new InvalidOperationException("init-fail"), () => { }));

        loader.InitializeAll(context);

        Assert.NotNull(captured);
        Assert.Equal(SystemDiagnosticCategory.ExtensionLoad, captured!.Category);
        Assert.Equal("ext.bad", captured.Source);
        Assert.Contains("init-fail", captured.Message);
        Assert.NotNull(captured.ExceptionText);
    }

    [Fact]
    [Trait("Issue", "68")]
    public void InitializeAll_WhenExtensionThrows_AddsDiagnosticToService()
    {
        var diagnostics = new DiagnosticService();
        var context = new HostContext(
            eventBus: new EventBus(),
            commands: new CommandRegistry(),
            panels: new PanelRegistry(),
            output: new OutputService(),
            processes: new TestProcessService(),
            workspace: new WorkspaceService(),
            diagnostics: diagnostics,
            activeDocument: new ActiveDocumentService(),
            openDocuments: new TestOpenDocumentRepository(),
            versionControl: null,
            views: new TestViewRegistry());

        var loader = new ExtensionLoader();
        loader.Add(new TestExtension("ext.bad", _ => throw new InvalidOperationException("init-fail"), () => { }));

        loader.InitializeAll(context);

        var diags = diagnostics.GetDiagnostics();
        Assert.Single(diags);
        Assert.Equal(DiagnosticSeverity.Error, diags[0].Severity);
        Assert.Contains("init-fail", diags[0].Message);
    }

    [Fact]
    [Trait("Issue", "68")]
    public void ShutdownAll_WhenExtensionThrows_PublishesSystemDiagnosticOnEventBus()
    {
        var eventBus = new EventBus();
        SystemDiagnostic? captured = null;
        eventBus.Subscribe<SystemDiagnostic>(d => captured = d);

        var context = new HostContext(
            eventBus: eventBus,
            commands: new CommandRegistry(),
            panels: new PanelRegistry(),
            output: new OutputService(),
            processes: new TestProcessService(),
            workspace: new WorkspaceService(),
            diagnostics: new DiagnosticService(),
            activeDocument: new ActiveDocumentService(),
            openDocuments: new TestOpenDocumentRepository(),
            versionControl: null,
            views: new TestViewRegistry());

        var loader = new ExtensionLoader();
        loader.Add(new TestExtension("ext.bad", _ => { }, () => throw new InvalidOperationException("shutdown-fail")));

        loader.InitializeAll(context);

        loader.ShutdownAll(context);

        Assert.NotNull(captured);
        Assert.Equal(SystemDiagnosticCategory.ExtensionException, captured!.Category);
        Assert.Equal("ext.bad", captured.Source);
        Assert.Contains("shutdown-fail", captured.Message);
    }

    private sealed class TestExtension : IJunexisExtension
    {
        private readonly Action<IHostContext> _onInit;
        private readonly Action _onShutdown;

        public TestExtension(string id, Action<IHostContext> onInit, Action onShutdown)
        {
            Id = id;
            _onInit = onInit;
            _onShutdown = onShutdown;
        }

        public string Id { get; }

        public void Initialize(IHostContext context) => _onInit(context);

        public void Shutdown() => _onShutdown();
    }
}

internal sealed class TestOpenDocumentRepository : IOpenDocumentRepository
{
    public IReadOnlyList<Document> GetOpenDocuments() => Array.Empty<Document>();

    public bool TryGetByPath(string path, out Document document)
    {
        document = default!;
        return false;
    }
}

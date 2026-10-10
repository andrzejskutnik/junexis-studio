using System.Text.Json;
using JunexisLang.Studio.CompilerExtension;
using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;
using Junexis.Studio.Host;

namespace Junexis.Studio.Host.Tests;

public sealed class LatestBuildIntegrationTests
{
    [Fact]
    [Trait("Issue", "31.2")]
    public async Task OpeningProject_RestoresLatestBuildAndPublishesArtifactAndAnalysisEvents()
    {
        var root = await CreateProjectAsync();
        var eventBus = new EventBus();
        using var workspace = new WorkspaceService(eventBus);
        using var openDocuments = new OpenDocumentRepository(eventBus, workspace);
        var capabilities = new CapabilityRegistry();
        capabilities.Register<ILatestBuildReceiptService>(new LatestBuildReceiptService());
        var context = new HostContext(
            eventBus,
            new CommandRegistry(),
            new PanelRegistry(),
            new OutputService(),
            new NoOpProcessService(),
            workspace,
            new DiagnosticService(),
            new ActiveDocumentService(),
            openDocuments,
            versionControl: null,
            views: new NullViewRegistry(),
            capabilities: capabilities);
        var extension = new JunexisLangCompilerExtension();
        LatestBuildUpdatedEvent? latest = null;
        var published = new TaskCompletionSource<AnalysisSnapshotUpdatedEvent>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        ArtifactsUpdatedEvent? artifacts = null;
        context.EventBus.Subscribe<LatestBuildUpdatedEvent>(value => latest = value);
        context.EventBus.Subscribe<ArtifactsUpdatedEvent>(value => artifacts = value);
        context.EventBus.Subscribe<AnalysisSnapshotUpdatedEvent>(value => published.TrySetResult(value));

        try
        {
            extension.Initialize(context);
            context.Workspace.OpenFolder(root);

            var analysis = await published.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.NotNull(latest);
            Assert.True(latest!.Restored);
            Assert.Equal(new string('b', 64), latest.Receipt.BuildId);
            Assert.Equal(latest.Receipt.BuildId, artifacts?.BuildId);
            Assert.Equal(latest.Receipt.ArtifactManifestPath, artifacts?.ArtifactManifestPath);
            Assert.Equal(latest.Receipt.BuildId, analysis.BuildId);
            Assert.Equal(latest.Receipt.AnalysisSnapshotPath, analysis.SnapshotPath);
        }
        finally
        {
            extension.Shutdown();
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    [Trait("Issue", "31.2")]
    public void Host_RegistersLatestBuildReceiptCapability()
    {
        var host = new JunexisStudioHost();

        Assert.True(host.Context.Capabilities.TryGet<ILatestBuildReceiptService>(out var service));
        Assert.NotNull(service);
    }

    [Fact]
    [Trait("Issue", "M7.5")]
    public void DeployCenterExtension_ReactsToLatestBuildUpdatedEvent()
    {
        var eventBus = new EventBus();
        using var workspace = new WorkspaceService(eventBus);
        using var openDocuments = new OpenDocumentRepository(eventBus, workspace);
        var capabilities = new CapabilityRegistry();
        capabilities.Register<ILatestBuildReceiptService>(new LatestBuildReceiptService());
        capabilities.Register<IDeploymentService>(new StubDeploymentService());
        var panels = new PanelRegistry();
        var context = new HostContext(
            eventBus,
            new CommandRegistry(),
            panels,
            new OutputService(),
            new NoOpProcessService(),
            workspace,
            new DiagnosticService(),
            new ActiveDocumentService(),
            openDocuments,
            versionControl: null,
            views: new NullViewRegistry(),
            capabilities: capabilities);
        var extension = new DeployCenterExtension();
        var receipt = CreateReceiptFixture();

        try
        {
            extension.Initialize(context);

            var registration = panels.GetRegisteredPanels()
                .Single(panel => panel.Id == "junexis.deploy.center");
            var viewModel = Assert.IsType<DeployCenterViewModel>(registration.Factory());

            eventBus.Publish(new LatestBuildUpdatedEvent("junexis.compiler.jnx", receipt, false));

            Assert.Contains(receipt.BuildId[..12], viewModel.BuildText, StringComparison.Ordinal);
            Assert.Contains("local: report, server", viewModel.TargetsText, StringComparison.Ordinal);
            Assert.Contains("Ready to package and preflight", viewModel.StatusText, StringComparison.Ordinal);
            Assert.True(viewModel.CanPrepare);
        }
        finally
        {
            extension.Shutdown();
            if (Directory.Exists(receipt.ProjectRoot))
                Directory.Delete(receipt.ProjectRoot, recursive: true);
        }
    }

    private static async Task<string> CreateProjectAsync()
    {
        var root = Path.GetFullPath(Path.Combine(
            Path.GetTempPath(),
            $"junexis-studio-latest-integration-{Guid.NewGuid():N}"));
        var entry = Path.Combine(root, "src", "main.jnx");
        var output = Path.Combine(root, "out");
        var bundle = Path.Combine(output, "junexis.bundle.json");
        var artifact = Path.Combine(output, "junexis.artifacts.json");
        var debug = Path.Combine(output, "debug", "junexis.debug.toml");
        var analysis = Path.Combine(output, "analysis", "junexis.analysis.json");
        Directory.CreateDirectory(Path.GetDirectoryName(entry)!);
        Directory.CreateDirectory(Path.GetDirectoryName(debug)!);
        Directory.CreateDirectory(Path.GetDirectoryName(analysis)!);
        await File.WriteAllTextAsync(entry, "task Main() @server {}");
        await File.WriteAllTextAsync(
            Path.Combine(root, "junexis.project.toml"),
            "entry = \"src/main.jnx\"");
        var buildId = new string('b', 64);
        var identityJson = JsonSerializer.Serialize(new { buildId });
        foreach (var path in new[] { bundle, artifact, analysis })
            await File.WriteAllTextAsync(path, identityJson);
        await File.WriteAllTextAsync(debug, "{}");

        var receiptPath = Path.Combine(root, ".junexis", "state", "latest-build.json");
        Directory.CreateDirectory(Path.GetDirectoryName(receiptPath)!);
        await File.WriteAllTextAsync(receiptPath, JsonSerializer.Serialize(new
        {
            schemaVersion = "1.0",
            buildId,
            mode = "project",
            projectRoot = root,
            entryFile = entry,
            outputRoot = output,
            targets = new[] { "server" },
            bundleManifest = bundle,
            artifactManifest = artifact,
            debugManifest = debug,
            analysisSnapshot = analysis,
        }));
        return root;
    }

    private static LatestBuildReceipt CreateReceiptFixture()
    {
        var root = Path.GetFullPath(Path.Combine(
            Path.GetTempPath(),
            $"junexis-studio-deploy-integration-{Guid.NewGuid():N}"));
        var entry = Path.Combine(root, "src", "main.jnx");
        var output = Path.Combine(root, "out");
        var bundle = Path.Combine(output, "junexis.bundle.json");
        var artifact = Path.Combine(output, "junexis.artifacts.json");
        var debug = Path.Combine(output, "debug", "junexis.debug.toml");
        var analysis = Path.Combine(output, "analysis", "junexis.analysis.json");
        Directory.CreateDirectory(Path.GetDirectoryName(entry)!);
        Directory.CreateDirectory(Path.GetDirectoryName(debug)!);
        Directory.CreateDirectory(Path.GetDirectoryName(analysis)!);
        File.WriteAllText(Path.Combine(root, "junexis.project.toml"), "entry = \"src/main.jnx\"");
        File.WriteAllText(entry, "task Main() @server {}\n");
        var buildId = new string('d', 64);
        var identityJson = JsonSerializer.Serialize(new { buildId });
        foreach (var path in new[] { bundle, artifact, analysis })
            File.WriteAllText(path, identityJson);
        File.WriteAllText(debug, "{}");

        return new LatestBuildReceipt(
            Path.Combine(root, ".junexis", "state", "latest-build.json"),
            buildId,
            "project",
            root,
            entry,
            output,
            ["report", "server"],
            bundle,
            artifact,
            debug,
            analysis);
    }

    private sealed class NoOpProcessService : IProcessService
    {
        public Task<ProcessResult> RunAsync(
            ProcessRequest request,
            Action<ProcessOutputLine>? listener,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ProcessResult(0, TimeSpan.Zero));
    }

    private sealed class NullViewRegistry : IViewRegistry
    {
        public void Register<TViewModel, TView>()
            where TViewModel : class
            where TView : class, new()
        {
        }

        public object? Resolve(object viewModel) => null;
    }

    private sealed class StubDeploymentService : IDeploymentService
    {
        public DeploymentReadiness Evaluate(LatestBuildReceipt? receipt)
        {
            if (receipt is null)
                return new DeploymentReadiness(false, false, "missing", null, null, null, [], [], []);
            return new DeploymentReadiness(
                true,
                true,
                $"Ready to package and preflight report, server for build {receipt.BuildId[..12]}.",
                receipt.BuildId,
                receipt.ProjectRoot,
                receipt.ReceiptPath,
                ["report", "server"],
                ["report", "server"],
                []);
        }

        public Task<DeploymentPreparationResult> PrepareAsync(
            LatestBuildReceipt receipt,
            Action<ProcessOutputLine>? listener = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<LocalDeploymentResult> RunLocalAsync(
            DeploymentPreparationResult preparation,
            int durationSeconds,
            Action<ProcessOutputLine>? listener = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<StoredLocalDeployment?> TryLoadLastLocalAsync(
            LatestBuildReceipt receipt,
            CancellationToken cancellationToken = default) => Task.FromResult<StoredLocalDeployment?>(null);

        public Task<LiveLocalDeployment?> TryLoadLiveLocalAsync(
            LatestBuildReceipt receipt,
            CancellationToken cancellationToken = default) => Task.FromResult<LiveLocalDeployment?>(null);

        public Task<LiveLocalDeployment> AdoptLiveLocalAsync(
            LatestBuildReceipt receipt,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<LiveLocalDeployment> StopLiveLocalAsync(
            LatestBuildReceipt receipt,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<DeploymentPreparationResult?> TryRestorePreparationAsync(
            LatestBuildReceipt receipt,
            CancellationToken cancellationToken = default) => Task.FromResult<DeploymentPreparationResult?>(null);

        public Task<EmbeddedProbeResult> ProbeEmbeddedAsync(
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<EmbeddedDeploymentPreparation> PrepareEmbeddedAsync(
            LatestBuildReceipt receipt,
            Action<ProcessOutputLine>? listener = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<EmbeddedDeviceSelection> SelectEmbeddedAsync(
            LatestBuildReceipt receipt,
            EmbeddedDeviceCandidate device,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<EmbeddedFlashResult> FlashEmbeddedAsync(
            EmbeddedDeploymentPreparation preparation,
            EmbeddedDeviceSelection selection,
            Action<ProcessOutputLine>? listener = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}

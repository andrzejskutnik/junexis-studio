using System.Text.Json;
using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;
using Junexis.Studio.Host;

namespace Junexis.Studio.Host.Tests;

public sealed class DeployCenterViewModelTests
{
    [Fact]
    public void ApplyLatestBuild_ShowsDeployableAndBlockedTargets()
    {
        using var fixture = new ReceiptFixture();
        var viewModel = CreateViewModel(new StubDeploymentService());

        viewModel.ApplyLatestBuild(fixture.Receipt with { Targets = ["server", "embedded"] });

        Assert.Contains("local: server", viewModel.TargetsText, StringComparison.Ordinal);
        Assert.Contains("separate flow: embedded", viewModel.TargetsText, StringComparison.Ordinal);
        Assert.True(viewModel.CanPrepare);
    }

    [Fact]
    public async Task PrepareAndRunLocalAsync_UpdatePanelStatus()
    {
        using var fixture = new ReceiptFixture();
        var deployment = new StubDeploymentService();
        var viewModel = CreateViewModel(deployment);
        viewModel.ApplyLatestBuild(fixture.Receipt);

        await viewModel.PrepareAsync();
        Assert.Contains("Prepared bundle", viewModel.StatusText, StringComparison.Ordinal);
        Assert.Contains("Preflight ready", viewModel.PreflightText, StringComparison.Ordinal);

        await viewModel.RunLocalAsync();

        Assert.Contains("completed successfully", viewModel.StatusText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Local deployment succeeded", viewModel.LocalDeploymentText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("exit 0", viewModel.LocalDeploymentText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("5s", viewModel.LocalDeploymentText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("deployment-receipt.json", viewModel.LocalDeploymentText, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Current Studio session", viewModel.RecoveryModeTitle);
        Assert.Contains("started from this Studio session", viewModel.RecoveryText, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, viewModel.LocalTargetStates.Count);
        Assert.Contains(viewModel.LocalTargetStates, target => target.Target == "server" && target.Status == "ready");
        Assert.NotEmpty(viewModel.Checks);
        Assert.Contains("[prepare]", viewModel.LogText, StringComparison.Ordinal);
        Assert.Contains("[local]", viewModel.LogText, StringComparison.Ordinal);
        Assert.Equal(1, deployment.PrepareCalls);
        Assert.Equal(1, deployment.LocalCalls);
    }

    [Fact]
    [Trait("Issue", "M7.9")]
    public async Task PrepareAsync_ExposesExplicitBundleAuthenticityPolicy()
    {
        using var fixture = new ReceiptFixture();
        var deployment = new StubDeploymentService();
        var viewModel = CreateViewModel(deployment);
        viewModel.ApplyLatestBuild(fixture.Receipt);
        viewModel.SelectedAuthenticityPolicy = "trusted";
        viewModel.SigningKeyPath = Path.Combine(fixture.Root, "release-key.pem");
        viewModel.SignerName = "Release signer";
        viewModel.TrustedSignerFingerprintsText = new string('a', 64);

        await viewModel.PrepareAsync();

        Assert.NotNull(deployment.LastAuthenticity);
        Assert.Equal("trusted", deployment.LastAuthenticity.Policy);
        Assert.Equal("Release signer", deployment.LastAuthenticity.Signer);
        Assert.Single(deployment.LastAuthenticity.TrustedSignerFingerprints);
        Assert.Contains("Authenticity trusted", viewModel.AuthenticityText, StringComparison.Ordinal);
        Assert.Contains("accepted", viewModel.AuthenticityText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EmbeddedFlow_RequiresExplicitSelectionAndFlashConfirmation()
    {
        using var fixture = new ReceiptFixture();
        var deployment = new StubDeploymentService();
        var viewModel = CreateViewModel(deployment);
        viewModel.ApplyLatestBuild(fixture.Receipt with { Targets = ["embedded"] });

        Assert.True(viewModel.CanProbeEmbedded);
        Assert.True(viewModel.CanPrepareEmbedded);
        Assert.False(viewModel.CanSelectEmbedded);
        Assert.False(viewModel.CanFlashEmbedded);

        await viewModel.ProbeEmbeddedAsync();

        var device = Assert.Single(viewModel.EmbeddedDevices);
        Assert.Equal("COM7", device.Port);
        Assert.Null(viewModel.SelectedEmbeddedDevice);
        Assert.False(viewModel.CanSelectEmbedded);

        viewModel.SelectedEmbeddedDevice = device;
        Assert.True(viewModel.CanSelectEmbedded);
        await viewModel.SelectEmbeddedAsync();
        await viewModel.PrepareEmbeddedAsync();

        Assert.False(viewModel.CanFlashEmbedded);
        Assert.Equal(1, deployment.EmbeddedSelectCalls);
        Assert.Equal(1, deployment.EmbeddedPrepareCalls);

        viewModel.IsEmbeddedFlashConfirmed = true;
        Assert.True(viewModel.CanFlashEmbedded);
        await viewModel.FlashEmbeddedAsync();

        Assert.Equal(1, deployment.EmbeddedFlashCalls);
        Assert.False(viewModel.IsEmbeddedFlashConfirmed);
        Assert.False(viewModel.CanFlashEmbedded);
        Assert.Contains("succeeded", viewModel.EmbeddedStatusText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("embedded-flash-receipt.json", viewModel.EmbeddedStatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StopLocalDeployment_UsesSameLifecycleModelAsStoredReceipt()
    {
        using var fixture = new ReceiptFixture();
        var deployment = new StubDeploymentService { DelayLocalUntilCancellation = true };
        var viewModel = CreateViewModel(deployment);
        viewModel.ApplyLatestBuild(fixture.Receipt);
        await viewModel.PrepareAsync();

        var runTask = viewModel.RunLocalAsync();
        await Task.Yield();
        await viewModel.StopLocalAsync();
        await runTask;
        await WaitForAsync(
            () => viewModel.LocalDeploymentText.Contains("Local deployment cancelled", StringComparison.OrdinalIgnoreCase)
                && viewModel.RecoveryText.Contains("started from this Studio session", StringComparison.OrdinalIgnoreCase));

        Assert.Contains("Local deployment cancelled", viewModel.LocalDeploymentText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("exit n/a", viewModel.LocalDeploymentText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("5s", viewModel.LocalDeploymentText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("started from this Studio session", viewModel.RecoveryText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(viewModel.LocalTargetStates, target => target.Status == "cancelled");
    }

    [Fact]
    public async Task RefreshAsync_RestoresSnapshotOnly_WhenPreparedStateIsMissing()
    {
        using var fixture = new ReceiptFixture();
        var deployment = new StubDeploymentService
        {
            Stored = new StoredLocalDeployment(
                Path.Combine(fixture.Root, ".junexis", "state", "deploy", fixture.BuildId, "deployment-receipt.json"),
                fixture.BuildId,
                "succeeded",
                0,
                5,
                ["report", "server"],
                [
                    new DeploymentTargetStatus("report", "ready", "Reported runtime readiness."),
                    new DeploymentTargetStatus("server", "ready", "HTTP http://127.0.0.1:7100")
                ],
                "Bounded local deployment completed successfully.")
        };
        var latestBuilds = new StubLatestBuildReceiptService { Receipt = fixture.Receipt };
        var workspace = new StubWorkspace { RootPathValue = fixture.Root };
        var viewModel = new DeployCenterViewModel(
            deployment,
            latestBuilds,
            workspace,
            new ActiveDocumentService(),
            new OutputService());

        await viewModel.RefreshAsync();

        Assert.False(viewModel.CanRunLocal);
        Assert.Equal("Snapshot only", viewModel.RecoveryModeTitle);
        Assert.Contains("snapshot only", viewModel.RecoveryText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RefreshAsync_RestoresLastLocalDeploymentReceipt()
    {
        using var fixture = new ReceiptFixture();
        var deployment = new StubDeploymentService
        {
            Stored = new StoredLocalDeployment(
                Path.Combine(fixture.Root, ".junexis", "state", "deploy", fixture.BuildId, "deployment-receipt.json"),
                fixture.BuildId,
                "succeeded",
                0,
                5,
                ["report", "server"],
                [
                    new DeploymentTargetStatus("report", "ready", "Reported runtime readiness."),
                    new DeploymentTargetStatus("server", "ready", "HTTP http://127.0.0.1:7100")
                ],
                "Bounded local deployment completed successfully."),
            RestoredPreparation = new DeploymentPreparationResult(
                fixture.BuildId,
                new string('c', 64),
                Path.Combine(fixture.Root, ".junexis", "state", "deploy", fixture.BuildId, "system-bundle.zip"),
                Path.Combine(fixture.Root, ".junexis", "state", "deploy", fixture.BuildId, "system-bundle.zip.package-receipt.json"),
                Path.Combine(fixture.Root, ".junexis", "state", "deploy", fixture.BuildId, "deployment-preflight.json"),
                "ready",
                ["report", "server"],
                [new DeploymentPreflightCheck("DP0001", "passed", null, "ok")],
                "reviewed",
                "non-blocking")
        };
        var latestBuilds = new StubLatestBuildReceiptService { Receipt = fixture.Receipt };
        var workspace = new StubWorkspace { RootPathValue = fixture.Root };
        var viewModel = new DeployCenterViewModel(
            deployment,
            latestBuilds,
            workspace,
            new ActiveDocumentService(),
            new OutputService());

        await viewModel.RefreshAsync();

        Assert.True(viewModel.CanRunLocal);
        Assert.Contains("Restored prepared deployment state", viewModel.StatusText, StringComparison.OrdinalIgnoreCase);
    Assert.Equal("Rerunnable prepared state", viewModel.RecoveryModeTitle);
        Assert.Contains("rerunnable prepared deployment state", viewModel.RecoveryText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Last local deployment succeeded", viewModel.LocalDeploymentText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("exit 0", viewModel.LocalDeploymentText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("5s", viewModel.LocalDeploymentText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("deployment-receipt.json", viewModel.LocalDeploymentText, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, viewModel.LocalTargetStates.Count);
        Assert.Contains(viewModel.LocalTargetStates, target => target.Target == "report" && target.Status == "ready");
    }

    [Fact]
    public async Task RefreshAsync_RestoresLiveResumableSession_AndDisablesRunLocal()
    {
        using var fixture = new ReceiptFixture();
        var deployment = new StubDeploymentService
        {
            Live = new LiveLocalDeployment(
                Path.Combine(fixture.Root, ".junexis", "state", "deploy", fixture.BuildId, "deployment-session.json"),
                fixture.BuildId,
                "running",
                "junexis-studio",
                "studio-instance",
                DateTimeOffset.UtcNow.AddSeconds(30).UtcDateTime.ToString("O"),
                DateTimeOffset.UtcNow.UtcDateTime.ToString("O"),
                true,
                false,
                ["report", "server"],
                [
                    new DeploymentTargetStatus("report", "running", "Process is alive."),
                    new DeploymentTargetStatus("server", "running", "Active probe OK: http://127.0.0.1:7101/health/ready")
                ],
                "Live local session running.")
        };
        var latestBuilds = new StubLatestBuildReceiptService { Receipt = fixture.Receipt };
        var workspace = new StubWorkspace { RootPathValue = fixture.Root };
        var viewModel = new DeployCenterViewModel(
            deployment,
            latestBuilds,
            workspace,
            new ActiveDocumentService(),
            new OutputService());

        await viewModel.RefreshAsync();

        Assert.False(viewModel.CanRunLocal);
        Assert.Equal("Live resumable session", viewModel.RecoveryModeTitle);
        Assert.Contains("renewed its lease", viewModel.RecoveryText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Lease valid for", viewModel.RecoveryText, StringComparison.Ordinal);
        Assert.Contains("Heartbeat is fresh", viewModel.RecoveryText, StringComparison.Ordinal);
        Assert.Contains("Live local deployment running", viewModel.LocalDeploymentText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Lease valid for", viewModel.LocalDeploymentText, StringComparison.Ordinal);
        Assert.Contains("Heartbeat is fresh", viewModel.LocalDeploymentText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AdoptLiveAsync_TransfersLiveSessionOwnership_ToCurrentStudio()
    {
        using var fixture = new ReceiptFixture();
        var deployment = new StubDeploymentService
        {
            Live = new LiveLocalDeployment(
                Path.Combine(fixture.Root, ".junexis", "state", "deploy", fixture.BuildId, "deployment-session.json"),
                fixture.BuildId,
                "running",
                "junexis-cli",
                "expired-owner",
                DateTimeOffset.UtcNow.AddSeconds(-10).UtcDateTime.ToString("O"),
                DateTimeOffset.UtcNow.AddSeconds(-25).UtcDateTime.ToString("O"),
                false,
                true,
                ["server"],
                [new DeploymentTargetStatus("server", "running", "Active probe OK")],
                "Live orphan session."),
            AdoptedLive = new LiveLocalDeployment(
                Path.Combine(fixture.Root, ".junexis", "state", "deploy", fixture.BuildId, "deployment-session.json"),
                fixture.BuildId,
                "running",
                "junexis-studio",
                "studio-instance",
                DateTimeOffset.UtcNow.AddSeconds(30).UtcDateTime.ToString("O"),
                DateTimeOffset.UtcNow.UtcDateTime.ToString("O"),
                true,
                false,
                ["server"],
                [new DeploymentTargetStatus("server", "running", "Active probe OK")],
                "Adopted live session.")
        };
        var latestBuilds = new StubLatestBuildReceiptService { Receipt = fixture.Receipt };
        var workspace = new StubWorkspace { RootPathValue = fixture.Root };
        var viewModel = new DeployCenterViewModel(
            deployment,
            latestBuilds,
            workspace,
            new ActiveDocumentService(),
            new OutputService());
        await viewModel.RefreshAsync();

        Assert.True(viewModel.CanAdoptLive);
        var notifications = new List<string?>();
        viewModel.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
        Assert.Contains("Lease expired", viewModel.RecoveryText, StringComparison.Ordinal);
        Assert.Contains("Heartbeat is stale", viewModel.RecoveryText, StringComparison.Ordinal);

        await viewModel.AdoptLiveAsync();

        Assert.Equal(1, deployment.AdoptCalls);
        Assert.Equal("Live resumable session", viewModel.RecoveryModeTitle);
        Assert.Contains("now owns the current lease", viewModel.RecoveryText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Lease valid for", viewModel.RecoveryText, StringComparison.Ordinal);
        Assert.Contains("Heartbeat is fresh", viewModel.RecoveryText, StringComparison.Ordinal);
        Assert.False(viewModel.CanAdoptLive);
        Assert.Contains(nameof(DeployCenterViewModel.CanAdoptLive), notifications);
    }

    [Fact]
    public async Task StopLocalAsync_StopsOwnedLiveSession_ThroughDeploymentService()
    {
        using var fixture = new ReceiptFixture();
        var deployment = new StubDeploymentService
        {
            Live = new LiveLocalDeployment(
                Path.Combine(fixture.Root, ".junexis", "state", "deploy", fixture.BuildId, "deployment-session.json"),
                fixture.BuildId,
                "running",
                "junexis-studio",
                "studio-instance",
                DateTimeOffset.UtcNow.AddSeconds(30).UtcDateTime.ToString("O"),
                DateTimeOffset.UtcNow.UtcDateTime.ToString("O"),
                true,
                false,
                ["server"],
                [new DeploymentTargetStatus("server", "running", "Active probe OK")],
                "Owned live session."),
            StoppedLive = new LiveLocalDeployment(
                Path.Combine(fixture.Root, ".junexis", "state", "deploy", fixture.BuildId, "deployment-session.json"),
                fixture.BuildId,
                "stopped",
                "junexis-studio",
                "studio-instance",
                DateTimeOffset.UtcNow.UtcDateTime.ToString("O"),
                DateTimeOffset.UtcNow.UtcDateTime.ToString("O"),
                false,
                false,
                ["server"],
                [new DeploymentTargetStatus("server", "stopped", "Stopped by owner")],
                "Stopped live session.")
        };
        var latestBuilds = new StubLatestBuildReceiptService { Receipt = fixture.Receipt };
        var workspace = new StubWorkspace { RootPathValue = fixture.Root };
        var viewModel = new DeployCenterViewModel(
            deployment,
            latestBuilds,
            workspace,
            new ActiveDocumentService(),
            new OutputService());

        await viewModel.RefreshAsync();
        Assert.True(viewModel.CanStopLocal);

        await viewModel.StopLocalAsync();

        Assert.Equal(1, deployment.StopLiveCalls);
        Assert.Equal("Historical snapshot", viewModel.RecoveryModeTitle);
        Assert.Contains("no longer resumable", viewModel.RecoveryText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Live local deployment stopped", viewModel.LocalDeploymentText, StringComparison.OrdinalIgnoreCase);
        Assert.False(viewModel.CanStopLocal);
    }

    private static DeployCenterViewModel CreateViewModel(IDeploymentService deployment) =>
        new(
            deployment,
            new StubLatestBuildReceiptService(),
            new StubWorkspace(),
            new ActiveDocumentService(),
            new OutputService());

    private static async Task WaitForAsync(Func<bool> predicate)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            if (predicate())
                return;
            await Task.Yield();
        }
    }

    private sealed class StubDeploymentService : IDeploymentService
    {
        public int PrepareCalls { get; private set; }

        public int LocalCalls { get; private set; }

        public int EmbeddedPrepareCalls { get; private set; }

        public int EmbeddedSelectCalls { get; private set; }

        public int EmbeddedFlashCalls { get; private set; }

        public StoredLocalDeployment? Stored { get; init; }

        public LiveLocalDeployment? Live { get; init; }

        public LiveLocalDeployment? AdoptedLive { get; init; }

        public LiveLocalDeployment? StoppedLive { get; init; }

        public DeploymentPreparationResult? RestoredPreparation { get; init; }

        public bool DelayLocalUntilCancellation { get; init; }

        public int AdoptCalls { get; private set; }

        public int StopLiveCalls { get; private set; }

        public DeploymentAuthenticityPolicy? LastAuthenticity { get; private set; }

        public DeploymentReadiness Evaluate(LatestBuildReceipt? receipt)
        {
            if (receipt is null)
                return new DeploymentReadiness(false, false, "missing", null, null, null, [], [], []);
            var targets = receipt.Targets.ToArray();
            var deployable = targets.Where(target => target is "server" or "report").Order().ToArray();
            var blocked = targets.Where(target => target is not ("server" or "report")).Order().ToArray();
            return new DeploymentReadiness(
                true,
                deployable.Length > 0,
                "ready",
                receipt.BuildId,
                receipt.ProjectRoot,
                receipt.ReceiptPath,
                targets,
                deployable,
                blocked);
        }

        public Task<DeploymentPreparationResult> PrepareAsync(
            LatestBuildReceipt receipt,
            Action<ProcessOutputLine>? listener = null,
            CancellationToken cancellationToken = default)
        {
            PrepareCalls++;
            listener?.Invoke(new ProcessOutputLine(ProcessOutputKind.StdOut, "[prepare] package ok"));
            return Task.FromResult(new DeploymentPreparationResult(
                receipt.BuildId,
                new string('c', 64),
                Path.Combine(receipt.ProjectRoot, "bundle.zip"),
                Path.Combine(receipt.ProjectRoot, "bundle.zip.package-receipt.json"),
                Path.Combine(receipt.ProjectRoot, "deployment-preflight.json"),
                "ready",
                ["report", "server"],
                [new DeploymentPreflightCheck("DP0001", "passed", null, "ok")],
                "reviewed",
                "non-blocking"));
        }

        public async Task<DeploymentPreparationResult> PrepareAsync(
            LatestBuildReceipt receipt,
            DeploymentAuthenticityPolicy authenticity,
            Action<ProcessOutputLine>? listener = null,
            CancellationToken cancellationToken = default)
        {
            LastAuthenticity = authenticity;
            var preparation = await PrepareAsync(receipt, listener, cancellationToken);
            var trusted = authenticity.Policy == "trusted"
                && authenticity.TrustedSignerFingerprints.Count > 0;
            return preparation with
            {
                AuthenticityStatus = trusted ? "trusted" : "unsigned",
                AuthenticityPolicy = authenticity.Policy,
                AuthenticityPolicySatisfied = authenticity.Policy == "permissive" || trusted,
                Signer = trusted ? authenticity.Signer : null,
                SignerFingerprint = trusted
                    ? authenticity.TrustedSignerFingerprints[0]
                    : null
            };
        }

        public Task<LocalDeploymentResult> RunLocalAsync(
            DeploymentPreparationResult preparation,
            int durationSeconds,
            Action<ProcessOutputLine>? listener = null,
            CancellationToken cancellationToken = default)
        {
            LocalCalls++;
            if (DelayLocalUntilCancellation)
                return RunCancelledAsync(preparation, durationSeconds, cancellationToken);
            listener?.Invoke(new ProcessOutputLine(ProcessOutputKind.StdOut, "[local] READY server"));
            return Task.FromResult(new LocalDeploymentResult(
                true,
                false,
                "succeeded",
                0,
                preparation.BuildId,
                preparation.BundlePath,
                Path.Combine(Path.GetDirectoryName(preparation.PreflightReportPath)!, "deployment-receipt.json"),
                preparation.PreflightReportPath,
                durationSeconds,
                preparation.Targets,
                [
                    new DeploymentTargetStatus("report", "ready", "Reported runtime readiness."),
                    new DeploymentTargetStatus("server", "ready", "HTTP http://127.0.0.1:7100")
                ],
                "Bounded local deployment completed successfully."));
        }

        private static async Task<LocalDeploymentResult> RunCancelledAsync(
            DeploymentPreparationResult preparation,
            int durationSeconds,
            CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException("Cancellation was expected.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return new LocalDeploymentResult(
                    false,
                    true,
                    "cancelled",
                    null,
                    preparation.BuildId,
                    preparation.BundlePath,
                    Path.Combine(Path.GetDirectoryName(preparation.PreflightReportPath)!, "deployment-receipt.json"),
                    preparation.PreflightReportPath,
                    durationSeconds,
                    preparation.Targets,
                    [new DeploymentTargetStatus("server", "cancelled", "Bounded local deployment was cancelled before completion.")],
                    "Bounded local deployment cancelled.");
            }
        }

        public Task<StoredLocalDeployment?> TryLoadLastLocalAsync(
            LatestBuildReceipt receipt,
            CancellationToken cancellationToken = default) => Task.FromResult(Stored);

        public Task<LiveLocalDeployment?> TryLoadLiveLocalAsync(
            LatestBuildReceipt receipt,
            CancellationToken cancellationToken = default) => Task.FromResult(Live);

        public Task<LiveLocalDeployment> AdoptLiveLocalAsync(
            LatestBuildReceipt receipt,
            CancellationToken cancellationToken = default)
        {
            AdoptCalls++;
            return Task.FromResult(AdoptedLive ?? throw new InvalidOperationException("No adopted live session was configured."));
        }

        public Task<LiveLocalDeployment> StopLiveLocalAsync(
            LatestBuildReceipt receipt,
            CancellationToken cancellationToken = default)
        {
            StopLiveCalls++;
            return Task.FromResult(StoppedLive ?? throw new InvalidOperationException("No stopped live session was configured."));
        }

        public Task<EmbeddedProbeResult> ProbeEmbeddedAsync(
            CancellationToken cancellationToken = default) => Task.FromResult(new EmbeddedProbeResult(
                true,
                true,
                "pio",
                "Detected 1 embedded device.",
                [new EmbeddedDeviceCandidate("COM7", "Junexis board", "USB VID:PID", "SERIAL-7", "Junexis")]));

        public Task<EmbeddedDeploymentPreparation> PrepareEmbeddedAsync(
            LatestBuildReceipt receipt,
            Action<ProcessOutputLine>? listener = null,
            CancellationToken cancellationToken = default)
        {
            EmbeddedPrepareCalls++;
            listener?.Invoke(new ProcessOutputLine(ProcessOutputKind.StdOut, "[embedded-package] package ok"));
            return Task.FromResult(new EmbeddedDeploymentPreparation(
                receipt.BuildId,
                new string('e', 64),
                Path.Combine(receipt.ProjectRoot, "embedded-system-bundle.zip"),
                Path.Combine(receipt.ProjectRoot, "embedded-system-bundle.zip.package-receipt.json")));
        }

        public Task<EmbeddedDeviceSelection> SelectEmbeddedAsync(
            LatestBuildReceipt receipt,
            EmbeddedDeviceCandidate device,
            CancellationToken cancellationToken = default)
        {
            EmbeddedSelectCalls++;
            return Task.FromResult(new EmbeddedDeviceSelection(
                Path.Combine(receipt.ProjectRoot, "embedded-device-selection.json"),
                device.Port,
                device.Description,
                device.HardwareId,
                device.SerialNumber,
                device.Manufacturer));
        }

        public Task<EmbeddedFlashResult> FlashEmbeddedAsync(
            EmbeddedDeploymentPreparation preparation,
            EmbeddedDeviceSelection selection,
            Action<ProcessOutputLine>? listener = null,
            CancellationToken cancellationToken = default)
        {
            EmbeddedFlashCalls++;
            listener?.Invoke(new ProcessOutputLine(ProcessOutputKind.StdOut, "[embedded-flash] upload ok"));
            return Task.FromResult(new EmbeddedFlashResult(
                true,
                "succeeded",
                "Embedded flash completed successfully.",
                preparation.BuildId,
                preparation.BundleId,
                selection.Port,
                Path.Combine(Path.GetDirectoryName(selection.SelectionPath)!, "embedded-flash-receipt.json")));
        }

        public Task<DeploymentPreparationResult?> TryRestorePreparationAsync(
            LatestBuildReceipt receipt,
            CancellationToken cancellationToken = default) => Task.FromResult(RestoredPreparation);
    }

    private sealed class StubLatestBuildReceiptService : ILatestBuildReceiptService
    {
        public LatestBuildReceipt? Receipt { get; init; }

        public Task<LatestBuildReceipt> LoadAsync(string receiptPath, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<LatestBuildReceipt?> TryLoadForContextAsync(
            string workspaceRoot,
            string? activeDocumentPath,
            CancellationToken cancellationToken = default) => Task.FromResult(Receipt);
    }

    private sealed class StubWorkspace : IWorkspace
    {
        public string? RootPath => RootPathValue;

        public string? RootPathValue { get; init; }

        public event Action<string>? FileOpened { add { } remove { } }
        public event Action<string>? FolderOpened { add { } remove { } }
        public event Action<string>? FileSaved { add { } remove { } }
        public void OpenFolder(string path) => throw new NotSupportedException();
        public string ReadFile(string path) => throw new NotSupportedException();
        public void WriteFile(string path, string content) => throw new NotSupportedException();
        public string? GetWorkspaceConfigRaw() => null;
        public void SetWorkspaceConfigRaw(string content) => throw new NotSupportedException();
    }

    private sealed class ReceiptFixture : IDisposable
    {
        public ReceiptFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), $"junexis-deploy-panel-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path.Combine(Root, "src"));
            Directory.CreateDirectory(Path.Combine(Root, "out", "debug"));
            Directory.CreateDirectory(Path.Combine(Root, "out", "analysis"));
            var entry = Path.Combine(Root, "src", "main.jnx");
            var bundleManifest = Path.Combine(Root, "out", "junexis.bundle.json");
            var artifactManifest = Path.Combine(Root, "out", "junexis.artifacts.json");
            var debugManifest = Path.Combine(Root, "out", "debug", "junexis.debug.toml");
            var analysisSnapshot = Path.Combine(Root, "out", "analysis", "junexis.analysis.json");
            File.WriteAllText(Path.Combine(Root, "junexis.project.toml"), "entry = \"src/main.jnx\"");
            File.WriteAllText(entry, "task Main() @server {}\n");
            var identity = JsonSerializer.Serialize(new { buildId = BuildId });
            File.WriteAllText(bundleManifest, identity);
            File.WriteAllText(artifactManifest, identity);
            File.WriteAllText(debugManifest, "{}");
            File.WriteAllText(analysisSnapshot, identity);
            Receipt = new LatestBuildReceipt(
                Path.Combine(Root, ".junexis", "state", "latest-build.json"),
                BuildId,
                "project",
                Root,
                entry,
                Path.Combine(Root, "out"),
                ["report", "server"],
                bundleManifest,
                artifactManifest,
                debugManifest,
                analysisSnapshot);
        }

        public string Root { get; }

        public string BuildId { get; } = new string('b', 64);

        public LatestBuildReceipt Receipt { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
    }
}

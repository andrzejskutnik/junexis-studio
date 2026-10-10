using System.Text.Json;
using Junexis.Observability.Adapters.Deploy;
using Junexis.Observability.Adapters.Studio;
using Junexis.Observability.Ingress;
using Junexis.Studio.Abstractions;

namespace JunexisLang.Studio.CompilerExtension;

internal sealed class JunexisDeploymentService : IDeploymentService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };
    private const string StudioOwnerTool = "junexis-studio";
    private const int StudioLeaseSeconds = 30;

    private readonly JunexisCompilerOptions _options;
    private readonly IProcessService _processes;
    private readonly DeployEventAdapter? _deployEventAdapter;

    internal JunexisDeploymentService(JunexisCompilerOptions options, IProcessService processes, EventIngressGateway? ingressGateway = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(processes);
        _options = options;
        _processes = processes;
        var effectiveGateway = ingressGateway ?? TryCreateIngressGateway(options.ResolvedObservabilityIngressUrl);
        if (effectiveGateway is not null)
            _deployEventAdapter = new DeployEventAdapter(effectiveGateway);
    }

    public DeploymentReadiness Evaluate(LatestBuildReceipt? receipt)
    {
        if (receipt is null)
        {
            return new DeploymentReadiness(
                false,
                false,
                "Build a Junexis project to prepare deployment.",
                null,
                null,
                null,
                [],
                [],
                []);
        }

        var targets = receipt.Targets
            .Select(target => target.ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var deployable = targets
            .Where(target => target is "server" or "report")
            .ToArray();
        var blocked = targets
            .Except(deployable, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        if (!string.Equals(receipt.Mode, "project", StringComparison.Ordinal))
        {
            return new DeploymentReadiness(
                true,
                false,
                "Deploy Center requires a project build; file-mode latest-build receipts cannot be packaged.",
                receipt.BuildId,
                receipt.ProjectRoot,
                receipt.ReceiptPath,
                targets,
                deployable,
                blocked);
        }

        if (!File.Exists(Path.Combine(receipt.ProjectRoot, "junexis.project.toml")))
        {
            return new DeploymentReadiness(
                true,
                false,
                "Verified build has no project manifest at its recorded root.",
                receipt.BuildId,
                receipt.ProjectRoot,
                receipt.ReceiptPath,
                targets,
                deployable,
                blocked);
        }

        if (deployable.Length == 0)
        {
            return new DeploymentReadiness(
                true,
                false,
                "Current build has no server/report targets for bounded local deployment. Use the dedicated embedded section for probe, selection, packaging, and confirmed flash.",
                receipt.BuildId,
                receipt.ProjectRoot,
                receipt.ReceiptPath,
                targets,
                deployable,
                blocked);
        }

        var status = blocked.Length == 0
            ? $"Ready to package and preflight {string.Join(", ", deployable)} for build {receipt.BuildId[..12]}."
            : $"Ready to package {string.Join(", ", deployable)} locally; separate deployment flows: {string.Join(", ", blocked)}.";
        return new DeploymentReadiness(
            true,
            true,
            status,
            receipt.BuildId,
            receipt.ProjectRoot,
            receipt.ReceiptPath,
            targets,
            deployable,
            blocked);
    }

    public async Task<DeploymentPreparationResult> PrepareAsync(
        LatestBuildReceipt receipt,
        Action<ProcessOutputLine>? listener = null,
        CancellationToken cancellationToken = default)
    {
        return await PrepareAsync(
            receipt,
            new DeploymentAuthenticityPolicy("permissive", null, null, []),
            listener,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<DeploymentPreparationResult> PrepareAsync(
        LatestBuildReceipt receipt,
        DeploymentAuthenticityPolicy authenticity,
        Action<ProcessOutputLine>? listener = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(authenticity);
        var readiness = Evaluate(receipt);
        if (!readiness.CanPrepare)
            throw new InvalidOperationException(readiness.StatusText);

        var stateRoot = Path.Combine(receipt.ProjectRoot, ".junexis", "state", "deploy", receipt.BuildId);
        Directory.CreateDirectory(stateRoot);
        var bundlePath = Path.Combine(stateRoot, "system-bundle.zip");
        var packageReceiptPath = bundlePath + ".package-receipt.json";
        var signaturePath = bundlePath + ".signature.json";
        var preflightReportPath = Path.Combine(stateRoot, "deployment-preflight.json");
        DeleteIfExists(bundlePath);
        DeleteIfExists(packageReceiptPath);
        DeleteIfExists(signaturePath);
        DeleteIfExists(preflightReportPath);

        var packageArguments = new List<string>
        {
            "package",
            receipt.ProjectRoot,
            "--out",
            bundlePath
        };
        packageArguments.AddRange(
            readiness.DeployableTargets.SelectMany(target => new[] { "--target", target }));
        if (!string.IsNullOrWhiteSpace(authenticity.SigningKeyPath))
        {
            packageArguments.Add("--sign-key");
            packageArguments.Add(authenticity.SigningKeyPath);
            packageArguments.Add("--signer");
            packageArguments.Add(authenticity.Signer ?? string.Empty);
        }

        await RunCheckedAsync(
            "package",
            packageArguments,
            receipt.ProjectRoot,
            listener,
            cancellationToken).ConfigureAwait(false);

        var preflightArguments = new List<string>
        {
            "deploy",
            "preflight",
            bundlePath,
            "--receipt",
            packageReceiptPath,
            "--report-out",
            preflightReportPath,
            "--auth-policy",
            authenticity.Policy,
            "--json"
        };
        preflightArguments.AddRange(
            authenticity.TrustedSignerFingerprints.SelectMany(
                fingerprint => new[] { "--trusted-signer", fingerprint }));
        preflightArguments.AddRange(
            readiness.DeployableTargets.SelectMany(target => new[] { "--target", target }));

        var preflightResult = await RunAsync(
            "preflight",
            preflightArguments,
            receipt.ProjectRoot,
            listener,
            cancellationToken).ConfigureAwait(false);

        if (!File.Exists(preflightReportPath))
        {
            throw new InvalidOperationException(
                $"preflight did not publish a report (exit code {preflightResult.ExitCode}). {Summarize(preflightResult.Transcript)}");
        }

        var report = await ReadReportAsync(preflightReportPath, cancellationToken).ConfigureAwait(false);
        var packageReceipt = await ReadPackageReceiptAsync(packageReceiptPath, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(report.BuildId, receipt.BuildId, StringComparison.Ordinal)
            || !string.Equals(packageReceipt.BuildId, receipt.BuildId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Deployment preparation produced artifacts for a different build. Rebuild the project and prepare deployment again.");
        }
        if (!string.Equals(report.BundleId, packageReceipt.BundleId, StringComparison.Ordinal))
            throw new InvalidOperationException("Deployment preflight and package receipt refer to different bundles.");
        if (!report.Targets.Order(StringComparer.Ordinal).SequenceEqual(
                packageReceipt.Targets.Order(StringComparer.Ordinal),
                StringComparer.Ordinal))
        {
            throw new InvalidOperationException("Deployment preflight and package receipt contain different targets.");
        }
        return new DeploymentPreparationResult(
            report.BuildId,
            report.BundleId,
            bundlePath,
            packageReceiptPath,
            preflightReportPath,
            report.TechnicalStatus,
            report.Targets,
            report.Checks
                .Select(check => new DeploymentPreflightCheck(
                    check.Code,
                    check.Status,
                    check.Target,
                    check.Message))
                .ToArray(),
            report.Mentor.Status,
            report.Mentor.Summary)
        {
            AuthenticityStatus = report.Authenticity.Status,
            AuthenticityPolicy = report.Authenticity.Policy,
            AuthenticityPolicySatisfied = report.Authenticity.PolicySatisfied,
            Signer = report.Authenticity.Signer,
            SignerFingerprint = report.Authenticity.PublicKeyFingerprintSha256,
            SignaturePath = report.Authenticity.SignaturePath
        };
    }

    public async Task<LocalDeploymentResult> RunLocalAsync(
        DeploymentPreparationResult preparation,
        int durationSeconds,
        Action<ProcessOutputLine>? listener = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        if (durationSeconds is < 1 or > 300)
            throw new ArgumentOutOfRangeException(nameof(durationSeconds), "Local deployment duration must be between 1 and 300 seconds.");
        if (!string.Equals(preparation.TechnicalStatus, "ready", StringComparison.Ordinal))
            throw new InvalidOperationException("Local deployment requires a technically ready preflight report.");

        var deploymentReceiptPath = Path.Combine(
            Path.GetDirectoryName(preparation.PreflightReportPath)
                ?? throw new InvalidOperationException("Preflight report path has no parent directory."),
            "deployment-receipt.json");

        try
        {
            var result = await RunAsync(
                "local",
                [
                    "deploy",
                    "local",
                    preparation.BundlePath,
                    "--receipt",
                    preparation.PackageReceiptPath,
                    "--preflight",
                    preparation.PreflightReportPath,
                    "--duration",
                    durationSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    .. preparation.Targets.SelectMany(target => new[] { "--target", target })
                ],
                Path.GetDirectoryName(preparation.BundlePath)!,
                listener,
                cancellationToken).ConfigureAwait(false);
            var success = result.ExitCode == 0;
            var summary = success
                ? "Bounded local deployment completed successfully."
                : $"Bounded local deployment failed with exit code {result.ExitCode}. {Summarize(result.Transcript)}";
            var stored = File.Exists(deploymentReceiptPath)
                ? await LoadStoredLocalAsync(deploymentReceiptPath, cancellationToken).ConfigureAwait(false)
                : null;
            if (File.Exists(deploymentReceiptPath))
                await TryPublishDeploymentReceiptAsync(deploymentReceiptPath, CreateCorrelationId(preparation.BuildId, "local-run"), cancellationToken).ConfigureAwait(false);

            return new LocalDeploymentResult(
                success,
                false,
                stored?.LifecycleStatus ?? (success ? "succeeded" : "failed"),
                stored?.ExitCode ?? result.ExitCode,
                preparation.BuildId,
                preparation.BundlePath,
                deploymentReceiptPath,
                preparation.PreflightReportPath,
                durationSeconds,
                stored?.Targets ?? preparation.Targets,
                stored?.TargetStates ?? CreateFallbackTargetStates(preparation.Targets, success ? "succeeded" : "failed"),
                summary);
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
                deploymentReceiptPath,
                preparation.PreflightReportPath,
                durationSeconds,
                preparation.Targets,
                CreateFallbackTargetStates(preparation.Targets, "cancelled"),
                "Bounded local deployment cancelled.");
        }
    }

    public async Task<StoredLocalDeployment?> TryLoadLastLocalAsync(
        LatestBuildReceipt receipt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);

        var receiptPath = Path.Combine(
            receipt.ProjectRoot,
            ".junexis",
            "state",
            "deploy",
            receipt.BuildId,
            "deployment-receipt.json");
        return File.Exists(receiptPath)
            ? await LoadStoredLocalAsync(receiptPath, cancellationToken).ConfigureAwait(false)
            : null;
    }

    public async Task<LiveLocalDeployment?> TryLoadLiveLocalAsync(
        LatestBuildReceipt receipt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);

        var sessionPath = Path.Combine(
            receipt.ProjectRoot,
            ".junexis",
            "state",
            "deploy",
            receipt.BuildId,
            "deployment-session.json");
        if (!File.Exists(sessionPath))
            return null;

        var studioOwnerInstanceId = GetOrCreateStudioOwnerInstanceId(receipt.ProjectRoot);
        var session = await LoadLiveSessionAsync(sessionPath, studioOwnerInstanceId, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(session.BuildId, receipt.BuildId, StringComparison.Ordinal))
            return null;
        if (string.Equals(session.State, "stopped", StringComparison.Ordinal)
            || string.Equals(session.State, "failed", StringComparison.Ordinal)
            || string.Equals(session.State, "lost", StringComparison.Ordinal))
        {
            return null;
        }

        if (string.Equals(session.OwnerTool, StudioOwnerTool, StringComparison.Ordinal)
            && string.Equals(session.OwnerInstanceId, studioOwnerInstanceId, StringComparison.Ordinal))
        {
            session = await AdoptLiveSessionInternalAsync(
                receipt.ProjectRoot,
                sessionPath,
                StudioOwnerTool,
                studioOwnerInstanceId,
                cancellationToken).ConfigureAwait(false);
        }

        return session;
    }

    public async Task<LiveLocalDeployment> AdoptLiveLocalAsync(
        LatestBuildReceipt receipt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);

        var sessionPath = Path.Combine(
            receipt.ProjectRoot,
            ".junexis",
            "state",
            "deploy",
            receipt.BuildId,
            "deployment-session.json");
        if (!File.Exists(sessionPath))
            throw new InvalidOperationException("No live deployment session is available for adoption.");

        var studioOwnerInstanceId = GetOrCreateStudioOwnerInstanceId(receipt.ProjectRoot);
        var adopted = await AdoptLiveSessionInternalAsync(
            receipt.ProjectRoot,
            sessionPath,
            StudioOwnerTool,
            studioOwnerInstanceId,
            cancellationToken).ConfigureAwait(false);
        await TryPublishLiveSessionAsync(sessionPath, CreateCorrelationId(receipt.BuildId, "adopt-live"), cancellationToken).ConfigureAwait(false);
        return adopted;
    }

    public async Task<LiveLocalDeployment> StopLiveLocalAsync(
        LatestBuildReceipt receipt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);

        var sessionPath = Path.Combine(
            receipt.ProjectRoot,
            ".junexis",
            "state",
            "deploy",
            receipt.BuildId,
            "deployment-session.json");
        if (!File.Exists(sessionPath))
            throw new InvalidOperationException("No live deployment session is available to stop.");

        var studioOwnerInstanceId = GetOrCreateStudioOwnerInstanceId(receipt.ProjectRoot);
        var json = await RunJsonCliAsync(
            "stop-session",
            [
                "deploy",
                "local",
                "stop",
                "--session",
                sessionPath,
                "--owner-tool",
                StudioOwnerTool,
                "--owner-instance-id",
                studioOwnerInstanceId,
                "--json"
            ],
            receipt.ProjectRoot,
            cancellationToken).ConfigureAwait(false);
        var dto = DeserializeLiveSession(json);
        var stopped = MapLiveSession(dto, sessionPath, studioOwnerInstanceId);
        await TryPublishLiveSessionAsync(sessionPath, CreateCorrelationId(receipt.BuildId, "stop-live"), cancellationToken).ConfigureAwait(false);
        return stopped;
    }

    public async Task<DeploymentPreparationResult?> TryRestorePreparationAsync(
        LatestBuildReceipt receipt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);

        var receiptPath = Path.Combine(
            receipt.ProjectRoot,
            ".junexis",
            "state",
            "deploy",
            receipt.BuildId,
            "deployment-receipt.json");
        if (!File.Exists(receiptPath))
            return null;

        var dto = await ReadDeploymentReceiptAsync(receiptPath, receipt.BuildId, cancellationToken).ConfigureAwait(false);
        var stateRoot = Path.GetDirectoryName(receiptPath)
            ?? throw new InvalidOperationException("Deployment receipt path has no parent directory.");
        var bundlePath = Path.Combine(stateRoot, "system-bundle.zip");
        if (!File.Exists(bundlePath)
            || !File.Exists(dto.PackageReceiptPath)
            || !File.Exists(dto.PreflightReportPath))
        {
            return null;
        }

        var report = await ReadReportAsync(dto.PreflightReportPath, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(report.BuildId, receipt.BuildId, StringComparison.Ordinal))
            throw new InvalidOperationException("Stored preflight report does not match the current verified build.");
        if (!string.Equals(report.BundleId, dto.BundleId, StringComparison.Ordinal))
            throw new InvalidOperationException("Stored preflight report does not match the stored deployment bundle.");

        return new DeploymentPreparationResult(
            report.BuildId,
            report.BundleId,
            bundlePath,
            dto.PackageReceiptPath,
            dto.PreflightReportPath,
            report.TechnicalStatus,
            report.Targets,
            report.Checks
                .Select(check => new DeploymentPreflightCheck(
                    check.Code,
                    check.Status,
                    check.Target,
                    check.Message))
                .ToArray(),
            report.Mentor.Status,
            report.Mentor.Summary)
        {
            AuthenticityStatus = report.Authenticity.Status,
            AuthenticityPolicy = report.Authenticity.Policy,
            AuthenticityPolicySatisfied = report.Authenticity.PolicySatisfied,
            Signer = report.Authenticity.Signer,
            SignerFingerprint = report.Authenticity.PublicKeyFingerprintSha256,
            SignaturePath = report.Authenticity.SignaturePath
        };
    }

    public async Task<EmbeddedProbeResult> ProbeEmbeddedAsync(
        CancellationToken cancellationToken = default)
    {
        var json = await RunJsonCliAsync(
            "embedded-probe",
            [
                "deploy", "embedded", "probe",
                "--platformio-exe", _options.ResolvedPlatformIoExecutable,
                "--json"
            ],
            Environment.CurrentDirectory,
            cancellationToken).ConfigureAwait(false);
        var dto = JsonSerializer.Deserialize<EmbeddedProbeDto>(json, JsonOptions)
            ?? throw new InvalidOperationException("Embedded probe JSON is empty.");
        if (!string.Equals(dto.SchemaVersion, "junexis.embedded-probe/0.1", StringComparison.Ordinal))
            throw new InvalidOperationException($"Unsupported embedded probe schema '{dto.SchemaVersion}'.");
        return new EmbeddedProbeResult(
            dto.ToolAvailable,
            dto.ProbeSucceeded,
            dto.ToolPath,
            dto.Message,
            dto.Devices.Select(device => new EmbeddedDeviceCandidate(
                device.Port,
                device.Description,
                device.HardwareId,
                device.SerialNumber,
                device.Manufacturer)).ToArray());
    }

    public async Task<EmbeddedDeploymentPreparation> PrepareEmbeddedAsync(
        LatestBuildReceipt receipt,
        Action<ProcessOutputLine>? listener = null,
        CancellationToken cancellationToken = default)
    {
        return await PrepareEmbeddedAsync(
            receipt,
            new DeploymentAuthenticityPolicy("permissive", null, null, []),
            listener,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<EmbeddedDeploymentPreparation> PrepareEmbeddedAsync(
        LatestBuildReceipt receipt,
        DeploymentAuthenticityPolicy authenticity,
        Action<ProcessOutputLine>? listener = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(authenticity);
        if (!string.Equals(receipt.Mode, "project", StringComparison.Ordinal)
            || !receipt.Targets.Contains("embedded", StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Embedded preparation requires a verified project build with an embedded target.");
        }

        var stateRoot = EmbeddedStateRoot(receipt.ProjectRoot, receipt.BuildId);
        Directory.CreateDirectory(stateRoot);
        var bundlePath = Path.Combine(stateRoot, "embedded-system-bundle.zip");
        var packageReceiptPath = bundlePath + ".package-receipt.json";
        var signaturePath = bundlePath + ".signature.json";
        DeleteIfExists(bundlePath);
        DeleteIfExists(packageReceiptPath);
        DeleteIfExists(signaturePath);
        var arguments = new List<string>
        {
            "package", receipt.ProjectRoot, "--target", "embedded", "--out", bundlePath
        };
        if (!string.IsNullOrWhiteSpace(authenticity.SigningKeyPath))
        {
            arguments.Add("--sign-key");
            arguments.Add(authenticity.SigningKeyPath);
            arguments.Add("--signer");
            arguments.Add(authenticity.Signer ?? string.Empty);
        }
        await RunCheckedAsync(
            "embedded-package",
            arguments,
            receipt.ProjectRoot,
            listener,
            cancellationToken).ConfigureAwait(false);

        var packageReceipt = await ReadPackageReceiptAsync(packageReceiptPath, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(packageReceipt.BuildId, receipt.BuildId, StringComparison.Ordinal))
            throw new InvalidOperationException("Embedded package was produced for a different verified build.");
        if (!packageReceipt.Targets.SequenceEqual(["embedded"], StringComparer.Ordinal))
            throw new InvalidOperationException("Embedded package receipt does not contain exactly the embedded target.");
        return new EmbeddedDeploymentPreparation(
            packageReceipt.BuildId,
            packageReceipt.BundleId,
            bundlePath,
            packageReceiptPath)
        {
            AuthenticityPolicy = authenticity.Policy,
            SignaturePath = File.Exists(signaturePath) ? signaturePath : null,
            TrustedSignerFingerprints = authenticity.TrustedSignerFingerprints.ToArray()
        };
    }

    public async Task<EmbeddedDeviceSelection> SelectEmbeddedAsync(
        LatestBuildReceipt receipt,
        EmbeddedDeviceCandidate device,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(device);
        if (!receipt.Targets.Contains("embedded", StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("Current verified build has no embedded target.");
        var stateRoot = EmbeddedStateRoot(receipt.ProjectRoot, receipt.BuildId);
        Directory.CreateDirectory(stateRoot);
        var selectionPath = Path.Combine(stateRoot, "embedded-device-selection.json");
        DeleteIfExists(selectionPath);
        var json = await RunJsonCliAsync(
            "embedded-select",
            [
                "deploy", "embedded", "select",
                "--platformio-exe", _options.ResolvedPlatformIoExecutable,
                "--port", device.Port,
                "--selection-out", selectionPath,
                "--json"
            ],
            receipt.ProjectRoot,
            cancellationToken).ConfigureAwait(false);
        var dto = JsonSerializer.Deserialize<EmbeddedSelectionDto>(json, JsonOptions)
            ?? throw new InvalidOperationException("Embedded selection JSON is empty.");
        if (!string.Equals(dto.SchemaVersion, "junexis.embedded-device-selection/0.1", StringComparison.Ordinal)
            || !string.Equals(dto.Port, device.Port, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Embedded selection does not match the explicitly selected device.");
        }
        return new EmbeddedDeviceSelection(
            selectionPath,
            dto.Port,
            dto.Description,
            dto.HardwareId,
            dto.SerialNumber,
            dto.Manufacturer);
    }

    public async Task<EmbeddedFlashResult> FlashEmbeddedAsync(
        EmbeddedDeploymentPreparation preparation,
        EmbeddedDeviceSelection selection,
        Action<ProcessOutputLine>? listener = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        ArgumentNullException.ThrowIfNull(selection);
        var stateRoot = Path.GetFullPath(Path.GetDirectoryName(selection.SelectionPath)
            ?? throw new InvalidOperationException("Embedded selection path has no parent directory."));
        var bundleStateRoot = Path.GetFullPath(Path.GetDirectoryName(preparation.BundlePath)
            ?? throw new InvalidOperationException("Embedded bundle path has no parent directory."));
        if (!string.Equals(stateRoot, bundleStateRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Embedded package and device selection do not belong to the same verified deployment state.");
        if (!File.Exists(preparation.BundlePath)
            || !File.Exists(preparation.PackageReceiptPath)
            || !File.Exists(selection.SelectionPath))
        {
            throw new InvalidOperationException("Embedded flash requires the verified bundle, package receipt, and explicit selection contract.");
        }
        var flashReceiptPath = Path.Combine(stateRoot, "embedded-flash-receipt.json");
        DeleteIfExists(flashReceiptPath);
        var arguments = new List<string>
        {
            "deploy", "embedded", "flash", preparation.BundlePath,
            "--receipt", preparation.PackageReceiptPath,
            "--selection", selection.SelectionPath,
            "--platformio-exe", _options.ResolvedPlatformIoExecutable,
            "--receipt-out", flashReceiptPath,
            "--auth-policy", preparation.AuthenticityPolicy,
            "--json"
        };
        if (preparation.SignaturePath is not null)
        {
            arguments.Add("--signature");
            arguments.Add(preparation.SignaturePath);
        }
        arguments.AddRange(preparation.TrustedSignerFingerprints.SelectMany(
            fingerprint => new[] { "--trusted-signer", fingerprint }));
        var result = await RunAsync(
            "embedded-flash",
            arguments,
            stateRoot,
            listener,
            cancellationToken).ConfigureAwait(false);
        if (!File.Exists(flashReceiptPath))
            throw new InvalidOperationException($"Embedded flash did not publish a receipt (exit code {result.ExitCode}). {Summarize(result.Transcript)}");
        await using var stream = File.OpenRead(flashReceiptPath);
        var dto = await JsonSerializer.DeserializeAsync<EmbeddedFlashReceiptDto>(
            stream,
            JsonOptions,
            cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Embedded flash receipt is empty.");
        if (!string.Equals(dto.SchemaVersion, "junexis.embedded-flash-receipt/0.1", StringComparison.Ordinal)
            || !string.Equals(dto.BuildId, preparation.BuildId, StringComparison.Ordinal)
            || !string.Equals(dto.BundleId, preparation.BundleId, StringComparison.Ordinal)
            || !string.Equals(dto.Port, selection.Port, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Embedded flash receipt does not match the prepared bundle and explicit device selection.");
        }
        var success = result.ExitCode == 0
            && string.Equals(dto.LifecycleStatus, "succeeded", StringComparison.Ordinal);
        return new EmbeddedFlashResult(
            success,
            dto.LifecycleStatus,
            dto.Summary,
            dto.BuildId,
            dto.BundleId,
            dto.Port,
            flashReceiptPath);
    }

    private static string EmbeddedStateRoot(string projectRoot, string buildId) =>
        Path.Combine(projectRoot, ".junexis", "state", "deploy", buildId, "embedded");

    private async Task<StoredLocalDeployment> LoadStoredLocalAsync(
        string receiptPath,
        CancellationToken cancellationToken)
    {
        var dto = await ReadDeploymentReceiptAsync(receiptPath, expectedBuildId: null, cancellationToken).ConfigureAwait(false);
        var lifecycle = dto.Lifecycle
            ?? throw new InvalidOperationException("Deployment receipt has no lifecycle status.");
        var targets = dto.Targets
            .Select(target => target.Target)
            .Where(target => !string.IsNullOrWhiteSpace(target))
            .ToArray();
        if (targets.Length == 0)
            throw new InvalidOperationException("Deployment receipt has no valid target names.");

        return new StoredLocalDeployment(
            receiptPath,
            dto.BuildId,
            lifecycle.Status,
            lifecycle.ExitCode,
            lifecycle.DurationSeconds,
            targets,
            dto.Targets.Select(target => new DeploymentTargetStatus(
                target.Target,
                target.Status,
                target.Details)).ToArray(),
            lifecycle.Summary);
    }

    private static async Task<DeploymentReceiptDto> ReadDeploymentReceiptAsync(
        string receiptPath,
        string? expectedBuildId,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(receiptPath);
        var dto = await JsonSerializer.DeserializeAsync<DeploymentReceiptDto>(
            stream,
            JsonOptions,
            cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Deployment receipt is empty.");

        if (!string.Equals(dto.SchemaVersion, "junexis.deployment-receipt/0.1", StringComparison.Ordinal))
            throw new InvalidOperationException($"Unsupported deployment receipt schema '{dto.SchemaVersion}'.");
        if (!string.IsNullOrWhiteSpace(expectedBuildId)
            && !string.Equals(dto.BuildId, expectedBuildId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Deployment receipt does not match the current verified build.");
        }
        if (dto.Targets.Count == 0)
            throw new InvalidOperationException("Deployment receipt has no targets.");
        if (dto.Lifecycle is null || string.IsNullOrWhiteSpace(dto.Lifecycle.Status))
            throw new InvalidOperationException("Deployment receipt has no lifecycle status.");
        if (string.IsNullOrWhiteSpace(dto.PackageReceiptPath) || string.IsNullOrWhiteSpace(dto.PreflightReportPath))
            throw new InvalidOperationException("Deployment receipt is missing package or preflight paths.");
        if (string.IsNullOrWhiteSpace(dto.BundleId))
            throw new InvalidOperationException("Deployment receipt is missing bundle identity.");

        return dto;
    }

    private static IReadOnlyList<DeploymentTargetStatus> CreateFallbackTargetStates(
        IReadOnlyList<string> targets,
        string lifecycleStatus) =>
        targets.Select(target => new DeploymentTargetStatus(
            target,
            lifecycleStatus,
            $"No per-target receipt details were available; using lifecycle status '{lifecycleStatus}'."))
        .ToArray();

    private async Task RunCheckedAsync(
        string phase,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        Action<ProcessOutputLine>? listener,
        CancellationToken cancellationToken)
    {
        var result = await RunAsync(
            phase,
            arguments,
            workingDirectory,
            listener,
            cancellationToken).ConfigureAwait(false);
        if (result.ExitCode == 0)
            return;
        throw new InvalidOperationException(
            $"{phase} failed with exit code {result.ExitCode}. {Summarize(result.Transcript)}");
    }

    private async Task<ExecutionCapture> RunAsync(
        string phase,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        Action<ProcessOutputLine>? listener,
        CancellationToken cancellationToken)
    {
        var transcript = new List<string>();
        var request = CreateCliRequest(arguments, workingDirectory);
        var result = await _processes.RunAsync(
            request,
            line =>
            {
                var rendered = $"[{phase}] {line.Text}";
                transcript.Add(rendered);
                listener?.Invoke(new ProcessOutputLine(line.Kind, rendered));
            },
            cancellationToken).ConfigureAwait(false);
        return new ExecutionCapture(result.ExitCode, transcript);
    }

    private ProcessRequest CreateCliRequest(
        IReadOnlyList<string> arguments,
        string workingDirectory)
    {
        var cliPath = _options.ResolvedCliPath;
        return cliPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            ? new ProcessRequest(
                "dotnet",
                [Path.GetFullPath(cliPath), .. arguments],
                workingDirectory)
            : new ProcessRequest(cliPath, arguments, workingDirectory);
    }

    private static async Task<PreflightReportDto> ReadReportAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var report = await JsonSerializer.DeserializeAsync<PreflightReportDto>(
            stream,
            JsonOptions,
            cancellationToken).ConfigureAwait(false);
        if (report is null)
            throw new InvalidOperationException("Deployment preflight report is empty.");
        if (!string.Equals(report.TechnicalStatus, "ready", StringComparison.Ordinal)
            && !string.Equals(report.TechnicalStatus, "blocked", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Deployment preflight report has an unsupported technical status.");
        }
        return report;
    }

    private static async Task<PackageReceiptDto> ReadPackageReceiptAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var receipt = await JsonSerializer.DeserializeAsync<PackageReceiptDto>(
            stream,
            JsonOptions,
            cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Package receipt is empty.");
        if (!string.Equals(receipt.SchemaVersion, "junexis.package-receipt/0.1", StringComparison.Ordinal))
            throw new InvalidOperationException($"Unsupported package receipt schema '{receipt.SchemaVersion}'.");
        if (string.IsNullOrWhiteSpace(receipt.BuildId)
            || string.IsNullOrWhiteSpace(receipt.BundleId)
            || receipt.Targets.Count == 0)
        {
            throw new InvalidOperationException("Package receipt has incomplete deployment identity.");
        }
        return receipt;
    }

    private static string Summarize(IReadOnlyList<string> transcript)
    {
        if (transcript.Count == 0)
            return "No process output was captured.";
        return string.Join(" ", transcript.TakeLast(4));
    }

    private static EventIngressGateway? TryCreateIngressGateway(string? ingressUrl)
    {
        if (string.IsNullOrWhiteSpace(ingressUrl))
            return null;
        if (!Uri.TryCreate(ingressUrl, UriKind.Absolute, out var endpoint))
            return null;

        var client = new HttpClient();
        var sink = new HttpIngressSink(client, endpoint);
        return new EventIngressGateway(new SystemIngressClock(), sink);
    }

    private async Task TryPublishDeploymentReceiptAsync(string deploymentReceiptPath, string correlationId, CancellationToken cancellationToken)
    {
        if (_deployEventAdapter is null)
            return;

        try
        {
            await _deployEventAdapter.PublishLocalDeploymentReceiptAsync(deploymentReceiptPath, correlationId, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private async Task TryPublishLiveSessionAsync(string sessionPath, string correlationId, CancellationToken cancellationToken)
    {
        if (_deployEventAdapter is null || !File.Exists(sessionPath))
            return;

        try
        {
            await _deployEventAdapter.PublishLiveSessionAsync(sessionPath, correlationId, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private static string CreateCorrelationId(string buildId, string operation)
        => $"{operation}:{buildId[..Math.Min(12, buildId.Length)]}:{Guid.NewGuid():N}";

    private async Task<LiveLocalDeployment> AdoptLiveSessionInternalAsync(
        string workingDirectory,
        string sessionPath,
        string ownerTool,
        string ownerInstanceId,
        CancellationToken cancellationToken)
    {
        var json = await RunJsonCliAsync(
            "adopt-session",
            [
                "deploy",
                "local",
                "adopt",
                "--session",
                sessionPath,
                "--owner-tool",
                ownerTool,
                "--owner-instance-id",
                ownerInstanceId,
                "--owner-process-id",
                Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "--lease-seconds",
                StudioLeaseSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "--json"
            ],
            workingDirectory,
            cancellationToken).ConfigureAwait(false);
        var dto = DeserializeLiveSession(json);
        return MapLiveSession(dto, sessionPath, ownerInstanceId);
    }

    private async Task<LiveLocalDeployment> LoadLiveSessionAsync(
        string sessionPath,
        string currentStudioOwnerInstanceId,
        CancellationToken cancellationToken)
    {
        var json = await RunJsonCliAsync(
            "status-session",
            [
                "deploy",
                "local",
                "status",
                "--session",
                sessionPath,
                "--json"
            ],
            Path.GetDirectoryName(sessionPath)!,
            cancellationToken).ConfigureAwait(false);
        var dto = DeserializeLiveSession(json);
        return MapLiveSession(dto, sessionPath, currentStudioOwnerInstanceId);
    }

    private async Task<string> RunJsonCliAsync(
        string phase,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var lines = new List<string>();
        var result = await _processes.RunAsync(
            CreateCliRequest(arguments, workingDirectory),
            line =>
            {
                if (line.Kind == ProcessOutputKind.StdOut)
                    lines.Add(line.Text);
            },
            cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"{phase} failed with exit code {result.ExitCode}.");
        return string.Join(Environment.NewLine, lines);
    }

    private static LiveSessionDto DeserializeLiveSession(string json) =>
        JsonSerializer.Deserialize<LiveSessionDto>(json, JsonOptions)
        ?? throw new InvalidOperationException("Deployment session JSON is empty.");

    private static LiveLocalDeployment MapLiveSession(
        LiveSessionDto dto,
        string sessionPath,
        string currentStudioOwnerInstanceId)
    {
        if (!string.Equals(dto.SchemaVersion, "junexis.deployment-session/0.2", StringComparison.Ordinal))
            throw new InvalidOperationException($"Unsupported deployment session schema '{dto.SchemaVersion}'.");
        if (string.IsNullOrWhiteSpace(dto.BuildId))
            throw new InvalidOperationException("Deployment session is missing build identity.");
        if (dto.Targets.Count == 0)
            throw new InvalidOperationException("Deployment session has no targets.");

        var ownedByCurrentStudio = string.Equals(dto.Owner.Tool, StudioOwnerTool, StringComparison.Ordinal)
            && string.Equals(dto.Owner.InstanceId, currentStudioOwnerInstanceId, StringComparison.Ordinal);
        var leaseActive = DateTimeOffset.TryParse(dto.Owner.LeaseExpiresAtUtc, out var leaseExpiry)
            && leaseExpiry > DateTimeOffset.UtcNow;
        var canAdopt = string.Equals(dto.State, "running", StringComparison.Ordinal)
            && (!leaseActive || ownedByCurrentStudio);
        var targetStates = dto.Targets.Select(target => new DeploymentTargetStatus(
            target.Target,
            target.Status,
            target.Details)).ToArray();
        var targets = dto.Targets.Select(target => target.Target).ToArray();
        var summary =
            $"Live local session {dto.State} · owner {dto.Owner.Tool}:{dto.Owner.InstanceId} · lease {dto.Owner.LeaseExpiresAtUtc} · heartbeat {dto.LastHeartbeatUtc}.";
        return new LiveLocalDeployment(
            sessionPath,
            dto.BuildId,
            dto.State,
            dto.Owner.Tool,
            dto.Owner.InstanceId,
            dto.Owner.LeaseExpiresAtUtc,
            dto.LastHeartbeatUtc,
            ownedByCurrentStudio,
            canAdopt && !ownedByCurrentStudio,
            targets,
            targetStates,
            summary);
    }

    private static string GetOrCreateStudioOwnerInstanceId(string projectRoot)
    {
        var stateRoot = Path.Combine(projectRoot, ".junexis", "state", "deploy");
        Directory.CreateDirectory(stateRoot);
        var path = Path.Combine(stateRoot, "studio-owner-instance-id.txt");
        if (File.Exists(path))
            return File.ReadAllText(path).Trim();
        var value = Guid.NewGuid().ToString("N");
        File.WriteAllText(path, value);
        return value;
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }

    private sealed record ExecutionCapture(int ExitCode, IReadOnlyList<string> Transcript);

    private sealed record DeploymentReceiptDto
    {
        public string SchemaVersion { get; init; } = string.Empty;

        public string BundleId { get; init; } = string.Empty;

        public string BuildId { get; init; } = string.Empty;

        public string PackageReceiptPath { get; init; } = string.Empty;

        public string PreflightReportPath { get; init; } = string.Empty;

        public List<DeploymentTargetDto> Targets { get; init; } = [];

        public DeploymentLifecycleDto? Lifecycle { get; init; }
    }

    private sealed record DeploymentTargetDto
    {
        public string Target { get; init; } = string.Empty;

        public string Status { get; init; } = string.Empty;

        public string Details { get; init; } = string.Empty;
    }

    private sealed record DeploymentLifecycleDto
    {
        public string Status { get; init; } = string.Empty;

        public int? ExitCode { get; init; }

        public string Summary { get; init; } = string.Empty;

        public int DurationSeconds { get; init; }
    }

    private sealed record PreflightReportDto(
        string TechnicalStatus,
        string BundleId,
        string BuildId,
        IReadOnlyList<string> Targets,
        IReadOnlyList<PreflightCheckDto> Checks,
        MentorDto Mentor)
    {
        public AuthenticityDto Authenticity { get; init; } = new();
    }

    private sealed record AuthenticityDto
    {
        public string Status { get; init; } = "unsigned";

        public string Policy { get; init; } = "permissive";

        public bool PolicySatisfied { get; init; } = true;

        public string? Signer { get; init; }

        public string? PublicKeyFingerprintSha256 { get; init; }

        public string? SignaturePath { get; init; }
    }

    private sealed record PackageReceiptDto(
        string SchemaVersion,
        string BundleId,
        string BuildId,
        IReadOnlyList<string> Targets);

    private sealed record LiveSessionDto
    {
        public string SchemaVersion { get; init; } = string.Empty;

        public string BuildId { get; init; } = string.Empty;

        public string State { get; init; } = string.Empty;

        public string LastHeartbeatUtc { get; init; } = string.Empty;

        public LiveOwnerDto Owner { get; init; } = new();

        public List<LiveTargetDto> Targets { get; init; } = [];
    }

    private sealed record LiveOwnerDto
    {
        public string Tool { get; init; } = string.Empty;

        public string InstanceId { get; init; } = string.Empty;

        public string LeaseExpiresAtUtc { get; init; } = string.Empty;
    }

    private sealed record LiveTargetDto
    {
        public string Target { get; init; } = string.Empty;

        public string Status { get; init; } = string.Empty;

        public string Details { get; init; } = string.Empty;
    }

    private sealed record PreflightCheckDto(
        string Code,
        string Status,
        string? Target,
        string Message);

    private sealed record MentorDto(
        string Status,
        string Summary);

    private sealed record EmbeddedProbeDto
    {
        public string SchemaVersion { get; init; } = string.Empty;

        public string? ToolPath { get; init; }

        public bool ToolAvailable { get; init; }

        public bool ProbeSucceeded { get; init; }

        public string Message { get; init; } = string.Empty;

        public List<EmbeddedDeviceDto> Devices { get; init; } = [];
    }

    private sealed record EmbeddedDeviceDto
    {
        public string Port { get; init; } = string.Empty;

        public string? Description { get; init; }

        public string? HardwareId { get; init; }

        public string? SerialNumber { get; init; }

        public string? Manufacturer { get; init; }
    }

    private sealed record EmbeddedSelectionDto
    {
        public string SchemaVersion { get; init; } = string.Empty;

        public string Port { get; init; } = string.Empty;

        public string? Description { get; init; }

        public string? HardwareId { get; init; }

        public string? SerialNumber { get; init; }

        public string? Manufacturer { get; init; }
    }

    private sealed record EmbeddedFlashReceiptDto
    {
        public string SchemaVersion { get; init; } = string.Empty;

        public string LifecycleStatus { get; init; } = string.Empty;

        public string Summary { get; init; } = string.Empty;

        public string BundleId { get; init; } = string.Empty;

        public string BuildId { get; init; } = string.Empty;

        public string Port { get; init; } = string.Empty;
    }
}

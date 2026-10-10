namespace Junexis.Studio.Abstractions;

/// <summary>Describes whether a verified Junexis build can enter the deployment flow.</summary>
/// <param name="HasVerifiedBuild">Whether Studio currently has a verified latest-build receipt.</param>
/// <param name="CanPrepare">Whether the current build can be packaged and preflighted for deployment.</param>
/// <param name="StatusText">Human-readable readiness summary.</param>
/// <param name="BuildId">Verified build identity, when available.</param>
/// <param name="ProjectRoot">Owning Junexis project root.</param>
/// <param name="ReceiptPath">Path to the verified latest-build receipt.</param>
/// <param name="Targets">Targets declared in the verified build.</param>
/// <param name="DeployableTargets">Targets currently supported by Deploy Center.</param>
/// <param name="BlockedTargets">Targets intentionally excluded from the current deployment flow.</param>
public sealed record DeploymentReadiness(
    bool HasVerifiedBuild,
    bool CanPrepare,
    string StatusText,
    string? BuildId,
    string? ProjectRoot,
    string? ReceiptPath,
    IReadOnlyList<string> Targets,
    IReadOnlyList<string> DeployableTargets,
    IReadOnlyList<string> BlockedTargets);

/// <summary>One technical check produced by deployment preflight.</summary>
/// <param name="Code">Stable check code.</param>
/// <param name="Status">Check status such as passed, blocked, or info.</param>
/// <param name="Target">Optional target constrained by the check.</param>
/// <param name="Message">Human-readable explanation.</param>
public sealed record DeploymentPreflightCheck(
    string Code,
    string Status,
    string? Target,
    string Message);

/// <summary>Explicit System Bundle signing and deployment admission policy selected by the user.</summary>
/// <param name="Policy">Admission policy: permissive, signed, or trusted.</param>
/// <param name="SigningKeyPath">Optional external ECDSA P-256 private key PEM used during packaging.</param>
/// <param name="Signer">Optional human-readable signer name paired with <paramref name="SigningKeyPath"/>.</param>
/// <param name="TrustedSignerFingerprints">Explicit SHA-256 signer fingerprints admitted by the trusted policy.</param>
public sealed record DeploymentAuthenticityPolicy(
    string Policy,
    string? SigningKeyPath,
    string? Signer,
    IReadOnlyList<string> TrustedSignerFingerprints);

/// <summary>Last known local deployment state for one target.</summary>
/// <param name="Target">Target name such as server or report.</param>
/// <param name="Status">Target lifecycle status such as prepared, built, ready, failed, or cancelled.</param>
/// <param name="Details">Human-readable details restored from the deployment receipt.</param>
public sealed record DeploymentTargetStatus(
    string Target,
    string Status,
    string Details);

/// <summary>Result of packaging and preflighting one verified build for deployment.</summary>
/// <param name="BuildId">Verified build identity preserved through packaging and preflight.</param>
/// <param name="BundleId">Identity of the verified System Bundle.</param>
/// <param name="BundlePath">Path to the packaged System Bundle ZIP.</param>
/// <param name="PackageReceiptPath">Path to the package receipt bound to the System Bundle.</param>
/// <param name="PreflightReportPath">Path to the deterministic preflight report JSON.</param>
/// <param name="TechnicalStatus">Overall preflight status: ready or blocked.</param>
/// <param name="Targets">Targets covered by the prepared deployment state.</param>
/// <param name="Checks">Technical deployment checks emitted by preflight.</param>
/// <param name="MentorStatus">Non-blocking Mentor status associated with the preparation.</param>
/// <param name="MentorSummary">Compact Mentor summary for the prepared build.</param>
public sealed record DeploymentPreparationResult(
    string BuildId,
    string BundleId,
    string BundlePath,
    string PackageReceiptPath,
    string PreflightReportPath,
    string TechnicalStatus,
    IReadOnlyList<string> Targets,
    IReadOnlyList<DeploymentPreflightCheck> Checks,
    string MentorStatus,
    string MentorSummary)
{
    /// <summary>Observed bundle authenticity status: unsigned, verified, trusted, or invalid.</summary>
    public string AuthenticityStatus { get; init; } = "unsigned";

    /// <summary>Admission policy applied by deployment preflight.</summary>
    public string AuthenticityPolicy { get; init; } = "permissive";

    /// <summary>Whether the observed authenticity status satisfied the selected policy.</summary>
    public bool AuthenticityPolicySatisfied { get; init; } = true;

    /// <summary>Declared signer name when a valid signature was present.</summary>
    public string? Signer { get; init; }

    /// <summary>SHA-256 fingerprint of the valid signing public key.</summary>
    public string? SignerFingerprint { get; init; }

    /// <summary>Detached System Bundle signature path evaluated by preflight.</summary>
    public string? SignaturePath { get; init; }
}

/// <summary>Outcome of a bounded local deployment session started from Studio.</summary>
/// <param name="Success">Whether the local deployment command completed successfully.</param>
/// <param name="Cancelled">Whether Studio cancelled the local deployment process.</param>
/// <param name="LifecycleStatus">Normalized lifecycle status such as succeeded, failed, or cancelled.</param>
/// <param name="ExitCode">CLI exit code when the process finished normally.</param>
/// <param name="BuildId">Build identity deployed in the local session.</param>
/// <param name="BundlePath">System Bundle ZIP used by the local session.</param>
/// <param name="DeploymentReceiptPath">Persistent deployment receipt written by the local session.</param>
/// <param name="PreflightReportPath">Preflight report authorizing the local session.</param>
/// <param name="DurationSeconds">Bounded session duration requested for the local deployment.</param>
/// <param name="Targets">Targets launched in the bounded local session.</param>
/// <param name="TargetStates">Last known target-level states published for the local session.</param>
/// <param name="Summary">Compact outcome summary for the user interface.</param>
public sealed record LocalDeploymentResult(
    bool Success,
    bool Cancelled,
    string LifecycleStatus,
    int? ExitCode,
    string BuildId,
    string BundlePath,
    string DeploymentReceiptPath,
    string PreflightReportPath,
    int DurationSeconds,
    IReadOnlyList<string> Targets,
    IReadOnlyList<DeploymentTargetStatus> TargetStates,
    string Summary);

/// <summary>Persisted snapshot of the last bounded local deployment session for one verified build.</summary>
/// <param name="DeploymentReceiptPath">Deterministic path of the stored deployment receipt.</param>
/// <param name="BuildId">Build identity associated with the stored local deployment session.</param>
/// <param name="LifecycleStatus">Final lifecycle status such as succeeded, failed, or cancelled.</param>
/// <param name="ExitCode">CLI exit code recorded in the stored lifecycle.</param>
/// <param name="DurationSeconds">Recorded bounded session duration in seconds.</param>
/// <param name="Targets">Targets covered by the stored local deployment session.</param>
/// <param name="TargetStates">Last known target-level states restored from the deployment receipt.</param>
/// <param name="Summary">Human-readable lifecycle summary restored from the receipt.</param>
public sealed record StoredLocalDeployment(
    string DeploymentReceiptPath,
    string BuildId,
    string LifecycleStatus,
    int? ExitCode,
    int DurationSeconds,
    IReadOnlyList<string> Targets,
    IReadOnlyList<DeploymentTargetStatus> TargetStates,
    string Summary);

/// <summary>Live local deployment session discovered from deterministic deployment-session state.</summary>
/// <param name="SessionPath">Deterministic path of the live deployment session snapshot.</param>
/// <param name="BuildId">Build identity associated with the live local deployment session.</param>
/// <param name="State">Evaluated session state such as running, lost, stopped, or failed.</param>
/// <param name="OwnerTool">Logical owner tool currently recorded for the session lease.</param>
/// <param name="OwnerInstanceId">Stable owner instance identity currently recorded for the session lease.</param>
/// <param name="LeaseExpiresAtUtc">Current lease expiry in UTC ISO-8601 form.</param>
/// <param name="LastHeartbeatUtc">Last heartbeat timestamp in UTC ISO-8601 form.</param>
/// <param name="OwnedByCurrentStudio">Whether the evaluated session is currently owned by this Studio instance.</param>
/// <param name="CanAdopt">Whether Studio may explicitly adopt the session because no foreign active owner lease remains.</param>
/// <param name="Targets">Targets covered by the live session snapshot.</param>
/// <param name="TargetStates">Last evaluated target-level states restored from the session snapshot.</param>
/// <param name="Summary">Human-readable session summary for the user interface.</param>
public sealed record LiveLocalDeployment(
    string SessionPath,
    string BuildId,
    string State,
    string OwnerTool,
    string OwnerInstanceId,
    string LeaseExpiresAtUtc,
    string LastHeartbeatUtc,
    bool OwnedByCurrentStudio,
    bool CanAdopt,
    IReadOnlyList<string> Targets,
    IReadOnlyList<DeploymentTargetStatus> TargetStates,
    string Summary);

/// <summary>One embedded device candidate reported by the read-only PlatformIO probe.</summary>
/// <param name="Port">Stable serial port name used for explicit selection.</param>
/// <param name="Description">Optional device description.</param>
/// <param name="HardwareId">Optional hardware identifier.</param>
/// <param name="SerialNumber">Optional serial number.</param>
/// <param name="Manufacturer">Optional manufacturer name.</param>
public sealed record EmbeddedDeviceCandidate(
    string Port,
    string? Description,
    string? HardwareId,
    string? SerialNumber,
    string? Manufacturer);

/// <summary>Read-only embedded toolchain and device discovery result.</summary>
/// <param name="ToolAvailable">Whether PlatformIO was resolved.</param>
/// <param name="ProbeSucceeded">Whether device discovery completed successfully.</param>
/// <param name="ToolPath">Resolved PlatformIO path, when available.</param>
/// <param name="Message">Human-readable probe summary.</param>
/// <param name="Devices">Detected candidate devices; discovery never selects one automatically.</param>
public sealed record EmbeddedProbeResult(
    bool ToolAvailable,
    bool ProbeSucceeded,
    string? ToolPath,
    string Message,
    IReadOnlyList<EmbeddedDeviceCandidate> Devices);

/// <summary>Embedded package prepared from one verified project build.</summary>
/// <param name="BuildId">Verified build identity preserved by the package receipt.</param>
/// <param name="BundleId">Verified embedded System Bundle identity.</param>
/// <param name="BundlePath">Path to the embedded System Bundle ZIP.</param>
/// <param name="PackageReceiptPath">Path to the package receipt authorizing flash.</param>
public sealed record EmbeddedDeploymentPreparation(
    string BuildId,
    string BundleId,
    string BundlePath,
    string PackageReceiptPath)
{
    /// <summary>Authenticity policy that must still be enforced immediately before flash.</summary>
    public string AuthenticityPolicy { get; init; } = "permissive";

    /// <summary>Detached signature path created during embedded packaging, when present.</summary>
    public string? SignaturePath { get; init; }

    /// <summary>Explicit signer fingerprints admitted by the trusted policy.</summary>
    public IReadOnlyList<string> TrustedSignerFingerprints { get; init; } = [];
}

/// <summary>Explicit persisted selection of one currently detected embedded device.</summary>
/// <param name="SelectionPath">Path to the deterministic selection contract.</param>
/// <param name="Port">Selected serial port.</param>
/// <param name="Description">Optional selected device description.</param>
/// <param name="HardwareId">Optional hardware identifier bound by selection.</param>
/// <param name="SerialNumber">Optional serial number bound by selection.</param>
/// <param name="Manufacturer">Optional manufacturer bound by selection.</param>
public sealed record EmbeddedDeviceSelection(
    string SelectionPath,
    string Port,
    string? Description,
    string? HardwareId,
    string? SerialNumber,
    string? Manufacturer);

/// <summary>Outcome restored from the deterministic embedded flash receipt.</summary>
/// <param name="Success">Whether PlatformIO upload succeeded.</param>
/// <param name="LifecycleStatus">Receipt lifecycle status.</param>
/// <param name="Summary">Human-readable flash result.</param>
/// <param name="BuildId">Build identity flashed to the device.</param>
/// <param name="BundleId">Bundle identity flashed to the device.</param>
/// <param name="Port">Explicitly selected upload port.</param>
/// <param name="ReceiptPath">Path to the deterministic flash receipt.</param>
public sealed record EmbeddedFlashResult(
    bool Success,
    string LifecycleStatus,
    string Summary,
    string BuildId,
    string BundleId,
    string Port,
    string ReceiptPath);

/// <summary>Junexis deployment boundary used by Studio panels and commands.</summary>
public interface IDeploymentService
{
    /// <summary>Evaluates deployment readiness from the currently verified build receipt.</summary>
    /// <param name="receipt">Verified latest-build receipt, or <see langword="null"/> when no build is available.</param>
    /// <returns>Deployment readiness snapshot for the current Studio context.</returns>
    DeploymentReadiness Evaluate(LatestBuildReceipt? receipt);

    /// <summary>Packages and preflights one verified Junexis build for deployment.</summary>
    /// <param name="receipt">Verified latest-build receipt owning the project to prepare.</param>
    /// <param name="listener">Optional streaming listener for package and preflight output lines.</param>
    /// <param name="cancellationToken">Token used to cancel the external package/preflight processes.</param>
    /// <returns>Prepared bundle, receipt, and preflight report metadata.</returns>
    Task<DeploymentPreparationResult> PrepareAsync(
        LatestBuildReceipt receipt,
        Action<ProcessOutputLine>? listener = null,
        CancellationToken cancellationToken = default);

    /// <summary>Packages and preflights one verified build under an explicit authenticity policy.</summary>
    /// <param name="receipt">Verified latest-build receipt owning the project to prepare.</param>
    /// <param name="authenticity">Explicit signing and admission policy.</param>
    /// <param name="listener">Optional streaming listener for package and preflight output lines.</param>
    /// <param name="cancellationToken">Token used to cancel external processes.</param>
    /// <returns>Prepared bundle with integrity, authenticity, policy, and Mentor evidence.</returns>
    Task<DeploymentPreparationResult> PrepareAsync(
        LatestBuildReceipt receipt,
        DeploymentAuthenticityPolicy authenticity,
        Action<ProcessOutputLine>? listener = null,
        CancellationToken cancellationToken = default) =>
        PrepareAsync(receipt, listener, cancellationToken);

    /// <summary>Runs a bounded local deployment session for a prepared server/report package.</summary>
    /// <param name="preparation">Prepared deployment state produced by <see cref="PrepareAsync(LatestBuildReceipt, CancellationToken)"/>.</param>
    /// <param name="durationSeconds">Bounded session duration in seconds.</param>
    /// <param name="listener">Optional streaming listener for local deployment output lines.</param>
    /// <param name="cancellationToken">Token used to cancel the running local session.</param>
    /// <returns>Outcome of the bounded local deployment command.</returns>
    Task<LocalDeploymentResult> RunLocalAsync(
        DeploymentPreparationResult preparation,
        int durationSeconds,
        Action<ProcessOutputLine>? listener = null,
        CancellationToken cancellationToken = default);

    /// <summary>Loads the last persisted bounded local deployment receipt for the current verified build.</summary>
    /// <param name="receipt">Verified latest-build receipt driving the current Deploy Center context.</param>
    /// <param name="cancellationToken">Token used to cancel receipt loading.</param>
    /// <returns>Stored local deployment snapshot or <see langword="null"/> when no receipt exists yet.</returns>
    Task<StoredLocalDeployment?> TryLoadLastLocalAsync(
        LatestBuildReceipt receipt,
        CancellationToken cancellationToken = default);

    /// <summary>Loads an evaluated live local deployment session for the current verified build, renewing the lease when it already belongs to this Studio instance.</summary>
    /// <param name="receipt">Verified latest-build receipt driving the current Deploy Center context.</param>
    /// <param name="cancellationToken">Token used to cancel session status or renewal work.</param>
    /// <returns>Live local deployment snapshot or <see langword="null"/> when no resumable live session exists.</returns>
    Task<LiveLocalDeployment?> TryLoadLiveLocalAsync(
        LatestBuildReceipt receipt,
        CancellationToken cancellationToken = default);

    /// <summary>Explicitly adopts an orphaned live local deployment session for this Studio instance.</summary>
    /// <param name="receipt">Verified latest-build receipt driving the current Deploy Center context.</param>
    /// <param name="cancellationToken">Token used to cancel adopt work.</param>
    /// <returns>Adopted live local deployment session owned by this Studio instance.</returns>
    Task<LiveLocalDeployment> AdoptLiveLocalAsync(
        LatestBuildReceipt receipt,
        CancellationToken cancellationToken = default);

    /// <summary>Stops a live local deployment session owned by this Studio instance through the canonical deployment-session contract.</summary>
    /// <param name="receipt">Verified latest-build receipt driving the current Deploy Center context.</param>
    /// <param name="cancellationToken">Token used to cancel stop work.</param>
    /// <returns>Updated deployment session snapshot after the stop attempt.</returns>
    Task<LiveLocalDeployment> StopLiveLocalAsync(
        LatestBuildReceipt receipt,
        CancellationToken cancellationToken = default);

    /// <summary>Restores the prepared deployment state for the current verified build when deterministic artifacts still exist.</summary>
    /// <param name="receipt">Verified latest-build receipt driving the current Deploy Center context.</param>
    /// <param name="cancellationToken">Token used to cancel restore work.</param>
    /// <returns>Prepared deployment state that can be re-run locally, or <see langword="null"/> when no reusable state exists.</returns>
    Task<DeploymentPreparationResult?> TryRestorePreparationAsync(
        LatestBuildReceipt receipt,
        CancellationToken cancellationToken = default);

    /// <summary>Runs read-only PlatformIO discovery without selecting or mutating hardware.</summary>
    /// <param name="cancellationToken">Token used to cancel the probe process.</param>
    /// <returns>Toolchain and candidate device snapshot.</returns>
    Task<EmbeddedProbeResult> ProbeEmbeddedAsync(CancellationToken cancellationToken = default);

    /// <summary>Packages the embedded target from the selected verified project build.</summary>
    /// <param name="receipt">Verified project build containing an embedded target.</param>
    /// <param name="listener">Optional streaming package output listener.</param>
    /// <param name="cancellationToken">Token used to cancel packaging.</param>
    /// <returns>Verified embedded bundle and package receipt.</returns>
    Task<EmbeddedDeploymentPreparation> PrepareEmbeddedAsync(
        LatestBuildReceipt receipt,
        Action<ProcessOutputLine>? listener = null,
        CancellationToken cancellationToken = default);

    /// <summary>Packages the embedded target under an explicit authenticity policy.</summary>
    /// <param name="receipt">Verified project build containing an embedded target.</param>
    /// <param name="authenticity">Signing and admission policy preserved until flash.</param>
    /// <param name="listener">Optional streaming package output listener.</param>
    /// <param name="cancellationToken">Token used to cancel packaging.</param>
    /// <returns>Verified embedded bundle carrying its flash admission policy.</returns>
    Task<EmbeddedDeploymentPreparation> PrepareEmbeddedAsync(
        LatestBuildReceipt receipt,
        DeploymentAuthenticityPolicy authenticity,
        Action<ProcessOutputLine>? listener = null,
        CancellationToken cancellationToken = default) =>
        PrepareEmbeddedAsync(receipt, listener, cancellationToken);

    /// <summary>Persists one explicit device choice after a fresh CLI probe.</summary>
    /// <param name="receipt">Verified build owning deterministic deployment state.</param>
    /// <param name="device">Candidate explicitly selected by the user.</param>
    /// <param name="cancellationToken">Token used to cancel selection.</param>
    /// <returns>Persisted selection contract.</returns>
    Task<EmbeddedDeviceSelection> SelectEmbeddedAsync(
        LatestBuildReceipt receipt,
        EmbeddedDeviceCandidate device,
        CancellationToken cancellationToken = default);

    /// <summary>Flashes a verified embedded package to one explicitly selected device.</summary>
    /// <param name="preparation">Verified embedded package.</param>
    /// <param name="selection">Explicit persisted device selection.</param>
    /// <param name="listener">Optional streaming upload output listener.</param>
    /// <param name="cancellationToken">Token used to cancel flashing.</param>
    /// <returns>Deterministic flash receipt outcome.</returns>
    Task<EmbeddedFlashResult> FlashEmbeddedAsync(
        EmbeddedDeploymentPreparation preparation,
        EmbeddedDeviceSelection selection,
        Action<ProcessOutputLine>? listener = null,
        CancellationToken cancellationToken = default);
}

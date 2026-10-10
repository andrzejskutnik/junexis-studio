using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Host;

/// <summary>View model for the Junexis Deploy Center panel.</summary>
public sealed class DeployCenterViewModel : INotifyPropertyChanged
{
    private const string OutputChannel = "Deploy";
    private static readonly TimeSpan LeaseRenewalInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan LeaseWarningWindow = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan HeartbeatStaleWindow = TimeSpan.FromSeconds(20);

    private readonly IDeploymentService _deployments;
    private readonly ILatestBuildReceiptService _latestBuilds;
    private readonly IWorkspace _workspace;
    private readonly IActiveDocumentService _activeDocument;
    private readonly IOutput _output;
    private readonly SynchronizationContext? _synchronizationContext;
    private readonly StringBuilder _log = new();
    private LatestBuildReceipt? _receipt;
    private DeploymentReadiness _readiness = new(
        false,
        false,
        "Build a Junexis project to prepare deployment.",
        null,
        null,
        null,
        [],
        [],
        []);
    private DeploymentPreparationResult? _preparation;
    private EmbeddedDeploymentPreparation? _embeddedPreparation;
    private EmbeddedDeviceSelection? _embeddedSelection;
    private EmbeddedDeviceCandidate? _selectedEmbeddedDevice;
    private LiveLocalDeployment? _liveDeployment;
    private CancellationTokenSource? _operationCts;
    private CancellationTokenSource? _leaseRenewalCts;
    private Task? _leaseRenewalTask;
    private string _statusText = "Build a Junexis project to prepare deployment.";
    private string _buildText = "No verified build receipt loaded.";
    private string _targetsText = "Targets: unavailable";
    private string _preflightText = "Preflight has not run yet.";
    private string _authenticityText = "Bundle authenticity has not been evaluated.";
    private string _selectedAuthenticityPolicy = "permissive";
    private string _signingKeyPath = string.Empty;
    private string _signerName = string.Empty;
    private string _trustedSignerFingerprintsText = string.Empty;
    private string _localDeploymentText = "Local deployment has not run yet.";
    private string _recoveryModeTitle = "No restored state";
    private string _recoveryText = "No deployment state has been restored yet.";
    private string _embeddedStatusText = "Embedded flow has not been started.";
    private string _logText = string.Empty;
    private bool _isBusy;
    private bool _isDeploying;
    private int _durationSeconds = 5;
    private IReadOnlyList<DeploymentPreflightCheck> _checks = [];
    private IReadOnlyList<DeploymentTargetStatus> _localTargetStates = [];
    private IReadOnlyList<EmbeddedDeviceCandidate> _embeddedDevices = [];
    private bool _isEmbeddedFlashConfirmed;

    /// <summary>Creates the Deploy Center view model.</summary>
    public DeployCenterViewModel(
        IDeploymentService deployments,
        ILatestBuildReceiptService latestBuilds,
        IWorkspace workspace,
        IActiveDocumentService activeDocument,
        IOutput output)
    {
        _deployments = deployments ?? throw new ArgumentNullException(nameof(deployments));
        _latestBuilds = latestBuilds ?? throw new ArgumentNullException(nameof(latestBuilds));
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _activeDocument = activeDocument ?? throw new ArgumentNullException(nameof(activeDocument));
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _synchronizationContext = SynchronizationContext.Current;
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Overall readiness and operation summary.</summary>
    public string StatusText
    {
        get => _statusText;
        private set => SetField(ref _statusText, value);
    }

    /// <summary>Verified build currently driving Deploy Center.</summary>
    public string BuildText
    {
        get => _buildText;
        private set => SetField(ref _buildText, value);
    }

    /// <summary>Targets visible to the deployment workflow.</summary>
    public string TargetsText
    {
        get => _targetsText;
        private set => SetField(ref _targetsText, value);
    }

    /// <summary>Latest deployment preflight status.</summary>
    public string PreflightText
    {
        get => _preflightText;
        private set => SetField(ref _preflightText, value);
    }

    /// <summary>Latest bounded local deployment status.</summary>
    public string LocalDeploymentText
    {
        get => _localDeploymentText;
        private set => SetField(ref _localDeploymentText, value);
    }

    /// <summary>Short recovery mode label shown above the detailed recovery explanation.</summary>
    public string RecoveryModeTitle
    {
        get => _recoveryModeTitle;
        private set => SetField(ref _recoveryModeTitle, value);
    }

    /// <summary>Explains whether refresh restored only a historical snapshot or a rerunnable prepared deployment state.</summary>
    public string RecoveryText
    {
        get => _recoveryText;
        private set => SetField(ref _recoveryText, value);
    }

    /// <summary>Streaming deployment log shown in the panel.</summary>
    public string LogText
    {
        get => _logText;
        private set => SetField(ref _logText, value);
    }

    /// <summary>Seconds for the bounded local deployment session.</summary>
    public int DurationSeconds
    {
        get => _durationSeconds;
        set
        {
            var normalized = Math.Clamp(value, 1, 300);
            if (!SetField(ref _durationSeconds, normalized))
                return;
            OnPropertyChanged(nameof(DurationText));
        }
    }

    /// <summary>Editable duration text bound by the view.</summary>
    public string DurationText
    {
        get => _durationSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        set
        {
            if (!int.TryParse(value, out var seconds))
                seconds = 5;
            DurationSeconds = seconds;
        }
    }

    /// <summary>Latest technical deployment checks.</summary>
    public IReadOnlyList<DeploymentPreflightCheck> Checks
    {
        get => _checks;
        private set => SetField(ref _checks, value);
    }

    /// <summary>Latest per-target states reported by the bounded local deployment receipt.</summary>
    public IReadOnlyList<DeploymentTargetStatus> LocalTargetStates
    {
        get => _localTargetStates;
        private set => SetField(ref _localTargetStates, value);
    }

    /// <summary>Authenticity result for the current prepared System Bundle.</summary>
    public string AuthenticityText
    {
        get => _authenticityText;
        private set => SetField(ref _authenticityText, value);
    }

    /// <summary>Authenticity policies offered by Deploy Center.</summary>
    public IReadOnlyList<string> AuthenticityPolicies { get; } = ["permissive", "signed", "trusted"];

    /// <summary>Explicit System Bundle admission policy selected for the next preparation.</summary>
    public string SelectedAuthenticityPolicy
    {
        get => _selectedAuthenticityPolicy;
        set => SetField(
            ref _selectedAuthenticityPolicy,
            AuthenticityPolicies.Contains(value, StringComparer.Ordinal) ? value : "permissive");
    }

    /// <summary>Optional external ECDSA P-256 private-key path used to sign the next package.</summary>
    public string SigningKeyPath
    {
        get => _signingKeyPath;
        set => SetField(ref _signingKeyPath, value ?? string.Empty);
    }

    /// <summary>Human-readable signer name paired with the external signing key.</summary>
    public string SignerName
    {
        get => _signerName;
        set => SetField(ref _signerName, value ?? string.Empty);
    }

    /// <summary>Whitespace-, comma-, or semicolon-separated trusted signer SHA-256 fingerprints.</summary>
    public string TrustedSignerFingerprintsText
    {
        get => _trustedSignerFingerprintsText;
        set => SetField(ref _trustedSignerFingerprintsText, value ?? string.Empty);
    }

    /// <summary>Devices returned by the latest read-only embedded probe.</summary>
    public IReadOnlyList<EmbeddedDeviceCandidate> EmbeddedDevices
    {
        get => _embeddedDevices;
        private set => SetField(ref _embeddedDevices, value);
    }

    /// <summary>Candidate device explicitly highlighted by the user.</summary>
    public EmbeddedDeviceCandidate? SelectedEmbeddedDevice
    {
        get => _selectedEmbeddedDevice;
        set
        {
            if (!SetField(ref _selectedEmbeddedDevice, value))
                return;
            _embeddedSelection = null;
            IsEmbeddedFlashConfirmed = false;
            OnPropertyChanged(nameof(CanSelectEmbedded));
            OnPropertyChanged(nameof(CanFlashEmbedded));
        }
    }

    /// <summary>Current embedded probe, selection, packaging, or flash summary.</summary>
    public string EmbeddedStatusText
    {
        get => _embeddedStatusText;
        private set => SetField(ref _embeddedStatusText, value);
    }

    /// <summary>Explicit destructive-operation confirmation for the currently selected device.</summary>
    public bool IsEmbeddedFlashConfirmed
    {
        get => _isEmbeddedFlashConfirmed;
        set
        {
            if (SetField(ref _isEmbeddedFlashConfirmed, value))
                OnPropertyChanged(nameof(CanFlashEmbedded));
        }
    }

    /// <summary>Whether any deployment operation is running.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetField(ref _isBusy, value))
                return;
            OnPropertyChanged(nameof(CanPrepare));
            OnPropertyChanged(nameof(CanRunLocal));
            OnPropertyChanged(nameof(CanStopLocal));
            OnPropertyChanged(nameof(CanAdoptLive));
            OnPropertyChanged(nameof(CanProbeEmbedded));
            OnPropertyChanged(nameof(CanPrepareEmbedded));
            OnPropertyChanged(nameof(CanSelectEmbedded));
            OnPropertyChanged(nameof(CanFlashEmbedded));
        }
    }

    /// <summary>Whether a bounded local deployment is currently active.</summary>
    public bool IsDeploying
    {
        get => _isDeploying;
        private set
        {
            if (!SetField(ref _isDeploying, value))
                return;
            OnPropertyChanged(nameof(CanPrepare));
            OnPropertyChanged(nameof(CanRunLocal));
            OnPropertyChanged(nameof(CanStopLocal));
        }
    }

    /// <summary>Whether Deploy Center can package and preflight the current build.</summary>
    public bool CanPrepare => !IsBusy && _readiness.CanPrepare;

    /// <summary>Whether Deploy Center can start a bounded local deployment.</summary>
    public bool CanRunLocal => !IsBusy
        && _preparation is not null
        && _liveDeployment is null
        && string.Equals(_preparation.TechnicalStatus, "ready", StringComparison.Ordinal)
        && _preparation.Targets.Any(target => target is "server" or "report");

    /// <summary>Whether the active bounded local deployment can be stopped.</summary>
    public bool CanStopLocal => (IsDeploying && _operationCts is not null)
        || (!IsBusy && _liveDeployment is { OwnedByCurrentStudio: true });

    /// <summary>Whether Studio can explicitly adopt a discovered live deployment session.</summary>
    public bool CanAdoptLive => !IsBusy && _liveDeployment is { CanAdopt: true };

    /// <summary>Whether read-only embedded device discovery can run.</summary>
    public bool CanProbeEmbedded => !IsBusy
        && _receipt is { Mode: "project" }
        && _receipt.Targets.Contains("embedded", StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether the current verified build can be packaged for embedded flash.</summary>
    public bool CanPrepareEmbedded => CanProbeEmbedded;

    /// <summary>Whether one probed device can be persisted as the explicit flash target.</summary>
    public bool CanSelectEmbedded => !IsBusy && _receipt is not null && SelectedEmbeddedDevice is not null;

    /// <summary>Whether all technical and human confirmation gates allow embedded flash.</summary>
    public bool CanFlashEmbedded => !IsBusy
        && _embeddedPreparation is not null
        && _embeddedSelection is not null
        && IsEmbeddedFlashConfirmed;

    /// <summary>Applies one verified build receipt to Deploy Center.</summary>
    public void ApplyLatestBuild(LatestBuildReceipt? receipt)
    {
        StopLeaseRenewal();
        _receipt = receipt;
        _preparation = null;
        _embeddedPreparation = null;
        _embeddedSelection = null;
        _selectedEmbeddedDevice = null;
        _liveDeployment = null;
        Update(() =>
        {
            Checks = [];
            LocalTargetStates = [];
            EmbeddedDevices = [];
            IsEmbeddedFlashConfirmed = false;
            _readiness = _deployments.Evaluate(receipt);
            StatusText = _readiness.StatusText;
            BuildText = receipt is null
                ? "No verified build receipt loaded."
                : $"Build {receipt.BuildId[..12]} · {receipt.Mode} · {receipt.ReceiptPath}";
            TargetsText = FormatTargets(_readiness);
            PreflightText = "Preflight has not run yet.";
            AuthenticityText = "Bundle authenticity has not been evaluated.";
            LocalDeploymentText = "Local deployment has not run yet.";
            EmbeddedStatusText = receipt is not null
                && receipt.Targets.Contains("embedded", StringComparer.OrdinalIgnoreCase)
                    ? "Embedded target detected. Probe devices before making an explicit selection."
                    : "Current verified build has no embedded target.";
            SetRecoveryState("No restored state", "No deployment state has been restored yet.");
            OnPropertyChanged(nameof(CanPrepare));
            OnPropertyChanged(nameof(CanRunLocal));
            OnPropertyChanged(nameof(CanAdoptLive));
            OnPropertyChanged(nameof(CanStopLocal));
            OnPropertyChanged(nameof(SelectedEmbeddedDevice));
            OnPropertyChanged(nameof(CanProbeEmbedded));
            OnPropertyChanged(nameof(CanPrepareEmbedded));
            OnPropertyChanged(nameof(CanSelectEmbedded));
            OnPropertyChanged(nameof(CanFlashEmbedded));
        });
    }

    /// <summary>Reloads the verified latest-build receipt for the current workspace context.</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var workspaceRoot = _workspace.RootPath;
        if (string.IsNullOrWhiteSpace(workspaceRoot))
        {
            ApplyLatestBuild(null);
            return;
        }

        try
        {
            var receipt = await _latestBuilds.TryLoadForContextAsync(
                workspaceRoot,
                _activeDocument.ActivePath,
                cancellationToken).ConfigureAwait(false);
            ApplyLatestBuild(receipt);
            if (receipt is not null)
            {
                var stored = await _deployments.TryLoadLastLocalAsync(receipt, cancellationToken).ConfigureAwait(false);
                if (stored is not null)
                {
                    Update(() =>
                    {
                        LocalDeploymentText = FormatLocalDeploymentSnapshot(
                            "Last local deployment",
                            stored.LifecycleStatus,
                            stored.ExitCode,
                            stored.DurationSeconds,
                            stored.Targets,
                            stored.Summary,
                            stored.DeploymentReceiptPath);
                        LocalTargetStates = stored.TargetStates;
                        SetRecoveryState(
                            "Historical snapshot",
                            "Recovered the last deployment snapshot from receipt history.");
                    });
                }

                var live = await _deployments.TryLoadLiveLocalAsync(receipt, cancellationToken).ConfigureAwait(false);
                if (live is not null)
                {
                    _liveDeployment = live;
                    if (live.OwnedByCurrentStudio)
                        StartLeaseRenewal();
                    else
                        StopLeaseRenewal();
                    Update(() =>
                    {
                        LocalDeploymentText = FormatLiveDeploymentSnapshot(live);
                        LocalTargetStates = live.TargetStates;
                        StatusText = $"Recovered live local deployment session for build {live.BuildId[..12]}.";
                        if (live.OwnedByCurrentStudio)
                        {
                            SetRecoveryState(
                                "Live resumable session",
                                BuildLiveRecoveryMessage(
                                    live,
                                    "Recovered a live local deployment session owned by this Studio instance and renewed its lease."));
                        }
                        else if (live.CanAdopt)
                        {
                            SetRecoveryState(
                                "Live resumable session",
                                BuildLiveRecoveryMessage(
                                    live,
                                    "Recovered a live local deployment session with live target evidence; use Adopt Session to transfer the lease to this Studio instance."));
                        }
                        else
                        {
                            SetRecoveryState(
                                "Live resumable session",
                                BuildLiveRecoveryMessage(
                                    live,
                                    $"Live session is currently owned by {live.OwnerTool}:{live.OwnerInstanceId}; Studio shows status but cannot adopt while that lease remains active."));
                        }
                        OnPropertyChanged(nameof(CanRunLocal));
                        OnPropertyChanged(nameof(CanAdoptLive));
                        OnPropertyChanged(nameof(CanStopLocal));
                    });
                }
                else
                {
                    StopLeaseRenewal();
                }

                var restoredPreparation = await _deployments.TryRestorePreparationAsync(receipt, cancellationToken)
                    .ConfigureAwait(false);
                if (restoredPreparation is not null)
                {
                    _preparation = restoredPreparation;
                    Update(() =>
                    {
                        Checks = restoredPreparation.Checks;
                        PreflightText =
                            $"Preflight {restoredPreparation.TechnicalStatus} · {string.Join(", ", restoredPreparation.Targets)} · Mentor {restoredPreparation.MentorStatus}.";
                        AuthenticityText = FormatAuthenticity(restoredPreparation);
                        if (_liveDeployment is null)
                        {
                            StatusText = $"Restored prepared deployment state for build {restoredPreparation.BuildId[..12]}.";
                            SetRecoveryState(
                                "Rerunnable prepared state",
                                "Recovered a rerunnable prepared deployment state; Run Local can start without a new Prepare.");
                        }
                        OnPropertyChanged(nameof(CanRunLocal));
                    });
                }
                else if (stored is not null && _liveDeployment is null)
                {
                    Update(() =>
                    {
                        SetRecoveryState(
                            "Snapshot only",
                            "Recovered snapshot only; rerunning local deployment still requires Prepare.");
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Update(() =>
            {
                ApplyLatestBuild(null);
                StatusText = $"Cannot refresh deployment readiness: {ex.Message}";
            });
            _output.Write(OutputChannel, StatusText, OutputSeverity.Warning);
        }
    }

    /// <summary>Packages and preflights the current verified build for deployment.</summary>
    public async Task PrepareAsync()
    {
        if (_receipt is null)
            return;

        using var cts = BeginOperation(isDeploying: false);
        Update(() =>
        {
            StatusText = "Packaging and preflighting the verified Junexis build…";
            PreflightText = "Running package and deployment preflight…";
        });

        try
        {
            var preparation = await _deployments.PrepareAsync(
                _receipt,
                new DeploymentAuthenticityPolicy(
                    SelectedAuthenticityPolicy,
                    string.IsNullOrWhiteSpace(SigningKeyPath) ? null : SigningKeyPath.Trim(),
                    string.IsNullOrWhiteSpace(SignerName) ? null : SignerName.Trim(),
                    TrustedSignerFingerprintsText.Split(
                        [' ', '\t', '\r', '\n', ',', ';'],
                        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)),
                AppendLog,
                cts.Token).ConfigureAwait(false);
            _preparation = preparation;
            Update(() =>
            {
                Checks = preparation.Checks;
                StatusText = $"Prepared bundle {preparation.BundleId[..12]} for build {preparation.BuildId[..12]}.";
                PreflightText =
                    $"Preflight {preparation.TechnicalStatus} · {string.Join(", ", preparation.Targets)} · Mentor {preparation.MentorStatus}.";
                AuthenticityText = FormatAuthenticity(preparation);
                LocalDeploymentText = string.Equals(preparation.TechnicalStatus, "ready", StringComparison.Ordinal)
                    ? "Local deployment can start for the prepared server/report targets."
                    : "Local deployment is blocked until preflight becomes technically ready.";
                OnPropertyChanged(nameof(CanRunLocal));
            });
            _output.Write(OutputChannel, PreflightText, OutputSeverity.Info);
        }
        catch (OperationCanceledException)
        {
            Update(() =>
            {
                StatusText = "Deployment preparation cancelled.";
                PreflightText = "Package and preflight were cancelled.";
            });
            _output.Write(OutputChannel, PreflightText, OutputSeverity.Warning);
        }
        catch (Exception ex)
        {
            Update(() =>
            {
                StatusText = $"Deployment preparation failed: {ex.Message}";
                PreflightText = StatusText;
                AuthenticityText = "Bundle authenticity could not be evaluated.";
                Checks = [];
                _preparation = null;
                OnPropertyChanged(nameof(CanRunLocal));
            });
            _output.Write(OutputChannel, StatusText, OutputSeverity.Error);
        }
        finally
        {
            EndOperation(cts);
        }
    }

    /// <summary>Runs a bounded local deployment session for the prepared bundle.</summary>
    public async Task RunLocalAsync()
    {
        if (_preparation is null)
            return;

        using var cts = BeginOperation(isDeploying: true);
        Update(() =>
        {
            StatusText = "Starting bounded local deployment…";
            LocalDeploymentText =
                $"Running bounded local deployment for {DurationSeconds}s on {string.Join(", ", _preparation.Targets)}.";
        });

        try
        {
            var result = await _deployments.RunLocalAsync(
                _preparation,
                DurationSeconds,
                AppendLog,
                cts.Token).ConfigureAwait(false);
            Update(() =>
            {
                StatusText = result.Summary;
                LocalDeploymentText = FormatLocalDeploymentSnapshot(
                    "Local deployment",
                    result.LifecycleStatus,
                    result.ExitCode,
                    result.DurationSeconds,
                    result.Targets,
                    result.Summary,
                    result.DeploymentReceiptPath);
                LocalTargetStates = result.TargetStates;
                _liveDeployment = null;
                StopLeaseRenewal();
                SetRecoveryState(
                    "Current Studio session",
                    "Current local deployment session was started from this Studio session.");
                OnPropertyChanged(nameof(CanAdoptLive));
                OnPropertyChanged(nameof(CanStopLocal));
            });
            _output.Write(
                OutputChannel,
                LocalDeploymentText,
                result.Success ? OutputSeverity.Info : OutputSeverity.Warning);
        }
        catch (Exception ex)
        {
            Update(() =>
            {
                StatusText = $"Local deployment failed: {ex.Message}";
                LocalDeploymentText = StatusText;
            });
            _output.Write(OutputChannel, StatusText, OutputSeverity.Error);
        }
        finally
        {
            EndOperation(cts);
        }
    }

    /// <summary>Stops the active bounded local deployment or the currently owned live deployment session.</summary>
    public async Task StopLocalAsync()
    {
        if (IsDeploying && _operationCts is not null)
        {
            _operationCts.Cancel();
            return;
        }

        if (_receipt is null || _liveDeployment is null || !_liveDeployment.OwnedByCurrentStudio)
            return;

        using var cts = BeginOperation(isDeploying: false);
        Update(() => StatusText = "Stopping live local deployment session…");
        try
        {
            var stopped = await _deployments.StopLiveLocalAsync(_receipt, cts.Token);
            StopLeaseRenewal();
            _liveDeployment = null;
            Update(() =>
            {
                LocalDeploymentText = FormatLiveDeploymentSnapshot(stopped);
                LocalTargetStates = stopped.TargetStates;
                StatusText = $"Stopped live local deployment session for build {stopped.BuildId[..12]}.";
                SetRecoveryState(
                    "Historical snapshot",
                    "Stopped the live local deployment session through the canonical deployment-session contract; runtime is no longer resumable.");
                OnPropertyChanged(nameof(CanRunLocal));
                OnPropertyChanged(nameof(CanAdoptLive));
                OnPropertyChanged(nameof(CanStopLocal));
            });
            _output.Write(OutputChannel, LocalDeploymentText, OutputSeverity.Info);
        }
        catch (Exception ex)
        {
            Update(() => StatusText = $"Live session stop failed: {ex.Message}");
            _output.Write(OutputChannel, StatusText, OutputSeverity.Error);
        }
        finally
        {
            EndOperation(cts);
        }
    }

    /// <summary>Explicitly adopts one discovered live deployment session for this Studio instance.</summary>
    public async Task AdoptLiveAsync()
    {
        if (_receipt is null || _liveDeployment is null || !_liveDeployment.CanAdopt)
            return;

        using var cts = BeginOperation(isDeploying: false);
        Update(() => StatusText = "Adopting live local deployment session…");
        try
        {
            var adopted = await _deployments.AdoptLiveLocalAsync(_receipt, cts.Token).ConfigureAwait(false);
            _liveDeployment = adopted;
            StartLeaseRenewal();
            Update(() =>
            {
                LocalDeploymentText = FormatLiveDeploymentSnapshot(adopted);
                LocalTargetStates = adopted.TargetStates;
                StatusText = $"Adopted live local deployment session for build {adopted.BuildId[..12]}.";
                SetRecoveryState(
                    "Live resumable session",
                    BuildLiveRecoveryMessage(
                        adopted,
                        "This Studio instance adopted the live local deployment session and now owns the current lease."));
                OnPropertyChanged(nameof(CanAdoptLive));
                OnPropertyChanged(nameof(CanStopLocal));
            });
            _output.Write(OutputChannel, LocalDeploymentText, OutputSeverity.Info);
        }
        catch (Exception ex)
        {
            Update(() => StatusText = $"Live session adopt failed: {ex.Message}");
            _output.Write(OutputChannel, StatusText, OutputSeverity.Error);
        }
        finally
        {
            EndOperation(cts);
        }
    }

    /// <summary>Runs read-only PlatformIO discovery for the verified embedded build.</summary>
    public async Task ProbeEmbeddedAsync()
    {
        if (!CanProbeEmbedded)
            return;
        using var cts = BeginOperation(isDeploying: false);
        Update(() => EmbeddedStatusText = "Probing PlatformIO devices without selecting or flashing hardware…");
        try
        {
            var result = await _deployments.ProbeEmbeddedAsync(cts.Token).ConfigureAwait(false);
            _embeddedSelection = null;
            Update(() =>
            {
                EmbeddedDevices = result.Devices;
                SelectedEmbeddedDevice = null;
                EmbeddedStatusText = result.Devices.Count == 0
                    ? result.Message
                    : $"{result.Message} Select exactly one device to create an auditable selection contract.";
                OnPropertyChanged(nameof(CanSelectEmbedded));
                OnPropertyChanged(nameof(CanFlashEmbedded));
            });
            _output.Write(OutputChannel, EmbeddedStatusText, OutputSeverity.Info);
        }
        catch (Exception ex)
        {
            Update(() =>
            {
                EmbeddedDevices = [];
                SelectedEmbeddedDevice = null;
                EmbeddedStatusText = $"Embedded probe failed: {ex.Message}";
            });
            _output.Write(OutputChannel, EmbeddedStatusText, OutputSeverity.Error);
        }
        finally
        {
            EndOperation(cts);
        }
    }

    /// <summary>Packages the embedded target while preserving the verified Build ID.</summary>
    public async Task PrepareEmbeddedAsync()
    {
        if (_receipt is null || !CanPrepareEmbedded)
            return;
        using var cts = BeginOperation(isDeploying: false);
        Update(() =>
        {
            IsEmbeddedFlashConfirmed = false;
            EmbeddedStatusText = "Packaging the verified embedded target…";
        });
        try
        {
            var preparation = await _deployments.PrepareEmbeddedAsync(
                _receipt,
                new DeploymentAuthenticityPolicy(
                    SelectedAuthenticityPolicy,
                    string.IsNullOrWhiteSpace(SigningKeyPath) ? null : SigningKeyPath.Trim(),
                    string.IsNullOrWhiteSpace(SignerName) ? null : SignerName.Trim(),
                    TrustedSignerFingerprintsText.Split(
                        [' ', '\t', '\r', '\n', ',', ';'],
                        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)),
                AppendLog,
                cts.Token).ConfigureAwait(false);
            _embeddedPreparation = preparation;
            Update(() =>
            {
                EmbeddedStatusText =
                    $"Embedded bundle {preparation.BundleId[..12]} prepared for build {preparation.BuildId[..12]}.";
                OnPropertyChanged(nameof(CanFlashEmbedded));
            });
            _output.Write(OutputChannel, EmbeddedStatusText, OutputSeverity.Info);
        }
        catch (Exception ex)
        {
            _embeddedPreparation = null;
            Update(() =>
            {
                EmbeddedStatusText = $"Embedded preparation failed: {ex.Message}";
                OnPropertyChanged(nameof(CanFlashEmbedded));
            });
            _output.Write(OutputChannel, EmbeddedStatusText, OutputSeverity.Error);
        }
        finally
        {
            EndOperation(cts);
        }
    }

    /// <summary>Persists the device explicitly selected in the embedded candidate list.</summary>
    public async Task SelectEmbeddedAsync()
    {
        if (_receipt is null || SelectedEmbeddedDevice is null || !CanSelectEmbedded)
            return;
        var candidate = SelectedEmbeddedDevice;
        using var cts = BeginOperation(isDeploying: false);
        Update(() => EmbeddedStatusText = $"Verifying and selecting {candidate.Port} through a fresh probe…");
        try
        {
            var selection = await _deployments.SelectEmbeddedAsync(
                _receipt,
                candidate,
                cts.Token).ConfigureAwait(false);
            _embeddedSelection = selection;
            Update(() =>
            {
                IsEmbeddedFlashConfirmed = false;
                EmbeddedStatusText =
                    $"Selected {selection.Port}. Review the device identity and explicitly confirm before flashing.";
                OnPropertyChanged(nameof(CanFlashEmbedded));
            });
            _output.Write(OutputChannel, EmbeddedStatusText, OutputSeverity.Info);
        }
        catch (Exception ex)
        {
            _embeddedSelection = null;
            Update(() =>
            {
                IsEmbeddedFlashConfirmed = false;
                EmbeddedStatusText = $"Embedded selection failed: {ex.Message}";
                OnPropertyChanged(nameof(CanFlashEmbedded));
            });
            _output.Write(OutputChannel, EmbeddedStatusText, OutputSeverity.Error);
        }
        finally
        {
            EndOperation(cts);
        }
    }

    /// <summary>Flashes the prepared embedded bundle after explicit user confirmation.</summary>
    public async Task FlashEmbeddedAsync()
    {
        if (_embeddedPreparation is null || _embeddedSelection is null || !CanFlashEmbedded)
            return;
        var preparation = _embeddedPreparation;
        var selection = _embeddedSelection;
        using var cts = BeginOperation(isDeploying: false);
        Update(() => EmbeddedStatusText = $"Flashing verified build {preparation.BuildId[..12]} to {selection.Port}…");
        try
        {
            var result = await _deployments.FlashEmbeddedAsync(
                preparation,
                selection,
                AppendLog,
                cts.Token).ConfigureAwait(false);
            Update(() =>
            {
                IsEmbeddedFlashConfirmed = false;
                EmbeddedStatusText =
                    $"Embedded flash {result.LifecycleStatus}: {result.Port} · {result.Summary} · receipt {result.ReceiptPath}";
            });
            _output.Write(
                OutputChannel,
                EmbeddedStatusText,
                result.Success ? OutputSeverity.Info : OutputSeverity.Error);
        }
        catch (Exception ex)
        {
            Update(() =>
            {
                IsEmbeddedFlashConfirmed = false;
                EmbeddedStatusText = $"Embedded flash failed: {ex.Message}";
            });
            _output.Write(OutputChannel, EmbeddedStatusText, OutputSeverity.Error);
        }
        finally
        {
            EndOperation(cts);
        }
    }

    /// <summary>Stops background renewal tasks owned by this view model.</summary>
    public void Shutdown() => StopLeaseRenewal();

    private CancellationTokenSource BeginOperation(bool isDeploying)
    {
        _operationCts?.Cancel();
        _operationCts?.Dispose();
        var cts = new CancellationTokenSource();
        _operationCts = cts;
        Update(() =>
        {
            IsBusy = true;
            IsDeploying = isDeploying;
        });
        return cts;
    }

    private void EndOperation(CancellationTokenSource cts)
    {
        Update(() =>
        {
            if (ReferenceEquals(_operationCts, cts))
            {
                _operationCts.Dispose();
                _operationCts = null;
            }
            IsBusy = false;
            IsDeploying = false;
        });
    }

    private void AppendLog(ProcessOutputLine line)
    {
        Update(() =>
        {
            if (_log.Length > 0)
                _log.AppendLine();
            _log.Append(line.Text);
            if (_log.Length > 16000)
                _log.Remove(0, _log.Length - 16000);
            LogText = _log.ToString();
        });
    }

    private static string FormatTargets(DeploymentReadiness readiness)
    {
        if (!readiness.HasVerifiedBuild)
            return "Targets: unavailable";
        var deployed = readiness.DeployableTargets.Count == 0
            ? "none"
            : string.Join(", ", readiness.DeployableTargets);
        var separate = readiness.BlockedTargets.Count == 0
            ? "none"
            : string.Join(", ", readiness.BlockedTargets);
        return $"Targets: {string.Join(", ", readiness.Targets)} · local: {deployed} · separate flow: {separate}";
    }

    private static string FormatAuthenticity(DeploymentPreparationResult preparation)
    {
        var signer = preparation.Signer is null
            ? "no verified signer"
            : $"{preparation.Signer} · {preparation.SignerFingerprint}";
        return $"Authenticity {preparation.AuthenticityStatus} · policy {preparation.AuthenticityPolicy} · " +
            $"{(preparation.AuthenticityPolicySatisfied ? "accepted" : "blocked")} · {signer}.";
    }

    private static string FormatLocalDeploymentSnapshot(
        string label,
        string lifecycleStatus,
        int? exitCode,
        int durationSeconds,
        IReadOnlyList<string> targets,
        string summary,
        string receiptPath)
    {
        var renderedExitCode = exitCode?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "n/a";
        return $"{label} {lifecycleStatus} · exit {renderedExitCode} · {durationSeconds}s · {string.Join(", ", targets)} · {summary} Receipt: {receiptPath}";
    }

    private static string FormatLiveDeploymentSnapshot(LiveLocalDeployment live) =>
        $"Live local deployment {live.State} · owner {live.OwnerTool}:{live.OwnerInstanceId} · {DescribeLease(live.LeaseExpiresAtUtc)} · {DescribeHeartbeat(live.LastHeartbeatUtc)} · {string.Join(", ", live.Targets)} · Session: {live.SessionPath}";

    private static string BuildLiveRecoveryMessage(LiveLocalDeployment live, string prefix) =>
        $"{prefix} {DescribeLease(live.LeaseExpiresAtUtc)}. {DescribeHeartbeat(live.LastHeartbeatUtc)}.";

    private static string DescribeLease(string leaseExpiresAtUtc)
    {
        if (!DateTimeOffset.TryParse(leaseExpiresAtUtc, out var leaseExpiry))
            return $"Lease expiry is unavailable ({leaseExpiresAtUtc}).";

        var remaining = leaseExpiry - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero)
            return $"Lease expired {FormatAge(-remaining)} ago";
        if (remaining <= LeaseWarningWindow)
            return $"Lease renews soon in {FormatAge(remaining)}";
        return $"Lease valid for {FormatAge(remaining)}";
    }

    private static string DescribeHeartbeat(string lastHeartbeatUtc)
    {
        if (!DateTimeOffset.TryParse(lastHeartbeatUtc, out var heartbeat))
            return $"Heartbeat age is unavailable ({lastHeartbeatUtc})";

        var age = DateTimeOffset.UtcNow - heartbeat;
        if (age < TimeSpan.Zero)
            age = TimeSpan.Zero;
        if (age >= HeartbeatStaleWindow)
            return $"Heartbeat is stale ({FormatAge(age)} old)";
        return $"Heartbeat is fresh ({FormatAge(age)} old)";
    }

    private static string FormatAge(TimeSpan value)
    {
        var roundedSeconds = Math.Max(0, (int)Math.Round(value.TotalSeconds, MidpointRounding.AwayFromZero));
        return roundedSeconds == 1 ? "1s" : $"{roundedSeconds}s";
    }

    private void StartLeaseRenewal()
    {
        if (_receipt is null || _liveDeployment is not { OwnedByCurrentStudio: true })
            return;
        if (_leaseRenewalTask is not null && !_leaseRenewalTask.IsCompleted)
            return;

        _leaseRenewalCts?.Dispose();
        _leaseRenewalCts = new CancellationTokenSource();
        _leaseRenewalTask = RenewLeaseLoopAsync(_receipt, _leaseRenewalCts.Token);
    }

    private void StopLeaseRenewal()
    {
        _leaseRenewalCts?.Cancel();
        _leaseRenewalCts?.Dispose();
        _leaseRenewalCts = null;
        _leaseRenewalTask = null;
    }

    private async Task RenewLeaseLoopAsync(LatestBuildReceipt receipt, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(LeaseRenewalInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                var live = await _deployments.TryLoadLiveLocalAsync(receipt, cancellationToken);
                if (live is null)
                {
                    Update(() =>
                    {
                        _liveDeployment = null;
                        SetRecoveryState(
                            "Historical snapshot",
                            "Live local deployment session is no longer resumable; the deterministic snapshot remains visible until the next refresh.");
                        OnPropertyChanged(nameof(CanRunLocal));
                        OnPropertyChanged(nameof(CanAdoptLive));
                        OnPropertyChanged(nameof(CanStopLocal));
                    });
                    StopLeaseRenewal();
                    return;
                }

                _liveDeployment = live;
                Update(() =>
                {
                    LocalDeploymentText = FormatLiveDeploymentSnapshot(live);
                    LocalTargetStates = live.TargetStates;
                    OnPropertyChanged(nameof(CanAdoptLive));
                    OnPropertyChanged(nameof(CanStopLocal));
                });
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Update(() =>
            {
                StatusText = $"Automatic lease renewal failed: {ex.Message}";
                if (_liveDeployment is not null)
                {
                    _liveDeployment = _liveDeployment with
                    {
                        OwnedByCurrentStudio = false,
                        CanAdopt = false,
                    };
                }
                RecoveryText = $"Automatic lease renewal failed; refresh or adopt again to continue. {ex.Message}";
                OnPropertyChanged(nameof(CanAdoptLive));
                OnPropertyChanged(nameof(CanStopLocal));
            });
            _output.Write(OutputChannel, StatusText, OutputSeverity.Warning);
            StopLeaseRenewal();
        }
    }

    private void SetRecoveryState(string title, string message)
    {
        RecoveryModeTitle = title;
        RecoveryText = message;
    }

    private void Update(Action action)
    {
        if (_synchronizationContext is null || SynchronizationContext.Current == _synchronizationContext)
            action();
        else
            _synchronizationContext.Post(_ => action(), null);
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

internal sealed class DeployCenterExtension : IJunexisExtension
{
    private const string OutputChannel = "Deploy";

    private CancellationTokenSource? _cancellation;
    private IEventBus? _eventBus;
    private DeployCenterViewModel? _viewModel;

    public string Id => "junexis.deploy.center";

    public void Initialize(IHostContext context)
    {
        if (!context.Capabilities.TryGet<IDeploymentService>(out var deployments) || deployments is null
            || !context.Capabilities.TryGet<ILatestBuildReceiptService>(out var latestBuilds) || latestBuilds is null)
        {
            context.Output.Write(OutputChannel, "Deploy Center unavailable: required deployment capabilities were not registered.", OutputSeverity.Warning);
            return;
        }

        _cancellation = new CancellationTokenSource();
        _eventBus = context.EventBus;
        _viewModel = new DeployCenterViewModel(
            deployments,
            latestBuilds,
            context.Workspace,
            context.ActiveDocument,
            context.Output);
        context.Panels.RegisterPanel(Id, PanelType.Tool, () => _viewModel, "Deploy", DockGroup.Right);
        context.Commands.Register(
            "deploy.center.open",
            () => context.EventBus.Publish(new ActivatePanelRequested(Id)),
            menuPath: "Deploy/Open Deploy Center",
            displayName: "Open Deploy Center",
            category: "Deploy");
        context.Commands.Register(
            "deploy.center.prepare",
            () => _ = _viewModel.PrepareAsync(),
            menuPath: "Deploy/Run Preflight",
            displayName: "Run Preflight",
            category: "Deploy");
        context.Commands.Register(
            "deploy.center.local",
            () => _ = _viewModel.RunLocalAsync(),
            menuPath: "Deploy/Run Local Deployment",
            displayName: "Run Local Deployment",
            category: "Deploy");
        context.Commands.Register(
            "deploy.center.stop",
            () => _ = _viewModel.StopLocalAsync(),
            menuPath: "Deploy/Stop Local Deployment",
            displayName: "Stop Local Deployment",
            category: "Deploy");
        _eventBus.Subscribe<LatestBuildUpdatedEvent>(OnLatestBuildUpdated);
        _ = _viewModel.RefreshAsync(_cancellation.Token);
    }

    public void Shutdown()
    {
        _eventBus?.Unsubscribe<LatestBuildUpdatedEvent>(OnLatestBuildUpdated);
        _eventBus = null;
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = null;
        _viewModel?.Shutdown();
        _viewModel = null;
    }

    private void OnLatestBuildUpdated(LatestBuildUpdatedEvent update) =>
        _viewModel?.ApplyLatestBuild(update.Receipt);
}

using Junexis.Studio.Abstractions;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Junexis.Studio.Host;

/// <summary>
/// Wspólny model górnego paska Build/Run/Debug, oparty na launch profiles i Doctorze.
/// </summary>
public sealed class WorkbenchRunViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly ILaunchProfileRegistry _profiles;
    private readonly IToolchainDoctorService _doctor;
    private readonly ICommandRegistry _commands;
    private readonly IActiveDocumentService _activeDocument;
    private readonly IWorkspace _workspace;
    private readonly IEventBus _eventBus;
    private readonly IOutput _output;
    private readonly IDebugSessionService _debugSession;
    private readonly IWorkbenchOperationService _operations;
    private readonly SynchronizationContext? _synchronizationContext;
    private LaunchProfile? _selectedProfile;
    private string _workspaceName = "No folder";
    private string _activeFileName = "No active file";
    private string _statusText = "Select a source file";
    private bool _isBusy;
    private bool _isDebugStopped;
    private bool _disposed;

    public WorkbenchRunViewModel(
        ILaunchProfileRegistry profiles,
        IToolchainDoctorService doctor,
        ICommandRegistry commands,
        IActiveDocumentService activeDocument,
        IWorkspace workspace,
        IEventBus eventBus,
        IOutput output,
        IDebugSessionService debugSession,
        IWorkbenchOperationService operations)
    {
        _profiles = profiles;
        _doctor = doctor;
        _commands = commands;
        _activeDocument = activeDocument;
        _workspace = workspace;
        _eventBus = eventBus;
        _output = output;
        _debugSession = debugSession;
        _operations = operations;
        _synchronizationContext = SynchronizationContext.Current;

        Profiles = profiles.GetProfiles();
        _activeDocument.ActiveDocumentChanged += OnActiveDocumentChanged;
        _workspace.FolderOpened += OnWorkspaceOpened;
        _eventBus.Subscribe<ToolchainCapabilitiesUpdatedEvent>(OnToolchainsUpdated);
        _debugSession.SnapshotChanged += OnDebugSnapshotChanged;
        _operations.SnapshotChanged += OnOperationSnapshotChanged;
        RefreshWorkspace();
        RefreshActiveDocument();
        OnDebugSnapshotChanged(_debugSession.Snapshot);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<LaunchProfile> Profiles { get; }

    public LaunchProfile? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (SetField(ref _selectedProfile, value))
            {
                RefreshReadiness();
                RaiseOperationProperties();
            }
        }
    }

    public string WorkspaceName
    {
        get => _workspaceName;
        private set => SetField(ref _workspaceName, value);
    }

    public string ActiveFileName
    {
        get => _activeFileName;
        private set => SetField(ref _activeFileName, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetField(ref _statusText, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
                RaiseOperationProperties();
        }
    }

    public bool CanBuild => !IsBusy && SelectedProfile?.BuildCommandId is not null;
    public bool CanRun => !IsBusy && SelectedProfile?.RunCommandId is not null;
    public bool CanDebug => !IsBusy && SelectedProfile?.DebugCommandId is not null;
    public bool CanStop =>
        !IsBusy
        && (_operations.Snapshot.IsActive
            || _debugSession.Snapshot.State is not DebugSessionState.Inactive
                and not DebugSessionState.Terminated
                and not DebugSessionState.Failed);
    public bool IsDebugStopped
    {
        get => _isDebugStopped;
        private set => SetField(ref _isDebugStopped, value);
    }

    public Task BuildAsync() => ExecuteAsync(LaunchOperation.Build);
    public Task RunAsync() => ExecuteAsync(LaunchOperation.Run);
    public Task DebugAsync() => ExecuteAsync(LaunchOperation.Debug);

    public void Stop()
    {
        if (!CanStop)
            return;
        if (_commands.CanExecute("build.stop"))
            _commands.Execute("build.stop");
        StatusText = "Stop requested";
    }

    public void OpenOutput() => _eventBus.Publish(new ActivatePanelRequested("junexis.output.filtered"));
    public void OpenProblems() => _eventBus.Publish(new ActivatePanelRequested("problems"));
    public void OpenWhy() => _eventBus.Publish(new ActivatePanelRequested("junexis.narrative.why"));

    public async Task ExecuteAsync(LaunchOperation operation, CancellationToken cancellationToken = default)
    {
        if (IsBusy || SelectedProfile is not { } profile)
            return;

        IsBusy = true;
        try
        {
            var snapshot = _doctor.LatestSnapshot;
            if (snapshot is null)
            {
                StatusText = "Checking toolchain…";
                snapshot = await _doctor.CheckAsync(cancellationToken);
                _eventBus.Publish(new ToolchainCapabilitiesUpdatedEvent(snapshot));
            }

            var validation = _profiles.Validate(profile, operation, snapshot);
            if (!validation.IsValid || validation.CommandId is null)
            {
                StatusText = validation.Message;
                _output.Write("Workbench", validation.Message, OutputSeverity.Warning);
                _eventBus.Publish(new ActivatePanelRequested("junexis.toolchains.doctor"));
                return;
            }

            if (!_commands.CanExecute(validation.CommandId))
            {
                StatusText = $"Command unavailable: {validation.CommandId}";
                _output.Write("Workbench", StatusText, OutputSeverity.Warning);
                return;
            }

            StatusText = $"{operation}: {profile.DisplayName}";
            _commands.Execute(validation.CommandId);
        }
        catch (OperationCanceledException)
        {
            StatusText = $"{operation} cancelled";
        }
        catch (Exception ex)
        {
            StatusText = $"{operation} failed: {ex.Message}";
            _output.Write("Workbench", StatusText, OutputSeverity.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _activeDocument.ActiveDocumentChanged -= OnActiveDocumentChanged;
        _workspace.FolderOpened -= OnWorkspaceOpened;
        _eventBus.Unsubscribe<ToolchainCapabilitiesUpdatedEvent>(OnToolchainsUpdated);
        _debugSession.SnapshotChanged -= OnDebugSnapshotChanged;
        _operations.SnapshotChanged -= OnOperationSnapshotChanged;
    }

    private void OnActiveDocumentChanged() => RefreshActiveDocument();
    private void OnWorkspaceOpened(string _) => RefreshWorkspace();
    private void OnToolchainsUpdated(ToolchainCapabilitiesUpdatedEvent _) => RefreshReadiness();

    private void OnOperationSnapshotChanged(WorkbenchOperationSnapshot snapshot)
    {
        if (_synchronizationContext is not null
            && SynchronizationContext.Current != _synchronizationContext)
        {
            _synchronizationContext.Post(_ => ApplyOperationSnapshot(snapshot), null);
            return;
        }
        ApplyOperationSnapshot(snapshot);
    }

    private void ApplyOperationSnapshot(WorkbenchOperationSnapshot snapshot)
    {
        StatusText = snapshot.State switch
        {
            WorkbenchOperationState.Starting => snapshot.Status,
            WorkbenchOperationState.Running => snapshot.Status,
            WorkbenchOperationState.Succeeded => $"{snapshot.Status} · {snapshot.DurationMs} ms",
            WorkbenchOperationState.Failed => $"{snapshot.Status} · {snapshot.DiagnosticCount} diagnostics",
            WorkbenchOperationState.Cancelled => $"{snapshot.Status} · {snapshot.DurationMs} ms",
            _ => "Ready",
        };
        RaiseOperationProperties();
        if (snapshot.State == WorkbenchOperationState.Failed)
        {
            _eventBus.Publish(new ActivatePanelRequested(
                snapshot.DiagnosticCount > 0 ? "problems" : "junexis.output.filtered"));
        }
    }

    private void OnDebugSnapshotChanged(DebugSessionSnapshot snapshot)
    {
        if (_synchronizationContext is not null
            && SynchronizationContext.Current != _synchronizationContext)
        {
            _synchronizationContext.Post(_ => ApplyDebugSnapshot(snapshot), null);
            return;
        }
        ApplyDebugSnapshot(snapshot);
    }

    private void ApplyDebugSnapshot(DebugSessionSnapshot snapshot)
    {
        IsDebugStopped = snapshot.State == DebugSessionState.Stopped;
        OnPropertyChanged(nameof(CanStop));
        if (snapshot.State is not DebugSessionState.Inactive)
            StatusText = $"Debug: {snapshot.Status}";
    }

    private void RefreshWorkspace()
    {
        var root = _workspace.RootPath;
        WorkspaceName = string.IsNullOrWhiteSpace(root)
            ? "No folder"
            : new DirectoryInfo(root).Name;
    }

    private void RefreshActiveDocument()
    {
        var path = _activeDocument.ActivePath;
        ActiveFileName = string.IsNullOrWhiteSpace(path) ? "No active file" : Path.GetFileName(path);
        SelectedProfile = _profiles.FindForPath(path);
        if (SelectedProfile is null)
            StatusText = string.IsNullOrWhiteSpace(path)
                ? "Select a source file"
                : "No launch profile for active file";
    }

    private void RefreshReadiness()
    {
        if (SelectedProfile is not { } profile)
        {
            StatusText = "Select a source file";
            return;
        }

        var snapshot = _doctor.LatestSnapshot;
        if (snapshot is null)
        {
            StatusText = $"{profile.DisplayName} · checking toolchain";
            return;
        }

        var validations = new[]
        {
            _profiles.Validate(profile, LaunchOperation.Build, snapshot),
            _profiles.Validate(profile, LaunchOperation.Run, snapshot),
            _profiles.Validate(profile, LaunchOperation.Debug, snapshot),
        };
        var supported = validations.Where(item => item.CommandId is not null).ToArray();
        var missing = supported.SelectMany(item => item.MissingCapabilities)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        StatusText = missing.Length == 0
            ? $"{profile.DisplayName} · ready"
            : $"{profile.DisplayName} · missing {string.Join(", ", missing)}";
    }

    private void RaiseOperationProperties()
    {
        OnPropertyChanged(nameof(CanBuild));
        OnPropertyChanged(nameof(CanRun));
        OnPropertyChanged(nameof(CanDebug));
        OnPropertyChanged(nameof(CanStop));
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

internal sealed class WorkbenchRunExtension : IJunexisExtension
{
    private WorkbenchRunViewModel? _viewModel;

    public string Id => "junexis.workbench.run";

    public void Initialize(IHostContext context)
    {
        if (!context.Capabilities.TryGet<ILaunchProfileRegistry>(out var profiles)
            || profiles is null
            || !context.Capabilities.TryGet<IToolchainDoctorService>(out var doctor)
            || doctor is null
            || !context.Capabilities.TryGet<IDebugSessionService>(out var debug)
            || debug is null
            || !context.Capabilities.TryGet<IWorkbenchOperationService>(out var operations)
            || operations is null)
            return;

        _viewModel = new WorkbenchRunViewModel(
            profiles,
            doctor,
            context.Commands,
            context.ActiveDocument,
            context.Workspace,
            context.EventBus,
            context.Output,
            debug,
            operations);
        context.Capabilities.Register(_viewModel);
    }

    public void Shutdown()
    {
        _viewModel?.Dispose();
        _viewModel = null;
    }
}

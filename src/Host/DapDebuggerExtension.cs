using System.ComponentModel;
using System.Runtime.CompilerServices;
using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Host;

/// <summary>View model for Debug Adapter Protocol session controls and state.</summary>
public sealed class DebugPanelViewModel : INotifyPropertyChanged
{
    private readonly IDebugSessionService _session;
    private readonly IBreakpointService _breakpoints;
    private readonly IActiveDocumentService _activeDocument;
    private readonly IEventBus _eventBus;
    private readonly IOutput _output;
    private readonly IWorkspace? _workspace;
    private readonly IDebugConfigurationService? _debugConfigurations;
    private readonly IDebugProcessService? _debugProcesses;
    private readonly IDebugTargetAssessmentService? _debugTargetAssessment;
    private readonly Func<DebugLaunchConfiguration, Task>? _startConfiguration;
    private readonly SynchronizationContext? _synchronizationContext;
    private DebugSessionSnapshot _snapshot;
    private IReadOnlyList<DebugSourceBreakpoint> _sourceBreakpoints;
    private IReadOnlyList<DebugBreakpointStatus> _breakpointStatuses;
    private IReadOnlyList<DebugScope> _scopes = [];
    private IReadOnlyList<DebugVariable> _variables = [];
    private IReadOnlyList<DebugInspectionNode> _variableTree = [];
    private readonly object _watchGate = new();
    private readonly List<string> _watchExpressions = [];
    private IReadOnlyList<DebugEvaluation> _watches = [];
    private IReadOnlyList<DebugInspectionNode> _watchTree = [];
    private string _breakpointLineText = "1";
    private DebugSourceBreakpoint? _selectedBreakpoint;
    private string _breakpointConditionText = string.Empty;
    private string _breakpointHitConditionText = string.Empty;
    private string _breakpointLogMessageText = string.Empty;
    private string _exceptionBreakpointFiltersText = string.Empty;
    private string _supportedExceptionBreakpointFiltersText = "Adapter filters: unavailable";
    private IReadOnlyList<DebugConsoleEntry> _debugConsoleEntries = [];
    private readonly List<string> _debugConsoleHistory = [];
    private int _debugConsoleHistoryIndex;
    private string _debugConsoleInput = string.Empty;
    private DebugInspectionNode? _selectedVariableNode;
    private string _variableValueText = string.Empty;
    private IReadOnlyList<DebugDataBreakpoint> _dataBreakpoints = [];
    private DebugDataBreakpoint? _selectedDataBreakpoint;
    private string _dataBreakpointAccessTypeText = "write";
    private string _dataBreakpointConditionText = string.Empty;
    private string _dataBreakpointHitConditionText = string.Empty;
    private string _debugCapabilitiesText = "Variable editing and data breakpoints unavailable";
    private IReadOnlyList<DebugLaunchConfiguration> _configurations = [];
    private DebugLaunchConfiguration? _selectedConfiguration;
    private IReadOnlyList<DebugProcessInfo> _processes = [];
    private DebugProcessInfo? _selectedProcess;
    private string _configurationNameText = string.Empty;
    private string _configurationProfileText = "cpp";
    private string _configurationRequestText = "Launch";
    private string _configurationProgramText = string.Empty;
    private string _configurationProcessIdText = string.Empty;
    private string _configurationWorkingDirectoryText = string.Empty;
    private string _configurationArgumentsText = string.Empty;
    private bool _configurationStopOnEntry;
    private DebugTargetAssessment? _selectedTargetAssessment;
    private string _debugTargetNarrativeText = "Select a process to see profile and Narrative evidence.";
    private string _watchExpressionText = string.Empty;
    private int? _selectedFrameId;
    private int _refreshVersion;

    /// <summary>Creates a DAP debugger panel view model.</summary>
    /// <param name="session">Debug session service.</param>
    /// <param name="breakpoints">Shared source breakpoint store.</param>
    /// <param name="activeDocument">Active editor document.</param>
    /// <param name="eventBus">Studio navigation event bus.</param>
    /// <param name="output">Studio output log.</param>
    /// <param name="workspace">Optional workspace used to persist watch expressions.</param>
    /// <param name="debugConfigurations">Optional persisted launch and attach configurations.</param>
    /// <param name="debugProcesses">Optional local attach-process provider.</param>
    /// <param name="startConfiguration">Optional callback that starts the selected configuration.</param>
    /// <param name="debugTargetAssessment">Optional evidence-based process and profile correlator.</param>
    public DebugPanelViewModel(
        IDebugSessionService session,
        IBreakpointService breakpoints,
        IActiveDocumentService activeDocument,
        IEventBus eventBus,
        IOutput output,
        IWorkspace? workspace = null,
        IDebugConfigurationService? debugConfigurations = null,
        IDebugProcessService? debugProcesses = null,
        Func<DebugLaunchConfiguration, Task>? startConfiguration = null,
        IDebugTargetAssessmentService? debugTargetAssessment = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _breakpoints = breakpoints ?? throw new ArgumentNullException(nameof(breakpoints));
        _activeDocument = activeDocument ?? throw new ArgumentNullException(nameof(activeDocument));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _workspace = workspace;
        _debugConfigurations = debugConfigurations;
        _debugProcesses = debugProcesses;
        _startConfiguration = startConfiguration;
        _debugTargetAssessment = debugTargetAssessment;
        _snapshot = session.Snapshot;
        _sourceBreakpoints = breakpoints.GetBreakpoints();
        _breakpointStatuses = session.BreakpointStatuses;
        UpdateSupportedExceptionFilters();
        _synchronizationContext = SynchronizationContext.Current;
        _session.SnapshotChanged += OnSnapshotChanged;
        _session.OutputReceived += OnOutputReceived;
        _session.BreakpointStatusesChanged += OnBreakpointStatusesChanged;
        _breakpoints.BreakpointsChanged += OnBreakpointsChanged;
        if (_workspace is not null)
        {
            _workspace.FolderOpened += OnWorkspaceOpened;
            LoadStoredWatches();
        }
        if (_debugConfigurations is not null)
        {
            _debugConfigurations.ConfigurationsChanged += OnDebugConfigurationsChanged;
            ReloadDebugConfigurations();
        }
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Latest debugger state.</summary>
    public DebugSessionSnapshot Snapshot
    {
        get => _snapshot;
        private set => SetField(ref _snapshot, value);
    }

    /// <summary>Current source breakpoints.</summary>
    public IReadOnlyList<DebugSourceBreakpoint> SourceBreakpoints
    {
        get => _sourceBreakpoints;
        private set => SetField(ref _sourceBreakpoints, value);
    }

    /// <summary>Latest verification results returned by the debug adapter.</summary>
    public IReadOnlyList<DebugBreakpointStatus> BreakpointStatuses
    {
        get => _breakpointStatuses;
        private set => SetField(ref _breakpointStatuses, value);
    }

    /// <summary>Scopes loaded for the selected stack frame.</summary>
    public IReadOnlyList<DebugScope> Scopes
    {
        get => _scopes;
        private set => SetField(ref _scopes, value);
    }

    /// <summary>Variables loaded for the selected scope.</summary>
    public IReadOnlyList<DebugVariable> Variables
    {
        get => _variables;
        private set => SetField(ref _variables, value);
    }

    /// <summary>Expandable tree for variables in the selected scope.</summary>
    public IReadOnlyList<DebugInspectionNode> VariableTree
    {
        get => _variableTree;
        private set => SetField(ref _variableTree, value);
    }

    /// <summary>Current watch expressions and their latest results.</summary>
    public IReadOnlyList<DebugEvaluation> Watches
    {
        get => _watches;
        private set => SetField(ref _watches, value);
    }

    /// <summary>Expandable tree for evaluated watch expressions.</summary>
    public IReadOnlyList<DebugInspectionNode> WatchTree
    {
        get => _watchTree;
        private set => SetField(ref _watchTree, value);
    }

    /// <summary>One-based line entered for breakpoint toggling.</summary>
    public string BreakpointLineText
    {
        get => _breakpointLineText;
        set => SetField(ref _breakpointLineText, value);
    }

    /// <summary>Breakpoint selected for condition, hit-count, or logpoint editing.</summary>
    public DebugSourceBreakpoint? SelectedBreakpoint
    {
        get => _selectedBreakpoint;
        set
        {
            if (EqualityComparer<DebugSourceBreakpoint?>.Default.Equals(_selectedBreakpoint, value))
                return;
            _selectedBreakpoint = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedBreakpoint)));
            if (value is null)
                return;
            BreakpointLineText = value.Line.ToString(System.Globalization.CultureInfo.InvariantCulture);
            BreakpointConditionText = value.Condition ?? string.Empty;
            BreakpointHitConditionText = value.HitCondition ?? string.Empty;
            BreakpointLogMessageText = value.LogMessage ?? string.Empty;
        }
    }

    /// <summary>Optional expression that must evaluate to true before stopping.</summary>
    public string BreakpointConditionText
    {
        get => _breakpointConditionText;
        set => SetField(ref _breakpointConditionText, value);
    }

    /// <summary>Optional adapter-specific breakpoint hit-count expression.</summary>
    public string BreakpointHitConditionText
    {
        get => _breakpointHitConditionText;
        set => SetField(ref _breakpointHitConditionText, value);
    }

    /// <summary>Optional logpoint message; a non-empty message makes the adapter log without stopping.</summary>
    public string BreakpointLogMessageText
    {
        get => _breakpointLogMessageText;
        set => SetField(ref _breakpointLogMessageText, value);
    }

    /// <summary>Comma-separated adapter exception filters to enable.</summary>
    public string ExceptionBreakpointFiltersText
    {
        get => _exceptionBreakpointFiltersText;
        set => SetField(ref _exceptionBreakpointFiltersText, value);
    }

    /// <summary>Human-readable exception filters advertised by the active adapter.</summary>
    public string SupportedExceptionBreakpointFiltersText
    {
        get => _supportedExceptionBreakpointFiltersText;
        private set => SetField(ref _supportedExceptionBreakpointFiltersText, value);
    }

    /// <summary>Retained expression and result history for the Debug Console.</summary>
    public IReadOnlyList<DebugConsoleEntry> DebugConsoleEntries
    {
        get => _debugConsoleEntries;
        private set => SetField(ref _debugConsoleEntries, value);
    }

    /// <summary>Current Debug Console expression or command.</summary>
    public string DebugConsoleInput
    {
        get => _debugConsoleInput;
        set => SetField(ref _debugConsoleInput, value);
    }

    /// <summary>Variable selected for setVariable or a data breakpoint.</summary>
    public DebugInspectionNode? SelectedVariableNode
    {
        get => _selectedVariableNode;
        set
        {
            SetField(ref _selectedVariableNode, value);
            if (value is not null)
                VariableValueText = value.Value;
        }
    }

    /// <summary>New value expression for the selected variable.</summary>
    public string VariableValueText
    {
        get => _variableValueText;
        set => SetField(ref _variableValueText, value);
    }

    /// <summary>Active data breakpoints configured in the adapter.</summary>
    public IReadOnlyList<DebugDataBreakpoint> DataBreakpoints
    {
        get => _dataBreakpoints;
        private set => SetField(ref _dataBreakpoints, value);
    }

    /// <summary>Data breakpoint selected for removal.</summary>
    public DebugDataBreakpoint? SelectedDataBreakpoint
    {
        get => _selectedDataBreakpoint;
        set => SetField(ref _selectedDataBreakpoint, value);
    }

    /// <summary>Requested data breakpoint access type.</summary>
    public string DataBreakpointAccessTypeText
    {
        get => _dataBreakpointAccessTypeText;
        set => SetField(ref _dataBreakpointAccessTypeText, value);
    }

    /// <summary>Optional data breakpoint condition.</summary>
    public string DataBreakpointConditionText
    {
        get => _dataBreakpointConditionText;
        set => SetField(ref _dataBreakpointConditionText, value);
    }

    /// <summary>Optional data breakpoint hit condition.</summary>
    public string DataBreakpointHitConditionText
    {
        get => _dataBreakpointHitConditionText;
        set => SetField(ref _dataBreakpointHitConditionText, value);
    }

    /// <summary>Human-readable adapter support for variable mutation and data breakpoints.</summary>
    public string DebugCapabilitiesText
    {
        get => _debugCapabilitiesText;
        private set => SetField(ref _debugCapabilitiesText, value);
    }

    /// <summary>Persisted launch and attach configurations.</summary>
    public IReadOnlyList<DebugLaunchConfiguration> Configurations
    {
        get => _configurations;
        private set => SetField(ref _configurations, value);
    }

    /// <summary>Configuration selected in the Debug panel.</summary>
    public DebugLaunchConfiguration? SelectedConfiguration
    {
        get => _selectedConfiguration;
        set
        {
            if (EqualityComparer<DebugLaunchConfiguration?>.Default.Equals(_selectedConfiguration, value))
                return;
            SetField(ref _selectedConfiguration, value);
            if (value is null)
                return;
            _debugConfigurations?.Select(value.Id);
            PopulateConfigurationEditor(value);
        }
    }

    /// <summary>Local processes available as attach targets.</summary>
    public IReadOnlyList<DebugProcessInfo> Processes
    {
        get => _processes;
        private set => SetField(ref _processes, value);
    }

    /// <summary>Local process selected for an attach configuration.</summary>
    public DebugProcessInfo? SelectedProcess
    {
        get => _selectedProcess;
        set
        {
            SetField(ref _selectedProcess, value);
            if (value is not null)
                ConfigurationProcessIdText = value.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
            UpdateDebugTargetAssessment();
        }
    }

    /// <summary>Latest evidence-based assessment of the selected attach process.</summary>
    public DebugTargetAssessment? SelectedTargetAssessment
    {
        get => _selectedTargetAssessment;
        private set => SetField(ref _selectedTargetAssessment, value);
    }

    /// <summary>Explanation connecting attach evidence with Narrative capabilities and limitations.</summary>
    public string DebugTargetNarrativeText
    {
        get => _debugTargetNarrativeText;
        private set => SetField(ref _debugTargetNarrativeText, value);
    }

    /// <summary>Edited debug configuration name.</summary>
    public string ConfigurationNameText { get => _configurationNameText; set => SetField(ref _configurationNameText, value); }

    /// <summary>Edited debugger profile identifier.</summary>
    public string ConfigurationProfileText
    {
        get => _configurationProfileText;
        set
        {
            SetField(ref _configurationProfileText, value);
            UpdateDebugTargetAssessment();
        }
    }

    /// <summary>Edited request name: Launch or Attach.</summary>
    public string ConfigurationRequestText { get => _configurationRequestText; set => SetField(ref _configurationRequestText, value); }

    /// <summary>Edited program path.</summary>
    public string ConfigurationProgramText { get => _configurationProgramText; set => SetField(ref _configurationProgramText, value); }

    /// <summary>Edited attach process identifier.</summary>
    public string ConfigurationProcessIdText { get => _configurationProcessIdText; set => SetField(ref _configurationProcessIdText, value); }

    /// <summary>Edited working directory.</summary>
    public string ConfigurationWorkingDirectoryText { get => _configurationWorkingDirectoryText; set => SetField(ref _configurationWorkingDirectoryText, value); }

    /// <summary>Semicolon-separated launch arguments.</summary>
    public string ConfigurationArgumentsText { get => _configurationArgumentsText; set => SetField(ref _configurationArgumentsText, value); }

    /// <summary>Whether the edited launch configuration stops on entry.</summary>
    public bool ConfigurationStopOnEntry { get => _configurationStopOnEntry; set => SetField(ref _configurationStopOnEntry, value); }

    /// <summary>Refreshes the local process list used by attach configurations.</summary>
    public void RefreshProcesses()
    {
        if (_debugProcesses is null)
            return;
        try
        {
            Processes = _debugProcesses.GetProcesses();
            var configuredPid = int.TryParse(ConfigurationProcessIdText, out var pid) ? pid : (int?)null;
            SelectedProcess = configuredPid is null ? null : Processes.FirstOrDefault(item => item.Id == configuredPid);
        }
        catch (Exception ex)
        {
            _output.Write("Debug", $"Cannot enumerate attach processes: {ex.Message}", OutputSeverity.Error);
        }
    }

    /// <summary>Applies the profile recommended by deterministic target evidence.</summary>
    public void UseRecommendedDebugProfile()
    {
        var recommended = SelectedTargetAssessment?.RecommendedProfileId;
        if (!string.IsNullOrWhiteSpace(recommended))
            ConfigurationProfileText = recommended;
    }

    /// <summary>Clears the editor to create a new debug configuration.</summary>
    public void NewDebugConfiguration()
    {
        _selectedConfiguration = null;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedConfiguration)));
        ConfigurationNameText = "New Debug Configuration";
        ConfigurationProfileText = "cpp";
        ConfigurationRequestText = DebugConfigurationRequest.Launch.ToString();
        ConfigurationProgramText = string.Empty;
        ConfigurationProcessIdText = string.Empty;
        ConfigurationWorkingDirectoryText = string.Empty;
        ConfigurationArgumentsText = string.Empty;
        ConfigurationStopOnEntry = false;
    }

    /// <summary>Saves the configuration currently edited in the Debug panel.</summary>
    public void SaveDebugConfiguration()
    {
        if (_debugConfigurations is null || string.IsNullOrWhiteSpace(ConfigurationNameText)
            || string.IsNullOrWhiteSpace(ConfigurationProfileText)
            || !Enum.TryParse<DebugConfigurationRequest>(ConfigurationRequestText, true, out var request))
        {
            _output.Write("Debug", "Enter a configuration name, profile and Launch or Attach request.", OutputSeverity.Warning);
            return;
        }
        int? processId = null;
        if (!string.IsNullOrWhiteSpace(ConfigurationProcessIdText))
        {
            if (!int.TryParse(ConfigurationProcessIdText, out var parsed) || parsed < 1)
            {
                _output.Write("Debug", "Attach process ID must be a positive integer.", OutputSeverity.Warning);
                return;
            }
            processId = parsed;
        }
        var id = SelectedConfiguration?.Id ?? $"debug-{Guid.NewGuid():N}";
        var arguments = ConfigurationArgumentsText.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(item => item.Trim()).Where(item => item.Length > 0).ToArray();
        _debugConfigurations.Upsert(new DebugLaunchConfiguration(
            id, ConfigurationNameText, ConfigurationProfileText, request,
            NormalizeOptional(ConfigurationProgramText), processId,
            NormalizeOptional(ConfigurationWorkingDirectoryText), ConfigurationStopOnEntry, arguments));
    }

    /// <summary>Removes the selected persisted debug configuration.</summary>
    public void RemoveSelectedDebugConfiguration()
    {
        if (_debugConfigurations is not null && SelectedConfiguration is not null)
            _debugConfigurations.Remove(SelectedConfiguration.Id);
    }

    /// <summary>Starts the selected persisted launch or attach configuration.</summary>
    public async Task StartSelectedConfigurationAsync()
    {
        if (SelectedConfiguration is null || _startConfiguration is null)
        {
            _output.Write("Debug", "Select a debug configuration first.", OutputSeverity.Warning);
            return;
        }
        await _startConfiguration(SelectedConfiguration).ConfigureAwait(false);
    }

    /// <summary>Expression entered for a new watch.</summary>
    public string WatchExpressionText
    {
        get => _watchExpressionText;
        set => SetField(ref _watchExpressionText, value);
    }

    /// <summary>Toggles a breakpoint in the active source file.</summary>
    public async Task ToggleBreakpointAsync()
    {
        var path = _activeDocument.ActivePath;
        if (string.IsNullOrWhiteSpace(path) || !int.TryParse(BreakpointLineText, out var line) || line < 1)
        {
            _output.Write("Debug", "Open a source file and enter a valid breakpoint line.", OutputSeverity.Warning);
            return;
        }
        _breakpoints.Toggle(path, line);
        if (_session.Snapshot.State is not DebugSessionState.Inactive and not DebugSessionState.Terminated)
        {
            var forFile = _breakpoints.GetBreakpoints()
                .Where(item => item.Path.Equals(Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase)).ToArray();
            await ExecuteAsync(() => _session.SetBreakpointsAsync(path, forFile)).ConfigureAwait(false);
        }
    }

    /// <summary>Adds or updates breakpoint behavior and synchronizes it with the active adapter.</summary>
    public async Task ApplyBreakpointOptionsAsync()
    {
        var selected = SelectedBreakpoint;
        var path = selected?.Path ?? _activeDocument.ActivePath;
        if (string.IsNullOrWhiteSpace(path) || !int.TryParse(BreakpointLineText, out var line) || line < 1)
        {
            _output.Write("Debug", "Select a breakpoint or open a source file and enter a valid line.", OutputSeverity.Warning);
            return;
        }
        var breakpoint = new DebugSourceBreakpoint(
            Path.GetFullPath(path),
            line,
            selected?.Column,
            BreakpointConditionText,
            BreakpointHitConditionText,
            BreakpointLogMessageText);
        if (selected is not null && (selected.Line != breakpoint.Line || selected.Column != breakpoint.Column))
            _breakpoints.Toggle(selected.Path, selected.Line, selected.Column);
        _breakpoints.Set(breakpoint);
        SelectedBreakpoint = _breakpoints.GetBreakpoints().First(item =>
            item.Path.Equals(breakpoint.Path, StringComparison.OrdinalIgnoreCase)
            && item.Line == breakpoint.Line
            && item.Column == breakpoint.Column);
        if (_session.Snapshot.State is not DebugSessionState.Inactive and not DebugSessionState.Terminated)
        {
            var forFile = _breakpoints.GetBreakpoints()
                .Where(item => item.Path.Equals(breakpoint.Path, StringComparison.OrdinalIgnoreCase)).ToArray();
            await ExecuteAsync(() => _session.SetBreakpointsAsync(breakpoint.Path, forFile)).ConfigureAwait(false);
        }
    }

    /// <summary>Applies comma-separated exception filters to the active adapter.</summary>
    public Task ApplyExceptionBreakpointsAsync()
    {
        if (_session.Snapshot.State is DebugSessionState.Inactive or DebugSessionState.Terminated or DebugSessionState.Failed)
        {
            _output.Write("Debug", "Start a debug session before applying exception breakpoints.", OutputSeverity.Warning);
            return Task.CompletedTask;
        }
        var filters = ExceptionBreakpointFiltersText.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries)
            .Select(item => item.Trim())
            .Where(item => item.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return ExecuteAsync(() => _session.SetExceptionBreakpointsAsync(filters));
    }

    /// <summary>Evaluates the current input in the adapter REPL and retains the result.</summary>
    public async Task ExecuteDebugConsoleAsync()
    {
        var expression = DebugConsoleInput.Trim();
        if (expression.Length == 0)
            return;
        if (_session.Snapshot.State is DebugSessionState.Inactive or DebugSessionState.Terminated or DebugSessionState.Failed)
        {
            _output.Write("Debug", "Start a debug session before using Debug Console.", OutputSeverity.Warning);
            return;
        }
        _debugConsoleHistory.RemoveAll(item => item.Equals(expression, StringComparison.Ordinal));
        _debugConsoleHistory.Add(expression);
        _debugConsoleHistoryIndex = _debugConsoleHistory.Count;
        DebugConsoleInput = string.Empty;
        try
        {
            var frameId = Snapshot.State == DebugSessionState.Stopped
                ? _selectedFrameId ?? Snapshot.StackFrames.FirstOrDefault()?.Id
                : null;
            var result = await _session.EvaluateReplAsync(expression, frameId).ConfigureAwait(false);
            Update(() => DebugConsoleEntries =
                [.. DebugConsoleEntries, new DebugConsoleEntry(expression, result.Result, result.Type, false)]);
        }
        catch (Exception ex)
        {
            Update(() => DebugConsoleEntries =
                [.. DebugConsoleEntries, new DebugConsoleEntry(expression, ex.Message, null, true)]);
        }
    }

    /// <summary>Moves Debug Console input to the previous submitted command.</summary>
    public void PreviousDebugConsoleCommand()
    {
        if (_debugConsoleHistory.Count == 0)
            return;
        _debugConsoleHistoryIndex = Math.Max(0, _debugConsoleHistoryIndex - 1);
        DebugConsoleInput = _debugConsoleHistory[_debugConsoleHistoryIndex];
    }

    /// <summary>Moves Debug Console input to the next submitted command.</summary>
    public void NextDebugConsoleCommand()
    {
        if (_debugConsoleHistory.Count == 0)
            return;
        _debugConsoleHistoryIndex = Math.Min(_debugConsoleHistory.Count, _debugConsoleHistoryIndex + 1);
        DebugConsoleInput = _debugConsoleHistoryIndex == _debugConsoleHistory.Count
            ? string.Empty
            : _debugConsoleHistory[_debugConsoleHistoryIndex];
    }

    /// <summary>Changes the selected variable through DAP setVariable.</summary>
    public async Task SetSelectedVariableAsync()
    {
        var node = SelectedVariableNode;
        var value = VariableValueText.Trim();
        if (!_session.SupportsSetVariable || node is null || node.ContainerVariablesReference <= 0 || value.Length == 0)
        {
            _output.Write("Debug", "Select a mutable variable and enter a value supported by the adapter.", OutputSeverity.Warning);
            return;
        }
        try
        {
            var updated = await _session.SetVariableAsync(node.ContainerVariablesReference, node.Name, value)
                .ConfigureAwait(false);
            Update(() =>
            {
                node.ApplyUpdatedValue(updated);
                VariableValueText = updated.Value;
            });
        }
        catch (Exception ex)
        {
            _output.Write("Debug", $"Cannot set variable '{node.Name}': {ex.Message}", OutputSeverity.Error);
        }
    }

    /// <summary>Creates a data breakpoint for the selected variable and synchronizes the complete set.</summary>
    public async Task AddDataBreakpointAsync()
    {
        var node = SelectedVariableNode;
        if (!_session.SupportsDataBreakpoints || node is null || node.ContainerVariablesReference <= 0)
        {
            _output.Write("Debug", "Select a variable supported by adapter data breakpoints.", OutputSeverity.Warning);
            return;
        }
        try
        {
            var frameId = _selectedFrameId ?? Snapshot.StackFrames.FirstOrDefault()?.Id;
            var info = await _session.GetDataBreakpointInfoAsync(
                node.Name, node.ContainerVariablesReference, frameId).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(info.DataId))
                throw new NotSupportedException($"Adapter cannot watch '{node.Name}': {info.Description}");
            var requestedAccess = DataBreakpointAccessTypeText.Trim();
            var accessType = requestedAccess.Length == 0
                ? info.AccessTypes.FirstOrDefault()
                : info.AccessTypes.Count == 0 || info.AccessTypes.Contains(requestedAccess, StringComparer.Ordinal)
                    ? requestedAccess
                    : info.AccessTypes[0];
            var breakpoint = new DebugDataBreakpoint(
                info.DataId, info.Description, accessType,
                NormalizeOptional(DataBreakpointConditionText), NormalizeOptional(DataBreakpointHitConditionText));
            var updated = DataBreakpoints.Where(item => !item.DataId.Equals(breakpoint.DataId, StringComparison.Ordinal))
                .Append(breakpoint).ToArray();
            await _session.SetDataBreakpointsAsync(updated).ConfigureAwait(false);
            Update(() => DataBreakpoints = updated);
        }
        catch (Exception ex)
        {
            _output.Write("Debug", $"Cannot add data breakpoint: {ex.Message}", OutputSeverity.Error);
        }
    }

    /// <summary>Removes the selected data breakpoint and synchronizes the remaining set.</summary>
    public async Task RemoveDataBreakpointAsync()
    {
        var selected = SelectedDataBreakpoint;
        if (selected is null)
            return;
        var updated = DataBreakpoints.Where(item => !item.DataId.Equals(selected.DataId, StringComparison.Ordinal)).ToArray();
        try
        {
            await _session.SetDataBreakpointsAsync(updated).ConfigureAwait(false);
            Update(() =>
            {
                DataBreakpoints = updated;
                SelectedDataBreakpoint = null;
            });
        }
        catch (Exception ex)
        {
            _output.Write("Debug", $"Cannot remove data breakpoint: {ex.Message}", OutputSeverity.Error);
        }
    }

    /// <summary>Continues the active stopped thread.</summary>
    public Task ContinueAsync() => ExecuteAsync(() => _session.ContinueAsync());

    /// <summary>Pauses the active debuggee.</summary>
    public Task PauseAsync() => ExecuteAsync(() => _session.PauseAsync());

    /// <summary>Steps over the current statement.</summary>
    public Task NextAsync() => ExecuteAsync(() => _session.NextAsync());

    /// <summary>Steps into the current statement.</summary>
    public Task StepInAsync() => ExecuteAsync(() => _session.StepInAsync());

    /// <summary>Steps out of the current frame.</summary>
    public Task StepOutAsync() => ExecuteAsync(() => _session.StepOutAsync());

    /// <summary>Stops the active debug session.</summary>
    public Task StopAsync() => ExecuteAsync(() => _session.StopAsync());

    /// <summary>Opens Narrative Why for the first mapped frame of the stopped debug session.</summary>
    public void ExplainStoppedFrame()
    {
        var frame = Snapshot.State == DebugSessionState.Stopped ? Snapshot.StackFrames.FirstOrDefault() : null;
        if (frame is null)
        {
            _output.Write("Why", "Stop at a mapped frame before asking why it happened.", OutputSeverity.Warning);
            return;
        }
        _eventBus.Publish(new ActivatePanelRequested("junexis.narrative.why"));
        _eventBus.Publish(new WhyForStoppedFrameRequested(frame));
    }

    /// <summary>Adds a watch expression and evaluates all watches when the debuggee is stopped.</summary>
    public async Task AddWatchAsync()
    {
        var expression = WatchExpressionText.Trim();
        if (expression.Length == 0)
        {
            _output.Write("Debug", "Enter a watch expression.", OutputSeverity.Warning);
            return;
        }
        lock (_watchGate)
        {
            if (!_watchExpressions.Contains(expression, StringComparer.Ordinal))
                _watchExpressions.Add(expression);
        }
        WatchExpressionText = string.Empty;
        SaveWatches();
        var frameId = _selectedFrameId ?? Snapshot.StackFrames.FirstOrDefault()?.Id;
        if (Snapshot.State == DebugSessionState.Stopped && frameId is not null)
            await RefreshWatchesAsync(frameId.Value, Volatile.Read(ref _refreshVersion)).ConfigureAwait(false);
        else
            UpdateUnavailableWatches("<not stopped>");
    }

    /// <summary>Removes a watch expression from the panel.</summary>
    /// <param name="watch">Watch result selected in the panel.</param>
    public void RemoveWatch(DebugEvaluation? watch)
    {
        if (watch is null)
            return;
        lock (_watchGate)
            _watchExpressions.RemoveAll(item => item.Equals(watch.Expression, StringComparison.Ordinal));
        SaveWatches();
        Update(() =>
        {
            Watches = Watches.Where(item =>
                !item.Expression.Equals(watch.Expression, StringComparison.Ordinal)).ToArray();
            WatchTree = Watches.Select(DebugInspectionNode.FromEvaluation).ToArray();
        });
    }

    /// <summary>Loads scopes and navigates to the selected stack frame.</summary>
    /// <param name="frame">Selected stack frame.</param>
    public async Task SelectFrameAsync(DebugStackFrame? frame)
    {
        if (frame is null)
            return;
        var version = Interlocked.Increment(ref _refreshVersion);
        await LoadFrameAsync(frame, navigate: true, version).ConfigureAwait(false);
    }

    /// <summary>Loads variables for the selected scope.</summary>
    /// <param name="scope">Selected scope.</param>
    public async Task SelectScopeAsync(DebugScope? scope)
    {
        if (scope is null)
            return;
        try
        {
            var variables = await _session.GetVariablesAsync(scope.VariablesReference).ConfigureAwait(false);
            Update(() =>
            {
                Variables = variables;
                VariableTree = variables.Select(variable =>
                    DebugInspectionNode.FromVariable(variable, scope.VariablesReference)).ToArray();
            });
        }
        catch (Exception ex)
        {
            _output.Write("Debug", $"Cannot load variables: {ex.Message}", OutputSeverity.Error);
        }
    }

    /// <summary>Lazily loads children for one expandable variable or watch value.</summary>
    /// <param name="node">Tree node selected by the user.</param>
    public async Task ExpandNodeAsync(DebugInspectionNode? node)
    {
        if (node is null || node.IsPlaceholder || node.VariablesReference <= 0 || node.IsLoaded || node.IsLoading)
            return;
        node.IsLoading = true;
        try
        {
            var children = await _session.GetVariablesAsync(node.VariablesReference).ConfigureAwait(false);
            Update(() => node.ReplaceChildren(children));
        }
        catch (Exception ex)
        {
            Update(() => node.SetLoadError(ex.Message));
        }
    }

    internal void Detach()
    {
        Interlocked.Increment(ref _refreshVersion);
        _session.SnapshotChanged -= OnSnapshotChanged;
        _session.OutputReceived -= OnOutputReceived;
        _session.BreakpointStatusesChanged -= OnBreakpointStatusesChanged;
        _breakpoints.BreakpointsChanged -= OnBreakpointsChanged;
        if (_workspace is not null)
            _workspace.FolderOpened -= OnWorkspaceOpened;
        if (_debugConfigurations is not null)
            _debugConfigurations.ConfigurationsChanged -= OnDebugConfigurationsChanged;
    }

    private async Task ExecuteAsync(Func<Task> operation)
    {
        try
        {
            await operation().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _output.Write("Debug", ex.Message, OutputSeverity.Error);
        }
    }

    private void OnSnapshotChanged(DebugSessionSnapshot snapshot)
    {
        var version = Interlocked.Increment(ref _refreshVersion);
        Update(() =>
        {
            Snapshot = snapshot;
            UpdateSupportedExceptionFilters();
        });
        if (snapshot.State != DebugSessionState.Stopped)
        {
            _selectedFrameId = null;
            Update(() =>
            {
                Scopes = [];
                Variables = [];
                VariableTree = [];
                SelectedVariableNode = null;
                if (snapshot.State is DebugSessionState.Inactive or DebugSessionState.Terminated or DebugSessionState.Failed)
                {
                    DataBreakpoints = [];
                    SelectedDataBreakpoint = null;
                }
            });
            UpdateUnavailableWatches(snapshot.State == DebugSessionState.Running ? "<running>" : "<not available>");
            return;
        }
        var frame = snapshot.StackFrames.FirstOrDefault();
        if (frame is not null)
            _ = LoadFrameAsync(frame, navigate: true, version);
    }

    private async Task LoadFrameAsync(DebugStackFrame frame, bool navigate, int version)
    {
        if (navigate && !string.IsNullOrWhiteSpace(frame.SourcePath))
        {
            Update(() => _eventBus.Publish(new NavigateToLocationRequested(
                frame.SourcePath, Math.Max(1, frame.Line), Math.Max(1, frame.Column))));
        }
        try
        {
            var scopes = await _session.GetScopesAsync(frame.Id).ConfigureAwait(false);
            var preferredScope = scopes.FirstOrDefault(scope =>
                                     scope.Name.Contains("local", StringComparison.OrdinalIgnoreCase))
                                 ?? scopes.FirstOrDefault(scope => !scope.IsExpensive)
                                 ?? scopes.FirstOrDefault();
            IReadOnlyList<DebugVariable> variables = preferredScope is null
                ? []
                : await _session.GetVariablesAsync(preferredScope.VariablesReference).ConfigureAwait(false);
            var watches = await EvaluateWatchesAsync(frame.Id).ConfigureAwait(false);
            if (version != Volatile.Read(ref _refreshVersion))
                return;
            _selectedFrameId = frame.Id;
            Update(() =>
            {
                Scopes = scopes;
                Variables = variables;
                VariableTree = variables.Select(variable =>
                    DebugInspectionNode.FromVariable(variable, preferredScope?.VariablesReference ?? 0)).ToArray();
                Watches = ReconcileWatches(watches);
                WatchTree = Watches.Select(DebugInspectionNode.FromEvaluation).ToArray();
            });
        }
        catch (Exception ex)
        {
            _output.Write("Debug", $"Cannot load frame inspection: {ex.Message}", OutputSeverity.Error);
        }
    }

    private async Task RefreshWatchesAsync(int frameId, int version)
    {
        var watches = await EvaluateWatchesAsync(frameId).ConfigureAwait(false);
        if (version == Volatile.Read(ref _refreshVersion))
            Update(() =>
            {
                Watches = ReconcileWatches(watches);
                WatchTree = Watches.Select(DebugInspectionNode.FromEvaluation).ToArray();
            });
    }

    private async Task<IReadOnlyList<DebugEvaluation>> EvaluateWatchesAsync(int frameId)
    {
        string[] expressions;
        lock (_watchGate)
            expressions = _watchExpressions.ToArray();
        var results = new List<DebugEvaluation>(expressions.Length);
        foreach (var expression in expressions)
        {
            try
            {
                results.Add(await _session.EvaluateAsync(expression, frameId).ConfigureAwait(false));
            }
            catch (Exception ex)
            {
                results.Add(new DebugEvaluation(expression, $"<error: {ex.Message}>", null, 0));
            }
        }
        return results;
    }

    private IReadOnlyList<DebugEvaluation> ReconcileWatches(IReadOnlyList<DebugEvaluation> results)
    {
        lock (_watchGate)
        {
            return _watchExpressions.Select(expression =>
                    results.FirstOrDefault(item => item.Expression.Equals(expression, StringComparison.Ordinal))
                    ?? new DebugEvaluation(expression, "<pending>", null, 0))
                .ToArray();
        }
    }

    private void UpdateUnavailableWatches(string result)
    {
        IReadOnlyList<DebugEvaluation> watches;
        lock (_watchGate)
            watches = _watchExpressions.Select(expression => new DebugEvaluation(expression, result, null, 0)).ToArray();
        Update(() =>
        {
            Watches = watches;
            WatchTree = watches.Select(DebugInspectionNode.FromEvaluation).ToArray();
        });
    }

    private void OnWorkspaceOpened(string _) => LoadStoredWatches();

    private void LoadStoredWatches()
    {
        if (_workspace is null)
            return;
        try
        {
            var expressions = DebugWatchStore.Load(_workspace);
            lock (_watchGate)
            {
                _watchExpressions.Clear();
                _watchExpressions.AddRange(expressions);
            }
            UpdateUnavailableWatches(Snapshot.State == DebugSessionState.Stopped ? "<pending>" : "<not stopped>");
            var frameId = _selectedFrameId ?? Snapshot.StackFrames.FirstOrDefault()?.Id;
            if (Snapshot.State == DebugSessionState.Stopped && frameId is not null)
                _ = RefreshWatchesAsync(frameId.Value, Volatile.Read(ref _refreshVersion));
        }
        catch (Exception ex)
        {
            _output.Write("Debug", $"Cannot load workspace watches: {ex.Message}", OutputSeverity.Warning);
        }
    }

    private void SaveWatches()
    {
        if (_workspace is null)
            return;
        try
        {
            string[] expressions;
            lock (_watchGate)
                expressions = _watchExpressions.ToArray();
            DebugWatchStore.Save(_workspace, expressions);
        }
        catch (Exception ex)
        {
            _output.Write("Debug", $"Cannot save workspace watches: {ex.Message}", OutputSeverity.Warning);
        }
    }

    private void OnOutputReceived(DebugOutputEvent value) =>
        _output.Write("Debug", value.Text,
            value.Category.Contains("stderr", StringComparison.OrdinalIgnoreCase)
                ? OutputSeverity.Warning
                : OutputSeverity.Info);

    private void OnBreakpointsChanged() => Update(() => SourceBreakpoints = _breakpoints.GetBreakpoints());

    private void OnBreakpointStatusesChanged(IReadOnlyList<DebugBreakpointStatus> statuses) =>
        Update(() => BreakpointStatuses = statuses);

    private void OnDebugConfigurationsChanged() => Update(ReloadDebugConfigurations);

    private void ReloadDebugConfigurations()
    {
        if (_debugConfigurations is null)
            return;
        Configurations = _debugConfigurations.GetConfigurations();
        var selected = Configurations.FirstOrDefault(item => item.Id.Equals(
            _debugConfigurations.SelectedId, StringComparison.Ordinal)) ?? Configurations.FirstOrDefault();
        _selectedConfiguration = selected;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedConfiguration)));
        if (selected is not null)
            PopulateConfigurationEditor(selected);
    }

    private void PopulateConfigurationEditor(DebugLaunchConfiguration configuration)
    {
        ConfigurationNameText = configuration.Name;
        ConfigurationProfileText = configuration.ProfileId;
        ConfigurationRequestText = configuration.Request.ToString();
        ConfigurationProgramText = configuration.Program ?? string.Empty;
        ConfigurationProcessIdText = configuration.ProcessId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        ConfigurationWorkingDirectoryText = configuration.WorkingDirectory ?? string.Empty;
        ConfigurationArgumentsText = string.Join("; ", configuration.Arguments ?? []);
        ConfigurationStopOnEntry = configuration.StopOnEntry;
        UpdateDebugTargetAssessment();
    }

    private void UpdateDebugTargetAssessment()
    {
        if (_debugTargetAssessment is null || SelectedProcess is null)
        {
            SelectedTargetAssessment = null;
            DebugTargetNarrativeText = "Select a process to see profile and Narrative evidence.";
            return;
        }
        SelectedTargetAssessment = _debugTargetAssessment.Assess(SelectedProcess, ConfigurationProfileText);
        DebugTargetNarrativeText = SelectedTargetAssessment.Summary;
    }

    private void UpdateSupportedExceptionFilters()
    {
        var filters = _session.ExceptionBreakpointFilters;
        SupportedExceptionBreakpointFiltersText = filters.Count == 0
            ? "Adapter filters: unavailable"
            : $"Adapter filters: {string.Join(", ", filters.Select(item => $"{item.Filter} ({item.Label})"))}";
        DebugCapabilitiesText =
            $"Set variable: {(_session.SupportsSetVariable ? "supported" : "unavailable")}; " +
            $"data breakpoints: {(_session.SupportsDataBreakpoints ? "supported" : "unavailable")}";
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private void Update(Action action)
    {
        if (_synchronizationContext is null || SynchronizationContext.Current == _synchronizationContext)
            action();
        else
            _synchronizationContext.Post(_ => action(), null);
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

internal sealed class DapDebuggerExtension : IJunexisExtension
{
    private IDebugSessionService? _session;
    private DebugPanelViewModel? _viewModel;
    private IDebugAdapterRegistry? _adapters;
    private WorkbenchOperationHandle? _debugOperation;
    private CancellationTokenSource? _debugStartCancellation;

    public string Id => "junexis.debugger.dap";

    public void Initialize(IHostContext context)
    {
        if (!context.Capabilities.TryGet<IDebugSessionService>(out _session) || _session is null
            || !context.Capabilities.TryGet<IBreakpointService>(out var breakpoints) || breakpoints is null
            || !context.Capabilities.TryGet<IDebugAdapterRegistry>(out _adapters) || _adapters is null)
        {
            context.Output.Write("Debug", "DAP session capabilities unavailable.", OutputSeverity.Warning);
            return;
        }
        _viewModel = new DebugPanelViewModel(
            _session, breakpoints, context.ActiveDocument, context.EventBus, context.Output, context.Workspace,
            context.Capabilities.TryGet<IDebugConfigurationService>(out var configurations) ? configurations : null,
            context.Capabilities.TryGet<IDebugProcessService>(out var debugProcesses) ? debugProcesses : null,
            configuration => StartSelectedAsync(context, configuration),
            context.Capabilities.TryGet<IDebugTargetAssessmentService>(out var targetAssessment) ? targetAssessment : null);
        _session.SnapshotChanged += OnLifecycleSnapshotChanged;
        context.Panels.RegisterPanel(Id, PanelType.Tool, () => _viewModel, "Debug", DockGroup.Right);
        context.Commands.Register("debug.gpp", () => Start(context, DefaultConfiguration("cpp")), "Build/Debug (C++)", "Debug (C++)", "Debug");
        context.Commands.Register("debug.go", () => Start(context, DefaultConfiguration("go")), "Build/Debug (Go)", "Debug (Go)", "Debug");
        context.Commands.Register("debug.typescript", () => Start(context, DefaultConfiguration("typescript")), "Build/Debug (TypeScript)", "Debug (TypeScript)", "Debug");
        context.Commands.Register("debug.selected", () => _ = _viewModel.StartSelectedConfigurationAsync(),
            "Debug/Start Selected Configuration", "Start Selected Configuration", "Debug");
        context.Commands.Register("debug.why", _viewModel.ExplainStoppedFrame,
            "Debug/Why Did This Happen?", "Why Did This Happen?", "Debug");
        context.Commands.Register("debug.continue", () => _ = _viewModel.ContinueAsync(), null, "Continue", "Debug");
        context.Commands.Register("debug.pause", () => _ = _viewModel.PauseAsync(), null, "Pause", "Debug");
        context.Commands.Register("debug.next", () => _ = _viewModel.NextAsync(), null, "Step Over", "Debug");
        context.Commands.Register("debug.stepIn", () => _ = _viewModel.StepInAsync(), null, "Step Into", "Debug");
        context.Commands.Register("debug.stepOut", () => _ = _viewModel.StepOutAsync(), null, "Step Out", "Debug");
        context.Commands.Register("debug.session.stop", () => StopDebugging(), null, "Stop Debugging", "Debug");
        context.Output.Write("Extensions", "DapDebuggerExtension loaded");
    }

    public void Shutdown()
    {
        _viewModel?.Detach();
        if (_session is not null)
        {
            _session.SnapshotChanged -= OnLifecycleSnapshotChanged;
            _debugStartCancellation?.Cancel();
            _debugStartCancellation?.Dispose();
            _debugStartCancellation = null;
            _debugOperation?.Cancel("Debug session stopped during Studio shutdown.");
            _debugOperation = null;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            try
            {
                _session.StopAsync(timeout.Token).GetAwaiter().GetResult();
            }
            catch (Exception)
            {
                // Host shutdown remains best-effort if an adapter no longer responds.
            }
        }
        _viewModel = null;
        _session = null;
        _adapters = null;
    }

    private void Start(IHostContext context, DebugLaunchConfiguration selected)
    {
        if (_session is null)
            return;
        var (lifecycle, cancellationToken) = BeginDebugOperation(context, selected);
        _ = PrepareAndStartAsync(context, selected, lifecycle, cancellationToken);
    }

    private Task StartSelectedAsync(IHostContext context, DebugLaunchConfiguration selected)
    {
        var (lifecycle, cancellationToken) = BeginDebugOperation(context, selected);
        return PrepareAndStartAsync(context, selected, lifecycle, cancellationToken);
    }

    private async Task PrepareAndStartAsync(
        IHostContext context,
        DebugLaunchConfiguration selected,
        WorkbenchOperationHandle lifecycle,
        CancellationToken cancellationToken)
    {
        try
        {
            if (selected.Request == DebugConfigurationRequest.Launch)
            {
                lifecycle.Running("Preparing Narrative for debug launch…");
            }
            await NarrativeRuntimeLaunch
                .PrepareDebugAsync(context, selected.Request, cancellationToken)
                .ConfigureAwait(false);
            var configuration = CreateConfiguration(context, selected, _adapters!);
            await StartAsync(context, configuration, lifecycle, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            lifecycle.Cancel("Debug launch cancelled.");
            ClearDebugOperation(lifecycle);
        }
        catch (Exception ex)
        {
            lifecycle.Fail($"Debug launch failed: {ex.Message}");
            ClearDebugOperation(lifecycle);
            context.Output.Write("Debug", ex.Message, OutputSeverity.Error);
        }
    }

    private async Task StartAsync(
        IHostContext context,
        DebugSessionConfiguration configuration,
        WorkbenchOperationHandle lifecycle,
        CancellationToken cancellationToken)
    {
        try
        {
            await _session!.StartAsync(configuration, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            lifecycle.Fail($"Debug launch failed: {ex.Message}");
            ClearDebugOperation(lifecycle);
            context.Output.Write("Debug", $"DAP launch failed: {ex.Message}", OutputSeverity.Error);
        }
    }

    private (WorkbenchOperationHandle Lifecycle, CancellationToken CancellationToken) BeginDebugOperation(
        IHostContext context,
        DebugLaunchConfiguration configuration)
    {
        _debugStartCancellation?.Cancel();
        _debugStartCancellation?.Dispose();
        _debugOperation?.Cancel("Debug session replaced by a new launch.");
        _debugStartCancellation = new CancellationTokenSource();
        var lifecycle = WorkbenchOperationHandle.Begin(
            context,
            configuration.ProfileId,
            configuration.Name,
            LaunchOperation.Debug,
            $"Starting debug configuration {configuration.Name}…");
        _debugOperation = lifecycle;
        return (lifecycle, _debugStartCancellation.Token);
    }

    private void OnLifecycleSnapshotChanged(DebugSessionSnapshot snapshot)
    {
        var lifecycle = _debugOperation;
        if (lifecycle is null)
            return;
        switch (snapshot.State)
        {
            case DebugSessionState.Running:
                lifecycle.Running(snapshot.Status);
                break;
            case DebugSessionState.Stopped:
                lifecycle.Running(snapshot.Status);
                break;
            case DebugSessionState.Terminated:
                lifecycle.Complete(0, 0, snapshot.Status);
                ClearDebugOperation(lifecycle);
                break;
            case DebugSessionState.Failed:
                lifecycle.Fail(snapshot.Status);
                ClearDebugOperation(lifecycle);
                break;
        }
    }

    private void StopDebugging()
    {
        _debugStartCancellation?.Cancel();
        _debugStartCancellation?.Dispose();
        _debugStartCancellation = null;
        _debugOperation?.Cancel("Debug session stopped.");
        _debugOperation = null;
        if (_viewModel is not null)
            _ = _viewModel.StopAsync();
    }

    private void ClearDebugOperation(WorkbenchOperationHandle lifecycle)
    {
        if (ReferenceEquals(_debugOperation, lifecycle))
        {
            _debugOperation = null;
            _debugStartCancellation?.Dispose();
            _debugStartCancellation = null;
        }
    }

    internal static DebugSessionConfiguration CreateConfiguration(
        IHostContext context,
        DebugLaunchConfiguration selected,
        IDebugAdapterRegistry adapters)
    {
        var root = context.Workspace.RootPath;
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            throw new InvalidOperationException("Open a workspace before starting a DAP session.");
        var profileId = selected.ProfileId.ToLowerInvariant();
        var adapter = adapters.Resolve(profileId)
            ?? throw new InvalidOperationException($"No concrete DAP adapter was found for profile '{profileId}'. Check Toolchain Doctor.");
        var active = context.ActiveDocument.ActivePath;
        var defaultProgram = profileId switch
        {
            "cpp" => Path.Combine(root, OperatingSystem.IsWindows() ? "junexis_build.exe" : "junexis_build"),
            "go" => root,
            "typescript" when !string.IsNullOrWhiteSpace(active) => Path.Combine(
                root, ".junexis", "build", "typescript",
                Path.ChangeExtension(Path.GetRelativePath(root, active), ".js")),
            _ => active ?? root,
        };
        var program = ResolveWorkspacePath(root, selected.Program) ?? defaultProgram;
        var workingDirectory = ResolveWorkspacePath(root, selected.WorkingDirectory) ?? root;
        if (selected.Request == DebugConfigurationRequest.Attach && selected.ProcessId is null or < 1)
            throw new InvalidOperationException("Attach configuration requires a positive process ID.");
        var launch = new Dictionary<string, object?>
        {
            ["name"] = selected.Name,
            ["request"] = selected.Request == DebugConfigurationRequest.Attach ? "attach" : "launch",
            ["type"] = adapter.ConfigurationType,
            ["cwd"] = workingDirectory,
        };
        if (selected.Request == DebugConfigurationRequest.Launch)
        {
            launch["program"] = program;
            launch["stopOnEntry"] = selected.StopOnEntry;
            if (selected.Arguments is { Count: > 0 })
                launch["args"] = selected.Arguments;
        }
        else
            launch["processId"] = selected.ProcessId;
        if (profileId == "cpp")
        {
            launch["MIMode"] = "gdb";
            launch["miDebuggerPath"] = DebugToolLocator.ResolveGdb();
            launch["externalConsole"] = false;
        }
        else if (profileId == "go")
        {
            launch["mode"] = selected.Request == DebugConfigurationRequest.Attach ? "local" : "debug";
        }
        else if (profileId == "typescript")
        {
            launch["sourceMaps"] = true;
            launch["outFiles"] = new[] { Path.Combine(root, ".junexis", "build", "typescript", "**", "*.js") };
            launch["console"] = "internalConsole";
        }
        if (selected.Request == DebugConfigurationRequest.Launch)
            NarrativeRuntimeLaunch.AttachToDebugLaunch(context, launch);
        var breakpoints = context.Capabilities.Get<IBreakpointService>().GetBreakpoints();
        var sourceMapTarget = profileId switch
        {
            "cpp" => "embedded",
            "go" => "server",
            "typescript" => "report",
            _ => null,
        };
        return new DebugSessionConfiguration(
            adapter.FileName, adapter.Arguments, workingDirectory, launch, breakpoints, adapter.Transport, sourceMapTarget);
    }

    private static DebugLaunchConfiguration DefaultConfiguration(string profileId) =>
        new($"launch-{profileId}", $"Launch {profileId}", profileId, DebugConfigurationRequest.Launch);

    private static string? ResolveWorkspacePath(string root, string? path) =>
        string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(root, path));
}

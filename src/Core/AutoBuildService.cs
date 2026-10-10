using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Core;

/// <summary>
/// Subscribes to workspace events (<see cref="DocumentSaved"/>, <see cref="WorkspaceOpenedEvent"/>)
/// and triggers auto-build through <see cref="BuildOrchestrator"/> with debounce protection.
/// <para>Auto-build is only active when <see cref="AutoBuildConfig.Enabled"/> is <c>true</c>.</para>
/// </summary>
public sealed class AutoBuildService : IDisposable
{
    private readonly BuildOrchestrator _orchestrator;
    private readonly IEventBus _eventBus;
    private readonly IOutput? _output;
    private readonly IWorkspace _workspace;

    private AutoBuildConfig _config;
    private CancellationTokenSource? _debounceCts;
    private readonly object _lock = new();
    private bool _disposed;
    private bool _buildInProgress;

    /// <summary>Output channel name for auto-build messages.</summary>
    public const string OutputChannel = "AutoBuild";

    /// <summary>
    /// Number of auto-builds triggered since the service was created.
    /// </summary>
    public int BuildCount { get; private set; }

    /// <summary>
    /// Current configuration.
    /// </summary>
    public AutoBuildConfig Config => _config;

    private readonly Action<DocumentSaved> _onDocSaved;
    private readonly Action<WorkspaceOpenedEvent> _onWorkspaceOpened;

    /// <summary>
    /// Creates a new <see cref="AutoBuildService"/>.
    /// </summary>
    /// <param name="orchestrator">The build orchestrator to delegate builds to.</param>
    /// <param name="eventBus">Event bus for subscribing to workspace events.</param>
    /// <param name="workspace">Workspace for resolving root path.</param>
    /// <param name="output">Optional output service for logging.</param>
    /// <param name="config">Optional auto-build configuration (disabled by default).</param>
    /// <exception cref="ArgumentNullException">Thrown when required parameters are null.</exception>
    public AutoBuildService(
        BuildOrchestrator orchestrator,
        IEventBus eventBus,
        IWorkspace workspace,
        IOutput? output = null,
        AutoBuildConfig? config = null)
    {
        ArgumentNullException.ThrowIfNull(orchestrator);
        ArgumentNullException.ThrowIfNull(eventBus);
        ArgumentNullException.ThrowIfNull(workspace);

        _orchestrator = orchestrator;
        _eventBus = eventBus;
        _workspace = workspace;
        _output = output;
        _config = config ?? new AutoBuildConfig();

        _onDocSaved = OnDocumentSaved;
        _onWorkspaceOpened = OnWorkspaceOpened;

        _eventBus.Subscribe(_onDocSaved);
        _eventBus.Subscribe(_onWorkspaceOpened);
    }

    /// <summary>
    /// Updates the auto-build configuration at runtime.
    /// </summary>
    /// <param name="config">The new configuration.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="config"/> is null.</exception>
    public void UpdateConfig(AutoBuildConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        _config = config;
        _output?.Write(OutputChannel, $"Auto-build config updated: Enabled={config.Enabled}, DebounceMs={config.DebounceMs}.");
    }

    private void OnDocumentSaved(DocumentSaved evt)
    {
        if (!_config.Enabled) return;

        _output?.Write(OutputChannel, $"Document saved: {evt.Path}. Scheduling auto-build (debounce {_config.DebounceMs} ms).");

        ScheduleDebouncedBuild(evt.Path);
    }

    private void OnWorkspaceOpened(WorkspaceOpenedEvent evt)
    {
        if (!_config.Enabled) return;

        _output?.Write(OutputChannel, $"Workspace opened: {evt.RootPath}. Scheduling auto-build.");

        ScheduleDebouncedBuild(activeDocumentPath: null);
    }

    private void ScheduleDebouncedBuild(string? activeDocumentPath)
    {
        lock (_lock)
        {
            if (_disposed) return;

            // Cancel previous pending build
            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
            _debounceCts = new CancellationTokenSource();
        }

        var cts = _debounceCts;
        var debounceMs = _config.DebounceMs;

        _ = Task.Run(async () =>
        {
            try
            {
                if (debounceMs > 0)
                    await Task.Delay(debounceMs, cts!.Token).ConfigureAwait(false);

                if (cts!.Token.IsCancellationRequested) return;

                // Prevent concurrent builds
                lock (_lock)
                {
                    if (_buildInProgress || _disposed) return;
                    _buildInProgress = true;
                }

                try
                {
                    var rootPath = _workspace.RootPath ?? string.Empty;
                    var context = new BuildContext(rootPath, activeDocumentPath);

                    _output?.Write(OutputChannel, "Auto-build triggered.", OutputSeverity.Info);

                    await _orchestrator.BuildAsync(context).ConfigureAwait(false);
                    BuildCount++;
                }
                finally
                {
                    lock (_lock)
                    {
                        _buildInProgress = false;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Debounce cancelled — expected
            }
            catch (Exception ex)
            {
                _output?.Write(OutputChannel, $"Auto-build error: {ex.Message}", OutputSeverity.Error);
            }
        });
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
        }

        _eventBus.Unsubscribe(_onDocSaved);
        _eventBus.Unsubscribe(_onWorkspaceOpened);
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
    }
}

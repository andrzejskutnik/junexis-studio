using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Host;

/// <summary>
/// Entry in the MCP communication log.
/// </summary>
/// <param name="TimestampUtc">When the event occurred.</param>
/// <param name="ConnectorId">Source connector ID.</param>
/// <param name="Kind">Kind of log entry (Connected, Disconnected, Message, Error).</param>
/// <param name="Detail">Human-readable detail text.</param>
public sealed record McpLogEntry(DateTime TimestampUtc, string ConnectorId, McpLogEntryKind Kind, string Detail);

/// <summary>
/// Kinds of MCP log entries.
/// </summary>
public enum McpLogEntryKind
{
    /// <summary>A connector connected.</summary>
    Connected,

    /// <summary>A connector disconnected.</summary>
    Disconnected,

    /// <summary>A message was received.</summary>
    Message,

    /// <summary>An error occurred.</summary>
    Error
}

/// <summary>
/// View-model for the MCP status panel. Subscribes to MCP events on the event bus
/// and shows connection status + communication log.
/// </summary>
public sealed class McpStatusViewModel : IDisposable
{
    private readonly IMcpConnectorRegistry _registry;
    private readonly IEventBus _eventBus;
    private readonly List<McpLogEntry> _log = new();
    private readonly object _gate = new();

    /// <summary>
    /// Gets the current log entries (newest first).
    /// </summary>
    public IReadOnlyList<McpLogEntry> Log
    {
        get
        {
            lock (_gate) return _log.ToList().AsReadOnly();
        }
    }

    /// <summary>
    /// Gets whether the active connector is currently connected.
    /// </summary>
    public bool IsActiveConnected => _registry.GetActive()?.IsConnected ?? false;

    /// <summary>
    /// Gets the active connector ID, if any.
    /// </summary>
    public string? ActiveConnectorId => _registry.ActiveConnectorId;

    /// <summary>
    /// Gets all registered connector IDs.
    /// </summary>
    public IReadOnlyList<string> ConnectorIds => _registry.GetAllIds();

    /// <summary>
    /// Raised when the log changes.
    /// </summary>
    public event Action? LogChanged;

    /// <summary>
    /// Raised when connection status changes.
    /// </summary>
    public event Action? StatusChanged;

    /// <summary>
    /// Initializes a new <see cref="McpStatusViewModel"/>.
    /// </summary>
    /// <param name="registry">MCP connector registry.</param>
    /// <param name="eventBus">Event bus to subscribe to MCP events.</param>
    /// <exception cref="ArgumentNullException">When a required parameter is null.</exception>
    public McpStatusViewModel(IMcpConnectorRegistry registry, IEventBus eventBus)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));

        _eventBus.Subscribe<McpConnectedEvent>(OnConnected);
        _eventBus.Subscribe<McpDisconnectedEvent>(OnDisconnected);
        _eventBus.Subscribe<McpMessageReceivedEvent>(OnMessage);
        _eventBus.Subscribe<McpErrorEvent>(OnError);
    }

    /// <summary>
    /// Clears the communication log.
    /// </summary>
    public void ClearLog()
    {
        lock (_gate) _log.Clear();
        LogChanged?.Invoke();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _eventBus.Unsubscribe<McpConnectedEvent>(OnConnected);
        _eventBus.Unsubscribe<McpDisconnectedEvent>(OnDisconnected);
        _eventBus.Unsubscribe<McpMessageReceivedEvent>(OnMessage);
        _eventBus.Unsubscribe<McpErrorEvent>(OnError);
    }

    private void AddEntry(string connectorId, McpLogEntryKind kind, string detail)
    {
        lock (_gate)
        {
            _log.Insert(0, new McpLogEntry(DateTime.UtcNow, connectorId, kind, detail));
        }
        LogChanged?.Invoke();
    }

    private void OnConnected(McpConnectedEvent e)
    {
        AddEntry(e.ConnectorId, McpLogEntryKind.Connected, "Connected");
        StatusChanged?.Invoke();
    }

    private void OnDisconnected(McpDisconnectedEvent e)
    {
        var detail = string.IsNullOrEmpty(e.Reason) ? "Disconnected" : $"Disconnected: {e.Reason}";
        AddEntry(e.ConnectorId, McpLogEntryKind.Disconnected, detail);
        StatusChanged?.Invoke();
    }

    private void OnMessage(McpMessageReceivedEvent e)
    {
        AddEntry(e.ConnectorId, McpLogEntryKind.Message, $"[{e.Method}] response received");
    }

    private void OnError(McpErrorEvent e)
    {
        AddEntry(e.ConnectorId, McpLogEntryKind.Error, e.Error);
        StatusChanged?.Invoke();
    }
}

/// <summary>
/// MCP Status extension: registers a panel showing connection status and
/// communication log. Uses <see cref="IMcpConnectorRegistry"/> from capabilities.
/// Gracefully inactive when no registry is available.
/// </summary>
public sealed class McpStatusExtension : IJunexisExtension
{
    private McpStatusViewModel? _viewModel;

    /// <inheritdoc />
    public string Id => "junexis.mcp.status";

    /// <inheritdoc />
    public void Initialize(IHostContext context)
    {
        if (!context.Capabilities.TryGet<IMcpConnectorRegistry>(out var registry) || registry is null)
        {
            context.Output.Write("MCP", "IMcpConnectorRegistry not available — Status panel inactive.");
            return;
        }

        _viewModel = new McpStatusViewModel(registry, context.EventBus);

        context.Panels.RegisterPanel(
            "junexis.mcp.status",
            PanelType.Tool,
            () => _viewModel,
            title: "MCP Status",
            dockGroup: DockGroup.Bottom);

        context.Commands.Register("mcp.status.clearLog", () =>
        {
            _viewModel.ClearLog();
        },
        menuPath: null, displayName: "Clear Log", category: "MCP", contextTarget: "junexis.mcp.status");

        context.Output.Write("MCP", "McpStatusExtension loaded.");
    }

    /// <inheritdoc />
    public void Shutdown()
    {
        _viewModel?.Dispose();
        _viewModel = null;
    }
}

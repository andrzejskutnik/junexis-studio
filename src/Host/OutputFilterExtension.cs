using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Host;

/// <summary>
/// View-model for the Output panel with channel/severity filtering and local clear.
/// Subscribes to <see cref="IOutput.EntryWritten"/> for live updates.
/// </summary>
public sealed class OutputPanelViewModel
{
    private readonly IOutput _output;
    private readonly List<OutputEntry> _allEntries = new();
    private readonly object _gate = new();

    /// <summary>
    /// Currently selected channel filter. <c>null</c> means "All channels".
    /// </summary>
    public string? SelectedChannel { get; private set; }

    /// <summary>
    /// Currently selected severity filter. <c>null</c> means "All severities".
    /// </summary>
    public OutputSeverity? SelectedSeverity { get; private set; }

    /// <summary>
    /// Filtered entries matching current channel and severity filters.
    /// </summary>
    public IReadOnlyList<OutputEntry> FilteredEntries
    {
        get
        {
            lock (_gate)
            {
                IEnumerable<OutputEntry> q = _allEntries;
                if (SelectedChannel is not null)
                    q = q.Where(e => string.Equals(e.Channel, SelectedChannel, StringComparison.Ordinal));
                if (SelectedSeverity is not null)
                    q = q.Where(e => e.Severity == SelectedSeverity.Value);
                return q.ToList();
            }
        }
    }

    /// <summary>
    /// Available channels (from the output service).
    /// </summary>
    public IReadOnlyList<string> AvailableChannels => _output.GetChannelsSnapshot();

    /// <summary>
    /// Raised when the entry list changes (new entry or clear).
    /// </summary>
    public event Action? EntriesChanged;

    /// <summary>
    /// Initializes a new <see cref="OutputPanelViewModel"/>.
    /// </summary>
    /// <param name="output">Output service to read entries from.</param>
    /// <exception cref="ArgumentNullException">When <paramref name="output"/> is null.</exception>
    public OutputPanelViewModel(IOutput output)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _output.EntryWritten += OnEntryWritten;

        // Seed with existing entries
        lock (_gate)
        {
            _allEntries.AddRange(_output.GetSnapshot());
        }
    }

    /// <summary>
    /// Sets the channel filter. Pass <c>null</c> for "All".
    /// </summary>
    /// <param name="channel">Channel name or null.</param>
    public void SetChannelFilter(string? channel)
    {
        SelectedChannel = channel;
        EntriesChanged?.Invoke();
    }

    /// <summary>
    /// Sets the severity filter. Pass <c>null</c> for "All".
    /// </summary>
    /// <param name="severity">Severity or null.</param>
    public void SetSeverityFilter(OutputSeverity? severity)
    {
        SelectedSeverity = severity;
        EntriesChanged?.Invoke();
    }

    /// <summary>
    /// Clears the local view (does not affect the underlying <see cref="IOutput"/> store).
    /// </summary>
    public void Clear()
    {
        lock (_gate)
        {
            _allEntries.Clear();
        }
        EntriesChanged?.Invoke();
    }

    /// <summary>
    /// Unsubscribes from the output service. Call on extension shutdown.
    /// </summary>
    public void Dispose()
    {
        _output.EntryWritten -= OnEntryWritten;
    }

    private void OnEntryWritten(OutputEntry entry)
    {
        lock (_gate)
        {
            _allEntries.Add(entry);
        }
        EntriesChanged?.Invoke();
    }
}

/// <summary>
/// Enhanced Output extension that registers the Output panel with channel/severity
/// filtering and clear functionality. Replaces the minimal OutputExtension panel
/// registration with a <see cref="OutputPanelViewModel"/>.
/// </summary>
public sealed class OutputFilterExtension : IJunexisExtension
{
    private OutputPanelViewModel? _viewModel;

    /// <inheritdoc />
    public string Id => "junexis.output.filter";

    /// <inheritdoc />
    public void Initialize(IHostContext context)
    {
        _viewModel = new OutputPanelViewModel(context.Output);

        context.Panels.RegisterPanel(
            "junexis.output.filtered",
            PanelType.Tool,
            () => _viewModel,
            title: "Output",
            dockGroup: DockGroup.Bottom);

        context.Commands.Register("output.clear", () => _viewModel.Clear(),
            menuPath: null, displayName: "Clear Output", category: "Output", contextTarget: "junexis.output.filtered");
        context.Commands.Register("output.filterChannel", () => { /* UI provides channel via context */ });
        context.Commands.Register("output.filterSeverity", () => { /* UI provides severity via context */ });

        context.Output.Write("Extensions", "OutputFilterExtension loaded.");
    }

    /// <inheritdoc />
    public void Shutdown()
    {
        _viewModel?.Dispose();
        _viewModel = null;
    }
}

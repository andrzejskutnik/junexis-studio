using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Host;

/// <summary>
/// Represents a visual decoration for a file in the Explorer tree.
/// </summary>
/// <param name="Path">File path.</param>
/// <param name="Badge">Short badge text (e.g. "M", "U", "A", "D", "R", "C").</param>
/// <param name="Color">Color hint for the UI (e.g. "yellow", "green", "red").</param>
/// <param name="Tooltip">Tooltip text describing the status.</param>
public sealed record FileDecoration(string Path, string Badge, string Color, string Tooltip);

/// <summary>
/// Provides VCS status decorations for files in the Explorer panel.
/// Subscribes to <see cref="VcsStatusChangedEvent"/> and recalculates decorations.
/// </summary>
public sealed class VcsDecorationProvider : IDisposable
{
    private readonly IVcsService _vcs;
    private readonly IEventBus _eventBus;
    private readonly object _gate = new();
    private IReadOnlyList<FileDecoration> _decorations = Array.Empty<FileDecoration>();

    /// <summary>
    /// Gets the current file decorations.
    /// </summary>
    public IReadOnlyList<FileDecoration> Decorations
    {
        get { lock (_gate) return _decorations; }
    }

    /// <summary>
    /// Raised when decorations are recalculated.
    /// </summary>
    public event Action? DecorationsChanged;

    /// <summary>
    /// Initializes a new <see cref="VcsDecorationProvider"/>.
    /// </summary>
    /// <param name="vcs">VCS service supplying file change data.</param>
    /// <param name="eventBus">Event bus for status change subscriptions.</param>
    /// <exception cref="ArgumentNullException">When a required parameter is null.</exception>
    public VcsDecorationProvider(IVcsService vcs, IEventBus eventBus)
    {
        _vcs = vcs ?? throw new ArgumentNullException(nameof(vcs));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _eventBus.Subscribe<VcsStatusChangedEvent>(OnStatusChanged);
        Refresh();
    }

    /// <summary>
    /// Recalculates decorations from current VCS changes.
    /// </summary>
    public void Refresh()
    {
        var changes = _vcs.GetChanges();
        var decos = new List<FileDecoration>(changes.Count);

        foreach (var change in changes)
        {
            var (badge, color, tooltip) = MapDecoration(change.Status, change.IsStaged);
            decos.Add(new FileDecoration(change.Path, badge, color, tooltip));
        }

        lock (_gate) _decorations = decos;
        DecorationsChanged?.Invoke();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _eventBus.Unsubscribe<VcsStatusChangedEvent>(OnStatusChanged);
    }

    /// <summary>
    /// Maps a <see cref="VcsFileStatus"/> to decoration info.
    /// </summary>
    /// <param name="status">File VCS status.</param>
    /// <param name="isStaged">Whether the change is staged.</param>
    /// <returns>Badge text, color hint, and tooltip.</returns>
    public static (string Badge, string Color, string Tooltip) MapDecoration(VcsFileStatus status, bool isStaged)
    {
        var suffix = isStaged ? " (staged)" : "";
        return status switch
        {
            VcsFileStatus.Modified => ("M", "yellow", $"Modified{suffix}"),
            VcsFileStatus.Added => ("A", "green", $"Added{suffix}"),
            VcsFileStatus.Deleted => ("D", "red", $"Deleted{suffix}"),
            VcsFileStatus.Renamed => ("R", "cyan", $"Renamed{suffix}"),
            VcsFileStatus.Copied => ("C", "cyan", $"Copied{suffix}"),
            VcsFileStatus.Untracked => ("U", "gray", "Untracked"),
            VcsFileStatus.Conflict => ("C", "magenta", "Conflict"),
            _ => ("?", "white", $"Unknown{suffix}")
        };
    }

    private void OnStatusChanged(VcsStatusChangedEvent _) => Refresh();
}

/// <summary>
/// Extension that registers VCS decorations for the Explorer panel.
/// Uses <see cref="IVcsService"/> from capabilities. Gracefully inactive when unavailable.
/// </summary>
public sealed class VcsDecorationExtension : IJunexisExtension
{
    private VcsDecorationProvider? _provider;

    /// <inheritdoc />
    public string Id => "junexis.vcs.decorations";

    /// <inheritdoc />
    public void Initialize(IHostContext context)
    {
        if (!context.Capabilities.TryGet<IVcsService>(out var vcs) || vcs is null)
        {
            context.Output.Write("VCS", "IVcsService not available — file decorations inactive.");
            return;
        }

        _provider = new VcsDecorationProvider(vcs, context.EventBus);

        // Register the decoration provider as a capability so the Explorer can consume it
        context.Capabilities.Register<VcsDecorationProvider>(_provider);

        context.Commands.Register("vcs.decorations.refresh", () =>
        {
            _provider.Refresh();
        },
        menuPath: null, displayName: "Refresh VCS Decorations", category: "VCS", contextTarget: "explorer");

        context.Output.Write("VCS", "VcsDecorationExtension loaded.");
    }

    /// <inheritdoc />
    public void Shutdown()
    {
        _provider?.Dispose();
        _provider = null;
    }
}

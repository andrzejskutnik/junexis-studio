using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Core;

/// <summary>
/// Serwis lifecycle zakładek dokumentów.
/// Nasłuchuje <see cref="FileOpenRequested"/> z event bus i zarządza kolekcją tabów.
/// Aktywna zakładka jest synchronizowana z <see cref="IActiveDocumentService"/>.
/// Zamknięcie taba publikuje <see cref="DocumentClosed"/> na event bus.
/// </summary>
public sealed class DocumentTabService : IDocumentTabService, IDisposable
{
    private readonly IEventBus _eventBus;
    private readonly IActiveDocumentService _activeDocument;
    private readonly object _gate = new();
    private readonly List<DocumentTab> _tabs = new();
    private DocumentTab? _activeTab;
    private readonly Action<FileOpenRequested> _onFileOpenRequested;

    /// <inheritdoc />
    public event Action? TabsChanged;

    /// <inheritdoc />
    public event Action? ActiveTabChanged;

    /// <summary>
    /// Tworzy serwis zakładek dokumentów.
    /// </summary>
    /// <param name="eventBus">Event bus do subskrypcji zdarzeń i publikacji <see cref="DocumentClosed"/>.</param>
    /// <param name="activeDocument">Serwis aktywnego dokumentu — synchronizowany z aktywną zakładką.</param>
    public DocumentTabService(IEventBus eventBus, IActiveDocumentService activeDocument)
    {
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _activeDocument = activeDocument ?? throw new ArgumentNullException(nameof(activeDocument));

        _onFileOpenRequested = OnFileOpenRequested;
        _eventBus.Subscribe(_onFileOpenRequested);
    }

    /// <inheritdoc />
    public IReadOnlyList<DocumentTab> OpenTabs
    {
        get { lock (_gate) { return _tabs.ToArray(); } }
    }

    /// <inheritdoc />
    public DocumentTab? ActiveTab
    {
        get { lock (_gate) { return _activeTab; } }
    }

    /// <inheritdoc />
    public void Open(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        bool added;
        lock (_gate)
        {
            var existing = FindTab(path);
            if (existing is not null)
            {
                SetActiveInternal(existing);
                return;
            }

            var title = Path.GetFileName(path);
            if (string.IsNullOrEmpty(title))
            {
                title = path;
            }

            var tab = new DocumentTab(path, title);
            _tabs.Add(tab);
            added = true;
            SetActiveInternal(tab);
        }

        if (added)
        {
            TabsChanged?.Invoke();
        }
    }

    /// <inheritdoc />
    public void Activate(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        lock (_gate)
        {
            var tab = FindTab(path);
            if (tab is null)
            {
                return;
            }

            SetActiveInternal(tab);
        }
    }

    /// <inheritdoc />
    public void Close(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        DocumentTab? removedTab;
        bool wasActive;

        lock (_gate)
        {
            var idx = FindTabIndex(path);
            if (idx < 0)
            {
                return;
            }

            removedTab = _tabs[idx];
            wasActive = _activeTab is not null
                        && string.Equals(_activeTab.Path, path, StringComparison.OrdinalIgnoreCase);

            _tabs.RemoveAt(idx);

            if (wasActive)
            {
                if (_tabs.Count > 0)
                {
                    var nextIdx = Math.Min(idx, _tabs.Count - 1);
                    SetActiveInternal(_tabs[nextIdx]);
                }
                else
                {
                    _activeTab = null;
                    _activeDocument.SetActivePath(null);
                    ActiveTabChanged?.Invoke();
                }
            }
        }

        TabsChanged?.Invoke();
        _eventBus.Publish(new DocumentClosed(removedTab!.Path));
    }

    /// <inheritdoc cref="IDisposable.Dispose"/>
    public void Dispose()
    {
        _eventBus.Unsubscribe(_onFileOpenRequested);
    }

    // ─── Internals ───────────────────────────────────────

    private void OnFileOpenRequested(FileOpenRequested evt)
    {
        if (evt is not null && !string.IsNullOrWhiteSpace(evt.Path))
        {
            Open(evt.Path);
        }
    }

    /// <summary>
    /// Ustawia aktywny tab i synchronizuje <see cref="IActiveDocumentService"/>.
    /// Musi być wywoływana wewnątrz lock(_gate).
    /// </summary>
    private void SetActiveInternal(DocumentTab tab)
    {
        if (_activeTab is not null
            && string.Equals(_activeTab.Path, tab.Path, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _activeTab = tab;
        _activeDocument.SetActivePath(tab.Path);
        ActiveTabChanged?.Invoke();
    }

    private DocumentTab? FindTab(string path)
    {
        foreach (var tab in _tabs)
        {
            if (string.Equals(tab.Path, path, StringComparison.OrdinalIgnoreCase))
            {
                return tab;
            }
        }

        return null;
    }

    private int FindTabIndex(string path)
    {
        for (var i = 0; i < _tabs.Count; i++)
        {
            if (string.Equals(_tabs[i].Path, path, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }
}

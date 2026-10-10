using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Core;

/// <summary>
/// Implementacja <see cref="IOpenDocumentRepository"/> utrzymywana na podstawie zdarzeń domenowych.
/// </summary>
public sealed class OpenDocumentRepository : IOpenDocumentRepository, IDisposable
{
    private readonly IEventBus _eventBus;
    private readonly IWorkspace _workspace;

    private readonly object _gate = new();
    private readonly Dictionary<string, Document> _documents = new(StringComparer.OrdinalIgnoreCase);

    private readonly Action<DocumentOpened> _onOpened;
    private readonly Action<DocumentChanged> _onChanged;
    private readonly Action<DocumentSaved> _onSaved;
    private readonly Action<FileChangedExternally> _onFileChangedExternally;
    private readonly Action<FileRenamedExternally> _onFileRenamedExternally;

    /// <summary>
    /// Tworzy repozytorium i podpina subskrypcje na EventBus.
    /// </summary>
    /// <param name="eventBus">EventBus do odbioru zdarzeń.</param>
    /// <param name="workspace">Workspace do odczytu treści z dysku przy zmianach zewnętrznych.</param>
    public OpenDocumentRepository(IEventBus eventBus, IWorkspace workspace)
    {
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));

        _onOpened = OnDocumentOpened;
        _onChanged = OnDocumentChanged;
        _onSaved = OnDocumentSaved;
        _onFileChangedExternally = OnFileChangedExternally;
        _onFileRenamedExternally = OnFileRenamedExternally;

        _eventBus.Subscribe(_onOpened);
        _eventBus.Subscribe(_onChanged);
        _eventBus.Subscribe(_onSaved);
        _eventBus.Subscribe(_onFileChangedExternally);
        _eventBus.Subscribe(_onFileRenamedExternally);
    }

    /// <inheritdoc />
    public IReadOnlyList<Document> GetOpenDocuments()
    {
        lock (_gate)
        {
            return _documents.Values.ToArray();
        }
    }

    /// <inheritdoc />
    public bool TryGetByPath(string path, out Document document)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            document = default!;
            return false;
        }

        lock (_gate)
        {
            return _documents.TryGetValue(path, out document!);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _eventBus.Unsubscribe(_onOpened);
        _eventBus.Unsubscribe(_onChanged);
        _eventBus.Unsubscribe(_onSaved);
        _eventBus.Unsubscribe(_onFileChangedExternally);
        _eventBus.Unsubscribe(_onFileRenamedExternally);
    }

    private void OnDocumentOpened(DocumentOpened evt)
    {
        if (evt is null || string.IsNullOrWhiteSpace(evt.Path))
        {
            return;
        }

        var doc = new Document(
            Id: DocumentId.New(),
            FilePath: evt.Path,
            Content: evt.Content ?? string.Empty,
            IsDirty: false,
            LastSavedTimestamp: DateTimeOffset.UtcNow);

        lock (_gate)
        {
            _documents[evt.Path] = doc;
        }
    }

    private void OnDocumentChanged(DocumentChanged evt)
    {
        if (evt is null || string.IsNullOrWhiteSpace(evt.Path))
        {
            return;
        }

        lock (_gate)
        {
            if (!_documents.TryGetValue(evt.Path, out var existing))
            {
                existing = new Document(
                    Id: DocumentId.New(),
                    FilePath: evt.Path,
                    Content: evt.Content ?? string.Empty,
                    IsDirty: true,
                    LastSavedTimestamp: null);

                _documents[evt.Path] = existing;
                return;
            }

            _documents[evt.Path] = existing with
            {
                Content = evt.Content ?? string.Empty,
                IsDirty = true,
            };
        }
    }

    private void OnDocumentSaved(DocumentSaved evt)
    {
        if (evt is null || string.IsNullOrWhiteSpace(evt.Path))
        {
            return;
        }

        lock (_gate)
        {
            if (!_documents.TryGetValue(evt.Path, out var existing))
            {
                return;
            }

            _documents[evt.Path] = existing with
            {
                IsDirty = false,
                LastSavedTimestamp = DateTimeOffset.UtcNow,
            };
        }
    }

    private void OnFileChangedExternally(FileChangedExternally evt)
    {
        if (evt is null || string.IsNullOrWhiteSpace(evt.Path))
        {
            return;
        }

        Document? existing;

        lock (_gate)
        {
            _documents.TryGetValue(evt.Path, out existing);
        }

        if (existing is null || existing.IsDirty)
        {
            return;
        }

        try
        {
            var content = _workspace.ReadFile(evt.Path);

            lock (_gate)
            {
                if (_documents.TryGetValue(evt.Path, out var current) && !current.IsDirty)
                {
                    _documents[evt.Path] = current with
                    {
                        Content = content,
                        LastSavedTimestamp = DateTimeOffset.UtcNow,
                    };
                }
            }
        }
        catch
        {
            // v0.1: w razie problemów z IO nie aktualizujemy modelu.
        }
    }

    private void OnFileRenamedExternally(FileRenamedExternally evt)
    {
        if (evt is null || string.IsNullOrWhiteSpace(evt.OldPath) || string.IsNullOrWhiteSpace(evt.NewPath))
        {
            return;
        }

        Document? existing;

        lock (_gate)
        {
            _documents.TryGetValue(evt.OldPath, out existing);
        }

        if (existing is null || existing.IsDirty)
        {
            return;
        }

        try
        {
            var content = _workspace.ReadFile(evt.NewPath);

            lock (_gate)
            {
                if (_documents.TryGetValue(evt.OldPath, out var current) && !current.IsDirty)
                {
                    _documents.Remove(evt.OldPath);
                    _documents[evt.NewPath] = current with
                    {
                        FilePath = evt.NewPath,
                        Content = content,
                        LastSavedTimestamp = DateTimeOffset.UtcNow,
                    };
                }
            }
        }
        catch
        {
            // v0.1: jak wyżej.
        }
    }
}

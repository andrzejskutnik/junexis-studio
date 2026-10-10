using Junexis.Studio.Abstractions;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Junexis.Studio.Host;

/// <summary>
/// ViewModel kontenera zakładek edytora — zarządza kolekcją otwartych dokumentów
/// i synchronizuje aktywną zakładkę z <see cref="IActiveDocumentService"/>.
/// </summary>
public sealed class EditorPanelViewModel : INotifyPropertyChanged
{
    private readonly IEventBus _eventBus;
    private readonly IOutput _output;
    private readonly IWorkspace _workspace;
    private readonly IActiveDocumentService _activeDocument;

    private DocumentTabViewModel? _activeTab;

    /// <summary>
    /// Tworzy kontener zakładek edytora i podpina subskrypcje na zdarzenia.
    /// </summary>
    /// <param name="workspace">Workspace do IO.</param>
    /// <param name="eventBus">EventBus do subskrypcji zdarzeń.</param>
    /// <param name="output">Output do logowania problemów.</param>
    /// <param name="activeDocument">Serwis aktywnego dokumentu.</param>
    public EditorPanelViewModel(IWorkspace workspace, IEventBus eventBus, IOutput output, IActiveDocumentService activeDocument)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _activeDocument = activeDocument ?? throw new ArgumentNullException(nameof(activeDocument));

        Tabs = new ObservableCollection<DocumentTabViewModel>();
        Tabs.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasOpenTabs));

        _eventBus.Subscribe<FileOpenRequested>(OnFileOpenRequested);
        _eventBus.Subscribe<FilePreviewRequested>(OnFilePreviewRequested);
        _eventBus.Subscribe<NavigateToLocationRequested>(OnNavigateToLocationRequested);
        _eventBus.Subscribe<FileChangedExternally>(OnFileChangedExternally);
        _eventBus.Subscribe<FileDeletedExternally>(OnFileDeletedExternally);
        _eventBus.Subscribe<FileRenamedExternally>(OnFileRenamedExternally);
        _eventBus.Subscribe<FileRenamedEvent>(OnFileRenamedFromExplorer);
        _eventBus.Subscribe<ReplaceOpenDocumentContentRequested>(OnReplaceOpenDocumentContentRequested);
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Kolekcja otwartych zakładek dokumentów.
    /// </summary>
    public ObservableCollection<DocumentTabViewModel> Tabs { get; }

    /// <summary>
    /// Czy aktualnie istnieją otwarte zakładki.
    /// </summary>
    public bool HasOpenTabs => Tabs.Count > 0;

    /// <summary>
    /// Aktywna zakładka dokumentu (lub <see langword="null"/> gdy brak otwartych tabów).
    /// Zmiana aktywnej zakładki synchronizuje <see cref="IActiveDocumentService"/>
    /// i przekierowuje <see cref="PropertyChanged"/> z właściwości fasadowych.
    /// </summary>
    public DocumentTabViewModel? ActiveTab
    {
        get => _activeTab;
        set
        {
            var old = _activeTab;
            if (!SetField(ref _activeTab, value))
            {
                return;
            }

            if (old is not null)
            {
                old.PropertyChanged -= OnActiveTabPropertyChanged;
            }

            if (value is not null)
            {
                value.PropertyChanged += OnActiveTabPropertyChanged;
            }

            _activeDocument.SetActivePath(value?.FilePath);

            // Powiadom UI o zmianie wartości wszystkich właściwości fasadowych.
            OnPropertyChanged(nameof(CurrentPath));
            OnPropertyChanged(nameof(Content));
            OnPropertyChanged(nameof(IsDirty));
            OnPropertyChanged(nameof(IsOutOfDate));
            OnPropertyChanged(nameof(RequestedLine));
            OnPropertyChanged(nameof(RequestedColumn));
            OnPropertyChanged(nameof(NavigationRequestVersion));
            OnPropertyChanged(nameof(StatusText));
        }
    }

    // ─── Właściwości fasadowe (backward-compatible z single-tab API) ────

    /// <summary>
    /// Aktualnie otwarty plik (pełna ścieżka) lub <see langword="null"/>.
    /// Deleguje do <see cref="ActiveTab"/>.
    /// </summary>
    public string? CurrentPath => ActiveTab?.FilePath;

    /// <summary>
    /// Aktualna treść edytora. Deleguje do <see cref="ActiveTab"/>.
    /// </summary>
    public string Content
    {
        get => ActiveTab?.Content ?? string.Empty;
        set
        {
            if (ActiveTab is not null)
            {
                ActiveTab.Content = value;
            }
        }
    }

    /// <summary>
    /// Czy w edytorze są niezapisane zmiany. Deleguje do <see cref="ActiveTab"/>.
    /// </summary>
    public bool IsDirty => ActiveTab?.IsDirty ?? false;

    /// <summary>
    /// Czy dokument może być nieaktualny. Deleguje do <see cref="ActiveTab"/>.
    /// </summary>
    public bool IsOutOfDate => ActiveTab?.IsOutOfDate ?? false;

    /// <summary>
    /// Ostatnio żądana linia (1-based). Deleguje do <see cref="ActiveTab"/>.
    /// </summary>
    public int? RequestedLine => ActiveTab?.RequestedLine;

    /// <summary>
    /// Ostatnio żądana kolumna (1-based). Deleguje do <see cref="ActiveTab"/>.
    /// </summary>
    public int? RequestedColumn => ActiveTab?.RequestedColumn;

    /// <summary>
    /// Licznik żądań nawigacji. Deleguje do <see cref="ActiveTab"/>.
    /// </summary>
    public int NavigationRequestVersion => ActiveTab?.NavigationRequestVersion ?? 0;

    /// <summary>
    /// Tekst paska stanu. Format: <c>● filename  Ln X, Col Y</c> (dirty) lub <c>filename  Ln X, Col Y</c>.
    /// Pusty string gdy brak aktywnej zakładki.
    /// </summary>
    public string StatusText
    {
        get
        {
            var tab = ActiveTab;
            if (tab is null)
                return string.Empty;

            var dirty = tab.IsDirty ? "● " : string.Empty;
            return $"{dirty}{tab.FileName}  Ln {tab.CaretLine}, Col {tab.CaretColumn}";
        }
    }

    /// <summary>
    /// Zapisuje aktywny dokument.
    /// </summary>
    public void Save()
    {
        ActiveTab?.Save();
    }

    /// <summary>
    /// Przełącza panel wyszukiwania w aktywnej zakładce.
    /// </summary>
    public void ToggleSearch()
    {
        ActiveTab?.ToggleSearch();
    }

    /// <summary>
    /// Otwiera panel wyszukiwania i zamiany w aktywnej zakładce.
    /// </summary>
    public void OpenReplace()
    {
        ActiveTab?.OpenReplace();
    }

    /// <summary>
    /// Przechodzi do następnego wystąpienia frazy wyszukiwania w aktywnej zakładce.
    /// </summary>
    public void FindNext()
    {
        ActiveTab?.FindNext();
    }

    /// <summary>
    /// Przechodzi do poprzedniego wystąpienia frazy wyszukiwania w aktywnej zakładce.
    /// </summary>
    public void FindPrev()
    {
        ActiveTab?.FindPrev();
    }

    /// <summary>
    /// Zastępuje następne wystąpienie frazy wyszukiwania w aktywnej zakładce.
    /// </summary>
    public void ReplaceNext()
    {
        ActiveTab?.ReplaceNext();
    }

    /// <summary>
    /// Zastępuje wszystkie wystąpienia frazy wyszukiwania w aktywnej zakładce.
    /// </summary>
    public void ReplaceAll()
    {
        ActiveTab?.ReplaceAll();
    }

    /// <summary>
    /// Aktywuje następną zakładkę, zawijając do początku kolekcji.
    /// </summary>
    public void ActivateNextTab()
    {
        ActivateRelativeTab(offset: 1);
    }

    /// <summary>
    /// Aktywuje poprzednią zakładkę, zawijając do końca kolekcji.
    /// </summary>
    public void ActivatePreviousTab()
    {
        ActivateRelativeTab(offset: -1);
    }

    /// <summary>
    /// Opcjonalny delegat potwierdzenia zamknięcia niezapisanej zakładki.
    /// Jeśli nie ustawiony, niezapisane zmiany są porzucane bez pytania.
    /// Występujący typ: <see cref="Func{T, TResult}"/>
    /// gdzie T = <see cref="DocumentTabViewModel"/>, TResult = <see cref="Task{UnsavedChangesResult}"/>.
    /// </summary>
    public Func<DocumentTabViewModel, Task<UnsavedChangesResult>>? ConfirmCloseAsync { get; set; }

    /// <summary>
    /// Zamyka wskazaną zakładkę i publikuje <see cref="DocumentClosed"/>.
    /// Jeśli zamykany tab był aktywny, aktywuje sąsiedni.
    /// </summary>
    /// <param name="tab">Zakładka do zamknięcia.</param>
    public void CloseTab(DocumentTabViewModel tab)
    {
        if (tab is null)
        {
            return;
        }

        var wasActive = _activeTab == tab;

        tab.CloseRequested -= OnTabCloseRequested;
        tab.PropertyChanged -= OnActiveTabPropertyChanged;

        var idx = Tabs.IndexOf(tab);
        Tabs.Remove(tab);

        _eventBus.Publish(new DocumentClosed(tab.FilePath));

        if (wasActive)
        {
            if (Tabs.Count > 0)
            {
                var nextIdx = Math.Min(idx, Tabs.Count - 1);
                ActiveTab = Tabs[nextIdx];
            }
            else
            {
                ActiveTab = null;
            }
        }
    }

    /// <summary>Zamyka wszystkie zakładki poza <paramref name="keepTab"/>.</summary>
    /// <param name="keepTab">Zakładka, która ma pozostać otwarta.</param>
    public async Task CloseOtherTabsAsync(DocumentTabViewModel keepTab)
    {
        foreach (var tab in Tabs.Where(t => t != keepTab).ToArray())
            await CloseTabWithConfirmAsync(tab);
    }

    /// <summary>Zamyka zakładki znajdujące się po prawej stronie <paramref name="anchorTab"/>.</summary>
    /// <param name="anchorTab">Zakładka referencyjna (ona sama pozostaje otwarta).</param>
    public async Task CloseTabsToTheRightAsync(DocumentTabViewModel anchorTab)
    {
        var idx = Tabs.IndexOf(anchorTab);
        if (idx < 0) return;
        foreach (var tab in Tabs.Skip(idx + 1).ToArray())
            await CloseTabWithConfirmAsync(tab);
    }

    private void ActivateRelativeTab(int offset)
    {
        if (Tabs.Count == 0)
        {
            return;
        }

        if (ActiveTab is null)
        {
            ActiveTab = Tabs[0];
            return;
        }

        var currentIndex = Tabs.IndexOf(ActiveTab);
        if (currentIndex < 0)
        {
            ActiveTab = Tabs[0];
            return;
        }

        var nextIndex = (currentIndex + offset + Tabs.Count) % Tabs.Count;
        ActiveTab = Tabs[nextIndex];
    }

    // ─── Handlery zdarzeń ────────────────────────────────

    private void OnFilePreviewRequested(FilePreviewRequested request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Path)) return;
        var path = request.Path;

        // If already open as a permanent tab, just activate it.
        var existing = FindTab(path);
        if (existing is { IsPreview: false })
        {
            ActiveTab = existing;
            return;
        }

        // Close any existing preview tab (it's never dirty).
        var currentPreview = Tabs.FirstOrDefault(t => t.IsPreview);
        if (currentPreview is not null && !string.Equals(currentPreview.FilePath, path, StringComparison.OrdinalIgnoreCase))
            CloseTab(currentPreview);

        // If same file is already the preview tab, just activate.
        var samePreview = FindTab(path);
        if (samePreview is not null)
        {
            ActiveTab = samePreview;
            return;
        }

        string content;
        try
        {
            content = _workspace.ReadFile(path);
        }
        catch (Exception ex)
        {
            _output.Write("Editor", $"Preview failed: {ex.Message}");
            return;
        }

        var tab = new DocumentTabViewModel(path, content, _workspace, _eventBus, _output) { };
        // Mark as preview — use reflection-free internal approach: tab exposes IsPreview internally.
        tab.CloseRequested += OnTabCloseRequested;
        Tabs.Add(tab);
        ActiveTab = tab;
        // Set preview flag after adding so UI gets the update.
        tab.SetPreview(true);

        _eventBus.Publish(new DocumentOpened(path, content));
    }

    private void OnFileOpenRequested(FileOpenRequested request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Path))
        {
            return;
        }

        var path = request.Path;

        // Jeśli tab już istnieje, aktywuj go.
        var existing = FindTab(path);
        if (existing is not null)
        {
            ActiveTab = existing;
            return;
        }

        // Wczytaj treść pliku.
        string content;
        try
        {
            try
            {
                content = _workspace.ReadFile(path);
            }
            catch (UnauthorizedAccessException)
            {
                // Plik poza workspace (np. globalne preferencje) — odczyt bezpośredni
                content = File.ReadAllText(path);
            }
        }
        catch (Exception ex)
        {
            _output.Write("Editor", $"Open failed: {ex.Message}");
            return;
        }

        var tab = new DocumentTabViewModel(path, content, _workspace, _eventBus, _output);
        tab.CloseRequested += OnTabCloseRequested;
        Tabs.Add(tab);
        ActiveTab = tab;

        _eventBus.Publish(new DocumentOpened(path, content));
    }

    private void OnNavigateToLocationRequested(NavigateToLocationRequested request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Path))
        {
            return;
        }

        var tab = FindTab(request.Path);
        if (tab is null)
        {
            OnFileOpenRequested(new FileOpenRequested(request.Path));
            tab = FindTab(request.Path);
            if (tab is null)
            {
                return;
            }
        }

        ActiveTab = tab;
        tab.NavigateTo(request.Line, request.Column);
    }

    private void OnFileChangedExternally(FileChangedExternally evt)
    {
        if (evt is null || string.IsNullOrWhiteSpace(evt.Path))
        {
            return;
        }

        FindTab(evt.Path)?.HandleExternalChange();
    }

    private void OnFileDeletedExternally(FileDeletedExternally evt)
    {
        if (evt is null || string.IsNullOrWhiteSpace(evt.Path))
        {
            return;
        }

        FindTab(evt.Path)?.HandleExternalDelete();
    }

    private void OnFileRenamedExternally(FileRenamedExternally evt)
    {
        if (evt is null || string.IsNullOrWhiteSpace(evt.OldPath) || string.IsNullOrWhiteSpace(evt.NewPath))
        {
            return;
        }

        FindTab(evt.OldPath)?.HandleExternalRename(evt.NewPath);
    }

    private void OnFileRenamedFromExplorer(FileRenamedEvent evt)
    {
        if (evt is null || string.IsNullOrWhiteSpace(evt.OldPath) || string.IsNullOrWhiteSpace(evt.NewPath))
        {
            return;
        }

        FindTab(evt.OldPath)?.HandleExternalRename(evt.NewPath);
    }

    private void OnReplaceOpenDocumentContentRequested(ReplaceOpenDocumentContentRequested request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Path))
            return;
        var tab = FindTab(request.Path);
        if (tab is not null)
            tab.Content = request.Content ?? string.Empty;
    }

    private async void OnTabCloseRequested(DocumentTabViewModel tab)
    {
        await CloseTabWithConfirmAsync(tab);
    }

    /// <summary>
    /// Zamyka zakładkę z opcjonalnym dialogiem potwierdzenia dla niezapisanych zmian.
    /// Jeśli <see cref="ConfirmCloseAsync"/> nie jest ustawiony, porzuca zmiany bez pytania.
    /// </summary>
    /// <param name="tab">Zakładka do zamknięcia.</param>
    public async Task CloseTabWithConfirmAsync(DocumentTabViewModel tab)
    {
        if (tab is null)
        {
            return;
        }

        if (tab.IsDirty && ConfirmCloseAsync is not null)
        {
            var result = await ConfirmCloseAsync(tab);
            switch (result)
            {
                case UnsavedChangesResult.Save:
                    tab.Save();
                    break;
                case UnsavedChangesResult.Cancel:
                    return;
                case UnsavedChangesResult.Discard:
                default:
                    break;
            }
        }

        CloseTab(tab);
    }

    private void OnActiveTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Przekieruj zmiany z aktywnego taba na właściwości fasadowe
        switch (e.PropertyName)
        {
            case nameof(DocumentTabViewModel.Content):
                OnPropertyChanged(nameof(Content));
                break;
            case nameof(DocumentTabViewModel.FilePath):
                OnPropertyChanged(nameof(CurrentPath));
                break;
            case nameof(DocumentTabViewModel.FileName):
                OnPropertyChanged(nameof(StatusText));
                break;
            case nameof(DocumentTabViewModel.IsDirty):
                OnPropertyChanged(nameof(IsDirty));
                OnPropertyChanged(nameof(StatusText));
                break;
            case nameof(DocumentTabViewModel.IsOutOfDate):
                OnPropertyChanged(nameof(IsOutOfDate));
                break;
            case nameof(DocumentTabViewModel.RequestedLine):
                OnPropertyChanged(nameof(RequestedLine));
                break;
            case nameof(DocumentTabViewModel.RequestedColumn):
                OnPropertyChanged(nameof(RequestedColumn));
                break;
            case nameof(DocumentTabViewModel.NavigationRequestVersion):
                OnPropertyChanged(nameof(NavigationRequestVersion));
                break;
            case nameof(DocumentTabViewModel.CaretLine):
            case nameof(DocumentTabViewModel.CaretColumn):
                OnPropertyChanged(nameof(StatusText));
                break;
        }
    }

    // ─── Helpers ─────────────────────────────────────────

    private DocumentTabViewModel? FindTab(string path)
    {
        foreach (var tab in Tabs)
        {
            if (string.Equals(tab.FilePath, path, StringComparison.OrdinalIgnoreCase))
            {
                return tab;
            }
        }

        return null;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}

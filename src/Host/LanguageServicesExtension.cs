using System.ComponentModel;
using System.Runtime.CompilerServices;
using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Host;

/// <summary>Wiersz wyniku referencji prezentowany w panelu Language Services.</summary>
/// <param name="Location">Lokacja źródłowa LSP.</param>
/// <param name="DisplayPath">Ścieżka względna lub nazwa pliku.</param>
/// <param name="Line">Linia liczona od jednego.</param>
/// <param name="Column">Kolumna liczona od jednego.</param>
/// <param name="Preview">Opcjonalny podgląd wiersza źródłowego.</param>
public sealed record LanguageReferenceViewItem(
    LanguageLocation Location,
    string DisplayPath,
    int Line,
    int Column,
    string? Preview)
{
    /// <summary>Zwarta lokacja używana w widoku listy.</summary>
    public string DisplayLocation => $"{DisplayPath}:{Line}:{Column}";
}

/// <summary>ViewModel panelu procesów LSP i wyników Find References.</summary>
public sealed class LanguageServicesViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly ILanguageService _languageService;
    private readonly IEventBus _eventBus;
    private readonly IWorkspace _workspace;
    private readonly IOutput _output;
    private readonly SynchronizationContext? _synchronizationContext;
    private IReadOnlyList<LanguageServerStatus> _statuses = [];
    private LanguageServerStatus? _selectedStatus;
    private IReadOnlyList<LanguageReferenceViewItem> _references = [];
    private LanguageReferenceViewItem? _selectedReference;
    private string _referencesTitle = "References";
    private string _statusText = "Language servers start when a supported document is opened.";
    private bool _isBusy;
    private bool _disposed;

    /// <summary>Tworzy ViewModel panelu usług językowych.</summary>
    public LanguageServicesViewModel(
        ILanguageService languageService,
        IEventBus eventBus,
        IWorkspace workspace,
        IOutput output)
    {
        _languageService = languageService ?? throw new ArgumentNullException(nameof(languageService));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _synchronizationContext = SynchronizationContext.Current;
        _languageService.StatusChanged += OnStatusChanged;
        _languageService.ReferencesPublished += OnReferencesPublished;
        RefreshStatuses();
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Snapshot wszystkich profili i ich stanów.</summary>
    public IReadOnlyList<LanguageServerStatus> Statuses
    {
        get => _statuses;
        private set => SetField(ref _statuses, value);
    }

    /// <summary>Profil wybrany do ręcznego restartu.</summary>
    public LanguageServerStatus? SelectedStatus
    {
        get => _selectedStatus;
        set => SetField(ref _selectedStatus, value);
    }

    /// <summary>Ostatni kompletny zestaw znalezionych referencji.</summary>
    public IReadOnlyList<LanguageReferenceViewItem> References
    {
        get => _references;
        private set
        {
            if (SetField(ref _references, value))
                OnPropertyChanged(nameof(HasReferences));
        }
    }

    /// <summary>Aktualnie wybrana referencja.</summary>
    public LanguageReferenceViewItem? SelectedReference
    {
        get => _selectedReference;
        set => SetField(ref _selectedReference, value);
    }

    /// <summary>Nagłówek ostatniego wyniku wyszukiwania.</summary>
    public string ReferencesTitle
    {
        get => _referencesTitle;
        private set => SetField(ref _referencesTitle, value);
    }

    /// <summary>Tekst podsumowujący ostatnią operację panelu.</summary>
    public string StatusText
    {
        get => _statusText;
        private set => SetField(ref _statusText, value);
    }

    /// <summary>Czy trwa ręczny restart serwera.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set => SetField(ref _isBusy, value);
    }

    /// <summary>Czy panel zawiera co najmniej jedną referencję.</summary>
    public bool HasReferences => References.Count > 0;

    /// <summary>Odświeża snapshot stanów z usługi lifecycle.</summary>
    public void RefreshStatuses()
    {
        Update(() =>
        {
            var selectedId = SelectedStatus?.ProfileId;
            Statuses = _languageService.GetStatuses()
                .OrderBy(status => status.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            SelectedStatus = Statuses.FirstOrDefault(status =>
                    status.ProfileId.Equals(selectedId, StringComparison.OrdinalIgnoreCase))
                ?? Statuses.FirstOrDefault();
        });
    }

    /// <summary>Restartuje profil zaznaczony w panelu.</summary>
    public async Task RestartSelectedAsync(CancellationToken cancellationToken = default)
    {
        var selected = SelectedStatus;
        if (selected is null || IsBusy)
            return;
        Update(() =>
        {
            IsBusy = true;
            StatusText = $"Restarting {selected.DisplayName}…";
        });
        try
        {
            await _languageService.RestartProfileAsync(selected.ProfileId, cancellationToken)
                .ConfigureAwait(false);
            Update(() => StatusText = $"{selected.DisplayName} restarted.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Update(() => StatusText = "Language server restart cancelled.");
        }
        catch (Exception ex)
        {
            Update(() => StatusText = $"Restart failed: {ex.Message}");
            _output.Write("Language Services", StatusText, OutputSeverity.Warning);
        }
        finally
        {
            Update(() => IsBusy = false);
            RefreshStatuses();
        }
    }

    /// <summary>Nawiguje do zaznaczonej referencji.</summary>
    public void NavigateToSelectedReference()
    {
        var selected = SelectedReference;
        if (selected is null)
            return;
        _eventBus.Publish(new NavigateToLocationRequested(
            selected.Location.Path,
            selected.Line,
            selected.Column));
    }

    /// <summary>Usuwa aktualny wynik referencji z panelu.</summary>
    public void ClearReferences()
    {
        References = [];
        SelectedReference = null;
        ReferencesTitle = "References";
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _languageService.StatusChanged -= OnStatusChanged;
        _languageService.ReferencesPublished -= OnReferencesPublished;
    }

    private void OnStatusChanged(LanguageServerStatus status)
    {
        RefreshStatuses();
        Update(() => StatusText = $"{status.DisplayName}: {status.State} — {status.Message}");
    }

    private void OnReferencesPublished(LanguageReferencesPublished result)
    {
        var items = result.Locations
            .Select(CreateReferenceItem)
            .OrderBy(item => item.DisplayPath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Line)
            .ThenBy(item => item.Column)
            .ToArray();
        Update(() =>
        {
            References = items;
            SelectedReference = items.FirstOrDefault();
            var symbol = string.IsNullOrWhiteSpace(result.Symbol) ? "symbol" : result.Symbol;
            ReferencesTitle = $"References — {symbol} ({items.Length})";
            StatusText = items.Length == 0
                ? $"No references found for {symbol}."
                : $"Found {items.Length} references for {symbol}.";
        });
    }

    private LanguageReferenceViewItem CreateReferenceItem(LanguageLocation location)
    {
        var fullPath = Path.GetFullPath(location.Path);
        var displayPath = fullPath;
        if (!string.IsNullOrWhiteSpace(_workspace.RootPath))
        {
            var relative = Path.GetRelativePath(_workspace.RootPath, fullPath);
            if (!relative.StartsWith("..", StringComparison.Ordinal))
                displayPath = relative;
        }

        string? preview = null;
        try
        {
            if (File.Exists(fullPath))
            {
                preview = File.ReadLines(fullPath)
                    .Skip(location.Position.Line)
                    .FirstOrDefault()
                    ?.Trim();
            }
        }
        catch
        {
            // Preview is optional; navigation still uses the LSP location.
        }

        return new LanguageReferenceViewItem(
            location,
            displayPath,
            location.Position.Line + 1,
            location.Position.Character + 1,
            preview);
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
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

internal sealed class LanguageServicesExtension : IJunexisExtension
{
    private LanguageServicesViewModel? _viewModel;

    public string Id => "junexis.language-services";

    public void Initialize(IHostContext context)
    {
        if (!context.Capabilities.TryGet<ILanguageService>(out var languageService)
            || languageService is null)
        {
            context.Output.Write("Language Services", "ILanguageService unavailable — panel inactive.");
            return;
        }

        _viewModel = new LanguageServicesViewModel(
            languageService,
            context.EventBus,
            context.Workspace,
            context.Output);
        context.Panels.RegisterPanel(
            Id,
            PanelType.Tool,
            () => _viewModel,
            title: "Language Services",
            dockGroup: DockGroup.Bottom);
        context.Commands.Register(
            "language.services.open",
            () => context.EventBus.Publish(new ActivatePanelRequested(Id)),
            menuPath: "View/Language Services",
            displayName: "Language Services",
            category: "View");
    }

    public void Shutdown()
    {
        _viewModel?.Dispose();
        _viewModel = null;
    }
}

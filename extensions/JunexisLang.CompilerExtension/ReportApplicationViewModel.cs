using System.ComponentModel;
using System.Runtime.CompilerServices;
using Junexis.Studio.Abstractions;

namespace JunexisLang.Studio.CompilerExtension;

/// <summary>Model panelu Studio zarządzającego aplikacją Junexis.</summary>
public sealed class ReportApplicationViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IReportApplicationSession _session;
    private readonly IWorkspace _workspace;
    private readonly IActiveDocumentService _activeDocument;
    private readonly SynchronizationContext? _synchronizationContext;
    private ReportApplicationSnapshot _snapshot;

    /// <summary>Tworzy model powiązany z sesją, obszarem roboczym i aktywnym dokumentem.</summary>
    /// <param name="session">Sesja aplikacji Junexis.</param>
    /// <param name="workspace">Aktualny obszar roboczy Studio.</param>
    /// <param name="activeDocument">Serwis aktywnego dokumentu.</param>
    public ReportApplicationViewModel(
        IReportApplicationSession session,
        IWorkspace workspace,
        IActiveDocumentService activeDocument)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _activeDocument = activeDocument ?? throw new ArgumentNullException(nameof(activeDocument));
        _synchronizationContext = SynchronizationContext.Current;
        _snapshot = session.Current;
        _session.Changed += OnSessionChanged;
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Czytelny stan prezentowany użytkownikowi.</summary>
    public string Status => _snapshot.Status;

    /// <summary>Adres działającego panelu albo informacja o oczekiwaniu.</summary>
    public string Url => _snapshot.Url?.AbsoluteUri
        ?? (_snapshot.State == ReportApplicationState.Ready ? "Aplikacja nie deklaruje panelu." : "Adres pojawi się po uruchomieniu.");

    /// <summary>Czy można rozpocząć nową sesję.</summary>
    public bool CanStart => _snapshot.State is ReportApplicationState.Idle or ReportApplicationState.Failed;

    /// <summary>Czy można zatrzymać bieżącą sesję.</summary>
    public bool CanStop => _snapshot.State is ReportApplicationState.Starting or ReportApplicationState.Ready;

    /// <summary>Czy panel ma adres, który można otworzyć.</summary>
    public bool CanOpen => _snapshot.Url is not null;

    /// <summary>Uruchamia aplikację z bieżącego projektu na maksymalnie pięć minut.</summary>
    /// <param name="cancellationToken">Token anulowania oczekiwania.</param>
    /// <returns>Zadanie trwające przez cały cykl życia sesji.</returns>
    public Task StartAsync(CancellationToken cancellationToken = default) =>
        _session.StartAsync(
            _workspace.RootPath ?? string.Empty,
            _activeDocument.ActivePath,
            durationSeconds: 300,
            cancellationToken);

    /// <summary>Otwiera gotowy panel w domyślnej przeglądarce.</summary>
    /// <returns><see langword="true"/>, gdy polecenie zostało przekazane systemowi.</returns>
    public bool Open() => _session.Open();

    /// <summary>Zatrzymuje aplikację i wszystkie należące do niej procesy.</summary>
    public void Stop() => _session.Stop();

    /// <inheritdoc />
    public void Dispose() => _session.Changed -= OnSessionChanged;

    private void OnSessionChanged(ReportApplicationSnapshot snapshot)
    {
        void Apply()
        {
            _snapshot = snapshot;
            OnPropertyChanged(nameof(Status));
            OnPropertyChanged(nameof(Url));
            OnPropertyChanged(nameof(CanStart));
            OnPropertyChanged(nameof(CanStop));
            OnPropertyChanged(nameof(CanOpen));
        }

        if (_synchronizationContext is null || SynchronizationContext.Current == _synchronizationContext)
            Apply();
        else
            _synchronizationContext.Post(_ => Apply(), null);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

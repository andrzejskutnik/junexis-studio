namespace Junexis.Studio.Abstractions;

/// <summary>Stan lokalnej sesji aplikacji Junexis.</summary>
public enum ReportApplicationState
{
    /// <summary>Żadna sesja nie jest uruchomiona.</summary>
    Idle,

    /// <summary>Trwa sprawdzanie, budowanie lub uruchamianie aplikacji.</summary>
    Starting,

    /// <summary>Aplikacja działa; adres panelu jest dostępny, jeśli aplikacja go deklaruje.</summary>
    Ready,

    /// <summary>Trwa zatrzymywanie procesów należących do sesji.</summary>
    Stopping,

    /// <summary>Sesja zakończyła się błędem.</summary>
    Failed,
}

/// <summary>Aktualny obraz lokalnej sesji aplikacji Junexis.</summary>
/// <param name="State">Stan cyklu życia sesji.</param>
/// <param name="Status">Czytelne objaśnienie bieżącego stanu.</param>
/// <param name="Url">Adres gotowego panelu, jeśli został już opublikowany.</param>
public sealed record ReportApplicationSnapshot(
    ReportApplicationState State,
    string Status,
    Uri? Url = null);

/// <summary>
/// Zarządza jedną lokalną sesją aplikacji Junexis i udostępnia jej panel
/// <c>@report</c> narzędziom Studio.
/// </summary>
public interface IReportApplicationSession
{
    /// <summary>Aktualny, niemutowalny obraz sesji.</summary>
    ReportApplicationSnapshot Current { get; }

    /// <summary>Występuje po każdej zmianie stanu, opisu lub adresu panelu.</summary>
    event Action<ReportApplicationSnapshot>? Changed;

    /// <summary>Buduje projekt i uruchamia jego lokalną aplikację.</summary>
    /// <param name="workspaceRoot">Katalog otwartego obszaru roboczego.</param>
    /// <param name="activeDocumentPath">Opcjonalna ścieżka aktywnego pliku <c>.jnx</c>.</param>
    /// <param name="durationSeconds">Maksymalny czas sesji w sekundach.</param>
    /// <param name="cancellationToken">Token anulowania oczekiwania na sesję.</param>
    /// <returns>Zadanie kończące się po zatrzymaniu całej sesji.</returns>
    Task StartAsync(
        string workspaceRoot,
        string? activeDocumentPath,
        int durationSeconds = 300,
        CancellationToken cancellationToken = default);

    /// <summary>Ponownie otwiera gotowy panel w domyślnej przeglądarce.</summary>
    /// <returns><see langword="true"/>, gdy adres był dostępny i został przekazany systemowi.</returns>
    bool Open();

    /// <summary>Żąda zatrzymania bieżącej sesji i wszystkich jej procesów.</summary>
    void Stop();
}

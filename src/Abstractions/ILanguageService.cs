namespace Junexis.Studio.Abstractions;

/// <summary>
/// Stan procesu serwera językowego zarządzanego przez Studio.
/// </summary>
public enum LanguageServerState
{
    /// <summary>Serwer nie jest uruchomiony.</summary>
    Stopped = 0,

    /// <summary>Serwer jest uruchamiany i inicjalizowany.</summary>
    Starting = 1,

    /// <summary>Serwer jest gotowy do obsługi żądań.</summary>
    Running = 2,

    /// <summary>Serwer zakończył pracę z błędem.</summary>
    Failed = 3,
}

/// <summary>
/// Bieżący stan jednego profilu serwera językowego.
/// </summary>
/// <param name="ProfileId">Stabilny identyfikator profilu.</param>
/// <param name="DisplayName">Nazwa widoczna dla użytkownika.</param>
/// <param name="State">Stan lifecycle procesu.</param>
/// <param name="Message">Krótki opis stanu lub błędu.</param>
/// <param name="RestartCount">Liczba automatycznych restartów w bieżącej sesji Studio.</param>
public sealed record LanguageServerStatus(
    string ProfileId,
    string DisplayName,
    LanguageServerState State,
    string Message,
    int RestartCount = 0);

/// <summary>
/// Pozycja w dokumencie zgodna z LSP: linia i znak są liczone od zera.
/// </summary>
/// <param name="Line">Linia liczona od zera.</param>
/// <param name="Character">Znak w linii liczony od zera.</param>
public readonly record struct LanguagePosition(int Line, int Character);

/// <summary>
/// Lokacja źródłowa zwrócona przez serwer językowy.
/// </summary>
/// <param name="Path">Pełna ścieżka pliku.</param>
/// <param name="Position">Pozycja liczona od zera.</param>
public sealed record LanguageLocation(string Path, LanguagePosition Position);

/// <summary>
/// Informacja typu hover zwrócona przez serwer językowy.
/// </summary>
/// <param name="Content">Treść w formacie tekstowym lub Markdown.</param>
public sealed record LanguageHover(string Content);

/// <summary>
/// Jedna propozycja uzupełnienia kodu.
/// </summary>
/// <param name="Label">Etykieta widoczna na liście.</param>
/// <param name="InsertText">Tekst wstawiany do dokumentu.</param>
/// <param name="Detail">Opcjonalny opis symbolu.</param>
/// <param name="Kind">Opcjonalna nazwa rodzaju elementu.</param>
public sealed record LanguageCompletionItem(
    string Label,
    string InsertText,
    string? Detail = null,
    string? Kind = null);

/// <summary>
/// Diagnostyki opublikowane dla jednego dokumentu przez serwer językowy.
/// </summary>
/// <param name="ProfileId">Identyfikator profilu serwera.</param>
/// <param name="Path">Pełna ścieżka dokumentu.</param>
/// <param name="Diagnostics">Aktualny, kompletny zestaw diagnostyk dokumentu.</param>
public sealed record LanguageDiagnosticsPublished(
    string ProfileId,
    string Path,
    IReadOnlyList<Diagnostic> Diagnostics);

/// <summary>
/// Kompletny wynik wyszukiwania referencji gotowy do prezentacji w panelu Studio.
/// </summary>
/// <param name="ProfileId">Identyfikator profilu serwera.</param>
/// <param name="QueryPath">Dokument, z którego wykonano zapytanie.</param>
/// <param name="Symbol">Symbol odczytany w pozycji zapytania, jeśli był dostępny.</param>
/// <param name="Locations">Znalezione lokacje źródłowe.</param>
public sealed record LanguageReferencesPublished(
    string ProfileId,
    string QueryPath,
    string? Symbol,
    IReadOnlyList<LanguageLocation> Locations);

/// <summary>Pojedynczy token semantyczny zwrócony przez LSP.</summary>
/// <param name="Line">Linia liczona od zera.</param>
/// <param name="Character">Znak liczony od zera.</param>
/// <param name="Length">Długość tokenu.</param>
/// <param name="Type">Nazwa typu z legendy serwera.</param>
public sealed record LanguageSemanticToken(int Line, int Character, int Length, string Type);

/// <summary>Pojedyncza tekstowa zmiana workspace.</summary>
/// <param name="Path">Pełna ścieżka pliku.</param>
/// <param name="Start">Początek zakresu LSP.</param>
/// <param name="End">Koniec zakresu LSP.</param>
/// <param name="NewText">Nowy tekst.</param>
public sealed record LanguageTextEdit(
    string Path,
    LanguagePosition Start,
    LanguagePosition End,
    string NewText);

/// <summary>Akcja naprawcza zwrócona przez serwer językowy.</summary>
/// <param name="Title">Tytuł widoczny dla użytkownika.</param>
/// <param name="Kind">Opcjonalny rodzaj LSP.</param>
/// <param name="IsPreferred">Czy serwer wskazał akcję preferowaną.</param>
/// <param name="Edits">Zmiany tekstowe akcji.</param>
public sealed record LanguageCodeAction(
    string Title,
    string? Kind,
    bool IsPreferred,
    IReadOnlyList<LanguageTextEdit> Edits);

/// <summary>Żądanie zastąpienia treści otwartego dokumentu przez operację workspace.</summary>
/// <param name="Path">Pełna ścieżka dokumentu.</param>
/// <param name="Content">Nowa kompletna treść.</param>
public sealed record ReplaceOpenDocumentContentRequested(string Path, string Content);

/// <summary>
/// Wspólna usługa funkcji językowych dla Junexis, C++, Go i TypeScript.
/// Zarządza procesami LSP oraz synchronizacją otwartych dokumentów.
/// </summary>
public interface ILanguageService : IAsyncDisposable
{
    /// <summary>Emitowane po zmianie stanu dowolnego serwera.</summary>
    event Action<LanguageServerStatus>? StatusChanged;

    /// <summary>Emitowane po opublikowaniu diagnostyk dokumentu.</summary>
    event Action<LanguageDiagnosticsPublished>? DiagnosticsPublished;

    /// <summary>Emitowane po zakończeniu wyszukiwania referencji.</summary>
    event Action<LanguageReferencesPublished>? ReferencesPublished;

    /// <summary>Zwraca snapshot stanów wszystkich znanych profili.</summary>
    IReadOnlyList<LanguageServerStatus> GetStatuses();

    /// <summary>Sprawdza, czy ścieżka ma skonfigurowany profil językowy.</summary>
    /// <param name="path">Ścieżka dokumentu.</param>
    bool Supports(string path);

    /// <summary>Pobiera opis symbolu pod wskazaną pozycją.</summary>
    Task<LanguageHover?> HoverAsync(
        string path,
        LanguagePosition position,
        CancellationToken cancellationToken = default);

    /// <summary>Pobiera propozycje uzupełnienia kodu.</summary>
    Task<IReadOnlyList<LanguageCompletionItem>> CompletionAsync(
        string path,
        LanguagePosition position,
        CancellationToken cancellationToken = default);

    /// <summary>Pobiera definicję symbolu.</summary>
    Task<LanguageLocation?> DefinitionAsync(
        string path,
        LanguagePosition position,
        CancellationToken cancellationToken = default);

    /// <summary>Pobiera referencje symbolu.</summary>
    Task<IReadOnlyList<LanguageLocation>> ReferencesAsync(
        string path,
        LanguagePosition position,
        CancellationToken cancellationToken = default);

    /// <summary>Pobiera pełny zestaw tokenów semantycznych dokumentu.</summary>
    Task<IReadOnlyList<LanguageSemanticToken>> SemanticTokensAsync(
        string path,
        CancellationToken cancellationToken = default);

    /// <summary>Zmienia nazwę symbolu i stosuje zwrócone zmiany workspace.</summary>
    Task<int> RenameAsync(
        string path,
        LanguagePosition position,
        string newName,
        CancellationToken cancellationToken = default);

    /// <summary>Pobiera akcje naprawcze dla wskazanej pozycji.</summary>
    Task<IReadOnlyList<LanguageCodeAction>> CodeActionsAsync(
        string path,
        LanguagePosition position,
        CancellationToken cancellationToken = default);

    /// <summary>Stosuje zmiany wybranej akcji i zwraca ich liczbę.</summary>
    Task<int> ApplyCodeActionAsync(
        LanguageCodeAction action,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Restartuje profil przypisany do dokumentu i ponownie otwiera jego dokumenty.
    /// </summary>
    Task RestartAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Restartuje wskazany profil i ponownie otwiera przypisane do niego dokumenty.
    /// </summary>
    Task RestartProfileAsync(string profileId, CancellationToken cancellationToken = default);
}

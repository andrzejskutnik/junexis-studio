namespace Junexis.Studio.Abstractions;

/// <summary>
/// Serwis zarządzania zakładkami dokumentów (tabami).
/// Obsługuje lifecycle: open / activate / close + routing focusa do <see cref="IActiveDocumentService"/>.
/// </summary>
public interface IDocumentTabService
{
    /// <summary>
    /// Aktualnie otwarte zakładki (snapshot).
    /// </summary>
    IReadOnlyList<DocumentTab> OpenTabs { get; }

    /// <summary>
    /// Aktywna zakładka (lub <see langword="null"/> gdy brak tabów).
    /// </summary>
    DocumentTab? ActiveTab { get; }

    /// <summary>
    /// Zdarzenie emitowane przy zmianach kolekcji tabów (otwarcie/zamknięcie).
    /// </summary>
    event Action? TabsChanged;

    /// <summary>
    /// Zdarzenie emitowane przy zmianie aktywnej zakładki.
    /// </summary>
    event Action? ActiveTabChanged;

    /// <summary>
    /// Otwiera dokument jako nowy tab lub aktywuje istniejący (nie duplikuje).
    /// </summary>
    /// <param name="path">Ścieżka do pliku.</param>
    void Open(string path);

    /// <summary>
    /// Aktywuje istniejący tab. Jeśli <paramref name="path"/> nie jest otwarty — ignoruje.
    /// </summary>
    /// <param name="path">Ścieżka do pliku.</param>
    void Activate(string path);

    /// <summary>
    /// Zamyka tab. Jeśli zamykany tab był aktywny — aktywuje sąsiedni.
    /// </summary>
    /// <param name="path">Ścieżka do pliku.</param>
    void Close(string path);
}

/// <summary>
/// Reprezentacja zakładki dokumentu.
/// </summary>
/// <param name="Path">Pełna ścieżka do pliku.</param>
/// <param name="Title">Wyświetlany tytuł zakładki (nazwa pliku).</param>
public sealed record DocumentTab(string Path, string Title);

/// <summary>
/// Zdarzenie domenowe publikowane gdy dokument jest zamykany (usuwany z tabów).
/// </summary>
/// <param name="Path">Ścieżka zamykanego dokumentu.</param>
public sealed record DocumentClosed(string Path);

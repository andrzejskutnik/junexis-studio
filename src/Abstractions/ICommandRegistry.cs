namespace Junexis.Studio.Abstractions;

/// <summary>
/// Rejestr komend (v0.1 bezkonkekstowe + v0.2 kontekstowe).
/// </summary>
public interface ICommandRegistry
{
    /// <summary>
    /// Rejestruje komendę pod danym identyfikatorem.
    /// </summary>
    /// <param name="id">Id komendy (np. "workspace.openFolder").</param>
    /// <param name="execute">Akcja wykonywana przy wywołaniu komendy.</param>
    void Register(string id, Action execute);

    /// <summary>
    /// Rejestruje komendę pod danym identyfikatorem i opcjonalnie opisuje,
    /// gdzie ma się pojawić w menu UI.
    /// </summary>
    /// <param name="id">Id komendy (np. "workspace.openFolder").</param>
    /// <param name="execute">Akcja wykonywana przy wywołaniu komendy.</param>
    /// <param name="menuPath">Ścieżka menu (np. "File/Open Folder").</param>
    void Register(string id, Action execute, string menuPath);

    /// <summary>
    /// Rejestruje komendę z pełnymi metadanymi UI.
    /// </summary>
    /// <param name="id">Id komendy.</param>
    /// <param name="execute">Akcja wykonywana przy wywołaniu komendy.</param>
    /// <param name="menuPath">Ścieżka menu (może być <see langword="null"/>).</param>
    /// <param name="displayName">Wyświetlana nazwa komendy.</param>
    /// <param name="category">Kategoria (np. "File", "Build").</param>
    void Register(string id, Action execute, string? menuPath, string? displayName, string? category);

    /// <summary>
    /// Rejestruje komendę z pełnymi metadanymi UI, w tym celem menu kontekstowego.
    /// </summary>
    /// <param name="id">Id komendy.</param>
    /// <param name="execute">Akcja wykonywana przy wywołaniu komendy.</param>
    /// <param name="menuPath">Ścieżka menu głównego (może być <see langword="null"/>).</param>
    /// <param name="displayName">Wyświetlana nazwa komendy.</param>
    /// <param name="category">Kategoria (np. "File", "Build").</param>
    /// <param name="contextTarget">
    /// Identyfikator panelu kontekstowego (np. <c>"explorer"</c>, <c>"editor"</c>).
    /// <see langword="null"/> oznacza brak menu kontekstowego.
    /// </param>
    void Register(string id, Action execute, string? menuPath, string? displayName, string? category, string? contextTarget);

    /// <summary>
    /// Rejestruje komendę z predykatem dostępności.
    /// </summary>
    /// <param name="id">Id komendy.</param>
    /// <param name="execute">Akcja wykonywana przy wywołaniu komendy.</param>
    /// <param name="canExecute">Predykat dostępności; <see langword="null"/> oznacza zawsze dostępna.</param>
    /// <param name="menuPath">Ścieżka menu (może być <see langword="null"/>).</param>
    /// <param name="displayName">Wyświetlana nazwa komendy.</param>
    /// <param name="category">Kategoria (np. "File", "Build").</param>
    void Register(string id, Action execute, Func<bool>? canExecute, string? menuPath = null, string? displayName = null, string? category = null);

    /// <summary>
    /// Rejestruje komendę kontekstową z predykatem <paramref name="canExecute"/>
    /// i akcją przyjmującą <see cref="CommandContext"/>.
    /// </summary>
    /// <param name="id">Id komendy.</param>
    /// <param name="execute">Akcja wykonywana z kontekstem.</param>
    /// <param name="canExecute">Predykat dostępności (opcjonalny; domyślnie <see langword="true"/>).</param>
    /// <param name="menuPath">Ścieżka menu (może być <see langword="null"/>).</param>
    /// <param name="displayName">Wyświetlana nazwa komendy.</param>
    /// <param name="category">Kategoria (np. "File", "Build").</param>
    void Register(string id, Action<CommandContext> execute, Func<CommandContext, bool>? canExecute = null,
        string? menuPath = null, string? displayName = null, string? category = null);

    /// <summary>
    /// Sprawdza, czy komenda jest dostępna do wykonania (bezkonkekstowo).
    /// </summary>
    /// <param name="id">Id komendy.</param>
    /// <returns><see langword="true"/> jeśli komenda jest zarejestrowana.</returns>
    bool CanExecute(string id);

    /// <summary>
    /// Sprawdza, czy komenda jest dostępna do wykonania w danym kontekście.
    /// Dla komend bezkonkekstowych zwraca to samo co <see cref="CanExecute(string)"/>.
    /// </summary>
    /// <param name="id">Id komendy.</param>
    /// <param name="context">Kontekst wywołania.</param>
    /// <returns><see langword="true"/> jeśli komenda jest dostępna.</returns>
    bool CanExecute(string id, CommandContext context);

    /// <summary>
    /// Wykonuje komendę (bezkonkekstowo).
    /// </summary>
    /// <param name="id">Id komendy.</param>
    void Execute(string id);

    /// <summary>
    /// Wykonuje komendę w danym kontekście.
    /// Dla komend bezkonkekstowych kontekst jest ignorowany.
    /// </summary>
    /// <param name="id">Id komendy.</param>
    /// <param name="context">Kontekst wywołania.</param>
    void Execute(string id, CommandContext context);

    /// <summary>
    /// Zdarzenie emitowane tuż przed wykonaniem komendy.
    /// </summary>
    event Action<string>? CommandExecuting;

    /// <summary>
    /// Zdarzenie emitowane po zakończeniu wykonania komendy (sukces lub błąd).
    /// </summary>
    event Action<CommandExecutedInfo>? CommandExecuted;

    /// <summary>
    /// Zwraca snapshot zarejestrowanych komend.
    /// </summary>
    /// <returns>Snapshot rejestracji komend.</returns>
    IReadOnlyList<CommandRegistration> GetRegisteredCommands();
}

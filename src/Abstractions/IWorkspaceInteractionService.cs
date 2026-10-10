namespace Junexis.Studio.Abstractions;

/// <summary>
/// Abstrakcja interakcji UI wymaganych przez workflow operacji na plikach workspace.
/// </summary>
public interface IWorkspaceInteractionService
{
    /// <summary>
    /// Wyświetla prompt tekstowy i zwraca wprowadzoną wartość albo <see langword="null"/>,
    /// gdy użytkownik anuluje operację.
    /// </summary>
    /// <param name="title">Tytuł okna dialogowego.</param>
    /// <param name="message">Komunikat opisujący oczekiwaną wartość.</param>
    /// <param name="initialValue">Wartość początkowa w polu edycji.</param>
    /// <returns>Wprowadzona wartość albo <see langword="null"/>.</returns>
    Task<string?> PromptForTextAsync(string title, string message, string initialValue);

    /// <summary>
    /// Otwiera picker katalogu i zwraca wybraną ścieżkę albo <see langword="null"/>,
    /// gdy użytkownik anuluje operację.
    /// </summary>
    /// <param name="title">Tytuł pickera.</param>
    /// <returns>Wybrana ścieżka katalogu albo <see langword="null"/>.</returns>
    Task<string?> PickFolderAsync(string title);

    /// <summary>
    /// Wyświetla dialog potwierdzenia i zwraca <see langword="true"/> gdy użytkownik kliknie OK.
    /// </summary>
    /// <param name="title">Tytuł okna.</param>
    /// <param name="message">Treść pytania.</param>
    /// <returns><see langword="true"/> gdy użytkownik potwierdzi; <see langword="false"/> gdy anuluje.</returns>
    Task<bool> ConfirmAsync(string title, string message);
}
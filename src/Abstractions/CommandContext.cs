namespace Junexis.Studio.Abstractions;

/// <summary>
/// Kontekst wykonania komendy — opisuje stan UI/workspace w momencie wywołania.
/// Shell buduje instancję na podstawie aktywnego dokumentu, zaznaczenia, workspace i panelu.
/// </summary>
/// <param name="ActiveDocumentPath">Ścieżka aktywnego dokumentu (lub <see langword="null"/>).</param>
/// <param name="SelectedFilePath">Ścieżka zaznaczonego pliku w drzewie (lub <see langword="null"/>).</param>
/// <param name="WorkspacePath">Ścieżka bieżącego workspace (lub <see langword="null"/>).</param>
/// <param name="ActivePanelId">Id aktywnego panelu (lub <see langword="null"/>).</param>
public sealed record CommandContext(
    string? ActiveDocumentPath = null,
    string? SelectedFilePath = null,
    string? WorkspacePath = null,
    string? ActivePanelId = null)
{
    /// <summary>
    /// Pusty kontekst — brak informacji o stanie UI.
    /// </summary>
    public static CommandContext Empty { get; } = new();
}

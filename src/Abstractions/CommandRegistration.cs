namespace Junexis.Studio.Abstractions;

/// <summary>
/// Rejestracja komendy dla UI (np. budowy menu) i diagnostyki.
/// </summary>
/// <param name="Id">Id komendy.</param>
/// <param name="MenuPath">Ścieżka menu (np. "File/Open Folder").</param>
/// <param name="DisplayName">Wyświetlana nazwa komendy (np. "Open Folder"). Może być <see langword="null"/>.</param>
/// <param name="Category">Kategoria komendy (np. "File", "Build", "View"). Może być <see langword="null"/>.</param>
/// <param name="ContextTarget">
/// Identyfikator panelu, w którego menu kontekstowym ma się pojawić komenda
/// (np. <c>"explorer"</c>, <c>"editor"</c>). <see langword="null"/> oznacza brak menu kontekstowego.
/// </param>
public sealed record CommandRegistration(
    string Id,
    string? MenuPath,
    string? DisplayName = null,
    string? Category = null,
    string? ContextTarget = null);

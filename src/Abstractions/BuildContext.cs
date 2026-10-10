namespace Junexis.Studio.Abstractions;

/// <summary>
/// Kontekst przekazywany kompilatorowi przy Build/Clean.
/// Nie zawiera elementów UI.
/// </summary>
/// <param name="WorkspaceRoot">Ścieżka katalogu workspace.</param>
/// <param name="ActiveDocumentPath">Ścieżka aktywnego dokumentu (opcjonalna).</param>
/// <param name="BuildProfile">Profil budowania (np. "Debug", "Release").</param>
/// <param name="EnvironmentVariables">Dodatkowe zmienne środowiskowe dla procesu kompilacji.</param>
public sealed record BuildContext(
    string WorkspaceRoot,
    string? ActiveDocumentPath = null,
    string BuildProfile = "Debug",
    IReadOnlyDictionary<string, string>? EnvironmentVariables = null)
{
    /// <summary>
    /// Tworzy pusty kontekst z domyślnymi wartościami (do testów).
    /// </summary>
    public static BuildContext Empty { get; } = new(string.Empty);
}

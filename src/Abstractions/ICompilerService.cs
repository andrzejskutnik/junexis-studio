namespace Junexis.Studio.Abstractions;

/// <summary>
/// Kontrakt capability dla kompilatora.
/// Rozszerzenia kompilatorów rejestrują swoją instancję jako capability w <see cref="ICapabilityRegistry"/>.
/// Wiele ICompilerService może współistnieć — selekcja per workspace/plik.
/// </summary>
public interface ICompilerService
{
    /// <summary>
    /// Unikalny identyfikator kompilatora (np. "gcc", "junexis-lang").
    /// </summary>
    string CompilerId { get; }

    /// <summary>
    /// Sprawdza, czy kompilator obsługuje pliki o danym rozszerzeniu.
    /// </summary>
    /// <param name="fileExtension">Rozszerzenie pliku (z kropką, np. ".cpp").</param>
    /// <returns><see langword="true"/> jeśli kompilator obsługuje dany typ pliku.</returns>
    bool SupportsFile(string fileExtension);

    /// <summary>
    /// Uruchamia kompilację.
    /// </summary>
    /// <param name="context">Kontekst kompilacji (workspace, profil, env).</param>
    /// <returns>Wynik kompilacji.</returns>
    Task<CompilerResult> BuildAsync(BuildContext context);

    /// <summary>
    /// Uruchamia kompilację z możliwością anulowania. Domyślna implementacja
    /// zachowuje kompatybilność ze starszymi providerami.
    /// </summary>
    /// <param name="context">Kontekst kompilacji.</param>
    /// <param name="cancellationToken">Token anulowania operacji i procesu narzędziowego.</param>
    /// <returns>Wynik kompilacji.</returns>
    Task<CompilerResult> BuildAsync(BuildContext context, CancellationToken cancellationToken) =>
        BuildAsync(context);

    /// <summary>
    /// Czyści artefakty kompilacji.
    /// </summary>
    /// <param name="context">Kontekst kompilacji.</param>
    /// <returns>Wynik operacji clean.</returns>
    Task<CompilerResult> CleanAsync(BuildContext context);

    /// <summary>
    /// Czyści artefakty z możliwością anulowania. Domyślna implementacja
    /// zachowuje kompatybilność ze starszymi providerami.
    /// </summary>
    /// <param name="context">Kontekst kompilacji.</param>
    /// <param name="cancellationToken">Token anulowania operacji.</param>
    /// <returns>Wynik operacji clean.</returns>
    Task<CompilerResult> CleanAsync(BuildContext context, CancellationToken cancellationToken) =>
        CleanAsync(context);

    /// <summary>
    /// Zwraca bieżące diagnostyki z ostatniego builda (cache).
    /// </summary>
    /// <returns>Lista diagnostyk kompilatora.</returns>
    IReadOnlyList<Diagnostic> GetDiagnostics();
}

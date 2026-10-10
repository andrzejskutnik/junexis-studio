namespace Junexis.Studio.Abstractions;

/// <summary>
/// Minimalny kontrakt rozszerzenia (extension) ładowanego przez host.
/// W v0.1 inicjalizacja jest synchroniczna i ma być odporna na błędy.
/// </summary>
public interface IJunexisExtension
{
    /// <summary>
    /// Stabilny identyfikator rozszerzenia (np. "junexis.explorer").
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Wywoływane podczas uruchamiania hosta.
    /// Rozszerzenie powinno zarejestrować panele/komendy/subskrypcje.
    /// </summary>
    /// <param name="context">Pakiet usług hosta dostępnych dla rozszerzenia.</param>
    void Initialize(IHostContext context);

    /// <summary>
    /// Wywoływane podczas zamykania hosta.
    /// Rozszerzenie powinno posprzątać subskrypcje i zwolnić zasoby.
    /// </summary>
    void Shutdown();
}

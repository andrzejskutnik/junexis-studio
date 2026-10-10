namespace Junexis.Studio.Abstractions;

/// <summary>
/// Circuit breaker per extension – śledzi błędy i automatycznie wyłącza rozszerzenie
/// po przekroczeniu progu w oknie czasowym.
/// </summary>
public interface ICircuitBreaker
{
    /// <summary>
    /// Rejestruje błąd z danego rozszerzenia.
    /// Jeśli próg zostanie przekroczony, rozszerzenie jest automatycznie wyłączane.
    /// </summary>
    /// <param name="extensionId">Identyfikator rozszerzenia.</param>
    /// <param name="exception">Wyjątek, który wystąpił.</param>
    void RecordFailure(string extensionId, Exception exception);

    /// <summary>
    /// Sprawdza, czy rozszerzenie zostało wyłączone przez circuit breaker.
    /// </summary>
    /// <param name="extensionId">Identyfikator rozszerzenia.</param>
    /// <returns><see langword="true"/> jeśli rozszerzenie jest wyłączone.</returns>
    bool IsDisabled(string extensionId);

    /// <summary>
    /// Ręczne włączenie wyłączonego rozszerzenia (reset circuit breakera).
    /// </summary>
    /// <param name="extensionId">Identyfikator rozszerzenia.</param>
    void Reset(string extensionId);

    /// <summary>
    /// Zwraca identyfikatory wszystkich aktualnie wyłączonych rozszerzeń.
    /// </summary>
    /// <returns>Snapshot wyłączonych ID.</returns>
    IReadOnlyList<string> GetDisabledExtensions();
}

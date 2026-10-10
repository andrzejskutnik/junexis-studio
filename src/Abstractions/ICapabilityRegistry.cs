namespace Junexis.Studio.Abstractions;

/// <summary>
/// Lekki capability lookup — extensions rejestrują / odpytują usługi bez twardych zależności.
/// Umożliwia graceful degradation, gdy dana capability nie jest dostępna.
/// </summary>
public interface ICapabilityRegistry
{
    /// <summary>
    /// Rejestruje instancję capability danego typu.
    /// Jeśli typ był już zarejestrowany, nadpisuje poprzednią instancję.
    /// </summary>
    /// <typeparam name="T">Typ (interfejs) capability.</typeparam>
    /// <param name="instance">Instancja implementacji.</param>
    /// <exception cref="ArgumentNullException">Gdy <paramref name="instance"/> jest <see langword="null"/>.</exception>
    void Register<T>(T instance) where T : class;

    /// <summary>
    /// Próbuje pobrać capability danego typu.
    /// </summary>
    /// <typeparam name="T">Typ (interfejs) capability.</typeparam>
    /// <param name="capability">Zwrócona instancja lub <see langword="default"/>.</param>
    /// <returns><see langword="true"/> jeśli capability jest zarejestrowana.</returns>
    bool TryGet<T>(out T? capability) where T : class;

    /// <summary>
    /// Pobiera capability danego typu lub rzuca <see cref="InvalidOperationException"/>.
    /// </summary>
    /// <typeparam name="T">Typ (interfejs) capability.</typeparam>
    /// <returns>Instancja capability.</returns>
    /// <exception cref="InvalidOperationException">Gdy capability nie jest zarejestrowana.</exception>
    T Get<T>() where T : class;

    /// <summary>
    /// Sprawdza, czy capability danego typu jest zarejestrowana.
    /// </summary>
    /// <typeparam name="T">Typ (interfejs) capability.</typeparam>
    /// <returns><see langword="true"/> jeśli zarejestrowana.</returns>
    bool Has<T>() where T : class;
}

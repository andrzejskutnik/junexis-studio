namespace Junexis.Studio.Abstractions;

/// <summary>
/// Rejestr mapowania skrótów klawiaturowych → komendy.
/// Last-wins policy przy konfliktach.
/// </summary>
public interface IKeybindingRegistry
{
    /// <summary>
    /// Przypisuje skrót klawiaturowy do komendy.
    /// Jeśli gest jest już przypisany do innej komendy, nadpisuje (last-wins) i loguje.
    /// </summary>
    /// <param name="gesture">Skrót klawiaturowy.</param>
    /// <param name="commandId">Identyfikator komendy.</param>
    void Bind(KeyGesture gesture, string commandId);

    /// <summary>
    /// Usuwa binding dla danego gestu.
    /// </summary>
    /// <param name="gesture">Skrót klawiaturowy.</param>
    void Unbind(KeyGesture gesture);

    /// <summary>
    /// Próbuje znaleźć komendę dla danego gestu.
    /// </summary>
    /// <param name="gesture">Skrót klawiaturowy.</param>
    /// <param name="commandId">Identyfikator komendy (lub <see langword="null"/>).</param>
    /// <returns><see langword="true"/> jeśli binding istnieje.</returns>
    bool TryGetCommand(KeyGesture gesture, out string? commandId);

    /// <summary>
    /// Zwraca snapshot wszystkich aktualnych bindingów.
    /// </summary>
    /// <returns>Kopia bindingów (gesture → commandId).</returns>
    IReadOnlyDictionary<KeyGesture, string> GetBindings();
}

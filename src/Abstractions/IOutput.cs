namespace Junexis.Studio.Abstractions;

/// <summary>
/// Minimalny output/logging.
/// </summary>
public interface IOutput
{
    /// <summary>
    /// Rejestruje kanał output (jeśli nie istnieje).
    /// </summary>
    /// <param name="channel">Nazwa kanału.</param>
    /// <returns>Znormalizowana nazwa kanału, która została zarejestrowana.</returns>
    string RegisterChannel(string channel);

    /// <summary>
    /// Zwraca snapshot zarejestrowanych kanałów.
    /// </summary>
    /// <returns>Snapshot nazw kanałów.</returns>
    IReadOnlyList<string> GetChannelsSnapshot();

    /// <summary>
    /// Zapisuje komunikat do kanału.
    /// </summary>
    /// <param name="channel">Nazwa kanału (np. "Host", "Commands").</param>
    /// <param name="message">Treść komunikatu.</param>
    void Write(string channel, string message);

    /// <summary>
    /// Zapisuje komunikat do kanału z określonym poziomem istotności.
    /// </summary>
    /// <param name="channel">Nazwa kanału (np. "Host", "Commands").</param>
    /// <param name="message">Treść komunikatu.</param>
    /// <param name="severity">Poziom istotności wpisu.</param>
    void Write(string channel, string message, OutputSeverity severity);

    /// <summary>
    /// Zapisuje komunikat z pełnymi metadanymi (severity, source, exception).
    /// </summary>
    /// <param name="channel">Nazwa kanału.</param>
    /// <param name="message">Treść komunikatu.</param>
    /// <param name="severity">Poziom istotności wpisu.</param>
    /// <param name="source">Identyfikator źródła (extension id / subsystem).</param>
    /// <param name="exceptionText">Bezpieczna tekstowa reprezentacja wyjątku.</param>
    void Write(string channel, string message, OutputSeverity severity, string? source, string? exceptionText = null);

    /// <summary>
    /// Zwraca snapshot dotychczasowych wpisów.
    /// </summary>
    /// <returns>Snapshot wpisów w kolejności chronologicznej.</returns>
    IReadOnlyList<OutputEntry> GetSnapshot();

    /// <summary>
    /// Zdarzenie emitowane po zapisaniu nowego wpisu.
    /// </summary>
    event Action<OutputEntry>? EntryWritten;
}

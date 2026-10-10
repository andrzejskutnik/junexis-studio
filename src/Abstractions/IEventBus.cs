namespace Junexis.Studio.Abstractions;

/// <summary>
/// Minimalna szyna zdarzeń (pub/sub) dla komunikacji wewnątrz IDE.
/// </summary>
public interface IEventBus
{
    /// <summary>
    /// Publikuje zdarzenie do subskrybentów jego typu.
    /// </summary>
    /// <param name="evt">Obiekt zdarzenia.</param>
    void Publish(object evt);

    /// <summary>
    /// Rejestruje handler dla zdarzeń typu <typeparamref name="T"/>.
    /// </summary>
    /// <param name="handler">Delegat wywoływany przy publikacji zdarzenia.</param>
    void Subscribe<T>(Action<T> handler);

    /// <summary>
    /// Usuwa wcześniej zarejestrowany handler.
    /// </summary>
    /// <param name="handler">Delegat do usunięcia.</param>
    void Unsubscribe<T>(Action<T> handler);
}

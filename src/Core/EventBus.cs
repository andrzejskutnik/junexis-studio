using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Core;

/// <summary>
/// Prosta implementacja <see cref="IEventBus"/> dla v0.1.
/// Zapewnia snapshot handlerów podczas publikacji.
/// </summary>
public sealed class EventBus : IEventBus
{
    private readonly object _gate = new();
    private readonly Dictionary<Type, List<Delegate>> _handlers = new();
    private readonly Action<Exception>? _onHandlerError;

    /// <summary>
    /// Tworzy EventBus bez callbacku błędów (wyjątki handlerów są połykane po cichu).
    /// </summary>
    public EventBus() : this(null) { }

    /// <summary>
    /// Tworzy EventBus z opcjonalnym callbackiem wywoływanym,
    /// gdy handler rzuci wyjątek (pozwala logować do Output bez zależności na UI).
    /// </summary>
    /// <param name="onHandlerError">Callback wywoływany przy wyjątku handlera. Może być <see langword="null"/>.</param>
    public EventBus(Action<Exception>? onHandlerError)
    {
        _onHandlerError = onHandlerError;
    }

    /// <summary>
    /// Publikuje zdarzenie do wszystkich subskrybentów jego typu runtime.
    /// </summary>
    /// <param name="evt">Obiekt zdarzenia.</param>
    /// <exception cref="ArgumentNullException">Gdy <paramref name="evt"/> jest <see langword="null"/>.</exception>
    public void Publish(object evt)
    {
        if (evt is null)
        {
            throw new ArgumentNullException(nameof(evt));
        }

        Delegate[] snapshot;

        lock (_gate)
        {
            if (!_handlers.TryGetValue(evt.GetType(), out var list) || list.Count == 0)
            {
                return;
            }

            snapshot = list.ToArray();
        }

        foreach (var handler in snapshot)
        {
            try
            {
                handler.DynamicInvoke(evt);
            }
            catch (Exception ex) when (_onHandlerError is not null)
            {
                // Unwrap TargetInvocationException from DynamicInvoke.
                var inner = ex is System.Reflection.TargetInvocationException tie && tie.InnerException is not null
                    ? tie.InnerException
                    : ex;
                _onHandlerError.Invoke(inner);
            }
        }
    }

    /// <summary>
    /// Rejestruje handler dla zdarzeń typu <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">Typ zdarzenia.</typeparam>
    /// <param name="handler">Delegat wywoływany przy publikacji.</param>
    /// <exception cref="ArgumentNullException">Gdy <paramref name="handler"/> jest <see langword="null"/>.</exception>
    public void Subscribe<T>(Action<T> handler)
    {
        if (handler is null)
        {
            throw new ArgumentNullException(nameof(handler));
        }

        lock (_gate)
        {
            if (!_handlers.TryGetValue(typeof(T), out var list))
            {
                list = new List<Delegate>();
                _handlers.Add(typeof(T), list);
            }

            list.Add(handler);
        }
    }

    /// <summary>
    /// Usuwa wcześniej zarejestrowany handler.
    /// </summary>
    /// <typeparam name="T">Typ zdarzenia.</typeparam>
    /// <param name="handler">Delegat do usunięcia.</param>
    /// <exception cref="ArgumentNullException">Gdy <paramref name="handler"/> jest <see langword="null"/>.</exception>
    public void Unsubscribe<T>(Action<T> handler)
    {
        if (handler is null)
        {
            throw new ArgumentNullException(nameof(handler));
        }

        lock (_gate)
        {
            if (!_handlers.TryGetValue(typeof(T), out var list))
            {
                return;
            }

            list.RemoveAll(d => d.Equals(handler));

            if (list.Count == 0)
            {
                _handlers.Remove(typeof(T));
            }
        }
    }
}

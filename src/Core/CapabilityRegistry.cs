using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Core;

/// <summary>
/// Thread-safe capability registry (lekki IoC per-service lookup).
/// </summary>
public sealed class CapabilityRegistry : ICapabilityRegistry
{
    private readonly object _lock = new();
    private readonly Dictionary<Type, object> _capabilities = new();

    /// <inheritdoc />
    public void Register<T>(T instance) where T : class
    {
        ArgumentNullException.ThrowIfNull(instance);

        lock (_lock)
        {
            _capabilities[typeof(T)] = instance;
        }
    }

    /// <inheritdoc />
    public bool TryGet<T>(out T? capability) where T : class
    {
        lock (_lock)
        {
            if (_capabilities.TryGetValue(typeof(T), out var raw))
            {
                capability = (T)raw;
                return true;
            }

            capability = default;
            return false;
        }
    }

    /// <inheritdoc />
    public T Get<T>() where T : class
    {
        if (TryGet<T>(out var cap) && cap is not null)
        {
            return cap;
        }

        throw new InvalidOperationException($"Capability '{typeof(T).Name}' is not registered.");
    }

    /// <inheritdoc />
    public bool Has<T>() where T : class
    {
        lock (_lock)
        {
            return _capabilities.ContainsKey(typeof(T));
        }
    }
}

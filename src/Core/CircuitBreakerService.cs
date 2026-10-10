using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Core;

/// <summary>
/// Circuit breaker per extension: liczy błędy w oknie czasowym, po progu wyłącza rozszerzenie
/// i raportuje <see cref="SystemDiagnostic"/> + pisze do Output.
/// </summary>
public sealed class CircuitBreakerService : ICircuitBreaker
{
    /// <summary>Kanał Output, na który raportowany jest auto-disable.</summary>
    public const string OutputChannel = "Extensions";

    private readonly int _threshold;
    private readonly TimeSpan _window;
    private readonly IOutput? _output;
    private readonly IEventBus? _eventBus;

    private readonly object _lock = new();
    private readonly Dictionary<string, ExtensionState> _states = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Tworzy circuit breaker.
    /// </summary>
    /// <param name="threshold">Liczba błędów do auto-disable (domyślnie 5).</param>
    /// <param name="window">Okno czasowe (domyślnie 60 s).</param>
    /// <param name="output">Opcjonalny output do raportowania.</param>
    /// <param name="eventBus">Opcjonalny event bus do publikacji <see cref="SystemDiagnostic"/>.</param>
    public CircuitBreakerService(
        int threshold = 5,
        TimeSpan? window = null,
        IOutput? output = null,
        IEventBus? eventBus = null)
    {
        if (threshold < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(threshold), "Threshold must be at least 1.");
        }

        _threshold = threshold;
        _window = window ?? TimeSpan.FromSeconds(60);
        _output = output;
        _eventBus = eventBus;
    }

    /// <inheritdoc />
    public void RecordFailure(string extensionId, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(extensionId);
        ArgumentNullException.ThrowIfNull(exception);

        lock (_lock)
        {
            if (!_states.TryGetValue(extensionId, out var state))
            {
                state = new ExtensionState();
                _states[extensionId] = state;
            }

            if (state.Disabled)
            {
                return; // already disabled
            }

            var now = DateTime.UtcNow;

            // Purge old failures outside the window.
            state.Failures.RemoveAll(t => (now - t) > _window);

            state.Failures.Add(now);

            if (state.Failures.Count >= _threshold)
            {
                state.Disabled = true;
                OnDisabled(extensionId, exception);
            }
        }
    }

    /// <inheritdoc />
    public bool IsDisabled(string extensionId)
    {
        ArgumentNullException.ThrowIfNull(extensionId);

        lock (_lock)
        {
            return _states.TryGetValue(extensionId, out var state) && state.Disabled;
        }
    }

    /// <inheritdoc />
    public void Reset(string extensionId)
    {
        ArgumentNullException.ThrowIfNull(extensionId);

        lock (_lock)
        {
            _states.Remove(extensionId);
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetDisabledExtensions()
    {
        lock (_lock)
        {
            return _states
                .Where(kv => kv.Value.Disabled)
                .Select(kv => kv.Key)
                .ToList();
        }
    }

    /// <summary>
    /// Wywoływane po osiągnięciu progu: raport do Output + SystemDiagnostic na EventBus.
    /// </summary>
    private void OnDisabled(string extensionId, Exception lastException)
    {
        var message = $"Extension '{extensionId}' auto-disabled after {_threshold} failures within {_window.TotalSeconds}s. Last error: {lastException.Message}";

        _output?.Write(OutputChannel, message, OutputSeverity.Error, extensionId, lastException.ToString());

        var diag = new SystemDiagnostic(
            SystemDiagnosticCategory.ExtensionException,
            message,
            DateTime.UtcNow,
            Source: extensionId,
            ExceptionText: lastException.ToString());

        _eventBus?.Publish(diag);
    }

    private sealed class ExtensionState
    {
        public List<DateTime> Failures { get; } = new();
        public bool Disabled { get; set; }
    }
}

using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Core;

/// <summary>
/// Thread-safe rejestr skrótów klawiaturowych.
/// Polityka konfliktów: last-wins + log do <see cref="IOutput"/> (jeśli podano).
/// </summary>
public sealed class KeybindingRegistry : IKeybindingRegistry
{
    private readonly object _lock = new();
    private readonly Dictionary<KeyGesture, string> _bindings = new();
    private readonly IOutput? _output;

    /// <summary>Kanał Output dla logowania konfliktów.</summary>
    public const string OutputChannel = "Keybindings";

    /// <summary>
    /// Tworzy rejestr keybindingów.
    /// </summary>
    /// <param name="output">Opcjonalny output do logowania konfliktów.</param>
    public KeybindingRegistry(IOutput? output = null)
    {
        _output = output;
    }

    /// <inheritdoc />
    public void Bind(KeyGesture gesture, string commandId)
    {
        ArgumentNullException.ThrowIfNull(gesture);
        ArgumentNullException.ThrowIfNull(commandId);

        lock (_lock)
        {
            if (_bindings.TryGetValue(gesture, out var existing) && existing != commandId)
            {
                _output?.Write(OutputChannel,
                    $"Keybinding conflict: {gesture} was bound to '{existing}', now rebound to '{commandId}'.",
                    OutputSeverity.Warning);
            }

            _bindings[gesture] = commandId;
        }
    }

    /// <inheritdoc />
    public void Unbind(KeyGesture gesture)
    {
        ArgumentNullException.ThrowIfNull(gesture);

        lock (_lock)
        {
            _bindings.Remove(gesture);
        }
    }

    /// <inheritdoc />
    public bool TryGetCommand(KeyGesture gesture, out string? commandId)
    {
        ArgumentNullException.ThrowIfNull(gesture);

        lock (_lock)
        {
            if (_bindings.TryGetValue(gesture, out var cmd))
            {
                commandId = cmd;
                return true;
            }

            commandId = null;
            return false;
        }
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<KeyGesture, string> GetBindings()
    {
        lock (_lock)
        {
            return new Dictionary<KeyGesture, string>(_bindings);
        }
    }
}

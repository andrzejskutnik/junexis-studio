using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Core;

/// <summary>
/// In-memory consent store (per sesja/workspace).
/// Przechowuje decyzje allow/deny per extensionId + permission.
/// </summary>
public sealed class ConsentStore : IConsentStore
{
    private readonly object _lock = new();

    // Key: "extensionId::permission" (case-insensitive)
    private readonly Dictionary<string, ConsentDecision> _decisions
        = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public ConsentDecision GetDecision(string extensionId, string permission)
    {
        ArgumentNullException.ThrowIfNull(extensionId);
        ArgumentNullException.ThrowIfNull(permission);

        lock (_lock)
        {
            return _decisions.TryGetValue(MakeKey(extensionId, permission), out var d) ? d : ConsentDecision.Unknown;
        }
    }

    /// <inheritdoc />
    public void SetDecision(string extensionId, string permission, ConsentDecision decision)
    {
        ArgumentNullException.ThrowIfNull(extensionId);
        ArgumentNullException.ThrowIfNull(permission);

        lock (_lock)
        {
            _decisions[MakeKey(extensionId, permission)] = decision;
        }
    }

    /// <inheritdoc />
    public bool IsAllowed(string extensionId, string permission)
        => GetDecision(extensionId, permission) == ConsentDecision.Allow;

    /// <inheritdoc />
    public IReadOnlyDictionary<string, ConsentDecision> GetDecisions(string extensionId)
    {
        ArgumentNullException.ThrowIfNull(extensionId);

        var prefix = extensionId + "::";
        var result = new Dictionary<string, ConsentDecision>(StringComparer.OrdinalIgnoreCase);

        lock (_lock)
        {
            foreach (var (key, decision) in _decisions)
            {
                if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    var permission = key[prefix.Length..];
                    result[permission] = decision;
                }
            }
        }

        return result;
    }

    /// <inheritdoc />
    public void ResetDecisions(string extensionId)
    {
        ArgumentNullException.ThrowIfNull(extensionId);

        var prefix = extensionId + "::";

        lock (_lock)
        {
            var keysToRemove = _decisions.Keys
                .Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var key in keysToRemove)
            {
                _decisions.Remove(key);
            }
        }
    }

    private static string MakeKey(string extensionId, string permission) => $"{extensionId}::{permission}";
}

using Junexis.Studio.Abstractions;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Junexis.Studio.Core;

/// <summary>
/// In-memory implementation of <see cref="ISessionMemory"/> with optional file-based persistence.
/// Stores messages per workspace with configurable retention policy.
/// </summary>
public sealed class SessionMemoryStore : ISessionMemory
{
    private readonly Dictionary<string, List<StoredMessage>> _store = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();
    private readonly IOutput? _output;
    private readonly Func<string, string>? _pathResolver;

    /// <summary>Output channel name for memory messages.</summary>
    public const string OutputChannel = "OMWiA";

    /// <summary>
    /// Gets or sets the retention policy applied during <see cref="ApplyRetention"/>.
    /// </summary>
    public RetentionPolicy Policy { get; set; } = new();

    /// <summary>
    /// Creates an in-memory session memory store.
    /// </summary>
    /// <param name="output">Optional output service for logging.</param>
    /// <param name="pathResolver">Optional function to resolve storage file path from workspace root.
    /// When provided, enables file-based persistence via Load/Save.</param>
    public SessionMemoryStore(IOutput? output = null, Func<string, string>? pathResolver = null)
    {
        _output = output;
        _pathResolver = pathResolver;
    }

    /// <inheritdoc />
    public IReadOnlyList<StoredMessage> GetHistory(string workspaceRoot)
    {
        ArgumentNullException.ThrowIfNull(workspaceRoot);
        lock (_lock)
        {
            return _store.TryGetValue(workspaceRoot, out var list)
                ? list.ToList()
                : [];
        }
    }

    /// <inheritdoc />
    public void Append(string workspaceRoot, StoredMessage message)
    {
        ArgumentNullException.ThrowIfNull(workspaceRoot);
        ArgumentNullException.ThrowIfNull(message);

        lock (_lock)
        {
            if (!_store.TryGetValue(workspaceRoot, out var list))
            {
                list = new List<StoredMessage>();
                _store[workspaceRoot] = list;
            }
            list.Add(message);
        }

        _output?.Write(OutputChannel, $"Message appended ({message.Role}): {Truncate(message.Content, 60)}");
    }

    /// <inheritdoc />
    public void Clear(string workspaceRoot)
    {
        ArgumentNullException.ThrowIfNull(workspaceRoot);
        int removed;
        lock (_lock)
        {
            if (_store.TryGetValue(workspaceRoot, out var list))
            {
                removed = list.Count;
                list.Clear();
            }
            else
            {
                removed = 0;
            }
        }

        _output?.Write(OutputChannel, $"Session memory cleared for workspace. {removed} message(s) removed.");
    }

    /// <inheritdoc />
    public void ApplyRetention(string workspaceRoot)
    {
        ArgumentNullException.ThrowIfNull(workspaceRoot);

        lock (_lock)
        {
            if (!_store.TryGetValue(workspaceRoot, out var list) || list.Count == 0)
                return;

            var removedCount = 0;

            // Age-based retention
            if (Policy.MaxAgeDays > 0)
            {
                var cutoff = DateTime.UtcNow.AddDays(-Policy.MaxAgeDays);
                removedCount += list.RemoveAll(m => m.TimestampUtc < cutoff);
            }

            // Count-based retention
            if (Policy.MaxMessages > 0 && list.Count > Policy.MaxMessages)
            {
                var excess = list.Count - Policy.MaxMessages;
                list.RemoveRange(0, excess);
                removedCount += excess;
            }

            if (removedCount > 0)
            {
                _output?.Write(OutputChannel, $"Retention applied: {removedCount} message(s) removed, {list.Count} remaining.");
            }
        }
    }

    /// <summary>
    /// Serializes the messages for a workspace to a JSON string.
    /// </summary>
    /// <param name="workspaceRoot">Workspace root path.</param>
    /// <returns>JSON representation of stored messages.</returns>
    public string Serialize(string workspaceRoot)
    {
        var history = GetHistory(workspaceRoot);
        return JsonSerializer.Serialize(history, SerializerOptions);
    }

    /// <summary>
    /// Loads messages from a JSON string into the store (replaces existing).
    /// </summary>
    /// <param name="workspaceRoot">Workspace root path.</param>
    /// <param name="json">JSON array of <see cref="StoredMessage"/>.</param>
    /// <returns>Number of messages loaded.</returns>
    public int Deserialize(string workspaceRoot, string json)
    {
        ArgumentNullException.ThrowIfNull(workspaceRoot);
        if (string.IsNullOrWhiteSpace(json))
            return 0;

        var messages = JsonSerializer.Deserialize<List<StoredMessage>>(json, SerializerOptions);
        if (messages is null || messages.Count == 0)
            return 0;

        lock (_lock)
        {
            _store[workspaceRoot] = messages;
        }

        _output?.Write(OutputChannel, $"Loaded {messages.Count} message(s) from storage.");
        return messages.Count;
    }

    private static string Truncate(string text, int maxLen) =>
        text.Length <= maxLen ? text : text[..maxLen] + "...";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}

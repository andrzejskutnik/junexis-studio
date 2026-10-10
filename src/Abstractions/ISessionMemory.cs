namespace Junexis.Studio.Abstractions;

/// <summary>
/// Persistence contract for OMWiA session memory (workspace-scoped).
/// Stores and retrieves assistant conversation history with retention policy.
/// </summary>
public interface ISessionMemory
{
    /// <summary>
    /// Gets all stored messages for the given workspace, newest last.
    /// </summary>
    /// <param name="workspaceRoot">Workspace root path.</param>
    /// <returns>Ordered list of stored messages.</returns>
    IReadOnlyList<StoredMessage> GetHistory(string workspaceRoot);

    /// <summary>
    /// Appends a message to the workspace's session memory.
    /// </summary>
    /// <param name="workspaceRoot">Workspace root path.</param>
    /// <param name="message">The message to store.</param>
    void Append(string workspaceRoot, StoredMessage message);

    /// <summary>
    /// Clears the entire session memory for the given workspace.
    /// </summary>
    /// <param name="workspaceRoot">Workspace root path.</param>
    void Clear(string workspaceRoot);

    /// <summary>
    /// Applies the configured retention policy (max messages / max age).
    /// Removes messages that exceed the limits.
    /// </summary>
    /// <param name="workspaceRoot">Workspace root path.</param>
    void ApplyRetention(string workspaceRoot);
}

/// <summary>
/// A persisted conversation message with metadata.
/// </summary>
/// <param name="Role">The role of the message author (User, Assistant, System).</param>
/// <param name="Content">The message content.</param>
/// <param name="TimestampUtc">UTC timestamp when the message was created.</param>
/// <param name="SessionId">ID of the session this message belongs to.</param>
public sealed record StoredMessage(
    AssistantRole Role,
    string Content,
    DateTime TimestampUtc,
    string? SessionId = null);

/// <summary>
/// Configuration for session memory retention.
/// </summary>
/// <param name="MaxMessages">Maximum number of messages to retain (0 = unlimited).</param>
/// <param name="MaxAgeDays">Maximum age of messages in days (0 = unlimited).</param>
public sealed record RetentionPolicy(int MaxMessages = 200, int MaxAgeDays = 30);

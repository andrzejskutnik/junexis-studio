namespace Junexis.Studio.Abstractions;

/// <summary>
/// Represents a role in an assistant conversation.
/// </summary>
public enum AssistantRole
{
    /// <summary>The user (human) sending a message.</summary>
    User,

    /// <summary>The assistant (AI) responding.</summary>
    Assistant,

    /// <summary>A system-level instruction or context message.</summary>
    System
}

/// <summary>
/// Represents a single message in an assistant conversation session.
/// </summary>
/// <param name="Role">The role of the message sender.</param>
/// <param name="Content">The text content of the message.</param>
/// <param name="TimestampUtc">UTC timestamp when the message was created.</param>
public sealed record AssistantMessage(AssistantRole Role, string Content, DateTime TimestampUtc)
{
    /// <summary>
    /// Creates a user message with the current UTC timestamp.
    /// </summary>
    /// <param name="content">The message content.</param>
    /// <returns>A new <see cref="AssistantMessage"/> with <see cref="AssistantRole.User"/> role.</returns>
    public static AssistantMessage FromUser(string content)
        => new(AssistantRole.User, content, DateTime.UtcNow);

    /// <summary>
    /// Creates an assistant message with the current UTC timestamp.
    /// </summary>
    /// <param name="content">The message content.</param>
    /// <returns>A new <see cref="AssistantMessage"/> with <see cref="AssistantRole.Assistant"/> role.</returns>
    public static AssistantMessage FromAssistant(string content)
        => new(AssistantRole.Assistant, content, DateTime.UtcNow);

    /// <summary>
    /// Creates a system message with the current UTC timestamp.
    /// </summary>
    /// <param name="content">The message content.</param>
    /// <returns>A new <see cref="AssistantMessage"/> with <see cref="AssistantRole.System"/> role.</returns>
    public static AssistantMessage FromSystem(string content)
        => new(AssistantRole.System, content, DateTime.UtcNow);
}

/// <summary>
/// Represents an assistant conversation session.
/// </summary>
/// <param name="Id">Unique session identifier.</param>
/// <param name="WorkspaceRoot">Optional workspace root path that was active when the session started.</param>
/// <param name="CreatedAtUtc">UTC timestamp of session creation.</param>
public sealed record AssistantSession(string Id, string? WorkspaceRoot, DateTime CreatedAtUtc);

/// <summary>
/// Optional token usage statistics for a response.
/// </summary>
/// <param name="PromptTokens">Number of tokens in the prompt.</param>
/// <param name="CompletionTokens">Number of tokens in the completion.</param>
/// <param name="TotalTokens">Total tokens used.</param>
public sealed record TokenUsage(int PromptTokens, int CompletionTokens, int TotalTokens);

/// <summary>
/// Result of sending a message to the assistant.
/// </summary>
/// <param name="Message">The assistant's response message.</param>
/// <param name="Usage">Optional token usage statistics.</param>
public sealed record AssistantResponse(AssistantMessage Message, TokenUsage? Usage = null);

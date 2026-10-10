namespace Junexis.Studio.Abstractions;

/// <summary>
/// Minimal contract for the OMWiA assistant service.
/// Defines session management and messaging without binding to a specific LLM provider.
/// </summary>
public interface IAssistantService
{
    /// <summary>
    /// Starts a new assistant session.
    /// </summary>
    /// <param name="workspaceRoot">Optional workspace root for context.</param>
    /// <returns>The newly created session.</returns>
    AssistantSession StartSession(string? workspaceRoot = null);

    /// <summary>
    /// Gets the currently active session, or <c>null</c> if none.
    /// </summary>
    AssistantSession? ActiveSession { get; }

    /// <summary>
    /// Sends a user message to the active session and returns the assistant's response.
    /// </summary>
    /// <param name="message">The user message content.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>The assistant's response.</returns>
    /// <exception cref="InvalidOperationException">When no active session exists.</exception>
    Task<AssistantResponse> SendMessageAsync(string message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels any in-progress message processing for the active session.
    /// </summary>
    void Cancel();

    /// <summary>
    /// Gets the message history for the active session.
    /// </summary>
    /// <returns>Ordered list of messages in the current session, or empty if no session.</returns>
    IReadOnlyList<AssistantMessage> GetHistory();

    /// <summary>
    /// Ends the active session.
    /// </summary>
    void EndSession();
}

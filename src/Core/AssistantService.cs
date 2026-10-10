using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Core;

/// <summary>
/// Delegate that processes a conversation history and produces an assistant response.
/// Allows plugging in any LLM backend without hard dependency.
/// </summary>
/// <param name="history">The current conversation history.</param>
/// <param name="cancellationToken">Cancellation token.</param>
/// <returns>The assistant response.</returns>
public delegate Task<AssistantResponse> AssistantBackend(
    IReadOnlyList<AssistantMessage> history,
    CancellationToken cancellationToken);

/// <summary>
/// In-memory implementation of <see cref="IAssistantService"/>.
/// Uses a pluggable <see cref="AssistantBackend"/> delegate for response generation.
/// Supports session lifecycle, cancellation, and history tracking.
/// </summary>
public sealed class AssistantService : IAssistantService
{
    private readonly AssistantBackend _backend;
    private readonly IEventBus? _eventBus;
    private readonly IOutput? _output;

    private AssistantSession? _session;
    private readonly List<AssistantMessage> _history = new();
    private CancellationTokenSource? _cts;
    private readonly object _lock = new();

    /// <summary>Output channel name for assistant messages.</summary>
    public const string OutputChannel = "OMWiA";

    /// <summary>
    /// Creates a new <see cref="AssistantService"/>.
    /// </summary>
    /// <param name="backend">The backend delegate for generating responses.</param>
    /// <param name="eventBus">Optional event bus for publishing assistant events.</param>
    /// <param name="output">Optional output service for logging.</param>
    /// <exception cref="ArgumentNullException">When <paramref name="backend"/> is null.</exception>
    public AssistantService(AssistantBackend backend, IEventBus? eventBus = null, IOutput? output = null)
    {
        ArgumentNullException.ThrowIfNull(backend);
        _backend = backend;
        _eventBus = eventBus;
        _output = output;
    }

    /// <inheritdoc />
    public AssistantSession? ActiveSession
    {
        get { lock (_lock) return _session; }
    }

    /// <inheritdoc />
    public AssistantSession StartSession(string? workspaceRoot = null)
    {
        lock (_lock)
        {
            _session = new AssistantSession(Guid.NewGuid().ToString("N"), workspaceRoot, DateTime.UtcNow);
            _history.Clear();
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
        }

        _output?.Write(OutputChannel, $"Session started: {_session.Id}.", OutputSeverity.Info);
        return _session;
    }

    /// <inheritdoc />
    public async Task<AssistantResponse> SendMessageAsync(string message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        CancellationTokenSource cts;
        lock (_lock)
        {
            if (_session is null)
                throw new InvalidOperationException("No active session. Call StartSession first.");

            cts = _cts!;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, cancellationToken);

        var userMsg = AssistantMessage.FromUser(message);

        lock (_lock) _history.Add(userMsg);

        _output?.Write(OutputChannel, $"User: {message}");

        IReadOnlyList<AssistantMessage> snapshot;
        lock (_lock) snapshot = _history.ToList();

        var response = await _backend(snapshot, linked.Token).ConfigureAwait(false);

        lock (_lock) _history.Add(response.Message);

        _output?.Write(OutputChannel, $"Assistant: {response.Message.Content}");

        return response;
    }

    /// <inheritdoc />
    public void Cancel()
    {
        lock (_lock)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = _session is not null ? new CancellationTokenSource() : null;
        }

        _output?.Write(OutputChannel, "Request cancelled.");
    }

    /// <inheritdoc />
    public IReadOnlyList<AssistantMessage> GetHistory()
    {
        lock (_lock) return _history.ToList();
    }

    /// <inheritdoc />
    public void EndSession()
    {
        lock (_lock)
        {
            if (_session is null) return;

            _output?.Write(OutputChannel, $"Session ended: {_session.Id}. Messages: {_history.Count}.");
            _session = null;
            _history.Clear();
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }
    }
}

using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Host;

/// <summary>
/// View-model for the OMWiA Chat panel. Manages session lifecycle, message
/// history, and async send. Works with <see cref="IAssistantService"/> via
/// <see cref="ICapabilityRegistry"/> — gracefully degrades when unavailable.
/// </summary>
public sealed class OmwiaChatViewModel
{
    private readonly IAssistantService _assistant;
    private readonly IOutput _output;

    /// <summary>
    /// The current chat message history (user + assistant messages).
    /// </summary>
    public IReadOnlyList<AssistantMessage> Messages => _assistant.GetHistory();

    /// <summary>
    /// Whether a send operation is currently in progress.
    /// </summary>
    public bool IsBusy { get; private set; }

    /// <summary>
    /// Last error message, or null.
    /// </summary>
    public string? LastError { get; private set; }

    /// <summary>
    /// Raised after the message list changes (send completed or session started).
    /// </summary>
    public event Action? MessagesChanged;

    /// <summary>
    /// Initializes a new <see cref="OmwiaChatViewModel"/>.
    /// </summary>
    /// <param name="assistant">The assistant service.</param>
    /// <param name="output">Output service for logging.</param>
    /// <exception cref="ArgumentNullException">When a required parameter is null.</exception>
    public OmwiaChatViewModel(IAssistantService assistant, IOutput output)
    {
        _assistant = assistant ?? throw new ArgumentNullException(nameof(assistant));
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    /// <summary>
    /// Ensures an active session exists. Starts one if needed.
    /// </summary>
    /// <param name="workspaceRoot">Optional workspace root.</param>
    public void EnsureSession(string? workspaceRoot = null)
    {
        if (_assistant.ActiveSession is null)
        {
            _assistant.StartSession(workspaceRoot);
            _output.Write("OMWiA", "Chat session started.");
            MessagesChanged?.Invoke();
        }
    }

    /// <summary>
    /// Sends a user message and awaits the assistant response.
    /// </summary>
    /// <param name="message">User message text.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The assistant's response message content.</returns>
    /// <exception cref="ArgumentException">When message is empty.</exception>
    public async Task<string> SendAsync(string message, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(message))
            throw new ArgumentException("Message must not be empty.", nameof(message));

        EnsureSession();

        IsBusy = true;
        LastError = null;

        try
        {
            var response = await _assistant.SendMessageAsync(message, cancellationToken);
            MessagesChanged?.Invoke();
            return response.Message.Content;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            _output.Write("OMWiA", $"Chat error: {ex.Message}");
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Cancels any in-progress send operation.
    /// </summary>
    public void Cancel()
    {
        _assistant.Cancel();
    }

    /// <summary>
    /// Ends the current chat session.
    /// </summary>
    public void EndSession()
    {
        _assistant.EndSession();
        _output.Write("OMWiA", "Chat session ended.");
        MessagesChanged?.Invoke();
    }
}

/// <summary>
/// OMWiA Chat extension: registers a Chat panel and commands for interacting
/// with the assistant. Uses <see cref="IAssistantService"/> from capabilities.
/// Gracefully inactive when no assistant service is available.
/// </summary>
public sealed class OmwiaChatExtension : IJunexisExtension
{
    private OmwiaChatViewModel? _viewModel;

    /// <inheritdoc />
    public string Id => "junexis.omwia.chat";

    /// <inheritdoc />
    public void Initialize(IHostContext context)
    {
        if (!context.Capabilities.TryGet<IAssistantService>(out var assistant) || assistant is null)
        {
            context.Output.Write("OMWiA", "IAssistantService not available — Chat panel inactive.");
            return;
        }

        _viewModel = new OmwiaChatViewModel(assistant, context.Output);

        context.Panels.RegisterPanel(
            "junexis.omwia.chat",
            PanelType.Tool,
            () => _viewModel,
            title: "OMWiA Chat",
            dockGroup: DockGroup.Right);

        context.Commands.Register("omwia.chat.open", () =>
        {
            _viewModel.EnsureSession(context.Workspace.RootPath);
        },
        menuPath: null, displayName: "New Session", category: "OMWiA", contextTarget: "junexis.omwia.chat");

        context.Commands.Register("omwia.chat.endSession", () =>
        {
            _viewModel.EndSession();
        },
        menuPath: null, displayName: "End Session", category: "OMWiA", contextTarget: "junexis.omwia.chat");

        context.Output.Write("OMWiA", "OmwiaChatExtension loaded.");
    }

    /// <inheritdoc />
    public void Shutdown()
    {
        _viewModel = null;
    }
}

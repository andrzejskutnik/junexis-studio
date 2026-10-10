using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Core;

/// <summary>
/// Wraps <see cref="IProcessService"/> and publishes terminal events
/// (<see cref="ProcessStartedEvent"/>, <see cref="ProcessOutputReceivedEvent"/>,
/// <see cref="ProcessExitedEvent"/>) to <see cref="IEventBus"/>.
/// Consumers can subscribe without depending on the terminal implementation.
/// </summary>
public sealed class TerminalEventPublisher
{
    private readonly IProcessService _processes;
    private readonly IEventBus _eventBus;
    private int _counter;

    /// <summary>
    /// Creates a new terminal event publisher.
    /// </summary>
    /// <param name="processes">Process service to execute and wrap.</param>
    /// <param name="eventBus">Event bus for publishing terminal events.</param>
    /// <exception cref="ArgumentNullException">If any argument is null.</exception>
    public TerminalEventPublisher(IProcessService processes, IEventBus eventBus)
    {
        _processes = processes ?? throw new ArgumentNullException(nameof(processes));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
    }

    /// <summary>
    /// Runs a process and publishes start/output/exit events to the event bus.
    /// The original <paramref name="listener"/> callback is still invoked if provided.
    /// </summary>
    /// <param name="request">Process request.</param>
    /// <param name="listener">Optional additional listener for output lines.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Process result.</returns>
    public async Task<ProcessResult> RunAsync(
        ProcessRequest request,
        Action<ProcessOutputLine>? listener,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var processId = GenerateProcessId();

        _eventBus.Publish(new ProcessStartedEvent(
            processId,
            request.FileName,
            request.Arguments,
            DateTimeOffset.UtcNow));

        void CombinedListener(ProcessOutputLine line)
        {
            _eventBus.Publish(new ProcessOutputReceivedEvent(
                processId,
                line.Kind,
                line.Text,
                line.TimestampUtc));

            listener?.Invoke(line);
        }

        var result = await _processes.RunAsync(request, CombinedListener, cancellationToken);

        _eventBus.Publish(new ProcessExitedEvent(
            processId,
            result.ExitCode,
            result.Duration,
            DateTimeOffset.UtcNow));

        return result;
    }

    private string GenerateProcessId()
    {
        var id = Interlocked.Increment(ref _counter);
        return $"proc-{id}";
    }
}

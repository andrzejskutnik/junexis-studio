namespace Junexis.Studio.Abstractions;

/// <summary>
/// Published to <see cref="IEventBus"/> when a terminal process starts.
/// </summary>
/// <param name="ProcessId">Unique process identifier (assigned by publisher).</param>
/// <param name="FileName">Executable file name.</param>
/// <param name="Arguments">Process arguments.</param>
/// <param name="TimestampUtc">UTC timestamp when the process started.</param>
public sealed record ProcessStartedEvent(
    string ProcessId,
    string FileName,
    string Arguments,
    DateTimeOffset TimestampUtc);

/// <summary>
/// Published to <see cref="IEventBus"/> when a terminal process emits a line of output.
/// </summary>
/// <param name="ProcessId">Unique process identifier.</param>
/// <param name="Kind">Output stream kind (stdout/stderr).</param>
/// <param name="Text">Output line text (without newline).</param>
/// <param name="TimestampUtc">UTC timestamp when the line was received.</param>
public sealed record ProcessOutputReceivedEvent(
    string ProcessId,
    ProcessOutputKind Kind,
    string Text,
    DateTimeOffset TimestampUtc);

/// <summary>
/// Published to <see cref="IEventBus"/> when a terminal process exits.
/// </summary>
/// <param name="ProcessId">Unique process identifier.</param>
/// <param name="ExitCode">Process exit code.</param>
/// <param name="Duration">Total execution time.</param>
/// <param name="TimestampUtc">UTC timestamp when the process exited.</param>
public sealed record ProcessExitedEvent(
    string ProcessId,
    int ExitCode,
    TimeSpan Duration,
    DateTimeOffset TimestampUtc);

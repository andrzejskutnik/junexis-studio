using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Host;

/// <summary>Bridges editor hover requests to DAP evaluation in the active stopped frame.</summary>
public sealed class DebugHoverService : IDebugHoverService
{
    private readonly IDebugSessionService _session;

    /// <summary>Creates a hover evaluator backed by one debug session.</summary>
    /// <param name="session">Debug session used for expression evaluation.</param>
    public DebugHoverService(IDebugSessionService session) =>
        _session = session ?? throw new ArgumentNullException(nameof(session));

    /// <inheritdoc />
    public Task<DebugEvaluation?> EvaluateAsync(
        string expression,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);
        var snapshot = _session.Snapshot;
        var frame = snapshot.StackFrames.FirstOrDefault();
        if (snapshot.State != DebugSessionState.Stopped || frame is null)
            return Task.FromResult<DebugEvaluation?>(null);
        return EvaluateCoreAsync(expression, frame.Id, cancellationToken);
    }

    private async Task<DebugEvaluation?> EvaluateCoreAsync(
        string expression,
        int frameId,
        CancellationToken cancellationToken) =>
        await _session.EvaluateAsync(expression, frameId, cancellationToken).ConfigureAwait(false);
}

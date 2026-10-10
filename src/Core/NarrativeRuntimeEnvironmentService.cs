using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Core;

/// <summary>Projects a verified managed Narrative session into a child-runtime environment.</summary>
public sealed class NarrativeRuntimeEnvironmentService : INarrativeRuntimeEnvironmentService
{
    private readonly INarrativeSessionService _session;

    /// <summary>Creates the resolver over the managed Narrative session.</summary>
    /// <param name="session">Managed session whose current snapshot is authoritative.</param>
    public NarrativeRuntimeEnvironmentService(INarrativeSessionService session) =>
        _session = session ?? throw new ArgumentNullException(nameof(session));

    /// <inheritdoc />
    public bool IsPreparationPending =>
        _session.Snapshot.State is NarrativeSessionState.Starting
            or NarrativeSessionState.Stopping;

    /// <inheritdoc />
    public NarrativeRuntimeEnvironment? GetCurrent()
    {
        var snapshot = _session.Snapshot;
        if (snapshot.State != NarrativeSessionState.Ready
            || string.IsNullOrWhiteSpace(snapshot.BaseUrl)
            || string.IsNullOrWhiteSpace(snapshot.BuildId)
            || !Uri.TryCreate(snapshot.BaseUrl, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttp
            || !uri.IsLoopback)
        {
            return null;
        }

        return new NarrativeRuntimeEnvironment(
            snapshot.BaseUrl.TrimEnd('/'),
            snapshot.BuildId);
    }

    /// <inheritdoc />
    public async Task<NarrativeRuntimeEnvironment?> WaitForCurrentAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (timeout < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout));

        var current = GetCurrent();
        if (current is not null)
            return current;
        if (!IsPreparationPending)
        {
            return null;
        }

        var completion = new TaskCompletionSource<NarrativeRuntimeEnvironment?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        void OnSnapshotChanged(NarrativeSessionSnapshot snapshot)
        {
            var environment = GetCurrent();
            if (environment is not null)
                completion.TrySetResult(environment);
            else if (snapshot.State is NarrativeSessionState.Inactive
                     or NarrativeSessionState.Failed)
                completion.TrySetResult(null);
        }

        _session.SnapshotChanged += OnSnapshotChanged;
        try
        {
            current = GetCurrent();
            if (current is not null)
                return current;
            if (_session.Snapshot.State is NarrativeSessionState.Inactive
                or NarrativeSessionState.Failed)
            {
                return null;
            }

            try
            {
                return await completion.Task
                    .WaitAsync(timeout, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                return null;
            }
        }
        finally
        {
            _session.SnapshotChanged -= OnSnapshotChanged;
        }
    }
}

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Host;

/// <summary>
/// Records privacy-bounded Git mutation evidence and correlates commits with
/// deterministic Junexis analysis context.
/// </summary>
public sealed class GitVcsEvidenceService : IVcsEvidenceStore, IDisposable
{
    private const int MaximumEntries = 200;
    private const int MaximumPaths = 100;
    private readonly object _gate = new();
    private readonly List<VcsOperationEvidence> _entries = [];
    private readonly IEventBus _eventBus;
    private readonly IAuditService _audit;
    private string? _buildId;
    private string? _mentorBuildId;
    private int? _mentorFindingCount;
    private string? _storyBuildId;
    private string? _diagnosticStoryId;
    private bool _disposed;

    /// <summary>Creates a Git evidence recorder.</summary>
    /// <param name="eventBus">Studio event bus.</param>
    /// <param name="audit">Shared audit service.</param>
    public GitVcsEvidenceService(IEventBus eventBus, IAuditService audit)
    {
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _eventBus.Subscribe<AnalysisSnapshotUpdatedEvent>(OnAnalysisSnapshotUpdated);
        _eventBus.Subscribe<MentorAnalysisCompletedEvent>(OnMentorAnalysisCompleted);
        _eventBus.Subscribe<DiagnosticStoryLoadedEvent>(OnDiagnosticStoryLoaded);
    }

    /// <inheritdoc />
    public IReadOnlyList<VcsOperationEvidence> GetEntries()
    {
        lock (_gate)
            return _entries.ToArray();
    }

    /// <summary>Records one completed mutation.</summary>
    /// <param name="operation">Stable operation name.</param>
    /// <param name="startedUtc">Operation start time.</param>
    /// <param name="repositoryRoot">Repository root used by Git.</param>
    /// <param name="before">Repository state before the mutation.</param>
    /// <param name="after">Repository state after the mutation.</param>
    /// <param name="paths">User-selected repository-relative paths.</param>
    /// <param name="target">Sanitized operation target.</param>
    /// <param name="success">Whether the mutation succeeded.</param>
    /// <param name="resultCode">Stable result code.</param>
    public void Record(
        string operation,
        DateTimeOffset startedUtc,
        string? repositoryRoot,
        VcsRepositorySnapshot before,
        VcsRepositorySnapshot after,
        IEnumerable<string>? paths,
        string? target,
        bool success,
        string resultCode)
    {
        var root = repositoryRoot ?? before.RootPath ?? after.RootPath ?? string.Empty;
        var isCommit = string.Equals(operation, "commit", StringComparison.Ordinal);
        string? buildId;
        int? mentorFindingCount;
        string? storyId;
        lock (_gate)
        {
            buildId = isCommit ? _buildId : null;
            mentorFindingCount = isCommit
                && string.Equals(_mentorBuildId, _buildId, StringComparison.Ordinal)
                ? _mentorFindingCount
                : null;
            storyId = isCommit
                && string.Equals(_storyBuildId, _buildId, StringComparison.Ordinal)
                ? _diagnosticStoryId
                : null;
        }

        var completedUtc = DateTimeOffset.UtcNow;
        var evidence = new VcsOperationEvidence(
            Guid.NewGuid().ToString("N"),
            SanitizeToken(operation, 32) ?? "unknown",
            completedUtc,
            Math.Max(0, (long)(completedUtc - startedUtc).TotalMilliseconds),
            string.IsNullOrWhiteSpace(root)
                ? "(none)"
                : SanitizeToken(Path.GetFileName(Path.TrimEndingDirectorySeparator(root)), 128) ?? "(redacted)",
            ComputeRepositoryId(root),
            SanitizeToken(before.Branch, 128),
            SanitizeToken(after.Branch, 128),
            ShortObjectId(before.HeadOid),
            ShortObjectId(after.HeadOid),
            SanitizePaths(paths, root),
            SanitizeToken(target, 256),
            success,
            SanitizeToken(resultCode, 32) ?? "failed",
            isCommit ? SanitizeToken(buildId, 128) : null,
            mentorFindingCount,
            storyId);

        lock (_gate)
        {
            _entries.Insert(0, evidence);
            if (_entries.Count > MaximumEntries)
                _entries.RemoveRange(MaximumEntries, _entries.Count - MaximumEntries);
        }

        _audit.Record(new AuditEntry(
            $"vcs.{evidence.Operation}",
            JsonSerializer.Serialize(evidence),
            evidence.TimestampUtc.UtcDateTime,
            "Git"));
        _eventBus.Publish(new VcsOperationEvidenceRecordedEvent(evidence));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _eventBus.Unsubscribe<AnalysisSnapshotUpdatedEvent>(OnAnalysisSnapshotUpdated);
        _eventBus.Unsubscribe<MentorAnalysisCompletedEvent>(OnMentorAnalysisCompleted);
        _eventBus.Unsubscribe<DiagnosticStoryLoadedEvent>(OnDiagnosticStoryLoaded);
        GC.SuppressFinalize(this);
    }

    private void OnAnalysisSnapshotUpdated(AnalysisSnapshotUpdatedEvent update)
    {
        lock (_gate)
        {
            _buildId = update.BuildId;
            if (!string.Equals(_mentorBuildId, update.BuildId, StringComparison.Ordinal))
            {
                _mentorBuildId = null;
                _mentorFindingCount = null;
            }
            if (!string.Equals(_storyBuildId, update.BuildId, StringComparison.Ordinal))
            {
                _storyBuildId = null;
                _diagnosticStoryId = null;
            }
        }
    }

    private void OnMentorAnalysisCompleted(MentorAnalysisCompletedEvent update)
    {
        lock (_gate)
        {
            if (!string.Equals(update.BuildId, _buildId, StringComparison.Ordinal))
                return;
            _mentorBuildId = update.BuildId;
            _mentorFindingCount = Math.Max(0, update.FindingCount);
        }
    }

    private void OnDiagnosticStoryLoaded(DiagnosticStoryLoadedEvent update)
    {
        lock (_gate)
        {
            if (string.IsNullOrWhiteSpace(update.BuildId)
                || !string.Equals(update.BuildId, _buildId, StringComparison.Ordinal))
                return;
            _storyBuildId = update.BuildId;
            _diagnosticStoryId = string.IsNullOrWhiteSpace(update.NarrativeId)
                ? $"event:{update.TargetEventId}"
                : $"{SanitizeToken(update.NarrativeId, 128)}:{update.TargetEventId}";
        }
    }

    private static string ComputeRepositoryId(string root)
    {
        var normalized = root.Trim().Replace('\\', '/').ToUpperInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(hash.AsSpan(0, 8)).ToLowerInvariant();
    }

    private static string? ShortObjectId(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : SanitizeToken(value, 12);

    private static IReadOnlyList<string> SanitizePaths(
        IEnumerable<string>? paths,
        string repositoryRoot)
    {
        if (paths is null)
            return [];
        var sanitized = new List<string>();
        foreach (var candidate in paths.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            var path = candidate;
            try
            {
                if (Path.IsPathRooted(path) && !string.IsNullOrWhiteSpace(repositoryRoot))
                    path = Path.GetRelativePath(repositoryRoot, path);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
            {
                path = "(redacted)";
            }
            path = path.Replace('\\', '/').TrimStart('/');
            if (path.Equals("..", StringComparison.Ordinal)
                || path.StartsWith("../", StringComparison.Ordinal))
                path = "(outside-repository)";
            var value = SanitizeToken(path, 260);
            if (value is not null
                && !sanitized.Contains(value, StringComparer.Ordinal))
                sanitized.Add(value);
            if (sanitized.Count == MaximumPaths)
                break;
        }
        return sanitized;
    }

    private static string? SanitizeToken(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        if (value.Contains("://", StringComparison.Ordinal)
            || value.Contains("token=", StringComparison.OrdinalIgnoreCase)
            || value.Contains("password=", StringComparison.OrdinalIgnoreCase)
            || value.Contains("secret=", StringComparison.OrdinalIgnoreCase)
            || value.Contains("apikey=", StringComparison.OrdinalIgnoreCase)
            || value.Contains("api_key=", StringComparison.OrdinalIgnoreCase))
            return "(redacted)";
        var sanitized = new string(value
            .Where(character => !char.IsControl(character))
            .ToArray())
            .Trim();
        if (sanitized.Length == 0)
            return null;
        return sanitized[..Math.Min(sanitized.Length, maximumLength)];
    }
}

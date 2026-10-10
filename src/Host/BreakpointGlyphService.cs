using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Host;

/// <summary>Combines source breakpoints with verification results from the active DAP session.</summary>
public sealed class BreakpointGlyphService : IBreakpointGlyphService
{
    private readonly IBreakpointService _breakpoints;
    private readonly IDebugSessionService _session;

    /// <summary>Creates the editor breakpoint projection.</summary>
    /// <param name="breakpoints">Shared source breakpoint store.</param>
    /// <param name="session">Active DAP session service.</param>
    public BreakpointGlyphService(IBreakpointService breakpoints, IDebugSessionService session)
    {
        _breakpoints = breakpoints ?? throw new ArgumentNullException(nameof(breakpoints));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _breakpoints.BreakpointsChanged += PublishChange;
        _session.BreakpointStatusesChanged += OnBreakpointStatusesChanged;
    }

    /// <inheritdoc />
    public event Action? GlyphsChanged;

    /// <inheritdoc />
    public IReadOnlyList<BreakpointGlyph> GetGlyphs(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var normalized = Path.GetFullPath(path);
        var statuses = _session.BreakpointStatuses;
        return _breakpoints.GetBreakpoints()
            .Where(item => item.Path.Equals(normalized, StringComparison.OrdinalIgnoreCase))
            .Select(item => CreateGlyph(item, statuses))
            .OrderBy(item => item.Line)
            .ThenBy(item => item.Column)
            .ToArray();
    }

    /// <inheritdoc />
    public async Task ToggleAsync(
        string path,
        int line,
        int? column = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfLessThan(line, 1);
        var normalized = Path.GetFullPath(path);
        _breakpoints.Toggle(normalized, line, column);
        if (_session.Snapshot.State is DebugSessionState.Inactive
            or DebugSessionState.Terminated
            or DebugSessionState.Failed)
            return;
        var forFile = _breakpoints.GetBreakpoints()
            .Where(item => item.Path.Equals(normalized, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        await _session.SetBreakpointsAsync(normalized, forFile, cancellationToken).ConfigureAwait(false);
    }

    private static BreakpointGlyph CreateGlyph(
        DebugSourceBreakpoint breakpoint,
        IReadOnlyList<DebugBreakpointStatus> statuses)
    {
        var matching = statuses.Where(status =>
                status.SourcePath.Equals(breakpoint.Path, StringComparison.OrdinalIgnoreCase)
                && status.SourceLine == breakpoint.Line
                && (breakpoint.Column is null || status.SourceColumn == breakpoint.Column))
            .ToArray();
        if (matching.Length == 0)
            return new(breakpoint.Path, breakpoint.Line, breakpoint.Column,
                BreakpointGlyphState.Pending, DescribeBehavior(breakpoint, "Breakpoint pending adapter verification."),
                IsLogpoint(breakpoint), HasCondition(breakpoint));

        var verified = matching.Count(item => item.Verified);
        var adjusted = matching.Any(item => item.Verified &&
            (item.ActualLine != item.RequestedLine
             || item.RequestedColumn is not null && item.ActualColumn != item.RequestedColumn));
        var state = verified switch
        {
            0 => BreakpointGlyphState.Rejected,
            _ when verified < matching.Length => BreakpointGlyphState.PartiallyVerified,
            _ when adjusted => BreakpointGlyphState.Adjusted,
            _ => BreakpointGlyphState.Verified,
        };
        var details = matching.Select(DescribeStatus).Distinct(StringComparer.Ordinal).ToArray();
        return new(
            breakpoint.Path,
            breakpoint.Line,
            breakpoint.Column,
            state,
            DescribeBehavior(breakpoint,
                $"{DescribeState(state)} ({verified}/{matching.Length} runtime locations). {string.Join(" ", details)}".Trim()),
            IsLogpoint(breakpoint),
            HasCondition(breakpoint));
    }

    private static bool IsLogpoint(DebugSourceBreakpoint breakpoint) =>
        !string.IsNullOrWhiteSpace(breakpoint.LogMessage);

    private static bool HasCondition(DebugSourceBreakpoint breakpoint) =>
        !string.IsNullOrWhiteSpace(breakpoint.Condition) || !string.IsNullOrWhiteSpace(breakpoint.HitCondition);

    private static string DescribeBehavior(DebugSourceBreakpoint breakpoint, string verification)
    {
        var behavior = new List<string>();
        if (IsLogpoint(breakpoint))
            behavior.Add($"Logpoint: {breakpoint.LogMessage}");
        if (!string.IsNullOrWhiteSpace(breakpoint.Condition))
            behavior.Add($"Condition: {breakpoint.Condition}");
        if (!string.IsNullOrWhiteSpace(breakpoint.HitCondition))
            behavior.Add($"Hit condition: {breakpoint.HitCondition}");
        return behavior.Count == 0 ? verification : $"{string.Join(". ", behavior)}. {verification}";
    }

    private static string DescribeStatus(DebugBreakpointStatus status)
    {
        if (!status.Verified)
            return string.IsNullOrWhiteSpace(status.Message) ? "Adapter rejected the location." : status.Message;
        if (status.ActualLine != status.RequestedLine
            || status.RequestedColumn is not null && status.ActualColumn != status.RequestedColumn)
            return $"Adjusted to runtime line {status.ActualLine ?? status.RequestedLine}.";
        return string.IsNullOrWhiteSpace(status.Message) ? "Adapter verified the location." : status.Message;
    }

    private static string DescribeState(BreakpointGlyphState state) => state switch
    {
        BreakpointGlyphState.Pending => "Pending",
        BreakpointGlyphState.Verified => "Verified",
        BreakpointGlyphState.Adjusted => "Adjusted",
        BreakpointGlyphState.PartiallyVerified => "Partially verified",
        BreakpointGlyphState.Rejected => "Rejected",
        _ => state.ToString(),
    };

    private void OnBreakpointStatusesChanged(IReadOnlyList<DebugBreakpointStatus> _) => PublishChange();

    private void PublishChange() => GlyphsChanged?.Invoke();
}

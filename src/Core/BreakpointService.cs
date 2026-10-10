using Junexis.Studio.Abstractions;
using System.Text.Json;

namespace Junexis.Studio.Core;

/// <summary>Thread-safe in-memory source breakpoint store.</summary>
public sealed class BreakpointService : IBreakpointService
{
    private readonly object _gate = new();
    private readonly List<DebugSourceBreakpoint> _breakpoints = [];
    private readonly IWorkspace? _workspace;
    private readonly IOutput? _output;

    /// <summary>Creates a breakpoint store, optionally persisted with a workspace.</summary>
    /// <param name="workspace">Workspace that owns persisted breakpoints.</param>
    /// <param name="output">Optional output used for persistence diagnostics.</param>
    public BreakpointService(IWorkspace? workspace = null, IOutput? output = null)
    {
        _workspace = workspace;
        _output = output;
        if (_workspace is null)
            return;
        _workspace.FolderOpened += OnWorkspaceOpened;
        if (!string.IsNullOrWhiteSpace(_workspace.RootPath))
            Load();
    }

    /// <inheritdoc />
    public event Action? BreakpointsChanged;

    /// <inheritdoc />
    public IReadOnlyList<DebugSourceBreakpoint> GetBreakpoints()
    {
        lock (_gate)
            return _breakpoints.OrderBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Line).ThenBy(item => item.Column).ToArray();
    }

    /// <inheritdoc />
    public bool Toggle(string path, int line, int? column = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (line < 1)
            throw new ArgumentOutOfRangeException(nameof(line));
        var normalized = Path.GetFullPath(path);
        bool added;
        lock (_gate)
        {
            var index = _breakpoints.FindIndex(item =>
                item.Path.Equals(normalized, StringComparison.OrdinalIgnoreCase)
                && item.Line == line
                && item.Column == column);
            if (index >= 0)
            {
                _breakpoints.RemoveAt(index);
                added = false;
            }
            else
            {
                _breakpoints.Add(new DebugSourceBreakpoint(normalized, line, column));
                added = true;
            }
        }
        BreakpointsChanged?.Invoke();
        Save();
        return added;
    }

    /// <inheritdoc />
    public void Set(DebugSourceBreakpoint breakpoint)
    {
        ArgumentNullException.ThrowIfNull(breakpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(breakpoint.Path);
        ArgumentOutOfRangeException.ThrowIfLessThan(breakpoint.Line, 1);
        var normalized = breakpoint with
        {
            Path = Path.GetFullPath(breakpoint.Path),
            Condition = NormalizeOptional(breakpoint.Condition),
            HitCondition = NormalizeOptional(breakpoint.HitCondition),
            LogMessage = NormalizeOptional(breakpoint.LogMessage),
        };
        lock (_gate)
        {
            var index = _breakpoints.FindIndex(item =>
                item.Path.Equals(normalized.Path, StringComparison.OrdinalIgnoreCase)
                && item.Line == normalized.Line
                && item.Column == normalized.Column);
            if (index >= 0)
                _breakpoints[index] = normalized;
            else
                _breakpoints.Add(normalized);
        }
        BreakpointsChanged?.Invoke();
        Save();
    }

    /// <inheritdoc />
    public void Clear()
    {
        lock (_gate)
            _breakpoints.Clear();
        BreakpointsChanged?.Invoke();
        Save();
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private void OnWorkspaceOpened(string _) => Load();

    private void Load()
    {
        try
        {
            var root = RequireWorkspaceRoot();
            var path = Path.Combine(root, ".junexis", "studio-breakpoints.json");
            var stored = File.Exists(path)
                ? JsonSerializer.Deserialize<StoredBreakpoint[]>(File.ReadAllText(path)) ?? []
                : [];
            var loaded = stored.Select(item => item.ToBreakpoint(root))
                .Where(item => item is not null)
                .Cast<DebugSourceBreakpoint>()
                .Distinct()
                .ToArray();
            lock (_gate)
            {
                _breakpoints.Clear();
                _breakpoints.AddRange(loaded);
            }
            BreakpointsChanged?.Invoke();
        }
        catch (Exception ex)
        {
            _output?.Write("Debug", $"Cannot load workspace breakpoints: {ex.Message}", OutputSeverity.Warning);
        }
    }

    private void Save()
    {
        if (_workspace is null || string.IsNullOrWhiteSpace(_workspace.RootPath))
            return;
        try
        {
            var root = RequireWorkspaceRoot();
            var directory = Path.Combine(root, ".junexis");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "studio-breakpoints.json");
            var temporary = path + ".tmp";
            DebugSourceBreakpoint[] snapshot;
            lock (_gate)
                snapshot = _breakpoints.ToArray();
            var stored = snapshot.Select(item => StoredBreakpoint.From(root, item)).ToArray();
            File.WriteAllText(temporary, JsonSerializer.Serialize(stored, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception ex)
        {
            _output?.Write("Debug", $"Cannot save workspace breakpoints: {ex.Message}", OutputSeverity.Warning);
        }
    }

    private string RequireWorkspaceRoot() => Path.GetFullPath(
        _workspace?.RootPath ?? throw new InvalidOperationException("No workspace is open."));

    private sealed record StoredBreakpoint(
        string Path,
        int Line,
        int? Column,
        string? Condition,
        string? HitCondition,
        string? LogMessage)
    {
        public static StoredBreakpoint From(string root, DebugSourceBreakpoint breakpoint) => new(
            System.IO.Path.GetRelativePath(root, breakpoint.Path), breakpoint.Line, breakpoint.Column,
            breakpoint.Condition, breakpoint.HitCondition, breakpoint.LogMessage);

        public DebugSourceBreakpoint? ToBreakpoint(string root)
        {
            if (Line < 1 || string.IsNullOrWhiteSpace(Path))
                return null;
            var candidate = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, Path));
            var prefix = root.EndsWith(System.IO.Path.DirectorySeparatorChar) ? root : root + System.IO.Path.DirectorySeparatorChar;
            if (!candidate.Equals(root, StringComparison.OrdinalIgnoreCase)
                && !candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return null;
            return new(candidate, Line, Column, Condition, HitCondition, LogMessage);
        }
    }
}

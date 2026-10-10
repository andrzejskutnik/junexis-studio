using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Core;

/// <summary>Adapter wspólnego magazynu diagnostyk dla funkcji edytora.</summary>
public sealed class EditorDiagnosticService : IEditorDiagnosticService, IDisposable
{
    private readonly IDiagnosticService _diagnostics;
    private bool _disposed;

    /// <summary>Tworzy adapter diagnostyk edytora.</summary>
    public EditorDiagnosticService(IDiagnosticService diagnostics)
    {
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        _diagnostics.DiagnosticsChanged += OnDiagnosticsChanged;
    }

    /// <inheritdoc />
    public event Action? DiagnosticsChanged;

    /// <inheritdoc />
    public IReadOnlyList<Diagnostic> GetForPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return [];
        return _diagnostics.GetDiagnostics()
            .Where(diagnostic => HasLocation(diagnostic) && SamePath(diagnostic.FilePath!, path))
            .OrderBy(diagnostic => diagnostic.Line)
            .ThenBy(diagnostic => diagnostic.Column)
            .ThenByDescending(diagnostic => diagnostic.Severity)
            .ToArray();
    }

    /// <inheritdoc />
    public IReadOnlyList<Diagnostic> GetAt(string path, int line, int column)
    {
        var onLine = GetForPath(path).Where(diagnostic => diagnostic.Line == line).ToArray();
        if (onLine.Length <= 1)
            return onLine;

        var close = onLine.Where(diagnostic =>
            diagnostic.Column is null || Math.Abs(diagnostic.Column.Value - column) <= 2).ToArray();
        return close.Length > 0 ? close : onLine;
    }

    /// <inheritdoc />
    public Diagnostic? FindAdjacent(string? path, int line, int column, bool previous)
    {
        var all = _diagnostics.GetDiagnostics()
            .Where(HasLocation)
            .OrderBy(diagnostic => NormalizePath(diagnostic.FilePath!))
            .ThenBy(diagnostic => diagnostic.Line)
            .ThenBy(diagnostic => diagnostic.Column)
            .ThenByDescending(diagnostic => diagnostic.Severity)
            .ToArray();
        if (all.Length == 0)
            return null;

        var currentPath = string.IsNullOrWhiteSpace(path) ? null : NormalizePath(path);
        if (currentPath is null)
            return previous ? all[^1] : all[0];

        if (previous)
        {
            return all.LastOrDefault(diagnostic =>
                       CompareLocation(diagnostic, currentPath, line, column) < 0)
                   ?? all[^1];
        }

        return all.FirstOrDefault(diagnostic =>
                   CompareLocation(diagnostic, currentPath, line, column) > 0)
               ?? all[0];
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _diagnostics.DiagnosticsChanged -= OnDiagnosticsChanged;
    }

    private void OnDiagnosticsChanged() => DiagnosticsChanged?.Invoke();

    private static bool HasLocation(Diagnostic diagnostic) =>
        !string.IsNullOrWhiteSpace(diagnostic.FilePath)
        && diagnostic.Line is > 0
        && diagnostic.Column is > 0;

    private static bool SamePath(string left, string right) =>
        NormalizePath(left).Equals(NormalizePath(right), StringComparison.OrdinalIgnoreCase);

    private static string NormalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return path;
        }
    }

    private static int CompareLocation(
        Diagnostic diagnostic,
        string currentPath,
        int line,
        int column)
    {
        var pathComparison = StringComparer.OrdinalIgnoreCase.Compare(
            NormalizePath(diagnostic.FilePath!),
            currentPath);
        if (pathComparison != 0)
            return pathComparison;
        var lineComparison = diagnostic.Line!.Value.CompareTo(line);
        return lineComparison != 0
            ? lineComparison
            : diagnostic.Column!.Value.CompareTo(column);
    }
}

using Junexis.Studio.Abstractions;
using System;
using System.Collections.Generic;

namespace Junexis.Studio.Core;

/// <summary>
/// Minimalna implementacja <see cref="IDiagnosticService"/> (v0.1).
/// </summary>
public sealed class DiagnosticService : IDiagnosticService
{
    private readonly object _gate = new();
    private readonly List<Diagnostic> _diagnostics = new();

    /// <inheritdoc />
    public event Action? DiagnosticsChanged;

    /// <inheritdoc />
    public void Clear()
    {
        var changed = false;

        lock (_gate)
        {
            if (_diagnostics.Count == 0)
            {
                return;
            }

            _diagnostics.Clear();
            changed = true;
        }

        if (changed)
        {
            DiagnosticsChanged?.Invoke();
        }
    }

    /// <inheritdoc />
    public void Add(Diagnostic diagnostic)
    {
        if (diagnostic is null)
        {
            throw new ArgumentNullException(nameof(diagnostic));
        }

        lock (_gate)
        {
            _diagnostics.Add(diagnostic);
        }

        DiagnosticsChanged?.Invoke();
    }

    /// <inheritdoc />
    public IReadOnlyList<Diagnostic> GetDiagnostics()
    {
        lock (_gate)
        {
            return _diagnostics.ToArray();
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<Diagnostic> GetBySource(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        lock (_gate)
        {
            return _diagnostics
                .Where(d => string.Equals(d.Source, source, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }
    }

    /// <inheritdoc />
    public void ClearBySource(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var changed = false;

        lock (_gate)
        {
            var before = _diagnostics.Count;
            _diagnostics.RemoveAll(d =>
                string.Equals(d.Source, source, StringComparison.OrdinalIgnoreCase));
            changed = _diagnostics.Count != before;
        }

        if (changed)
        {
            DiagnosticsChanged?.Invoke();
        }
    }
}

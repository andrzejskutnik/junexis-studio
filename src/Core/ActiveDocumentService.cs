using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Core;

/// <summary>
/// Minimalna implementacja <see cref="IActiveDocumentService"/> (v0.2.x).
/// </summary>
public sealed class ActiveDocumentService : IActiveDocumentService
{
    private readonly object _gate = new();
    private string? _activePath;

    /// <inheritdoc />
    public event Action? ActiveDocumentChanged;

    /// <inheritdoc />
    public string? ActivePath
    {
        get
        {
            lock (_gate)
            {
                return _activePath;
            }
        }
    }

    /// <inheritdoc />
    public void SetActivePath(string? path)
    {
        var changed = false;

        lock (_gate)
        {
            if (string.Equals(_activePath, path, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _activePath = path;
            changed = true;
        }

        if (changed)
        {
            ActiveDocumentChanged?.Invoke();
        }
    }
}

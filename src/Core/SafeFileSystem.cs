using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Core;

/// <summary>
/// Decorator around <see cref="IFileSystem"/> that enforces a root-jail.
/// All path operations are validated to ensure they resolve within the configured root directory.
/// Attempts to escape the root (e.g., via <c>..</c> traversal) result in <see cref="UnauthorizedAccessException"/>.
/// </summary>
public sealed class SafeFileSystem : IFileSystem
{
    private readonly IFileSystem _inner;
    private readonly string _rootPath;
    private readonly IOutput? _output;

    /// <summary>Output channel name for audit messages.</summary>
    public const string AuditChannel = "Security";

    /// <summary>
    /// Creates a root-jailed file system wrapper.
    /// </summary>
    /// <param name="inner">The underlying file system implementation.</param>
    /// <param name="rootPath">The root directory that acts as a jail. Must exist.</param>
    /// <param name="output">Optional output service for logging security audit events.</param>
    /// <exception cref="ArgumentNullException">When <paramref name="inner"/> or <paramref name="rootPath"/> is null.</exception>
    /// <exception cref="ArgumentException">When <paramref name="rootPath"/> is empty or whitespace.</exception>
    public SafeFileSystem(IFileSystem inner, string rootPath, IOutput? output = null)
    {
        ArgumentNullException.ThrowIfNull(inner);
        if (string.IsNullOrWhiteSpace(rootPath))
            throw new ArgumentException("Root path cannot be empty.", nameof(rootPath));

        _inner = inner;
        _rootPath = Path.GetFullPath(rootPath);
        _output = output;
    }

    /// <summary>
    /// Gets the configured root path for this jail.
    /// </summary>
    public string RootPath => _rootPath;

    /// <inheritdoc />
    public string ReadFile(string path)
    {
        var safe = ValidateAndResolve(path);
        return _inner.ReadFile(safe);
    }

    /// <inheritdoc />
    public void WriteFile(string path, string content)
    {
        var safe = ValidateAndResolve(path);
        _inner.WriteFile(safe, content);
    }

    /// <inheritdoc />
    public bool Exists(string path)
    {
        var safe = ValidateAndResolve(path);
        return _inner.Exists(safe);
    }

    /// <inheritdoc />
    public IEnumerable<string> EnumerateDirectory(string path)
    {
        var safe = ValidateAndResolve(path);
        return _inner.EnumerateDirectory(safe);
    }

    /// <inheritdoc />
    public IDisposable Watch(string rootPath, bool includeSubdirectories, Action<FileSystemChange> onChange)
    {
        var safe = ValidateAndResolve(rootPath);
        return _inner.Watch(safe, includeSubdirectories, onChange);
    }

    /// <summary>
    /// Validates and resolves a path, ensuring it stays within the root jail.
    /// </summary>
    /// <param name="path">The path to validate (absolute or relative).</param>
    /// <returns>The resolved full path, guaranteed to be within the root.</returns>
    /// <exception cref="ArgumentException">When <paramref name="path"/> is empty.</exception>
    /// <exception cref="UnauthorizedAccessException">When the resolved path escapes the root jail.</exception>
    private string ValidateAndResolve(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path cannot be empty.", nameof(path));

        var fullPath = Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(_rootPath, path));

        if (!IsWithinRoot(fullPath))
        {
            var message = $"Path traversal blocked: '{path}' resolves to '{fullPath}' which is outside root '{_rootPath}'.";
            _output?.Write(AuditChannel, message, OutputSeverity.Warning);
            throw new UnauthorizedAccessException(message);
        }

        return fullPath;
    }

    /// <summary>
    /// Checks whether the given full path is within the configured root.
    /// </summary>
    private bool IsWithinRoot(string fullPath)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (string.Equals(fullPath, _rootPath, comparison))
            return true;

        var rootWithSep = _rootPath.EndsWith(Path.DirectorySeparatorChar)
            ? _rootPath
            : _rootPath + Path.DirectorySeparatorChar;

        return fullPath.StartsWith(rootWithSep, comparison);
    }
}

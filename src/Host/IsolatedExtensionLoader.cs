using System.Reflection;
using System.Runtime.Loader;
using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Host;

/// <summary>
/// A collectible <see cref="AssemblyLoadContext"/> used to isolate a single extension.
/// Supports unloading to reclaim resources without restarting the host.
/// </summary>
public sealed class ExtensionLoadContext : AssemblyLoadContext
{
    private readonly string? _basePath;

    /// <summary>
    /// Creates a collectible ALC for extension isolation.
    /// </summary>
    /// <param name="name">Display name for the context (typically extension ID).</param>
    /// <param name="basePath">Base directory for resolving dependencies (null = no probing).</param>
    public ExtensionLoadContext(string name, string? basePath = null)
        : base(name, isCollectible: true)
    {
        _basePath = basePath;
    }

    /// <inheritdoc />
    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (_basePath is null) return null;

        var path = Path.Combine(_basePath, $"{assemblyName.Name}.dll");
        return File.Exists(path) ? LoadFromAssemblyPath(path) : null;
    }
}

/// <summary>
/// Tracks a single isolated extension: its ALC, instance, and status.
/// </summary>
public sealed class IsolatedExtension
{
    /// <summary>Extension instance.</summary>
    public IJunexisExtension Extension { get; }

    /// <summary>The ALC that contains this extension's assemblies.</summary>
    public ExtensionLoadContext Context { get; }

    /// <summary>Whether the extension is currently active (initialized).</summary>
    public bool IsActive { get; internal set; }

    /// <summary>
    /// Creates a tracked isolated extension.
    /// </summary>
    /// <param name="extension">The extension instance.</param>
    /// <param name="context">The owning ALC.</param>
    public IsolatedExtension(IJunexisExtension extension, ExtensionLoadContext context)
    {
        Extension = extension ?? throw new ArgumentNullException(nameof(extension));
        Context = context ?? throw new ArgumentNullException(nameof(context));
    }
}

/// <summary>
/// Loads extensions into isolated <see cref="AssemblyLoadContext"/> instances (one per extension).
/// Supports initialization, shutdown, and unloading (ALC unload).
/// Errors are reported as <see cref="SystemDiagnostic"/> to <see cref="IOutput"/>.
/// </summary>
public sealed class IsolatedExtensionLoader
{
    private readonly List<IsolatedExtension> _extensions = new();
    private readonly IOutput? _output;

    /// <summary>Output channel for isolation reports.</summary>
    public const string OutputChannel = "Host";

    /// <summary>
    /// Currently tracked isolated extensions.
    /// </summary>
    public IReadOnlyList<IsolatedExtension> Extensions => _extensions;

    /// <summary>
    /// Creates an isolated extension loader.
    /// </summary>
    /// <param name="output">Optional output for logging.</param>
    public IsolatedExtensionLoader(IOutput? output = null)
    {
        _output = output;
    }

    /// <summary>
    /// Loads an extension assembly from disk into a new isolated ALC
    /// and discovers all <see cref="IJunexisExtension"/> types.
    /// </summary>
    /// <param name="assemblyPath">Full path to the extension assembly.</param>
    /// <returns>List of isolated extensions found in the assembly.</returns>
    /// <exception cref="ArgumentException">If path is null/empty.</exception>
    public IReadOnlyList<IsolatedExtension> LoadFromAssembly(string assemblyPath)
    {
        if (string.IsNullOrWhiteSpace(assemblyPath))
            throw new ArgumentException("Assembly path is required.", nameof(assemblyPath));

        var basePath = Path.GetDirectoryName(assemblyPath);
        var alcName = Path.GetFileNameWithoutExtension(assemblyPath);
        var alc = new ExtensionLoadContext(alcName, basePath);

        var loaded = new List<IsolatedExtension>();

        try
        {
            var assembly = alc.LoadFromAssemblyPath(Path.GetFullPath(assemblyPath));
            var extensionTypes = assembly.GetTypes()
                .Where(t => typeof(IJunexisExtension).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface);

            foreach (var type in extensionTypes)
            {
                try
                {
                    var instance = (IJunexisExtension)Activator.CreateInstance(type)!;
                    var isolated = new IsolatedExtension(instance, alc);
                    _extensions.Add(isolated);
                    loaded.Add(isolated);

                    _output?.Write(OutputChannel,
                        $"Loaded extension '{instance.Id}' in isolated ALC '{alcName}'.",
                        OutputSeverity.Info);
                }
                catch (Exception ex)
                {
                    _output?.Write(OutputChannel,
                        $"Failed to instantiate extension type {type.FullName}: {ex.Message}",
                        OutputSeverity.Error);
                }
            }
        }
        catch (Exception ex)
        {
            _output?.Write(OutputChannel,
                $"Failed to load assembly '{assemblyPath}': {ex.Message}",
                OutputSeverity.Error);

            alc.Unload();
        }

        return loaded;
    }

    /// <summary>
    /// Registers an already-created extension instance in a new isolated ALC.
    /// Useful for testing and built-in extensions.
    /// </summary>
    /// <param name="extension">The extension instance.</param>
    /// <returns>The tracked isolated extension wrapper.</returns>
    public IsolatedExtension Register(IJunexisExtension extension)
    {
        ArgumentNullException.ThrowIfNull(extension);

        var alc = new ExtensionLoadContext(extension.Id);
        var isolated = new IsolatedExtension(extension, alc);
        _extensions.Add(isolated);

        _output?.Write(OutputChannel,
            $"Registered extension '{extension.Id}' in isolated ALC.",
            OutputSeverity.Info);

        return isolated;
    }

    /// <summary>
    /// Initializes a specific isolated extension.
    /// </summary>
    /// <param name="isolated">The extension to initialize.</param>
    /// <param name="context">Host context to pass to the extension.</param>
    public void Initialize(IsolatedExtension isolated, IHostContext context)
    {
        ArgumentNullException.ThrowIfNull(isolated);
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            isolated.Extension.Initialize(context);
            isolated.IsActive = true;

            _output?.Write(OutputChannel,
                $"Extension '{isolated.Extension.Id}' initialized.",
                OutputSeverity.Info);
        }
        catch (Exception ex)
        {
            _output?.Write(OutputChannel,
                $"Extension '{isolated.Extension.Id}' failed to initialize: {ex.Message}",
                OutputSeverity.Error);
        }
    }

    /// <summary>
    /// Initializes all loaded extensions.
    /// </summary>
    /// <param name="context">Host context.</param>
    public void InitializeAll(IHostContext context)
    {
        foreach (var ext in _extensions)
        {
            Initialize(ext, context);
        }
    }

    /// <summary>
    /// Unloads a specific extension: shuts it down, removes from tracking,
    /// and unloads its ALC (allowing GC to collect the assembly).
    /// </summary>
    /// <param name="extensionId">Extension ID to unload.</param>
    /// <returns>True if the extension was found and unloaded.</returns>
    public bool Unload(string extensionId)
    {
        var isolated = _extensions.FirstOrDefault(e =>
            string.Equals(e.Extension.Id, extensionId, StringComparison.OrdinalIgnoreCase));

        if (isolated is null) return false;

        try
        {
            if (isolated.IsActive)
            {
                isolated.Extension.Shutdown();
                isolated.IsActive = false;
            }
        }
        catch (Exception ex)
        {
            _output?.Write(OutputChannel,
                $"Extension '{extensionId}' shutdown error: {ex.Message}",
                OutputSeverity.Warning);
        }

        _extensions.Remove(isolated);

        try
        {
            isolated.Context.Unload();
            _output?.Write(OutputChannel,
                $"Extension '{extensionId}' ALC unloaded.",
                OutputSeverity.Info);
        }
        catch (Exception ex)
        {
            _output?.Write(OutputChannel,
                $"Extension '{extensionId}' ALC unload error: {ex.Message}",
                OutputSeverity.Error);
        }

        return true;
    }

    /// <summary>
    /// Shuts down and unloads all extensions.
    /// </summary>
    public void UnloadAll()
    {
        var ids = _extensions.Select(e => e.Extension.Id).ToList();
        foreach (var id in ids)
        {
            Unload(id);
        }
    }
}

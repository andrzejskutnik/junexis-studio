using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Host;

/// <summary>Discovers configured and locally installed concrete DAP adapters.</summary>
public sealed class DebugAdapterRegistry : IDebugAdapterRegistry
{
    private readonly string _extensionsRoot;
    private readonly string? _path;
    private readonly string _userProfile;
    private readonly string _toolchainsRoot;
    private readonly Func<string, string?> _environment;

    /// <summary>Creates a registry using the current user environment and VS Code installation.</summary>
    public DebugAdapterRegistry()
        : this(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".vscode", "extensions"),
            Environment.GetEnvironmentVariable("PATH"),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetEnvironmentVariable,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Junexis", "toolchains"))
    {
    }

    internal DebugAdapterRegistry(
        string extensionsRoot,
        string? path,
        string userProfile,
        Func<string, string?> environment,
        string? toolchainsRoot = null)
    {
        _extensionsRoot = extensionsRoot;
        _path = path;
        _userProfile = userProfile;
        _environment = environment;
        _toolchainsRoot = toolchainsRoot ?? Path.Combine(userProfile, ".junexis", "toolchains");
    }

    /// <inheritdoc />
    public DebugAdapterDescriptor? Resolve(string profileId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        return profileId.ToLowerInvariant() switch
        {
            "cpp" => ResolveConfigured("cpp", "C++ DAP", "JUNEXIS_CPP_DAP", "cppdbg") ?? ResolveOpenDebugAd7(),
            "go" => ResolveConfigured("go", "Go DAP", "JUNEXIS_GO_DAP", "go") ?? ResolveDelve(),
            "typescript" => ResolveConfigured("typescript", "TypeScript DAP", "JUNEXIS_TYPESCRIPT_DAP", "pwa-node")
                ?? ResolveJsDebug(),
            _ => null,
        };
    }

    /// <inheritdoc />
    public IReadOnlyList<DebugAdapterDescriptor> GetAvailableAdapters() =>
        new[] { "cpp", "go", "typescript" }.Select(Resolve).OfType<DebugAdapterDescriptor>().ToArray();

    private DebugAdapterDescriptor? ResolveConfigured(
        string profileId,
        string displayName,
        string variable,
        string defaultType)
    {
        var fileName = _environment(variable);
        if (string.IsNullOrWhiteSpace(fileName))
            return null;
        var transportText = _environment(variable + "_TRANSPORT");
        var transport = transportText?.Equals("tcp", StringComparison.OrdinalIgnoreCase) == true
            ? DebugAdapterTransport.Tcp
            : DebugAdapterTransport.Stdio;
        return new DebugAdapterDescriptor(
            profileId,
            displayName,
            fileName,
            _environment(variable + "_ARGS") ?? string.Empty,
            transport,
            _environment(variable + "_TYPE") ?? defaultType,
            $"environment:{variable}");
    }

    private DebugAdapterDescriptor? ResolveOpenDebugAd7()
    {
        if (!Directory.Exists(_extensionsRoot))
            return null;
        var candidate = Directory.EnumerateDirectories(_extensionsRoot, "ms-vscode.cpptools-*", SearchOption.TopDirectoryOnly)
            .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => Path.Combine(path, "debugAdapters", "bin", OperatingSystem.IsWindows() ? "OpenDebugAD7.exe" : "OpenDebugAD7"))
            .FirstOrDefault(File.Exists);
        return candidate is null ? null : new DebugAdapterDescriptor(
            "cpp", "VS Code C++ OpenDebugAD7", candidate, string.Empty,
            DebugAdapterTransport.Stdio, "cppdbg", "vscode-extension:ms-vscode.cpptools");
    }

    private DebugAdapterDescriptor? ResolveDelve()
    {
        var executable = DebugToolLocator.Resolve(
            "JUNEXIS_DLV",
            OperatingSystem.IsWindows() ? "dlv.exe" : "dlv",
            [Path.Combine(_userProfile, "go", "bin", OperatingSystem.IsWindows() ? "dlv.exe" : "dlv")],
            _environment,
            _path);
        return !File.Exists(executable) ? null : new DebugAdapterDescriptor(
            "go", "Delve DAP", executable, "dap --listen=127.0.0.1:{port}",
            DebugAdapterTransport.Tcp, "go", "go-tool:dlv");
    }

    private DebugAdapterDescriptor? ResolveJsDebug()
    {
        var root = Path.Combine(_toolchainsRoot, "js-debug");
        if (!Directory.Exists(root))
            return null;
        var server = Directory.EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly)
            .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => Path.Combine(path, "js-debug", "src", "dapDebugServer.js"))
            .FirstOrDefault(File.Exists);
        if (server is null)
            return null;
        var node = DebugToolLocator.FindOnPath(OperatingSystem.IsWindows() ? "node.exe" : "node", _path)
            ?? (OperatingSystem.IsWindows() ? "node.exe" : "node");
        return new DebugAdapterDescriptor(
            "typescript",
            "Microsoft JavaScript Debugger",
            node,
            $"\"{server}\" {{port}}",
            DebugAdapterTransport.Tcp,
            "pwa-node",
            "junexis-toolchain:js-debug");
    }
}

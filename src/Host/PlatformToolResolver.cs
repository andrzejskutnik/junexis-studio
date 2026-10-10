using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Host;

/// <summary>Rozpoznaje polecenia Junexis zgodnie z wieloplatformowym profilem użytkownika.</summary>
public sealed class PlatformToolResolver : IPlatformToolResolver
{
    private readonly string _platform;
    private readonly string _homeDirectory;
    private readonly string _localApplicationData;
    private readonly string? _xdgBinHome;
    private readonly Func<string, string?> _environment;
    private readonly Func<string, bool> _fileExists;

    /// <summary>Tworzy resolver dla bieżącego systemu i środowiska procesu.</summary>
    public PlatformToolResolver()
        : this(
            OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : "linux",
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetEnvironmentVariable("XDG_BIN_HOME"),
            Environment.GetEnvironmentVariable,
            File.Exists)
    {
    }

    internal PlatformToolResolver(
        string platform,
        string homeDirectory,
        string localApplicationData,
        string? xdgBinHome,
        Func<string, string?> environment,
        Func<string, bool> fileExists)
    {
        _platform = platform;
        _homeDirectory = homeDirectory;
        _localApplicationData = localApplicationData;
        _xdgBinHome = xdgBinHome;
        _environment = environment;
        _fileExists = fileExists;
    }

    /// <inheritdoc />
    public string ResolveCommand(string commandName, string? overrideEnvironmentVariable = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandName);
        if (!string.IsNullOrWhiteSpace(overrideEnvironmentVariable)
            && _environment(overrideEnvironmentVariable) is { } configured
            && !string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        foreach (var directory in ProfileBinDirectories())
        {
            var fileName = _platform == "windows" && !commandName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? commandName + ".exe"
                : commandName;
            var candidate = Combine(directory, fileName);
            if (_fileExists(candidate))
                return candidate;
        }
        return commandName;
    }

    private IEnumerable<string> ProfileBinDirectories()
    {
        var configured = _environment("JUNEXIS_PROFILE_BIN");
        if (!string.IsNullOrWhiteSpace(configured))
            yield return configured;

        if (_platform == "windows")
        {
            if (!string.IsNullOrWhiteSpace(_localApplicationData))
                yield return Combine(_localApplicationData, "Junexis", "bin");
            yield break;
        }

        if (!string.IsNullOrWhiteSpace(_xdgBinHome))
            yield return _xdgBinHome;
        else if (!string.IsNullOrWhiteSpace(_homeDirectory))
            yield return Combine(_homeDirectory, ".local", "bin");
    }

    private string Combine(params string[] parts) =>
        _platform == "windows"
            ? string.Join('\\', parts.Select(part => part.Trim('\\', '/')))
            : string.Join('/', parts.Select(part => part.Trim('/'))).Insert(0, parts[0].StartsWith('/') ? "/" : string.Empty);
}

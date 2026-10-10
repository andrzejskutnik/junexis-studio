namespace Junexis.Studio.Host;

internal static class DebugToolLocator
{
    public static string ResolveGdb() =>
        Resolve(
            "JUNEXIS_GDB",
            OperatingSystem.IsWindows() ? "gdb.exe" : "gdb",
            DefaultMsys2Candidates("gdb.exe"));

    public static string ResolveGxx() =>
        Resolve(
            "JUNEXIS_GXX",
            OperatingSystem.IsWindows() ? "g++.exe" : "g++",
            DefaultMsys2Candidates("g++.exe"));

    public static string ResolveDelve() =>
        Resolve(
            "JUNEXIS_DLV",
            OperatingSystem.IsWindows() ? "dlv.exe" : "dlv",
            [Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "go", "bin",
                OperatingSystem.IsWindows() ? "dlv.exe" : "dlv")]);

    internal static string Resolve(
        string environmentVariable,
        string commandName,
        IEnumerable<string> fallbackCandidates,
        Func<string, string?>? environment = null,
        string? path = null)
    {
        environment ??= Environment.GetEnvironmentVariable;
        var configured = environment(environmentVariable);
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        var fromPath = FindOnPath(commandName, path ?? environment("PATH"));
        if (fromPath is not null)
            return fromPath;

        return fallbackCandidates.FirstOrDefault(File.Exists) ?? commandName;
    }

    internal static string? FindOnPath(string fileName, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        foreach (var segment in path.Split(
                     Path.PathSeparator,
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                var candidate = Path.Combine(segment, fileName);
                if (File.Exists(candidate))
                    return candidate;
            }
            catch (ArgumentException)
            {
                // Ignore malformed PATH segments.
            }
        }
        return null;
    }

    private static IEnumerable<string> DefaultMsys2Candidates(string fileName)
    {
        if (!OperatingSystem.IsWindows())
            return [];
        var systemDrive = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.System)) ?? "C:\\";
        return
        [
            Path.Combine(systemDrive, "msys64", "ucrt64", "bin", fileName),
            Path.Combine(systemDrive, "msys64", "mingw64", "bin", fileName),
        ];
    }
}

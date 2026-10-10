namespace Junexis.Studio.Shell;

/// <summary>Pure decisions shared by adaptive shell chrome and its tests.</summary>
internal static class ShellChromePolicy
{
    /// <summary>Determines whether Studio should start with onboarding-only chrome.</summary>
    internal static bool UseCompactEmptyWorkspace(string? rootPath) =>
        string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath);

    /// <summary>Formats compact Activity Bar counters without allowing wide badges.</summary>
    internal static string FormatBadgeCount(int count) =>
        count switch
        {
            <= 0 => string.Empty,
            > 99 => "99+",
            _ => count.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
}

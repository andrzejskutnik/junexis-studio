using Avalonia;
using System;
using System.Text.Json;
using Junexis.Studio.Host;

namespace Junexis.Studio.Shell;

/// <summary>
/// Punkt wejścia powłoki (Shell) opartej o Avalonia.
/// </summary>
class Program
{
    internal static string? ReadinessProbePath { get; private set; }

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    /// <summary>
    /// Uruchamia aplikację desktopową w trybie ClassicDesktopLifetime.
    /// </summary>
    /// <param name="args">Argumenty linii komend.</param>
    public static int Main(string[] args)
    {
        if (args is ["--distribution-manifest", var runtimeIdentifier, var entrypoint, var selfContainedText, var outputPath]
            && bool.TryParse(selfContainedText, out var selfContained))
        {
            var version = typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
            WriteProbe(outputPath, new
            {
                schemaVersion = "junexis.studio-distribution/0.1",
                name = "Junexis Studio",
                version,
                runtimeIdentifier,
                entrypoint,
                selfContained
            });
            return 0;
        }

        if (args is ["--profile-probe", var profileOutputPath])
        {
            var command = new PlatformToolResolver().ResolveCommand("junexis", "JUNEXIS_CLI");
            WriteProbe(profileOutputPath, new
            {
                schemaVersion = "junexis.studio-profile-probe/0.1",
                command
            });
            return 0;
        }

        if (args is ["--readiness-probe", var readinessOutputPath])
        {
            ReadinessProbePath = Path.GetFullPath(readinessOutputPath);
            if (File.Exists(ReadinessProbePath)
                || !Directory.Exists(Path.GetDirectoryName(ReadinessProbePath)))
                return 2;
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime([]);
        }

        if (args.FirstOrDefault()?.StartsWith("--distribution-", StringComparison.Ordinal) is true
            || args.FirstOrDefault()?.StartsWith("--profile-", StringComparison.Ordinal) is true
            || args.FirstOrDefault()?.StartsWith("--readiness-", StringComparison.Ordinal) is true)
            return 2;

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    /// <summary>
    /// Konfiguruje builder aplikacji Avalonia.
    /// </summary>
    /// <returns>Skonfigurowany <see cref="AppBuilder"/>.</returns>
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    private static void WriteProbe(string outputPath, object value)
    {
        var json = JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(outputPath, json);
        Console.WriteLine(json);
    }

    internal static void WriteReadinessProbe(bool hostStarted, bool mainWindowCreated)
    {
        if (ReadinessProbePath is null) return;
        var json = JsonSerializer.Serialize(new
        {
            schemaVersion = "junexis.studio-readiness/0.1",
            status = "ready",
            framework = "avalonia",
            hostStarted,
            mainWindowCreated
        });
        using var stream = new FileStream(ReadinessProbePath, FileMode.CreateNew,
            FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough);
        using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false));
        writer.Write(json);
        writer.Flush();
        stream.Flush(flushToDisk: true);
    }
}

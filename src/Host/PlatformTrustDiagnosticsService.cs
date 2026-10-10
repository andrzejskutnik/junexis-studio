using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Host;

/// <summary>Implementation of <see cref="IPlatformTrustDiagnosticsService"/> delegating to public <c>junexis trust --json</c>.</summary>
public sealed class PlatformTrustDiagnosticsService : IPlatformTrustDiagnosticsService
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);
    private readonly IProcessService _processes;
    private readonly string _junexisExecutable;
    private PlatformTrustSnapshot? _latestSnapshot;

    /// <summary>Creates a trust diagnostics service backed by <see cref="IProcessService"/>.</summary>
    /// <param name="processes">Process runner service.</param>
    /// <param name="junexisExecutable">Optional path to public <c>junexis</c> CLI executable.</param>
    public PlatformTrustDiagnosticsService(IProcessService processes, string? junexisExecutable = null)
    {
        _processes = processes ?? throw new ArgumentNullException(nameof(processes));
        _junexisExecutable = junexisExecutable ?? "junexis";
    }

    /// <inheritdoc />
    public PlatformTrustSnapshot? LatestSnapshot => Volatile.Read(ref _latestSnapshot);

    /// <inheritdoc />
    public async Task<PlatformTrustSnapshot> CheckTrustAsync(string? catalogPath = null, CancellationToken cancellationToken = default)
    {
        var arguments = new List<string> { "environment", "check" }; // Fallback arguments if junexis CLI routing
        var trustArgs = new List<string> { "trust", "--json" };
        if (!string.IsNullOrWhiteSpace(catalogPath))
        {
            trustArgs.Add("--catalog");
            trustArgs.Add(catalogPath);
        }

        var output = new ConcurrentQueue<string>();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(DefaultTimeout);

        try
        {
            var result = await _processes.RunAsync(
                new ProcessRequest(_junexisExecutable, trustArgs),
                line =>
                {
                    if (!string.IsNullOrWhiteSpace(line.Text))
                        output.Enqueue(line.Text.Trim());
                },
                timeout.Token).ConfigureAwait(false);

            var jsonText = string.Join(Environment.NewLine, output);
            if (result.ExitCode == 0 && !string.IsNullOrWhiteSpace(jsonText))
            {
                var parsed = JsonSerializer.Deserialize<PlatformTrustSnapshot>(jsonText, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (parsed is not null)
                {
                    var sanitized = SanitizeSnapshot(parsed);
                    Volatile.Write(ref _latestSnapshot, sanitized);
                    return sanitized;
                }
            }
        }
        catch
        {
            // Fallback to error snapshot below
        }

        var fallback = new PlatformTrustSnapshot(
            SchemaVersion: "junexis.trust-diagnostics/0.1",
            Status: "untrusted",
            Channel: "preview",
            ReleaseVersion: "0.1.0-preview.1",
            Publisher: "Junexis Official Authority",
            FailureReason: "cli-execution-failed",
            Checks:
            [
                new PlatformTrustCheckEntry("cli-execution", "failed", "Public CLI execution failed or returned invalid output.")
            ]);

        Volatile.Write(ref _latestSnapshot, fallback);
        return fallback;
    }

    private static PlatformTrustSnapshot SanitizeSnapshot(PlatformTrustSnapshot raw)
    {
        var sanitizedChecks = raw.Checks.Select(c => new PlatformTrustCheckEntry(
            Name: c.Name,
            Status: c.Status,
            Detail: RedactSensitiveDetails(c.Detail)
        )).ToList();

        return raw with
        {
            Publisher = RedactSensitiveDetails(raw.Publisher),
            Checks = sanitizedChecks
        };
    }

    internal static string RedactSensitiveDetails(string detail)
    {
        if (string.IsNullOrWhiteSpace(detail)) return detail;
        // Redact file paths, bearer tokens, private keys, passwords
        detail = Regex.Replace(detail, @"(?:[A-Za-z]:\\[^""'\s]+|/[^""'\s]{4,})", "[REDACTED_PATH]");
        detail = Regex.Replace(detail, @"(?i)(?:password|secret|token|privatekey)\s*[:=]\s*[^\s]+", "[REDACTED_SECRET]");
        detail = Regex.Replace(detail, @"sha256:[0-9a-f]{64}", "sha256:[REDACTED_KEY_ID]");
        return detail;
    }
}

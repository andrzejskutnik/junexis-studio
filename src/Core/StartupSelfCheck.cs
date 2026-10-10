using Junexis.Studio.Abstractions;
using System.Runtime.InteropServices;

namespace Junexis.Studio.Core;

/// <summary>
/// Runs pluggable startup checks and reports results to Output (Integrity channel)
/// and IDiagnosticService. Built-in checks can be extended via <see cref="AddCheck"/>.
/// </summary>
public sealed class StartupSelfCheck : IStartupSelfCheck
{
    private readonly IOutput? _output;
    private readonly IDiagnosticService? _diagnostics;
    private readonly List<Func<StartupCheckEntry>> _checks = new();

    /// <summary>Output channel for startup check messages.</summary>
    public const string OutputChannel = "Integrity";

    /// <summary>Diagnostics source identifier.</summary>
    public const string DiagnosticSource = "StartupSelfCheck";

    /// <summary>
    /// Creates a new startup self-check runner.
    /// </summary>
    /// <param name="output">Optional output service for logging.</param>
    /// <param name="diagnostics">Optional diagnostics service for failed checks.</param>
    public StartupSelfCheck(IOutput? output = null, IDiagnosticService? diagnostics = null)
    {
        _output = output;
        _diagnostics = diagnostics;
    }

    /// <summary>
    /// Registers a custom check step.
    /// </summary>
    /// <param name="check">Function returning a check entry.</param>
    /// <exception cref="ArgumentNullException">If <paramref name="check"/> is null.</exception>
    public void AddCheck(Func<StartupCheckEntry> check)
    {
        ArgumentNullException.ThrowIfNull(check);
        _checks.Add(check);
    }

    /// <inheritdoc />
    public StartupCheckResult Run()
    {
        var entries = new List<StartupCheckEntry>();

        foreach (var check in _checks)
        {
            try
            {
                var entry = check();
                entries.Add(entry);
            }
            catch (Exception ex)
            {
                entries.Add(new StartupCheckEntry(
                    "Error", $"Check threw: {ex.Message}", false,
                    "Review the check implementation."));
            }
        }

        var allPassed = entries.Count == 0 || entries.All(e => e.Passed);
        var result = new StartupCheckResult(entries, allPassed);

        Report(result);

        return result;
    }

    /// <summary>
    /// Creates a <see cref="StartupSelfCheck"/> with built-in checks for
    /// runtime version, required services and extension conflicts.
    /// </summary>
    /// <param name="output">Optional output service.</param>
    /// <param name="diagnostics">Optional diagnostics service.</param>
    /// <param name="requiredServices">Service names that must be present.</param>
    /// <param name="availableServices">Service names that are actually available.</param>
    /// <param name="extensionIds">Loaded extension identifiers (for duplicate detection).</param>
    /// <param name="minimumRuntimeMajor">Minimum required .NET major version.</param>
    /// <returns>A configured <see cref="StartupSelfCheck"/> instance.</returns>
    public static StartupSelfCheck CreateDefault(
        IOutput? output = null,
        IDiagnosticService? diagnostics = null,
        IReadOnlyList<string>? requiredServices = null,
        IReadOnlyList<string>? availableServices = null,
        IReadOnlyList<string>? extensionIds = null,
        int minimumRuntimeMajor = 10)
    {
        var check = new StartupSelfCheck(output, diagnostics);

        // 1. Runtime version
        check.AddCheck(() =>
        {
            var ver = Environment.Version;
            var passed = ver.Major >= minimumRuntimeMajor;
            return new StartupCheckEntry(
                "Runtime",
                $".NET {ver} (minimum: {minimumRuntimeMajor})",
                passed,
                passed ? null : $"Upgrade to .NET {minimumRuntimeMajor} or later.");
        });

        // 2. Required services
        if (requiredServices is { Count: > 0 })
        {
            var available = new HashSet<string>(
                availableServices ?? [],
                StringComparer.OrdinalIgnoreCase);

            foreach (var svc in requiredServices)
            {
                var s = svc; // capture
                check.AddCheck(() =>
                {
                    var found = available.Contains(s);
                    return new StartupCheckEntry(
                        "Service",
                        $"Required service: {s}",
                        found,
                        found ? null : $"Service '{s}' is missing. Check host configuration.");
                });
            }
        }

        // 3. Extension duplicates
        if (extensionIds is { Count: > 0 })
        {
            check.AddCheck(() =>
            {
                var duplicates = extensionIds
                    .GroupBy(id => id, StringComparer.OrdinalIgnoreCase)
                    .Where(g => g.Count() > 1)
                    .Select(g => g.Key)
                    .ToList();

                if (duplicates.Count == 0)
                {
                    return new StartupCheckEntry(
                        "Extension",
                        $"No duplicate extensions ({extensionIds.Count} loaded)",
                        true, null);
                }

                return new StartupCheckEntry(
                    "Extension",
                    $"Duplicate extension IDs: {string.Join(", ", duplicates)}",
                    false,
                    "Remove duplicate extensions or rename their IDs.");
            });
        }

        return check;
    }

    // ── Private ──────────────────────────────────────────────────

    private void Report(StartupCheckResult result)
    {
        if (_output is null && _diagnostics is null) return;

        if (result.AllPassed)
        {
            _output?.Write(OutputChannel,
                $"Startup self-check passed ({result.Entries.Count} check(s)).",
                OutputSeverity.Info);
        }
        else
        {
            var failed = result.Entries.Where(e => !e.Passed).ToList();
            _output?.Write(OutputChannel,
                $"Startup self-check: {failed.Count} issue(s) found.",
                OutputSeverity.Warning);

            foreach (var entry in failed)
            {
                _output?.Write(OutputChannel,
                    $"[{entry.Category}] {entry.Description}" +
                    (entry.Fix is not null ? $" → {entry.Fix}" : ""),
                    OutputSeverity.Warning);

                _diagnostics?.Add(new Diagnostic(
                    severity: DiagnosticSeverity.Warning,
                    message: $"[{entry.Category}] {entry.Description}",
                    source: DiagnosticSource));
            }
        }
    }
}

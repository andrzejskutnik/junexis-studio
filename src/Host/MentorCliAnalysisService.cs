using System.Text;
using System.Text.Json;
using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Host;

/// <summary>Runs the external Junexis Mentor CLI and parses its JSON contract.</summary>
public sealed class MentorCliAnalysisService : IMentorAnalysisService
{
    private readonly IProcessService _processes;
    private readonly string _executablePath;

    /// <summary>Creates a Mentor CLI adapter.</summary>
    /// <param name="processes">Studio process service.</param>
    /// <param name="executablePath">
    /// Optional tool or DLL path; defaults to `JUNEXIS_MENTOR_CLI` or `junexis-mentor`.
    /// </param>
    public MentorCliAnalysisService(IProcessService processes, string? executablePath = null)
    {
        _processes = processes ?? throw new ArgumentNullException(nameof(processes));
        _executablePath = executablePath
            ?? Environment.GetEnvironmentVariable("JUNEXIS_MENTOR_CLI")
            ?? ResolveDevelopmentCli()
            ?? "junexis-mentor";
    }

    /// <inheritdoc />
    public async Task<MentorAnalysisResult> AnalyzeAsync(
        string snapshotPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotPath);
        var fullPath = Path.GetFullPath(snapshotPath);
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var (fileName, arguments) = CreateCommand(fullPath);
        var result = await _processes.RunAsync(
            new ProcessRequest(fileName, arguments, Path.GetDirectoryName(fullPath)),
            line =>
            {
                var builder = line.Kind == ProcessOutputKind.StdOut ? stdout : stderr;
                lock (builder)
                    builder.AppendLine(line.Text);
            },
            cancellationToken).ConfigureAwait(false);

        if (result.ExitCode != 0)
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(stderr.ToString())
                    ? $"Mentor CLI exited with code {result.ExitCode}."
                    : stderr.ToString().Trim());
        return Parse(stdout.ToString());
    }

    internal static MentorAnalysisResult Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var schema = root.GetProperty("schemaVersion").GetString() ?? string.Empty;
            if (!schema.StartsWith("1.", StringComparison.Ordinal))
                throw new InvalidDataException($"Unsupported Mentor schema '{schema}'.");
            if (!root.GetProperty("success").GetBoolean())
                throw new InvalidDataException("Mentor reported an unsuccessful analysis.");

            var findings = root.GetProperty("findings").EnumerateArray().Select(item =>
            {
                AnalysisSourceLocation? source = null;
                if (item.TryGetProperty("source", out var sourceElement) && sourceElement.ValueKind == JsonValueKind.Object)
                {
                    source = new AnalysisSourceLocation(
                        sourceElement.GetProperty("file").GetString() ?? string.Empty,
                        sourceElement.GetProperty("line").GetInt32(),
                        sourceElement.GetProperty("column").GetInt32());
                }

                return new MentorFinding(
                    item.GetProperty("code").GetString() ?? string.Empty,
                    item.GetProperty("severity").GetString() ?? "info",
                    item.GetProperty("message").GetString() ?? string.Empty,
                    item.TryGetProperty("relatedSymbolId", out var related) && related.ValueKind == JsonValueKind.String
                        ? related.GetString()
                        : null,
                    source,
                    OptionalString(item, "explanation"),
                    OptionalString(item, "recommendedAction"));
            }).ToArray();
            var summary = root.TryGetProperty("summary", out var summaryElement) &&
                          summaryElement.ValueKind == JsonValueKind.Object
                ? new MentorAnalysisSummary(
                    OptionalInt(summaryElement, "taskCount"),
                    OptionalInt(summaryElement, "pipelineCount"),
                    OptionalInt(summaryElement, "narrativeCount"),
                    OptionalInt(summaryElement, "platformCount"),
                    OptionalInt(summaryElement, "findingCount"),
                    OptionalInt(summaryElement, "insightCount"))
                : null;
            var insights = root.TryGetProperty("insights", out var insightsElement) &&
                           insightsElement.ValueKind == JsonValueKind.Array
                ? insightsElement.EnumerateArray().Select(item => new MentorInsight(
                    OptionalString(item, "kind") ?? string.Empty,
                    OptionalString(item, "summary") ?? string.Empty,
                    item.TryGetProperty("score", out var score) && score.ValueKind == JsonValueKind.Number
                        ? score.GetDouble()
                        : null,
                    StringArray(item, "relatedSymbolIds"))).ToArray()
                : [];
            var patterns = root.TryGetProperty("patterns", out var patternsElement) &&
                           patternsElement.ValueKind == JsonValueKind.Array
                ? patternsElement.EnumerateArray().Select(item => new MentorPattern(
                    OptionalString(item, "patternId") ?? string.Empty,
                    OptionalString(item, "name") ?? string.Empty,
                    item.TryGetProperty("confidence", out var confidence) &&
                    confidence.ValueKind == JsonValueKind.Number
                        ? confidence.GetDouble()
                        : 0,
                    OptionalString(item, "rationale") ?? string.Empty,
                    StringArray(item, "participantSymbolIds"))).ToArray()
                : [];
            var conformance = root.TryGetProperty("conformance", out var conformanceElement) &&
                              conformanceElement.ValueKind == JsonValueKind.Object
                ? new MentorConformanceOpinion(
                    OptionalString(conformanceElement, "status") ?? "unknown",
                    OptionalString(conformanceElement, "targetLevel") ?? string.Empty,
                    OptionalInt(conformanceElement, "scenarioCount"),
                    OptionalInt(conformanceElement, "targetSatisfiedCount"),
                    OptionalInt(conformanceElement, "gapCount"),
                    conformanceElement.TryGetProperty("gaps", out var gapsElement) && gapsElement.ValueKind == JsonValueKind.Array
                        ? gapsElement.EnumerateArray().Select(gap => new MentorConformanceGap(
                            OptionalString(gap, "id") ?? string.Empty,
                            OptionalString(gap, "achievedLevel") ?? string.Empty,
                            OptionalString(gap, "targetLevel") ?? string.Empty)).ToArray()
                        : [],
                    conformanceElement.TryGetProperty("blocking", out var blocking) && blocking.GetBoolean(),
                    OptionalString(conformanceElement, "opinion") ?? string.Empty,
                    conformanceElement.TryGetProperty("bound", out var bound) && bound.GetBoolean(),
                    OptionalString(conformanceElement, "buildId"),
                    OptionalString(conformanceElement, "bundleId"),
                    OptionalString(conformanceElement, "bindingSha256"))
                : null;
            return new MentorAnalysisResult(
                root.GetProperty("buildId").GetString() ?? string.Empty,
                root.GetProperty("snapshotPath").GetString() ?? string.Empty,
                findings,
                summary,
                insights,
                patterns,
                schema,
                conformance);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Mentor output is not valid JSON.", ex);
        }
    }

    private static string? OptionalString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int OptionalInt(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : 0;

    private static IReadOnlyList<string> StringArray(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString() ?? string.Empty)
                .ToArray()
            : [];

    private (string FileName, string Arguments) CreateCommand(string snapshotPath)
    {
        var analyzeArguments = $"analyze --snapshot {Quote(snapshotPath)} --json";
        if (FindConformanceReport(snapshotPath) is { } conformancePath)
            analyzeArguments += $" --conformance {Quote(conformancePath)}";
        return _executablePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            ? ("dotnet", $"{Quote(Path.GetFullPath(_executablePath))} {analyzeArguments}")
            : (_executablePath, analyzeArguments);
    }

    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";

    internal static string? FindConformanceReport(string snapshotPath)
    {
        var analysisDirectory = Path.GetDirectoryName(Path.GetFullPath(snapshotPath));
        if (analysisDirectory is null)
            return null;
        var outputDirectory = Directory.GetParent(analysisDirectory)?.FullName;
        var projectDirectory = outputDirectory is null ? null : Directory.GetParent(outputDirectory)?.FullName;
        var candidates = new[]
        {
            Path.Combine(analysisDirectory, "semantic-conformance.json"),
            outputDirectory is null ? null : Path.Combine(outputDirectory, "semantic-conformance.json"),
            projectDirectory is null ? null : Path.Combine(projectDirectory, ".junexis", "semantic-conformance.json"),
            projectDirectory is null ? null : Path.Combine(projectDirectory, "semantic-conformance.json")
        };
        return candidates.FirstOrDefault(candidate => candidate is not null && File.Exists(candidate));
    }

    internal static string? ResolveDevelopmentCli()
    {
        foreach (var environmentVariable in new[] { "JUNEXIS_MENTOR_CLI", "JUNEXIS_COMPILER_CLI" })
        {
            var configured = Environment.GetEnvironmentVariable(environmentVariable);
            if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
                return configured;
        }

        foreach (var candidate in EnumerateCommandCandidates("junexis-mentor", "JunexisLang.Mentor.CLI.dll"))
        {
            if (File.Exists(candidate))
                return candidate;
        }

        foreach (var root in CandidateRoots())
        {
            foreach (var configuration in new[] { "Debug", "Release" })
            {
                var candidate = Path.Combine(
                    root,
                    "junexis-mentor",
                    "JunexisLang.Mentor.CLI",
                    "bin",
                    configuration,
                    "net10.0",
                    "JunexisLang.Mentor.CLI.dll");
                if (File.Exists(candidate))
                    return candidate;
            }
        }
        return null;

        static IEnumerable<string> CandidateRoots()
        {
            var current = new DirectoryInfo(Environment.CurrentDirectory);
            for (var depth = 0; current is not null && depth < 4; depth++, current = current.Parent)
                yield return current.FullName;
        }
    }

    private static IEnumerable<string> EnumerateCommandCandidates(string executableName, string dllName)
    {
        foreach (var directory in CandidateCommandDirectories())
        {
            if (!Directory.Exists(directory))
                continue;

            var fromPath = Directory.EnumerateFiles(directory, executableName + "*", SearchOption.TopDirectoryOnly)
                .Concat(Directory.EnumerateFiles(directory, dllName, SearchOption.TopDirectoryOnly))
                .Select(file => file.Trim())
                .Where(file => !string.IsNullOrWhiteSpace(file))
                .Distinct(StringComparer.OrdinalIgnoreCase);

            foreach (var candidate in fromPath)
                yield return candidate;
        }
    }

    private static IEnumerable<string> CandidateCommandDirectories()
    {
        foreach (var value in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator))
        {
            if (!string.IsNullOrWhiteSpace(value))
                yield return value;
        }

        foreach (var root in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "Local", "Programs"),
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".junexis", "bin")
                 })
        {
            if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
                yield return root;
        }
    }
}

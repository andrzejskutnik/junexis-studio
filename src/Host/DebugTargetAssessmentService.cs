using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Host;

/// <summary>Correlates local processes with the latest versioned Junexis artifact inventory.</summary>
public sealed class DebugTargetAssessmentService : IDebugTargetAssessmentService
{
    private readonly IArtifactService _artifacts;
    private readonly IOutput _output;
    private readonly object _gate = new();
    private ArtifactInventorySnapshot? _latestInventory;

    /// <summary>Creates an assessment service and observes subsequent Junexis build inventories.</summary>
    /// <param name="eventBus">Event bus carrying artifact publication events.</param>
    /// <param name="artifacts">Versioned artifact inventory reader.</param>
    /// <param name="output">Studio output used for isolated inventory failures.</param>
    public DebugTargetAssessmentService(IEventBus eventBus, IArtifactService artifacts, IOutput output)
    {
        ArgumentNullException.ThrowIfNull(eventBus);
        _artifacts = artifacts ?? throw new ArgumentNullException(nameof(artifacts));
        _output = output ?? throw new ArgumentNullException(nameof(output));
        eventBus.Subscribe<ArtifactsUpdatedEvent>(OnArtifactsUpdated);
    }

    internal DebugTargetAssessmentService(ArtifactInventorySnapshot? inventory)
    {
        _artifacts = null!;
        _output = null!;
        _latestInventory = inventory;
    }

    /// <inheritdoc />
    public DebugTargetAssessment Assess(DebugProcessInfo process, string? requestedProfileId = null)
    {
        ArgumentNullException.ThrowIfNull(process);
        ArtifactInventorySnapshot? inventory;
        lock (_gate)
            inventory = _latestInventory;

        var evidence = new List<string>();
        var profile = InferFromInventory(process, inventory, evidence, out var sourceReady);
        var confidence = profile is null ? DebugTargetConfidence.Unknown : DebugTargetConfidence.BuildArtifact;
        if (profile is null)
        {
            profile = InferRuntimeProfile(process);
            if (profile is not null)
            {
                confidence = DebugTargetConfidence.Heuristic;
                evidence.Add($"Well-known runtime '{process.Name}' suggests profile '{profile}'.");
            }
        }

        bool? matches = profile is null || string.IsNullOrWhiteSpace(requestedProfileId)
            ? null
            : NormalizeProfile(profile).Equals(NormalizeProfile(requestedProfileId), StringComparison.Ordinal);
        if (matches == false)
            evidence.Add($"Selected profile '{requestedProfileId}' conflicts with recommended profile '{profile}'.");

        var buildId = confidence == DebugTargetConfidence.BuildArtifact ? inventory?.BuildId : null;
        var summary = BuildSummary(profile, confidence, buildId, sourceReady, matches);
        return new DebugTargetAssessment(process, profile, confidence, matches, buildId, sourceReady, summary, evidence);
    }

    private void OnArtifactsUpdated(ArtifactsUpdatedEvent update) => _ = LoadInventoryAsync(update);

    private async Task LoadInventoryAsync(ArtifactsUpdatedEvent update)
    {
        try
        {
            var inventory = await _artifacts.LoadAsync(update.ArtifactManifestPath).ConfigureAwait(false);
            if (!inventory.BuildId.Equals(update.BuildId, StringComparison.Ordinal))
                throw new InvalidDataException("Debug target inventory buildId does not match the build event.");
            lock (_gate)
                _latestInventory = inventory;
        }
        catch (Exception ex)
        {
            _output.Write("Debug", $"Cannot correlate attach targets with build '{update.BuildId}': {ex.Message}", OutputSeverity.Warning);
        }
    }

    private static string? InferFromInventory(
        DebugProcessInfo process,
        ArtifactInventorySnapshot? inventory,
        List<string> evidence,
        out bool sourceReady)
    {
        sourceReady = false;
        if (inventory is null || string.IsNullOrWhiteSpace(process.ExecutablePath))
            return null;
        var executable = Path.GetFullPath(process.ExecutablePath);
        var outputRoot = Path.GetDirectoryName(inventory.ManifestPath)!;
        if (!IsWithin(executable, outputRoot))
            return null;

        var relative = Path.GetRelativePath(outputRoot, executable).Replace('\\', '/');
        var target = inventory.Items.Select(item => item.Target)
            .Where(item => item is "embedded" or "server" or "report")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(item => relative.StartsWith(item + "/", StringComparison.OrdinalIgnoreCase));
        if (target is null)
            return null;

        sourceReady = inventory.Items.Any(item =>
            item.Kind.Equals("debug-source-map", StringComparison.OrdinalIgnoreCase)
            || item.RelativePath.Equals("debug/junexis.source-map.json", StringComparison.OrdinalIgnoreCase));
        evidence.Add($"Executable is inside target '{target}' of Junexis build '{inventory.BuildId}'.");
        evidence.Add(sourceReady
            ? "Versioned source map is available for runtime → Junexis correlation."
            : "No versioned source map is present; local attach cannot explain Junexis causality.");
        evidence.Add("WHY analysis still requires captured Narrative events; attaching a process alone is only a local observation.");
        return target switch
        {
            "embedded" => "cpp",
            "server" => "go",
            "report" => "typescript",
            _ => null,
        };
    }

    private static string? InferRuntimeProfile(DebugProcessInfo process)
    {
        var name = Path.GetFileNameWithoutExtension(process.Name).ToLowerInvariant();
        return name is "node" or "nodejs" or "deno" or "bun" ? "typescript" : null;
    }

    private static string BuildSummary(
        string? profile,
        DebugTargetConfidence confidence,
        string? buildId,
        bool sourceReady,
        bool? matches)
    {
        if (profile is null)
            return "No profile recommendation: choose deliberately. Attach is local; Narrative WHY needs trace evidence.";
        var origin = confidence == DebugTargetConfidence.BuildArtifact
            ? $"Junexis build {ShortId(buildId)}"
            : "runtime heuristic";
        var compatibility = matches == false ? " Selected profile conflicts with this evidence." : string.Empty;
        var correlation = sourceReady
            ? " Source correlation is ready; WHY becomes available when Narrative events are captured."
            : " Narrative source correlation is not proven.";
        return $"Recommended: {profile} ({origin}).{compatibility}{correlation}";
    }

    private static string NormalizeProfile(string profile) => profile.Trim().ToLowerInvariant() switch
    {
        "node" or "ts" => "typescript",
        "c++" or "gpp" => "cpp",
        _ => profile.Trim().ToLowerInvariant(),
    };

    private static bool IsWithin(string path, string root)
    {
        var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static string ShortId(string? buildId) => string.IsNullOrWhiteSpace(buildId)
        ? "unknown"
        : buildId[..Math.Min(12, buildId.Length)];
}

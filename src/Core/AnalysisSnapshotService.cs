using System.Text.Json;
using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Core;

/// <summary>JSON reader and structural validator for Junexis analysis snapshot 1.x.</summary>
public sealed class AnalysisSnapshotService : IAnalysisSnapshotService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <inheritdoc />
    public async Task<AnalysisSnapshot> LoadAsync(
        string snapshotPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotPath);
        var fullPath = Path.GetFullPath(snapshotPath);
        await using var stream = File.OpenRead(fullPath);
        var dto = await JsonSerializer.DeserializeAsync<SnapshotDto>(
            stream, JsonOptions, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("Analysis snapshot is empty.");

        if (!dto.SchemaVersion.StartsWith("1.", StringComparison.Ordinal))
            throw new InvalidDataException($"Unsupported analysis snapshot schema '{dto.SchemaVersion}'.");
        if (string.IsNullOrWhiteSpace(dto.BuildId))
            throw new InvalidDataException("Analysis snapshot has no buildId.");

        var symbols = dto.Symbols.Select(symbol => new AnalysisSymbol(
            Required(symbol.Id, "symbol id"),
            Required(symbol.Kind, "symbol kind"),
            Required(symbol.Name, "symbol name"),
            Map(symbol.Source),
            symbol.Platforms,
            symbol.DataType)).ToArray();
        var symbolIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var symbol in symbols)
            if (!symbolIds.Add(symbol.Id))
                throw new InvalidDataException($"Duplicate analysis symbol id '{symbol.Id}'.");

        var relations = dto.Relations.Select(relation => new AnalysisRelation(
            Required(relation.Id, "relation id"),
            Required(relation.Kind, "relation kind"),
            Required(relation.CommunicationKind, "communication kind"),
            Required(relation.PipelineSymbolId, "pipeline symbol id"),
            Required(relation.SourceSymbolId, "source symbol id"),
            relation.SourcePort,
            Required(relation.TargetSymbolId, "target symbol id"),
            relation.TargetPort,
            Map(relation.Source))).ToArray();
        var relationIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var relation in relations)
        {
            if (!relationIds.Add(relation.Id))
                throw new InvalidDataException($"Duplicate analysis relation id '{relation.Id}'.");
            foreach (var referencedId in new[] { relation.PipelineSymbolId, relation.SourceSymbolId, relation.TargetSymbolId })
                if (!symbolIds.Contains(referencedId))
                    throw new InvalidDataException($"Relation '{relation.Id}' references unknown symbol '{referencedId}'.");
        }

        return new AnalysisSnapshot(
            dto.BuildId,
            Required(dto.SourceFile, "source file"),
            fullPath,
            symbols,
            relations,
            new AnalysisTopology(
                dto.Topology.HasCycles,
                dto.Topology.MaxDepth,
                dto.Topology.StronglyConnectedComponents,
                dto.Topology.FanIn,
                dto.Topology.FanOut,
                dto.Topology.Depth));
    }

    private static string Required(string value, string label) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new InvalidDataException($"Analysis snapshot has an empty {label}.")
            : value;

    private static AnalysisSourceLocation? Map(SourceDto? source) =>
        source is null ? null : new AnalysisSourceLocation(source.File, source.Line, source.Column);

    private sealed record SnapshotDto
    {
        public string SchemaVersion { get; init; } = string.Empty;
        public string BuildId { get; init; } = string.Empty;
        public string SourceFile { get; init; } = string.Empty;
        public List<SymbolDto> Symbols { get; init; } = [];
        public List<RelationDto> Relations { get; init; } = [];
        public TopologyDto Topology { get; init; } = new();
    }

    private sealed record SymbolDto
    {
        public string Id { get; init; } = string.Empty;
        public string Kind { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public SourceDto? Source { get; init; }
        public List<string> Platforms { get; init; } = [];
        public string? DataType { get; init; }
    }

    private sealed record RelationDto
    {
        public string Id { get; init; } = string.Empty;
        public string Kind { get; init; } = string.Empty;
        public string CommunicationKind { get; init; } = string.Empty;
        public string PipelineSymbolId { get; init; } = string.Empty;
        public string SourceSymbolId { get; init; } = string.Empty;
        public string? SourcePort { get; init; }
        public string TargetSymbolId { get; init; } = string.Empty;
        public string? TargetPort { get; init; }
        public SourceDto? Source { get; init; }
    }

    private sealed record SourceDto
    {
        public string File { get; init; } = string.Empty;
        public int Line { get; init; }
        public int Column { get; init; }
    }

    private sealed record TopologyDto
    {
        public bool HasCycles { get; init; }
        public int MaxDepth { get; init; }
        public List<IReadOnlyList<string>> StronglyConnectedComponents { get; init; } = [];
        public Dictionary<string, int> FanIn { get; init; } = new(StringComparer.Ordinal);
        public Dictionary<string, int> FanOut { get; init; } = new(StringComparer.Ordinal);
        public Dictionary<string, int> Depth { get; init; } = new(StringComparer.Ordinal);
    }
}

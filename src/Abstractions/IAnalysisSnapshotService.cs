namespace Junexis.Studio.Abstractions;

/// <summary>A source location published by the Junexis compiler analysis contract.</summary>
/// <param name="File">Absolute or workspace-resolvable source file path.</param>
/// <param name="Line">One-based source line.</param>
/// <param name="Column">One-based source column.</param>
public sealed record AnalysisSourceLocation(string File, int Line, int Column);

/// <summary>A language-level symbol available to Studio and Mentor.</summary>
/// <param name="Id">Stable semantic symbol identifier.</param>
/// <param name="Kind">Junexis declaration kind.</param>
/// <param name="Name">Source-level declaration name.</param>
/// <param name="Source">Optional source declaration location.</param>
/// <param name="Platforms">Code generation platforms associated with the symbol.</param>
/// <param name="DataType">Optional canonical data type.</param>
public sealed record AnalysisSymbol(
    string Id,
    string Kind,
    string Name,
    AnalysisSourceLocation? Source,
    IReadOnlyList<string> Platforms,
    string? DataType);

/// <summary>A directed relation between stable Junexis symbols.</summary>
/// <param name="Id">Stable semantic relation identifier.</param>
/// <param name="Kind">Relation category.</param>
/// <param name="CommunicationKind">Junexis communication type.</param>
/// <param name="PipelineSymbolId">Stable identifier of the containing pipeline.</param>
/// <param name="SourceSymbolId">Stable identifier of the source symbol.</param>
/// <param name="SourcePort">Optional source port.</param>
/// <param name="TargetSymbolId">Stable identifier of the target symbol.</param>
/// <param name="TargetPort">Optional target port.</param>
/// <param name="Source">Optional source location of the relation.</param>
public sealed record AnalysisRelation(
    string Id,
    string Kind,
    string CommunicationKind,
    string PipelineSymbolId,
    string SourceSymbolId,
    string? SourcePort,
    string TargetSymbolId,
    string? TargetPort,
    AnalysisSourceLocation? Source);

/// <summary>Topology metrics keyed by stable task symbol identifiers.</summary>
/// <param name="HasCycles">Whether the logical graph contains a cycle.</param>
/// <param name="MaxDepth">Maximum depth in the SCC condensation graph.</param>
/// <param name="StronglyConnectedComponents">Strongly connected components containing stable task IDs.</param>
/// <param name="FanIn">Fan-in values keyed by stable task ID.</param>
/// <param name="FanOut">Fan-out values keyed by stable task ID.</param>
/// <param name="Depth">Graph depth keyed by stable task ID.</param>
public sealed record AnalysisTopology(
    bool HasCycles,
    int MaxDepth,
    IReadOnlyList<IReadOnlyList<string>> StronglyConnectedComponents,
    IReadOnlyDictionary<string, int> FanIn,
    IReadOnlyDictionary<string, int> FanOut,
    IReadOnlyDictionary<string, int> Depth);

/// <summary>A versioned compiler analysis snapshot that does not expose compiler IR types.</summary>
/// <param name="BuildId">Build identifier shared with artifact and debug manifests.</param>
/// <param name="SourceFile">Main Junexis source file.</param>
/// <param name="SnapshotPath">Loaded snapshot path.</param>
/// <param name="Symbols">Language-level symbols.</param>
/// <param name="Relations">Relations between symbols.</param>
/// <param name="Topology">Logical topology metrics.</param>
public sealed record AnalysisSnapshot(
    string BuildId,
    string SourceFile,
    string SnapshotPath,
    IReadOnlyList<AnalysisSymbol> Symbols,
    IReadOnlyList<AnalysisRelation> Relations,
    AnalysisTopology Topology);

/// <summary>Reads and validates the stable Junexis analysis contract.</summary>
public interface IAnalysisSnapshotService
{
    /// <summary>Loads an analysis snapshot produced by the Junexis compiler.</summary>
    Task<AnalysisSnapshot> LoadAsync(
        string snapshotPath,
        CancellationToken cancellationToken = default);
}

/// <summary>Signals that a compiler published a new analysis snapshot.</summary>
/// <param name="CompilerId">Compiler provider identifier.</param>
/// <param name="BuildId">Build identifier.</param>
/// <param name="SnapshotPath">Path to the published snapshot.</param>
public sealed record AnalysisSnapshotUpdatedEvent(
    string CompilerId,
    string BuildId,
    string SnapshotPath);

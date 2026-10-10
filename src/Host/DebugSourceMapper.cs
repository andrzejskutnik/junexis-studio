using Junexis.Studio.Abstractions;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Junexis.Studio.Host;

/// <summary>Maps generated runtime frames through the latest compiler analysis snapshot.</summary>
public sealed class DebugSourceMapper : IDebugSourceMapper
{
    private readonly IAnalysisSnapshotService _analysis;
    private readonly IOutput _output;
    private AnalysisSnapshot? _snapshot;
    private IReadOnlyList<RuntimeSourceMapping> _mappings = [];

    /// <summary>Creates a mapper that follows analysis snapshots published after Junexis builds.</summary>
    /// <param name="analysis">Analysis snapshot reader.</param>
    /// <param name="eventBus">Studio event bus.</param>
    /// <param name="output">Studio output log.</param>
    public DebugSourceMapper(IAnalysisSnapshotService analysis, IEventBus eventBus, IOutput output)
    {
        _analysis = analysis ?? throw new ArgumentNullException(nameof(analysis));
        ArgumentNullException.ThrowIfNull(eventBus);
        _output = output ?? throw new ArgumentNullException(nameof(output));
        eventBus.Subscribe<AnalysisSnapshotUpdatedEvent>(OnSnapshotUpdated);
    }

    /// <inheritdoc />
    public DebugStackFrame MapFrame(DebugStackFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var mappings = Volatile.Read(ref _mappings);
        if (TryFullPath(frame.SourcePath, out var runtimePath))
        {
            var exact = mappings.Where(mapping =>
                    mapping.GeneratedPath.Equals(runtimePath, StringComparison.OrdinalIgnoreCase)
                    && frame.Line >= mapping.StartLine
                    && frame.Line <= mapping.EndLine)
                .OrderBy(mapping => mapping.EndLine - mapping.StartLine)
                .ThenByDescending(mapping => mapping.Kind == "statement")
                .FirstOrDefault();
            if (exact is not null)
            {
                return frame with
                {
                    RuntimeSourcePath = frame.SourcePath,
                    RuntimeLine = frame.Line,
                    RuntimeColumn = frame.Column,
                    SourcePath = exact.SourcePath,
                    Line = exact.SourceLine,
                    Column = exact.SourceColumn,
                    JunexisSymbolId = exact.SymbolId,
                };
            }
        }
        var snapshot = Volatile.Read(ref _snapshot);
        if (snapshot is null || string.IsNullOrWhiteSpace(frame.Name))
            return frame;
        if (!string.IsNullOrWhiteSpace(frame.SourcePath)
            && Path.GetExtension(frame.SourcePath).Equals(".jnx", StringComparison.OrdinalIgnoreCase))
            return frame;

        var symbol = snapshot.Symbols
            .Where(item => item.Source is not null && FrameMatchesSymbol(frame.Name, item.Name))
            .OrderByDescending(item => item.Name.Length)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .FirstOrDefault();
        if (symbol?.Source is null)
            return frame;
        return frame with
        {
            RuntimeSourcePath = frame.SourcePath,
            RuntimeLine = frame.Line,
            RuntimeColumn = frame.Column,
            SourcePath = symbol.Source.File,
            Line = symbol.Source.Line,
            Column = symbol.Source.Column,
            JunexisSymbolId = symbol.Id,
        };
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, IReadOnlyList<DebugSourceBreakpoint>> MapBreakpoints(
        string? target,
        string sourcePath,
        IReadOnlyList<DebugSourceBreakpoint> breakpoints)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(breakpoints);
        if (string.IsNullOrWhiteSpace(target))
            return Original(sourcePath, breakpoints);
        var normalizedSource = Path.GetFullPath(sourcePath);
        var candidates = Volatile.Read(ref _mappings)
            .Where(mapping => mapping.Target.Equals(target, StringComparison.OrdinalIgnoreCase)
                && mapping.SourcePath.Equals(normalizedSource, StringComparison.OrdinalIgnoreCase))
            .OrderBy(mapping => mapping.SourceLine)
            .ToArray();
        if (candidates.Length == 0)
            return Original(sourcePath, breakpoints);

        var result = candidates.Select(mapping => mapping.GeneratedPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(path => path, _ => (IReadOnlyList<DebugSourceBreakpoint>)[], StringComparer.OrdinalIgnoreCase);
        foreach (var group in breakpoints.SelectMany(breakpoint =>
                     SelectBreakpointMappings(candidates, breakpoint.Line).Select(mapping => new
                     {
                         Breakpoint = breakpoint,
                         Mapping = mapping,
                     }))
                 .GroupBy(item => item.Mapping.GeneratedPath, StringComparer.OrdinalIgnoreCase))
        {
            result[group.Key] = group.Select(item => new DebugSourceBreakpoint(
                item.Mapping.GeneratedPath,
                item.Mapping.StartLine,
                item.Mapping.StartColumn,
                item.Breakpoint.Condition,
                item.Breakpoint.HitCondition,
                item.Breakpoint.LogMessage)).Distinct().ToArray();
        }
        return result;
    }

    private static IReadOnlyList<RuntimeSourceMapping> SelectBreakpointMappings(
        IReadOnlyList<RuntimeSourceMapping> candidates,
        int sourceLine)
    {
        var exactStatements = candidates.Where(mapping =>
            mapping.Kind == "statement" && mapping.SourceLine == sourceLine).ToArray();
        if (exactStatements.Length > 0)
            return exactStatements;
        var precedingLine = candidates.Where(mapping =>
                mapping.Kind == "statement" && mapping.SourceLine <= sourceLine)
            .Select(mapping => (int?)mapping.SourceLine)
            .Max();
        if (precedingLine is not null)
            return candidates.Where(mapping =>
                mapping.Kind == "statement" && mapping.SourceLine == precedingLine).ToArray();
        return
        [
            candidates.LastOrDefault(mapping => mapping.SourceLine <= sourceLine) ?? candidates[0],
        ];
    }

    internal void SetSnapshot(AnalysisSnapshot snapshot) => Volatile.Write(ref _snapshot, snapshot);

    internal void SetMappings(IReadOnlyList<RuntimeSourceMapping> mappings) =>
        Volatile.Write(ref _mappings, mappings);

    private void OnSnapshotUpdated(AnalysisSnapshotUpdatedEvent update) => _ = LoadAsync(update.SnapshotPath);

    private async Task LoadAsync(string path)
    {
        try
        {
            var snapshot = await _analysis.LoadAsync(path).ConfigureAwait(false);
            Volatile.Write(ref _snapshot, snapshot);
            await LoadSourceMapAsync(path).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _output.Write("Debug", $"Cannot load Junexis debug source map: {ex.Message}", OutputSeverity.Warning);
        }
    }

    private async Task LoadSourceMapAsync(string snapshotPath)
    {
        var analysisDirectory = Path.GetDirectoryName(Path.GetFullPath(snapshotPath))!;
        var outputRoot = Directory.GetParent(analysisDirectory)?.FullName
            ?? throw new InvalidDataException("Analysis snapshot has no output root.");
        var sourceMapPath = Path.Combine(outputRoot, "debug", "junexis.source-map.json");
        if (!File.Exists(sourceMapPath))
        {
            Volatile.Write(ref _mappings, []);
            return;
        }
        await using var stream = File.OpenRead(sourceMapPath);
        var contract = await JsonSerializer.DeserializeAsync<SourceMapContract>(stream).ConfigureAwait(false)
            ?? throw new InvalidDataException("Debug source map is empty.");
        if (contract.SchemaVersion != "1.0" || contract.Kind != "junexis.debug-source-map")
            throw new InvalidDataException($"Unsupported debug source-map contract '{contract.SchemaVersion}'.");
        var rootPrefix = Path.GetFullPath(outputRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var mappings = contract.Mappings.Select(mapping =>
        {
            var generatedPath = Path.GetFullPath(Path.Combine(outputRoot, mapping.Generated.File));
            if (!generatedPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Generated source path escapes output root: {mapping.Generated.File}");
            return new RuntimeSourceMapping(
                mapping.SymbolId,
                Path.GetFullPath(mapping.Source.File),
                mapping.Source.Line,
                mapping.Source.Column,
                mapping.Generated.Target,
                generatedPath,
                mapping.Generated.StartLine,
                mapping.Generated.EndLine,
                mapping.Generated.StartColumn,
                mapping.Kind ?? "task",
                mapping.IrPath);
        }).ToArray();
        Volatile.Write(ref _mappings, mappings);
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<DebugSourceBreakpoint>> Original(
        string sourcePath,
        IReadOnlyList<DebugSourceBreakpoint> breakpoints) =>
        new Dictionary<string, IReadOnlyList<DebugSourceBreakpoint>>(StringComparer.OrdinalIgnoreCase)
        {
            [sourcePath] = breakpoints,
        };

    private static bool TryFullPath(string? path, out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
            return false;
        try
        {
            fullPath = Path.GetFullPath(path);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static bool FrameMatchesSymbol(string frameName, string symbolName)
    {
        if (frameName.Equals(symbolName, StringComparison.OrdinalIgnoreCase))
            return true;
        var separators = new[] { '.', ':', '/', '\\', '<', '>', '(', ')' };
        return frameName.Split(separators, StringSplitOptions.RemoveEmptyEntries)
            .Any(part => part.Equals(symbolName, StringComparison.OrdinalIgnoreCase));
    }

    internal sealed record RuntimeSourceMapping(
        string SymbolId,
        string SourcePath,
        int SourceLine,
        int SourceColumn,
        string Target,
        string GeneratedPath,
        int StartLine,
        int EndLine,
        int StartColumn,
        string Kind = "task",
        string? IrPath = null);

    private sealed record SourceMapContract(
        [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
        [property: JsonPropertyName("kind")] string? Kind,
        [property: JsonPropertyName("mappings")] IReadOnlyList<SourceMapEntry> Mappings);

    private sealed record SourceMapEntry(
        [property: JsonPropertyName("symbolId")] string SymbolId,
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("irPath")] string? IrPath,
        [property: JsonPropertyName("source")] SourcePosition Source,
        [property: JsonPropertyName("generated")] GeneratedRange Generated);

    private sealed record SourcePosition(
        [property: JsonPropertyName("file")] string File,
        [property: JsonPropertyName("line")] int Line,
        [property: JsonPropertyName("column")] int Column);

    private sealed record GeneratedRange(
        [property: JsonPropertyName("target")] string Target,
        [property: JsonPropertyName("file")] string File,
        [property: JsonPropertyName("startLine")] int StartLine,
        [property: JsonPropertyName("endLine")] int EndLine,
        [property: JsonPropertyName("startColumn")] int StartColumn);
}

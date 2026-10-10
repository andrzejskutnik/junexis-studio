namespace Junexis.Studio.Abstractions;

/// <summary>Maps runtime stack frames to stable Junexis symbols and source locations.</summary>
public interface IDebugSourceMapper
{
    /// <summary>Maps a runtime frame when the current analysis snapshot contains a matching symbol.</summary>
    /// <param name="frame">Runtime frame returned by a debug adapter.</param>
    /// <returns>The original frame or a frame enriched with Junexis source information.</returns>
    DebugStackFrame MapFrame(DebugStackFrame frame);

    /// <summary>Maps Junexis breakpoints to generated source files for one code-generation target.</summary>
    /// <param name="target">Compiler target such as embedded, server, or report.</param>
    /// <param name="sourcePath">Junexis source path whose adapter breakpoints are being replaced.</param>
    /// <param name="breakpoints">Current Junexis breakpoints for the source.</param>
    /// <returns>Generated source paths and replacement breakpoint sets.</returns>
    IReadOnlyDictionary<string, IReadOnlyList<DebugSourceBreakpoint>> MapBreakpoints(
        string? target,
        string sourcePath,
        IReadOnlyList<DebugSourceBreakpoint> breakpoints);
}

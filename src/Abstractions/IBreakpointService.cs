namespace Junexis.Studio.Abstractions;

/// <summary>Stores source breakpoints shared by the editor and debugger.</summary>
public interface IBreakpointService
{
    /// <summary>Returns all source breakpoints in deterministic path and line order.</summary>
    /// <returns>Current breakpoints.</returns>
    IReadOnlyList<DebugSourceBreakpoint> GetBreakpoints();

    /// <summary>Adds a breakpoint or removes an existing breakpoint at the same location.</summary>
    /// <param name="path">Absolute source path.</param>
    /// <param name="line">One-based source line.</param>
    /// <param name="column">Optional one-based source column.</param>
    /// <returns>True when a breakpoint was added; false when it was removed.</returns>
    bool Toggle(string path, int line, int? column = null);

    /// <summary>Adds or replaces the breakpoint at its source location.</summary>
    /// <param name="breakpoint">Breakpoint definition including optional stop or log behavior.</param>
    void Set(DebugSourceBreakpoint breakpoint);

    /// <summary>Removes all breakpoints.</summary>
    void Clear();

    /// <summary>Raised after the breakpoint collection changes.</summary>
    event Action? BreakpointsChanged;
}

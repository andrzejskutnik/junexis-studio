namespace Junexis.Studio.Abstractions;

/// <summary>Source location currently selected by a stopped debug session.</summary>
/// <param name="Path">Absolute source path.</param>
/// <param name="Line">One-based source line.</param>
/// <param name="Column">One-based source column.</param>
public sealed record DebugExecutionLocation(string Path, int Line, int Column);

/// <summary>Projects the active stopped stack frame into an editor execution marker.</summary>
public interface IDebugExecutionLocationService
{
    /// <summary>Gets the current execution location, or null while the debuggee is not stopped.</summary>
    DebugExecutionLocation? Current { get; }

    /// <summary>Raised when the editor execution marker should be refreshed.</summary>
    event Action? LocationChanged;
}

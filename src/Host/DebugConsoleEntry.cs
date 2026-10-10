namespace Junexis.Studio.Host;

/// <summary>One expression and result retained by the interactive Debug Console.</summary>
/// <param name="Expression">Submitted expression or debugger command.</param>
/// <param name="Result">Formatted adapter result or error message.</param>
/// <param name="Type">Optional result type.</param>
/// <param name="IsError">Whether evaluation failed.</param>
public sealed record DebugConsoleEntry(string Expression, string Result, string? Type, bool IsError);

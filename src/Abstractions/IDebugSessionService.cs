namespace Junexis.Studio.Abstractions;

/// <summary>Lifecycle state of a Debug Adapter Protocol session.</summary>
public enum DebugSessionState
{
    /// <summary>No adapter is connected.</summary>
    Inactive,
    /// <summary>The adapter is starting and negotiating capabilities.</summary>
    Starting,
    /// <summary>The debuggee is running.</summary>
    Running,
    /// <summary>The debuggee is paused.</summary>
    Stopped,
    /// <summary>The session has terminated.</summary>
    Terminated,
    /// <summary>The session failed.</summary>
    Failed,
}

/// <summary>Source breakpoint sent to a debug adapter.</summary>
/// <param name="Path">Absolute source path.</param>
/// <param name="Line">One-based source line.</param>
/// <param name="Column">Optional one-based source column.</param>
/// <param name="Condition">Optional language expression that must evaluate to true.</param>
/// <param name="HitCondition">Optional adapter-specific hit-count expression.</param>
/// <param name="LogMessage">Optional message emitted instead of stopping execution.</param>
public sealed record DebugSourceBreakpoint(
    string Path,
    int Line,
    int? Column = null,
    string? Condition = null,
    string? HitCondition = null,
    string? LogMessage = null);

/// <summary>Exception breakpoint filter advertised by a debug adapter.</summary>
/// <param name="Filter">Adapter-defined filter identifier.</param>
/// <param name="Label">Human-readable filter label.</param>
/// <param name="Default">Whether the adapter recommends enabling the filter by default.</param>
public sealed record DebugExceptionBreakpointFilter(string Filter, string Label, bool Default);

/// <summary>Adapter verification result for one generated breakpoint location.</summary>
/// <param name="SourcePath">Junexis or original source path represented by the breakpoint.</param>
/// <param name="SourceLine">One-based Junexis or original source line.</param>
/// <param name="SourceColumn">Optional source column.</param>
/// <param name="RuntimePath">Source path sent to the debug adapter.</param>
/// <param name="RequestedLine">One-based runtime line requested from the adapter.</param>
/// <param name="RequestedColumn">Optional runtime column requested from the adapter.</param>
/// <param name="Verified">Whether the adapter verified the breakpoint.</param>
/// <param name="ActualLine">Optional runtime line selected by the adapter.</param>
/// <param name="ActualColumn">Optional runtime column selected by the adapter.</param>
/// <param name="Message">Optional adapter diagnostic.</param>
/// <param name="AdapterId">Optional adapter breakpoint identifier.</param>
public sealed record DebugBreakpointStatus(
    string SourcePath,
    int SourceLine,
    int? SourceColumn,
    string RuntimePath,
    int RequestedLine,
    int? RequestedColumn,
    bool Verified,
    int? ActualLine,
    int? ActualColumn,
    string? Message,
    int? AdapterId);

/// <summary>Configuration used to start a DAP adapter and launch a debuggee.</summary>
/// <param name="AdapterFileName">DAP adapter executable.</param>
/// <param name="AdapterArguments">Adapter process arguments.</param>
/// <param name="WorkingDirectory">Adapter and debuggee working directory.</param>
/// <param name="LaunchArguments">DAP launch-request arguments.</param>
/// <param name="Breakpoints">Breakpoints installed before configurationDone.</param>
/// <param name="Transport">Adapter communication transport.</param>
/// <param name="SourceMapTarget">Optional compiler target used to map Junexis breakpoints.</param>
/// <param name="ExceptionBreakpointFilters">Adapter-defined exception filters enabled at launch.</param>
public sealed record DebugSessionConfiguration(
    string AdapterFileName,
    string AdapterArguments,
    string WorkingDirectory,
    IReadOnlyDictionary<string, object?> LaunchArguments,
    IReadOnlyList<DebugSourceBreakpoint> Breakpoints,
    DebugAdapterTransport Transport = DebugAdapterTransport.Stdio,
    string? SourceMapTarget = null,
    IReadOnlyList<string>? ExceptionBreakpointFilters = null);

/// <summary>One thread reported by a debug adapter.</summary>
/// <param name="Id">Adapter thread identifier.</param>
/// <param name="Name">Thread display name.</param>
public sealed record DebugThread(int Id, string Name);

/// <summary>One call-stack frame reported by a debug adapter.</summary>
/// <param name="Id">Adapter frame identifier.</param>
/// <param name="Name">Frame display name.</param>
/// <param name="SourcePath">Optional source path.</param>
/// <param name="Line">One-based source line.</param>
/// <param name="Column">One-based source column.</param>
/// <param name="RuntimeSourcePath">Original generated/runtime source path when remapped.</param>
/// <param name="JunexisSymbolId">Stable Junexis symbol identifier when resolved.</param>
/// <param name="RuntimeLine">Original generated/runtime line when remapped.</param>
/// <param name="RuntimeColumn">Original generated/runtime column when remapped.</param>
public sealed record DebugStackFrame(
    int Id,
    string Name,
    string? SourcePath,
    int Line,
    int Column,
    string? RuntimeSourcePath = null,
    string? JunexisSymbolId = null,
    int? RuntimeLine = null,
    int? RuntimeColumn = null);

/// <summary>Variable scope associated with a stack frame.</summary>
/// <param name="Name">Scope display name.</param>
/// <param name="VariablesReference">Adapter reference used to load variables.</param>
/// <param name="IsExpensive">Whether loading the scope may be expensive.</param>
public sealed record DebugScope(string Name, int VariablesReference, bool IsExpensive);

/// <summary>Variable reported by a debug adapter.</summary>
/// <param name="Name">Variable name.</param>
/// <param name="Value">Formatted value.</param>
/// <param name="Type">Optional type name.</param>
/// <param name="VariablesReference">Reference for nested variables, or zero.</param>
public sealed record DebugVariable(string Name, string Value, string? Type, int VariablesReference);

/// <summary>Result of evaluating an expression in a stopped stack frame.</summary>
/// <param name="Expression">Expression submitted to the adapter.</param>
/// <param name="Result">Formatted evaluation result.</param>
/// <param name="Type">Optional result type.</param>
/// <param name="VariablesReference">Reference for expandable children, or zero.</param>
public sealed record DebugEvaluation(
    string Expression,
    string Result,
    string? Type,
    int VariablesReference);

/// <summary>Adapter metadata used to create a data breakpoint for a variable.</summary>
/// <param name="DataId">Opaque adapter data identifier, or null when unavailable.</param>
/// <param name="Description">Human-readable data location description.</param>
/// <param name="AccessTypes">Supported access types such as read, write, or readWrite.</param>
/// <param name="CanPersist">Whether the identifier can be reused across sessions.</param>
public sealed record DebugDataBreakpointInfo(
    string? DataId,
    string Description,
    IReadOnlyList<string> AccessTypes,
    bool CanPersist);

/// <summary>Configured DAP data breakpoint.</summary>
/// <param name="DataId">Opaque identifier returned by dataBreakpointInfo.</param>
/// <param name="Description">Display description.</param>
/// <param name="AccessType">Optional read, write, or readWrite access type.</param>
/// <param name="Condition">Optional activation condition.</param>
/// <param name="HitCondition">Optional hit-count condition.</param>
public sealed record DebugDataBreakpoint(
    string DataId,
    string Description,
    string? AccessType = null,
    string? Condition = null,
    string? HitCondition = null);

/// <summary>Current observable debugger state.</summary>
/// <param name="State">Session lifecycle state.</param>
/// <param name="Status">Human-readable state description.</param>
/// <param name="ActiveThreadId">Paused thread identifier, when known.</param>
/// <param name="Threads">Known debuggee threads.</param>
/// <param name="StackFrames">Frames for the active thread.</param>
public sealed record DebugSessionSnapshot(
    DebugSessionState State,
    string Status,
    int? ActiveThreadId,
    IReadOnlyList<DebugThread> Threads,
    IReadOnlyList<DebugStackFrame> StackFrames);

/// <summary>Output emitted by a debug adapter or debuggee.</summary>
/// <param name="Category">DAP output category.</param>
/// <param name="Text">Output text.</param>
public sealed record DebugOutputEvent(string Category, string Text);

/// <summary>Controls one Debug Adapter Protocol session.</summary>
public interface IDebugSessionService : IAsyncDisposable
{
    /// <summary>Gets the latest session snapshot.</summary>
    DebugSessionSnapshot Snapshot { get; }

    /// <summary>Gets the latest adapter verification results for breakpoints.</summary>
    IReadOnlyList<DebugBreakpointStatus> BreakpointStatuses { get; }

    /// <summary>Gets exception breakpoint filters advertised by the active adapter.</summary>
    IReadOnlyList<DebugExceptionBreakpointFilter> ExceptionBreakpointFilters => [];

    /// <summary>Gets whether the active adapter supports changing variable values.</summary>
    bool SupportsSetVariable => false;

    /// <summary>Gets whether the active adapter supports data breakpoint requests.</summary>
    bool SupportsDataBreakpoints => false;

    /// <summary>Raised whenever the observable session state changes.</summary>
    event Action<DebugSessionSnapshot>? SnapshotChanged;

    /// <summary>Raised for adapter and debuggee output.</summary>
    event Action<DebugOutputEvent>? OutputReceived;

    /// <summary>Raised after an adapter verifies or rejects a replacement breakpoint set.</summary>
    event Action<IReadOnlyList<DebugBreakpointStatus>>? BreakpointStatusesChanged;

    /// <summary>Starts an adapter and launches a debuggee.</summary>
    /// <param name="configuration">Adapter, launch, and breakpoint configuration.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task StartAsync(DebugSessionConfiguration configuration, CancellationToken cancellationToken = default);

    /// <summary>Replaces breakpoints for one source file.</summary>
    /// <param name="path">Absolute source path.</param>
    /// <param name="breakpoints">Breakpoints for the source.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SetBreakpointsAsync(string path, IReadOnlyList<DebugSourceBreakpoint> breakpoints, CancellationToken cancellationToken = default);

    /// <summary>Replaces enabled exception breakpoint filters.</summary>
    /// <param name="filters">Adapter-defined filter identifiers.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SetExceptionBreakpointsAsync(
        IReadOnlyList<string> filters,
        CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <summary>Continues execution to an adapter-provided target near a source location.</summary>
    /// <param name="path">Absolute source path.</param>
    /// <param name="line">One-based source line.</param>
    /// <param name="column">Optional one-based source column.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RunToLocationAsync(
        string path,
        int line,
        int? column = null,
        CancellationToken cancellationToken = default) =>
        Task.FromException(new NotSupportedException("Run to location is not implemented by this debug session."));

    /// <summary>Continues the active stopped thread.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ContinueAsync(CancellationToken cancellationToken = default);

    /// <summary>Steps over the current statement.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task NextAsync(CancellationToken cancellationToken = default);

    /// <summary>Steps into the current statement.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task StepInAsync(CancellationToken cancellationToken = default);

    /// <summary>Steps out of the current frame.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task StepOutAsync(CancellationToken cancellationToken = default);

    /// <summary>Pauses the active thread.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task PauseAsync(CancellationToken cancellationToken = default);

    /// <summary>Loads scopes for a stack frame.</summary>
    /// <param name="frameId">Adapter frame identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Frame scopes.</returns>
    Task<IReadOnlyList<DebugScope>> GetScopesAsync(int frameId, CancellationToken cancellationToken = default);

    /// <summary>Loads variables for a scope or nested variable.</summary>
    /// <param name="variablesReference">Adapter variables reference.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Variables returned by the adapter.</returns>
    Task<IReadOnlyList<DebugVariable>> GetVariablesAsync(int variablesReference, CancellationToken cancellationToken = default);

    /// <summary>Evaluates an expression in a stopped stack frame.</summary>
    /// <param name="expression">Language expression understood by the active adapter.</param>
    /// <param name="frameId">Optional adapter stack-frame identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Formatted evaluation result.</returns>
    Task<DebugEvaluation> EvaluateAsync(
        string expression,
        int? frameId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Evaluates an expression in the adapter's interactive REPL context.</summary>
    /// <param name="expression">Expression or debugger command.</param>
    /// <param name="frameId">Optional stopped stack-frame identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Formatted evaluation result.</returns>
    Task<DebugEvaluation> EvaluateReplAsync(
        string expression,
        int? frameId = null,
        CancellationToken cancellationToken = default) =>
        EvaluateAsync(expression, frameId, cancellationToken);

    /// <summary>Changes a variable value in a stopped scope or container.</summary>
    /// <param name="variablesReference">Parent scope or variable reference.</param>
    /// <param name="name">Variable name.</param>
    /// <param name="value">Language expression for the new value.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Updated variable representation.</returns>
    Task<DebugVariable> SetVariableAsync(
        int variablesReference,
        string name,
        string value,
        CancellationToken cancellationToken = default) =>
        Task.FromException<DebugVariable>(new NotSupportedException("Set variable is not implemented by this debug session."));

    /// <summary>Resolves an adapter data identifier for a variable.</summary>
    /// <param name="name">Variable name.</param>
    /// <param name="variablesReference">Optional parent container reference.</param>
    /// <param name="frameId">Optional stopped frame identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Data breakpoint metadata.</returns>
    Task<DebugDataBreakpointInfo> GetDataBreakpointInfoAsync(
        string name,
        int? variablesReference = null,
        int? frameId = null,
        CancellationToken cancellationToken = default) =>
        Task.FromException<DebugDataBreakpointInfo>(new NotSupportedException("Data breakpoints are not implemented by this debug session."));

    /// <summary>Replaces active data breakpoints.</summary>
    /// <param name="breakpoints">Complete data breakpoint set.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SetDataBreakpointsAsync(
        IReadOnlyList<DebugDataBreakpoint> breakpoints,
        CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <summary>Disconnects and terminates the active adapter session.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task StopAsync(CancellationToken cancellationToken = default);
}

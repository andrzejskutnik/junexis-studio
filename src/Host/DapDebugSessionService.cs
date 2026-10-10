using System.Text.Json;
using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Host;

/// <summary>Runs and controls one process-based Debug Adapter Protocol session.</summary>
public sealed class DapDebugSessionService : IDebugSessionService
{
    private readonly Func<DebugSessionConfiguration, Action<DebugOutputEvent>?, CancellationToken,
        Task<IDapAdapterConnection>> _connectionFactory;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly object _breakpointGate = new();
    private readonly IDebugSourceMapper? _sourceMapper;
    private IDapAdapterConnection? _connection;
    private DapProtocolClient? _client;
    private DapProtocolClient? _activeClient;
    private readonly List<(IDapAdapterConnection Connection, DapProtocolClient Client)> _childSessions = [];
    private string? _sourceMapTarget;
    private IReadOnlyList<DebugBreakpointStatus> _breakpointStatuses = [];
    private IReadOnlyList<DebugExceptionBreakpointFilter> _exceptionBreakpointFilters = [];
    private IReadOnlyList<string> _enabledExceptionBreakpointFilters = [];
    private bool _supportsSetVariable;
    private bool _supportsDataBreakpoints;
    private bool _terminateDebuggeeOnDisconnect = true;
    private IReadOnlyDictionary<string, IReadOnlyList<DebugSourceBreakpoint>> _mappedBreakpoints =
        new Dictionary<string, IReadOnlyList<DebugSourceBreakpoint>>(StringComparer.OrdinalIgnoreCase);
    private TaskCompletionSource _initialized = NewSignal();
    private DebugSessionSnapshot _snapshot = new(DebugSessionState.Inactive, "No debug session.", null, [], []);

    /// <summary>Creates a process-based DAP session service.</summary>
    public DapDebugSessionService()
        : this(sourceMapper: null)
    {
    }

    /// <summary>Creates a process-based DAP session with Junexis frame mapping.</summary>
    /// <param name="sourceMapper">Optional generated-to-Junexis source mapper.</param>
    public DapDebugSessionService(IDebugSourceMapper? sourceMapper)
        : this(async (configuration, output, cancellationToken) => configuration.Transport switch
        {
            DebugAdapterTransport.Stdio => await ProcessDapAdapterConnection.StartAsync(configuration, output, cancellationToken).ConfigureAwait(false),
            DebugAdapterTransport.Tcp => await TcpProcessDapAdapterConnection.StartAsync(configuration, output, cancellationToken).ConfigureAwait(false),
            _ => throw new ArgumentOutOfRangeException(nameof(configuration.Transport)),
        }, sourceMapper)
    {
    }

    internal DapDebugSessionService(
        Func<DebugSessionConfiguration, Action<DebugOutputEvent>?, CancellationToken,
            Task<IDapAdapterConnection>> connectionFactory,
        IDebugSourceMapper? sourceMapper = null)
    {
        _connectionFactory = connectionFactory;
        _sourceMapper = sourceMapper;
    }

    /// <inheritdoc />
    public DebugSessionSnapshot Snapshot => Volatile.Read(ref _snapshot);

    /// <inheritdoc />
    public IReadOnlyList<DebugBreakpointStatus> BreakpointStatuses
    {
        get
        {
            lock (_breakpointGate)
                return _breakpointStatuses;
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<DebugExceptionBreakpointFilter> ExceptionBreakpointFilters =>
        Volatile.Read(ref _exceptionBreakpointFilters);

    /// <inheritdoc />
    public bool SupportsSetVariable => Volatile.Read(ref _supportsSetVariable);

    /// <inheritdoc />
    public bool SupportsDataBreakpoints => Volatile.Read(ref _supportsDataBreakpoints);

    /// <inheritdoc />
    public event Action<DebugSessionSnapshot>? SnapshotChanged;

    /// <inheritdoc />
    public event Action<DebugOutputEvent>? OutputReceived;

    /// <inheritdoc />
    public event Action<IReadOnlyList<DebugBreakpointStatus>>? BreakpointStatusesChanged;

    /// <inheritdoc />
    public async Task StartAsync(DebugSessionConfiguration configuration, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(configuration.AdapterFileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(configuration.WorkingDirectory);
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_client is not null)
                throw new InvalidOperationException("A debug session is already active.");
            SetSnapshot(new(DebugSessionState.Starting, "Starting debug adapter…", null, [], []));
            _sourceMapTarget = configuration.SourceMapTarget;
            lock (_breakpointGate)
                _mappedBreakpoints = new Dictionary<string, IReadOnlyList<DebugSourceBreakpoint>>(
                    StringComparer.OrdinalIgnoreCase);
            _initialized = NewSignal();
            _connection = await _connectionFactory(configuration, PublishOutput, cancellationToken).ConfigureAwait(false);
            PublishOutput(new DebugOutputEvent("adapter.lifecycle", "DAP transport connected."));
            _client = new DapProtocolClient(_connection.Input, _connection.Output, HandleReverseRequestAsync);
            _activeClient = _client;
            _client.EventReceived += OnEvent;
            var adapterId = configuration.LaunchArguments.TryGetValue("type", out var adapterType)
                && adapterType is string configuredAdapterId
                && !string.IsNullOrWhiteSpace(configuredAdapterId)
                    ? configuredAdapterId
                    : "junexis";

            var initializeBody = await _client.SendRequestAsync("initialize", new
            {
                clientID = "junexis-studio",
                clientName = "Junexis Studio",
                adapterID = adapterId,
                pathFormat = "path",
                linesStartAt1 = true,
                columnsStartAt1 = true,
                supportsVariableType = true,
                supportsRunInTerminalRequest = false,
                supportsStartDebuggingRequest = configuration.Transport == DebugAdapterTransport.Tcp,
            }, cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref _exceptionBreakpointFilters, ParseExceptionBreakpointFilters(initializeBody));
            ApplyAdapterCapabilities(initializeBody);
            PublishOutput(new DebugOutputEvent("adapter.lifecycle", "DAP initialize accepted."));

            var requestCommand = GetDebugRequestCommand(configuration.LaunchArguments);
            _terminateDebuggeeOnDisconnect = requestCommand == "launch";
            var launchTask = _client.SendRequestAsync(requestCommand, configuration.LaunchArguments, cancellationToken);
            await _initialized.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            PublishOutput(new DebugOutputEvent("adapter.lifecycle", "DAP initialized event received."));
            var exceptionFilters = configuration.ExceptionBreakpointFilters ?? [];
            if (exceptionFilters.Count > 0)
                await SetExceptionBreakpointsAsync(exceptionFilters, cancellationToken).ConfigureAwait(false);
            foreach (var group in configuration.Breakpoints.GroupBy(item => item.Path, StringComparer.OrdinalIgnoreCase))
                await SetBreakpointsAsync(group.Key, group.ToArray(), cancellationToken).ConfigureAwait(false);
            await _client.SendRequestAsync("configurationDone", new { }, cancellationToken).ConfigureAwait(false);
            await launchTask.ConfigureAwait(false);
            PublishOutput(new DebugOutputEvent("adapter.lifecycle", $"DAP {requestCommand} accepted."));
            if (Snapshot.State == DebugSessionState.Starting)
                SetSnapshot(new(DebugSessionState.Running, "Debuggee running.", null, [], []));
        }
        catch (Exception ex)
        {
            await CleanupAsync().ConfigureAwait(false);
            SetSnapshot(new(DebugSessionState.Failed, $"Debug session failed: {ex.Message}", null, [], []));
            throw;
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    /// <inheritdoc />
    public Task SetBreakpointsAsync(
        string path,
        IReadOnlyList<DebugSourceBreakpoint> breakpoints,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(breakpoints);
        var mapped = _sourceMapper?.MapBreakpoints(_sourceMapTarget, path, breakpoints)
            ?? new Dictionary<string, IReadOnlyList<DebugSourceBreakpoint>>(StringComparer.OrdinalIgnoreCase)
            {
                [path] = breakpoints,
            };
        return SetMappedBreakpointsAsync(mapped, cancellationToken);
    }

    /// <inheritdoc />
    public async Task SetExceptionBreakpointsAsync(
        IReadOnlyList<string> filters,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filters);
        var normalized = filters.Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        await RequireClient().SendRequestAsync("setExceptionBreakpoints", new { filters = normalized }, cancellationToken)
            .ConfigureAwait(false);
        Volatile.Write(ref _enabledExceptionBreakpointFilters, normalized);
    }

    /// <inheritdoc />
    public async Task RunToLocationAsync(
        string path,
        int line,
        int? column = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfLessThan(line, 1);
        var fullPath = Path.GetFullPath(path);
        var requested = new DebugSourceBreakpoint(fullPath, line, column);
        var mapped = _sourceMapper?.MapBreakpoints(_sourceMapTarget, fullPath, [requested])
            ?? new Dictionary<string, IReadOnlyList<DebugSourceBreakpoint>>(StringComparer.OrdinalIgnoreCase)
            {
                [fullPath] = [requested],
            };
        var target = mapped.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .SelectMany(item => item.Value.Select(breakpoint => (Path: item.Key, Breakpoint: breakpoint)))
            .FirstOrDefault();
        if (target.Breakpoint is null)
            throw new InvalidOperationException("The source location cannot be mapped to a runtime target.");
        var gotoArguments = new Dictionary<string, object>
        {
            ["source"] = new { path = Path.GetFullPath(target.Path) },
            ["line"] = target.Breakpoint.Line,
        };
        if (target.Breakpoint.Column is not null)
            gotoArguments["column"] = target.Breakpoint.Column.Value;
        var body = await RequireClient().SendRequestAsync("gotoTargets", gotoArguments, cancellationToken)
            .ConfigureAwait(false);
        var gotoTarget = body.TryGetProperty("targets", out var targets) && targets.ValueKind == JsonValueKind.Array
            ? targets.EnumerateArray().FirstOrDefault()
            : default;
        var targetId = OptionalInt(gotoTarget, "id")
            ?? throw new NotSupportedException("The debug adapter did not provide a run-to-cursor target.");
        var threadId = RequireThread();
        await RequireClient().SendRequestAsync("goto", new { threadId, targetId }, cancellationToken)
            .ConfigureAwait(false);
        SetRunningSnapshot("Running to cursor.");
    }

    private async Task SetMappedBreakpointsAsync(
        IReadOnlyDictionary<string, IReadOnlyList<DebugSourceBreakpoint>> mapped,
        CancellationToken cancellationToken)
    {
        var client = RequireClient();
        lock (_breakpointGate)
        {
            var cached = new Dictionary<string, IReadOnlyList<DebugSourceBreakpoint>>(
                _mappedBreakpoints, StringComparer.OrdinalIgnoreCase);
            foreach (var (path, breakpoints) in mapped)
                cached[path] = breakpoints.ToArray();
            _mappedBreakpoints = cached;
        }
        await SetMappedBreakpointsAsync(client, mapped, publishStatuses: true, cancellationToken).ConfigureAwait(false);
    }

    private async Task SetMappedBreakpointsAsync(
        DapProtocolClient client,
        IReadOnlyDictionary<string, IReadOnlyList<DebugSourceBreakpoint>> mapped,
        bool publishStatuses,
        CancellationToken cancellationToken)
    {
        var statuses = new List<DebugBreakpointStatus>();
        foreach (var (mappedPath, mappedBreakpoints) in mapped)
        {
            var body = await client.SendRequestAsync("setBreakpoints", new
            {
                source = new { path = Path.GetFullPath(mappedPath) },
                breakpoints = mappedBreakpoints.Select(CreateBreakpointArguments).ToArray(),
                sourceModified = false,
            }, cancellationToken).ConfigureAwait(false);
            statuses.AddRange(ParseBreakpointStatuses(mappedPath, mappedBreakpoints, body));
        }
        if (publishStatuses)
            PublishBreakpointStatuses(statuses);
    }

    private static IReadOnlyDictionary<string, object> CreateBreakpointArguments(DebugSourceBreakpoint breakpoint)
    {
        var arguments = new Dictionary<string, object>
        {
            ["line"] = breakpoint.Line,
        };
        if (breakpoint.Column is not null)
            arguments["column"] = breakpoint.Column.Value;
        if (!string.IsNullOrWhiteSpace(breakpoint.Condition))
            arguments["condition"] = breakpoint.Condition;
        if (!string.IsNullOrWhiteSpace(breakpoint.HitCondition))
            arguments["hitCondition"] = breakpoint.HitCondition;
        if (!string.IsNullOrWhiteSpace(breakpoint.LogMessage))
            arguments["logMessage"] = breakpoint.LogMessage;
        return arguments;
    }

    private static IReadOnlyDictionary<string, object> CreateDataBreakpointArguments(DebugDataBreakpoint breakpoint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(breakpoint.DataId);
        var arguments = new Dictionary<string, object> { ["dataId"] = breakpoint.DataId };
        if (!string.IsNullOrWhiteSpace(breakpoint.AccessType))
            arguments["accessType"] = breakpoint.AccessType;
        if (!string.IsNullOrWhiteSpace(breakpoint.Condition))
            arguments["condition"] = breakpoint.Condition;
        if (!string.IsNullOrWhiteSpace(breakpoint.HitCondition))
            arguments["hitCondition"] = breakpoint.HitCondition;
        return arguments;
    }

    private IReadOnlyList<DebugBreakpointStatus> ParseBreakpointStatuses(
        string runtimePath,
        IReadOnlyList<DebugSourceBreakpoint> requested,
        JsonElement body)
    {
        var responses = body.TryGetProperty("breakpoints", out var values)
            && values.ValueKind == JsonValueKind.Array
            ? values.EnumerateArray().ToArray()
            : [];
        return requested.Select((breakpoint, index) =>
        {
            var response = index < responses.Length ? responses[index] : default;
            var verified = response.ValueKind == JsonValueKind.Object
                && response.TryGetProperty("verified", out var verifiedValue)
                && verifiedValue.ValueKind == JsonValueKind.True;
            var actualLine = OptionalInt(response, "line");
            var actualColumn = OptionalInt(response, "column");
            var mapped = _sourceMapper?.MapFrame(new DebugStackFrame(
                0, "breakpoint", runtimePath, breakpoint.Line, breakpoint.Column ?? 1));
            return new DebugBreakpointStatus(
                mapped?.SourcePath ?? runtimePath,
                mapped?.Line ?? breakpoint.Line,
                mapped?.Column ?? breakpoint.Column,
                Path.GetFullPath(runtimePath),
                breakpoint.Line,
                breakpoint.Column,
                verified,
                actualLine,
                actualColumn,
                OptionalString(response, "message"),
                OptionalInt(response, "id"));
        }).ToArray();
    }

    private void PublishBreakpointStatuses(IReadOnlyList<DebugBreakpointStatus> statuses)
    {
        lock (_breakpointGate)
            _breakpointStatuses = statuses.ToArray();
        BreakpointStatusesChanged?.Invoke(BreakpointStatuses);
    }

    /// <inheritdoc />
    public Task ContinueAsync(CancellationToken cancellationToken = default) =>
        ResumeAsync("continue", cancellationToken);

    /// <inheritdoc />
    public Task NextAsync(CancellationToken cancellationToken = default) =>
        ResumeAsync("next", cancellationToken);

    /// <inheritdoc />
    public Task StepInAsync(CancellationToken cancellationToken = default) =>
        ResumeAsync("stepIn", cancellationToken);

    /// <inheritdoc />
    public Task StepOutAsync(CancellationToken cancellationToken = default) =>
        ResumeAsync("stepOut", cancellationToken);

    /// <inheritdoc />
    public async Task PauseAsync(CancellationToken cancellationToken = default)
    {
        var threadId = RequireThread();
        await RequireClient().SendRequestAsync("pause", new { threadId }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DebugScope>> GetScopesAsync(
        int frameId,
        CancellationToken cancellationToken = default)
    {
        var body = await RequireClient().SendRequestAsync("scopes", new { frameId }, cancellationToken).ConfigureAwait(false);
        if (!body.TryGetProperty("scopes", out var scopes))
            return [];
        return scopes.EnumerateArray().Select(item => new DebugScope(
            GetString(item, "name", "Scope"),
            GetInt(item, "variablesReference"),
            item.TryGetProperty("expensive", out var expensive) && expensive.GetBoolean())).ToArray();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DebugVariable>> GetVariablesAsync(
        int variablesReference,
        CancellationToken cancellationToken = default)
    {
        var body = await RequireClient().SendRequestAsync("variables", new { variablesReference }, cancellationToken)
            .ConfigureAwait(false);
        if (!body.TryGetProperty("variables", out var variables))
            return [];
        return variables.EnumerateArray().Select(item => new DebugVariable(
            GetString(item, "name", string.Empty),
            GetString(item, "value", string.Empty),
            item.TryGetProperty("type", out var type) ? type.GetString() : null,
            GetInt(item, "variablesReference"))).ToArray();
    }

    /// <inheritdoc />
    public async Task<DebugEvaluation> EvaluateAsync(
        string expression,
        int? frameId = null,
        CancellationToken cancellationToken = default)
    {
        return await EvaluateCoreAsync(expression, frameId, "watch", cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<DebugEvaluation> EvaluateReplAsync(
        string expression,
        int? frameId = null,
        CancellationToken cancellationToken = default) =>
        EvaluateCoreAsync(expression, frameId, "repl", cancellationToken);

    private async Task<DebugEvaluation> EvaluateCoreAsync(
        string expression,
        int? frameId,
        string context,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);
        var body = await RequireClient().SendRequestAsync("evaluate", new
        {
            expression,
            frameId,
            context,
        }, cancellationToken).ConfigureAwait(false);
        return new DebugEvaluation(
            expression,
            GetString(body, "result", string.Empty),
            OptionalString(body, "type"),
            GetInt(body, "variablesReference"));
    }

    /// <inheritdoc />
    public async Task<DebugVariable> SetVariableAsync(
        int variablesReference,
        string name,
        string value,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(variablesReference, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var body = await RequireClient().SendRequestAsync("setVariable", new
        {
            variablesReference,
            name,
            value,
        }, cancellationToken).ConfigureAwait(false);
        return new DebugVariable(
            name,
            GetString(body, "value", value),
            OptionalString(body, "type"),
            GetInt(body, "variablesReference"));
    }

    /// <inheritdoc />
    public async Task<DebugDataBreakpointInfo> GetDataBreakpointInfoAsync(
        string name,
        int? variablesReference = null,
        int? frameId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var arguments = new Dictionary<string, object> { ["name"] = name };
        if (variablesReference is > 0)
            arguments["variablesReference"] = variablesReference.Value;
        if (frameId is not null)
            arguments["frameId"] = frameId.Value;
        var body = await RequireClient().SendRequestAsync("dataBreakpointInfo", arguments, cancellationToken)
            .ConfigureAwait(false);
        var accessTypes = body.TryGetProperty("accessTypes", out var values) && values.ValueKind == JsonValueKind.Array
            ? values.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString()!).ToArray()
            : [];
        return new DebugDataBreakpointInfo(
            OptionalString(body, "dataId"),
            GetString(body, "description", name),
            accessTypes,
            body.TryGetProperty("canPersist", out var canPersist) && canPersist.ValueKind == JsonValueKind.True);
    }

    /// <inheritdoc />
    public async Task SetDataBreakpointsAsync(
        IReadOnlyList<DebugDataBreakpoint> breakpoints,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(breakpoints);
        var payload = breakpoints.Select(CreateDataBreakpointArguments).ToArray();
        await RequireClient().SendRequestAsync("setDataBreakpoints", new { breakpoints = payload }, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_client is not null)
            {
                try
                {
                    using var disconnectTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    disconnectTimeout.CancelAfter(TimeSpan.FromSeconds(2));
                    await _client.SendRequestAsync("disconnect", new
                    {
                        terminateDebuggee = _terminateDebuggeeOnDisconnect,
                    }, disconnectTimeout.Token)
                        .ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                {
                    // The adapter may close immediately or fail to answer disconnect.
                }
            }
            await CleanupAsync().ConfigureAwait(false);
            SetSnapshot(new(DebugSessionState.Terminated, "Debug session terminated.", null, [], []));
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _lifecycle.Dispose();
    }

    private async Task ResumeAsync(string command, CancellationToken cancellationToken)
    {
        var threadId = RequireThread();
        await RequireClient().SendRequestAsync(command, new { threadId }, cancellationToken).ConfigureAwait(false);
        SetRunningSnapshot("Debuggee running.");
    }

    private void SetRunningSnapshot(string status) =>
        SetSnapshot(Snapshot with { State = DebugSessionState.Running, Status = status, StackFrames = [] });

    private void OnEvent(DapEventMessage message)
    {
        switch (message.Event)
        {
            case "initialized":
                _initialized.TrySetResult();
                break;
            case "stopped":
                _ = RefreshStoppedAsync(message.Body, _activeClient ?? RequireClient());
                break;
            case "continued":
                SetSnapshot(Snapshot with { State = DebugSessionState.Running, Status = "Debuggee running.", StackFrames = [] });
                break;
            case "breakpoint":
                ApplyBreakpointEvent(message.Body);
                break;
            case "terminated":
            case "exited":
                SetSnapshot(new(DebugSessionState.Terminated, "Debuggee terminated.", null, [], []));
                break;
            case "output":
                PublishOutput(new DebugOutputEvent(
                    GetString(message.Body, "category", "console"),
                    GetString(message.Body, "output", string.Empty)));
                break;
        }
    }

    private async Task RefreshStoppedAsync(JsonElement body, DapProtocolClient client)
    {
        try
        {
            _activeClient = client;
            var threadsBody = await client.SendRequestAsync("threads", new { }).ConfigureAwait(false);
            var threads = threadsBody.TryGetProperty("threads", out var threadArray)
                ? threadArray.EnumerateArray().Select(item => new DebugThread(
                    GetInt(item, "id"), GetString(item, "name", "Thread"))).ToArray()
                : [];
            var threadId = body.TryGetProperty("threadId", out var stoppedThread)
                ? stoppedThread.GetInt32()
                : threads.FirstOrDefault()?.Id;
            IReadOnlyList<DebugStackFrame> frames = [];
            if (threadId is not null)
            {
                var stackBody = await client.SendRequestAsync("stackTrace", new { threadId }).ConfigureAwait(false);
                if (stackBody.TryGetProperty("stackFrames", out var stackFrames))
                    frames = stackFrames.EnumerateArray()
                        .Select(ParseFrame)
                        .Select(frame => _sourceMapper?.MapFrame(frame) ?? frame)
                        .ToArray();
            }
            var reason = GetString(body, "reason", "breakpoint");
            SetSnapshot(new(DebugSessionState.Stopped, $"Stopped: {reason}.", threadId, threads, frames));
        }
        catch (Exception ex)
        {
            SetSnapshot(new(DebugSessionState.Failed, $"Cannot read stopped state: {ex.Message}", null, [], []));
        }
    }

    private async Task CleanupAsync()
    {
        _sourceMapTarget = null;
        Volatile.Write(ref _exceptionBreakpointFilters, []);
        Volatile.Write(ref _enabledExceptionBreakpointFilters, []);
        Volatile.Write(ref _supportsSetVariable, false);
        Volatile.Write(ref _supportsDataBreakpoints, false);
        lock (_breakpointGate)
            _mappedBreakpoints = new Dictionary<string, IReadOnlyList<DebugSourceBreakpoint>>(
                StringComparer.OrdinalIgnoreCase);
        PublishBreakpointStatuses([]);
        (IDapAdapterConnection Connection, DapProtocolClient Client)[] childSessions;
        lock (_childSessions)
        {
            childSessions = _childSessions.ToArray();
            _childSessions.Clear();
        }
        foreach (var (connection, client) in childSessions)
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            await client.DisposeAsync().ConfigureAwait(false);
        }
        _activeClient = null;
        if (_connection is not null)
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
            _connection = null;
        }
        if (_client is not null)
        {
            _client.EventReceived -= OnEvent;
            await _client.DisposeAsync().ConfigureAwait(false);
            _client = null;
        }
    }

    private void PublishOutput(DebugOutputEvent value) => OutputReceived?.Invoke(value);

    private void SetSnapshot(DebugSessionSnapshot snapshot)
    {
        Volatile.Write(ref _snapshot, snapshot);
        SnapshotChanged?.Invoke(snapshot);
    }

    private DapProtocolClient RequireClient() =>
        _activeClient ?? _client ?? throw new InvalidOperationException("No active DAP session.");

    private Task<object?> HandleReverseRequestAsync(
        string command,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        if (!command.Equals("startDebugging", StringComparison.Ordinal)
            || _connection is not IMultiplexDapAdapterConnection multiplex
            || !arguments.TryGetProperty("configuration", out var configuration))
            throw new NotSupportedException();
        _ = StartChildSessionAsync(multiplex, configuration.Clone(), cancellationToken);
        PublishOutput(new DebugOutputEvent("adapter.lifecycle", "DAP startDebugging child session requested."));
        return Task.FromResult<object?>(new { });
    }

    private async Task StartChildSessionAsync(
        IMultiplexDapAdapterConnection multiplex,
        JsonElement launchConfiguration,
        CancellationToken cancellationToken)
    {
        try
        {
            var connection = await multiplex.OpenAdditionalConnectionAsync(cancellationToken).ConfigureAwait(false);
            var client = new DapProtocolClient(connection.Input, connection.Output, HandleReverseRequestAsync);
            var initialized = NewSignal();
            client.EventReceived += message =>
            {
                if (message.Event == "initialized")
                    initialized.TrySetResult();
                OnChildEvent(client, message);
            };
            lock (_childSessions)
                _childSessions.Add((connection, client));
            var initializeBody = await client.SendRequestAsync("initialize", new
            {
                clientID = "junexis-studio",
                clientName = "Junexis Studio",
                adapterID = GetString(launchConfiguration, "type", "junexis"),
                pathFormat = "path",
                linesStartAt1 = true,
                columnsStartAt1 = true,
                supportsVariableType = true,
                supportsRunInTerminalRequest = false,
                supportsStartDebuggingRequest = true,
            }, cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref _exceptionBreakpointFilters, ParseExceptionBreakpointFilters(initializeBody));
            ApplyAdapterCapabilities(initializeBody);
            var requestCommand = GetDebugRequestCommand(launchConfiguration);
            var launch = client.SendRequestAsync(requestCommand, launchConfiguration, cancellationToken);
            await initialized.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            var exceptionFilters = Volatile.Read(ref _enabledExceptionBreakpointFilters);
            if (exceptionFilters.Count > 0)
                await client.SendRequestAsync("setExceptionBreakpoints", new { filters = exceptionFilters }, cancellationToken)
                    .ConfigureAwait(false);
            IReadOnlyDictionary<string, IReadOnlyList<DebugSourceBreakpoint>> cachedBreakpoints;
            lock (_breakpointGate)
                cachedBreakpoints = new Dictionary<string, IReadOnlyList<DebugSourceBreakpoint>>(
                    _mappedBreakpoints, StringComparer.OrdinalIgnoreCase);
            await SetMappedBreakpointsAsync(
                client, cachedBreakpoints, publishStatuses: true, cancellationToken).ConfigureAwait(false);
            await client.SendRequestAsync("configurationDone", new { }, cancellationToken).ConfigureAwait(false);
            await launch.ConfigureAwait(false);
            _activeClient = client;
            PublishOutput(new DebugOutputEvent("adapter.lifecycle", "DAP child session connected."));
        }
        catch (Exception ex)
        {
            PublishOutput(new DebugOutputEvent("adapter.stderr", $"DAP child session failed: {ex.Message}"));
            SetSnapshot(new(DebugSessionState.Failed, $"DAP child session failed: {ex.Message}", null, [], []));
        }
    }

    private void OnChildEvent(DapProtocolClient client, DapEventMessage message)
    {
        if (message.Event == "stopped")
        {
            _activeClient = client;
            _ = RefreshStoppedAsync(message.Body, client);
            return;
        }
        OnEvent(message);
    }

    private void ApplyBreakpointEvent(JsonElement body)
    {
        if (!body.TryGetProperty("breakpoint", out var breakpoint)
            || breakpoint.ValueKind != JsonValueKind.Object)
            return;
        var adapterId = OptionalInt(breakpoint, "id");
        if (adapterId is null)
            return;
        IReadOnlyList<DebugBreakpointStatus> updated;
        lock (_breakpointGate)
        {
            updated = _breakpointStatuses.Select(status => status.AdapterId == adapterId
                ? status with
                {
                    Verified = breakpoint.TryGetProperty("verified", out var verified)
                        && verified.ValueKind == JsonValueKind.True,
                    ActualLine = OptionalInt(breakpoint, "line") ?? status.ActualLine,
                    ActualColumn = OptionalInt(breakpoint, "column") ?? status.ActualColumn,
                    Message = OptionalString(breakpoint, "message"),
                }
                : status).ToArray();
            _breakpointStatuses = updated;
        }
        BreakpointStatusesChanged?.Invoke(updated);
    }

    private int RequireThread() => Snapshot.ActiveThreadId
        ?? throw new InvalidOperationException("No stopped debug thread is active.");

    private static DebugStackFrame ParseFrame(JsonElement item)
    {
        string? path = null;
        if (item.TryGetProperty("source", out var source)
            && source.TryGetProperty("path", out var sourcePath))
            path = sourcePath.GetString();
        return new DebugStackFrame(
            GetInt(item, "id"),
            GetString(item, "name", "Frame"),
            path,
            GetInt(item, "line", 1),
            GetInt(item, "column", 1));
    }

    private static IReadOnlyList<DebugExceptionBreakpointFilter> ParseExceptionBreakpointFilters(JsonElement body)
    {
        if (!body.TryGetProperty("exceptionBreakpointFilters", out var filters)
            || filters.ValueKind != JsonValueKind.Array)
            return [];
        return filters.EnumerateArray()
            .Select(item => new DebugExceptionBreakpointFilter(
                GetString(item, "filter", string.Empty),
                GetString(item, "label", GetString(item, "filter", string.Empty)),
                item.TryGetProperty("default", out var enabled) && enabled.ValueKind == JsonValueKind.True))
            .Where(item => item.Filter.Length > 0)
            .ToArray();
    }

    private void ApplyAdapterCapabilities(JsonElement body)
    {
        Volatile.Write(ref _supportsSetVariable,
            body.TryGetProperty("supportsSetVariable", out var setVariable)
            && setVariable.ValueKind == JsonValueKind.True);
        Volatile.Write(ref _supportsDataBreakpoints,
            body.TryGetProperty("supportsDataBreakpoints", out var dataBreakpoints)
            && dataBreakpoints.ValueKind == JsonValueKind.True);
    }

    private static int GetInt(JsonElement element, string property, int fallback = 0) =>
        element.TryGetProperty(property, out var value) && value.TryGetInt32(out var result) ? result : fallback;

    private static string GetString(JsonElement element, string property, string fallback) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;

    private static int? OptionalInt(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.TryGetInt32(out var result)
            ? result
            : null;

    private static string? OptionalString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string GetDebugRequestCommand(IReadOnlyDictionary<string, object?> arguments) =>
        arguments.TryGetValue("request", out var request) && request is string text
        && text.Equals("attach", StringComparison.OrdinalIgnoreCase)
            ? "attach"
            : "launch";

    private static string GetDebugRequestCommand(JsonElement arguments) =>
        GetString(arguments, "request", "launch").Equals("attach", StringComparison.OrdinalIgnoreCase)
            ? "attach"
            : "launch";

    private static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task SendWithoutResultAsync(
        DapProtocolClient client,
        string command,
        object arguments,
        CancellationToken cancellationToken) =>
        await client.SendRequestAsync(command, arguments, cancellationToken).ConfigureAwait(false);
}

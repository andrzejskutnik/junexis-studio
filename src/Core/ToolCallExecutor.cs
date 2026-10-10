using System.Diagnostics;
using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Core;

/// <summary>
/// Executes MCP tool calls through the connector registry with capability gating and logging.
/// </summary>
public sealed class ToolCallExecutor
{
    private readonly IMcpConnectorRegistry _registry;
    private readonly IOutput? _output;
    private readonly HashSet<string> _allowedTools = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _deniedTools = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    /// <summary>Output channel name for tool call messages.</summary>
    public const string OutputChannel = "OMWiA";

    /// <summary>
    /// Creates a new <see cref="ToolCallExecutor"/>.
    /// </summary>
    /// <param name="registry">The MCP connector registry.</param>
    /// <param name="output">Optional output service for logging.</param>
    /// <exception cref="ArgumentNullException">When <paramref name="registry"/> is null.</exception>
    public ToolCallExecutor(IMcpConnectorRegistry registry, IOutput? output = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _registry = registry;
        _output = output;
    }

    /// <summary>
    /// Adds a tool to the allow list. When the allow list is non-empty, only allowed tools can be called.
    /// </summary>
    /// <param name="toolName">Tool name to allow.</param>
    public void Allow(string toolName)
    {
        ArgumentNullException.ThrowIfNull(toolName);
        lock (_lock) _allowedTools.Add(toolName);
    }

    /// <summary>
    /// Adds a tool to the deny list. Denied tools are always blocked.
    /// </summary>
    /// <param name="toolName">Tool name to deny.</param>
    public void Deny(string toolName)
    {
        ArgumentNullException.ThrowIfNull(toolName);
        lock (_lock) _deniedTools.Add(toolName);
    }

    /// <summary>
    /// Checks whether a tool call is permitted based on allow/deny lists.
    /// </summary>
    /// <param name="toolName">The tool name to check.</param>
    /// <returns><c>true</c> if the tool is allowed; <c>false</c> otherwise.</returns>
    public bool IsAllowed(string toolName)
    {
        lock (_lock)
        {
            if (_deniedTools.Contains(toolName)) return false;
            if (_allowedTools.Count > 0 && !_allowedTools.Contains(toolName)) return false;
            return true;
        }
    }

    /// <summary>
    /// Executes a tool call through the active MCP connector.
    /// Checks capabilities before execution, enforces timeouts, and logs everything.
    /// </summary>
    /// <param name="request">The tool call request.</param>
    /// <param name="connectorId">Optional specific connector ID. Uses active connector if null.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>The tool call result.</returns>
    public async Task<ToolCallResult> ExecuteAsync(
        ToolCallRequest request,
        string? connectorId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        _output?.Write(OutputChannel, $"Tool call requested: {request.ToolName}.", OutputSeverity.Info);

        // Capability check
        if (!IsAllowed(request.ToolName))
        {
            var denyMsg = $"Tool '{request.ToolName}' is denied by capability policy.";
            _output?.Write(OutputChannel, denyMsg, OutputSeverity.Warning);
            return ToolCallResult.Fail(denyMsg);
        }

        // Resolve connector
        var connector = connectorId is not null
            ? _registry.Get(connectorId)
            : _registry.GetActive();

        if (connector is null)
        {
            var noConn = connectorId is not null
                ? $"Connector '{connectorId}' not found."
                : "No active MCP connector available.";
            _output?.Write(OutputChannel, noConn, OutputSeverity.Warning);
            return ToolCallResult.Fail(noConn);
        }

        if (!connector.IsConnected)
        {
            var notConn = $"Connector '{connector.ConnectorId}' is not connected.";
            _output?.Write(OutputChannel, notConn, OutputSeverity.Warning);
            return ToolCallResult.Fail(notConn);
        }

        // Execute with timeout
        var sw = Stopwatch.StartNew();
        try
        {
            using var timeoutCts = request.TimeoutMs > 0
                ? new CancellationTokenSource(request.TimeoutMs)
                : new CancellationTokenSource();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken);

            var mcpRequest = new McpRequest($"tool/{request.ToolName}", request.Payload);
            var response = await connector.SendAsync(mcpRequest, linked.Token).ConfigureAwait(false);

            sw.Stop();

            if (response.Success)
            {
                _output?.Write(OutputChannel,
                    $"Tool '{request.ToolName}' succeeded in {sw.ElapsedMilliseconds} ms.",
                    OutputSeverity.Info);
                return ToolCallResult.Ok(response.Payload, sw.ElapsedMilliseconds);
            }
            else
            {
                _output?.Write(OutputChannel,
                    $"Tool '{request.ToolName}' failed: {response.Error}.",
                    OutputSeverity.Warning);
                return ToolCallResult.Fail(response.Error ?? "Unknown error", sw.ElapsedMilliseconds);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            sw.Stop();
            _output?.Write(OutputChannel, $"Tool '{request.ToolName}' cancelled.", OutputSeverity.Warning);
            return ToolCallResult.Fail("Cancelled", sw.ElapsedMilliseconds);
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            _output?.Write(OutputChannel, $"Tool '{request.ToolName}' timed out after {sw.ElapsedMilliseconds} ms.", OutputSeverity.Error);
            return ToolCallResult.Fail("Timeout", sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _output?.Write(OutputChannel,
                $"Tool '{request.ToolName}' error: {ex.Message}.",
                OutputSeverity.Error);
            return ToolCallResult.Fail(ex.Message, sw.ElapsedMilliseconds);
        }
    }
}

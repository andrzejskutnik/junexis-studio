using System.Text.Json;
using Junexis.Studio.Abstractions;
using Junexis.Studio.Host;

namespace Junexis.Studio.Host.Tests;

public sealed class DapDebugSessionServiceTests
{
    [Fact]
    public async Task StartAsync_AttachConfiguration_SendsAttachRequest()
    {
        await using var connection = new DapProtocolClientTests.InMemoryDapConnection();
        await using var service = new DapDebugSessionService((_, _, _) =>
            Task.FromResult<IDapAdapterConnection>(connection));
        var adapter = RunAttachAdapterAsync(connection);
        var configuration = new DebugSessionConfiguration(
            "fake-adapter", "", Path.GetTempPath(),
            new Dictionary<string, object?>
            {
                ["name"] = "Attach Node",
                ["type"] = "pwa-node",
                ["request"] = "attach",
                ["processId"] = 4242,
            }, []);

        await service.StartAsync(configuration);

        Assert.Equal(DebugSessionState.Running, service.Snapshot.State);
        await service.StopAsync();
        await adapter;
    }

    [Fact]
    public async Task StartAndStoppedEvent_LoadThreadsFramesScopesAndVariables()
    {
        await using var connection = new DapProtocolClientTests.InMemoryDapConnection();
        await using var service = new DapDebugSessionService(
            (_, _, _) => Task.FromResult<IDapAdapterConnection>(connection), new RunToMapper());
        var adapter = RunAdapterAsync(connection);
        var configuration = new DebugSessionConfiguration(
            "fake-adapter", "", Path.GetTempPath(),
            new Dictionary<string, object?> { ["program"] = "sample", ["type"] = "cppdbg" },
            [
                new DebugSourceBreakpoint(
                    Path.Combine(Path.GetTempPath(), "main.go"), 7, null,
                    "answer > 40", ">= 3", "answer={answer}"),
                new DebugSourceBreakpoint(Path.Combine(Path.GetTempPath(), "main.go"), 10, 2),
            ], SourceMapTarget: "server", ExceptionBreakpointFilters: ["uncaught"]);

        await service.StartAsync(configuration);
        await WaitUntilAsync(() => service.Snapshot.State == DebugSessionState.Stopped);

        var frame = Assert.Single(service.Snapshot.StackFrames);
        Assert.Equal(42, frame.Id);
        Assert.Equal(7, frame.Line);
        Assert.Equal(2, service.BreakpointStatuses.Count);
        var exceptionFilter = Assert.Single(service.ExceptionBreakpointFilters);
        Assert.Equal("uncaught", exceptionFilter.Filter);
        Assert.Equal("Uncaught exceptions", exceptionFilter.Label);
        var breakpointStatus = service.BreakpointStatuses[0];
        Assert.True(breakpointStatus.Verified);
        Assert.Equal(7, breakpointStatus.RequestedLine);
        Assert.Equal(8, breakpointStatus.ActualLine);
        Assert.Equal(4, breakpointStatus.ActualColumn);
        Assert.Equal(71, breakpointStatus.AdapterId);
        var rejected = service.BreakpointStatuses[1];
        Assert.False(rejected.Verified);
        Assert.Equal("no executable code", rejected.Message);
        Assert.Equal(10, rejected.RequestedLine);
        var scope = Assert.Single(await service.GetScopesAsync(frame.Id));
        var variable = Assert.Single(await service.GetVariablesAsync(scope.VariablesReference));
        Assert.Equal("answer", variable.Name);
        Assert.Equal("42", variable.Value);
        var evaluation = await service.EvaluateAsync("answer + 1", frame.Id);
        Assert.Equal("answer + 1", evaluation.Expression);
        Assert.Equal("43", evaluation.Result);
        Assert.Equal("int", evaluation.Type);
        Assert.True(service.SupportsSetVariable);
        Assert.True(service.SupportsDataBreakpoints);
        var repl = await service.EvaluateReplAsync("answer", frame.Id);
        Assert.Equal("42", repl.Result);
        var updatedVariable = await service.SetVariableAsync(scope.VariablesReference, "answer", "50");
        Assert.Equal("50", updatedVariable.Value);
        var dataInfo = await service.GetDataBreakpointInfoAsync("answer", scope.VariablesReference, frame.Id);
        Assert.Equal("local.answer", dataInfo.DataId);
        Assert.Equal(["write", "readWrite"], dataInfo.AccessTypes);
        await service.SetDataBreakpointsAsync(
            [new DebugDataBreakpoint(dataInfo.DataId!, dataInfo.Description, "write", "answer > 0", ">= 2")]);

        await service.RunToLocationAsync(Path.Combine(Path.GetTempPath(), "main.go"), 12, 4);
        Assert.Equal(DebugSessionState.Running, service.Snapshot.State);
        Assert.Equal("Running to cursor.", service.Snapshot.Status);
        await service.StopAsync();
        await adapter;
        Assert.Equal(DebugSessionState.Terminated, service.Snapshot.State);
    }

    private static async Task RunAdapterAsync(DapProtocolClientTests.InMemoryDapConnection connection)
    {
        using var initialize = await connection.ReadClientMessageAsync();
        Assert.Equal("cppdbg", initialize.RootElement.GetProperty("arguments").GetProperty("adapterID").GetString());
        await RespondAsync(connection, initialize, new
        {
            supportsConfigurationDoneRequest = true,
            supportsGotoTargetsRequest = true,
            supportsSetVariable = true,
            supportsDataBreakpoints = true,
            exceptionBreakpointFilters = new[]
            {
                new { filter = "uncaught", label = "Uncaught exceptions", @default = true },
            },
        });
        using var launch = await connection.ReadClientMessageAsync();
        await connection.WriteAdapterMessageAsync(new { seq = 10, type = "event", @event = "initialized", body = new { } });
        using var exceptions = await connection.ReadClientMessageAsync();
        Assert.Equal("setExceptionBreakpoints", exceptions.RootElement.GetProperty("command").GetString());
        Assert.Equal("uncaught", exceptions.RootElement.GetProperty("arguments").GetProperty("filters")[0].GetString());
        await RespondAsync(connection, exceptions, new { breakpoints = Array.Empty<object>() });
        using var breakpoints = await connection.ReadClientMessageAsync();
        Assert.Equal("setBreakpoints", breakpoints.RootElement.GetProperty("command").GetString());
        var sentBreakpoint = breakpoints.RootElement.GetProperty("arguments").GetProperty("breakpoints")[0];
        Assert.Equal("answer > 40", sentBreakpoint.GetProperty("condition").GetString());
        Assert.Equal(">= 3", sentBreakpoint.GetProperty("hitCondition").GetString());
        Assert.Equal("answer={answer}", sentBreakpoint.GetProperty("logMessage").GetString());
        var plainBreakpoint = breakpoints.RootElement.GetProperty("arguments").GetProperty("breakpoints")[1];
        Assert.False(plainBreakpoint.TryGetProperty("condition", out _));
        Assert.False(plainBreakpoint.TryGetProperty("hitCondition", out _));
        Assert.False(plainBreakpoint.TryGetProperty("logMessage", out _));
        await RespondAsync(connection, breakpoints, new
        {
            breakpoints = new[]
            {
                new { id = 71, verified = false, line = 7, column = 1, message = "pending" },
                new { id = 72, verified = false, line = 10, column = 2, message = "no executable code" },
            },
        });
        using var configured = await connection.ReadClientMessageAsync();
        await connection.WriteAdapterMessageAsync(new
        {
            seq = 13,
            type = "event",
            @event = "breakpoint",
            body = new
            {
                reason = "changed",
                breakpoint = new { id = 71, verified = true, line = 8, column = 4, message = "moved to executable line" },
            },
        });
        await RespondAsync(connection, configured, new { });
        await RespondAsync(connection, launch, new { });
        await connection.WriteAdapterMessageAsync(new
        {
            seq = 14, type = "event", @event = "stopped", body = new { reason = "breakpoint", threadId = 5 },
        });
        using var threads = await connection.ReadClientMessageAsync();
        await RespondAsync(connection, threads, new { threads = new[] { new { id = 5, name = "main" } } });
        using var stack = await connection.ReadClientMessageAsync();
        await RespondAsync(connection, stack, new
        {
            stackFrames = new[]
            {
                new { id = 42, name = "main", source = new { path = Path.Combine(Path.GetTempPath(), "main.go") }, line = 7, column = 3 },
            },
        });
        using var scopes = await connection.ReadClientMessageAsync();
        await RespondAsync(connection, scopes, new
        {
            scopes = new[] { new { name = "Locals", variablesReference = 99, expensive = false } },
        });
        using var variables = await connection.ReadClientMessageAsync();
        await RespondAsync(connection, variables, new
        {
            variables = new[] { new { name = "answer", value = "42", type = "int", variablesReference = 0 } },
        });
        using var evaluate = await connection.ReadClientMessageAsync();
        Assert.Equal("evaluate", evaluate.RootElement.GetProperty("command").GetString());
        Assert.Equal("answer + 1", evaluate.RootElement.GetProperty("arguments").GetProperty("expression").GetString());
        Assert.Equal(42, evaluate.RootElement.GetProperty("arguments").GetProperty("frameId").GetInt32());
        Assert.Equal("watch", evaluate.RootElement.GetProperty("arguments").GetProperty("context").GetString());
        await RespondAsync(connection, evaluate, new { result = "43", type = "int", variablesReference = 0 });
        using var repl = await connection.ReadClientMessageAsync();
        Assert.Equal("evaluate", repl.RootElement.GetProperty("command").GetString());
        Assert.Equal("repl", repl.RootElement.GetProperty("arguments").GetProperty("context").GetString());
        await RespondAsync(connection, repl, new { result = "42", type = "int", variablesReference = 0 });
        using var setVariable = await connection.ReadClientMessageAsync();
        Assert.Equal("setVariable", setVariable.RootElement.GetProperty("command").GetString());
        Assert.Equal(99, setVariable.RootElement.GetProperty("arguments").GetProperty("variablesReference").GetInt32());
        Assert.Equal("answer", setVariable.RootElement.GetProperty("arguments").GetProperty("name").GetString());
        Assert.Equal("50", setVariable.RootElement.GetProperty("arguments").GetProperty("value").GetString());
        await RespondAsync(connection, setVariable, new { value = "50", type = "int", variablesReference = 0 });
        using var dataInfo = await connection.ReadClientMessageAsync();
        Assert.Equal("dataBreakpointInfo", dataInfo.RootElement.GetProperty("command").GetString());
        Assert.Equal(99, dataInfo.RootElement.GetProperty("arguments").GetProperty("variablesReference").GetInt32());
        Assert.Equal(42, dataInfo.RootElement.GetProperty("arguments").GetProperty("frameId").GetInt32());
        await RespondAsync(connection, dataInfo, new
        {
            dataId = "local.answer",
            description = "answer",
            accessTypes = new[] { "write", "readWrite" },
            canPersist = false,
        });
        using var setDataBreakpoints = await connection.ReadClientMessageAsync();
        Assert.Equal("setDataBreakpoints", setDataBreakpoints.RootElement.GetProperty("command").GetString());
        var dataBreakpoint = setDataBreakpoints.RootElement.GetProperty("arguments").GetProperty("breakpoints")[0];
        Assert.Equal("local.answer", dataBreakpoint.GetProperty("dataId").GetString());
        Assert.Equal("write", dataBreakpoint.GetProperty("accessType").GetString());
        Assert.Equal("answer > 0", dataBreakpoint.GetProperty("condition").GetString());
        Assert.Equal(">= 2", dataBreakpoint.GetProperty("hitCondition").GetString());
        await RespondAsync(connection, setDataBreakpoints, new
        {
            breakpoints = new[] { new { id = 81, verified = true } },
        });
        using var gotoTargets = await connection.ReadClientMessageAsync();
        Assert.Equal("gotoTargets", gotoTargets.RootElement.GetProperty("command").GetString());
        Assert.Equal(Path.GetFullPath("generated.go"),
            gotoTargets.RootElement.GetProperty("arguments").GetProperty("source").GetProperty("path").GetString());
        Assert.Equal(99, gotoTargets.RootElement.GetProperty("arguments").GetProperty("line").GetInt32());
        Assert.Equal(6, gotoTargets.RootElement.GetProperty("arguments").GetProperty("column").GetInt32());
        await RespondAsync(connection, gotoTargets, new
        {
            targets = new[] { new { id = 91, label = "line 12", line = 12, column = 4 } },
        });
        using var gotoRequest = await connection.ReadClientMessageAsync();
        Assert.Equal("goto", gotoRequest.RootElement.GetProperty("command").GetString());
        Assert.Equal(5, gotoRequest.RootElement.GetProperty("arguments").GetProperty("threadId").GetInt32());
        Assert.Equal(91, gotoRequest.RootElement.GetProperty("arguments").GetProperty("targetId").GetInt32());
        await RespondAsync(connection, gotoRequest, new { });
        using var disconnect = await connection.ReadClientMessageAsync();
        await RespondAsync(connection, disconnect, new { });
    }

    private static async Task RunAttachAdapterAsync(DapProtocolClientTests.InMemoryDapConnection connection)
    {
        using var initialize = await connection.ReadClientMessageAsync();
        await RespondAsync(connection, initialize, new { supportsConfigurationDoneRequest = true });
        using var attach = await connection.ReadClientMessageAsync();
        Assert.Equal("attach", attach.RootElement.GetProperty("command").GetString());
        Assert.Equal(4242, attach.RootElement.GetProperty("arguments").GetProperty("processId").GetInt32());
        await connection.WriteAdapterMessageAsync(new
        {
            seq = 20, type = "event", @event = "initialized", body = new { },
        });
        using var configured = await connection.ReadClientMessageAsync();
        Assert.Equal("configurationDone", configured.RootElement.GetProperty("command").GetString());
        await RespondAsync(connection, configured, new { });
        await RespondAsync(connection, attach, new { });
        using var disconnect = await connection.ReadClientMessageAsync();
        Assert.False(disconnect.RootElement.GetProperty("arguments").GetProperty("terminateDebuggee").GetBoolean());
        await RespondAsync(connection, disconnect, new { });
    }

    private static Task RespondAsync(
        DapProtocolClientTests.InMemoryDapConnection connection,
        JsonDocument request,
        object body) => connection.WriteAdapterMessageAsync(new
        {
            seq = 100,
            type = "response",
            request_seq = request.RootElement.GetProperty("seq").GetInt32(),
            success = true,
            command = request.RootElement.GetProperty("command").GetString(),
            body,
        });

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var timeout = DateTimeOffset.UtcNow.AddSeconds(2);
        while (!condition())
        {
            if (DateTimeOffset.UtcNow >= timeout)
                throw new TimeoutException("DAP session did not reach the expected state.");
            await Task.Delay(10);
        }
    }

    private sealed class RunToMapper : IDebugSourceMapper
    {
        public DebugStackFrame MapFrame(DebugStackFrame frame) => frame;

        public IReadOnlyDictionary<string, IReadOnlyList<DebugSourceBreakpoint>> MapBreakpoints(
            string? target,
            string sourcePath,
            IReadOnlyList<DebugSourceBreakpoint> breakpoints)
        {
            if (breakpoints.Count == 1 && breakpoints[0].Line == 12)
                return new Dictionary<string, IReadOnlyList<DebugSourceBreakpoint>>(StringComparer.OrdinalIgnoreCase)
                {
                    [Path.GetFullPath("generated.go")] =
                        [new DebugSourceBreakpoint(Path.GetFullPath("generated.go"), 99, 6)],
                };
            return new Dictionary<string, IReadOnlyList<DebugSourceBreakpoint>>(StringComparer.OrdinalIgnoreCase)
            {
                [sourcePath] = breakpoints,
            };
        }
    }
}

using Junexis.Agent.Abstractions;
using Junexis.Mcp.Studio.Abstractions;
using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Host;

/// <summary>Publishes process-boundary Agent events to Studio Output.</summary>
public sealed class AgentEventOutputConsumer : IAgentEventConsumer
{
    private readonly IOutput _output;

    /// <summary>Initializes the consumer over Studio Output.</summary>
    public AgentEventOutputConsumer(IOutput output) => _output = output ?? throw new ArgumentNullException(nameof(output));

    /// <inheritdoc />
    public Task ConsumeAsync(AgentEvent agentEvent, CancellationToken cancellationToken)
    {
        _output.Write(
            "Agent",
            $"Event: {agentEvent.Kind} — {agentEvent.ToolName ?? agentEvent.Status ?? agentEvent.Goal}",
            OutputSeverity.Info,
            "Junexis.Agent.Events");

        if (agentEvent.Report is not null)
        {
            _output.Write(
                "Agent",
                $"Report: {agentEvent.Report.Status} — {agentEvent.Report.Steps.Count} steps; changed files: {agentEvent.Report.ChangedFiles.Count}",
                OutputSeverity.Info,
                "Junexis.Agent.Report");
        }

        return Task.CompletedTask;
    }
}

using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Host;

/// <summary>Applies one managed Narrative runtime context to Run and DAP launch requests.</summary>
internal static class NarrativeRuntimeLaunch
{
    private static readonly TimeSpan PreparationTimeout = TimeSpan.FromSeconds(3);

    public static ProcessRequest Attach(IHostContext context, ProcessRequest request)
    {
        var environment = Resolve(context);
        if (environment is null)
            return request;

        var variables = new Dictionary<string, string?>(
            request.EnvironmentVariables,
            StringComparer.Ordinal);
        foreach (var pair in environment.ToProcessVariables())
            variables[pair.Key] = pair.Value;

        context.Output.Write(
            "Narrative",
            $"Run attached to managed Narrative build {ShortId(environment.BuildId)}.");
        return request with { EnvironmentVariables = variables };
    }

    public static void AttachToDebugLaunch(
        IHostContext context,
        IDictionary<string, object?> launchArguments)
    {
        var environment = Resolve(context);
        if (environment is null)
            return;

        launchArguments["env"] = environment.ToDebugAdapterVariables();
        context.Output.Write(
            "Narrative",
            $"Debug launch attached to managed Narrative build {ShortId(environment.BuildId)}.");
    }

    public static async Task<ProcessRequest> AttachAsync(
        IHostContext context,
        ProcessRequest request,
        CancellationToken cancellationToken)
    {
        await PrepareAsync(context, cancellationToken).ConfigureAwait(false);
        return Attach(context, request);
    }

    public static bool IsPreparationPending(IHostContext context) =>
        context.Capabilities.TryGet<INarrativeRuntimeEnvironmentService>(out var service)
        && service?.IsPreparationPending == true;

    public static async Task PrepareDebugAsync(
        IHostContext context,
        DebugConfigurationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request != DebugConfigurationRequest.Launch)
            return;
        await PrepareAsync(context, cancellationToken).ConfigureAwait(false);
    }

    private static async Task PrepareAsync(
        IHostContext context,
        CancellationToken cancellationToken)
    {
        if (!context.Capabilities.TryGet<INarrativeRuntimeEnvironmentService>(out var service)
            || service is null
            || !service.IsPreparationPending
            || service.GetCurrent() is not null)
        {
            return;
        }

        context.Output.Write(
            "Narrative",
            "Preparing managed Narrative session before launch…");
        var environment = await service
            .WaitForCurrentAsync(PreparationTimeout, cancellationToken)
            .ConfigureAwait(false);
        if (environment is null)
        {
            context.Output.Write(
                "Narrative",
                "Narrative was not ready before launch; continuing without runtime tracing.",
                OutputSeverity.Warning);
        }
    }

    private static NarrativeRuntimeEnvironment? Resolve(IHostContext context) =>
        context.Capabilities.TryGet<INarrativeRuntimeEnvironmentService>(out var service)
            ? service?.GetCurrent()
            : null;

    private static string ShortId(string value) => value.Length <= 12 ? value : value[..12];
}

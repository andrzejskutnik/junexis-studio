using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Host;

/// <summary>
/// Rozszerzenie Problems: rejestruje panel narzędziowy "problems".
/// </summary>
internal sealed class ProblemsExtension : IJunexisExtension
{
    private ProblemsPanelViewModel? _viewModel;

    /// <inheritdoc />
    public string Id => "junexis.problems";

    /// <inheritdoc />
    public void Initialize(IHostContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        context.Capabilities.TryGet<IAnalysisSnapshotService>(out var analysis);
        var viewModel = new ProblemsPanelViewModel(
            context.Diagnostics,
            context.EventBus,
            analysis,
            context.Preferences);
        _viewModel = viewModel;
        context.Panels.RegisterPanel(
            "problems",
            PanelType.Tool,
            () => viewModel,
            title: "Problems");
        context.Output.Write("Extensions", "ProblemsExtension loaded");
    }

    /// <inheritdoc />
    public void Shutdown()
    {
        _viewModel?.Dispose();
        _viewModel = null;
    }
}

using Avalonia.Controls;
using Avalonia.Interactivity;
using Junexis.Studio.Host;

namespace Junexis.Studio.Shell;

/// <summary>Widok cyklu instalacji delegowanego do publicznego CLI Junexis.</summary>
public partial class PlatformLifecycleView : UserControl
{
    /// <summary>Inicjalizuje widok cyklu platformy.</summary>
    public PlatformLifecycleView() => InitializeComponent();

    private async void PlanUpdate_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PlatformLifecycleViewModel viewModel) await viewModel.PlanUpdateAsync();
    }

    private async void History_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PlatformLifecycleViewModel viewModel) await viewModel.RefreshHistoryAsync();
    }

    private async void TakeOver_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PlatformLifecycleViewModel viewModel) await viewModel.TakeOverInterruptedAsync();
    }

    private async void CheckRepair_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PlatformLifecycleViewModel viewModel) await viewModel.CheckRepairAsync();
    }

    private async void ApplyRepair_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PlatformLifecycleViewModel viewModel) await viewModel.ApplyRepairAsync();
    }

    private async void PlanRollback_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PlatformLifecycleViewModel viewModel) await viewModel.PlanRollbackAsync();
    }

    private async void ApplyRollback_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PlatformLifecycleViewModel viewModel) await viewModel.ApplyRollbackAsync();
    }

    private void Cancel_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PlatformLifecycleViewModel viewModel) viewModel.Cancel();
    }
}

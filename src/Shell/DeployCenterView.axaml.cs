using Avalonia.Controls;

namespace Junexis.Studio.Shell;

/// <summary>View for the Junexis Deploy Center panel.</summary>
public partial class DeployCenterView : UserControl
{
    /// <summary>Initializes the Deploy Center view.</summary>
    public DeployCenterView()
    {
        InitializeComponent();
    }

    private async void Refresh_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is Host.DeployCenterViewModel viewModel)
            await viewModel.RefreshAsync();
    }

    private async void Prepare_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is Host.DeployCenterViewModel viewModel)
            await viewModel.PrepareAsync();
    }

    private async void RunLocal_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is Host.DeployCenterViewModel viewModel)
            await viewModel.RunLocalAsync();
    }

    private async void StopLocal_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is Host.DeployCenterViewModel viewModel)
            await viewModel.StopLocalAsync();
    }

    private async void AdoptLive_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is Host.DeployCenterViewModel viewModel)
            await viewModel.AdoptLiveAsync();
    }

    private async void ProbeEmbedded_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is Host.DeployCenterViewModel viewModel)
            await viewModel.ProbeEmbeddedAsync();
    }

    private async void PrepareEmbedded_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is Host.DeployCenterViewModel viewModel)
            await viewModel.PrepareEmbeddedAsync();
    }

    private async void SelectEmbedded_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is Host.DeployCenterViewModel viewModel)
            await viewModel.SelectEmbeddedAsync();
    }

    private async void FlashEmbedded_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is Host.DeployCenterViewModel viewModel)
            await viewModel.FlashEmbeddedAsync();
    }
}

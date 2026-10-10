using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Junexis.Studio.Host;

namespace Junexis.Studio.Shell;

/// <summary>Widok stanów serwerów LSP i wyników Find References.</summary>
public partial class LanguageServicesView : UserControl
{
    /// <summary>Tworzy widok panelu Language Services.</summary>
    public LanguageServicesView()
    {
        InitializeComponent();
    }

    private void Refresh_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is LanguageServicesViewModel viewModel)
            viewModel.RefreshStatuses();
    }

    private async void Restart_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is LanguageServicesViewModel viewModel)
            await viewModel.RestartSelectedAsync();
    }

    private void Navigate_OnClick(object? sender, RoutedEventArgs e) => Navigate();

    private void Clear_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is LanguageServicesViewModel viewModel)
            viewModel.ClearReferences();
    }

    private void References_OnDoubleTapped(object? sender, TappedEventArgs e) => Navigate();

    private void Navigate()
    {
        if (DataContext is LanguageServicesViewModel viewModel)
            viewModel.NavigateToSelectedReference();
    }
}

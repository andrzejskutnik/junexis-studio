using Avalonia.Controls;
using Avalonia.Interactivity;
using Junexis.Studio.Host;

namespace Junexis.Studio.Shell;

/// <summary>Displays the availability of external Studio toolchains.</summary>
public partial class ToolchainDoctorView : UserControl
{
    /// <summary>Initializes the toolchain doctor view.</summary>
    public ToolchainDoctorView()
    {
        InitializeComponent();
    }

    private async void Refresh_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ToolchainDoctorViewModel viewModel)
            await viewModel.RefreshAsync();
    }
}

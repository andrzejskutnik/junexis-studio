using Avalonia.Controls;
using Avalonia.Interactivity;
using Junexis.Studio.Host;

namespace Junexis.Studio.Shell;

/// <summary>
/// Okno preferencji JunexisStudio.
/// </summary>
public partial class PreferencesWindow : Window
{
    /// <summary>
    /// Tworzy okno preferencji.
    /// </summary>
    public PreferencesWindow()
    {
        InitializeComponent();
        Icon = AppIconFactory.Icon;
    }

    private void OnSaveClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PreferencesViewModel vm)
            vm.SaveFromFields();
        Close(true);
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e) => Close(false);
}

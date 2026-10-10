using Avalonia.Controls;
using Junexis.Studio.Abstractions;
using Junexis.Studio.Host;

namespace Junexis.Studio.Shell;

/// <summary>
/// Widok panelu Problems.
/// </summary>
public partial class ProblemsPanelView : UserControl
{
    /// <summary>
    /// Tworzy widok i ładuje XAML.
    /// </summary>
    public ProblemsPanelView()
    {
        InitializeComponent();
    }

    private void OnProblemDoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (DataContext is not ProblemsPanelViewModel vm)
        {
            return;
        }

        if (ProblemsList.SelectedItem is ProblemItemViewModel item)
        {
            vm.NavigateTo(item.Diagnostic);
        }
    }

    private void OnOpenMentorReportClick(
        object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is ProblemsPanelViewModel vm)
            vm.OpenMentorReport();
        e.Handled = true;
    }
}

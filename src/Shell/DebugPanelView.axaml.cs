using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Junexis.Studio.Abstractions;
using Junexis.Studio.Host;

namespace Junexis.Studio.Shell;

/// <summary>Displays DAP controls, breakpoints, stack frames, scopes, variables, and watches.</summary>
public partial class DebugPanelView : UserControl
{
    /// <summary>Initializes the debugger panel view.</summary>
    public DebugPanelView() => InitializeComponent();

    private async void Continue_OnClick(object? sender, RoutedEventArgs e) => await ViewModel.ContinueAsync();
    private async void Pause_OnClick(object? sender, RoutedEventArgs e) => await ViewModel.PauseAsync();
    private async void Next_OnClick(object? sender, RoutedEventArgs e) => await ViewModel.NextAsync();
    private async void StepIn_OnClick(object? sender, RoutedEventArgs e) => await ViewModel.StepInAsync();
    private async void StepOut_OnClick(object? sender, RoutedEventArgs e) => await ViewModel.StepOutAsync();
    private async void Stop_OnClick(object? sender, RoutedEventArgs e) => await ViewModel.StopAsync();
    private void ExplainStoppedFrame_OnClick(object? sender, RoutedEventArgs e) => ViewModel.ExplainStoppedFrame();
    private async void ToggleBreakpoint_OnClick(object? sender, RoutedEventArgs e) => await ViewModel.ToggleBreakpointAsync();
    private async void ApplyBreakpointOptions_OnClick(object? sender, RoutedEventArgs e) =>
        await ViewModel.ApplyBreakpointOptionsAsync();
    private async void ApplyExceptionBreakpoints_OnClick(object? sender, RoutedEventArgs e) =>
        await ViewModel.ApplyExceptionBreakpointsAsync();
    private async void SetVariable_OnClick(object? sender, RoutedEventArgs e) =>
        await ViewModel.SetSelectedVariableAsync();
    private async void ExecuteDebugConsole_OnClick(object? sender, RoutedEventArgs e) =>
        await ViewModel.ExecuteDebugConsoleAsync();
    private async void AddDataBreakpoint_OnClick(object? sender, RoutedEventArgs e) =>
        await ViewModel.AddDataBreakpointAsync();
    private async void RemoveDataBreakpoint_OnClick(object? sender, RoutedEventArgs e) =>
        await ViewModel.RemoveDataBreakpointAsync();
    private async void StartConfiguration_OnClick(object? sender, RoutedEventArgs e) =>
        await ViewModel.StartSelectedConfigurationAsync();
    private void NewConfiguration_OnClick(object? sender, RoutedEventArgs e) => ViewModel.NewDebugConfiguration();
    private void SaveConfiguration_OnClick(object? sender, RoutedEventArgs e) => ViewModel.SaveDebugConfiguration();
    private void RemoveConfiguration_OnClick(object? sender, RoutedEventArgs e) => ViewModel.RemoveSelectedDebugConfiguration();
    private void RefreshProcesses_OnClick(object? sender, RoutedEventArgs e) => ViewModel.RefreshProcesses();
    private void UseRecommendedProfile_OnClick(object? sender, RoutedEventArgs e) =>
        ViewModel.UseRecommendedDebugProfile();
    private async void AddWatch_OnClick(object? sender, RoutedEventArgs e) => await ViewModel.AddWatchAsync();
    private void RemoveWatch_OnClick(object? sender, RoutedEventArgs e)
    {
        if (WatchesTree.SelectedItem is not DebugInspectionNode node)
            return;
        ViewModel.RemoveWatch(ViewModel.Watches.FirstOrDefault(watch =>
            watch.Expression.Equals(node.Name, StringComparison.Ordinal)));
    }
    private async void WatchExpression_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;
        e.Handled = true;
        await ViewModel.AddWatchAsync();
    }

    private async void DebugConsole_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Up)
        {
            e.Handled = true;
            ViewModel.PreviousDebugConsoleCommand();
        }
        else if (e.Key == Key.Down)
        {
            e.Handled = true;
            ViewModel.NextDebugConsoleCommand();
        }
        else if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await ViewModel.ExecuteDebugConsoleAsync();
        }
    }

    private async void InspectionNode_OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        var control = e.Source as Control;
        while (control is not null && control.DataContext is not DebugInspectionNode)
            control = control.Parent as Control;
        var node = control?.DataContext as DebugInspectionNode
                   ?? (sender as TreeView)?.SelectedItem as DebugInspectionNode;
        await ViewModel.ExpandNodeAsync(node);
    }

    private async void Frame_OnDoubleTapped(object? sender, TappedEventArgs e) =>
        await ViewModel.SelectFrameAsync(FramesList.SelectedItem as DebugStackFrame);

    private async void Scope_OnDoubleTapped(object? sender, TappedEventArgs e) =>
        await ViewModel.SelectScopeAsync(ScopesList.SelectedItem as DebugScope);

    private DebugPanelViewModel ViewModel =>
        DataContext as DebugPanelViewModel
        ?? throw new InvalidOperationException("DebugPanelView requires DebugPanelViewModel DataContext.");
}

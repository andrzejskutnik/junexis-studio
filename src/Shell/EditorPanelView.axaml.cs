using Avalonia.Controls;
using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;
using Junexis.Studio.Host;
using System;

namespace Junexis.Studio.Shell;

/// <summary>
/// Widok kontenera zakładek edytora — TabControl z przyciskami zamykania.
/// Logika synchronizacji AvaloniaEdit przeniesiona do <see cref="DocumentTabView"/>.
/// </summary>
public partial class EditorPanelView : UserControl
{
    private const string OpenFolderCommandId = "workspace.openFolder";
    private const string CloneRepositoryCommandId = "workspace.cloneRepository";
    private const string QuickOpenCommandId = "shell.quickOpen";
    private const string CommandPaletteCommandId = "shell.commandPalette";
    private const string ToolchainCommandId = "toolchains.doctor.refresh";
    private const string HelpCenterCommandId = "help.gettingStarted";
    private readonly WorkspaceHistoryStore _workspaceHistory = new();
    private IHostContext? _hostContext;

    /// <summary>
    /// Tworzy widok kontenera zakładek i ładuje XAML.
    /// </summary>
    public EditorPanelView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var context = TopLevel.GetTopLevel(this)?.DataContext as IHostContext;
        if (ReferenceEquals(context, _hostContext))
        {
            RefreshWelcome();
            return;
        }

        UnsubscribeWorkspaceEvents();
        _hostContext = context;
        _hostContext?.EventBus.Subscribe<WorkspaceOpenedEvent>(OnWorkspaceOpened);
        RefreshWelcome();
    }

    private void OnUnloaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        UnsubscribeWorkspaceEvents();
    }

    private void OnWorkspaceOpened(WorkspaceOpenedEvent e)
    {
        RefreshWelcome();
    }

    private void RefreshWelcome()
    {
        var state = WorkspaceWelcomeState.Create(
            _hostContext?.Workspace.RootPath,
            _workspaceHistory.LoadRecentFolders());

        WorkspaceName.Text = state.WorkspaceName;
        WorkspacePath.Text = state.WorkspacePath ?? "Open a folder or clone a repository to begin.";
        WorkspaceFolderMarker.IsVisible = !state.IsGitRepository;
        WorkspaceGitMarker.IsVisible = state.IsGitRepository;
        RecentWorkspaces.ItemsSource = state.RecentWorkspaces;
        RecentSection.IsVisible = state.RecentWorkspaces.Count > 0;
    }

    private void UnsubscribeWorkspaceEvents()
    {
        _hostContext?.EventBus.Unsubscribe<WorkspaceOpenedEvent>(OnWorkspaceOpened);
        _hostContext = null;
    }

    private void ExecuteCommand(string commandId)
    {
        var context = _hostContext ?? TopLevel.GetTopLevel(this)?.DataContext as IHostContext;
        if (context?.Commands.CanExecute(commandId) == true)
        {
            context.Commands.Execute(commandId);
        }
    }

    private void ActivatePanel(string panelId)
    {
        _hostContext?.EventBus.Publish(new ActivatePanelRequested(panelId));
    }

    private void OnOpenFolderClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        ExecuteCommand(OpenFolderCommandId);

    private void OnCloneRepositoryClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        ExecuteCommand(CloneRepositoryCommandId);

    private void OnQuickOpenClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        ExecuteCommand(QuickOpenCommandId);

    private void OnCommandPaletteClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        ExecuteCommand(CommandPaletteCommandId);

    private void OnToolchainsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        ActivatePanel("junexis.toolchains.doctor");
        ExecuteCommand(ToolchainCommandId);
    }

    private void OnAnalysisClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        ActivatePanel("junexis.analysis.explorer");

    private void OnNarrativeClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        ActivatePanel("junexis.narrative.why");

    private void OnHelpCenterClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        ExecuteCommand(HelpCenterCommandId);

    private async void OnOpenRecentClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button { Tag: string folderPath }
            && TopLevel.GetTopLevel(this) is MainWindow window)
        {
            await window.OpenWorkspaceFolderAsync(folderPath);
        }
    }

    /// <summary>
    /// Obsługuje kliknięcie przycisku zamknięcia zakładki.
    /// </summary>
    private void OnCloseTabClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (GetTabFromSender(sender) is { } tab)
            tab.RequestClose();
    }

    private async void OnCloseOtherTabsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (GetTabFromSender(sender) is { } tab && DataContext is EditorPanelViewModel vm)
            await vm.CloseOtherTabsAsync(tab);
    }

    private async void OnCloseTabsToRightClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (GetTabFromSender(sender) is { } tab && DataContext is EditorPanelViewModel vm)
            await vm.CloseTabsToTheRightAsync(tab);
    }

    private void OnCopyTabPathClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (GetTabFromSender(sender) is { FilePath: { } path })
            TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync(path);
    }

    private static DocumentTabViewModel? GetTabFromSender(object? sender) =>
        sender is MenuItem { DataContext: DocumentTabViewModel tab } ? tab :
        sender is Button { DataContext: DocumentTabViewModel tab2 } ? tab2 : null;
}

/// <summary>Converts bool IsPreview to FontStyle: true = Italic, false = Normal.</summary>
public sealed class BoolToFontStyleConverter : Avalonia.Data.Converters.IValueConverter
{
    /// <summary>Singleton instance.</summary>
    public static readonly BoolToFontStyleConverter Instance = new();

    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is true ? Avalonia.Media.FontStyle.Italic : Avalonia.Media.FontStyle.Normal;

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;
using Junexis.Studio.Host;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Junexis.Studio.Shell;

/// <summary>
/// Główne okno powłoki (Shell).
/// </summary>
public partial class MainWindow : Window
{
    private static readonly string[] TopLevelMenuOrder =
        ["File", "Edit", "Refactor", "Navigate", "View", "Workspace", "Build", "Run", "Debug", "Deploy", "Tools", "Agent", "VCS", "Help"];
    private static readonly string[] FileMenuOrder =
    [
        "New File in Workspace...",
        "New Folder in Workspace...",
        "Open Folder...",
        "Clone Repository...",
        "Save",
        "Close Tab",
        "Copy File Path",
        "Copy Workspace Path",
        "Reveal in File Manager",
        "Rename Selected Item...",
        "Move Selected Item to...",
        "Delete Selected Item",
        "Refresh Explorer",
        "Preferences",
        "Workspace Preferences",
    ];
    private static readonly string[][] FileMenuGroups =
    [
        ["New File in Workspace...", "New Folder in Workspace..."],
        ["Open Folder...", "Clone Repository..."],
        ["Save", "Close Tab"],
        ["Copy File Path", "Copy Workspace Path", "Reveal in File Manager"],
        ["Rename Selected Item...", "Move Selected Item to...", "Delete Selected Item"],
        ["Refresh Explorer"],
        ["Preferences", "Workspace Preferences"],
    ];
    private static readonly string[] EditMenuOrder =
    [
        "Undo",
        "Redo",
        "Cut",
        "Copy",
        "Paste",
        "Select All",
        "Select Line",
        "Go to Line...",
        "Find",
        "Find Next",
        "Find Previous",
        "Replace",
        "Replace Next",
        "Replace All",
    ];
    private static readonly string[][] EditMenuGroups =
    [
        ["Undo", "Redo"],
        ["Cut", "Copy", "Paste"],
        ["Select All", "Select Line"],
        ["Go to Line..."],
        ["Find", "Find Next", "Find Previous"],
        ["Replace", "Replace Next", "Replace All"],
    ];
    private static readonly string[] RunMenuOrder =
    [
        "Build",
        "Run",
        "Stop Build",
        "Toggle Auto-Build",
        "Build (g++)",
        "Run (g++)",
        "Stop Build (g++)",
        "Build (dotnet)",
        "Run (dotnet)",
        "Stop Build (dotnet)",
        "Build (go)",
        "Run (go)",
        "Stop Build (go)",
    ];
    private static readonly string[][] RunMenuGroups =
    [
        ["Build", "Run", "Stop Build", "Toggle Auto-Build"],
        ["Build (g++)", "Run (g++)", "Stop Build (g++)"],
        ["Build (dotnet)", "Run (dotnet)", "Stop Build (dotnet)"],
        ["Build (go)", "Run (go)", "Stop Build (go)"],
    ];
    private static readonly string[] DebugMenuOrder =
    [
        "Start Debugging",
        "Run to Cursor",
        "Toggle Breakpoint",
        "Run and Debug",
    ];
    private static readonly string[][] DebugMenuGroups =
    [
        ["Start Debugging", "Run to Cursor", "Toggle Breakpoint"],
        ["Run and Debug"],
    ];
    private static readonly string[] HelpMenuOrder =
    [
        "Getting Started",
        "Keyboard Shortcuts",
        "Build, Run and Debug",
        "Git Workflow",
        "Troubleshooting",
    ];
    private const string DefaultExplorerPanelId = "explorer";
    private const string DefaultEditorPanelId = "editor";
    private const string DefaultOutputPanelId = "junexis.output.filtered";
    private const string OpenFolderCommandId = "workspace.openFolder";
    private const string CloneRepositoryCommandId = "workspace.cloneRepository";
    private const string CommandPaletteCommandId = "shell.commandPalette";
    private const string QuickOpenCommandId = "shell.quickOpen";
    private const string ExplorerViewCommandId = "view.explorer";
    private const string SearchViewCommandId = "view.search";
    private const string ProblemsViewCommandId = "view.problems";
    private const string SourceControlViewCommandId = "view.sourceControl";
    private const string RunAndDebugViewCommandId = "view.runAndDebug";
    private const string NarrativeViewCommandId = "view.narrative";
    private const int ExplorerColumnIndex = 1;
    private const int RightPanelColumnIndex = 5;
    private const int ToolTabsRowIndex = 4;
    private const string WorkspaceSearchPanelId = "junexis.workspace.search";

    private EditorPanelViewModel? _statusEditorVm;
    private readonly ShellLayoutStore _layoutStore = new();
    private readonly EditorClipboardService _editorClipboard = new();
    private double _lastExplorerWidth = ShellLayoutData.Default.ExplorerWidth;
    private double _lastRightPanelWidth = ShellLayoutData.Default.RightPanelWidth;
    private string? _activeActivity = "explorer";
    private string _sidebarMode = "explorer";
    private IDiagnosticService? _badgeDiagnostics;
    private IVcsService? _badgeVcs;
    private IEventBus? _badgeEventBus;
    private Action<VcsStatusChangedEvent>? _vcsBadgeHandler;

    /// <summary>
    /// Tworzy okno i ładuje XAML.
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();
        Icon = AppIconFactory.Icon;
    }

    /// <summary>
    /// Po otwarciu okna przywraca zapisane rozmiary paneli.
    /// </summary>
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        ApplyLayout(_layoutStore.Load());
        if (DataContext is IHostContext context
            && ShellChromePolicy.UseCompactEmptyWorkspace(context.Workspace.RootPath))
        {
            HideSidebar(MainGrid.ColumnDefinitions[ExplorerColumnIndex]);
            HideRightPanel();
        }
    }

    private void ApplyLayout(ShellLayoutData data)
    {
        _lastExplorerWidth = data.ExplorerWidth;
        _lastRightPanelWidth = data.RightPanelWidth;
        MainGrid.ColumnDefinitions[ExplorerColumnIndex].Width = new GridLength(data.ExplorerWidth, GridUnitType.Pixel);
        MainGrid.RowDefinitions[ToolTabsRowIndex].Height = new GridLength(data.ToolTabsHeight, GridUnitType.Pixel);
        MainGrid.ColumnDefinitions[RightPanelColumnIndex].Width = RightTabsHost.ItemCount > 0
            ? new GridLength(data.RightPanelWidth, GridUnitType.Pixel)
            : new GridLength(0, GridUnitType.Pixel);
        SelectPanelById(ToolTabsHost, data.BottomPanelId);
        SelectPanelById(RightTabsHost, data.RightPanelId);
    }

    /// <summary>
    /// Reaguje na zmianę DataContext i wiąże panele z <see cref="IPanelRegistry"/>.
    /// </summary>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is not IHostContext context)
        {
            UnsubscribeActivityBadges();
            WorkbenchRunBar.DataContext = null;
            ExplorerHost.Content = "Explorer (no host context)";
            SearchHost.Content = "Search (no host context)";
            EditorHost.Content = "Editor (no host context)";
            ToolTabsHost.Items.Clear();
            ToolTabsHost.Items.Add(new TabItem
            {
                Header = "output",
                Content = new TextBlock { Text = "Tools (no host context)" },
            });
            RightTabsHost.Items.Clear();
            RightPanelHost.IsVisible = false;
            RightSplitter.IsVisible = false;
            MainMenu.Items.Clear();
            return;
        }

        RegisterShellCommands(context);
        WorkbenchRunBar.DataContext = context.Capabilities.TryGet<WorkbenchRunViewModel>(out var runViewModel)
            ? runViewModel
            : null;
        context.Capabilities.Register<IWorkspaceInteractionService>(new WorkspaceInteractionService(this));
        context.Capabilities.Register<IEditorClipboardService>(_editorClipboard);
        BindPanels(context);
        BindMenu(context);
        BindContextMenus(context);
        SubscribePreferencesWindow(context);
        SubscribeKeyboardShortcuts(context);
        SubscribeVcsBranch(context);
        SubscribePanelActivation(context);
        SubscribeActivityBadges(context);
        SubscribeBuildStatus(context);
        SubscribeDiagnosticsStatus(context);
        SetWorkspaceStatus(context.Workspace.RootPath);
    }

    private void RegisterShellCommands(IHostContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        context.Commands.Register(
            OpenFolderCommandId,
            () => _ = OpenFolderAsync(context),
            menuPath: "File/Open Folder...");
        context.Commands.Register(
            CloneRepositoryCommandId,
            () => _ = CloneRepositoryAsync(context),
            menuPath: "File/Clone Repository...");
        context.Commands.Register(
            CommandPaletteCommandId,
            () => _ = ShowCommandPaletteAsync(context),
            menuPath: null,
            displayName: "Show Command Palette",
            category: "View");
        context.Commands.Register(
            QuickOpenCommandId,
            () => _ = ShowQuickOpenAsync(context),
            menuPath: null,
            displayName: "Quick Open File",
            category: "File");
        context.Commands.Register(
            ExplorerViewCommandId,
            ToggleExplorer,
            menuPath: "View/Explorer",
            displayName: "Explorer",
            category: "View");
        context.Commands.Register(
            SearchViewCommandId,
            ToggleSearchSidebar,
            menuPath: "View/Search",
            displayName: "Search",
            category: "View");
        context.Commands.Register(
            ProblemsViewCommandId,
            () => ActivatePanel(context, "problems"),
            menuPath: "View/Problems",
            displayName: "Problems",
            category: "View");
        context.Commands.Register(
            SourceControlViewCommandId,
            () => ToggleRightPanel(context, "junexis.vcs.changes"),
            menuPath: "View/Source Control",
            displayName: "Source Control",
            category: "View");
        context.Commands.Register(
            RunAndDebugViewCommandId,
            () => ToggleRightPanel(context, "junexis.debugger.dap"),
            menuPath: "Debug/Run and Debug",
            displayName: "Run and Debug",
            category: "View");
        context.Commands.Register(
            NarrativeViewCommandId,
            () => ActivatePanel(context, "junexis.narrative.why"),
            menuPath: "View/Narrative Why",
            displayName: "Narrative: Why?",
            category: "View");
    }

    private void ToggleExplorer()
    {
        var column = MainGrid.ColumnDefinitions[ExplorerColumnIndex];
        var isVisible = column.Width.Value >= 80;
        if (isVisible && _activeActivity == "explorer" && _sidebarMode == "explorer")
        {
            HideSidebar(column);
            return;
        }

        ShowSidebar(column);
        _sidebarMode = "explorer";
        ExplorerHost.IsVisible = true;
        SearchHost.IsVisible = false;
        SetActiveActivity("explorer");
    }

    private void ToggleSearchSidebar()
    {
        var column = MainGrid.ColumnDefinitions[ExplorerColumnIndex];
        var isVisible = column.Width.Value >= 80;
        if (isVisible && _activeActivity == "search" && _sidebarMode == "search")
        {
            HideSidebar(column);
            return;
        }

        ShowSidebar(column);
        _sidebarMode = "search";
        ExplorerHost.IsVisible = false;
        SearchHost.IsVisible = true;
        SetActiveActivity("search");
        if (SearchHost.Content is WorkspaceSearchView view)
            Avalonia.Threading.Dispatcher.UIThread.Post(view.FocusQuery);
    }

    private void ShowSidebar(ColumnDefinition column)
    {
        if (column.Width.Value < 80)
        {
            column.Width = new GridLength(
                Math.Max(_lastExplorerWidth, ShellLayoutData.Default.ExplorerWidth),
                GridUnitType.Pixel);
        }
        ExplorerPanelHost.IsVisible = true;
        ExplorerSplitter.IsVisible = true;
    }

    private void HideSidebar(ColumnDefinition column)
    {
        if (column.Width.Value >= 80)
            _lastExplorerWidth = column.Width.Value;
        column.Width = new GridLength(0, GridUnitType.Pixel);
        ExplorerPanelHost.IsVisible = false;
        ExplorerSplitter.IsVisible = false;
        SetActiveActivity(null);
    }

    private static void ActivatePanel(IHostContext context, string panelId)
    {
        context.EventBus.Publish(new ActivatePanelRequested(panelId));
    }

    private void ToggleRightPanel(IHostContext context, string panelId)
    {
        var isSelected = RightTabsHost.SelectedItem is TabItem { Tag: string selectedId }
            && string.Equals(selectedId, panelId, StringComparison.Ordinal);
        if (isSelected && MainGrid.ColumnDefinitions[RightPanelColumnIndex].Width.Value >= 80)
        {
            HideRightPanel();
            return;
        }

        ActivatePanel(context, panelId);
    }

    private void ShowRightPanel()
    {
        if (RightTabsHost.ItemCount == 0)
            return;
        var column = MainGrid.ColumnDefinitions[RightPanelColumnIndex];
        if (column.Width.Value < 80)
        {
            column.Width = new GridLength(
                Math.Max(_lastRightPanelWidth, ShellLayoutData.Default.RightPanelWidth),
                GridUnitType.Pixel);
        }
        RightPanelHost.IsVisible = true;
        RightSplitter.IsVisible = true;
    }

    private void HideRightPanel()
    {
        var column = MainGrid.ColumnDefinitions[RightPanelColumnIndex];
        if (column.Width.Value >= 80)
            _lastRightPanelWidth = column.Width.Value;
        column.Width = new GridLength(0, GridUnitType.Pixel);
        RightPanelHost.IsVisible = false;
        RightSplitter.IsVisible = false;
        if (_activeActivity is "debug" or "sourceControl")
            SetActiveActivity(null);
    }

    private void ExecuteActivityCommand(string commandId)
    {
        if (DataContext is IHostContext context
            && context.Commands.CanExecute(commandId))
        {
            context.Commands.Execute(commandId);
        }
    }

    private void OnActivityExplorerClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        ExecuteActivityCommand(ExplorerViewCommandId);

    private void OnActivitySearchClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        ExecuteActivityCommand(SearchViewCommandId);

    private void OnActivityProblemsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        ExecuteActivityCommand(ProblemsViewCommandId);

    private void OnActivitySourceControlClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        ExecuteActivityCommand(SourceControlViewCommandId);

    private void OnActivityDebugClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        ExecuteActivityCommand(RunAndDebugViewCommandId);

    private void OnActivityNarrativeClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        ExecuteActivityCommand(NarrativeViewCommandId);

    private void OnActivityCommandPaletteClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        ExecuteActivityCommand(CommandPaletteCommandId);

    private void OnToolPanelSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is TabControl { SelectedItem: TabItem { Tag: string panelId } })
        {
            SetActiveActivityForPanel(panelId);
        }
    }

    private void SetActiveActivity(string? activity)
    {
        _activeActivity = activity;
        SetActivityButtonState(ActivityExplorerButton, activity == "explorer");
        SetActivityButtonState(ActivitySearchButton, activity == "search");
        SetActivityButtonState(ActivityProblemsButton, activity == "problems");
        SetActivityButtonState(ActivitySourceControlButton, activity == "sourceControl");
        SetActivityButtonState(ActivityDebugButton, activity == "debug");
        SetActivityButtonState(ActivityNarrativeButton, activity == "narrative");
    }

    private static void SetActivityButtonState(Button button, bool active)
    {
        if (active)
        {
            if (!button.Classes.Contains("active"))
            {
                button.Classes.Add("active");
            }
        }
        else
        {
            button.Classes.Remove("active");
        }
    }

    private void SetActiveActivityForPanel(string panelId)
    {
        var activity = panelId switch
        {
            "problems" => "problems",
            "junexis.vcs.changes" or "junexis.vcs.repository" => "sourceControl",
            "junexis.debugger.dap" => "debug",
            "junexis.narrative.why" => "narrative",
            _ => null,
        };
        SetActiveActivity(activity);
    }

    private async Task ShowCommandPaletteAsync(IHostContext context)
    {
        var items = QuickAccessCatalog.CreateCommands(context.Commands, context.Keybindings);
        var selected = await QuickAccessWindow.ShowAndGetResultAsync(
            this,
            "Command Palette",
            "Type a command name...",
            items);

        if (selected is not null && context.Commands.CanExecute(selected.Value))
        {
            context.Commands.Execute(selected.Value);
        }
    }

    private async Task ShowQuickOpenAsync(IHostContext context)
    {
        if (string.IsNullOrWhiteSpace(context.Workspace.RootPath)
            || !Directory.Exists(context.Workspace.RootPath))
        {
            await OpenFolderAsync(context);
            return;
        }

        var items = QuickAccessCatalog.CreateFiles(context.Workspace.RootPath);
        var selected = await QuickAccessWindow.ShowAndGetResultAsync(
            this,
            "Quick Open",
            "Type a file name or path...",
            items);

        if (selected is not null)
        {
            context.EventBus.Publish(new FileOpenRequested(selected.Value));
        }
    }

    private async Task OpenFolderAsync(IHostContext context)
    {
        if (context is null)
        {
            return;
        }

        if (StorageProvider is null)
        {
            context.Output.Write("Workspace", "Open Folder failed: no storage provider");
            return;
        }

        IReadOnlyList<IStorageFolder> folders;

        try
        {
            var homePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            IStorageFolder? homeFolder = null;
            if (!string.IsNullOrWhiteSpace(homePath))
            {
                homeFolder = await StorageProvider.TryGetFolderFromPathAsync(homePath);
            }

            folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Open Folder",
                AllowMultiple = false,
                SuggestedStartLocation = homeFolder,
            });
        }
        catch (Exception ex)
        {
            context.Output.Write("Workspace", $"Open Folder failed: {ex.Message}");
            return;
        }

        var folder = folders.FirstOrDefault();
        if (folder is null)
        {
            return;
        }

        var localPath = folder.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(localPath))
        {
            context.Output.Write("Workspace", "Open Folder failed: selected folder has no local path");
            return;
        }

        await SwitchWorkspaceAsync(context, localPath);
    }

    /// <summary>
    /// Otwiera wskazany folder z ekranu ostatnich workspace, stosując ten sam
    /// bezpieczny przepływ co natywny dialog wyboru folderu.
    /// </summary>
    /// <param name="folderPath">Pełna ścieżka folderu.</param>
    /// <returns>Zadanie zakończone wartością <see langword="true"/>, gdy folder otwarto.</returns>
    internal async Task<bool> OpenWorkspaceFolderAsync(string folderPath)
    {
        if (DataContext is not IHostContext context)
        {
            return false;
        }

        return await SwitchWorkspaceAsync(context, folderPath);
    }

    private async Task<bool> SwitchWorkspaceAsync(IHostContext context, string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
        {
            context.Output.Write(
                "Workspace",
                $"Open Folder failed: folder does not exist: {folderPath}",
                OutputSeverity.Warning);
            return false;
        }

        var fullPath = Path.GetFullPath(folderPath);
        if (string.Equals(
            context.Workspace.RootPath,
            fullPath,
            StringComparison.OrdinalIgnoreCase))
        {
            context.Output.Write("Workspace", $"Already open: {fullPath}");
            return true;
        }

        if (!await CloseDocumentsForWorkspaceSwitchAsync())
        {
            context.Output.Write("Workspace", "Workspace switch cancelled.");
            return false;
        }

        try
        {
            StatusBarBranch.Text = string.Empty;
            context.Workspace.OpenFolder(fullPath);
            SetWorkspaceStatus(fullPath);
            ShowSidebar(MainGrid.ColumnDefinitions[ExplorerColumnIndex]);
            _sidebarMode = "explorer";
            ExplorerHost.IsVisible = true;
            SearchHost.IsVisible = false;
            SetActiveActivity("explorer");
            context.Output.Write("Workspace", $"Opened: {fullPath}");
            return true;
        }
        catch (Exception ex)
        {
            context.Output.Write("Workspace", $"Open Folder failed: {ex.Message}");
            return false;
        }
    }

    private async Task<bool> CloseDocumentsForWorkspaceSwitchAsync()
    {
        if (_statusEditorVm is null)
        {
            return true;
        }

        foreach (var tab in _statusEditorVm.Tabs.ToArray())
        {
            await _statusEditorVm.CloseTabWithConfirmAsync(tab);
            if (_statusEditorVm.Tabs.Contains(tab))
            {
                return false;
            }
        }

        return true;
    }

    private async Task CloneRepositoryAsync(IHostContext context)
    {
        var repositoryUrl = await TextPromptWindow.ShowAndGetResultAsync(
            this,
            "Clone Repository",
            "Repository URL",
            string.Empty);
        repositoryUrl = repositoryUrl?.Trim();
        if (string.IsNullOrWhiteSpace(repositoryUrl))
        {
            return;
        }

        if (StorageProvider is null)
        {
            context.Output.Write("Git Clone", "Clone failed: no storage provider.");
            return;
        }

        string? destinationParent;
        try
        {
            destinationParent = await PickFolderAsync("Select destination folder");
        }
        catch (Exception ex)
        {
            context.Output.Write(
                "Git Clone",
                $"Cannot select destination folder: {ex.Message}",
                OutputSeverity.Error);
            return;
        }
        if (string.IsNullOrWhiteSpace(destinationParent))
        {
            return;
        }

        var repositoryName = DeriveRepositoryName(repositoryUrl);
        if (string.IsNullOrWhiteSpace(repositoryName))
        {
            context.Output.Write(
                "Git Clone",
                "Clone failed: cannot derive a folder name from the repository URL.",
                OutputSeverity.Warning);
            return;
        }

        var destinationPath = Path.Combine(destinationParent, repositoryName);
        if (Directory.Exists(destinationPath) || File.Exists(destinationPath))
        {
            context.Output.Write(
                "Git Clone",
                $"Clone failed: destination already exists: {destinationPath}",
                OutputSeverity.Warning);
            return;
        }

        context.Output.Write("Git Clone", $"Cloning into: {destinationPath}");
        ProcessResult result;
        try
        {
            result = await context.Processes.RunAsync(
                new ProcessRequest(
                    "git",
                    ["clone", "--progress", "--", repositoryUrl, destinationPath],
                    destinationParent),
                line =>
                {
                    var safeText = line.Text.Replace(
                        repositoryUrl,
                        "[repository]",
                        StringComparison.OrdinalIgnoreCase);
                    context.Output.Write("Git Clone", safeText);
                },
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            context.Output.Write("Git Clone", $"Clone failed: {ex.Message}", OutputSeverity.Error);
            return;
        }

        if (result.ExitCode != 0)
        {
            context.Output.Write(
                "Git Clone",
                $"Clone failed with exit code {result.ExitCode}.",
                OutputSeverity.Error);
            return;
        }

        if (!GitRepositoryContext.HasGitMarker(destinationPath))
        {
            context.Output.Write(
                "Git Clone",
                "Clone completed, but the destination does not contain a .git marker.",
                OutputSeverity.Warning);
        }

        context.Output.Write("Git Clone", "Clone completed.");
        await SwitchWorkspaceAsync(context, destinationPath);
    }

    private async Task<string?> PickFolderAsync(string title)
    {
        if (StorageProvider is null)
        {
            return null;
        }

        var homePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        IStorageFolder? homeFolder = null;
        if (!string.IsNullOrWhiteSpace(homePath))
        {
            homeFolder = await StorageProvider.TryGetFolderFromPathAsync(homePath);
        }

        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            SuggestedStartLocation = homeFolder,
        });
        return folders.FirstOrDefault()?.TryGetLocalPath();
    }

    private static string? DeriveRepositoryName(string repositoryUrl)
    {
        var normalized = repositoryUrl.Trim().TrimEnd('/', '\\');
        var separator = Math.Max(normalized.LastIndexOf('/'), normalized.LastIndexOf(':'));
        var name = separator >= 0 ? normalized[(separator + 1)..] : normalized;
        if (name.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^4];
        }

        name = name.Trim();
        return name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            ? null
            : name;
    }

    private void SetWorkspaceStatus(string? rootPath)
    {
        var folderName = rootPath is null
            ? null
            : Path.GetFileName(rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        StatusBarWorkspace.Text = folderName ?? string.Empty;
        ToolTip.SetTip(StatusBarWorkspace, rootPath);
        Title = folderName is null ? "Junexis Studio" : $"{folderName} — Junexis Studio";
    }

    private void SubscribeBuildStatus(IHostContext context)
    {
        context.EventBus.Subscribe<BuildStartedEvent>(_ =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                StatusBarBuild.Text = "⚙ Building…";
                StatusBarBuild.Foreground = (Avalonia.Media.IBrush)Resources["StudioMutedTextBrush"]!;
            }));

        context.EventBus.Subscribe<BuildFinishedEvent>(e =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                var duration = e.DurationMs < 1000 ? $"{e.DurationMs}ms" : $"{e.DurationMs / 1000.0:F1}s";
                StatusBarBuild.Text = e.Success ? $"✓ Build OK ({duration})" : $"✗ Build failed ({duration})";
                StatusBarBuild.Foreground = e.Success
                    ? new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#4EC9B0"))
                    : new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#F14C4C"));
            }));
    }

    private void SubscribeDiagnosticsStatus(IHostContext context)
    {
        context.Diagnostics.DiagnosticsChanged += () =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                var diags = context.Diagnostics.GetDiagnostics();
                var errors = diags.Count(d => d.Severity == DiagnosticSeverity.Error);
                var warnings = diags.Count(d => d.Severity == DiagnosticSeverity.Warning);
                if (errors > 0)
                {
                    StatusBarDiagnostics.Text = $"✗ {errors}  ⚠ {warnings}";
                    StatusBarDiagnostics.Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#F14C4C"));
                }
                else if (warnings > 0)
                {
                    StatusBarDiagnostics.Text = $"⚠ {warnings}";
                    StatusBarDiagnostics.Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#CCA700"));
                }
                else
                {
                    StatusBarDiagnostics.Text = string.Empty;
                }
            });
    }

    private void BindMenu(IHostContext context)
    {
        var commands = context.Commands
            .GetRegisteredCommands()
            .Where(c => !string.IsNullOrWhiteSpace(c.MenuPath))
            .OrderBy(c => c.MenuPath, StringComparer.Ordinal)
            .ThenBy(c => c.Id, StringComparer.Ordinal)
            .ToArray();

        MainMenu.Items.Clear();
        foreach (var item in BuildMenuItems(commands, context))
        {
            MainMenu.Items.Add(item);
        }
    }

    private static IReadOnlyList<MenuItem> BuildMenuItems(
        IReadOnlyList<CommandRegistration> commands,
        IHostContext context)
    {
        var root = new Dictionary<string, MenuNode>(StringComparer.Ordinal);

        foreach (var command in commands)
        {
            var path = command.MenuPath;
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            var parts = path
                .Replace('\\', '/')
                .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (parts.Length == 0)
            {
                continue;
            }

            var current = root;
            MenuNode? node = null;

            for (var i = 0; i < parts.Length; i++)
            {
                var header = parts[i];
                var isLeaf = i == parts.Length - 1;

                if (!current.TryGetValue(header, out node))
                {
                    node = new MenuNode(header);
                    current[header] = node;
                }

                if (isLeaf)
                {
                    node.CommandId = command.Id;
                }
                else
                {
                    current = node.Children;
                }
            }
        }

        return root.Values
            .OrderBy(GetTopLevelMenuOrder)
            .ThenBy(n => n.Header, StringComparer.Ordinal)
            .Select(n => n.ToMenuItem(context))
            .ToArray();
    }

    private static int GetTopLevelMenuOrder(MenuNode node)
    {
        for (var i = 0; i < TopLevelMenuOrder.Length; i++)
        {
            if (string.Equals(TopLevelMenuOrder[i], node.Header, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return int.MaxValue;
    }

    private static int GetChildMenuOrder(string? parentHeader, MenuNode node)
    {
        if (string.Equals(parentHeader, "File", StringComparison.Ordinal))
        {
            for (var i = 0; i < FileMenuOrder.Length; i++)
            {
                if (string.Equals(FileMenuOrder[i], node.Header, StringComparison.Ordinal))
                {
                    return i;
                }
            }
        }

        if (string.Equals(parentHeader, "Edit", StringComparison.Ordinal))
        {
            for (var i = 0; i < EditMenuOrder.Length; i++)
            {
                if (string.Equals(EditMenuOrder[i], node.Header, StringComparison.Ordinal))
                {
                    return i;
                }
            }
        }

        if (string.Equals(parentHeader, "Run", StringComparison.Ordinal))
        {
            for (var i = 0; i < RunMenuOrder.Length; i++)
            {
                if (string.Equals(RunMenuOrder[i], node.Header, StringComparison.Ordinal))
                {
                    return i;
                }
            }
        }

        if (string.Equals(parentHeader, "Debug", StringComparison.Ordinal))
        {
            for (var i = 0; i < DebugMenuOrder.Length; i++)
            {
                if (string.Equals(DebugMenuOrder[i], node.Header, StringComparison.Ordinal))
                {
                    return i;
                }
            }
        }

        if (string.Equals(parentHeader, "Help", StringComparison.Ordinal))
        {
            for (var i = 0; i < HelpMenuOrder.Length; i++)
            {
                if (string.Equals(HelpMenuOrder[i], node.Header, StringComparison.Ordinal))
                    return i;
            }
        }

        return int.MaxValue;
    }

    private static int GetChildMenuGroup(string? parentHeader, MenuNode node)
    {
        if (string.Equals(parentHeader, "File", StringComparison.Ordinal))
        {
            for (var i = 0; i < FileMenuGroups.Length; i++)
            {
                if (FileMenuGroups[i].Any(header => string.Equals(header, node.Header, StringComparison.Ordinal)))
                {
                    return i;
                }
            }
        }

        if (string.Equals(parentHeader, "Edit", StringComparison.Ordinal))
        {
            for (var i = 0; i < EditMenuGroups.Length; i++)
            {
                if (EditMenuGroups[i].Any(header => string.Equals(header, node.Header, StringComparison.Ordinal)))
                {
                    return i;
                }
            }
        }

        if (string.Equals(parentHeader, "Run", StringComparison.Ordinal))
        {
            for (var i = 0; i < RunMenuGroups.Length; i++)
            {
                if (RunMenuGroups[i].Any(header => string.Equals(header, node.Header, StringComparison.Ordinal)))
                {
                    return i;
                }
            }
        }

        if (string.Equals(parentHeader, "Debug", StringComparison.Ordinal))
        {
            for (var i = 0; i < DebugMenuGroups.Length; i++)
            {
                if (DebugMenuGroups[i].Any(header => string.Equals(header, node.Header, StringComparison.Ordinal)))
                {
                    return i;
                }
            }
        }

        return int.MaxValue;
    }

    private sealed class MenuNode
    {
        public MenuNode(string header)
        {
            Header = header;
        }

        public string Header { get; }

        public string? CommandId { get; set; }

        public Dictionary<string, MenuNode> Children { get; } = new(StringComparer.Ordinal);

        public MenuItem ToMenuItem(IHostContext context)
        {
            var item = new MenuItem { Header = Header };

            if (CommandId is not null)
            {
                ConfigureCommandMenuItem(item, CommandId, Header, context);
            }

            if (Children.Count > 0)
            {
                var orderedChildren = Children.Values
                    .OrderBy(n => GetChildMenuOrder(Header, n))
                    .ThenBy(n => n.Header, StringComparer.Ordinal)
                    .ToArray();

                var previousGroup = int.MinValue;

                foreach (var child in orderedChildren)
                {
                    var currentGroup = GetChildMenuGroup(Header, child);
                    if (previousGroup != int.MinValue && currentGroup != previousGroup)
                    {
                        item.Items.Add(new Separator());
                    }

                    item.Items.Add(child.ToMenuItem(context));
                    previousGroup = currentGroup;
                }

                // Refresh IsEnabled for all leaf items each time this submenu opens.
                item.SubmenuOpened += (_, _) => RefreshMenuEnabled(item, context);
            }

            return item;
        }

        private static void RefreshMenuEnabled(MenuItem parent, IHostContext context)
        {
            foreach (var child in parent.Items.OfType<MenuItem>())
            {
                if (child.Tag is string commandId)
                    child.IsEnabled = context.Commands.CanExecute(commandId);

                if (child.Items.Count > 0)
                    RefreshMenuEnabled(child, context);
            }
        }
    }

    private void BindPanels(IHostContext context)
    {
        DisposeCurrentPanels();

        var registrations = context.Panels.GetRegisteredPanels();
        var instances = new Dictionary<string, object>(StringComparer.Ordinal);

        foreach (var reg in registrations)
        {
            try
            {
                instances[reg.Id] = reg.Factory();
            }
            catch (Exception ex)
            {
                context.Output.Write("Shell", $"Panel '{reg.Id}' factory failed: {ex.Message}");
            }
        }

        ExplorerHost.Content = ResolveViewOrFallback(
            context,
            ResolveExplorerContent(registrations, instances),
            fallbackText: "Explorer");
        if (instances.TryGetValue(WorkspaceSearchPanelId, out var searchInstance))
        {
            SearchHost.Content = ResolveViewOrFallback(context, searchInstance, fallbackText: "Search");
        }
        EditorHost.Content = ResolveViewOrFallback(
            context,
            ResolveEditorContent(registrations, instances),
            fallbackText: "Editor");

        // Wire unsaved-changes confirmation dialog.
        if (instances.TryGetValue(DefaultEditorPanelId, out var editorObj)
            && editorObj is EditorPanelViewModel editorVm)
        {
            editorVm.ConfirmCloseAsync = tab =>
                UnsavedChangesWindow.ShowAndGetResultAsync(this, tab.FileName);

            // Wire status bar — update on any StatusText change; click triggers Go to Line.
            StatusBarText.Content = editorVm.StatusText;
            StatusBarText.Click += (_, _) => context.Commands.Execute("editor.goToLine");
            editorVm.PropertyChanged += OnEditorStatusChanged;
            _statusEditorVm = editorVm;
        }

        BindToolTabs(context, registrations, instances);
    }

    private void OnEditorStatusChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EditorPanelViewModel.StatusText)
            && sender is EditorPanelViewModel vm)
        {
            StatusBarText.Content = vm.StatusText;
        }
    }

    private void BindContextMenus(IHostContext context)
    {
        ExplorerHost.ContextMenu = BuildContextMenu("explorer", context);
        EditorHost.ContextMenu = BuildContextMenu("editor", context);

        foreach (var host in new[] { ToolTabsHost, RightTabsHost })
        {
            foreach (var item in host.Items)
            {
                if (item is TabItem tab)
                {
                    var panelId = tab.Tag as string ?? tab.Header as string ?? string.Empty;
                    tab.ContextMenu = BuildContextMenu(panelId, context);
                }
            }
        }
    }

    private static ContextMenu? BuildContextMenu(string target, IHostContext context)
    {
        var commands = context.Commands
            .GetRegisteredCommands()
            .Where(c => string.Equals(c.ContextTarget, target, StringComparison.Ordinal))
            .OrderBy(GetContextTopLevelOrder)
            .ThenBy(GetContextChildOrder)
            .ThenBy(c => c.DisplayName ?? c.Id, StringComparer.Ordinal)
            .ToArray();

        if (commands.Length == 0)
        {
            return null;
        }

        var menu = new ContextMenu();
        string? previousTopLevel = null;
        var previousGroup = int.MinValue;

        foreach (var command in commands)
        {
            var commandId = command.Id;
            var topLevel = GetContextTopLevelHeader(command);
            var group = GetContextChildGroup(command);

            if (menu.Items.Count > 0)
            {
                if (!string.Equals(previousTopLevel, topLevel, StringComparison.Ordinal)
                    || group != previousGroup)
                {
                    menu.Items.Add(new Separator());
                }
            }

            var item = new MenuItem { Header = command.DisplayName ?? commandId };
            ConfigureCommandMenuItem(item, commandId, command.DisplayName ?? commandId, context);

            menu.Items.Add(item);
            previousTopLevel = topLevel;
            previousGroup = group;
        }

        // Refresh IsEnabled for all items each time the context menu opens.
        menu.Opening += (_, _) =>
        {
            foreach (var item in menu.Items.OfType<MenuItem>())
            {
                if (item.Tag is string cmdId)
                    item.IsEnabled = context.Commands.CanExecute(cmdId);
            }
        };

        return menu;
    }

    private static int GetContextTopLevelOrder(CommandRegistration command)
    {
        var topLevel = GetContextTopLevelHeader(command);

        for (var i = 0; i < TopLevelMenuOrder.Length; i++)
        {
            if (string.Equals(TopLevelMenuOrder[i], topLevel, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return int.MaxValue;
    }

    private static int GetContextChildOrder(CommandRegistration command)
    {
        var (parentHeader, childHeader) = GetContextMenuParts(command);
        return GetChildMenuOrder(parentHeader, new MenuNode(childHeader));
    }

    private static int GetContextChildGroup(CommandRegistration command)
    {
        var (parentHeader, childHeader) = GetContextMenuParts(command);
        return GetChildMenuGroup(parentHeader, new MenuNode(childHeader));
    }

    private static string? GetContextTopLevelHeader(CommandRegistration command)
    {
        var (parentHeader, _) = GetContextMenuParts(command);
        return parentHeader;
    }

    private static void ConfigureCommandMenuItem(MenuItem item, string commandId, string displayName, IHostContext context)
    {
        var keybindingText = GetKeybindingText(commandId, context.Keybindings);
        item.Header = string.IsNullOrWhiteSpace(keybindingText)
            ? displayName
            : $"{displayName}    {keybindingText}";

        item.Tag = commandId;
        item.IsEnabled = context.Commands.CanExecute(commandId);

        item.Click += (_, _) =>
        {
            try
            {
                context.Commands.Execute(commandId);
            }
            catch (Exception ex)
            {
                context.Output.Write("Shell", $"Command '{commandId}' failed: {ex.Message}");
            }
        };
    }

    private static string? GetKeybindingText(string commandId, IKeybindingRegistry keybindings)
    {
        var matches = keybindings.GetBindings()
            .Where(binding => string.Equals(binding.Value, commandId, StringComparison.Ordinal))
            .Select(binding => binding.Key.ToString())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(text => text, StringComparer.Ordinal)
            .ToArray();

        return matches.Length == 0
            ? null
            : string.Join(" / ", matches);
    }

    private static (string? parentHeader, string childHeader) GetContextMenuParts(CommandRegistration command)
    {
        if (string.IsNullOrWhiteSpace(command.MenuPath))
        {
            return (null, command.DisplayName ?? command.Id);
        }

        var parts = command.MenuPath
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length == 0)
        {
            return (null, command.DisplayName ?? command.Id);
        }

        if (parts.Length == 1)
        {
            return (parts[0], command.DisplayName ?? parts[0]);
        }

        return (parts[0], parts[^1]);
    }

    private void DisposeCurrentPanels()
    {
        if (_statusEditorVm is not null)
        {
            _statusEditorVm.PropertyChanged -= OnEditorStatusChanged;
            _statusEditorVm = null;
        }

        DisposeDataContextIfNeeded(ExplorerHost.Content);
        DisposeDataContextIfNeeded(SearchHost.Content);
        DisposeDataContextIfNeeded(EditorHost.Content);

        foreach (var host in new[] { ToolTabsHost, RightTabsHost })
        {
            foreach (var item in host.Items)
            {
                if (item is TabItem tab)
                    DisposeDataContextIfNeeded(tab.Content);
            }
        }
    }

    private void BindToolTabs(
        IHostContext context,
        IReadOnlyList<PanelRegistration> registrations,
        IReadOnlyDictionary<string, object> instances)
    {
        ToolTabsHost.Items.Clear();
        RightTabsHost.Items.Clear();

        var toolRegs = registrations
            .Where(r => r.Type == PanelType.Tool
                && r.Id != DefaultExplorerPanelId
                && r.Id != WorkspaceSearchPanelId)
            .ToArray();
        var rightRegs = toolRegs
            .Where(registration => registration.DockGroup == DockGroup.Right)
            .OrderBy(RightPanelOrder)
            .ThenBy(registration => registration.Id, StringComparer.Ordinal)
            .ToArray();
        var bottomRegs = toolRegs
            .Where(registration => registration.DockGroup != DockGroup.Right)
            .ToArray();

        var output = bottomRegs.FirstOrDefault(r => r.Id == DefaultOutputPanelId);
        if (output is not null)
        {
            AddToolTab(ToolTabsHost, context, output, instances);
        }

        foreach (var reg in bottomRegs
            .Where(r => r.Id != DefaultOutputPanelId)
            .OrderBy(r => r.Id, StringComparer.Ordinal))
        {
            AddToolTab(ToolTabsHost, context, reg, instances);
        }

        foreach (var reg in rightRegs)
        {
            AddToolTab(RightTabsHost, context, reg, instances);
        }

        if (ToolTabsHost.ItemCount == 0)
        {
            ToolTabsHost.Items.Add(new TabItem
            {
                Header = "tools",
                Content = new TextBlock { Text = "Tools (not registered)" },
            });
        }

        var idx = 0;
        if (output is not null)
        {
            idx = 0;
        }

        ToolTabsHost.SelectedIndex = idx;
        if (RightTabsHost.ItemCount > 0)
            RightTabsHost.SelectedIndex = 0;
        ToolTabsMoreButton.IsVisible = ToolTabsHost.ItemCount > 1;
        RightTabsMoreButton.IsVisible = RightTabsHost.ItemCount > 1;

        var hasRightPanels = RightTabsHost.ItemCount > 0;
        if (!hasRightPanels)
        {
            HideRightPanel();
        }
        else
        {
            var isVisible = MainGrid.ColumnDefinitions[RightPanelColumnIndex].Width.Value >= 80;
            RightPanelHost.IsVisible = isVisible;
            RightSplitter.IsVisible = isVisible;
        }
    }

    private void OnToolTabsMoreClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        ShowPanelMenu(ToolTabsMoreButton, ToolTabsHost);

    private void OnRightTabsMoreClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        ShowPanelMenu(RightTabsMoreButton, RightTabsHost);

    private void ShowPanelMenu(Button anchor, TabControl host)
    {
        var menu = new ContextMenu();
        foreach (var item in host.Items)
        {
            if (item is not TabItem tab)
                continue;
            var menuItem = new MenuItem
            {
                Header = ReferenceEquals(tab, host.SelectedItem)
                    ? $"✓ {tab.Header}"
                    : tab.Header,
            };
            menuItem.Click += (_, _) =>
            {
                host.SelectedItem = tab;
                if (ReferenceEquals(host, RightTabsHost))
                    ShowRightPanel();
            };
            menu.Items.Add(menuItem);
        }
        menu.Open(anchor);
    }

    private void AddToolTab(
        TabControl host,
        IHostContext context,
        PanelRegistration registration,
        IReadOnlyDictionary<string, object> instances)
    {
        if (!instances.TryGetValue(registration.Id, out var instance))
        {
            return;
        }

        instance = registration.Id == DefaultOutputPanelId
            ? WrapOutputIfNeeded(instance)
            : instance;

        var content = ResolveViewOrFallback(context, instance, fallbackText: registration.Id);

        host.Items.Add(new TabItem
        {
            Header = registration.Title ?? registration.Id,
            Tag = registration.Id,
            Content = content,
        });
    }

    private static object ResolveViewOrFallback(IHostContext context, object panelInstance, string fallbackText)
    {
        if (panelInstance is string text)
        {
            return new TextBlock { Text = text };
        }

        try
        {
            var view = context.Views.Resolve(panelInstance);
            if (view is not null)
            {
                return view;
            }
        }
        catch (Exception ex)
        {
            context.Output.Write("Shell", $"View resolve failed: {ex.Message}");
        }

        return new TextBlock { Text = $"{fallbackText} (no view registered)" };
    }

    private object ResolveExplorerContent(IReadOnlyList<PanelRegistration> registrations, IReadOnlyDictionary<string, object> instances)
    {
        var preferred = registrations.FirstOrDefault(r => r.Id == DefaultExplorerPanelId);
        if (preferred is not null && instances.TryGetValue(preferred.Id, out var preferredInstance))
        {
            return preferredInstance;
        }

        var fallback = registrations.FirstOrDefault(r => r.Type == PanelType.Tool && r.Id != DefaultOutputPanelId);
        if (fallback is not null && instances.TryGetValue(fallback.Id, out var fallbackInstance))
        {
            return fallbackInstance;
        }

        return "Explorer (not registered)";
    }

    private object ResolveEditorContent(IReadOnlyList<PanelRegistration> registrations, IReadOnlyDictionary<string, object> instances)
    {
        var preferred = registrations.FirstOrDefault(r => r.Id == DefaultEditorPanelId);
        if (preferred is not null && instances.TryGetValue(preferred.Id, out var preferredInstance))
        {
            return preferredInstance;
        }

        var fallback = registrations.FirstOrDefault(r => r.Type == PanelType.Document);
        if (fallback is not null && instances.TryGetValue(fallback.Id, out var fallbackInstance))
        {
            return fallbackInstance;
        }

        return "Editor (not registered)";
    }

    private static object WrapOutputIfNeeded(object panelInstance)
    {
        return panelInstance is IOutput outputService
            ? new OutputPanelViewModel(outputService)
            : panelInstance;
    }

    /// <summary>
    /// Przed zamknięciem okna sprawdza, czy są niezapisane dokumenty.
    /// Jeśli tak, anuluje zamknięcie i pokazuje dialog asynchronicznie.
    /// </summary>
    protected override void OnClosing(Avalonia.Controls.WindowClosingEventArgs e)
    {
        base.OnClosing(e);

        if (e.Cancel || e.IsProgrammatic)
        {
            return;
        }

        if (DataContext is not IHostContext context)
        {
            return;
        }

        var panels = context.Panels.GetRegisteredPanels();
        var editorReg = panels.FirstOrDefault(p => p.Id == DefaultEditorPanelId);
        if (editorReg is null)
        {
            return;
        }

        var editorObj = editorReg.Factory();
        if (editorObj is not EditorPanelViewModel editorVm)
        {
            return;
        }

        var dirtyTabs = editorVm.Tabs.Where(t => t.IsDirty).ToList();
        if (dirtyTabs.Count == 0)
        {
            return;
        }

        // Ansuluj domyślne zamknięcie i obsłuż asynchronicznie.
        e.Cancel = true;
        Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
        {
            foreach (var tab in dirtyTabs)
            {
                var result = await UnsavedChangesWindow.ShowAndGetResultAsync(this, tab.FileName);
                if (result == Junexis.Studio.Abstractions.UnsavedChangesResult.Cancel)
                {
                    return; // Nie zamykaj
                }

                if (result == Junexis.Studio.Abstractions.UnsavedChangesResult.Save)
                {
                    tab.Save();
                }
            }

            Close();
        });
    }

    /// <summary>
    /// Integracja z <see cref="IKeybindingRegistry"/> — konwertuje Avalonia KeyEvent na gest
    /// i deleguje wykonanie do <see cref="ICommandRegistry"/>.
    /// </summary>
    /// <param name="e">Argumenty zdarzenia.</param>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (DataContext is not IHostContext context)
        {
            return;
        }

        var gesture = new Junexis.Studio.Abstractions.KeyGesture(e.Key.ToString(), ToJunexisModifiers(e.KeyModifiers));

        if (context.Keybindings.TryGetCommand(gesture, out var commandId)
            && commandId is not null
            && context.Commands.CanExecute(commandId))
        {
            context.Commands.Execute(commandId);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Konwertuje modyfikatory Avalonia na <see cref="Junexis.Studio.Abstractions.KeyModifiers"/>.
    /// </summary>
    private static Junexis.Studio.Abstractions.KeyModifiers ToJunexisModifiers(Avalonia.Input.KeyModifiers avaloniaModifiers)
    {
        var result = Junexis.Studio.Abstractions.KeyModifiers.None;
        if (avaloniaModifiers.HasFlag(Avalonia.Input.KeyModifiers.Control)) result |= Junexis.Studio.Abstractions.KeyModifiers.Ctrl;
        if (avaloniaModifiers.HasFlag(Avalonia.Input.KeyModifiers.Shift)) result |= Junexis.Studio.Abstractions.KeyModifiers.Shift;
        if (avaloniaModifiers.HasFlag(Avalonia.Input.KeyModifiers.Alt)) result |= Junexis.Studio.Abstractions.KeyModifiers.Alt;
        return result;
    }

    /// <summary>
    /// Zwalnia zasoby view modeli (np. subskrypcje eventów).
    /// </summary>
    protected override void OnClosed(EventArgs e)
    {
        // Zapisz rozmiary paneli zanim zostaną zutylizowane.
        SaveLayout();

        DisposeDataContextIfNeeded(ExplorerHost.Content);
        DisposeDataContextIfNeeded(SearchHost.Content);
        DisposeDataContextIfNeeded(EditorHost.Content);
        foreach (var host in new[] { ToolTabsHost, RightTabsHost })
        {
            foreach (var item in host.Items)
            {
                if (item is TabItem tab)
                    DisposeDataContextIfNeeded(tab.Content);
            }
        }

        UnsubscribeActivityBadges();
        base.OnClosed(e);
    }

    private void SaveLayout()
    {
        try
        {
            var explorerWidth = MainGrid.ColumnDefinitions[ExplorerColumnIndex].Width.Value;
            var toolTabsHeight = MainGrid.RowDefinitions[ToolTabsRowIndex].Height.Value;
            var rightPanelWidth = MainGrid.ColumnDefinitions[RightPanelColumnIndex].Width.Value;
            _layoutStore.Save(new ShellLayoutData(
                explorerWidth < 80 ? _lastExplorerWidth : explorerWidth,
                toolTabsHeight,
                rightPanelWidth < 80
                    ? _lastRightPanelWidth
                    : rightPanelWidth,
                (ToolTabsHost.SelectedItem as TabItem)?.Tag as string,
                (RightTabsHost.SelectedItem as TabItem)?.Tag as string));
        }
        catch
        {
            // Zapis layoutu jest non-critical.
        }
    }

    private void SubscribePreferencesWindow(IHostContext context)
    {
        context.EventBus.Subscribe<OpenPreferencesRequested>(req =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
            {
                var vm = new PreferencesViewModel(context.Preferences, context.Output, req.IsWorkspace);
                var window = new PreferencesWindow { DataContext = vm };
                await window.ShowDialog(this);
            });
        });
    }

    private void SubscribeKeyboardShortcuts(IHostContext context)
    {
        context.EventBus.Subscribe<OpenKeyboardShortcutsRequested>(_ =>
            Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
            {
                var window = new KeyboardShortcutsWindow(context);
                await window.ShowDialog(this);
            }));
    }

    private void SubscribeVcsBranch(IHostContext context)
    {
        context.EventBus.Subscribe<VcsBranchChangedEvent>(evt =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                StatusBarBranch.Text = string.IsNullOrWhiteSpace(evt.BranchName)
                    ? ""
                    : $"\u2387 {evt.BranchName}";
            });
        });
    }

    private void SubscribePanelActivation(IHostContext context)
    {
        context.EventBus.Subscribe<ActivatePanelRequested>(request =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                foreach (var host in new[] { ToolTabsHost, RightTabsHost })
                {
                    foreach (var item in host.Items)
                    {
                        if (item is not TabItem tab
                            || !string.Equals(tab.Tag as string, request.PanelId, StringComparison.Ordinal))
                            continue;
                        host.SelectedItem = tab;
                        SetActiveActivityForPanel(request.PanelId);
                        if (ReferenceEquals(host, ToolTabsHost)
                            && MainGrid.RowDefinitions[ToolTabsRowIndex].Height.Value < 80)
                        {
                            MainGrid.RowDefinitions[ToolTabsRowIndex].Height = new GridLength(
                                ShellLayoutData.Default.ToolTabsHeight,
                                GridUnitType.Pixel);
                        }
                        if (ReferenceEquals(host, RightTabsHost)
                            && MainGrid.ColumnDefinitions[RightPanelColumnIndex].Width.Value < 80)
                        {
                            ShowRightPanel();
                        }
                        return;
                    }
                }
                context.Output.Write("Shell", $"Panel '{request.PanelId}' is not registered.", OutputSeverity.Warning);
            });
        });
    }

    private static int RightPanelOrder(PanelRegistration registration) =>
        registration.Id switch
        {
            "junexis.debugger.dap" => 0,
            "junexis.vcs.changes" => 1,
            "junexis.help.center" => 99,
            _ => 10,
        };

    private static void SelectPanelById(TabControl host, string? panelId)
    {
        if (string.IsNullOrWhiteSpace(panelId))
            return;
        foreach (var item in host.Items)
        {
            if (item is TabItem tab
                && string.Equals(tab.Tag as string, panelId, StringComparison.Ordinal))
            {
                host.SelectedItem = tab;
                return;
            }
        }
    }

    private void SubscribeActivityBadges(IHostContext context)
    {
        UnsubscribeActivityBadges();
        _badgeDiagnostics = context.Diagnostics;
        _badgeDiagnostics.DiagnosticsChanged += OnDiagnosticsBadgeChanged;
        context.Capabilities.TryGet<IVcsService>(out _badgeVcs);
        _badgeEventBus = context.EventBus;
        _vcsBadgeHandler = _ =>
            Avalonia.Threading.Dispatcher.UIThread.Post(RefreshActivityBadges);
        _badgeEventBus.Subscribe(_vcsBadgeHandler);
        RefreshActivityBadges();
    }

    private void UnsubscribeActivityBadges()
    {
        if (_badgeDiagnostics is not null)
            _badgeDiagnostics.DiagnosticsChanged -= OnDiagnosticsBadgeChanged;
        if (_badgeEventBus is not null && _vcsBadgeHandler is not null)
            _badgeEventBus.Unsubscribe(_vcsBadgeHandler);
        _badgeDiagnostics = null;
        _badgeVcs = null;
        _badgeEventBus = null;
        _vcsBadgeHandler = null;
    }

    private void OnDiagnosticsBadgeChanged() =>
        Avalonia.Threading.Dispatcher.UIThread.Post(RefreshActivityBadges);

    private void RefreshActivityBadges()
    {
        SetActivityBadge(
            ProblemsBadge,
            ProblemsBadgeText,
            _badgeDiagnostics?.GetDiagnostics().Count ?? 0);
        SetActivityBadge(
            SourceControlBadge,
            SourceControlBadgeText,
            _badgeVcs?.Snapshot.Files.Count ?? 0);
    }

    private static void SetActivityBadge(Border badge, TextBlock text, int count)
    {
        var label = ShellChromePolicy.FormatBadgeCount(count);
        text.Text = label;
        badge.IsVisible = label.Length > 0;
    }

    private static void DisposeDataContextIfNeeded(object? view)
    {
        if (view is Control control && control.DataContext is IDisposable d)
        {
            d.Dispose();
        }
        else if (view is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}

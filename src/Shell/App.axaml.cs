using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Junexis.Studio.Core;
using Junexis.Studio.Host;
using JunexisLang.Studio.CompilerExtension;
using System;
using Avalonia.Threading;

namespace Junexis.Studio.Shell;

/// <summary>
/// Główna aplikacja Avalonia.
/// Odpowiada za uruchomienie <see cref="JunexisStudioHost"/> i spięcie go z UI.
/// </summary>
public partial class App : Application
{
    private readonly WorkspaceHistoryStore _workspaceHistory = new();
    private readonly System.Collections.Generic.HashSet<string> _openFiles = new(StringComparer.OrdinalIgnoreCase);
    private string? _activeFile;

    /// <summary>
    /// Uruchomiony host (v0.1: tworzony w OnFrameworkInitializationCompleted).
    /// </summary>
    public JunexisStudioHost? Host { get; private set; }

    /// <summary>
    /// Ładuje zasoby XAML aplikacji.
    /// </summary>
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        JunexisHighlightingRegistrar.RegisterAll();
    }

    /// <summary>
    /// Kończy inicjalizację frameworka i startuje hosta dla klasycznego lifetime.
    /// </summary>
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var views = new AvaloniaViewRegistry();
            views.Register<OutputPanelViewModel, OutputPanelView>();
            views.Register<AgentReportPanelViewModel, AgentReportPanelView>();
            views.Register<ExplorerPanelViewModel, ExplorerPanelView>();
            views.Register<EditorPanelViewModel, EditorPanelView>();
            views.Register<ProblemsPanelViewModel, ProblemsPanelView>();
            views.Register<TerminalPanelViewModel, TerminalPanelView>();
            views.Register<VcsChangesViewModel, VcsChangesView>();
            views.Register<GitRepositoryViewModel, GitRepositoryView>();
            views.Register<McpStatusViewModel, McpStatusView>();
            views.Register<WhyPanelViewModel, WhyPanelView>();
            views.Register<ArtifactExplorerViewModel, ArtifactExplorerView>();
            views.Register<AnalysisExplorerViewModel, AnalysisExplorerView>();
            views.Register<DeployCenterViewModel, DeployCenterView>();
            views.Register<ToolchainDoctorViewModel, ToolchainDoctorView>();
            views.Register<PlatformLifecycleViewModel, PlatformLifecycleView>();
            views.Register<ReportApplicationViewModel, ReportApplicationView>();
            views.Register<LanguageServicesViewModel, LanguageServicesView>();
            views.Register<WorkspaceSearchViewModel, WorkspaceSearchView>();
            views.Register<DebugPanelViewModel, DebugPanelView>();
            views.Register<HelpCenterViewModel, HelpCenterView>();
            views.Register<OmwiaPanelViewModel, OmwiaPanelView>();
            views.Register<OmwiaChatViewModel, OmwiaChatView>();

            Host = new JunexisStudioHost(views);
            Host.Context.Workspace.FolderOpened += _workspaceHistory.SaveLastFolder;

            // Zastosuj motyw z preferencji
            ApplyThemeFromPreferences();

            WireTabSessionTracking(Host.Context);

            var lastFolder = _workspaceHistory.LoadLastFolder();
            if (!string.IsNullOrWhiteSpace(lastFolder))
            {
                try
                {
                    Host.Context.Workspace.OpenFolder(lastFolder);
                }
                catch (Exception ex)
                {
                    Host.Context.Output.Write("Workspace", $"Restore last folder failed: {ex.Message}");
                }
            }

            Host.Start();
            Host.Context.Output.Write("Shell", "Shell started");

            desktop.Exit += (_, _) =>
            {
                Host.Context.Workspace.FolderOpened -= _workspaceHistory.SaveLastFolder;
                SaveCurrentTabSession(Host.Context);
                Host.Shutdown();
            };

            desktop.MainWindow = new MainWindow
            {
                DataContext = Host.Context,
            };

            if (Program.ReadinessProbePath is not null)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    Program.WriteReadinessProbe(
                        hostStarted: Host is not null,
                        mainWindowCreated: desktop.MainWindow is not null
                            && desktop.MainWindow.DataContext is not null);
                    desktop.Shutdown(0);
                }, DispatcherPriority.Background);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Ustawia motyw aplikacji na podstawie preferencji użytkownika.
    /// Jeśli w preferences.yml nie określono, używa Dark.
    /// </summary>
    private void ApplyThemeFromPreferences()
    {
        if (Host is null) return;

        var theme = Host.Context.Preferences.Current.Theme;
        RequestedThemeVariant = theme?.Equals("Light", StringComparison.OrdinalIgnoreCase) == true
            ? ThemeVariant.Light
            : ThemeVariant.Dark;
    }

    private void WireTabSessionTracking(Junexis.Studio.Abstractions.IHostContext context)
    {
        // Track open/close events to maintain the current tab list.
        context.EventBus.Subscribe<Junexis.Studio.Abstractions.DocumentOpened>(e =>
        {
            _openFiles.Add(e.Path);
            _activeFile = e.Path;
        });
        context.EventBus.Subscribe<Junexis.Studio.Abstractions.DocumentClosed>(e =>
            _openFiles.Remove(e.Path));

        // Restore tabs when workspace opens (handles both startup and mid-session folder switch).
        context.EventBus.Subscribe<Junexis.Studio.Abstractions.WorkspaceOpenedEvent>(e =>
        {
            _openFiles.Clear();
            var folder = e.RootPath;
            if (string.IsNullOrWhiteSpace(folder)) return;
            var session = _workspaceHistory.LoadSession(folder);
            if (session is null) return;

            // Small delay so the editor panel is ready before files are pushed.
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                foreach (var file in session.OpenFiles.Where(File.Exists))
                    context.EventBus.Publish(new Junexis.Studio.Abstractions.FileOpenRequested(file));
            }, Avalonia.Threading.DispatcherPriority.Background);
        });
    }

    private void SaveCurrentTabSession(Junexis.Studio.Abstractions.IHostContext context)
    {
        var folder = context.Workspace.RootPath;
        if (string.IsNullOrWhiteSpace(folder)) return;

        var openFiles = _openFiles.Where(File.Exists).ToList();
        _workspaceHistory.SaveSession(folder,
            new Junexis.Studio.Core.WorkspaceHistoryStore.WorkspaceSession(openFiles, _activeFile));
    }
}

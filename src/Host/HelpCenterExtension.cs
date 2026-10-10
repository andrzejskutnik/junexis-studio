using System.ComponentModel;
using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Host;

/// <summary>One task-oriented topic displayed by the in-product Help Center.</summary>
public sealed record HelpTopic(
    string Id,
    string Title,
    string Summary,
    IReadOnlyList<string> Steps,
    IReadOnlyList<string> SuccessChecks,
    string Documentation);

/// <summary>Stable, UI-independent content model for contextual Studio help.</summary>
public sealed class HelpCenterViewModel : INotifyPropertyChanged
{
    public const string PanelId = "junexis.help.center";

    private HelpTopic _selectedTopic;

    public HelpCenterViewModel()
    {
        Topics =
        [
            new(
                "getting-started",
                "Getting Started",
                "Open a real project, verify its tools and complete the first build.",
                [
                    "Open a folder or clone a repository. Studio never treats its launch directory as your project.",
                    "Open Toolchains and run Check Toolchains before the first build.",
                    "Open a .jnx, .cpp, .cs, .go or .ts file so Studio can select a profile.",
                    "Use F6 to build. Read Problems for source diagnostics and Output for process details.",
                    "Use F5 to run or F7 to debug when the selected profile supports it.",
                ],
                [
                    "The full workspace path is visible in the status bar.",
                    "The Run & Debug bar shows the expected language profile.",
                    "The operation ends as Succeeded, Failed or Cancelled instead of remaining busy.",
                ],
                "docs/GETTING_STARTED.md"),
            new(
                "keyboard-shortcuts",
                "Keyboard Shortcuts",
                "Use the editor and workbench without hunting through menus.",
                [
                    "Ctrl+P opens a workspace file; Ctrl+Shift+P opens every available command.",
                    "F6 builds, F5 runs, F7 debugs and Shift+F5 stops the active operation.",
                    "F2 renames, F12 opens a definition and Shift+F12 finds references.",
                    "Ctrl+. opens Code Actions; F8 and Shift+F8 move between diagnostics.",
                    "Ctrl+Shift+E/F/M/G/D switch Explorer, Search, Problems, Source Control and Run & Debug.",
                ],
                [
                    "Command Palette displays the active shortcut beside each command.",
                    "Focus and the active Activity Bar item remain visibly highlighted.",
                ],
                "docs/KEYBOARD_SHORTCUTS.md"),
            new(
                "build-run-debug",
                "Build, Run and Debug",
                "Understand the shared operation lifecycle and where each kind of evidence appears.",
                [
                    "Select or open a source file and confirm the profile in the Run & Debug bar.",
                    "Build runs a Toolchain Doctor preflight before invoking the compiler.",
                    "Run starts the generated or language-native program for the selected profile.",
                    "Debug starts a DAP launch or attach configuration and enables stepping and variables.",
                    "Use Stop only while an operation is active; use Why? only after DAP reports Stopped.",
                ],
                [
                    "Problems contains structured diagnostics with source locations.",
                    "Output contains compiler, process and adapter logs.",
                    "Why? explains causal runtime evidence and never invents a match when evidence is missing.",
                ],
                "docs/USER_GUIDE.md — Pasek Run & Debug"),
            new(
                "git-workflow",
                "Git Workflow",
                "Keep workspace selection and repository context explicit.",
                [
                    "Open the repository root—the selected folder must directly contain .git.",
                    "Use Changes for staged, changed, untracked and conflicted paths.",
                    "Review a diff and run commit preflight before entering the final commit.",
                    "Use Repository for history, branches, comparison, stash and remotes.",
                    "Clone Repository reports progress in Output and opens the new folder after success.",
                ],
                [
                    "The status bar shows the active branch.",
                    "Source Control badge matches the number of changed paths.",
                    "A plain folder remains usable even when Git context is unavailable.",
                ],
                "docs/WORKSPACE_REPOSITORY_WORKFLOW.md"),
            new(
                "troubleshooting",
                "Troubleshooting",
                "Find the failing layer before changing configuration.",
                [
                    "Start with Toolchains: verify the compiler, runtime, language server and DAP adapter separately.",
                    "Check Problems for source diagnostics and the matching Output channel for raw details.",
                    "Confirm the active workspace root, source file and selected language profile.",
                    "For Git, confirm that .git exists directly in the opened folder.",
                    "For Narrative, confirm the current debug manifest, TraceCollector URL and runtime events.",
                ],
                [
                    "The failure is assigned to one layer: workspace, toolchain, compiler, DAP, Git or Narrative.",
                    "The relevant Output channel contains an actionable message.",
                ],
                "docs/TROUBLESHOOTING.md"),
        ];
        _selectedTopic = Topics[0];
    }

    public IReadOnlyList<HelpTopic> Topics { get; }

    public HelpTopic SelectedTopic
    {
        get => _selectedTopic;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (Equals(_selectedTopic, value))
                return;
            _selectedTopic = value;
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(nameof(SelectedTopic)));
        }
    }

    public bool SelectTopic(string id)
    {
        var topic = Topics.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, id, StringComparison.Ordinal));
        if (topic is null)
            return false;
        SelectedTopic = topic;
        return true;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

internal sealed class HelpCenterExtension : IJunexisExtension
{
    private HelpCenterViewModel? _viewModel;

    public string Id => HelpCenterViewModel.PanelId;

    public void Initialize(IHostContext context)
    {
        _viewModel = new HelpCenterViewModel();
        context.Panels.RegisterPanel(
            Id,
            PanelType.Tool,
            () => _viewModel,
            title: "Help",
            dockGroup: DockGroup.Right);

        RegisterTopic(context, "help.gettingStarted", "Getting Started", "getting-started");
        RegisterTopic(context, "help.buildRunDebug", "Build, Run and Debug", "build-run-debug");
        RegisterTopic(context, "help.gitWorkflow", "Git Workflow", "git-workflow");
        RegisterTopic(context, "help.troubleshooting", "Troubleshooting", "troubleshooting");

        context.Commands.Register(
            "help.keyboardShortcuts",
            () => context.EventBus.Publish(new OpenKeyboardShortcutsRequested()),
            menuPath: "Help/Keyboard Shortcuts",
            displayName: "Keyboard Shortcuts",
            category: "Help");
    }

    public void Shutdown() => _viewModel = null;

    private void RegisterTopic(IHostContext context, string commandId, string title, string topicId)
    {
        context.Commands.Register(
            commandId,
            () =>
            {
                _viewModel?.SelectTopic(topicId);
                context.EventBus.Publish(new ActivatePanelRequested(Id));
            },
            menuPath: $"Help/{title}",
            displayName: title,
            category: "Help");
    }
}

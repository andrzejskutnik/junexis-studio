using Junexis.Studio.Abstractions;
using Junexis.Mcp.Studio;
using Junexis.Studio.Core;
using Junexis.Studio.Host;
using System.IO;
using System.Reflection;
using Xunit;

namespace Junexis.Studio.Host.Tests;

public sealed class JunexisStudioHostTests
{
    [Fact]
    public void Registers_StudioMcpBridge_as_a_host_capability()
    {
        var host = new JunexisStudioHost();

        Assert.True(host.Context.Capabilities.TryGet<StudioMcpBridge>(out var bridge));
        Assert.NotNull(bridge);
    }

    [Fact]
    public void Registers_the_Agent_report_panel()
    {
        var host = new JunexisStudioHost();

        Assert.Contains(host.Context.Panels.GetRegisteredPanels(), panel => panel.Id == "agent.report");
    }

    [Fact]
    public async Task Host_writes_a_structured_report_summary_for_TaskFinished_event()
    {
        using var workspace = new TemporaryWorkspace();
        var host = new JunexisStudioHost();
        host.Context.Workspace.OpenFolder(workspace.Root);
        Directory.CreateDirectory(Path.Combine(workspace.Root, ".junexis"));
        host.Start();
        await File.WriteAllTextAsync(
            Path.Combine(workspace.Root, ".junexis", "agent-events.jsonl"),
            "{\"kind\":\"TaskFinished\",\"sessionId\":\"s\",\"goal\":\"Build\",\"repositoryRoot\":\"C:\\\\repo\",\"timestamp\":\"2026-08-18T10:00:00Z\",\"status\":\"Completed\",\"report\":{\"sessionId\":\"s\",\"goal\":\"Build\",\"repositoryRoot\":\"C:\\\\repo\",\"status\":\"Completed\",\"steps\":[{\"toolName\":\"build\",\"decision\":\"Allow\",\"policyId\":\"rod.build\",\"reason\":\"allowed\",\"exitCode\":0,\"standardOutput\":\"ok\",\"standardError\":\"\"}],\"changedFiles\":[]}}\n");

        for (var attempt = 0; attempt < 20; attempt++)
        {
            if (host.Context.Output.GetSnapshot().Any(entry => entry.Message.Contains("Report: Completed", StringComparison.Ordinal)))
            {
                return;
            }

            await Task.Delay(100);
        }

        Assert.Contains(host.Context.Output.GetSnapshot(), entry => entry.Message.Contains("Report: Completed — 1 steps", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Host_consumes_process_bound_Agent_events_into_Output()
    {
        using var workspace = new TemporaryWorkspace();
        var host = new JunexisStudioHost();
        host.Context.Workspace.OpenFolder(workspace.Root);
        Directory.CreateDirectory(Path.Combine(workspace.Root, ".junexis"));
        host.Start();
        await File.WriteAllTextAsync(
            Path.Combine(workspace.Root, ".junexis", "agent-events.jsonl"),
            "{\"kind\":\"TaskFinished\",\"sessionId\":\"s\",\"goal\":\"Build\",\"repositoryRoot\":\"C:\\\\repo\",\"timestamp\":\"2026-08-18T10:00:00Z\",\"status\":\"Completed\"}\n");
        for (var attempt = 0; attempt < 20; attempt++)
        {
            if (host.Context.Output.GetSnapshot().Any(entry => entry.Message.Contains("Event: TaskFinished", StringComparison.Ordinal)))
            {
                return;
            }

            await Task.Delay(100);
        }

        Assert.Contains(host.Context.Output.GetSnapshot(), entry => entry.Message.Contains("Event: TaskFinished", StringComparison.Ordinal));
    }

    [Fact]
    public void Agent_report_panel_loads_changed_file_paths()
    {
        using var workspace = new TemporaryWorkspace();
        var host = new JunexisStudioHost();
        host.Context.Workspace.OpenFolder(workspace.Root);
        File.WriteAllText(Path.Combine(workspace.Root, "Feature.cs"), "class Feature {}");
        Directory.CreateDirectory(Path.Combine(workspace.Root, ".junexis", "sessions"));
        File.WriteAllText(
            Path.Combine(workspace.Root, ".junexis", "sessions", "session.json"),
            "{\"status\":\"Completed\",\"fileStates\":[{\"path\":\"Feature.cs\",\"beforeSha256\":\"same\",\"afterSha256\":\"same\",\"existsAfter\":true,\"diff\":\"--- a/Feature.cs\\n+++ b/Feature.cs\"},{\"path\":\"Test.cs\",\"beforeSha256\":\"old\",\"afterSha256\":\"new\",\"existsAfter\":true,\"diff\":\"-old\\n+new\"}]}");
        host.RefreshLatestAgentSession();

        var panel = Assert.IsType<AgentReportPanelViewModel>(
            host.Context.Panels.GetRegisteredPanels().Single(item => item.Id == "agent.report").Factory());

        Assert.Equal(
            [AgentFileStatus.Unchanged, AgentFileStatus.Changed],
            panel.ChangedFileItems.Select(item => item.Status));
        Assert.Equal(["Feature.cs", "Test.cs"], panel.ChangedFileItems.Select(item => item.Path));
        Assert.Contains("Feature.cs", panel.ChangedFileItems[0].Diff, StringComparison.Ordinal);
        panel.Dispose();
    }

    [Fact]
    public void Start_publishes_the_latest_Agent_session_to_Output()
    {
        using var workspace = new TemporaryWorkspace();
        var host = new JunexisStudioHost();
        host.Context.Workspace.OpenFolder(workspace.Root);
        Directory.CreateDirectory(Path.Combine(workspace.Root, ".junexis", "sessions"));
        File.WriteAllText(
            Path.Combine(workspace.Root, ".junexis", "sessions", "session.json"),
            "{\"status\":\"Completed\",\"goal\":\"Patch feature\",\"changedFiles\":[\"Feature.cs\"]}");

        host.Start();

        var agentMessages = host.Context.Output.GetSnapshot()
            .Where(entry => entry.Channel == "Agent")
            .Select(entry => entry.Message)
            .ToList();
        Assert.Contains("Latest session: Completed — Patch feature; changed files: 1", agentMessages);
    }

    [Fact]
    public void RefreshLatestAgentSession_updates_Output_after_a_session_finishes()
    {
        using var workspace = new TemporaryWorkspace();
        var host = new JunexisStudioHost();
        host.Context.Workspace.OpenFolder(workspace.Root);
        host.Start();
        Directory.CreateDirectory(Path.Combine(workspace.Root, ".junexis", "sessions"));
        File.WriteAllText(
            Path.Combine(workspace.Root, ".junexis", "sessions", "session.json"),
            "{\"status\":\"Completed\",\"goal\":\"Refresh feature\",\"changedFiles\":[\"Feature.cs\",\"Test.cs\"]}");

        host.Context.Commands.Execute("agent.refresh-session");

        var agentMessages = host.Context.Output.GetSnapshot()
            .Where(entry => entry.Channel == "Agent")
            .Select(entry => entry.Message)
            .ToList();
        Assert.Contains("Latest session: Completed — Refresh feature; changed files: 2", agentMessages);
    }

    [Fact]
    public async Task Start_automatically_refreshes_Output_when_a_session_file_changes()
    {
        using var workspace = new TemporaryWorkspace();
        var host = new JunexisStudioHost();
        host.Context.Workspace.OpenFolder(workspace.Root);
        host.Start();
        Directory.CreateDirectory(Path.Combine(workspace.Root, ".junexis", "sessions"));
        File.WriteAllText(
            Path.Combine(workspace.Root, ".junexis", "sessions", "session.json"),
            "{\"status\":\"Completed\",\"goal\":\"Automatic refresh\",\"changedFiles\":[]}");

        var refreshed = await WaitForOutputAsync(host, "Latest session: Completed — Automatic refresh; changed files: 0");

        Assert.True(refreshed);
    }

    private static async Task<bool> WaitForOutputAsync(JunexisStudioHost host, string expected)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            if (host.Context.Output.GetSnapshot().Any(entry => entry.Message == expected))
            {
                return true;
            }

            await Task.Delay(100);
        }

        return false;
    }

    private sealed class TemporaryWorkspace : IDisposable
    {
        public TemporaryWorkspace()
        {
            Root = Path.Combine(Path.GetTempPath(), $"junexis-studio-agent-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    [Fact]
    [Trait("Issue", "1")]
    public void Start_CalledTwice_DoesNotRepeatStartup()
    {
        var sut = new JunexisStudioHost();

        sut.Start();
        sut.Start();

        var hostEntries = sut.Context.Output
            .GetSnapshot()
            .Where(e => e.Channel == "Host")
            .Select(e => e.Message)
            .ToList();

        Assert.Equal(1, hostEntries.Count(m => m == "Starting..."));
        Assert.Equal(1, hostEntries.Count(m => m == "Started"));
        Assert.Contains(hostEntries, m => m.Contains("already started"));
    }

    [Fact]
    [Trait("Issue", "1")]
    public void Shutdown_WithoutStart_IsSafeAndMarksShutdown()
    {
        var sut = new JunexisStudioHost();

        sut.Shutdown();

        var hostEntries = sut.Context.Output
            .GetSnapshot()
            .Where(e => e.Channel == "Host")
            .Select(e => e.Message)
            .ToList();

        Assert.Contains(hostEntries, m => m.Contains("not started"));
    }

    [Fact]
    [Trait("Issue", "1")]
    public void Shutdown_CalledTwice_IsIdempotent()
    {
        var sut = new JunexisStudioHost();

        sut.Start();
        sut.Shutdown();
        sut.Shutdown();

        var hostEntries = sut.Context.Output
            .GetSnapshot()
            .Where(e => e.Channel == "Host")
            .Select(e => e.Message)
            .ToList();

        Assert.Equal(1, hostEntries.Count(m => m == "Shutting down..."));
        Assert.Equal(1, hostEntries.Count(m => m == "Shutdown complete"));
        Assert.Contains(hostEntries, m => m.Contains("already shutdown"));
    }

    // --- Preferences commands ---

    [Fact]
    public void PreferencesOpen_IsRegistered()
    {
        var sut = new JunexisStudioHost();

        Assert.True(sut.Context.Commands.CanExecute("preferences.open"));
    }

    [Fact]
    public void PreferencesOpenWorkspace_IsRegistered()
    {
        var sut = new JunexisStudioHost();

        Assert.True(sut.Context.Commands.CanExecute("preferences.openWorkspace"));
    }

    [Fact]
    public void PreferencesOpen_HasCorrectMenuPath()
    {
        var sut = new JunexisStudioHost();

        var reg = sut.Context.Commands.GetRegisteredCommands()
            .FirstOrDefault(c => c.Id == "preferences.open");

        Assert.NotNull(reg);
        Assert.Equal("File/Preferences", reg!.MenuPath);
    }

    [Fact]
    public void PreferencesOpenWorkspace_HasCorrectMenuPath()
    {
        var sut = new JunexisStudioHost();

        var reg = sut.Context.Commands.GetRegisteredCommands()
            .FirstOrDefault(c => c.Id == "preferences.openWorkspace");

        Assert.NotNull(reg);
        Assert.Equal("File/Workspace Preferences", reg!.MenuPath);
    }

    [Fact]
    public void PreferencesOpen_PublishesOpenPreferencesRequested()
    {
        var sut = new JunexisStudioHost();
        OpenPreferencesRequested? captured = null;
        sut.Context.EventBus.Subscribe<OpenPreferencesRequested>(e => captured = e);

        sut.Context.Commands.Execute("preferences.open");

        Assert.NotNull(captured);
        Assert.False(captured!.IsWorkspace);
    }

    [Fact]
    public void PreferencesOpenWorkspace_WhenNoWorkspace_LogsWarning()
    {
        var sut = new JunexisStudioHost();
        // Domyślnie workspace nie ma ustawionego RootPath → WorkspaceFilePath == null
        OpenPreferencesRequested? captured = null;
        sut.Context.EventBus.Subscribe<OpenPreferencesRequested>(e => captured = e);

        sut.Context.Commands.Execute("preferences.openWorkspace");

        // Nie powinno opublikować FileOpenRequested
        Assert.Null(captured);
        // Powinno zalogować warning
        var warnings = sut.Context.Output.GetSnapshot()
            .Where(e => e.Channel == "Preferences" && e.Severity == OutputSeverity.Warning)
            .ToList();
        Assert.NotEmpty(warnings);
    }

    [Fact]
    [Trait("Issue", "103")]
    public void Context_HasCtrlS_BoundToEditorSave()
    {
        var sut = new JunexisStudioHost();

        var bound = sut.Context.Keybindings.TryGetCommand(
            new KeyGesture("S", KeyModifiers.Ctrl), out var cmd);

        Assert.True(bound);
        Assert.Equal("editor.save", cmd);
    }

    [Fact]
    [Trait("Issue", "103")]
    public void Context_HasCtrlW_BoundToEditorCloseActiveTab()
    {
        var sut = new JunexisStudioHost();

        var bound = sut.Context.Keybindings.TryGetCommand(
            new KeyGesture("W", KeyModifiers.Ctrl), out var cmd);

        Assert.True(bound);
        Assert.Equal("editor.closeActiveTab", cmd);
    }

    [Fact]
    [Trait("Issue", "103")]
    public void Context_EditorSaveCommand_IsRegistered()
    {
        var sut = new JunexisStudioHost();
        sut.Start();

        Assert.True(sut.Context.Commands.CanExecute("editor.save"));
    }

    [Fact]
    [Trait("Issue", "103")]
    public void Context_EditorCloseActiveTabCommand_IsRegistered()
    {
        var sut = new JunexisStudioHost();
        sut.Start();

        Assert.True(sut.Context.Commands.CanExecute("editor.closeActiveTab"));
    }

    [Fact]
    [Trait("Issue", "103")]
    public void Context_HasCtrlF4_BoundToEditorCloseActiveTab()
    {
        var sut = new JunexisStudioHost();

        var bound = sut.Context.Keybindings.TryGetCommand(
            new KeyGesture("F4", KeyModifiers.Ctrl), out var cmd);

        Assert.True(bound);
        Assert.Equal("editor.closeActiveTab", cmd);
    }

    [Fact]
    [Trait("Issue", "106")]
    public void Context_HasCtrlTab_BoundToEditorNextTab()
    {
        var sut = new JunexisStudioHost();

        var bound = sut.Context.Keybindings.TryGetCommand(
            new KeyGesture("Tab", KeyModifiers.Ctrl), out var cmd);

        Assert.True(bound);
        Assert.Equal("editor.nextTab", cmd);
    }

    [Fact]
    [Trait("Issue", "106")]
    public void Context_HasCtrlShiftTab_BoundToEditorPreviousTab()
    {
        var sut = new JunexisStudioHost();

        var bound = sut.Context.Keybindings.TryGetCommand(
            new KeyGesture("Tab", KeyModifiers.Ctrl | KeyModifiers.Shift), out var cmd);

        Assert.True(bound);
        Assert.Equal("editor.previousTab", cmd);
    }

    [Fact]
    [Trait("Issue", "106")]
    public void Context_HasCtrlPageDown_BoundToEditorNextTab()
    {
        var sut = new JunexisStudioHost();

        var bound = sut.Context.Keybindings.TryGetCommand(
            new KeyGesture("PageDown", KeyModifiers.Ctrl), out var cmd);

        Assert.True(bound);
        Assert.Equal("editor.nextTab", cmd);
    }

    [Fact]
    [Trait("Issue", "106")]
    public void Context_HasCtrlPageUp_BoundToEditorPreviousTab()
    {
        var sut = new JunexisStudioHost();

        var bound = sut.Context.Keybindings.TryGetCommand(
            new KeyGesture("PageUp", KeyModifiers.Ctrl), out var cmd);

        Assert.True(bound);
        Assert.Equal("editor.previousTab", cmd);
    }

    [Fact]
    [Trait("Issue", "106")]
    public void Context_EditorNextTabCommand_IsRegistered()
    {
        var sut = new JunexisStudioHost();
        sut.Start();

        Assert.True(sut.Context.Commands.CanExecute("editor.nextTab"));
    }

    [Fact]
    [Trait("Issue", "106")]
    public void Context_EditorPreviousTabCommand_IsRegistered()
    {
        var sut = new JunexisStudioHost();
        sut.Start();

        Assert.True(sut.Context.Commands.CanExecute("editor.previousTab"));
    }

    [Fact]
    [Trait("Issue", "104")]
    public void Context_HasCtrlF_BoundToEditorToggleSearch()
    {
        var sut = new JunexisStudioHost();

        var bound = sut.Context.Keybindings.TryGetCommand(
            new KeyGesture("F", KeyModifiers.Ctrl), out var cmd);

        Assert.True(bound);
        Assert.Equal("editor.toggleSearch", cmd);
    }

    [Fact]
    [Trait("Issue", "104")]
    public void Context_HasCtrlShiftZ_BoundToEditorRedo()
    {
        var sut = new JunexisStudioHost();

        var bound = sut.Context.Keybindings.TryGetCommand(
            new KeyGesture("Z", KeyModifiers.Ctrl | KeyModifiers.Shift), out var cmd);

        Assert.True(bound);
        Assert.Equal("editor.redo", cmd);
    }

    [Fact]
    [Trait("Issue", "104")]
    public void Context_HasCtrlL_BoundToEditorSelectLine()
    {
        var sut = new JunexisStudioHost();

        var bound = sut.Context.Keybindings.TryGetCommand(
            new KeyGesture("L", KeyModifiers.Ctrl), out var cmd);

        Assert.True(bound);
        Assert.Equal("editor.selectLine", cmd);
    }

    [Fact]
    [Trait("Issue", "104")]
    public void Context_EditorToggleSearchCommand_IsRegistered()
    {
        var sut = new JunexisStudioHost();
        sut.Start();

        Assert.True(sut.Context.Commands.CanExecute("editor.toggleSearch"));
    }

    [Fact]
    [Trait("Issue", "104")]
    public void Context_HasF3_BoundToEditorFindNext()
    {
        var sut = new JunexisStudioHost();

        var bound = sut.Context.Keybindings.TryGetCommand(
            new KeyGesture("F3"), out var cmd);

        Assert.True(bound);
        Assert.Equal("editor.findNext", cmd);
    }

    [Fact]
    [Trait("Issue", "104")]
    public void Context_HasCtrlH_BoundToEditorOpenReplace()
    {
        var sut = new JunexisStudioHost();

        var bound = sut.Context.Keybindings.TryGetCommand(
            new KeyGesture("H", KeyModifiers.Ctrl), out var cmd);

        Assert.True(bound);
        Assert.Equal("editor.openReplace", cmd);
    }

    [Fact]
    [Trait("Issue", "104")]
    public void Context_HasShiftF3_BoundToEditorFindPrev()
    {
        var sut = new JunexisStudioHost();

        var bound = sut.Context.Keybindings.TryGetCommand(
            new KeyGesture("F3", KeyModifiers.Shift), out var cmd);

        Assert.True(bound);
        Assert.Equal("editor.findPrev", cmd);
    }

    [Fact]
    public void Context_RegistersWhyTraceQueryServiceCapability()
    {
        var sut = new JunexisStudioHost();

        Assert.True(sut.Context.Capabilities.TryGet<IWhyTraceQueryService>(out var service));
        Assert.NotNull(service);
    }

    [Fact]
    public void Context_ProjectRunCommand_IsRegistered()
    {
        var sut = new JunexisStudioHost();

        Assert.True(sut.Context.Commands.CanExecute("project.run"));
    }

    [Fact]
    public void Context_ProjectBuildCommand_IsRegistered()
    {
        var sut = new JunexisStudioHost();

        Assert.True(sut.Context.Commands.CanExecute("project.build"));
    }

    [Fact]
    public void Context_HasF6_BoundToProjectBuild()
    {
        var sut = new JunexisStudioHost();

        var bound = sut.Context.Keybindings.TryGetCommand(
            new KeyGesture("F6"), out var cmd);

        Assert.True(bound);
        Assert.Equal("project.build", cmd);
    }

    [Fact]
    [Trait("Issue", "M14")]
    public void Context_HasCtrlShiftF_BoundToViewSearch()
    {
        var sut = new JunexisStudioHost();

        var bound = sut.Context.Keybindings.TryGetCommand(
            new KeyGesture("F", KeyModifiers.Ctrl | KeyModifiers.Shift), out var cmd);

        Assert.True(bound);
        Assert.Equal("view.search", cmd);
    }

    [Fact]
    public void ProjectRun_WhenNoActiveFile_WritesWarningFromHostRegistration()
    {
        var sut = new JunexisStudioHost();

        sut.Context.Commands.Execute("project.run");

        var snapshot = sut.Context.Output.GetSnapshot();
        Assert.Contains(snapshot, e =>
            e.Channel == "Build" &&
            e.Message.Contains("project.run", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ProjectBuild_WhenNoActiveFile_WritesWarningFromHostRegistration()
    {
        var sut = new JunexisStudioHost();

        sut.Context.Commands.Execute("project.build");

        var snapshot = sut.Context.Output.GetSnapshot();
        Assert.Contains(snapshot, e =>
            e.Channel == "Build" &&
            e.Message.Contains("project.build", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("main.c", "project.run", "run.gpp")]
    [InlineData("main.cc", "project.run", "run.gpp")]
    [InlineData("main.cxx", "project.run", "run.gpp")]
    [InlineData("main.cpp", "project.run", "run.gpp")]
    [InlineData("program.cs", "project.run", "run.csharp")]
    [InlineData("MyProject.csproj", "project.run", "run.csharp")]
    [InlineData("JunexisStudio.sln", "project.run", "run.csharp")]
    [InlineData("main.go", "project.run", "run.go")]
    [InlineData("main.jnx", "project.run", "run.jnx")]
    [InlineData("main.c", "project.build", "build.gpp")]
    [InlineData("main.cc", "project.build", "build.gpp")]
    [InlineData("main.cxx", "project.build", "build.gpp")]
    [InlineData("main.cpp", "project.build", "build.gpp")]
    [InlineData("program.cs", "project.build", "build.csharp")]
    [InlineData("MyProject.csproj", "project.build", "build.csharp")]
    [InlineData("JunexisStudio.sln", "project.build", "build.csharp")]
    [InlineData("main.go", "project.build", "build.go")]
    [InlineData("main.jnx", "project.build", "build.jnx")]
    public void BuiltInProjectCommands_DispatchUsingHostRegistration(string fileName, string entryCommand, string expectedCommand)
    {
        var executed = (string?)null;
        var commands = new CommandRegistry();
        commands.Register("run.gpp", () => executed = "run.gpp");
        commands.Register("run.csharp", () => executed = "run.csharp");
        commands.Register("run.go", () => executed = "run.go");
        commands.Register("run.jnx", () => executed = "run.jnx");
        commands.Register("debug.selected", () => executed = "debug.selected");
        commands.Register("build.gpp", () => executed = "build.gpp");
        commands.Register("build.csharp", () => executed = "build.csharp");
        commands.Register("build.go", () => executed = "build.go");
        commands.Register("build.jnx", () => executed = "build.jnx");

        var activeDocument = new ActiveDocumentService();
        activeDocument.SetActivePath(Path.Combine(Path.GetTempPath(), fileName));

        var context = CreateContext(commands: commands, activeDocument: activeDocument);
        RegisterBuiltInCommands(context);

        context.Commands.Execute(entryCommand);

        Assert.Equal(expectedCommand, executed);
    }

    [Theory]
    [InlineData("main.c", "project.run", "run.gpp")]
    [InlineData("main.cc", "project.run", "run.gpp")]
    [InlineData("main.cxx", "project.run", "run.gpp")]
    [InlineData("main.c", "project.build", "build.gpp")]
    [InlineData("main.cc", "project.build", "build.gpp")]
    [InlineData("main.cxx", "project.build", "build.gpp")]
    [InlineData("main.cpp", "project.build", "build.gpp")]
    [InlineData("main.cpp", "project.run", "run.gpp")]
    [InlineData("main.go", "project.build", "build.go")]
    [InlineData("main.go", "project.run", "run.go")]
    [InlineData("JunexisStudio.sln", "project.build", "build.csharp")]
    [InlineData("JunexisStudio.sln", "project.run", "run.csharp")]
    [InlineData("main.jnx", "project.build", "build.jnx")]
    [InlineData("main.jnx", "project.run", "run.jnx")]
    public void BuiltInProjectCommands_WhenResolvedCommandUnavailable_WriteHostWarning(string fileName, string entryCommand, string missingCommand)
    {
        var output = new OutputService();
        var activeDocument = new ActiveDocumentService();
        activeDocument.SetActivePath(Path.Combine(Path.GetTempPath(), fileName));

        var context = CreateContext(output: output, activeDocument: activeDocument);
        RegisterBuiltInCommands(context);

        context.Commands.Execute(entryCommand);

        Assert.Contains(output.GetSnapshot(), e =>
            e.Channel == "Build" &&
            e.Message.Contains(missingCommand, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("notes.txt", "project.run")]
    [InlineData("notes.txt", "project.build")]
    public void BuiltInProjectCommands_WhenLanguageUnsupported_WriteHostWarning(string fileName, string entryCommand)
    {
        var output = new OutputService();
        var activeDocument = new ActiveDocumentService();
        activeDocument.SetActivePath(Path.Combine(Path.GetTempPath(), fileName));

        var context = CreateContext(output: output, activeDocument: activeDocument);
        RegisterBuiltInCommands(context);

        context.Commands.Execute(entryCommand);

        Assert.Contains(output.GetSnapshot(), e =>
            e.Channel == "Build" &&
            e.Message.Contains(entryCommand, StringComparison.OrdinalIgnoreCase) &&
            e.Message.Contains("nieobsługiwany język", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuiltInProjectBuild_WhenLanguageUnsupported_SuggestsJunexisBuildCommand()
    {
        var output = new OutputService();
        var activeDocument = new ActiveDocumentService();
        activeDocument.SetActivePath(Path.Combine(Path.GetTempPath(), "notes.txt"));

        var context = CreateContext(output: output, activeDocument: activeDocument);
        RegisterBuiltInCommands(context);

        context.Commands.Execute("project.build");

        Assert.Contains(output.GetSnapshot(), e =>
            e.Channel == "Build" &&
            e.Message.Contains("build.jnx", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuiltInProjectRun_WhenActiveFileIsJnx_DispatchesToReportApplication()
    {
        var executed = (string?)null;
        var commands = new CommandRegistry();
        commands.Register("run.jnx", () => executed = "run.jnx");
        var activeDocument = new ActiveDocumentService();
        activeDocument.SetActivePath(Path.Combine(Path.GetTempPath(), "main.jnx"));

        var context = CreateContext(commands: commands, activeDocument: activeDocument);
        RegisterBuiltInCommands(context);

        context.Commands.Execute("project.run");

        Assert.Equal("run.jnx", executed);
    }

    private static HostContext CreateContext(
        OutputService? output = null,
        CommandRegistry? commands = null,
        ActiveDocumentService? activeDocument = null)
        => new(
            eventBus: new EventBus(),
            commands: commands ?? new CommandRegistry(),
            panels: new PanelRegistry(),
            output: output ?? new OutputService(),
            processes: new TestProcessService(),
            workspace: new WorkspaceService(),
            diagnostics: new DiagnosticService(),
            activeDocument: activeDocument ?? new ActiveDocumentService(),
            openDocuments: new TestOpenDocumentRepository(),
            versionControl: null,
            views: new TestViewRegistry());

    private static void RegisterBuiltInCommands(IHostContext context)
    {
        var method = typeof(JunexisStudioHost).GetMethod(
            "RegisterBuiltInCommands",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        method!.Invoke(null, [context]);
    }
}

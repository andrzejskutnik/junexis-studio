using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;
using Junexis.Studio.Host;
using System;
using System.Threading.Tasks;
using Xunit;

namespace Junexis.Studio.Host.Tests;

/// <summary>
/// Tests for <see cref="AutoBuildExtension"/>.
/// </summary>
[Trait("Issue", "121")]
public sealed class AutoBuildExtensionTests
{
    // ── Fakes ───────────────────────────────────────────────────

    private sealed class FakePreferencesService : IPreferencesService
    {
        public JunexisPreferences Current { get; } = new();

        public string GlobalFilePath => string.Empty;
        public string? WorkspaceFilePath => null;

        public void Reload() { }
        public void SaveGlobal() { /* no-op in tests */ }
        public void SaveWorkspace() { /* no-op in tests */ }
    }

    private static HostContext CreateContext(IPreferencesService? preferences = null)
    {
        return new HostContext(
            eventBus: new EventBus(),
            commands: new CommandRegistry(),
            panels: new PanelRegistry(),
            output: new OutputService(),
            processes: new TestProcessService(),
            workspace: new WorkspaceService(),
            diagnostics: new DiagnosticService(),
            activeDocument: new ActiveDocumentService(),
            openDocuments: new TestOpenDocumentRepository(),
            versionControl: null,
            views: new TestViewRegistry(),
            preferences: preferences ?? new FakePreferencesService());
    }

    // ── Tests ───────────────────────────────────────────────────

    [Fact]
    public void Initialize_RegistersAutoBuildToggleCommand()
    {
        var ctx = CreateContext();
        var sut = new AutoBuildExtension();

        sut.Initialize(ctx);

        var cmds = ctx.Commands.GetRegisteredCommands();
        Assert.Contains(cmds, c => c.Id == "autobuild.toggle");
    }

    [Fact]
    public async Task DocumentSaved_WhenEnabled_CallsProjectBuild()
    {
        var prefs = new FakePreferencesService();
        prefs.Current.AutoBuild.Enabled = true;
        prefs.Current.AutoBuild.DebounceMs = 10;

        var ctx = CreateContext(prefs);

        var buildCalled = false;
        ctx.Commands.Register("project.build", () => buildCalled = true);

        var sut = new AutoBuildExtension();
        sut.Initialize(ctx);

        ctx.EventBus.Publish(new DocumentSaved("file.cs"));

        await Task.Delay(200);

        Assert.True(buildCalled, "project.build should have been called after DocumentSaved");
    }

    [Fact]
    public async Task DocumentSaved_WhenDisabled_DoesNotCallProjectBuild()
    {
        var prefs = new FakePreferencesService();
        prefs.Current.AutoBuild.Enabled = false;
        prefs.Current.AutoBuild.DebounceMs = 10;

        var ctx = CreateContext(prefs);

        var buildCalled = false;
        ctx.Commands.Register("project.build", () => buildCalled = true);

        var sut = new AutoBuildExtension();
        sut.Initialize(ctx);

        ctx.EventBus.Publish(new DocumentSaved("file.cs"));

        await Task.Delay(200);

        Assert.False(buildCalled, "project.build should NOT be called when auto-build is disabled");
    }

    [Fact]
    public async Task WorkspaceOpened_WhenEnabled_CallsProjectBuild()
    {
        var prefs = new FakePreferencesService();
        prefs.Current.AutoBuild.Enabled = true;
        prefs.Current.AutoBuild.DebounceMs = 10;

        var ctx = CreateContext(prefs);

        var buildCalls = 0;
        ctx.Commands.Register("project.build", () => Interlocked.Increment(ref buildCalls));

        var sut = new AutoBuildExtension();
        sut.Initialize(ctx);

        ctx.EventBus.Publish(new WorkspaceOpenedEvent("c:\\project"));

        await WaitUntilAsync(() => Volatile.Read(ref buildCalls) > 0);

        Assert.True(buildCalls > 0, "project.build should have been called after WorkspaceOpenedEvent");
    }

    [Fact]
    public async Task Debounce_MultipleRapidSaves_OnlyOneBuild()
    {
        var prefs = new FakePreferencesService();
        prefs.Current.AutoBuild.Enabled = true;
        prefs.Current.AutoBuild.DebounceMs = 100;

        var ctx = CreateContext(prefs);

        var buildCount = 0;
        ctx.Commands.Register("project.build", () => Interlocked.Increment(ref buildCount));

        var sut = new AutoBuildExtension();
        sut.Initialize(ctx);

        // Rapid saves — debounce should coalesce them into one build
        for (int i = 0; i < 5; i++)
            ctx.EventBus.Publish(new DocumentSaved($"file{i}.cs"));

        await WaitUntilAsync(() => Volatile.Read(ref buildCount) > 0);
        await Task.Delay(200);

        Assert.Equal(1, Volatile.Read(ref buildCount));
    }

    [Fact]
    public async Task Shutdown_AfterInit_StopsTriggeringBuild()
    {
        var prefs = new FakePreferencesService();
        prefs.Current.AutoBuild.Enabled = true;
        prefs.Current.AutoBuild.DebounceMs = 10;

        var ctx = CreateContext(prefs);

        var buildCalled = false;
        ctx.Commands.Register("project.build", () => buildCalled = true);

        var sut = new AutoBuildExtension();
        sut.Initialize(ctx);
        sut.Shutdown();

        ctx.EventBus.Publish(new DocumentSaved("file.cs"));

        await Task.Delay(200);

        Assert.False(buildCalled, "project.build should NOT be called after Shutdown");
    }

    [Fact]
    public void Toggle_WhenDisabled_EnablesAutoBuild()
    {
        var prefs = new FakePreferencesService();
        prefs.Current.AutoBuild.Enabled = false;

        var ctx = CreateContext(prefs);
        var sut = new AutoBuildExtension();
        sut.Initialize(ctx);

        ctx.Commands.Execute("autobuild.toggle");

        Assert.True(prefs.Current.AutoBuild.Enabled);
    }

    [Fact]
    public void Toggle_WhenEnabled_DisablesAutoBuild()
    {
        var prefs = new FakePreferencesService();
        prefs.Current.AutoBuild.Enabled = true;

        var ctx = CreateContext(prefs);
        var sut = new AutoBuildExtension();
        sut.Initialize(ctx);

        ctx.Commands.Execute("autobuild.toggle");

        Assert.False(prefs.Current.AutoBuild.Enabled);
    }

    [Fact]
    public void Initialize_NullContext_Throws()
    {
        var sut = new AutoBuildExtension();
        Assert.Throws<ArgumentNullException>(() => sut.Initialize(null!));
    }

    [Fact]
    public void JunexisStudioHost_RegistersProjectBuildCommand()
    {
        var host = new JunexisStudioHost();

        var cmds = host.Context.Commands.GetRegisteredCommands();

        Assert.Contains(cmds, c => c.Id == "project.build");
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var timeout = DateTimeOffset.UtcNow.AddSeconds(2);
        while (!condition())
        {
            if (DateTimeOffset.UtcNow >= timeout)
                throw new TimeoutException("Expected asynchronous AutoBuild action did not complete.");
            await Task.Delay(10);
        }
    }
}

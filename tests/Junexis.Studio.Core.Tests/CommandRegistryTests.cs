using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;
using System.Linq;
using Xunit;

namespace Junexis.Studio.Core.Tests;

public sealed class CommandRegistryTests
{
    [Fact]
    [Trait("Issue", "8")]
    public void Register_WithEmptyId_Throws()
    {
        var sut = new CommandRegistry();
        Assert.Throws<ArgumentException>(() => sut.Register(" ", () => { }));
    }

    [Fact]
    [Trait("Issue", "8")]
    public void Register_WithNullAction_Throws()
    {
        var sut = new CommandRegistry();
        Assert.Throws<ArgumentNullException>(() => sut.Register("x", null!));
    }

    [Fact]
    [Trait("Issue", "8")]
    public void Register_WithEmptyMenuPath_Throws()
    {
        var sut = new CommandRegistry();
        Assert.Throws<ArgumentException>(() => sut.Register("x", () => { }, " "));
    }

    [Fact]
    [Trait("Issue", "8")]
    public void CanExecute_UnknownCommand_ReturnsFalse()
    {
        var sut = new CommandRegistry();
        Assert.False(sut.CanExecute("missing"));
    }

    [Fact]
    [Trait("Issue", "8")]
    public void Execute_UnknownCommand_Throws()
    {
        var sut = new CommandRegistry();
        Assert.Throws<InvalidOperationException>(() => sut.Execute("missing"));
    }

    [Fact]
    [Trait("Issue", "8")]
    public void Execute_RegisteredCommand_InvokesAction()
    {
        var sut = new CommandRegistry();
        var called = 0;
        sut.Register("cmd", () => called++);

        sut.Execute("cmd");

        Assert.Equal(1, called);
    }

    [Fact]
    [Trait("Issue", "8")]
    public void GetRegisteredCommands_IncludesMenuPathIfProvided()
    {
        var sut = new CommandRegistry();
        sut.Register("app.ping", () => { }, "Help/Ping");

        var registered = sut.GetRegisteredCommands();
        var ping = Assert.Single(registered, c => c.Id == "app.ping");
        Assert.Equal("Help/Ping", ping.MenuPath);
    }

    [Fact]
    [Trait("Issue", "8")]
    public void Register_OverwritesMenuPathOnReRegister()
    {
        var sut = new CommandRegistry();
        sut.Register("cmd", () => { }, "File/Old");
        sut.Register("cmd", () => { }, "File/New");

        var registered = sut.GetRegisteredCommands();
        var cmd = Assert.Single(registered, c => c.Id == "cmd");
        Assert.Equal("File/New", cmd.MenuPath);
    }

    [Fact]
    [Trait("Issue", "40")]
    public void Execute_WhenActionThrows_WithCallback_DoesNotPropagate()
    {
        string? failedId = null;
        Exception? captured = null;
        var sut = new CommandRegistry((id, ex) => { failedId = id; captured = ex; });
        sut.Register("bad", () => throw new InvalidOperationException("kaboom"));

        sut.Execute("bad");

        Assert.Equal("bad", failedId);
        Assert.NotNull(captured);
        Assert.Equal("kaboom", captured!.Message);
    }

    [Fact]
    [Trait("Issue", "40")]
    public void Execute_WhenActionThrows_WithoutCallback_Propagates()
    {
        var sut = new CommandRegistry();
        sut.Register("bad", () => throw new InvalidOperationException("kaboom"));

        Assert.Throws<InvalidOperationException>(() => sut.Execute("bad"));
    }

    [Fact]
    [Trait("Issue", "48")]
    public void Execute_FiresCommandExecutingBeforeRun()
    {
        var sut = new CommandRegistry();
        string? executingId = null;
        sut.CommandExecuting += id => executingId = id;
        sut.Register("cmd", () => { });

        sut.Execute("cmd");

        Assert.Equal("cmd", executingId);
    }

    [Fact]
    [Trait("Issue", "48")]
    public void Execute_FiresCommandExecutedAfterRun_WithElapsed()
    {
        var sut = new CommandRegistry();
        CommandExecutedInfo? info = null;
        sut.CommandExecuted += i => info = i;
        sut.Register("cmd", () => { });

        sut.Execute("cmd");

        Assert.NotNull(info);
        Assert.Equal("cmd", info!.CommandId);
        Assert.True(info.Elapsed >= TimeSpan.Zero);
        Assert.Null(info.Exception);
    }

    [Fact]
    [Trait("Issue", "48")]
    public void Execute_WhenThrows_CommandExecutedContainsException()
    {
        var sut = new CommandRegistry((_, _) => { });
        CommandExecutedInfo? info = null;
        sut.CommandExecuted += i => info = i;
        sut.Register("bad", () => throw new InvalidOperationException("fail"));

        sut.Execute("bad");

        Assert.NotNull(info);
        Assert.NotNull(info!.Exception);
        Assert.Equal("fail", info.Exception!.Message);
    }

    [Fact]
    [Trait("Issue", "51")]
    public void Register_WithDisplayNameAndCategory_ReflectedInSnapshot()
    {
        var sut = new CommandRegistry();
        sut.Register("build.run", () => { }, "Build/Run", "Run Build", "Build");

        var registered = sut.GetRegisteredCommands();
        var cmd = Assert.Single(registered, c => c.Id == "build.run");
        Assert.Equal("Build/Run", cmd.MenuPath);
        Assert.Equal("Run Build", cmd.DisplayName);
        Assert.Equal("Build", cmd.Category);
    }

    [Fact]
    [Trait("Issue", "51")]
    public void Register_WithoutDisplayName_DefaultsToNull()
    {
        var sut = new CommandRegistry();
        sut.Register("cmd", () => { });

        var registered = sut.GetRegisteredCommands();
        var cmd = Assert.Single(registered, c => c.Id == "cmd");
        Assert.Null(cmd.DisplayName);
        Assert.Null(cmd.Category);
    }

    // --- #47 CommandContext ---

    [Fact]
    [Trait("Issue", "47")]
    public void RegisterContextual_ExecuteWithContext_ReceivesContext()
    {
        var sut = new CommandRegistry();
        CommandContext? received = null;
        sut.Register("ctx.cmd", ctx => received = ctx);

        var context = new CommandContext(ActiveDocumentPath: "/file.nx", WorkspacePath: "/ws");
        sut.Execute("ctx.cmd", context);

        Assert.NotNull(received);
        Assert.Equal("/file.nx", received!.ActiveDocumentPath);
        Assert.Equal("/ws", received.WorkspacePath);
    }

    [Fact]
    [Trait("Issue", "47")]
    public void RegisterContextual_CanExecute_UsesPredicateWithContext()
    {
        var sut = new CommandRegistry();
        sut.Register("ctx.cmd",
            ctx => { },
            canExecute: ctx => ctx.ActiveDocumentPath is not null);

        Assert.True(sut.CanExecute("ctx.cmd", new CommandContext(ActiveDocumentPath: "/f.nx")));
        Assert.False(sut.CanExecute("ctx.cmd", new CommandContext()));
    }

    [Fact]
    [Trait("Issue", "47")]
    public void RegisterContextual_WithoutPredicate_CanExecuteReturnsTrue()
    {
        var sut = new CommandRegistry();
        sut.Register("ctx.cmd", ctx => { });

        Assert.True(sut.CanExecute("ctx.cmd", CommandContext.Empty));
    }

    [Fact]
    [Trait("Issue", "47")]
    public void RegisterContextual_ExecuteWithoutContext_UsesEmpty()
    {
        var sut = new CommandRegistry();
        CommandContext? received = null;
        sut.Register("ctx.cmd", ctx => received = ctx);

        sut.Execute("ctx.cmd");

        Assert.NotNull(received);
        Assert.Same(CommandContext.Empty, received);
    }

    [Fact]
    [Trait("Issue", "47")]
    public void RegisterContextual_WithNullExecute_Throws()
    {
        var sut = new CommandRegistry();
        Assert.Throws<ArgumentNullException>(() =>
            sut.Register("ctx.cmd", (Action<CommandContext>)null!));
    }

    [Fact]
    [Trait("Issue", "100")]
    public void Register_WithContextTarget_IsIncludedInGetRegisteredCommands()
    {
        var sut = new CommandRegistry();

        sut.Register(
            id: "explorer.copyPath",
            execute: () => { },
            menuPath: null,
            displayName: "Copy Path",
            category: "Explorer",
            contextTarget: "explorer");

        var registrations = sut.GetRegisteredCommands();
        var reg = Assert.Single(registrations, r => r.Id == "explorer.copyPath");
        Assert.Equal("explorer", reg.ContextTarget);
        Assert.Equal("Copy Path", reg.DisplayName);
        Assert.Null(reg.MenuPath);
    }

    [Fact]
    [Trait("Issue", "100")]
    public void GetRegisteredCommands_ContextTargetIsNull_WhenNotSet()
    {
        var sut = new CommandRegistry();
        sut.Register("cmd.nocontext", () => { }, menuPath: "File/Foo");

        var reg = Assert.Single(sut.GetRegisteredCommands(), r => r.Id == "cmd.nocontext");
        Assert.Null(reg.ContextTarget);
    }

    [Fact]
    [Trait("Issue", "100")]
    public void GetRegisteredCommands_CanFilterByContextTarget()
    {
        var sut = new CommandRegistry();
        sut.Register("explorer.a", () => { }, menuPath: null, displayName: "A", category: null, contextTarget: "explorer");
        sut.Register("explorer.b", () => { }, menuPath: null, displayName: "B", category: null, contextTarget: "explorer");
        sut.Register("editor.x", () => { }, menuPath: null, displayName: "X", category: null, contextTarget: "editor");
        sut.Register("global.y", () => { }, menuPath: "File/Y");

        var explorerItems = sut.GetRegisteredCommands()
            .Where(c => c.ContextTarget == "explorer")
            .ToList();

        Assert.Equal(2, explorerItems.Count);
        Assert.All(explorerItems, r => Assert.Equal("explorer", r.ContextTarget));
    }

    [Fact]
    [Trait("Issue", "100")]
    public void Register_WithContextTarget_CommandIsExecutable()
    {
        var executed = false;
        var sut = new CommandRegistry();
        sut.Register(
            id: "explorer.reveal",
            execute: () => executed = true,
            menuPath: null,
            displayName: "Reveal",
            category: null,
            contextTarget: "explorer");

        sut.Execute("explorer.reveal");

        Assert.True(executed);
    }
}

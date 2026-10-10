using Junexis.Studio.Abstractions;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Junexis.Studio.Host;

/// <summary>
/// Rozszerzenie auto-build: wyzwala budowanie projektu po zapisaniu pliku dokumentu
/// lub otwarciu workspace, z debouncingiem konfigurowalnym przez preferencje.
/// <para>
/// Funkcja jest domyślnie wyłączona. Włącza/wyłącza się komendą <c>autobuild.toggle</c>
/// lub przez zmianę <see cref="AutoBuildPreferences.Enabled"/> w preferencjach.
/// </para>
/// </summary>
internal sealed class AutoBuildExtension : IJunexisExtension
{
    /// <summary>Kanał wyjściowy dla komunikatów auto-build.</summary>
    public const string OutputChannel = "AutoBuild";

    private IHostContext? _context;
    private CancellationTokenSource? _debounceCts;
    private readonly object _lock = new();

    private Action<DocumentSaved>? _docSavedHandler;
    private Action<WorkspaceOpenedEvent>? _workspaceOpenedHandler;

    /// <inheritdoc />
    public string Id => "junexis.autobuild";

    /// <inheritdoc />
    public void Initialize(IHostContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;

        _docSavedHandler = _ => ScheduleBuild();
        _workspaceOpenedHandler = _ => ScheduleBuild();

        context.EventBus.Subscribe(_docSavedHandler);
        context.EventBus.Subscribe(_workspaceOpenedHandler);

        context.Commands.Register(
            id: "autobuild.toggle",
            execute: ToggleAutoBuild,
            menuPath: "Run/Toggle Auto-Build",
            displayName: "Toggle Auto-Build",
            category: "Build");

        context.Output.Write("Extensions", "AutoBuildExtension loaded");
    }

    /// <inheritdoc />
    public void Shutdown()
    {
        var ctx = _context;

        if (ctx is not null)
        {
            if (_docSavedHandler is not null)
                ctx.EventBus.Unsubscribe(_docSavedHandler);

            if (_workspaceOpenedHandler is not null)
                ctx.EventBus.Unsubscribe(_workspaceOpenedHandler);
        }

        lock (_lock)
        {
            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
            _debounceCts = null;
        }

        _context = null;
    }

    private void ScheduleBuild()
    {
        var ctx = _context;
        if (ctx is null || !ctx.Preferences.Current.AutoBuild.Enabled)
            return;

        CancellationTokenSource newCts;

        lock (_lock)
        {
            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
            _debounceCts = newCts = new CancellationTokenSource();
        }

        var debounceMs = ctx.Preferences.Current.AutoBuild.DebounceMs;

        _ = Task.Run(async () =>
        {
            try
            {
                if (debounceMs > 0)
                    await Task.Delay(debounceMs, newCts.Token).ConfigureAwait(false);

                if (newCts.Token.IsCancellationRequested)
                    return;

                var currentCtx = _context;
                if (currentCtx is null)
                    return;

                if (!currentCtx.Commands.CanExecute("project.build"))
                {
                    currentCtx.Output.Write(OutputChannel,
                        "Auto-build: komenda 'project.build' niedostępna.",
                        OutputSeverity.Warning);
                    return;
                }

                currentCtx.Output.Write(OutputChannel, "Auto-build wyzwolony.", OutputSeverity.Info);
                currentCtx.Commands.Execute("project.build");
            }
            catch (OperationCanceledException)
            {
                // debounce anulowany — normalna sytuacja
            }
            catch (Exception ex)
            {
                _context?.Output.Write(OutputChannel,
                    $"Auto-build: błąd: {ex.Message}", OutputSeverity.Error);
            }
        });
    }

    private void ToggleAutoBuild()
    {
        var ctx = _context;
        if (ctx is null)
            return;

        var prefs = ctx.Preferences.Current;
        prefs.AutoBuild.Enabled = !prefs.AutoBuild.Enabled;

        try
        {
            ctx.Preferences.SaveGlobal();
        }
        catch (Exception ex)
        {
            ctx.Output.Write(OutputChannel,
                $"Auto-build toggle: nie udało się zapisać preferencji: {ex.Message}",
                OutputSeverity.Warning);
        }

        ctx.Output.Write(OutputChannel,
            $"Auto-build {(prefs.AutoBuild.Enabled ? "włączony" : "wyłączony")}.");
    }
}

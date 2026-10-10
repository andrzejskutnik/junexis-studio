using Junexis.Studio.Abstractions;
using System.Diagnostics;

namespace Junexis.Studio.Core;

/// <summary>
/// Implementacja <see cref="ICommandRegistry"/> oparta o słownik id→akcja.
/// Obsługuje komendy bezkonkekstowe (v0.1) i kontekstowe (v0.2).
/// </summary>
public sealed class CommandRegistry : ICommandRegistry
{
    private sealed record CommandEntry(
        Action? Execute,
        Action<CommandContext>? ExecuteCtx,
        Func<bool>? CanExecuteSimple,
        Func<CommandContext, bool>? CanExecuteCtx,
        string? MenuPath,
        string? DisplayName,
        string? Category,
        string? ContextTarget);

    private readonly Dictionary<string, CommandEntry> _commands = new(StringComparer.Ordinal);
    private readonly Action<string, Exception>? _onExecuteError;

    /// <summary>
    /// Zdarzenie emitowane tuż przed wykonaniem komendy.
    /// </summary>
    public event Action<string>? CommandExecuting;

    /// <summary>
    /// Zdarzenie emitowane po zakończeniu wykonania komendy.
    /// </summary>
    public event Action<CommandExecutedInfo>? CommandExecuted;

    /// <summary>
    /// Tworzy CommandRegistry bez callbacku błędów (wyjątki lecą wyżej).
    /// </summary>
    public CommandRegistry() : this(null) { }

    /// <summary>
    /// Tworzy CommandRegistry z opcjonalnym callbackiem wywoływanym,
    /// gdy wykonanie komendy rzuci wyjątek.
    /// </summary>
    /// <param name="onExecuteError">Callback (commandId, exception). Może być <see langword="null"/>.</param>
    public CommandRegistry(Action<string, Exception>? onExecuteError)
    {
        _onExecuteError = onExecuteError;
    }

    /// <inheritdoc />
    public void Register(string id, Action execute)
    {
        RegisterCore(id, execute, executeCtx: null, canExecuteSimple: null, canExecuteCtx: null, menuPath: null, displayName: null, category: null, contextTarget: null);
    }

    /// <inheritdoc />
    public void Register(string id, Action execute, string menuPath)
    {
        if (string.IsNullOrWhiteSpace(menuPath))
        {
            throw new ArgumentException("Menu path cannot be empty.", nameof(menuPath));
        }

        RegisterCore(id, execute, executeCtx: null, canExecuteSimple: null, canExecuteCtx: null, menuPath, displayName: null, category: null, contextTarget: null);
    }

    /// <inheritdoc />
    public void Register(string id, Action execute, string? menuPath, string? displayName, string? category)
    {
        RegisterCore(id, execute, executeCtx: null, canExecuteSimple: null, canExecuteCtx: null, menuPath, displayName, category, contextTarget: null);
    }

    /// <inheritdoc />
    public void Register(string id, Action execute, string? menuPath, string? displayName, string? category, string? contextTarget)
    {
        RegisterCore(id, execute, executeCtx: null, canExecuteSimple: null, canExecuteCtx: null, menuPath, displayName, category, contextTarget);
    }

    /// <inheritdoc />
    public void Register(string id, Action execute, Func<bool>? canExecute, string? menuPath = null, string? displayName = null, string? category = null)
    {
        RegisterCore(id, execute, executeCtx: null, canExecuteSimple: canExecute, canExecuteCtx: null, menuPath, displayName, category, contextTarget: null);
    }

    /// <inheritdoc />
    public void Register(string id, Action<CommandContext> execute, Func<CommandContext, bool>? canExecute = null,
        string? menuPath = null, string? displayName = null, string? category = null)
    {
        if (execute is null)
        {
            throw new ArgumentNullException(nameof(execute));
        }

        RegisterCore(id, executePlain: null, execute, canExecuteSimple: null, canExecuteCtx: canExecute, menuPath, displayName, category, contextTarget: null);
    }

    /// <inheritdoc />
    public IReadOnlyList<CommandRegistration> GetRegisteredCommands()
    {
        return _commands
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => new CommandRegistration(p.Key, p.Value.MenuPath, p.Value.DisplayName, p.Value.Category, p.Value.ContextTarget))
            .ToArray();
    }

    /// <inheritdoc />
    public bool CanExecute(string id)
    {
        return CanExecute(id, CommandContext.Empty);
    }

    /// <inheritdoc />
    public bool CanExecute(string id, CommandContext context)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        if (!_commands.TryGetValue(id, out var entry))
        {
            return false;
        }

        if (entry.CanExecuteSimple is not null)
        {
            return entry.CanExecuteSimple();
        }

        if (entry.CanExecuteCtx is not null)
        {
            return entry.CanExecuteCtx(context);
        }

        return true;
    }

    /// <inheritdoc />
    public void Execute(string id)
    {
        Execute(id, CommandContext.Empty);
    }

    /// <inheritdoc />
    public void Execute(string id, CommandContext context)
    {
        if (!_commands.TryGetValue(id, out var entry))
        {
            throw new InvalidOperationException($"Command '{id}' not registered.");
        }

        CommandExecuting?.Invoke(id);

        var sw = Stopwatch.StartNew();
        Exception? error = null;

        try
        {
            if (entry.ExecuteCtx is not null)
            {
                entry.ExecuteCtx(context);
            }
            else
            {
                entry.Execute!();
            }
        }
        catch (Exception ex) when (_onExecuteError is not null)
        {
            error = ex;
            _onExecuteError.Invoke(id, ex);
        }
        catch (Exception ex)
        {
            error = ex;
            throw;
        }
        finally
        {
            sw.Stop();
            CommandExecuted?.Invoke(new CommandExecutedInfo(id, sw.Elapsed, error));
        }
    }

    private void RegisterCore(string id, Action? executePlain, Action<CommandContext>? executeCtx,
        Func<bool>? canExecuteSimple, Func<CommandContext, bool>? canExecuteCtx, string? menuPath, string? displayName, string? category,
        string? contextTarget)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Command id cannot be empty.", nameof(id));
        }

        if (executePlain is null && executeCtx is null)
        {
            throw new ArgumentNullException(nameof(executePlain), "Either plain or contextual execute must be provided.");
        }

        _commands[id] = new CommandEntry(executePlain, executeCtx, canExecuteSimple, canExecuteCtx, menuPath, displayName, category, contextTarget);
    }
}

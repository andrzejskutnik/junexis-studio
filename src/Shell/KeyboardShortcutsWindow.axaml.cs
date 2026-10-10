using Avalonia.Controls;
using Junexis.Studio.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Junexis.Studio.Shell;

/// <summary>Single row in the keyboard shortcuts list.</summary>
public sealed record ShortcutEntry(string Gesture, string DisplayName, string Category);

/// <summary>Read-only reference window listing all registered keybindings.</summary>
public partial class KeyboardShortcutsWindow : Window
{
    private IReadOnlyList<ShortcutEntry> _allEntries = [];

    /// <summary>Parameterless constructor for Avalonia XAML loader.</summary>
    public KeyboardShortcutsWindow()
    {
        InitializeComponent();
        Icon = AppIconFactory.Icon;
    }

    /// <summary>Creates the window and populates the shortcuts list from the host context.</summary>
    public KeyboardShortcutsWindow(IHostContext context) : this()
    {
        _allEntries = BuildEntries(context);
        ShortcutList.ItemsSource = _allEntries;
    }

    private void OnFilterChanged(object? sender, Avalonia.Controls.TextChangedEventArgs e)
    {
        var filter = FilterBox.Text?.Trim() ?? string.Empty;
        ShortcutList.ItemsSource = string.IsNullOrEmpty(filter)
            ? _allEntries
            : _allEntries.Where(s =>
                s.Gesture.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                s.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                s.Category.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private static IReadOnlyList<ShortcutEntry> BuildEntries(IHostContext context)
    {
        var commands = context.Commands.GetRegisteredCommands()
            .ToDictionary(c => c.Id, StringComparer.Ordinal);

        return context.Keybindings.GetBindings()
            .Select(kvp =>
            {
                commands.TryGetValue(kvp.Value, out var reg);
                var name = reg?.DisplayName ?? kvp.Value;
                var category = reg?.Category ?? string.Empty;
                return new ShortcutEntry(kvp.Key.ToString(), name, category);
            })
            .OrderBy(s => s.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

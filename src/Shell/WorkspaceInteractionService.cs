using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Shell;

/// <summary>
/// Implementacja <see cref="IWorkspaceInteractionService"/> oparta o okno Shell i natywne dialogi Avalonia.
/// </summary>
public sealed class WorkspaceInteractionService : IWorkspaceInteractionService
{
    private readonly Window _owner;

    /// <summary>
    /// Tworzy serwis interakcji UI dla operacji na plikach workspace.
    /// </summary>
    /// <param name="owner">Okno właściciela dialogów.</param>
    public WorkspaceInteractionService(Window owner)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
    }

    /// <inheritdoc />
    public Task<string?> PromptForTextAsync(string title, string message, string initialValue)
    {
        return TextPromptWindow.ShowAndGetResultAsync(_owner, title, message, initialValue);
    }

    /// <inheritdoc />
    public async Task<string?> PickFolderAsync(string title)
    {
        if (_owner.StorageProvider is null)
        {
            return null;
        }

        IReadOnlyList<IStorageFolder> folders = await _owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
        });

        return folders.FirstOrDefault()?.TryGetLocalPath();
    }

    /// <inheritdoc />
    public Task<bool> ConfirmAsync(string title, string message)
    {
        return ConfirmWindow.ShowAndGetResultAsync(_owner, title, message);
    }
}
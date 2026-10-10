using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Host;

/// <summary>
/// Węzeł drzewa w Explorerze (folder lub plik).
/// Implementuje <see cref="INotifyPropertyChanged"/> aby umożliwić dwukierunkowe
/// wiązanie stanu rozwinięcia (<see cref="IsExpanded"/>).
/// </summary>
public sealed class ExplorerNode : INotifyPropertyChanged
{
    private bool _isExpanded;

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Tworzy węzeł drzewa Explorer.
    /// </summary>
    /// <param name="name">Nazwa wyświetlana.</param>
    /// <param name="fullPath">Pełna ścieżka do pliku lub folderu.</param>
    /// <param name="isDirectory"><see langword="true"/> jeśli węzeł reprezentuje folder.</param>
    /// <param name="children">Dzieci (dla folderów).</param>
    /// <param name="versionControlStatus">Opcjonalny status VCS (dla plików).</param>
    /// <param name="isExpanded">Czy węzeł jest wstępnie rozwinięty (domyślnie <see langword="false"/>).</param>
    public ExplorerNode(
        string name,
        string fullPath,
        bool isDirectory,
        IReadOnlyList<ExplorerNode> children,
        VersionControlFileStatus? versionControlStatus = null,
        bool isExpanded = false)
    {
        Name = name;
        FullPath = fullPath;
        IsDirectory = isDirectory;
        Children = children;
        VersionControlStatus = versionControlStatus;
        _isExpanded = isExpanded;
    }

    /// <summary>
    /// Nazwa wyświetlana w UI.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Nazwa z dekoracją statusu (jeśli dostępne).
    /// </summary>
    public string DisplayName
    {
        get
        {
            if (IsDirectory || VersionControlStatus is null)
            {
                return Name;
            }

            var tag = VersionControlStatus.Value switch
            {
                VersionControlFileStatus.Modified => "M",
                VersionControlFileStatus.Staged => "S",
                VersionControlFileStatus.Untracked => "?",
                VersionControlFileStatus.Ignored => "I",
                _ => " ",
            };

            return tag == " " ? Name : $"{Name} [{tag}]";
        }
    }

    /// <summary>
    /// Pełna ścieżka do pliku lub folderu.
    /// </summary>
    public string FullPath { get; }

    /// <summary>
    /// Czy węzeł jest rozwinięty w widoku drzewa.
    /// Właściwość jest zapisywalna, aby wiązanie TwoWay z TreeViewItem mogło aktualizować stan.
    /// </summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
        }
    }

    /// <summary>
    /// Czy węzeł reprezentuje folder.
    /// </summary>
    public bool IsDirectory { get; }

    /// <summary>
    /// Dzieci węzła (dla folderów).
    /// </summary>
    public IReadOnlyList<ExplorerNode> Children { get; }

    /// <summary>
    /// Opcjonalny status VCS dla pliku.
    /// </summary>
    public VersionControlFileStatus? VersionControlStatus { get; }

    /// <summary>
    /// Ikona węzła jako znak Unicode zależna od typu i rozszerzenia.
    /// Folder: "📁", pliki: symbol wg rozszerzenia, domyślnie "📄".
    /// </summary>
    public string IconText
    {
        get
        {
            if (IsDirectory)
                return "📁";

            return Path.GetExtension(Name).ToLowerInvariant() switch
            {
                ".cs" => "♯",
                ".axaml" => "🖼",
                ".xaml" => "🖼",
                ".json" => "🛠",
                ".md" => "📝",
                ".txt" => "📄",
                ".sln" => "◈",
                ".csproj" => "◈",
                ".toml" => "◈",
                ".yaml" or ".yml" => "◈",
                ".sh" or ".ps1" => "⌨",
                ".go" => "◈",
                ".py" => "🐍",
                _ => "📄",
            };
        }
    }

    /// <summary>Hex color for the file icon based on extension; <see langword="null"/> means use default theme color.</summary>
    public string? FileIconColor
    {
        get
        {
            if (IsDirectory) return null;
            return Path.GetExtension(Name).ToLowerInvariant() switch
            {
                ".jnx" => "#4EC9B0",
                ".cs" or ".axaml" or ".xaml" => "#9B59D6",
                ".cpp" or ".c" or ".h" or ".hpp" => "#649ED1",
                ".go" => "#6AD7E5",
                ".ts" or ".tsx" => "#3178C6",
                ".js" or ".jsx" => "#F7DF1E",
                ".json" or ".toml" => "#F9A825",
                ".yaml" or ".yml" => "#F9A825",
                ".py" => "#3572A5",
                ".md" => "#D4D4D4",
                _ => null,
            };
        }
    }

    /// <summary>True for Junexis source files, so the explorer can show the Junexis mark instead of a generic file glyph.</summary>
    public bool IsJunexisSource =>
        !IsDirectory && string.Equals(Path.GetExtension(Name), ".jnx", StringComparison.OrdinalIgnoreCase);

    /// <summary>True for files that use the generic file glyph (not a directory, not a Junexis source file).</summary>
    public bool IsGenericFile => !IsDirectory && !IsJunexisSource;
}

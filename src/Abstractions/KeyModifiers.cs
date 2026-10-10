namespace Junexis.Studio.Abstractions;

/// <summary>
/// Klawisze modyfikujące (Ctrl, Shift, Alt).
/// Flagi można łączyć (<c>KeyModifiers.Ctrl | KeyModifiers.Shift</c>).
/// </summary>
[Flags]
public enum KeyModifiers
{
    /// <summary>Brak modyfikatora.</summary>
    None = 0,

    /// <summary>Ctrl (lub Cmd na macOS).</summary>
    Ctrl = 1,

    /// <summary>Shift.</summary>
    Shift = 2,

    /// <summary>Alt (lub Option na macOS).</summary>
    Alt = 4,
}

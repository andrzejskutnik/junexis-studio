namespace Junexis.Studio.Abstractions;

/// <summary>
/// Skrót klawiaturowy przypisany do komendy.
/// </summary>
/// <param name="Key">Klawisz główny (case-insensitive, np. "S", "F5", "O").</param>
/// <param name="Modifiers">Klawisze modyfikujące.</param>
public sealed record KeyGesture(string Key, KeyModifiers Modifiers = KeyModifiers.None)
{
    /// <summary>
    /// Zwraca czytelną reprezentację tekstową (np. "Ctrl+Shift+S").
    /// </summary>
    public override string ToString()
    {
        var parts = new List<string>(4);

        if (Modifiers.HasFlag(KeyModifiers.Ctrl)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(KeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(KeyModifiers.Shift)) parts.Add("Shift");

        parts.Add(Key.ToUpperInvariant());

        return string.Join("+", parts);
    }
}

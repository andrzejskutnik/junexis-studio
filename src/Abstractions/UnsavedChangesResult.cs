namespace Junexis.Studio.Abstractions;

/// <summary>
/// Wynik dialogu potwierdzenia zamknięcia dokumentu z niezapisanymi zmianami.
/// </summary>
public enum UnsavedChangesResult
{
    /// <summary>Zapisz zmiany przed zamknięciem.</summary>
    Save = 0,

    /// <summary>Porzuć zmiany i zamknij.</summary>
    Discard = 1,

    /// <summary>Anuluj — nie zamykaj dokumentu.</summary>
    Cancel = 2,
}

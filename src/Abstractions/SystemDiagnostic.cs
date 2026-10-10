namespace Junexis.Studio.Abstractions;

/// <summary>
/// Niemutowalny wpis diagnostyki systemowej — błędy infrastruktury oddzielone od zwykłych logów.
/// </summary>
/// <param name="Category">Kategoria zdarzenia (np. ExtensionLoad, HostError).</param>
/// <param name="Message">Treść komunikatu.</param>
/// <param name="Timestamp">Czas wystąpienia zdarzenia.</param>
/// <param name="Source">Opcjonalne źródło (np. „Host", id rozszerzenia).</param>
/// <param name="ExceptionText">Opcjonalny pełny tekst wyjątku.</param>
public sealed record SystemDiagnostic(
    SystemDiagnosticCategory Category,
    string Message,
    DateTime Timestamp,
    string? Source = null,
    string? ExceptionText = null);

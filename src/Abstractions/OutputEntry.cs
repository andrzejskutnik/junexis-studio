namespace Junexis.Studio.Abstractions;

/// <summary>
/// Pojedynczy wpis Output (kanał + wiadomość + znacznik czasu + opcjonalne metadane).
/// </summary>
/// <param name="Channel">Kanał logiczny (np. "Core", "Extensions", "Workspace").</param>
/// <param name="Message">Treść wpisu.</param>
/// <param name="Timestamp">Znacznik czasu wpisu (UTC).</param>
/// <param name="Severity">Poziom istotności wpisu.</param>
/// <param name="Source">Identyfikator źródła wpisu (np. extension id, subsystem). Może być <see langword="null"/>.</param>
/// <param name="ExceptionText">Bezpieczna tekstowa reprezentacja wyjątku (bez sekretów). Może być <see langword="null"/>.</param>
public sealed record OutputEntry(
    string Channel,
    string Message,
    DateTime Timestamp,
    OutputSeverity Severity = OutputSeverity.Info,
    string? Source = null,
    string? ExceptionText = null);

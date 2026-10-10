using System;

namespace Junexis.Studio.Abstractions;

/// <summary>
/// Niemutowalny wpis diagnostyczny (błąd/ostrzeżenie) z opcjonalną lokacją.
/// </summary>
public sealed record Diagnostic
{
    /// <summary>
    /// Tworzy diagnostykę.
    /// </summary>
    /// <param name="severity">Poziom ważności.</param>
    /// <param name="message">Treść komunikatu.</param>
    /// <param name="filePath">Opcjonalna ścieżka do pliku.</param>
    /// <param name="line">Opcjonalna linia (1-based).</param>
    /// <param name="column">Opcjonalna kolumna (1-based).</param>
    /// <param name="source">Optional owner/source identifier (e.g. CompilerId, McpConnectorId).</param>
    /// <param name="code">Optional stable diagnostic code supplied by the producer.</param>
    public Diagnostic(
        DiagnosticSeverity severity,
        string message,
        string? filePath = null,
        int? line = null,
        int? column = null,
        string? source = null,
        string? code = null)
    {
        Severity = severity;
        Message = message ?? throw new ArgumentNullException(nameof(message));
        FilePath = filePath;
        Line = line;
        Column = column;
        Source = source;
        Code = code;
    }

    /// <summary>
    /// Poziom ważności.
    /// </summary>
    public DiagnosticSeverity Severity { get; }

    /// <summary>
    /// Treść komunikatu.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// Opcjonalna ścieżka do pliku.
    /// </summary>
    public string? FilePath { get; }

    /// <summary>
    /// Opcjonalna linia (1-based).
    /// </summary>
    public int? Line { get; }

    /// <summary>
    /// Opcjonalna kolumna (1-based).
    /// </summary>
    public int? Column { get; }

    /// <summary>
    /// Optional owner/source identifier (e.g. CompilerId, McpConnectorId).
    /// Used to correlate diagnostics with their producer.
    /// </summary>
    public string? Source { get; }

    /// <summary>Optional stable diagnostic code, for example <c>E5001</c>.</summary>
    public string? Code { get; }
}

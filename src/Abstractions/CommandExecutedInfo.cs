namespace Junexis.Studio.Abstractions;

/// <summary>
/// Informacja o zakończonym wykonaniu komendy (after-execute hook).
/// </summary>
/// <param name="CommandId">Identyfikator komendy.</param>
/// <param name="Elapsed">Czas wykonania.</param>
/// <param name="Exception">Wyjątek (jeśli komenda się nie powiodła) lub <see langword="null"/>.</param>
public sealed record CommandExecutedInfo(string CommandId, TimeSpan Elapsed, Exception? Exception);

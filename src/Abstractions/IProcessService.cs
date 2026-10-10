using System;
using System.Threading;
using System.Threading.Tasks;

namespace Junexis.Studio.Abstractions;

/// <summary>
/// Uruchamia procesy zewnętrzne w sposób asynchroniczny, ze streamingiem stdout/stderr.
/// </summary>
public interface IProcessService
{
    /// <summary>
    /// Uruchamia proces i streamuje output linia-po-linii.
    /// </summary>
    /// <param name="request">Parametry uruchomienia procesu.</param>
    /// <param name="listener">Callback wywoływany dla każdej linii stdout/stderr.</param>
    /// <param name="cancellationToken">Token anulowania; powinien zatrzymać proces i zwrócić anulowanie.</param>
    /// <returns>Wynik wykonania procesu.</returns>
    /// <exception cref="ArgumentNullException">Gdy <paramref name="request"/> jest null.</exception>
    /// <exception cref="OperationCanceledException">Gdy operacja zostanie anulowana.</exception>
    Task<ProcessResult> RunAsync(
        ProcessRequest request,
        Action<ProcessOutputLine>? listener,
        CancellationToken cancellationToken);
}

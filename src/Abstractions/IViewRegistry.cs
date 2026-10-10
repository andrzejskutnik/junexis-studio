namespace Junexis.Studio.Abstractions;

/// <summary>
/// Rejestr mapowania ViewModel → View.
/// Abstrakcja nie narzuca konkretnego frameworka UI – widok jest zwracany jako obiekt.
/// </summary>
public interface IViewRegistry
{
    /// <summary>
    /// Rejestruje mapowanie typu view modelu na typ widoku.
    /// </summary>
    /// <typeparam name="TViewModel">Typ view modelu.</typeparam>
    /// <typeparam name="TView">Typ widoku.</typeparam>
    void Register<TViewModel, TView>()
        where TViewModel : class
        where TView : class, new();

    /// <summary>
    /// Próbuje rozwiązać widok dla podanego view modelu.
    /// </summary>
    /// <param name="viewModel">Instancja view modelu.</param>
    /// <returns>
    /// Instancja widoku lub <c>null</c>, jeśli nie ma zarejestrowanego mapowania.
    /// </returns>
    object? Resolve(object viewModel);
}

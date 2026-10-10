namespace Junexis.Studio.Abstractions;

/// <summary>
/// Publiczny rejestr providerów kompilacji dostępny dla rozszerzeń Studio.
/// </summary>
public interface ICompilerRegistry
{
    /// <summary>Rejestruje provider kompilacji.</summary>
    /// <param name="compiler">Provider do zarejestrowania.</param>
    void Register(ICompilerService compiler);

    /// <summary>Zwraca aktualnie zarejestrowane providery.</summary>
    /// <returns>Lista providerów kompilacji.</returns>
    IReadOnlyList<ICompilerService> GetRegistered();
}

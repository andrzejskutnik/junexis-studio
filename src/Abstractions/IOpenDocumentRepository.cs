namespace Junexis.Studio.Abstractions;

/// <summary>
/// Repozytorium aktualnie otwartych dokumentów (v0.1).
/// </summary>
public interface IOpenDocumentRepository
{
    /// <summary>
    /// Zwraca snapshot dokumentów aktualnie otwartych w edytorze.
    /// </summary>
    /// <returns>Lista otwartych dokumentów.</returns>
    IReadOnlyList<Document> GetOpenDocuments();

    /// <summary>
    /// Próbuje pobrać dokument po ścieżce pliku.
    /// </summary>
    /// <param name="path">Pełna ścieżka do pliku.</param>
    /// <param name="document">Znaleziony dokument.</param>
    /// <returns><see langword="true"/> jeśli znaleziono; w przeciwnym razie <see langword="false"/>.</returns>
    bool TryGetByPath(string path, out Document document);
}

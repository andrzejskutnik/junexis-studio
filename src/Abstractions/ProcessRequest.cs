using System;

namespace Junexis.Studio.Abstractions;

/// <summary>
/// Parametry uruchomienia procesu zewnętrznego.
/// </summary>
public sealed record ProcessRequest
{
    /// <summary>
    /// Tworzy żądanie uruchomienia procesu.
    /// </summary>
    /// <param name="fileName">Nazwa pliku wykonywalnego (np. <c>git</c>, <c>g++</c>).</param>
    /// <param name="arguments">Argumenty w formie pojedynczego stringa.</param>
    /// <param name="workingDirectory">Opcjonalny katalog roboczy.</param>
    public ProcessRequest(string fileName, string? arguments = null, string? workingDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("FileName cannot be empty.", nameof(fileName));
        }

        FileName = fileName;
        Arguments = arguments ?? string.Empty;
        WorkingDirectory = workingDirectory;
    }

    /// <summary>Tworzy żądanie z argumentami przekazywanymi bezpośrednio do listy argumentów procesu.</summary>
    /// <param name="fileName">Nazwa pliku wykonywalnego.</param>
    /// <param name="argumentList">Argumenty procesu bez ręcznego cytowania.</param>
    /// <param name="workingDirectory">Opcjonalny katalog roboczy.</param>
    public ProcessRequest(
        string fileName,
        IReadOnlyList<string> argumentList,
        string? workingDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("FileName cannot be empty.", nameof(fileName));
        ArgumentNullException.ThrowIfNull(argumentList);

        FileName = fileName;
        ArgumentList = argumentList.ToArray();
        Arguments = string.Join(' ', ArgumentList.Select(FormatForDisplay));
        WorkingDirectory = workingDirectory;
    }

    /// <summary>
    /// Nazwa pliku wykonywalnego.
    /// </summary>
    public string FileName { get; }

    /// <summary>
    /// Argumenty procesu.
    /// </summary>
    public string Arguments { get; }

    /// <summary>
    /// Argumenty przekazywane strukturalnie do procesu; <see langword="null"/> dla starszego tekstowego żądania.
    /// </summary>
    public IReadOnlyList<string>? ArgumentList { get; }

    /// <summary>
    /// Katalog roboczy; jeśli null/empty, używany jest domyślny katalog procesu.
    /// </summary>
    public string? WorkingDirectory { get; }

    /// <summary>
    /// Czy proces ma być uruchomiony bez okna konsoli.
    /// </summary>
    public bool CreateNoWindow { get; init; } = true;

    /// <summary>Gets environment variables applied only to the child process.</summary>
    public IReadOnlyDictionary<string, string?> EnvironmentVariables { get; init; } =
        new Dictionary<string, string?>();

    private static string FormatForDisplay(string value) =>
        value.Any(char.IsWhiteSpace) || value.Contains('"')
            ? $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\""
            : value;
}

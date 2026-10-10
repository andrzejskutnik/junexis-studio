using System.Text.RegularExpressions;

namespace JunexisLang.Studio.CompilerExtension;

/// <summary>Usuwa dane prywatne z wyjścia publicznego polecenia restore.</summary>
internal static partial class RestoreOutputSanitizer
{
    internal static string Sanitize(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var result = BearerToken().Replace(value, "Bearer [USUNIĘTO_TOKEN]");
        result = UrlQuery().Replace(result, "$1?[USUNIĘTO_PARAMETRY]");
        result = NamedCredential().Replace(result, "$1=[USUNIĘTO_DANE_UWIERZYTELNIAJĄCE]");
        result = WindowsUserPath().Replace(result, "[USUNIĘTO_ŚCIEŻKĘ_UŻYTKOWNIKA]");
        return UnixUserPath().Replace(result, "[USUNIĘTO_ŚCIEŻKĘ_UŻYTKOWNIKA]");
    }

    [GeneratedRegex(@"(?i)\bBearer\s+[^\s\""']+", RegexOptions.CultureInvariant)]
    private static partial Regex BearerToken();

    [GeneratedRegex(@"(?i)(https?://[^\s?#\""']+)\?[^\s#\""']*", RegexOptions.CultureInvariant)]
    private static partial Regex UrlQuery();

    [GeneratedRegex(@"(?i)\b(api[_-]?key|access[_-]?token|token|secret|password|passwd|authorization)\s*[:=]\s*[^\s\""';]+", RegexOptions.CultureInvariant)]
    private static partial Regex NamedCredential();

    [GeneratedRegex(@"(?i)\b[A-Z]:\\Users\\[^\\\r\n\""']+(?:\\[^\r\n\""']*)?", RegexOptions.CultureInvariant)]
    private static partial Regex WindowsUserPath();

    [GeneratedRegex(@"(?i)/home/[^/\r\n\""']+(?:/[^\r\n\""']*)?", RegexOptions.CultureInvariant)]
    private static partial Regex UnixUserPath();
}

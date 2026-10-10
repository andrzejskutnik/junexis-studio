namespace JunexisLang.Studio.CompilerExtension;

/// <summary>
/// Rozpoznaje stabilne klasy błędów publicznego <c>junexis restore</c> bez
/// utrwalania pełnej treści procesu, która może zawierać dane środowiska.
/// </summary>
internal sealed class RestoreFailureClassifier
{
    private int _kind;

    internal void Observe(string? line)
    {
        if (string.IsNullOrWhiteSpace(line) || Volatile.Read(ref _kind) != 0)
            return;

        var kind = Classify(line);
        if (kind != RestoreFailureKind.Unknown)
            Interlocked.CompareExchange(ref _kind, (int)kind, 0);
    }

    internal RestoreFailureAdvice? GetAdvice() =>
        Advice((RestoreFailureKind)Volatile.Read(ref _kind));

    private static RestoreFailureKind Classify(string line)
    {
        if (Contains(line, "w profilu offline") || Contains(line, "offline miss"))
            return RestoreFailureKind.OfflineMissing;
        if (Contains(line, "registry-timeout") || Contains(line, "przekroczyła limit czasu"))
            return RestoreFailureKind.Timeout;
        if (Contains(line, "rate-limited") || Contains(line, "HTTP 429"))
            return RestoreFailureKind.RateLimit;
        if (Contains(line, "payload-too-large") || Contains(line, "HTTP 413") ||
            Contains(line, "przekracza limit rozmiaru") || Contains(line, "przekracza limit artefaktów"))
            return RestoreFailureKind.RequestLimit;
        if (Contains(line, "artifact-conflict") || Contains(line, "HTTP 409"))
            return RestoreFailureKind.Conflict;
        if (Contains(line, "nie obsługuje SDK") || Contains(line, "provider-sdk-mismatch"))
            return RestoreFailureKind.ProviderSdkMismatch;
        return RestoreFailureKind.Unknown;
    }

    private static bool Contains(string text, string value) =>
        text.Contains(value, StringComparison.OrdinalIgnoreCase);

    private static RestoreFailureAdvice? Advice(RestoreFailureKind kind) => kind switch
    {
        RestoreFailureKind.OfflineMissing => new(
            "JXR001",
            "W lokalnej pamięci podręcznej brakuje wymaganej zależności. Połącz się z siecią, uruchom „Odtwórz zależności Junexis”, a po powodzeniu ponów tryb offline."),
        RestoreFailureKind.Timeout => new(
            "JXR002",
            "Registry nie odpowiedziało w limicie czasu. Sprawdź połączenie i ponów „Odtwórz zależności Junexis”."),
        RestoreFailureKind.RateLimit => new(
            "JXR003",
            "Registry ograniczyło liczbę żądań. Odczekaj czas wskazany przez usługę i ponów „Odtwórz zależności Junexis”."),
        RestoreFailureKind.RequestLimit => new(
            "JXR004",
            "Registry odrzuciło artefakt lub katalog z powodu limitu rozmiaru. Sprawdź manifest zależności albo zgłoś problem administratorowi Registry; samo ponowienie nie usunie przyczyny."),
        RestoreFailureKind.Conflict => new(
            "JXR005",
            "Registry zgłosiło konflikt niezmiennego artefaktu. Wybierz inną wersję zależności albo zgłoś konflikt administratorowi Registry."),
        RestoreFailureKind.ProviderSdkMismatch => new(
            "JXR006",
            "Provider nie obsługuje wersji SDK wymaganej przez projekt. Dopasuj providerVersion i providerSdkVersion w junexis.project.toml albo opublikuj zgodną wersję providera."),
        _ => null
    };

    private enum RestoreFailureKind
    {
        Unknown,
        OfflineMissing,
        Timeout,
        RateLimit,
        RequestLimit,
        Conflict,
        ProviderSdkMismatch
    }
}

internal sealed record RestoreFailureAdvice(string Code, string Message);

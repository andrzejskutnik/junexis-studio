# M20.11.2 — cykl platformy w Studio

Panel **Cykl platformy** jest warstwą prezentacji nad publicznym poleceniem
`junexis`. Studio nie odczytuje ani nie zapisuje wewnętrznych plików profilu
instalacji i nie ma własnego wykonawcy aktualizacji.

## Dostępne działania

| Działanie w Studio | Wywołanie publicznego CLI | Charakter |
|---|---|---|
| Pokaż plan | `junexis update plan ... --json` | tylko odczyt |
| Odśwież postęp | `junexis update history --json` | tylko odczyt |
| Sprawdź naprawę | `junexis repair check --json` | tylko odczyt |
| Napraw | `junexis repair apply ...` | jawna zmiana po sprawdzeniu |
| Pokaż plan powrotu | `junexis update rollback ... --json` | tylko odczyt |
| Wykonaj powrót | `junexis update rollback ... --apply --json` | jawna zmiana po planie |

Pełne wykonanie aktualizacji nie jest przedstawiane jako dostępne, ponieważ
publiczne `junexis update apply` nie zostało jeszcze udostępnione. Panel nie
zastępuje tej brakującej operacji własną implementacją.

## Bezpieczny rytm

1. Użytkownik wskazuje materiały i opcjonalny profil.
2. Studio uruchamia plan przez `IProcessService` i strumieniuje wynik.
3. Naprawa lub powrót stają się dostępne dopiero po wyniku zero.
4. Zmiana profilu, źródła, katalogu albo magazynu zaufania unieważnia zgodę.
5. Anulowanie przekazuje token do `IProcessService`, który kończy całe drzewo
   procesu publicznego CLI.
6. Historię można odświeżyć po sukcesie, błędzie lub anulowaniu.

Argumenty są przekazywane jako strukturalna lista, bez ręcznego składania
wiersza polecenia. Wyjście widoczne w panelu i wspólnym kanale Studio przechodzi
redakcję nazwanych sekretów i pełnych ścieżek katalogów domowych.

## Próby

```powershell
dotnet test tests/Junexis.Studio.Host.Tests/Junexis.Studio.Host.Tests.csproj `
  --filter "Milestone=M20.11.2"
```

Sześć prób sprawdza dokładne argumenty, ochronę plików użytkownika, historię,
warunek planu przed mutacją, unieważnianie zgody, anulowanie i redakcję.

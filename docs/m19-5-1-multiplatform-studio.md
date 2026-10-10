# M19.5.1 — samodzielne Studio na trzech systemach

**Status:** `IMPLEMENTED`; natywnie zweryfikowano `win-x64` i `linux-x64`
(2026-09-11). Do pełnego `VERIFIED` pozostaje `osx-arm64`.

M19.5.1 tworzy trzy paczki Junexis Studio, które nie wymagają osobnej instalacji
.NET: `win-x64`, `linux-x64` i `osx-arm64`. Wariant macOS ma strukturę aplikacji
`Junexis Studio.app`, a nie luźny zestaw plików.

## Jak Studio odnajduje narzędzia

Studio nie szuka już kompilatora w sąsiednich repozytoriach. Stosuje jeden,
przewidywalny porządek:

1. jawna zmienna danego narzędzia, na przykład `JUNEXIS_CLI`;
2. katalog wskazany przez `JUNEXIS_PROFILE_BIN`;
3. standardowy katalog profilu: `%LOCALAPPDATA%\Junexis\bin` w Windows albo
   `$XDG_BIN_HOME`/`$HOME/.local/bin` w Linux i macOS;
4. publiczne polecenie z `PATH`.

Dzięki temu Studio współpracuje z instalatorem i publicznym poleceniem `junexis`,
ale pozostaje niezależne od układu repozytoriów deweloperskich.

## Budowanie i kontrola

```powershell
./scripts/verify-m19-5-1.ps1 -BuildAll
```

Skrypt buduje wszystkie warianty i sprawdza ich rachunki oraz sumy SHA-256.
Tylko wariant zgodny z systemem wykonawcy otrzymuje próbę natywną. Pozostałe
warianty są poprawnymi wynikami kompilacji krzyżowej, lecz nie stanowią dowodu
uruchomienia na docelowym systemie.

Udana próba natywna automatycznie zapisuje rachunek w
`evidence/m19-5-1/<rid>.json`. Próba Linux x64 potwierdza samodzielny program,
manifest dystrybucji oraz odnalezienie `junexis` w katalogu wskazanym przez
`JUNEXIS_PROFILE_BIN`.

Serwer użyty do próby Linuksa nie ma środowiska graficznego. Dlatego dowód
obejmuje uruchomienie technicznych punktów kontrolnych przed inicjalizacją
Avalonia, ale nie zastępuje ręcznej kontroli okna, klawiatury i renderowania na
stacji roboczej z pulpitem Linux.

## Kryterium zamknięcia

M19.5.1 jest zaimplementowane po utworzeniu trzech paczek i przejściu testów
Studio. Status `VERIFIED` dla całej macierzy wymaga dodatkowo uruchomienia skryptu
na Windows x64, Linux x64 i macOS ARM64 oraz zachowania trzech natywnych dowodów.

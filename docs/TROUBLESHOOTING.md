# Junexis Studio — rozwiązywanie problemów

## Zacznij od właściwej warstwy

Zamiast zmieniać kilka ustawień naraz, ustal najpierw miejsce awarii:

| Objaw | Najpierw sprawdź | Potem |
|---|---|---|
| brak plików lub zły profil | workspace i aktywny dokument | status bar, Explorer |
| Build/Run nie startuje | Toolchains | kanał Output właściwego języka |
| błąd z linią źródła | Problems | kliknij diagnostykę |
| proces działa, Debug nie | adapter DAP i debugger | kanał Debug |
| Changes nie działa | `.git` w otwartym folderze | kanał Git |
| Why? nie ma odpowiedzi | manifest i TraceCollector | kanał Why |

W aplikacji wybierz **Help → Troubleshooting**. Panel zawiera tę samą kolejność
kontroli oraz skróty do Toolchains, Problems i Output.

## Operacja pozostaje w Starting albo Running

Pasek Run & Debug pokazuje autorytatywny lifecycle bieżącej operacji. Jeśli stan
nie kończy się jako `Succeeded`, `Failed` albo `Cancelled`:

1. użyj `Shift+F5`,
2. sprawdź kanał procesu w Output,
3. potwierdź, czy narzędzie potomne odpowiada,
4. dla Debug sprawdź również kanał Debug i stan adaptera DAP.

Stop jest aktywny tylko podczas rzeczywistej operacji. Nieaktywny Stop oznacza,
że Workbench nie ma procesu lub sesji do zatrzymania.

## Studio nie widzi kompilatora Junexis

Najpierw zbuduj CLI:

```powershell
dotnet build .\junexis-lang\compiler\JunexisLang.Compiler.CLI\JunexisLang.Compiler.CLI.csproj
```

Uruchamiaj Studio z katalogu nadrzędnego zawierającego `junexis-lang` i
`junexis-studio`. Alternatywnie ustaw:

```powershell
$env:JUNEXIS_COMPILER_CLI = "C:\pełna\ścieżka\JunexisLang.Compiler.CLI.dll"
```

Następnie uruchom ponownie Studio i odśwież panel **Toolchains**.

## Studio nie może odtworzyć zależności projektu

Komendy **Build → Odtwórz zależności Junexis** używają publicznego Installera,
nie prywatnej ścieżki compilera. Ustaw jego executable oraz, dla projektu z
providerami, lokalny zestaw wydania:

```powershell
$env:JUNEXIS_CLI = "C:\Junexis\Junexis.Installer.exe"
$env:JUNEXIS_LOCAL_PACKAGES = "C:\Junexis\wydanie\win-x64"
```

Szczegóły błędu są zapisywane w kanale **Output → Restore**.
Rozpoznane problemy pojawiają się także w panelu **Problems** jako diagnostyki
`JXR001`–`JXR006`. Ich opis podaje następny bezpieczny krok. Nie uruchamiaj
prywatnego `junexis-compiler restore`; ponowienie wykonuj poleceniem
**Build → Odtwórz zależności Junexis**.

| Kod | Znaczenie | Działanie |
|---|---|---|
| `JXR001` | brak zależności offline | połącz się z siecią, wykonaj zwykłe odtwarzanie, potem ponów offline |
| `JXR002` | przekroczony czas Registry | sprawdź połączenie i ponów zwykłe odtwarzanie |
| `JXR003` | ograniczenie liczby żądań | odczekaj czas wskazany przez usługę i ponów |
| `JXR004` | przekroczony limit rozmiaru | sprawdź manifest albo zgłoś problem administratorowi Registry |
| `JXR005` | konflikt niezmiennego artefaktu | wybierz inną wersję albo zgłoś konflikt administratorowi Registry |
| `JXR006` | provider niezgodny z SDK | dopasuj `providerVersion` i `providerSdkVersion` |

Jeśli komunikat mówi, że inny proces modyfikuje profil providerów, poczekaj na
zakończenie działającego `junexis restore` albo polecenia `junexis provider` i
ponów operację. Nie usuwaj ręcznie pliku `profile.transaction.lock`; blokada
jest zwalniana automatycznie po powodzeniu, błędzie i wycofaniu transakcji.

Anulowanie restore zatrzymuje również uruchomione drzewo procesu compilera i
wycofuje providerów zainstalowanych przez niedokończoną operację. Po anulowaniu
można bezpiecznie uruchomić polecenie ponownie.

Pełny kontrakt komunikatów opisuje
[`m17-11-2-restore-diagnostics.md`](m17-11-2-restore-diagnostics.md).
Testowane zachowanie anulowania i ponowienia opisuje
[`m17-11-3-cancel-retry.md`](m17-11-3-cancel-retry.md).

## Mentor jest niedostępny

```powershell
dotnet build .\junexis-mentor\JunexisLang.Mentor.CLI\JunexisLang.Mentor.CLI.csproj
$env:JUNEXIS_MENTOR_CLI = "C:\pełna\ścieżka\JunexisLang.Mentor.CLI.dll"
```

Mentor uruchamia się podczas preflightu, przed generowaniem targetów, na pliku
`junexis.analysis.preflight.json`. Jeśli CLI jest niedostępne, kanał
**Output → Mentor** pokaże `unavailable; build continues`; compiler nadal
przejdzie do codegenu. Po udanym buildzie panel Analysis zachowuje wynik o tym
samym `buildId` bez powtórnej analizy.

Jeśli wybrana diagnostyka nie pokazuje sekcji Mentora, nie musi to oznaczać
błędu. Studio celowo wymaga jednocześnie kodu diagnostyki, zgodnego build ID,
dokładnej linii deklaracji stable symbol ID oraz findingu dotyczącego tego
symbolu. Przy błędzie składni, który uniemożliwił utworzenie bieżącego
snapshotu, albo przy wyniku `Stale` Mentor milczy.

## Brak completion, hover albo F12

Otwórz panel **Toolchains** i sprawdź właściwy serwer językowy:

- Junexis — użyj `junexis install --lsp`, zbuduj `JunexisLang.Compiler.LspHost`
  albo ustaw `JUNEXIS_LSP`,
- C/C++ — zainstaluj `clangd` albo ustaw `JUNEXIS_CLANGD`,
- Go — zainstaluj `gopls` albo ustaw `JUNEXIS_GOPLS`,
- TypeScript/JavaScript — zainstaluj `typescript-language-server` i `typescript`
  albo ustaw `JUNEXIS_TYPESCRIPT_LSP`.

Po zmianie `PATH` lub zmiennej środowiskowej uruchom Studio ponownie. Dla
aktywnego dokumentu możesz użyć **Tools → Restart Language Server**. Szczegóły
inicjalizacji, restartów i stderr procesu są zapisywane w kanale
**Language Services**.

## Problems ma wpis, ale edytor nie pokazuje podkreślenia

Podkreślenie wymaga diagnostyki z pełną ścieżką, linią i kolumną. Diagnostyki
bez lokacji pozostają widoczne wyłącznie w panelu **Problems**. Jeśli plik
został przeniesiony poza Studio, ponów build albo zapisz dokument, aby producent
diagnostyki opublikował aktualną ścieżkę. `F8` pomija wpisy bez lokacji.

## F6 wybiera niewłaściwy compiler

Wybór profilu zależy od aktywnego dokumentu. Otwórz plik `.jnx`, `.cpp`, `.go`,
`.cs` albo `.ts`, a następnie ponów `F6`.

Dla `.jnx` Studio preferuje katalog z `junexis.project.toml`. Jeśli manifestu
nie ma, używa aktywnego pliku lub pierwszego `.jnx` w katalogu głównym.

## Build przeszedł, ale nie ma natywnego programu

Zwykły build Junexis generuje artefakty. Natywną poprawność targetu sprawdza
`artifact-gate`, który wymaga odpowiedniego toolchainu:

```powershell
junexis artifact-gate . --target server
junexis artifact-gate . --target report
junexis artifact-gate . --target embedded
```

Użyj panelu **Toolchains**, aby sprawdzić Go, Node/npm/TypeScript albo
G++/PlatformIO.

## Debugger nie startuje

Sprawdź osobno:

1. program targetu,
2. debugger natywny, np. GDB albo Delve,
3. adapter DAP,
4. konfigurację launch/attach.

Obecność GDB nie oznacza obecności adaptera C++ DAP. Szczegóły wykrytej komendy
i źródła adaptera pokazuje panel **Toolchains**.

## Breakpoint `.jnx` jest niepotwierdzony

Sprawdź, czy ostatni build utworzył:

```text
out/debug/junexis.source-map.json
out/debug/junexis.debug.toml
```

Następnie sprawdź sekcję **Adapter verification** w panelu Debug. Adapter może
przesunąć breakpoint do najbliższej wykonywalnej linii albo odrzucić go.

## „Why?” nie znalazło zdarzenia

To oczekiwany wynik, gdy brakuje dowodu. Sprawdź:

- czy TraceCollector działa,
- czy panel ma właściwy URL,
- czy serwer używa debug manifestu z bieżącego builda,
- czy runtime wysłał zdarzenia,
- czy ramka została zmapowana do stable symbol ID lub dokładnego źródła.

Narrative nie dopasowuje zdarzeń po podobnej nazwie funkcji.

## Zakładka Concurrent jest pusta

Pusta lista ma dwa możliwe znaczenia:

- żadne zdarzenie nie jest współbieżne z celem,
- eventy nie mają vector clocks i współbieżności nie można dowieść.

Rozróżnienie znajduje się w zakładce **Evidence**.

## Port 8080 jest zajęty

Uruchom TraceCollector na innym porcie:

```powershell
dotnet run --project .\junexis-narrative\Junexis.Debugger.TraceCollector.Server -- `
  --port 8181 --manifest C:\project\out\debug\junexis.debug.toml
```

W panelu **Why?** ustaw `http://localhost:8181`.

## Studio otworzyło niewłaściwy folder

Studio przywraca najnowszy istniejący folder zapisany w historii użytkownika.
Użyj **Workspace → Open Folder...** i wskaż właściwy katalog projektu.
Historia znajduje się w `workspace-state.json` w katalogu danych użytkownika
`JunexisStudio`. Nie jest częścią repozytorium.

## Changes pokazuje „.git not found”

Studio aktywuje kontekst Git tylko wtedy, gdy otwarty folder zawiera
bezpośrednio katalog albo plik `.git`. Jeśli otwarto podkatalog, na przykład
`repo/src`, użyj **Workspace → Open Folder...** i wybierz katalog `repo`.

Sam folder nadal jest poprawnym workspace dla edytora, builda i Mentora.

## Klonowanie repozytorium nie powiodło się

Sprawdź kanał **Git Clone** w panelu Output. Najczęstsze przyczyny:

- `git` nie jest dostępny w `PATH`,
- URL lub uprawnienia do repozytorium są niepoprawne,
- katalog docelowy o wyprowadzonej nazwie już istnieje,
- połączenie sieciowe albo uwierzytelnianie Git zakończyło się błędem.

Studio nie nadpisuje istniejącego katalogu. Po poprawnym klonowaniu automatycznie
otwiera nowy workspace.

## Gdzie szukać szczegółów

- kanał **Build** — compiler i generowanie,
- kanał **Debug** — adapter oraz DAP,
- kanał **Why** — korelacja i Diagnostic Story,
- panel **Problems** — diagnostyki źródłowe,
- panel **Toolchains** — wykrywanie narzędzi.

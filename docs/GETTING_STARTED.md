# Junexis Studio — pierwsze 10 minut

Ten przewodnik prowadzi od uruchomienia Studio do pierwszego sprawdzonego
Build/Run/Debug. Szczegóły funkcji znajdują się w
[podręczniku użytkownika](USER_GUIDE.md), a problemy w
[Troubleshooting](TROUBLESHOOTING.md).

## 1. Uruchom Studio

Z katalogu nadrzędnego platformy:

```powershell
dotnet run --project .\junexis-studio\src\Shell\Junexis.Studio.Shell.csproj
```

Studio przywróci ostatni istniejący folder. Nie traktuje katalogu, z którego
zostało uruchomione, jako projektu.

## 2. Otwórz właściwy folder

Wybierz jedną drogę:

- **Open Folder...** — otwiera istniejący folder,
- **Clone Git Repository...** — klonuje repozytorium i otwiera je po sukcesie,
- pozycję z sekcji **Recent**.

Każdy folder może być workspace. Funkcje Git włączają się tylko wtedy, gdy
otwarty folder zawiera bezpośrednio katalog albo plik `.git`. Dla projektu
Junexis najlepiej otworzyć katalog zawierający `junexis.project.toml`.

Prawidłowy rezultat:

- pełna ścieżka jest widoczna na pasku stanu,
- Explorer pokazuje zawartość wybranego folderu,
- przy repozytorium pasek stanu pokazuje branch.

## 3. Sprawdź narzędzia

Otwórz **Help → Getting Started**, a następnie użyj przycisku
**Check Toolchains**, albo wybierz **Tools → Check Toolchains**.

Toolchain Doctor osobno sprawdza:

- compiler i runtime,
- serwer językowy,
- debugger natywny,
- adapter DAP.

Brak jednego elementu nie oznacza awarii całego Studio. Na przykład edycja
i Build mogą działać bez adaptera Debug.

## 4. Otwórz źródło

Użyj Explorera albo `Ctrl+P`. Profil na pasku Run & Debug jest dobierany z
aktywnego pliku:

| Plik | Profil |
|---|---|
| `.jnx` | Junexis |
| `.cpp`, `.cc`, `.cxx` | C/C++ |
| `.cs`, `.csproj` | C#/.NET |
| `.go` | Go |
| `.ts`, `.tsx` | TypeScript |

Jeżeli profil jest pusty, najpierw otwórz obsługiwany plik. Profil możesz
również wybrać ręcznie z listy.

## 5. Zbuduj

Naciśnij `F6` albo **Build** na pasku.

Operacja przechodzi przez wspólny lifecycle:

`Starting → Running → Succeeded | Failed | Cancelled`

Po sukcesie sprawdź:

- **Artifacts** — co wygenerowano,
- **Analysis** — symbole, relacje i wyniki Mentora,
- **Output** — szczegóły procesu.

Studio odczytuje te widoki z wersjonowanego
`.junexis/state/latest-build.json`. Po zamknięciu i ponownym otwarciu projektu
ostatni poprawny build zostanie odtworzony bez ponownej kompilacji.

Po błędzie Studio otwiera **Problems**, jeśli compiler podał diagnostyki
źródłowe. Gdy błąd nie ma lokalizacji, szczegóły trafiają do **Output**.

## 6. Uruchom albo debuguj

- `F5` — Run,
- `F7` — Debug,
- `Shift+F5` — Stop,
- `F9` — breakpoint,
- `F10` / `F11` — Step Over / Step Into.

Run i Debug zależą od wybranego profilu. Junexis najpierw generuje artefakty,
a uruchamianie i debugowanie odbywa się dla właściwego języka docelowego.

Po zatrzymaniu debuggera przycisk **Why?** łączy klatkę DAP z dowodami
Narrative. Brak dopasowania jest poprawnym wynikiem, jeśli runtime nie
dostarczył wymaganych zdarzeń. Zakładka **Bookmarks** zapisuje Story w
workspace, eksportuje pakiet z manifestem SHA-256 i importuje zweryfikowany
pakiet offline w trybie tylko do odczytu. Zakładka **Session** pokazuje
pochodzenie i inwentarz dowodów oraz porównuje pakiet z live Story otwartą
bezpośrednio przed importem. **Export signed bundle** używa zewnętrznego klucza
ECDSA P-256 PEM; sekcja autentyczności rozróżnia poprawny podpis od lokalnego
zaufania do dokładnego fingerprintu.

Po imporcie przejdź do **Review**, dodaj jawnie oznaczone Observation,
Hypothesis, Decision lub Action i wybierz **Save revision**. Recenzja jest
osobnym sidecarem związanym z Bundle ID — nie zmienia dowodowego ZIP.
**Export case** przekazuje bundle i wersjonowaną recenzję razem; **Import case**
weryfikuje oba offline.

Jeśli Story zawiera findings Mentora, wybierz **Generate from Story**.
Propozycje pozostają osobną kolejką i pokazują kod reguły, build oraz symbol.
**Accept selected** świadomie dodaje wpis z pochodzeniem Mentor; **Dismiss
selected** niczego nie zapisuje. Mentor nie proponuje ani nie ustawia Decision.

## 7. Zapisz zmianę w Git

Otwórz **Source Control** (`Ctrl+Shift+G`):

1. przejrzyj Changes i diff,
2. wybierz pliki do stage,
3. uruchom commit preflight,
4. wpisz komunikat,
5. wykonaj commit.

Pełny przepływ opisuje
[Workspace i repozytorium](WORKSPACE_REPOSITORY_WORKFLOW.md).

## Gdzie patrzeć, gdy coś nie działa

| Pytanie | Miejsce |
|---|---|
| Czy narzędzie jest wykryte? | Toolchains |
| Co jest błędne w źródle? | Problems |
| Co dokładnie uruchomił proces? | Output |
| Co zostało wygenerowane? | Artifacts |
| Co widzi compiler i Mentor? | Analysis |
| Gdzie zatrzymał się proces? | Debug |
| Dlaczego runtime znalazł się w tym stanie? | Why? |

W każdej chwili użyj **Help → Troubleshooting** albo
`Ctrl+Shift+P` i wpisz `Help`.

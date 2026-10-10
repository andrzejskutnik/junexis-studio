# 05 – Workspace i System Plików

---

# 1. Rola Workspace

Workspace jest logiczną reprezentacją przestrzeni pracy użytkownika.

Nie jest tylko folderem na dysku.
Jest kontekstem, w którym działają rozszerzenia.

Workspace odpowiada za:

* wskazanie root path,
* zarządzanie otwartymi dokumentami,
* publikowanie zdarzeń zmian plików,
* utrzymanie stanu projektu (np. konfiguracji lokalnej).

---

# 2. Model Workspace v0.1

Pierwsza wersja systemu przyjmuje:

* Jeden aktywny workspace
* Jeden root folder
* Brak multi-root

Multi-root może zostać wprowadzony w przyszłości.

Decyzja: prostota > elastyczność w pierwszej fazie.

---

# 3. Model Dokumentu

Dokument to logiczna reprezentacja otwartego pliku.

Powinien zawierać:

* FilePath
* Content
* IsDirty
* LastSavedTimestamp
* DocumentId

Dokument nie jest UI.
Dokument jest modelem.

UI (Editor Panel) jest tylko reprezentacją dokumentu.

---

# 4. Cykl Życia Dokumentu

1. FileOpenRequested
2. Workspace ładuje plik
3. Tworzy Document
4. Publikuje DocumentOpened
5. Editor reaguje
6. Użytkownik edytuje → DocumentChanged
7. SaveRequested
8. Workspace zapisuje plik
9. DocumentSaved

Cykl używa dwóch poziomów powiadomień:

* Workspace emituje lokalne eventy (np. `FolderOpened`, `FileOpened`, `FileSaved`),
* po zmianie root publikuje globalny `WorkspaceOpenedEvent`,
* extensions (Explorer/Editor) używają EventBus do komunikacji między sobą
  (np. `FileOpenRequested`).

`WorkspaceOpenedEvent` jest sygnałem zmiany kontekstu całego IDE. Git,
auto-build, Mentor, debugger i inne moduły mogą reagować niezależnie bez
bezpośredniej zależności z Workspace.

---

# 5. Abstrakcja Systemu Plików

Wprowadzamy IFileSystem:

Powody:

* testowalność,
* możliwość pracy z wirtualnym FS w przyszłości,
* możliwość pracy z repozytorium zdalnym.

IFileSystem zapewnia:

* ReadFile
* WriteFile
* Exists
* EnumerateDirectory
* Watch

---

# 6. File Watcher

Workspace powinien reagować na:

* zmiany pliku na dysku,
* usunięcie pliku,
* zmianę nazwy.

Zdarzenia:

* FileChangedExternally
* FileDeletedExternally
* FileRenamedExternally

Editor może wówczas:

* wyświetlić ostrzeżenie,
* przeładować dokument,
* pozwolić użytkownikowi zdecydować.

---

# 7. Explorer

Explorer jest rozszerzeniem.

Nie zarządza Workspace.

Explorer:

* wyświetla strukturę folderu,
* reaguje na zmiany workspace,
* publikuje zdarzenia (FileOpenRequested, FileDeleteRequested itd.).

Bez aktywnego root Explorera pokazuje ekran powitalny, a nie katalog procesu.
Po `FolderOpened` przebudowuje drzewo i przełącza widok z pustego stanu.

---

# 8. Relacja Workspace ↔ Extensions

Workspace publikuje zdarzenia.

Extensions reagują.

Przykłady:

* Kompilator reaguje na DocumentSaved
* Outline reaguje na DocumentChanged
* OMWiA może reagować na FileOpened

Workspace nie zna tych rozszerzeń.

---

# 9. Konfiguracja Workspace

Workspace może posiadać plik konfiguracyjny, np.:

* junexis.workspace.json

Może zawierać:

* konfigurację kompilatora,
* aktywne profile,
* ustawienia projektu,
* ścieżki narzędzi.

Parsowanie tego pliku należy do odpowiedniego rozszerzenia.

Workspace tylko udostępnia jego zawartość.

---

# 10. Minimalny zakres v0.1

Pierwsza wersja powinna zawierać:

* otwarcie folderu
* wyświetlenie struktury
* otwieranie plików
* edycję
* zapis
* reakcję na zmiany zewnętrzne

Bez:

* projektów wielomodułowych
* dependency graph
* build pipeline w rdzeniu

---

# 11. Start i historia workspace

Start aplikacji ma tylko dwa poprawne wyniki:

1. przywrócenie najnowszego istniejącego folderu z historii,
2. pusty workspace z jawnymi akcjami użytkownika.

Katalog procesu nie jest fallbackiem. Historia jest stanem użytkownika,
nie konfiguracją projektu. `WorkspaceHistoryStore` przechowuje maksymalnie 10
unikalnych, istniejących ścieżek w `workspace-state.json` w katalogu danych
użytkownika.

---

# 12. Bezpieczna zmiana root

Przed ustawieniem nowego root Shell przechodzi po otwartych dokumentach.
Niezapisany dokument wymaga decyzji:

* Save — zapisz i zamknij,
* Discard — zamknij bez zapisu,
* Cancel — przerwij całą zmianę workspace.

Dopiero po zamknięciu wszystkich kart Shell wywołuje `IWorkspace.OpenFolder`.
Workspace normalizuje pełną ścieżkę, sprawdza istnienie folderu, restartuje
watcher oraz publikuje lokalny `FolderOpened` i globalny
`WorkspaceOpenedEvent`.

---

Kolejny dokument:

06_System_Komend_i_Terminal.md

W nim zostanie opisany system komend, integracja z terminalem oraz relacja z kompilatorami.

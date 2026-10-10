# 10 – System Kontroli Wersji jako Rozszerzenie

---

# 1. Cel

System kontroli wersji (VCS) w JunexisStudio jest rozszerzeniem.

Rdzeń IDE:

* nie zna Git,
* nie zna innych systemów VCS,
* nie wymaga obecności repozytorium.

Brak rozszerzenia VCS nie blokuje pracy użytkownika.

---

# 2. Capability – IVcsService

Rozszerzenie VCS publikuje capability:

* `IVcsService`

Aktywny kontrakt jest asynchroniczny i udostępnia:

* niezmienny `VcsRepositorySnapshot`,
* `RefreshAsync()`,
* `StageAsync(path)`,
* `UnstageAsync(path)`,
* `CommitAsync(message)`,
* `GetDiffAsync(path, target)`.

Snapshot rozdziela status index i working tree dla każdej ścieżki. Panel
Changes, Explorer i dekoracje czytają ten sam snapshot; nie prowadzą własnej
symulacji indeksu. Metadane snapshotu obejmują branch, detached HEAD, upstream,
ahead/behind i trwające operacje repozytorium.

Commit jest operacją dwuetapową. `PrepareCommitAsync` odczytuje dokładny
staged scope, autora i cached diff stat bez mutacji. Dopiero
`CommitAsync(VcsCommitRequest)` może uruchomić Git. Duży zakres wymaga jawnego
potwierdzenia, a timeout lub anulowanie kończy się ponownym odczytem HEAD.
Hooki i podpisywanie pozostają natywną odpowiedzialnością Git; Studio pokazuje
ich błąd i nie usuwa wpisanej wiadomości.

Panel Repository rozszerza lokalny workflow o historię, szczegóły commitów,
branch i stash. Operacje, które zmieniają worktree, wymagają braku konfliktów
i — dla switch, stash apply/pop oraz pull — czystego drzewa. Pull zawsze używa
`--ff-only` i musi wskazywać branch zgodny z aktualnym branchem lokalnym.

Fetch, pull i push nigdy nie uruchamiają się przy odświeżeniu panelu. Użytkownik
wybiera remote i wpisuje lub wybiera branch, po czym uruchamia konkretną akcję.
Wyświetlane URL-e remote mają usunięte dane użytkownika i hasła.

Każda mutacja tworzy ponadto ograniczony dowód VCS w `IAuditService` oraz
publikuje go przez EventBus. Dowód zawiera intencję, anonimowy identyfikator
repozytorium, przejście branch/HEAD, względne ścieżki, czas i kod rezultatu.
Nie zawiera diffu, wiadomości commita, pełnej ścieżki repozytorium, URL remote
ani surowego stdout/stderr. Commit może wskazać build, liczbę findingów Mentora
i Diagnostic Story wyłącznie po zgodnym build ID.

Implementacja może być oparta o:

* wywołania procesów (git CLI),
* bibliotekę natywną,
* zewnętrzny serwis.

---

# 3. Status Plików

Status pliku powinien być reprezentowany jako:

* Untracked,
* Modified,
* Added,
* Deleted,
* Renamed,
* Copied,
* Conflict.

Git jest odczytywany przez NUL-delimited `porcelain=v2`, dlatego nazwa pliku
nie jest parsowana na podstawie cudzysłowów, strzałek ani końca linii.

Explorer może opcjonalnie:

* wyświetlać ikony statusu,
* udostępniać komendy kontekstowe.

Jeśli VCS nie istnieje – Explorer działa bez tej warstwy.

---

# 4. Integracja z Explorer

Explorer:

* sprawdza dostępność `IVcsService`,
* opcjonalnie wyświetla status pliku,
* dodaje komendy kontekstowe (Commit, Diff itd.).

Explorer nie zna implementacji VCS.

---

# 5. Integracja z Workspace

Workspace może publikować zdarzenia:

* `FolderOpened`,
* `FileSaved`.

Rozszerzenie VCS może reagować na te zdarzenia:

* odświeżać status repozytorium,
* publikować zdarzenie RepositoryStatusChanged.

Workspace nie zna VCS.

## 5.1. Granica kontekstu repozytorium

Aktywny root workspace jest kontekstem Git tylko wtedy, gdy bezpośrednio
zawiera znacznik `.git`:

* katalog `.git` dla standardowego checkoutu,
* plik `.git` dla worktree lub submodule.

Samo `git rev-parse --is-inside-work-tree` nie wystarcza, ponieważ uznałoby
dowolny podkatalog za całe repozytorium. Studio najpierw sprawdza lokalny
znacznik, a dopiero potem dostępność Git i poprawność repozytorium.

Brak znacznika publikuje snapshot `IsRepository=false`. Dostawca operacji nie
jest tworzony, więc stage, commit, diff i pozostałe mutacje nie mogą wykonać
się przypadkowo w repozytorium nadrzędnym.

## 5.2. Clone Repository

Klonowanie jest orkiestracją Shell nad `IProcessService`:

1. użytkownik podaje URL,
2. wybiera katalog nadrzędny,
3. Shell wyprowadza nazwę celu i odrzuca istniejącą ścieżkę,
4. Git otrzymuje strukturalną listę argumentów `clone --progress -- URL TARGET`,
5. wynik trafia do kanału `Git Clone`,
6. po sukcesie standardowy workflow workspace otwiera katalog docelowy.

Shell nie używa interpretera poleceń. URL jest maskowany w liniach outputu.
Po klonowaniu lokalny detektor `.git` potwierdza oczekiwany kontekst.

---

# 6. Wiele Implementacji VCS

System musi umożliwiać:

* istnienie wielu implementacji `IVcsService`,
* wybór aktywnego VCS per workspace,
* fallback do braku VCS.

Przykłady:

* GitExtension
* SnapshotExtension
* RemoteVcsExtension

---

# 7. Izolacja Błędów

Błąd w operacji VCS:

* logowany do kanału Extensions lub VCS,
* nie blokuje IDE,
* nie blokuje Explorer,
* nie wpływa na kompilator.

---

# 8. Minimalny zakres v0.1

Pierwsza wersja powinna:

* obsługiwać Git przez CLI,
* wykrywać repozytorium w workspace,
* wyświetlać status plików,
* umożliwiać commit.

Bez:

* zarządzania branchami,
* rebase,
* zaawansowanego merge UI,
* integracji z serwerami zdalnymi.

Ten historyczny minimalny zakres został przekroczony: bieżąca implementacja ma
historię, branche, stash, jawne operacje remote, evidence oraz bezpieczny
workflow klonowania. Sekcja pozostaje zapisem pierwotnej granicy v0.1.

---

Kolejny dokument:

11_OMWiA_jako_Modul_Systemowy.md

W nim zostanie opisany model integracji OMWiA jako rozszerzenia kontekstowego oraz jego relacja z EventBus, MCP i Workspace.

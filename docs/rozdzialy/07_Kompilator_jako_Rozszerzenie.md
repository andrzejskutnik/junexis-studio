# 07 – Kompilator jako Rozszerzenie

---

# 1. Cel

Kompilator w JunexisStudio nie jest częścią rdzenia.

Jest rozszerzeniem, które:

* rejestruje komendy build / run,
* publikuje diagnostykę,
* może korzystać z terminala lub IProcessService,
* reaguje na zdarzenia workspace.

System musi umożliwiać współistnienie wielu kompilatorów.

---

# 2. Model ICompilerService

Rozszerzenie kompilatora publikuje capability:

* ICompilerService

ICompilerService powinien umożliwiać:

* Build(BuildContext)
* Clean()
* GetDiagnostics()
* SupportsFile(string extension)

Brak ICompilerService nie blokuje IDE.

---

# 3. BuildContext

BuildContext powinien zawierać:

* WorkspaceRoot
* ActiveDocument
* BuildProfile
* EnvironmentVariables

Nie powinien zawierać elementów UI.

---

# 4. Rejestracja Komend

Rozszerzenie kompilatora rejestruje komendy:

* Build
* Rebuild
* Run

Komendy publikują zdarzenie:

* BuildRequested

Samo wykonanie odbywa się w rozszerzeniu.

---

# 5. Integracja z IProcessService

Kompilator może:

* używać IProcessService do uruchamiania procesów,
* przekazywać stdout/stderr do Output,
* publikować zdarzenia o postępie.

Terminal może być opcjonalnym widokiem procesu.

---

# 6. Diagnostyka

Diagnostyka powinna być reprezentowana jako model:

* FilePath
* Line
* Column
* Severity (Info / Warning / Error)
* Message
* Source (CompilerId)

Diagnostyka jest publikowana jako zdarzenie:

* DiagnosticsUpdated

Panel diagnostyki jest osobnym rozszerzeniem.

---

# 7. Wiele Kompilatorów

System musi umożliwiać:

* jednoczesne istnienie wielu ICompilerService,
* wybór aktywnego kompilatora przez konfigurację workspace,
* możliwość przypisania kompilatora do rozszerzenia pliku.

Brak zgodnego kompilatora powoduje:

* brak akcji build,
* komunikat informacyjny w Output.

Nie jest to błąd krytyczny.

---

# 8. Reakcja na Zdarzenia Workspace

Kompilator może reagować na:

* DocumentSaved
* WorkspaceOpened

Może:

* uruchamiać automatyczny build,
* aktualizować konfigurację,
* odświeżać zależności.

## 8.1. Trwały stan builda Junexis

Stdout procesu opisuje wykonanie komendy, ale nie jest trwałym stanem IDE.
Junexis compiler publikuje kontrakt `latest-build` 1.0 dopiero po zapisaniu
wszystkich artefaktów. Studio udostępnia `ILatestBuildReceiptService`, który:

* akceptuje wyłącznie wspieraną wersję kontraktu,
* sprawdza `buildId`, targety i bezwzględne ścieżki,
* porównuje `buildId` receiptu z bundle, inventory i Analysis Snapshot,
* nie pozwala manifestom wyjść poza `outputRoot`,
* wymaga deterministycznej lokalizacji `.junexis/state/`,
* odtwarza stan po `WorkspaceOpened`.

Dopiero zweryfikowany receipt publikuje `LatestBuildUpdatedEvent`, a następnie
zdarzenia kompatybilności dla paneli Artifacts i Analysis. Brak receiptu lub
rozbieżny `buildId` nie uruchamia skanowania katalogów ani heurystyki
„najnowszego pliku”.

---

# 9. Minimalny zakres v0.1

Pierwsza wersja kompilatora powinna:

* obsługiwać jeden profil build,
* wywoływać zewnętrzny proces (np. g++),
* parsować stdout w celu wyciągnięcia błędów,
* publikować diagnostykę,
* wyświetlać wynik w Output.

Bez:

* incremental build,
* dependency graph,
* równoległych buildów,
* zaawansowanego cache.

---

Kolejny dokument:

08_MCP_jako_Rozszerzenie.md

W nim zostanie opisany model integracji MCP oraz komunikacja z zewnętrznymi usługami.

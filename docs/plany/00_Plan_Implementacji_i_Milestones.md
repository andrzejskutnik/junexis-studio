# 13 – Plan Implementacji i Milestones

> **Stan na 2026-08-13:** wydano v0.16.0. Milestone v0.1–v0.16 sąąą zamknięte.
> Poniżej: historia zamkniętych milestones oraz aktualny plan kontynuacji.

---

# 1. Cel

Ten dokument zamienia architekturę w plan realizacji.

Założenia:

* iteracyjny rozwój,
* częste „działające punkty kontrolne",
* minimalny rdzeń,
* rozszerzenia jako nośniki funkcji,
* brak blokujących zależności.

Każdy milestone kończy się działającą wersją aplikacji.

---

# 2. Historia milestones (zamknięte)

| Wersja | Opis | Data |
|---|---|---|
| v0.1 | Minimalny Host — Shell, ExtensionLoader, Explorer, Editor | 2026-03-02 |
| v0.1.1 | UX — skróty, dialog niezapisanych zmian, status bar, ikony plików, terminal | 2026-03-02 |
| v0.2 | Kompilator i Diagnostyka (g++, C#, Go; Problems panel) | 2026-03-04 |
| v0.3 | MCP Minimal — IMcpConnector, McpStatusExtension | 2026-03-04 |
| v0.4 | VCS Minimal — Git CLI, Changes panel, dekoracje Explorer | 2026-03-04 |
| v0.5 | OMWiA Minimal — panel i subskrypcje zdarzeń IDE | 2026-03-04 |
| v0.6 | Run commands — F5 uruchamia artefakt | 2026-03-04 |
| v0.7 | Layout zakładek — VCS / MCP Status / OMWiA widoczne w ToolTabsHost | 2026-03-04 |
| v0.8 | OMWiA Chat panel wbudowany w IDE | 2026-03-04 |
| v0.9 | Layout resizing, ShellLayoutStore, filtrowanie Output | 2026-03-05 |
| v0.10 | Build cancel, VCS commit flow, git branch w status barze | 2026-03-05 |
| v0.11 | Menu Edit, komendy edytora, naprawa regresji UI-thread, brakujące capability w Host | 2026-08-13 |
| v0.12 | Debug + Narrative pipeline aktywny (DapDebuggerExtension, F5→debug.selected, Shift+F5→stop) | 2026-08-13 |
| v0.13 | _pominięty — LSP wymaga zewnętrznego serwera_ | — |
| v0.14 | Workspace Search Ctrl+Shift+F, F2/Delete w Explorer, ConfirmWindow, dialog potwierdzenia Delete | 2026-08-13 |
| v0.15 | Toolchain Doctor, WorkbenchRun, ILaunchProfileRegistry, IWorkbenchOperationService aktywne | 2026-08-14 |
| v0.16 | Analysis+Artifact Explorer, HelpCenter, OutputFilter, TypeScript compiler, .ts dispatch | 2026-08-14 |

---

# 3. Aktualny plan — v0.17 i dalej

## v0.12 — Debug i Narrative w IDE

### Zakres

* Pełne powiązanie panelu Narrative z sesją `NarrativeSessionService` (już zarejestrowaną w Host).
* Panel debugowania DAP — widok wątków, stosu wywołań, zmiennych lokalnych.
* `IDebugHoverService` aktywny w edytorze (hover nad zmienną w trybie Stopped).
* Komenda `debug.start` / `debug.stop` podpięta do F5 / Shift+F5 gdy aktywny projekt Junexis.

### Kryterium sukcesu

* Można uruchomić sesję debugowania na projekcie Junexis.
* Panel Narrative pokazuje trace z bieżącej sesji.
* Hover w edytorze pokazuje wartość zmiennej (lub komunikat o braku zatrzymania).

---

## v0.13 — Language Services (LSP)

### Zakres

* `LanguageService` i `LanguageServicesExtension` aktywne w domyślnym projekcie Junexis.
* Completions, hover i diagnostyki LSP w edytorze przez `ILanguageService`.
* Panel Language Services pokazuje aktywne profile i umożliwia ręczny restart.
* Konfiguracja profili LSP przez `junexis.project.toml`.

### Kryterium sukcesu

* Otwarcie pliku `.jnx` aktywuje LSP i pokazuje completions.
* Błędy LSP trafiają do Problems.
* Brak LSP nie blokuje pracy z IDE.

---

## v0.14 — Workspace Search i Explorer (file ops)

### Zakres

* Panel `junexis.workspace.search` (WorkspaceSearchExtension) z pełnym UX wyszukiwania.
* Komendy Explorer: `newFile`, `newFolder`, `rename`, `move`, `delete` z potwierdzeniem.
* Klawiatura w Explorer: `F2` dla rename, `Delete` dla usunięcia.

### Kryterium sukcesu

* `Ctrl+Shift+F` otwiera panel wyszukiwania.
* Można stworzyć, zmienić nazwę i usunąć plik/folder z Explorer.

---

## Dalsze kierunki (backlog)

* **Multi-root workspace** — wiele folderów otwartych jednocześnie.
* **Roslyn kompilator C#** — pełna integracja z diagnostykami Roslyn.
* **Advanced terminal** — wiele zakładek terminalowych, historia.
* **Extension marketplace** — lokalny ładownik zewnętrznych rozszerzeń z manifestem.
* **Deploy Center** — `DeployCenterExtension` z integracją `IDeploymentService`.
* **Toolchain Doctor** — diagnostyka środowiska w Start Center.

---

# 4. Zasady Realizacji

1. Każdy milestone kończy się działającą wersją.
2. Najpierw infrastruktura, potem funkcje.
3. Najpierw minimalne API, potem rozbudowa.
4. Nie implementujemy „na zapas".
5. Każde rozszerzenie musi działać lub degradować się łagodnie.

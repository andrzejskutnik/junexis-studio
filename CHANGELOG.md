# CHANGELOG.md

Format inspirowany: *Keep a Changelog* oraz *Semantic Versioning*.

## Unreleased

### Build

- Public Studio builds now restore its compiler, Mentor, MCP, and observability
  assemblies from the MIT-licensed `Junexis.Studio.Platform.Binaries` NuGet
  package. The GitHub Actions workflow builds and tests the complete solution.
- M1002/M1012 contract tests now use checked-in fixtures, so they run without a
  neighboring Mentor checkout.

### 2026-09-25 — wspólny adres panelu

Wyjątek właściciela: ta implementacja może powstać bez issue. Zakres obejmuje
przekazywanie gotowego adresu CLI → Studio / VS Code, nadzór sesji i testy.
Obsługa `junexis.panel-ready/1` dodaje panel server-only z deklaracji aplikacji,
zachowując starszy komunikat report. Brak deklaracji nie tworzy fikcyjnego adresu.
Wyniki i ograniczenia odnotowano w wewnętrznym raporcie walidacyjnym platformy,
który nie jest częścią tego publicznego repozytorium.


### Added

- M20.11.3 pozwala nowej sesji Studio przejąć nadzór nad przerwaną operacją
  wyłącznie przez `junexis update history`. Przejęcie nie wznawia mutacji.
  Redakcja wyników obejmuje też tokeny Bearer, dane użytkownika w URL, sekrety
  parametrów zapytania oraz bezwzględne ścieżki Windows i Unix.

- M20.11.2 dodaje panel **Cykl platformy**. Studio prezentuje plan i historię,
  pozwala anulować działanie oraz deleguje naprawę i powrót wyłącznie do
  publicznego `junexis` przez `IProcessService`. Wykonanie naprawy lub powrotu
  wymaga wcześniejszego udanego planu dla niezmienionych parametrów.

- M19.5.1 dodaje wspólne wykrywanie publicznych narzędzi z profilu użytkownika
  na Windows, Linux i macOS. Studio nie przeszukuje już sąsiednich repozytoriów
  w poszukiwaniu Kompilatora.

- M17.11.3 potwierdza zakończenie całego drzewa procesu przy zamknięciu
  rozszerzenia oraz bezpieczne ponowienie restore na tym samym projekcie.

- M17.10.6 usuwa z komunikatów restore tokeny, nazwane dane uwierzytelniające,
  parametry zapytania URL oraz bezwzględne ścieżki profilu użytkownika.

- M17.11.2 dodaje diagnostyki `JXR001`–`JXR006` dla braku zależności offline,
  timeoutu, limitów Registry, konfliktu artefaktu i niezgodności providera z
  SDK. Każda diagnostyka podaje bezpieczne działanie naprawcze wykonywane przez
  publiczne `junexis restore`.

- M17.11.1 prezentuje trzy etapy publicznego `junexis restore` i bezpieczną
  nazwę źródła. `JUNEXIS_REGISTRY` wybiera Registry bez ujawniania adresu w
  komunikacie Studio, a tryb offline nie przekazuje sprzecznego źródła
  lokalnego. Cały przepływ nadal używa wyłącznie `IProcessService`.

- Rozszerzenie Junexis rejestruje komendy `restore.jnx` i
  `restore.jnx.offline`, które przez `IProcessService` uruchamiają publiczne
  `junexis restore` dla otwartego projektu. `JUNEXIS_CLI` wskazuje publiczne
  wejście, a opcjonalne `JUNEXIS_LOCAL_PACKAGES` wskazuje lokalny zestaw
  wydania dla providerów. Zestaw Studio zawiera publiczne CLI Installera w
  `tools/junexis`; komendy blokują równoległy restore i anulują proces przy
  zamknięciu rozszerzenia.

---

## 0.16.0 - 2026-08-14

Analysis Explorer, Artifact Explorer, Help Center, Output Filter, TypeScript compiler aktywne. Łącznie **1632 testy**, 0 błędów.

### Added

- `JunexisStudioHost` rejestruje teraz `IAnalysisSnapshotService`, `IArtifactService` i `IDebugTargetAssessmentService` w capabilities.
- Do loadera dodano: `TypeScriptCompilerExtension` (`build.typescript`, `run.typescript`, `build.typescript.stop`), `HelpCenterExtension`, `OutputFilterExtension`, `AnalysisExplorerExtension`, `ArtifactExplorerExtension`.
- `project.build` i `project.run` obsługują teraz `.ts` → `build.typescript` / `run.typescript`.

## 0.15.0 - 2026-08-14

Toolchain Doctor i Workbench Run aktywne w IDE. Łącznie **1625 testów**, 0 błędów.

### Added

- `JunexisStudioHost` rejestruje teraz `IToolchainDoctorService`, `IWorkbenchOperationService` i `ILaunchProfileRegistry` w capabilities; `ToolchainDoctorExtension` i `WorkbenchRunExtension` dodane do loadera — panel Toolchain Doctor (`junexis.toolchains.doctor`) i `WorkbenchRunViewModel` są aktywne po starcie hosta.

## 0.14.0 - 2026-08-13

Workspace Search UX, skróty Explorer i dialog potwierdzenia Delete. Łącznie **1620 testów**, 0 błędów.

### Added

- `Ctrl+Shift+F` otwiera panel Workspace Search (komenda `view.search`).
- `F2` w Explorer wywołuje `explorer.rename` na zaznaczonym węźle; `Delete` wywołuje `explorer.delete`.
- `explorer.delete` pokazuje teraz dialog potwierdzenia (OK/Cancel) przed usunięciem węzła, gdy `IWorkspaceInteractionService` jest dostępny.
- `IWorkspaceInteractionService` rozszerzony o `ConfirmAsync(title, message)` — nowe `ConfirmWindow` (AXAML + code-behind) wzorowane na `TextPromptWindow`.

---

## 0.12.0 - 2026-08-13

Debug + Narrative pipeline aktywny w IDE. Łącznie **1619 testów**, 0 błędów.

### Added

- `JunexisStudioHost` rejestruje teraz `IBreakpointService`, `IDebugAdapterRegistry` i `IDebugConfigurationService` w capabilities; `DapDebuggerExtension` dodany do loadera — panel Debug (`junexis.debugger.dap`) z komendami `debug.gpp`, `debug.go`, `debug.continue`, `debug.session.stop`, `debug.why` jest aktywny po starcie hosta.
- `project.run` (F5) dispatchuje teraz `.jnx` do `debug.selected` zamiast wypisywać ostrzeżenie o nieobsługiwanym języku.
- `build.stop` (Shift+F5) wywołuje teraz również `debug.session.stop` gdy komenda jest dostępna, więc jeden skrót zatrzymuje zarówno build jak i sesję debugowania.

### Notes

- `NarrativeSessionService`, `IDebugHoverService` i powiązanie Why panelu z sesją były już zaimplementowane — v0.12 domknął brakujące rejestracje i routing F5/Shift+F5.

---

## 0.11.0 - 2026-08-13

Komendy edytora, menu Edit i naprawa regresji UI-thread. Łącznie **1613 testów**, 0 błędów.

### Added

- `Edit` menu i komendy edytora rozszerzone o `Undo`, `Redo`, `Cut`, `Copy`, `Paste` i `Select All` wraz ze skrótami klawiaturowymi oraz spójnym menu kontekstowym dla edytora.
- `Edit/Go to Line...` z `Ctrl+G` dodaje szybką nawigację do wskazanej linii lub pozycji `line:column` w aktywnym dokumencie, wykorzystując istniejący prompt Shell i mechanizm nawigacji zakładki.
- `Edit` dostało też skróty `F3` / `Shift+F3` dla `Find Next` / `Find Previous`, `Ctrl+H` dla `Replace` oraz alternatywny standardowy binding `Ctrl+Shift+Z` dla `Redo`.
- Edytor dostał też `Ctrl+L` dla `Select Line` oraz alternatywny skrót `Ctrl+F4` dla `Close Tab`.
- Edytor wspiera teraz szybkie przełączanie otwartych kart przez `Ctrl+Tab` / `Ctrl+Shift+Tab` oraz alternatywnie `Ctrl+PageDown` / `Ctrl+PageUp`.
- Shell pokazuje skróty klawiaturowe przy pozycjach menu i menu kontekstowego na podstawie aktywnych bindingów hosta.
- Shell pokazuje teraz przy jednej pozycji menu wszystkie skróty przypisane do tej samej komendy, zamiast tylko pierwszego bindingu.

### Changed

- Operacje clipboard i podstawowe akcje edycyjne są wykonywane przez capability aktywnego edytora, zamiast przez logikę ViewModel dokumentu.
- `AGENTS.md` i `tests/README.md` zaktualizowano do bieżącej struktury repo (`extensions/`) i aktualnego workflow bez automatycznego przenoszenia issue przez CI.
- `TerminalPanelViewModel.RunCommandLineAsync` owrapowuje teraz polecenie w powłokę systemową (`cmd.exe /c` na Windows, `/bin/sh -c` na innych platformach), dzięki czemu działają wbudowane komendy (`dir`, `echo`, `ls`).

### Fixed

- Terminal panel: `TerminalPanelViewModel` nie odtwarza już wyniku procesu po `await` poza kontekstem UI, więc `IsRunning`, `LastExitCode` i `ProcessFinished` pozostają bezpieczne dla bindingów Avalonia.
- OMWiA Chat: `OmwiaChatViewModel` nie odtwarza już odpowiedzi po `await` poza kontekstem UI, więc odświeżenie historii i flagi `IsBusy` pozostaje bezpieczne dla bindingów Avalonia.
- Why panel: `WhyPanelViewModel` nie zrywa już kontekstu UI podczas `LoadAsync`, dzięki czemu aktualizacja właściwości po odpowiedzi HTTP pozostaje bezpieczna dla Avalonia bindingów.
- Why panel: walidacja pustego URL i błędnego `Event ID` czyści poprzedni `CausalChain`, więc panel nie pokazuje już nieaktualnych wyników po nieudanej próbie.
- Why panel: anulowane zapytanie `LoadAsync` kończy się neutralnym statusem `Why query cancelled.` zamiast komunikatu błędu, więc zwykły cancel nie wygląda już jak awaria TraceCollector.
- `JunexisStudioHost` rejestruje teraz brakujące capability: `ILatestBuildReceiptService`, `INarrativeSessionService`, `INarrativeRuntimeEnvironmentService`, `IDebugSessionService`, `IDebugHoverService` i `ILanguageService`; dodano do loadera `WorkspaceSearchExtension` i `LanguageServicesExtension`.
- `explorer.refresh` zmienił `MenuPath` z `File/Refresh Explorer` na `Workspace/Refresh Explorer` zgodnie ze specyfikacją menu.

### Removed

- Usunięto job `move_issues_to_testing` z `.gitlab-ci.yml` oraz skrypt `ci/move_issues_to_testing.sh`, bo nie wnosiły już wartości do bieżącego workflow repo.

---

## 0.10.0 - 2026-03-05

Build cancel, VCS commit flow i branch w status barze. Łącznie **1070 testów**, 0 błędów.

### Added

- **#118 build.stop** — anulowanie aktywnej kompilacji / uruchomienia (`Shift+F5`):
  `build.gpp.stop` / `build.csharp.stop` / `build.go.stop` w każdym rozszerzeniu
  kompilatora; globalny `build.stop` w `JunexisStudioHost` iteruje po wszystkich trzech;
  każde rozszerzenie: `_buildCts`/`_runCts` (`CancellationTokenSource`),
  `CancelBuild()`, `Shutdown()` anuluje aktywne procesy; 10 testów.

- **#119 VCS commit flow** — uzupełnienie panelu Changes:
  `VcsChangesViewModel` implementuje `INotifyPropertyChanged`;
  `CommitMessage` (INPC), `CanCommit` (bool), `Commit()` czyści wiadomość na
  sukces; `VcsChangesView.axaml` — pasek (Stage All / Unstage All / Refresh),
  dblclick na liście Staged → Unstage, dblclick na Unstaged → Stage,
  TextBox commit message + Button Commit (`IsEnabled=CanCommit`); 9 testów.

- **#120 Status bar — git branch** — `VcsBranchChangedEvent(string BranchName)`
  w Abstractions; `GitProviderExtension` publikuje branch po init i po
  każdym `vcs.git.refresh`; `MainWindow.axaml` — status bar zmieniony na
  `DockPanel`: `StatusBarBranch` (⎇ main, niebieski, lewo) +
  `StatusBarText` (info plik/karet, prawo);
  `SubscribeVcsBranch()` aktualizuje tekst na wątku UI; 2 testy.

---

## 0.9.0 - 2026-03-05

Layout resizing, trwałość layoutu i filtrowanie Output. Łącznie **1049 testów**, 0 błędów.

### Added

- **#115 GridSplitter** — dwa splitter'y w `MainWindow.axaml`:
  pionowy między kolumną eksploratora a edytorem (resize lewo/prawo)
  oraz poziomy między głównym obszarem edytora a paskiem dolnych paneli
  (resize góra/dół); minimalne rozmiary kolumn/wierszy blokują zamknięcie
  paneli do zera.

- **#116 ShellLayoutStore** — klasa `ShellLayoutStore` w Core zapisuje
  i odczytuje pozycje splitterów do/z pliku JSON
  (`%LOCALAPPDATA%\JunexisStudio\shell-layout.json`);
  `ShellLayoutData(ExplorerWidth, ToolTabsHeight)` z wartościami domyślnymi
  (260 / 180); zapis przy `Window.Closing`, odczyt przy starcie Shell;
  obsługa brakującego pliku i uszkodzonego JSON (fallback do defaults);
  11 testów w `ShellLayoutStoreTests`.

- **#117 Output panel filter toolbar** — `OutputPanelViewModel` w Host:
  `FilteredEntries` (ObservableCollection z live-filter),
  `SelectedChannel` (ComboBox kanałów, „All" = brak filtru),
  `SelectedSeverity` (All/Info/Warning/Error), komenda `Clear`;
  `OutputPanelView.axaml` przepisany z toolbar (dwa ComboBox + przycisk Clear)
  i ScrollViewer z auto-scroll; usunięty stary stub `OutputPanelViewModel.cs`
  z `src/Shell`.

### Fixed

- `RunCommandTests`: trait `[Trait("Issue","run")]` zastąpiony poprawnym
  `[Trait("Issue","113")]` (10 miejsc).
- `ShellLayoutStoreTests`: dodano `[Trait("Issue","116")]` na poziomie klasy.

---

## 0.8.0 - 2026-03-04

OMWiA Chat panel — widok czatu wbudowany w IDE. Łącznie **1038 testów**, 0 błędów.

### Added

- **AssistantService** (tryb offline) zarejestrowany jako `IAssistantService` capability w `JunexisStudioHost`.
  Backend odpowiada deterministycznie: echo wiadomości użytkownika z prefixem `[offline]`.
- **OmwiaChatExtension** dodany do loadera w `JunexisStudioHost` — rejestruje panel `junexis.omwia.chat`
  z tytułem „OMWiA Chat" w zakładce ToolTabsHost (DockGroup.Right).
- **OmwiaChatView.axaml + OmwiaChatView.axaml.cs** — nowy widok czatu:
  - lista wiadomości z podziałem na Role (User/Assistant) i Content
  - pole wejściowe + przycisk „Wyślij" (Enter bez Shift też wysyła)
  - `ObservableCollection<AssistantMessage>` odświeżana przez `MessagesChanged`
  - `EnsureSession()` wywoływane automatycznie po załadowaniu DataContext
- `App.axaml.cs` — rejestracja `OmwiaChatViewModel → OmwiaChatView`.

### Fixed

- `OmwiaChatView.axaml.cs`: niejednoznaczność `KeyModifiers` (Abstractions vs Avalonia)
  — jawne kwalifikowanie `Avalonia.Input.KeyModifiers.Shift`.

---

## 0.7.0 - 2026-03-04

Layout — zakładki VCS / MCP Status / OMWiA widoczne w ToolTabsHost. Łącznie **1038 testów**, 0 błędów.

### Changed

- `MainWindow.axaml.cs` — `AddToolTab`: nagłówek zakładki używa teraz `registration.Title ?? registration.Id`
  zamiast surowego ID (wyniki: „Changes", „MCP Status", „OMWiA" zamiast `junexis.vcs.changes` itp.)
- `MainWindow.axaml.cs` — `TabItem.Tag` przechowuje panel ID; `BindContextMenus` korzysta z `Tag`
  do prawidłowego dobierania `contextTarget` komend (poprzednio Header = ID, teraz Header = Title).

### Fixed

- Zakładki VCS/MCP/OMWiA były rejestrowane z `PanelType.Tool`, lecz wyświetlały surowe ID zamiast
  przyjaznych tytułów.

---

## 0.6.0 - 2026-03-04

Run commands — F5 uruchamia zbudowany artefakt. Łącznie **1038 testów**, 0 błędów.

### Added

- **run.gpp** (`GppCompilerExtension`) — uruchamia `junexis_build.exe` / `junexis_build`
  z katalogu workspace; guard: jeśli plik nie istnieje → komunikat "Run build.gpp first";
  `_isRunning` guard (jak w build) blokuje podwójne uruchomienie.

- **run.csharp** (`CsharpCompilerExtension`) — uruchamia `dotnet run` z katalogu
  workspace; streaming output do kanału Build.

- **run.go** (`GoCompilerExtension`) — uruchamia `go run ./...` z katalogu workspace;
  streaming output do kanału Build.

- **project.run** (JunexisStudioHost wbudowana komenda) — smart dispatch po rozszerzeniu
  aktywnego dokumentu: `.cpp/.cc/.cxx/.c → run.gpp`, `.cs/.csproj/.sln → run.csharp`,
  `.go → run.go`; fallback → komunikat w Output o nieobsługiwanym języku.

- **F5 → project.run** — keybinding w `JunexisStudioHost.RegisterBuiltInKeybindings`.

- **Testy** — `RunCommandTests.cs` (13 testów):
  `GppExtension_RegistersRunGppCommand`, `RunGpp_WhenExeExists_*`,
  `RunGpp_WhenExeMissing_*`, `CsharpExtension_RegistersRunCsharpCommand`,
  `RunCsharp_ExecutesDotnetRun`, `GoExtension_RegistersRunGoCommand`,
  `RunGo_ExecutesGoRunDotDotDot`, `ProjectRun_DispatchesToCorrectRunCommand`
  (Theory × 4 pliki), `ProjectRun_WhenNoActiveFile_*`, `JunexisStudioHost_F5_BoundToProjectRun`.

---

## 0.5.0 - 2026-03-04

Milestone v0.5 — OMWiA panel. Łącznie **1025 testów**, 0 błędów.

### Added

- **OMWiA panel** — `OmwiaPanelViewModel`: subskrypcja zdarzeń IDE
  (`WorkspaceOpenedEvent`, `DocumentSaved`, `BuildRequestedEvent`),
  deterministyczne sugestie (bez LLM); `OmwiaExtension` rejestruje panel
  w prawej grupie doków; `OmwiaChatExtension` dla interfejsu czatu;
  `OmwiaPanelView.axaml` + rejestracja widoku w Shell.

---

## 0.4.0 - 2026-03-04

Milestone v0.4 — VCS minimal (Git CLI). Łącznie **1025 testów**, 0 błędów.

### Added

- **Git CLI provider** — `GitCliProvider`: `IsGitAvailableAsync`,
  `IsRepositoryAsync`, `GetChangesAsync`, `CommitAsync`; parser statusu
  `GitPorcelainStatusParser` (format `--porcelain=v1`).
- **GitProviderExtension** — rejestruje `IVcsService` w `CapabilityRegistry`;
  fallback: brak git / brak repo → degradacja z logiem w Output.
- **VcsExtension** — panel `Changes` (staged/unstaged listy), komendy
  `vcs.stageAll` / `vcs.unstageAll` / `vcs.refresh`; używa `IVcsService`
  z capabilities.
- **VcsDecorationExtension** — `VcsDecorationProvider`: dekoracje plików
  w Explorerze (badge M/U/A/D/R/C + kolor); subskrypcja `VcsStatusChangedEvent`.
- **GitVersionControlService** (IVersionControlService) — warstwa async
  delegująca do `GitCliProvider`; `vcs.commit` w `GitExtension`.
- **Shell** — `VcsChangesView.axaml` + rejestracja `VcsChangesViewModel →
  VcsChangesView` w `App.axaml.cs`.

---

## 0.3.0 - 2026-03-04

Milestone v0.3 — MCP connector (minimal). Łącznie **1025 testów**, 0 błędów.

### Added

- **IMcpConnector / IMcpConnectorRegistry** — kontrakty w Abstractions;
  `McpConnectorRegistry` (Core): thread-safe, active connector ID,
  connect/disconnect lifecycle.
- **McpStatusExtension** — `McpStatusViewModel`: log (Connected, Disconnected,
  Message, Error), `IsActiveConnected`, `ActiveConnectorId`; subskrypcja
  zdarzeń MCP na EventBus; panel w Bottom dock; komenda `mcp.status.clearLog`.
- **McpConnector** (Core) — implementacja `IMcpConnector` z stdio transport,
  heartbeat, circuit breaker; `McpMessageValidator`, `McpConfigStore`.
- **ToolCallExecutor** — obsługuje wywołania narzędzi przez MCP.
- **Shell** — `McpStatusView.axaml` + rejestracja `McpStatusViewModel →
  McpStatusView` w `App.axaml.cs`.
- `McpConnectorRegistry` zarejestrowane jako capability `IMcpConnectorRegistry`
  w `JunexisStudioHost`.

---

## 0.2.0 - 2026-03-04

Milestone v0.2 — kompilator i diagnostyka. Łącznie **1025 testów**, 0 błędów.

### Added

- **IProcessService / ProcessService** — kontrakty w Abstractions
  (`ProcessRequest`, `ProcessResult`, `ProcessOutputLine`, `ProcessOutputKind`);
  implementacja Core oparta o `System.Diagnostics.Process`;
  streaming stdout/stderr (`BeginOutputReadLine`), cancellation
  (`Kill(entireProcessTree: true)`), `WasCancelled` flag.
- **IDiagnosticService / DiagnosticService** — model `Diagnostic`
  (`FilePath`, `Line`, `Column`, `Message`, `Severity`); `DiagnosticSeverity`
  enum; `DiagnosticsChanged` event; thread-safe w Core.
- **ICapabilityRegistry / CapabilityRegistry** — lekki IoC lookup
  (`Register<T>` / `TryGet<T>` / `Get<T>`); `JunexisStudioHost` rejestruje
  `IProcessService`, `IDiagnosticService`, `IOutput`, `IMcpConnectorRegistry`.
- **GppCompilerExtension** + `GppDiagnosticParser` — komenda `build.gpp`;
  streaming output do kanału Build; parser błędów g++ (`file:line:col: error:`);
  diagnostyki trafiają do `IDiagnosticService`.
- **CsharpCompilerExtension** + `CsharpDiagnosticParser` — komenda `build.csharp`
  (dotnet build); analogiczny pipeline kompilacji i diagnostyki.
- **GoCompilerExtension** + `GoDiagnosticParser` — komenda `build.go`
  (go build); analogiczny pipeline.
- **ProblemsExtension** + `ProblemsPanelViewModel` — panel Problems
  wyświetlający snapshot `IDiagnosticService`; double-tap → publikacja
  `NavigateToLocationRequested`; `ProblemsPanelView.axaml` w Shell.
- **NavigateToLocationRequested** — zdarzenie EventBus (File, Line, Column)
  umożliwiające nawigację edytora do lokacji diagnostyki.

---

## 0.1.1 - 2026-03-02

Milestone UX v0.1 — skróty klawiszowe, dialog niezapisanych zmian, pasek stanu,
ikony plików, panel terminala z UI.
Łącznie **1025 testów**, 0 błędów.

### Added

- **#106 IKeybindingRegistry** — rejestr skrótów klawiaturowych (`KeyGesture`
  → `commandId`), thread-safe, last-wins, logowanie konfliktów do kanału
  `Keybindings`; `IHostContext.Keybindings`; `MainWindow.OnKeyDown` używa
  rejestru zamiast hard-coded switch.

- **#103 Ctrl+S / Ctrl+W** — komendy `editor.save` i `editor.closeActiveTab`
  w `EditorExtension`; keybindingi rejestrowane przez `JunexisStudioHost`;
  `editor.copyActivePath` jako bonus.

- **#104 Ctrl+F — panel wyszukiwania** — `editor.toggleSearch`;
  `DocumentTabViewModel.IsSearchVisible` + `ToggleSearch()`;
  `SearchPanel.Install(TextEditor)` w `DocumentTabView`; Ctrl+F toggle.

- **#105 Dialog niezapisanych zmian** — `UnsavedChangesResult` enum
  (`Save/Discard/Cancel`) w Abstractions; `EditorPanelViewModel
  .ConfirmCloseAsync` delegate + `CloseTabWithConfirmAsync()`;
  `UnsavedChangesWindow` (modal AXAML + TCS-based result);
  `MainWindow.OnClosing` — pętla dirty-tabs przed zamknięciem okna.

- **#107 Pasek stanu** — `DocumentTabViewModel.CaretLine/CaretColumn`
  aktualizowane przez `Caret.PositionChanged`; `EditorPanelViewModel
  .StatusText` (`● filename  Ln X, Col Y`);
  wiersz 24 px w `MainWindow.axaml`; subskrypcja INPC w Shell.

- **#109 Ikony plików** — `ExplorerNode.IconText` computed z rozszerzenia
  (case-insensitive): `.cs=♯`, `.axaml/.xaml=🖼`, `.json=🛠`, `.md=📝`,
  `.py=🐍`, folder=`📁`, default=`📄` i inne; `ExplorerPanelView` już
  bindował `IconText`.

- **#108 Panel terminala UI** — `TerminalPanelViewModel.RunCommandLineAsync
  (commandLine)` — parsuje na `fileName + arguments`;
  `OutputKindBrushConverter` (StdOut=szary, StdErr=łososiowy);
  `TerminalPanelView.axaml` — ScrollViewer + pasek wejścia (`❯`, TextBox,
  ▶/✕); real-time output, echo komendy, auto-scroll; rejestracja w
  `App.axaml.cs`.

### Fixed

- Poprawiono błędny symbol `♿` (wózek inwalidzki) użyty via copy-paste jako
  ikona pliku `.cs`/`.go`/`.py` — zastąpiony właściwymi symbolami Unicode.

---

## 0.1.0 - 2026-02-23

Pierwszy uruchamialny szkielet v0.1.

### Added

- Szkielet solution `JunexisStudio.sln` i projekty: Abstractions/Core/Host/Shell.
- Minimalny host (`JunexisStudioHost`) i loader rozszerzeń.
- Minimalna powłoka UI (Avalonia Shell) uruchamiająca hosta.
- Bazowe serwisy Core: event bus, rejestr komend, rejestr paneli, output, workspace.
- XML docs dla publicznego API (typy i członkowie) dla v0.1.
- `AGENTS.md` jako spis zasad obowiązujących w projekcie.
- `CHANGELOG.md` jako historia zmian.

### Changed

- Repo przestawione na `net10.0` (spójny TFM dla wszystkich projektów).
- Dokumentacja repo (quickstart + opis architektury v0.1) + przewodnik po kodzie v0.1.

### Fixed

- (brak)

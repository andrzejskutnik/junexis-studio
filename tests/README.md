# Testy (xUnit)

Ten katalog zawiera testy jednostkowe i (docelowo) integracyjne dla JunexisStudio.

Ścieżki zaczynające się od `../junexis-*` w tym dokumencie odnoszą się do
sąsiednich checkoutów w workspace platformy i nie są zawartością tego repo.
Pełne testy integracyjne wymagają tych zależności. Publiczny workflow CI
pomija dwa testy kontraktowe M1002/M1012 wymagające fixture'ów z repozytorium
Mentor, a pozostałe testy Core uruchamia z publicznego checkoutu.

`WhyPanelExtensionTests` obejmuje przepływ Diagnostic Story: timeline,
concurrency, evidence, zgodność runtime/analysis/Mentor build ID, filtrowanie
Mentora po stable symbol ID oraz eksport JSON/Markdown. Cross-repo kontrakt z
rzeczywistym TraceCollectorem sprawdza zewnętrzny skrypt
`test-studio-integration-e2e.ps1` z repozytorium platformy.
`NarrativeSessionServiceTests` sprawdza lifecycle, latest-build i wybór eventu,
a `RealNarrativeSessionSmokeTests` uruchamia rzeczywisty proces Collectora w
bramce platformowej M6.5.
M6.6 jest chronione przez `NarrativeRuntimeEnvironmentServiceTests`,
`RunCommandTests` i `DebugConfigurationBuilderTests`: tylko zdrowa sesja
loopback tworzy atomową parę URL/build ID, Run przekazuje ją do C++/Go/Node,
DAP `launch` umieszcza ją w `env`, a `attach` pozostaje bez zmian.
M6.7 dodaje testy przejścia `Starting → Ready`, timeoutu i anulowania,
potwierdza, że proces Run nie startuje przed gotowością sesji oraz że DAP
`attach` omija coordinator oczekiwania.
M6.8 sprawdza jawne stany aktywności, deltę i timestamp, automatyczne pobranie
Story po nowym evencie oraz możliwość wyłączenia auto-follow. Realny smoke
potwierdza ścieżkę ingest → monitor → panel Why → Diagnostic Story.
M6.9 sprawdza pauzę auto-follow bez zatrzymania capture, wznowienie z
nadrobieniem najnowszego eventu zgodnego z filtrami oraz automatyczną ścieżkę
DAP stopped frame → korelacja aktywnego build ID → Diagnostic Story. Realny
smoke odrzuca nowszy event innej Narrative i potwierdza anulowanie kontekstu po
wznowieniu debuggee.
M6.10 sprawdza filtrowane Previous/Next, zachowanie przypiętej Story podczas
nawigacji, jawny diff dowodów i granice historii. Realny smoke przechodzi
event `#3 → #1`, zachowując równocześnie korelację zatrzymanej klatki.
M6.11 sprawdza atomowy magazyn `.junexis/narrative-session.json`, odtworzenie
Story bez połączenia z Collectorem oraz ZIP z pełnym zestawem dowodów i
zweryfikowanymi SHA-256.
M6.12 sprawdza import ZIP bez wywołania Collectora, jawny tryb tylko do odczytu,
atomowy powrót do bookmarków workspace oraz odrzucenie zmienionego dowodu przed
zmianą stanu panelu.
M6.13 sprawdza provenance i pełny inwentarz zweryfikowanych plików oraz
kierunkowe porównanie importowanej Story z live Story zachowaną przed importem.
M6.14 sprawdza ECDSA P-256 export/import, fingerprint, trwałe lokalne zaufanie,
usunięcie zaufania, naruszony podpis i brak wymaganego `signature.json`.
M6.15 sprawdza oddzielną recenzję semantyczną, rewizje, podpis, unieważnienie
podpisu przez edycję oraz portable diagnostic case.
M6.16 sprawdza offline, build-correlated propozycje Mentora, zakaz automatycznej
Decision, jawne accept/dismiss i trwałe pochodzenie zaakceptowanego wpisu.

## Cel: szybka droga od buga do kodu

Docelowy przepływ triage wygląda tak:

1) mamy zgłoszony błąd
2) znajdujemy najbardziej pasujące issue (feature/regresja)
3) znajdujemy testy powiązane z tym issue
4) z testów przechodzimy do kodu produkcyjnego i fixujemy regresję

Ten dokument jest kontraktem, który ma to umożliwiać.

## Mapowanie testów do issue

Konwencja:

- Każda klasa/metoda testowa powinna mieć adnotację `[Trait("Issue","<nr>")]`.
- Jeśli test dotyczy wielu issue, dodajemy wiele traitów.

Przykład:

```csharp
[Fact]
[Trait("Issue", "13")]
public void Write_RespectsRingBufferLimit() { /* ... */ }
```

### Szybkie wyszukiwanie testów dla issue

- Uruchom testy oznaczone numerem issue:
  - `dotnet test JunexisStudio.sln -c Debug --filter "Issue=13"`
- Albo znajdź je po kodzie:
  - `grep -R "Trait(\"Issue\", \"13\"" -n tests/`

Jeśli filtr traitów nie działa w danym środowisku, grep jest zawsze pewny.

### Konwencja nazw testów (dla grepa)

Zalecenie (nie wymaganie):

- nazwa pliku/klasy zawiera testowany typ, np. `OutputServiceTests`
- nazwa testu opisuje zachowanie, np. `Write_RespectsRingBufferLimit`

W razie regresji szybciej znajdziesz testy i dotknięty kod.

## Checklist: bug → issue → testy → kod

1) Identyfikacja

- Zbierz minimalny opis (co, gdzie, kiedy, expected vs actual).
- Spróbuj wskazać obszar: `area::*`.

1) Znalezienie issue

- Jeśli bug jest regresją znanej funkcji: znajdź issue feature (np. przez etykiety/komentarze/MR).
- Jeśli nie ma pasującego issue: utwórz nowe issue typu bug i linkuj je do feature issue.

1) Znalezienie testów

- Uruchom: `dotnet test ... --filter "Issue=<nr>"`.

- Jeśli brak testów dla issue: dopisz test regresyjny jako pierwszy krok fixa.

1) Dojście do kodu

- Z testu przejdź do typu w `src/`.
- Jeśli bug dotyczy kilku modułów, dodaj dodatkowe testy lub oznacz test wieloma issue.

1) Reguła końcowa

- Fix buga jest gotowy dopiero, gdy test regresyjny jest zielony.

| Issue | Zakres | Lokalizacja |
|------:|--------|------------|
| M20.11.3 | Przejęcie nadzoru bez wznowienia mutacji oraz skan sekretów i bezwzględnych ścieżek | `tests/Junexis.Studio.Host.Tests/PlatformLifecycleViewModelTests.cs` |
| M20.11.2 | Panel cyklu platformy: plan, historia, anulowanie, naprawa i powrót wyłącznie przez publiczne CLI | `tests/Junexis.Studio.Host.Tests/PlatformLifecycleViewModelTests.cs` |
| #5 | PanelRegistry (rejestracja paneli) | `tests/Junexis.Studio.Core.Tests/PanelRegistryTests.cs` |
| #13 | OutputService (snapshot + event + ring buffer) | `tests/Junexis.Studio.Core.Tests/OutputServiceTests.cs` |
| #94 | Wprowadzenie testów + CI `dotnet test` | `tests/` + pipeline repozytorium |
| #95 | Doprecyzowanie traceability i checklisty triage | `tests/README.md` |
| #102 | Bug: brak contentu po dwukrotnym kliknięciu w Explorerze | `tests/Junexis.Studio.Host.Tests/ExplorerPanelViewModelTests.cs`, `tests/Junexis.Studio.Host.Tests/EditorPanelViewModelTests.cs` |
| — | JunexisLang syntax highlighting (xshd + registrar) | `tests/Junexis.Studio.Host.Tests/JunexisHighlightingRegistrarTests.cs` |
| — | Opt-in smoke prawdziwego Delve DAP na wygenerowanym Go | `tests/Junexis.Studio.Host.Tests/RealDapSmokeTests.cs` |
| — | Opt-in smoke standalone js-debug na wygenerowanym TypeScript | `tests/Junexis.Studio.Host.Tests/RealJsDapSmokeTests.cs` |
| — | Opt-in smoke OpenDebugAD7/GDB na wygenerowanym C++ | `tests/Junexis.Studio.Host.Tests/RealCppDapSmokeTests.cs` |
| — | Drzewa zmiennych, trwałe watches i editor hover evaluate | `tests/Junexis.Studio.Host.Tests/DebugPanelViewModelTests.cs`, `tests/Junexis.Studio.Host.Tests/DebugHoverServiceTests.cs` |
| — | Aktualna linia wykonania, conditional breakpoints, hit conditions i logpoints | `tests/Junexis.Studio.Core.Tests/BreakpointServiceTests.cs`, `tests/Junexis.Studio.Host.Tests/DebugExecutionLocationServiceTests.cs`, `tests/Junexis.Studio.Host.Tests/DapDebugSessionServiceTests.cs`, `tests/Junexis.Studio.Host.Tests/DebugSourceMapperTests.cs`, `tests/Junexis.Studio.Host.Tests/BreakpointGlyphServiceTests.cs`, `tests/Junexis.Studio.Host.Tests/DebugPanelViewModelTests.cs` |
| — | Trwałe breakpointy, exception breakpoints i mapowane Run to Cursor | `tests/Junexis.Studio.Core.Tests/BreakpointServiceTests.cs`, `tests/Junexis.Studio.Host.Tests/DapDebugSessionServiceTests.cs`, `tests/Junexis.Studio.Host.Tests/DebugPanelViewModelTests.cs`, `tests/Junexis.Studio.Host.Tests/EditorExtensionTests.cs` |
| — | Debug Console REPL, setVariable i data breakpoints/watchpoints | `tests/Junexis.Studio.Host.Tests/DapDebugSessionServiceTests.cs`, `tests/Junexis.Studio.Host.Tests/DebugPanelViewModelTests.cs` |
| — | Trwałe konfiguracje launch/attach, lista procesów i profile DAP C++/Go/TypeScript | `tests/Junexis.Studio.Core.Tests/DebugConfigurationServiceTests.cs`, `tests/Junexis.Studio.Host.Tests/DebugConfigurationBuilderTests.cs`, `tests/Junexis.Studio.Host.Tests/DebugProcessServiceTests.cs`, `tests/Junexis.Studio.Host.Tests/DapDebugSessionServiceTests.cs`, `tests/Junexis.Studio.Host.Tests/DebugPanelViewModelTests.cs` |
| — | Narrative-aware ocena celu, rekomendacja profilu i realny opt-in attach bez zabijania procesu | `tests/Junexis.Studio.Host.Tests/DebugTargetAssessmentServiceTests.cs`, `tests/Junexis.Studio.Host.Tests/DebugPanelViewModelTests.cs`, `tests/Junexis.Studio.Host.Tests/DapDebugSessionServiceTests.cs`, `tests/Junexis.Studio.Host.Tests/RealAttachDapSmokeTests.cs` |
| — | Stopped frame → Narrative event → WHY wraz z aktywacją panelu i brakiem zgadywania | `tests/Junexis.Studio.Host.Tests/WhyPanelExtensionTests.cs`, `tests/Junexis.Studio.Host.Tests/DebugPanelViewModelTests.cs`, `../junexis-narrative/Junexis.Debugger.TraceCollector.Tests/JunexisDebugManifestTests.cs`, `../junexis-narrative/Junexis.Debugger.TraceCollector.Tests/TraceCollectorClientContractTests.cs` |
| #32 / M6.12 | Weryfikowany import bundle, tryb tylko do odczytu i atomowe odrzucenie naruszonego dowodu | `tests/Junexis.Studio.Host.Tests/WhyPanelExtensionTests.cs`, `tests/Junexis.Studio.Host.Tests/RealNarrativeSessionSmokeTests.cs` |
| #33 / M6.13 | Provenance pakietu, inwentarz SHA-256 i porównanie imported–live | `tests/Junexis.Studio.Host.Tests/WhyPanelExtensionTests.cs`, `tests/Junexis.Studio.Host.Tests/RealNarrativeSessionSmokeTests.cs` |
| #34 / M6.14 | Podpis manifestu ECDSA P-256, fingerprint i lokalny trust store | `tests/Junexis.Studio.Host.Tests/WhyPanelExtensionTests.cs`, `tests/Junexis.Studio.Host.Tests/RealNarrativeSessionSmokeTests.cs` |
| #35 / M6.15 | Review Observation/Hypothesis/Decision/Action, rewizje, podpis i diagnostic case | `tests/Junexis.Studio.Host.Tests/WhyPanelExtensionTests.cs`, `tests/Junexis.Studio.Host.Tests/RealNarrativeSessionSmokeTests.cs` |
| #36 / M6.16 | Jawne propozycje Mentora, exact-build grounding i human acceptance boundary | `tests/Junexis.Studio.Host.Tests/WhyPanelExtensionTests.cs`, `tests/Junexis.Studio.Host.Tests/RealNarrativeSessionSmokeTests.cs` |
| Mentor 30.1 | Tryby obecności, schema/build freshness i delta findings bez publikowania stale result jako current | `tests/Junexis.Studio.Core.Tests/PreferencesServiceTests.cs`, `tests/Junexis.Studio.Host.Tests/PreferencesViewModelTests.cs`, `tests/Junexis.Studio.Host.Tests/MentorCliAnalysisServiceTests.cs`, `tests/Junexis.Studio.Host.Tests/AnalysisExplorerViewModelTests.cs` |
| Mentor 30.2 | Nieblokujący preflight compiler → Mentor przed code generation | `tests/Junexis.Studio.Core.Tests/BuildOrchestratorTests.cs`, `tests/Junexis.Studio.Host.Tests/MentorBuildPreflightServiceTests.cs` |
| Mentor 30.3 | Dowodowy kontekst Mentora przy dokładnie skorelowanej diagnostyce kompilatora | `tests/Junexis.Studio.Host.Tests/MentorDiagnosticContextResolverTests.cs`, `tests/Junexis.Studio.Host.Tests/ProblemsPanelViewModelTests.cs` |
| Mentor 30.4 | Refaktoryzacje M1002 i jednoznacznego M1012 z preview, apply, compiler check oraz jawnym keep/revert | `tests/Junexis.Studio.Core.Tests/MentorRefactoringServiceTests.cs`, `tests/Junexis.Studio.Host.Tests/AnalysisExplorerViewModelTests.cs` |
| Mentor 30.5 | Atomowa `.junexis/mentor.yml`, decyzje, wąskie wyciszenia, expiry i jawny dowód zastosowania | `tests/Junexis.Studio.Core.Tests/MentorProjectPolicyServiceTests.cs`, `tests/Junexis.Studio.Host.Tests/MentorBuildPreflightServiceTests.cs`, `tests/Junexis.Studio.Host.Tests/AnalysisExplorerViewModelTests.cs` |
| M8.9 | Nieblokująca opinia Semantic Conformance, discovery raportu i karta Analysis → Mentor → Conformance | `tests/Junexis.Studio.Host.Tests/MentorCliAnalysisServiceTests.cs`, `tests/Junexis.Studio.Host.Tests/AnalysisExplorerViewModelTests.cs` |
| Etap 31.2 | Walidacja `latest-build` 1.0, bezpieczne ścieżki i odtworzenie paneli po otwarciu projektu | `tests/Junexis.Studio.Core.Tests/LatestBuildReceiptServiceTests.cs`, `tests/Junexis.Studio.Host.Tests/LatestBuildIntegrationTests.cs`, `tests/JunexisLang.CompilerExtension.Tests/JunexisCliOutputParserTests.cs` |
| M7.5 | Deploy Center: verified receipt → readiness → package/preflight → bounded local deploy dla `server/report` | `tests/Junexis.Studio.Host.Tests/LatestBuildIntegrationTests.cs`, `tests/Junexis.Studio.Host.Tests/DeployCenterViewModelTests.cs`, `tests/JunexisLang.CompilerExtension.Tests/JunexisDeploymentServiceTests.cs` |
| M7.6 | Persistent local receipt/session, lease/heartbeat, restore, adopt i owner-aware stop | `tests/Junexis.Studio.Host.Tests/DeployCenterViewModelTests.cs`, `tests/JunexisLang.CompilerExtension.Tests/JunexisDeploymentServiceTests.cs` |
| M7.7 | Embedded probe → jawny wybór → verified package → human confirmation → flash receipt | `tests/Junexis.Studio.Host.Tests/DeployCenterViewModelTests.cs`, `tests/JunexisLang.CompilerExtension.Tests/JunexisDeploymentServiceTests.cs` |
| M7.8 | Build ID + System Bundle ID od deploymentu do Narrative Story 1.2 i exact-pair history | `tests/Junexis.Studio.Host.Tests/WhyPanelExtensionTests.cs`, `tests/Junexis.Studio.Host.Tests/RealNarrativeSessionSmokeTests.cs`, `../junexis-narrative/Junexis.Debugger.TraceCollector.Tests/JunexisDebugManifestTests.cs` |
| M7.9 | Odłączony podpis System Bundle, jawna polityka permissive/signed/trusted i wynik autentyczności w Deploy Center | `tests/Junexis.Studio.Host.Tests/DeployCenterViewModelTests.cs`, `tests/JunexisLang.CompilerExtension.Tests/JunexisDeploymentServiceTests.cs`, `../junexis-lang/compiler/JunexisLang.Compiler.CLI.Tests/SystemBundleContractTests.cs`, `../junexis-lang/compiler/JunexisLang.Compiler.CLI.Tests/DeploymentPreflightTests.cs` |
| #101 / Git P0 | Menu Commit bez mutacji, commit poza UI i blokada równoległej operacji | `tests/Junexis.Studio.Host.Tests/GitExtensionTests.cs`, `tests/Junexis.Studio.Host.Tests/VcsExtensionTests.cs` |
| Git P1 | Wspólny snapshot index/worktree, prawdziwy stage i lifecycle workspace | `tests/Junexis.Studio.Host.Tests/GitWorkspaceVcsServiceTests.cs`, `tests/Junexis.Studio.Host.Tests/GitCliProviderTests.cs`, `tests/Junexis.Studio.Host.Tests/VcsExtensionTests.cs` |
| Git P2 | Porcelain v2 NUL, rename/copy, bezpieczne argumenty, debounce, sekcje Changes i diff preview | `tests/Junexis.Studio.Host.Tests/GitCliProviderTests.cs`, `tests/Junexis.Studio.Host.Tests/GitWorkspaceVcsServiceTests.cs`, `tests/Junexis.Studio.Host.Tests/VcsExtensionTests.cs`, `tests/Junexis.Studio.Host.Tests/VcsGitDiffTests.cs` |
| Git P3 | Commit preflight, autor, cached stat, hook/GPG failure, potwierdzenie dużego zakresu, timeout, Cancel i weryfikacja HEAD | `tests/Junexis.Studio.Host.Tests/GitCliProviderTests.cs`, `tests/Junexis.Studio.Host.Tests/GitWorkspaceVcsServiceTests.cs`, `tests/Junexis.Studio.Host.Tests/VcsExtensionTests.cs` |
| Git P4 | Historia i szczegóły, branche, stash, jawne remote/branch, fast-forward pull i tymczasowe repo end-to-end | `tests/Junexis.Studio.Host.Tests/GitRepositoryP4Tests.cs`, `tests/Junexis.Studio.Host.Tests/GitCliProviderTests.cs` |

## Realny smoke DAP

Test integracyjny jest domyślnie neutralny. Aby uruchomić go na artefakcie
kompilatora, ustaw `JUNEXIS_REAL_DAP_ROOT` na katalog buildu zawierający
`debug/junexis.source-map.json` i `server/main.go`, a następnie uruchom:

```powershell
dotnet test tests/Junexis.Studio.Host.Tests/Junexis.Studio.Host.Tests.csproj `
  --filter "FullyQualifiedName~RealDapSmokeTests"
```

Smoke TypeScript wymaga buildu `report`, skompilowanego przez `tsc` do katalogu
`report/dist`, oraz pliku uruchomieniowego `report/dist/smoke.js`. Ustaw
`JUNEXIS_REAL_JS_DAP_ROOT` na katalog buildu zawierający także
`debug/junexis.source-map.json`, a następnie uruchom:

```powershell
dotnet test tests/Junexis.Studio.Host.Tests/Junexis.Studio.Host.Tests.csproj `
  --filter "FullyQualifiedName~RealJsDapSmokeTests"
```

Smoke C++ wymaga buildu `embedded` z mapą źródeł i hostowego programu
`embedded/junexis_build.exe` skompilowanego przez G++ z `-g -O0`. Ustaw
`JUNEXIS_REAL_CPP_DAP_ROOT` na katalog buildu zawierający `debug/` i
`embedded/`, a następnie uruchom:

```powershell
dotnet test tests/Junexis.Studio.Host.Tests/Junexis.Studio.Host.Tests.csproj `
  --filter "FullyQualifiedName~RealCppDapSmokeTests"
```

Test wymaga lokalnego rozszerzenia VS Code C/C++ z `OpenDebugAD7` oraz GDB.
Weryfikuje breakpoint `.jnx` zmapowany na wygenerowany C++, powrotną klatkę
stosu, odczyt zmiennej lokalnej i `evaluate("answer + 1")` przez prawdziwy
adapter.

### Realny attach do działających procesów

`RealAttachDapSmokeTests` korzysta z już działających procesów, aby test nie
narzucał programu ani czasu życia debuggee. Dla wybranego profilu ustaw parę
zmiennych z katalogiem buildu i PID-em:

- C++: `JUNEXIS_REAL_CPP_ATTACH_ROOT`, `JUNEXIS_REAL_CPP_ATTACH_PID`,
- Go: `JUNEXIS_REAL_GO_ATTACH_ROOT`, `JUNEXIS_REAL_GO_ATTACH_PID`,
- Node: `JUNEXIS_REAL_JS_ATTACH_ROOT`, `JUNEXIS_REAL_JS_ATTACH_PID`.

Następnie uruchom:

```powershell
dotnet test tests/Junexis.Studio.Host.Tests/Junexis.Studio.Host.Tests.csproj `
  --filter "FullyQualifiedName~RealAttachDapSmokeTests"
```

Test używa rzeczywistego adaptera, wykonuje DAP `attach`, odłącza Studio i
potwierdza, że obserwowany proces nadal działa. Brak zmiennych pozostawia test
neutralny, tak jak pozostałe opt-in smoke.

### Semantic Conformance M8.9–M8.10

Testy hosta weryfikują mapowanie nieblokującej opinii Mentora do karty
`Analysis → Mentor → Conformance`. Dla raportu 0.2 sprawdzają także propagację
Build ID, Bundle ID i bindingu oraz prezentację statusu `BOUND`; raport
projektowy bez tożsamości pozostaje `UNBOUND`.

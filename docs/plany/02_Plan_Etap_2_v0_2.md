# Plan – Etap 2 (v0.2)

Etap 2 ma zmienić JunexisStudio z „modularnego szkieletu UI” w narzędzie, które potrafi wykonywać realne operacje inżynierskie: uruchamiać procesy zewnętrzne, kompilować, zbierać wynik i prezentować diagnostykę.

Dokument jest skonsolidowaną specyfikacją v0.2 (bez dialogów i powtórzeń). Zasady architektury (brak twardych zależności między extension, degradacja bez blokowania) pozostają bez zmian.

## Cel strategiczny v0.2

- Dodać fundament do uruchamiania narzędzi zewnętrznych (toolchain), bez blokowania UI.
- Zrealizować pierwszy „prawdziwy” extension narzędziowy: kompilator C++ oparty o g++.
- Wprowadzić podstawy diagnostyki (model + serwis), niezależne od logów/Output.
- Wprowadzić lekki capability lookup (żeby extension mogły opcjonalnie korzystać z usług) – bez twardych zależności.

## Nie-cele (świadomie poza v0.2)

- Marketplace, instalacja z internetu, wersjonowanie/aktualizacje extension.
- Unload/hot-reload extension.
- Roslyn / LSP / IntelliSense.
- Debugger.
- Pełny system buildów (targety, konfiguracje, graf zależności).

## Filary v0.2

1. `IProcessService` (infrastruktura uruchamiania procesów)
2. Kompilator jako extension (g++ jako pierwszy)
3. Diagnostyka (structured) + minimalny UI (Problems)
4. Lekki capability system

## 1) IProcessService (fundament)

### Założenia

- Async i bez blokowania UI.
- Streaming stdout/stderr (linia po linii) w trakcie działania procesu.
- Cancellation (przerwanie kompilacji/run) i realne zatrzymanie procesu.
- Brak zależności od UI, Output, Workspace.

### Kontrakty (Abstractions)

```csharp
public interface IProcessService
{
    Task<ProcessResult> RunAsync(
        ProcessRequest request,
        IProcessOutputListener? listener = null,
        CancellationToken cancellationToken = default);
}

public sealed class ProcessRequest
{
    public string FileName { get; init; } = "";
    public string Arguments { get; init; } = "";
    public string? WorkingDirectory { get; init; }
    public IDictionary<string, string>? EnvironmentVariables { get; init; }
}

public sealed class ProcessResult
{
    public int ExitCode { get; init; }
    public string StdOut { get; init; } = "";
    public string StdErr { get; init; } = "";
    public bool WasCancelled { get; init; }
}

public interface IProcessOutputListener
{
    void OnStdOut(string line);
    void OnStdErr(string line);
}
```

### Notatki implementacyjne (Core)

- Implementacja opiera się o `System.Diagnostics.Process`.
- Preferowany mechanizm: `BeginOutputReadLine()` / `BeginErrorReadLine()` (brak `ReadToEnd()`), aby uniknąć deadlocków.
- Cancellation:
  - na `cancellationToken` kończymy proces (`Kill(entireProcessTree: true)` – o ile platforma wspiera),
  - wynik oznaczamy `WasCancelled = true`.

### DoD (IProcessService)

- Da się uruchomić proces i odbierać output w trakcie.
- Da się anulować proces i UI nie wisi.
- Jest test/integracyjny smoke-test uruchamiający bezpieczne polecenie (Windows: `cmd /c echo hello`, Linux/macOS: `bash -c "echo hello"`).

## 2) Kompilator jako extension: `Compiler.Gpp`

### Cel

Pierwszy extension, który pokazuje realną wartość: kompilacja projektu C++ z wykorzystaniem `IProcessService`.

### Zakres minimalny

- Rejestruje komendę budowania (np. `build.gpp`).
- Strumieniuje stdout/stderr do Output.
- Zwraca exit code i czytelny wynik.

### Rozszerzenia v0.2.x (po v0.2 minimal)

- `build.stop` (cancel) – Stop Build.
- `run.gpp` (Run) – uruchomienie zbudowanego artefaktu.

### DoD (Compiler.Gpp)

- Build działa na prostym projekcie (1+ plików `.cpp`).
- Stop Build zatrzymuje proces.
- Output pokazuje przebieg kompilacji oraz wynik (exit code).

## 3) Diagnostyka (structured) i Problems

### Cel

Oddzielić logi (Output) od diagnostyki (błędy/ostrzeżenia z lokacją). Diagnostyka musi być możliwa do prezentacji w panelu, a później do nawigacji po kodzie.

### Kontrakty (Abstractions)

```csharp
public enum DiagnosticSeverity
{
    Info,
    Warning,
    Error,
}

public sealed class Diagnostic
{
    public string File { get; init; } = "";
    public int Line { get; init; }
    public int Column { get; init; }
    public string Message { get; init; } = "";
    public DiagnosticSeverity Severity { get; init; }
}

public interface IDiagnosticService
{
    IReadOnlyList<Diagnostic> GetDiagnostics();
    void Clear();
    void Add(Diagnostic diagnostic);
    event Action? DiagnosticsChanged;
}
```

### Implementacja (Core)

- `DiagnosticService` trzyma listę w pamięci (v0.2).
- Dla spójności z „brak blokowania”: `DiagnosticsChanged` powinno być bezpieczne (snapshot, brak wyjątków wywracających hosta).

### Parser błędów g++

- Minimalny parser do modelu `Diagnostic` z linii typu:
  - `main.cpp:10:5: error: expected ';'`
- Parser może działać na stderr w czasie streamingu.

### UI: Problems jako extension

- `Problems` to osobny extension/panel.
- Panel bierze dane z `IDiagnosticService` i renderuje listę.
- Interakcja (nawigacja) idzie przez EventBus, nie przez bezpośredni kontakt z edytorem.

### Zdarzenie nawigacji (EventBus)

```csharp
public sealed class NavigateToLocationRequested
{
    public NavigateToLocationRequested(string file, int line, int column)
    {
        File = file;
        Line = line;
        Column = column;
    }

    public string File { get; }
    public int Line { get; }
    public int Column { get; }
}
```

### DoD (diagnostyka)

- Po buildzie problemy są w `IDiagnosticService` (nie tylko w Output).
- Problems panel pokazuje diagnostyki.
- Kliknięcie/aktywacja wpisu publikuje `NavigateToLocationRequested`.

## 4) Capability system (lekki)

### Cel

Umożliwić extensionom opcjonalne odnajdywanie usług w hostcie, bez twardych zależności między extension.

### Minimalny kontrakt (HostContext)

- Host może udostępnić:
  - `GetCapability<T>()` / `TryGetCapability<T>(out T)`
  - `RegisterCapability<T>(T instance)`

Wariant preferowany w v0.2: lookup po `Type` (proste i wystarczające). Nazewnictwo capability (`string Name`) może zostać dodane później.

### DoD (capabilities)

- Host rejestruje co najmniej: `IProcessService`, `IDiagnosticService`, (opcjonalnie) `IActiveDocumentService`, `IProjectService`.
- Brak capability nigdy nie wywraca extension: extension działa w trybie ograniczonym i raportuje to do Output.

## Usługi wspierające (v0.2.x)

### Aktywny dokument

Jeśli build ma być „IDE-like” (kompiluj to, nad czym pracuję), wprowadzamy serwis:

```csharp
public interface IActiveDocumentService
{
    string? CurrentFilePath { get; }
    event Action? Changed;
    void SetActive(string? filePath);
}
```

Editor ustawia aktywny plik, kompilator bierze go jako kontekst (albo builduje cały projekt – zależnie od decyzji w danym milestone).

### Minimalny model projektu

W v0.2 unikamy rozbudowanego systemu buildów. Dopuszczalny jest bardzo lekki plik konfiguracyjny (np. `project.json`) i serwis `IProjectService`.

## Roadmap v0.2 (milestones)

### 2.1 – Process Foundation

- Kontrakty + implementacja `IProcessService`.
- Cancellation + streaming.
- Smoke test.

### 2.2 – G++ Extension (Build)

- Komenda `build.gpp`.
- Output streaming.
- Minimalny przepływ: build → exit code.

### 2.3 – Stop Build + Run (opcjonalnie)

- `build.stop` (cancellation token).
- `run.gpp` (uruchom wynik).

### 2.4 – Diagnostic Model + Service

- Model `Diagnostic` + `IDiagnosticService`.
- Parser podstawowych błędów g++.

### 2.5 – Problems (panel) + nawigacja

- Problems extension/panel.
- `NavigateToLocationRequested`.

## Kryteria akceptacji Etapu 2

- Działa pełny przepływ: otwórz folder → edytuj `.cpp` → Build → zobacz wynik i problemy → przejdź do błędu.
- System jest odporny na brak extension (kompilatora, problems, itp.).
- Rdzeń pozostaje infrastrukturą: brak wiedzy o konkretnych extension i brak logiki domenowej w Shell.

---

Powiązane dokumenty: [03_Model_Rozszerzen.md](../rozdzialy/03_Model_Rozszerzen.md), [06_System_Komend_i_Terminal.md](../rozdzialy/06_System_Komend_i_Terminal.md), [07_Kompilator_jako_Rozszerzenie.md](../rozdzialy/07_Kompilator_jako_Rozszerzenie.md), [09_Output_Diagnostics_i_Kanaly.md](../rozdzialy/09_Output_Diagnostics_i_Kanaly.md), [00_Plan_Implementacji_i_Milestones.md](00_Plan_Implementacji_i_Milestones.md).

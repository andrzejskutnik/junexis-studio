# Plan – Etap 1 (v0.1–v0.1.5)

Ten dokument konsoliduje notatki „etap 1” do postaci spójnej specyfikacji wdrożeniowej. Zakres obejmuje:

- v0.1: minimalne, modularne IDE (Host + Core + Shell + 3 extensions)
- v0.1.5: „utwardzenie” (testy Core, ViewRegistry, ExtensionLoader, loader folderowy bez ALC)

## 1. Cel etapu

Zbudować stabilny, minimalny „kernel IDE” z wyraźnymi granicami warstw:

- Core: usługi infrastrukturalne i rejestry (bez UI)
- Host: lifecycle + inicjalizacja + izolacja błędów
- Shell: UI (Avalonia) + binding do rejestrów
- Extensions: funkcje IDE (Explorer/Editor/Output)

Efekt końcowy v0.1: uruchamiasz aplikację, otwierasz folder, otwierasz plik, edytujesz, zapisujesz; wszystko loguje się w Output.

## 2. Słownik i zasady

### 2.1. Definicje

- Host: orkiestrator uruchomienia i zamknięcia, tworzy Core Services, inicjalizuje rozszerzenia.
- Core Services: EventBus, CommandRegistry, PanelRegistry, OutputService, WorkspaceService oraz (docelowo) CapabilityRegistry.
- HostContext: readonly „wiązka” referencji do usług i rejestrów, przekazywana do extensions.
- Shell: warstwa UI (MainWindow), która pobiera rejestracje i renderuje.
- Extension: moduł implementujący `IJunexisExtension`, rejestruje panele/komendy i podpina się pod eventy.

### 2.2. Zasady architektoniczne (etap 1)

1) Host startuje przed UI bindingiem.
2) Extensions rejestrują (panele/komendy/mapowanie view), ale nie renderują.
3) Core nie referencjonuje Avalonia.
4) Brak kontenera DI w v0.1: jawne tworzenie obiektów.
5) Błąd w jednym extension/handlerze nie przerywa działania systemu: wyjątki są przechwytywane i logowane do Output.
6) UI nie zna konkretnych extension: UI używa rejestrów.

Uwaga: dokumenty wysokiego poziomu zakładają `CapabilityRegistry` jako część Core Services.
W etapie 1 wystarczy zostawić na to miejsce w `IHostContext` (nawet jeśli implementacja będzie dopiero w v0.2).

## 3. Sekwencja startu aplikacji (v0.1)

### 3.1. Fazy

1) Bootstrap (Main)
2) Host Initialization
3) Extension Activation
4) UI Binding

### 3.2. Diagram sekwencji (logiczny)

```
Main()
  -> create Shell/App
  -> Host.Start()
       -> Create Core Services
       -> Create HostContext
       -> Load + Initialize Extensions
  -> Shell binds UI to registries
  -> Application Ready
```

## 4. Host i HostContext

### 4.1. Odpowiedzialności Host

Host:

- tworzy Core Services
- tworzy HostContext
- ładuje i inicjalizuje extensions (przez loader)
- izoluje błędy (try/catch i logowanie)
- zapewnia `Shutdown()` w reverse order

Host nie:

- zna UI
- renderuje paneli
- wykonuje logiki domenowej extensions

### 4.2. Publiczne API Host

Minimalnie:

```
void Start()
void Shutdown()
IHostContext Context { get; }
```

### 4.3. Start() — kolejność

```
if (_started) return;

1. CreateCoreServices()
2. CreateHostContext()
3. LoadExtensions()           // przez IExtensionLoader
4. InitializeExtensions()
5. _started = true
```

### 4.4. Shutdown() — reverse order

- Zamyka extensions w odwrotnej kolejności inicjalizacji.
- Każdy błąd w `Shutdown()` jest logowany, nie przerywa pozostałych.

### 4.5. HostContext

HostContext:

- tylko readonly properties
- brak setterów
- nie umożliwia modyfikacji infrastruktury po starcie

## 5. Core Services (v0.1)

### 5.1. EventBus

#### Cel

- publish/subscribe
- brak zależności między extensions
- synchronicznie (jedna nitka)
- brak dziedziczenia w dispatchu (obsługa po konkretnym typie)

#### Kontrakt

```
void Publish(object evt)
void Subscribe<T>(Action<T> handler)
void Unsubscribe<T>(Action<T> handler)
```

#### Zasady

- Publish iteruje po kopii listy handlerów (snapshot), aby nie wywrócić się przy modyfikacjach w trakcie.
- EventBus nie zna Output bezpośrednio.
- Obsługa wyjątków przez callback:

```
Action<Exception>? OnHandlerException
```

Host podpina `OnHandlerException` do `Output.Write(...)`.

#### Decyzja: re-entrancy

W v0.1 dopuszczalne: handler może w środku opublikować kolejne zdarzenie.

### 5.2. PanelRegistry

#### Cel

- Extensions rejestrują panele (Tool/Document)
- Shell renderuje
- Core nie zna UI

#### Kontrakt

```
void RegisterToolPanel(string id, Func<object> factory)
void RegisterDocumentPanel(string id, Func<object> factory)
IEnumerable<PanelRegistration> GetRegisteredPanels()
```

#### Model

- `PanelRegistration`: `Id`, `Type`, `Factory`
- `Factory` zwraca `object` w v0.1 (minimalizm)
- `GetRegisteredPanels()` zwraca snapshot

### 5.3. CommandRegistry

#### Cel

- Extensions rejestrują komendy
- Shell je wykonuje
- brak zależności od UI
- obsługa `CanExecute` (opcjonalna)

#### Minimalny kontrakt v0.1

W wersji rozszerzonej o metadane menu (potrzebne dla dynamicznego budowania menu):

```
void Register(string id, string displayName, string? menuPath, Action execute, Func<bool>? canExecute = null)
bool CanExecute(string id)
void Execute(string id)
IEnumerable<CommandDefinition> GetAll()
```

#### Izolacja błędów

`CommandRegistry` ma callback:

```
Action<Exception>? OnCommandException
```

Host podpina go do Output.

### 5.4. OutputService

#### Cel

- ring buffer (limit historii)
- snapshot (dla panelu pojawiającego się po starcie)
- event realtime

#### Kontrakt

```
void Write(string channel, string message)
IEnumerable<OutputEntry> GetSnapshot()
event Action<OutputEntry> EntryWritten
```

#### Zasady

- `EntryWritten` emitowany poza lockiem
- kanały umowne: `Core`, `Extensions`, `Workspace`, `UI`

### 5.5. WorkspaceService

#### Cel

- RootPath
- open folder
- read/write
- podstawowe eventy domenowe

#### Kontrakt

```
string? RootPath { get; }
void OpenFolder(string path)
string ReadFile(string path)
void WriteFile(string path, string content)

event Action<string>? FolderOpened
event Action<string>? FileOpened
event Action<string>? FileSaved
```

#### Zasady

- Workspace nie publikuje przez EventBus w v0.1 (nie mieszamy poziomów)
- `ResolvePath` traktuje ścieżki względne względem RootPath
- docelowo (dług techniczny): dodać ochronę przed wyjściem poza RootPath

### 5.6. CapabilityRegistry (miejsce w etapie 1)

JunexisStudio preferuje „capability over dependency”, więc rejestr capabilities jest docelowym mechanizmem wykrywania usług opcjonalnych.

W v0.1/v0.1.5 wystarczy:

- dodać w `IHostContext` właściwość typu `ICapabilityRegistry` (może być implementacją minimalną lub stubem),
- nie opierać na tym krytycznych ścieżek (Explorer/Editor/Output powinny działać bez capabilities).

## 6. Shell (UI) — v0.1

### 6.1. Layout

Minimalny układ (Grid 2x2):

- lewa kolumna: Explorer
- prawa kolumna: Editor
- dolny wiersz: Output

### 6.2. Binding paneli

1) Shell pyta `PanelRegistry.GetRegisteredPanels()`
2) Dla każdej rejestracji tworzy ViewModel: `vm = Factory()`
3) Shell renderuje View na podstawie mapowania ViewModel → View (patrz v0.1.5: ViewRegistry)

W v0.1 dopuszczalne jest twarde mapowanie w `CreateView(...)` (dług techniczny), ale w v0.1.5 zastępujemy to ViewRegistry.

### 6.3. Menu i komendy

- Minimalnie: `Open Folder`, `Save`
- Docelowo w etapie 1: dynamiczne budowanie menu na podstawie `CommandRegistry.GetAll()` i `MenuPath`

## 7. Extensions (v0.1)

### 7.1. ExplorerExtension

#### Cel

- rejestruje ToolPanel `Explorer`
- reaguje na `Workspace.FolderOpened`
- buduje proste drzewo plików
- publikuje `FileOpenRequested` przez EventBus

#### Zasady

- Explorer nie czyta i nie zapisuje plików
- Explorer nie zna Editor

#### Minimalny przepływ

1) `Workspace.OpenFolder()` → `FolderOpened`
2) `ExplorerViewModel` buduje drzewo
3) UI podwójnym kliknięciem wywołuje `OpenFile(path)`
4) `EventBus.Publish(new FileOpenRequested(path))`

### 7.2. EditorExtension

#### Cel

- rejestruje DocumentPanel `Editor`
- subskrybuje `FileOpenRequested`
- czyta plik przez Workspace
- zapisuje plik przez Workspace

#### Komenda Save

- `editor.save` wywołuje `EditorViewModel.Save()`
- w v0.1 dopuszczalne jest, że extension trzyma referencję do swojego singletonowego VM (jeden dokument)

#### Dirty flag (minimalnie)

- `IsDirty` ustawiane przy zmianie `Content`
- `DisplayName` pokazuje `*` gdy brudny
- `IsDirty = false` po otwarciu i po zapisie

### 7.3. OutputExtension

#### Cel

- rejestruje ToolPanel `Output`
- buduje `ObservableCollection<OutputEntry>` z `GetSnapshot()` i `EntryWritten`
- brak filtrów i kolorów w v0.1

## 8. Utwardzenie v0.1.5

### 8.1. Testy jednostkowe Core

Minimalny zakres testów (zachowanie, nie implementacja):

- EventBus: subscribe/publish, unsubscribe, brak subskrybentów, wyjątek w handlerze (callback), re-entrancy, snapshot safety
- CommandRegistry: register/execute, canExecute false, unknown command nie rzuca, wyjątek w execute (callback)
- OutputService: EntryWritten, snapshot, limit
- WorkspaceService: OpenFolder ustawia RootPath, read/write działają, read bez RootPath rzuca

### 8.2. ViewRegistry (usunięcie sprzężenia Shell ↔ Extensions)

Cel: Shell nie zna typów `ExplorerViewModel`, `EditorViewModel`, etc.

Kontrakt:

```
void Register<TViewModel, TView>()
Control Resolve(object viewModel)
```

Wariant prosty: implementacja `ViewRegistry` w Shell, a interfejs w Abstractions.

Uwaga architektoniczna: jeśli interfejs używa `Control`, Abstractions staje się „UI-aware”. Jest to akceptowalne w etapie 1, ale warto rozważyć w przyszłości wydzielenie osobnego modułu `*.UI.Abstractions`.

### 8.3. ExtensionLoader (refaktor Host)

Cel: Host nie zna konkretnych extension.

Kontrakt:

```
IEnumerable<IJunexisExtension> LoadExtensions()
```

W v0.1: `StaticExtensionLoader` zwraca 3 wbudowane extension.

### 8.4. FolderExtensionLoader (dynamiczne DLL bez AssemblyLoadContext)

Założenia minimalne:

- skan `./extensions/**` po `*.dll`
- `Assembly.LoadFrom` + `GetTypes`
- filtr: `IJunexisExtension` + nie-abstract
- obsługa `ReflectionTypeLoadException` i logowanie `LoaderExceptions`
- błędy nie wywalają IDE (log do Output)

Ryzyko świadomie akceptowane w v0.1.5:

- brak izolacji zależności i konfliktów wersji (to rozwiązuje dopiero `AssemblyLoadContext`)

## 9. Dług techniczny i ryzyka (świadomie akceptowane)

1) Twarde mapowanie ViewModel → View (zanim wejdzie ViewRegistry).
2) Dialogi synchroniczne (`.Result`) w komendach UI.
3) Brak ochrony przed wyjściem poza RootPath w `ResolvePath`.
4) Dynamiczny loader bez ALC (konflikty zależności możliwe).

## 10. Definition of Done — Etap 1

Etap 1 uznajemy za ukończony, gdy:

- Host ma `Start()` i `Shutdown()` i jest wywoływany na Exit aplikacji.
- Core Services działają i są dostępne w HostContext.
- Explorer/Editor/Output działają jako extensions rejestrujące panele.
- UI renderuje panele z PanelRegistry.
- Menu wywołuje komendy przez CommandRegistry (ręcznie lub dynamicznie).
- Przepływ użytkownika działa: Open Folder → drzewo → Open File → edycja → Save.
- Output pokazuje logi startu i operacji.
- (v0.1.5) Testy Core przechodzą.
- (v0.1.5) Shell nie zna typów extensions (ViewRegistry).
- (v0.1.5) Host ładuje extensions przez IExtensionLoader.

## 11. Następny krok (po etapie 1)

Naturalny start v0.2:

- `IProcessService` + pierwszy kompilator (np. `g++`)
- albo izolacja extensions przez `AssemblyLoadContext`
- albo system capabilities dla extensions

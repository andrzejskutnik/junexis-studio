# 16 – Minimalne Interfejsy i Pierwszy Kod

---

# 1. Cel

Celem tego dokumentu jest zdefiniowanie absolutnie minimalnego zestawu interfejsów i klas potrzebnych do uruchomienia v0.1.

Bez nadmiaru abstrakcji.
Bez "na przyszłość".
Tylko to, co potrzebne.

---

# 2. Minimalne Abstractions

Delikatne doprecyzowanie interfejsów – nadal minimalnie, ale bez późniejszych "bolesnych refaktorów".

---

## 2.1. IJunexisExtension

* string Id
* void Initialize(IHostContext context)
* void Shutdown()

Dodatkowa zasada:

* Initialize nie może rzucać nieobsłużonych wyjątków.

---

## 2.2. IHostContext

* IEventBus EventBus
* ICommandRegistry Commands
* IPanelRegistry Panels
* IOutput Output
* IWorkspace Workspace

Nie dodajemy jeszcze CapabilityRegistry, ale zostawiamy miejsce w architekturze.

---

## 2.3. IEventBus

* void Publish(object evt)
* void Subscribe<T>(Action<T> handler)
* void Unsubscribe<T>(Action<T> handler)

Unsubscribe dodajemy teraz, żeby uniknąć wycieków w przyszłości.

---

## 2.4. ICommandRegistry

* void Register(string id, Action execute)
* bool CanExecute(string id)
* void Execute(string id)

CanExecute dodajemy minimalnie – bez kontekstu. Kontekst pojawi się w v0.2.

---

## 2.5. IPanelRegistry

* void RegisterToolPanel(string id, Func<object> factory)
* void RegisterDocumentPanel(string id, Func<object> factory)
* IEnumerable<string> GetRegisteredPanels()

To pozwoli Shellowi dynamicznie budować UI.

---

## 2.6. IOutput

* void Write(string channel, string message)

Dodajemy prostą strukturę wpisu (model wewnętrzny Core):

* string Channel
* string Message
* DateTime Timestamp

Severity nadal odkładamy do v0.2.

---

## 2.7. IWorkspace

* string RootPath
* void OpenFolder(string path)
* string ReadFile(string path)
* void WriteFile(string path, string content)

Dodajemy zdarzenie domenowe:

* event Action<string> FileOpened

Nie przez EventBus – lokalnie w Workspace.

---

# 3. Minimalne Implementacje (Core)

W projekcie Junexis.Studio.Core implementujemy:

* EventBus (Dictionary<Type, List<Delegate>>)
* CommandRegistry (Dictionary<string, Action>)
* OutputService (lista wpisów + zdarzenie OnWrite)
* WorkspaceService (System.IO + event FileOpened)

Nie tworzymy jeszcze:

* DI kontenera
* CapabilityRegistry
* Zaawansowanego routingu zdarzeń

Prostota > elegancja w tym etapie.

---

# 4. Minimalny Host Minimalne Implementacje (Core)

W projekcie Junexis.Studio.Core implementujemy:

* EventBus (lista handlerów per typ)
* CommandRegistry (Dictionary<string, Action>)
* OutputService (lista wpisów + event)
* WorkspaceService (proste operacje na System.IO)

Nie tworzymy osobnych warstw DI.

---

# 4. Minimalny Host

Host powinien:

1. Utworzyć instancje:

   * EventBus
   * CommandRegistry
   * OutputService
   * WorkspaceService

2. Utworzyć HostContext.

3. Ręcznie zarejestrować rozszerzenia (na start statycznie):

   * ExplorerExtension
   * EditorExtension
   * OutputExtension

4. Wywołać Initialize.

5. Przechwycić wyjątki.

Bez dynamicznego ładowania na tym etapie.

---

# 5. Pierwsze Rozszerzenie Testowe – HelloExtension

Cel:

* sprawdzić czy Host ładuje extension,
* sprawdzić czy Output działa,
* sprawdzić czy EventBus działa.

Zachowanie:

* w Initialize → Output.Write("Core", "Hello Extension Loaded")
* subskrypcja przykładowego eventu

To jest pierwszy test integracyjny.

---

# 6. Minimalny Przepływ v0.1

1. Shell startuje.
2. Tworzy Host.
3. Host ładuje rozszerzenia.
4. Explorer rejestruje panel.
5. Editor rejestruje panel.
6. Output rejestruje panel.
7. Workspace.OpenFolder wywołane z menu.
8. Explorer odświeża widok.

Jeśli to działa → mamy fundament.

---

# 7. Czego Pilnować

* nie wprowadzać CapabilityRegistry teraz,
* nie robić generycznych wrapperów,
* nie robić plugin marketplace,
* nie implementować DI kontenera.

Prosto. Stabilnie. Czytelnie.

---

# 8. Po Zakończeniu v0.1

Dopiero wtedy:

* dodajemy CapabilityRegistry,
* dodajemy IProcessService,
* wprowadzamy diagnostykę z severity,
* wprowadzamy dynamiczne ładowanie DLL.

Najpierw musi działać prosty system.

---

To jest moment, w którym zaczynamy pisać kod.
Bez nadmiaru.
Bez filozofii.
Z dyscypliną.

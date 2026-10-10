# 03 – Model Rozszerzeń

---

# 1. Cel Modelu Rozszerzeń

Model rozszerzeń jest sercem JunexisStudio.

Ma zapewnić:

* brak twardych zależności między modułami,
* izolację błędów,
* możliwość dynamicznego ładowania,
* rozszerzalność przez lata,
* łagodne degradowanie systemu.

Każda funkcjonalność wyższego poziomu powinna być realizowana jako rozszerzenie.

---

# 2. Kontrakt Rozszerzenia

Minimalny kontrakt:

* identyfikacja rozszerzenia,
* inicjalizacja,
* zamykanie.

Proponowany interfejs:

* Name / Id
* Version
* Initialize(IHostContext)
* Shutdown()

Rozszerzenie nie może wymagać żadnego innego rozszerzenia.

---

# 3. Kontekst Hosta (IHostContext)

IHostContext jest jedynym wejściem rozszerzenia do systemu.

Udostępnia:

* Core Services (gwarantowane)
* Rejestry (commands, panels, capabilities)
* EventBus
* Output
* Workspace
* Konfigurację

Rozszerzenie nie otrzymuje bezpośrednio referencji do innych rozszerzeń.

---

# 4. Core Services – gwarantowany zestaw

Każde rozszerzenie może zakładać istnienie:

* IEventBus
* ICommandRegistry
* IPanelRegistry
* ICapabilityRegistry
* IOutput
* IWorkspace
* IFileSystem
* IConfiguration

To jest minimalny „system operacyjny” dla rozszerzeń.

---

# 5. Capabilities

## 5.1. Definicja

Capability to formalnie zarejestrowana możliwość, np.:

* ICompilerService
* IMcpConnector
* IOutlineProvider
* IDocumentRenderer
* ITerminalProvider

## 5.2. Zasady

1. Capability są opcjonalne.
2. Brak capability nie jest błędem.
3. Rozszerzenie może działać w trybie ograniczonym.
4. Rozszerzenie może publikować wiele capabilities.

## 5.3. Wyszukiwanie

Wyszukiwanie odbywa się przez:

* TryGet<T>()
* GetAll<T>()

Ważne: rozszerzenie nie może „wymagać” capability.

---

# 6. Komunikacja

System wykorzystuje dwa komplementarne mechanizmy:

1. EventBus – komunikacja asynchroniczna, luźne powiązanie.
2. CapabilityRegistry – dostęp do usług opcjonalnych.

Zasada:

* EventBus do sygnałów i zdarzeń.
* Capabilities do usług i API.

---

# 7. EventBus – kontrakt

EventBus powinien umożliwiać:

* Publish(event)
* Subscribe<TEvent>(handler)
* Unsubscribe

Zdarzenia powinny być:

* niemutowalne,
* wersjonowalne,
* z minimalnym payloadem,
* niezależne od UI.

---

# 8. Komendy (Command Registry)

Rozszerzenia rejestrują komendy:

* globalne (menu / skróty)
* kontekstowe (np. explorer)

Komenda:

* Id
* Name
* Execute
* CanExecute

Komendy mogą być wywoływane przez UI lub inne rozszerzenia.

---

# 9. Panele (Panel Registry)

Rozszerzenia rejestrują panele.

Panel może być:

* narzędziowy (tool window)
* dokumentowy (editor tab)
* tymczasowy (np. quick view)

Rejestracja powinna obejmować:

* PanelId
* Title
* DockGroup
* Factory (View / ViewModel)

---

# 10. Dynamiczne ładowanie rozszerzeń

## 10.1. Źródło

Rozszerzenia są ładowane z folderu:

* ./extensions/

## 10.2. Izolacja

Docelowo rozszerzenia powinny być ładowane w izolacji (AssemblyLoadContext).

Cel izolacji:

* możliwość aktualizacji,
* brak konfliktów wersji,
* bezpieczeństwo.

## 10.3. Manifest

Każde rozszerzenie powinno posiadać manifest opisujący:

* Id
* Version
* Entry type
* Capabilities exposed
* Minimal IDE version

Manifest jest wykorzystywany na etapie Discover.

---

# 11. Odporność na błędy

Błąd rozszerzenia:

* nie zatrzymuje hosta,
* jest logowany do Output,
* może dezaktywować rozszerzenie,
* nie może blokować pracy użytkownika.

---

Kolejny dokument:

04_Shell_i_System_Paneli.md

W nim zostanie opisany system dockowania, layout oraz sposób integracji UI z rejestrem paneli.

# 02 – Architektura Wysokiego Poziomu

---

# 1. Podział Warstw Systemu

Architektura JunexisStudio opiera się na wyraźnym rozdzieleniu odpowiedzialności.

System składa się z pięciu głównych warstw:

1. UI Shell (Avalonia)
2. Host Infrastructure
3. Core Services
4. Extension Layer
5. External Systems

---

# 2. Schemat Logiczny

UI Shell
↓
Host Infrastructure
↓
Core Services
↓
Extension Layer
↓
External Systems (Compiler, MCP, Runtime, OMWiA)

Każda warstwa zna wyłącznie warstwę bezpośrednio poniżej.
Rozszerzenia nie znają siebie nawzajem.

---

# 3. UI Shell (Avalonia)

UI Shell odpowiada wyłącznie za:

* główne okno aplikacji,
* system dockowania paneli,
* pasek menu,
* rejestrację komend wizualnych,
* zarządzanie layoutem.

Shell nie zawiera logiki domenowej.
Nie zna kompilatora, MCP ani OMWiA.

---

# 4. Host Infrastructure

Warstwa hosta odpowiada za:

* ładowanie rozszerzeń,
* zarządzanie cyklem życia rozszerzeń,
* udostępnienie kontekstu hosta (IHostContext),
* kontrolę startu i zamykania systemu.

Host nie wykonuje logiki biznesowej.

---

# 5. Core Services

Core Services to minimalny zestaw usług dostępnych dla wszystkich rozszerzeń:

* EventBus
* CommandRegistry
* PanelRegistry
* CapabilityRegistry
* Output / Logging
* WorkspaceManager
* FileSystem abstraction

Te usługi są jedynymi gwarantowanymi elementami systemu.

---

# 6. Extension Layer

Rozszerzenia:

* implementują IJunexisExtension,
* rejestrują swoje komendy,
* rejestrują panele,
* publikują capabilities,
* reagują na zdarzenia.

Rozszerzenia nie mają twardych zależności.
Brak rozszerzenia nie blokuje systemu.

---

# 7. External Systems

Warstwa zewnętrzna obejmuje:

* kompilatory (g++, Roslyn, inne),
* systemy MCP,
* runtime JunexisLang,
* narzędzia analityczne,
* przyszłe moduły eksperymentalne.

Są one dostępne wyłącznie poprzez rozszerzenia.

---

# 8. Przepływ Inicjalizacji

1. Shell uruchamia Host Infrastructure.
2. Host ładuje Core Services.
3. Host skanuje folder rozszerzeń.
4. Każde rozszerzenie jest inicjalizowane z IHostContext.
5. Rozszerzenia rejestrują swoje zasoby.
6. System przechodzi w stan gotowości.

Błąd pojedynczego rozszerzenia nie przerywa startu całego systemu.

---

# 9. Cykl Życia Rozszerzenia

Rozszerzenie przechodzi przez etapy:

1. Discover
2. Load
3. Initialize
4. Active
5. Shutdown

Każdy etap musi być bezpieczny i izolowany.

---

Kolejny dokument:

03_Model_Rozszerzen.md

W nim zostanie formalnie zaprojektowany interfejs IJunexisExtension oraz mechanizm capability i komunikacji.

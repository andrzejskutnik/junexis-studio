# 11 – OMWiA jako Moduł Systemowy

---

# 1. Cel

OMWiA (Osobisty Model Wiedzy i Asysty) w JunexisStudio jest rozszerzeniem.

Nie jest częścią rdzenia.
Nie ma uprzywilejowanego dostępu do systemu.
Nie posiada ukrytych zależności.

OMWiA jest konsumentem zdarzeń oraz opcjonalnym dostawcą capability.

---

# 2. Rola OMWiA w Systemie

OMWiA może pełnić funkcje:

* obserwatora zdarzeń systemowych,
* analizatora kontekstu workspace,
* konsumenta danych z MCP,
* generatora sugestii,
* rejestratora narracji operacyjnej.

OMWiA nie steruje rdzeniem.
OMWiA nie modyfikuje infrastruktury.

---

# 3. Dostęp do Kontekstu

OMWiA uzyskuje kontekst poprzez:

* subskrypcję EventBus,
* dostęp do IWorkspace,
* opcjonalne capability (np. ICompilerService, IMcpConnector),
* dostęp do Output jako źródła sygnałów.

OMWiA nie powinien bezpośrednio modyfikować dokumentów bez wyraźnej akcji użytkownika.

---

# 4. Tryb Headless i Tryb Panelowy

OMWiA może działać w dwóch trybach:

## 4.1. Headless

* reaguje na zdarzenia,
* zapisuje narrację,
* publikuje sugestie jako zdarzenia,
* nie posiada UI.

## 4.2. Panelowy

* posiada panel narzędziowy,
* umożliwia interakcję użytkownika,
* może prezentować analizę kontekstu,
* może proponować akcje.

Panel jest opcjonalny.

---

# 5. Sugestie i Akcje

OMWiA może publikować zdarzenia typu:

* SuggestionCreated
* ContextInsightDetected
* RecommendedCommand

OMWiA nie wykonuje komend bez zgody użytkownika.

Akcja musi przejść przez CommandRegistry.

---

# 6. Integracja z MCP

OMWiA może:

* konsumować dane kontekstowe z MCP,
* wysyłać zapytania przez IMcpConnector,
* analizować wyniki.

Brak MCP nie blokuje OMWiA.

---

# 7. Granice Wpływu

OMWiA nie może:

* przejmować kontroli nad hostem,
* omijać CommandRegistry,
* bezpośrednio ingerować w inne rozszerzenia,
* wymuszać obecności innych capability.

OMWiA jest jednym z rozszerzeń.

---

# 8. Minimalny zakres v0.1

Pierwsza wersja powinna:

* subskrybować zdarzenia workspace,
* subskrybować zdarzenia build,
* publikować proste sugestie,
* posiadać prosty panel tekstowy.

Bez:

* autonomicznych akcji,
* globalnej orkiestracji,
* ingerencji w architekturę systemu.

---

Kolejny dokument:

12_Bezpieczenstwo_i_Izolacja.md

W nim zostaną opisane zasady izolacji rozszerzeń, ładowania w kontekście AssemblyLoadContext oraz granice zaufania.

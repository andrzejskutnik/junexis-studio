# Kanwa dokumentacji JunexisStudio

Ten plik jest „kanwą” i punktem startowym do czytania/pisania dokumentacji JunexisStudio. Ma porządkować **cel**, **zasady** i **strukturę rozdziałów**, a nie być zapisem dialogu.

## Cel

- Zdefiniować zakres JunexisStudio jako środowiska do pracy z JunexisLang (i ekosystemem: kompilator, runtime, MCP, OMWiA), ale budowanego **extension-first**.
- Utrzymać spójność między „wizją” a **minimalnym zakresem v0.1** i kolejnymi milestone’ami.
- Ułatwić implementację: najpierw opis i interfejsy, potem kod.

## Zasady redakcyjne (kanwa)

- Każdy rozdział = osobny plik w `docs/`.
- Jeśli rozdział rośnie ponad ~2–3 ekrany: dzielimy na mniejsze podrozdziały (osobne pliki), zamiast robić jeden „gigantyczny” dokument.
- W każdym rozdziale:
  - „**Po co**” (cel i granice),
  - „**Kontrakty**” (interfejsy / zdarzenia / registry),
  - „**Scenariusze**” (happy path + brakujące extension),
  - „**Wersjonowanie**” (co jest v0.1, co docelowo v0.2+).

## Kolejność / mapa dokumentów

- Wstęp + zasady: [00_wstęp_i_Zasady.md](../rozdzialy/00_wstęp_i_Zasady.md)
- Wizja: [01_Wizja_systemu.md](../rozdzialy/01_Wizja_systemu.md)
- Architektura high-level: [02_Architektura_Wysokiego_Poziomu.md](../rozdzialy/02_Architektura_Wysokiego_Poziomu.md)
- Model rozszerzeń: [03_Model_Rozszerzen.md](../rozdzialy/03_Model_Rozszerzen.md)
- Shell i panele: [04_Shell_i_System_Paneli.md](../rozdzialy/04_Shell_i_System_Paneli.md)
- Workspace i pliki: [05_Workspace_i_System_Plikow.md](../rozdzialy/05_Workspace_i_System_Plikow.md)
- Komendy i terminal: [06_System_Komend_i_Terminal.md](../rozdzialy/06_System_Komend_i_Terminal.md)
- Output/diagnostics: [09_Output_Diagnostics_i_Kanaly.md](../rozdzialy/09_Output_Diagnostics_i_Kanaly.md)
- Plan + milestones: [00_Plan_Implementacji_i_Milestones.md](../plany/00_Plan_Implementacji_i_Milestones.md)
- Plan Etapu 1 (v0.1–v0.1.5): [01_Plan_Etap_1_v0_1.md](../plany/01_Plan_Etap_1_v0_1.md)
- Plan Etapu 2 (v0.2): [02_Plan_Etap_2_v0_2.md](../plany/02_Plan_Etap_2_v0_2.md)

## Decyzje architektoniczne (w pigułce)

### UI

- Shell w Avalonia (MVVM), ale Shell to tylko „powłoka” (host UI), bez logiki domenowej.
- Edytor: podejście pragmatyczne (np. AvaloniaEdit) – bez IntelliSense na start.

### Extensions

- Brak twardych zależności między extension.
- Brakujące extension **nie może** blokować startu ani pracy („graceful degradation”).
- Komunikacja: EventBus + opcjonalny lookup capability (bez zależności konstruktorowych).

### Rdzeń (Core Services)

- Minimalne, stabilne usługi hosta: EventBus, Output, CommandRegistry, PanelRegistry, Workspace/FileSystem (zakres minimalny), konfiguracja.
- Reszta jako rozszerzenia (kompilator, MCP, OMWiA, grafy, runtime itd.).

## Zakres funkcji: „chcę na start” vs milestones

Poniżej rozdzielamy: **co docelowo** ma istnieć, a co jest **niezbędne** w v0.1.

### Docelowy zestaw (kierunek)

- Explorer plików
- Edycja i podgląd zawartości pliku
- System extensions + loader
- System MCP (connectory jako extension)
- Kompilator jako extension (JunexisLang: g++, C#: Roslyn)
- Panel OMWiA
- Output
- Terminal
- Konspekt/outline

### v0.1 (MVP – minimalny „szkielet”)

- Shell + podstawowe panele: Explorer, Editor, Output
- Minimalne Core Services i kontrakty rozszerzeń
- Extension loader (w wersji minimalnej) + 1–2 przykładowe extension techniczne

Uwaga: Terminal i outline mogą wejść jako **opcjonalne** w v0.1, jeśli nie rozbiją stabilności i nie skomplikują interfejsów (w przeciwnym razie v0.2+).

### v0.1.5 (utwardzenie)

- Testy jednostkowe Core
- Rozdzielenie Shell ↔ extension (np. przez ViewRegistry)
- Uporządkowanie loadera (folder extensions/) bez zaawansowanych mechanizmów izolacji

### v0.2+ (rozbudowa)

- Terminal (jeśli nie zrobiony w v0.1) + lepsza integracja z komendami
- Outline (jeśli nie zrobiony w v0.1) + pierwsze źródło danych (parser/AST/prosty analizator)
- Kompilator jako extension + parser diagnostyki
- MCP jako extension
- OMWiA jako extension (lub „moduł systemowy” – zależnie od dalszych decyzji)

## Otwarte pytania (do uzupełnienia przy kolejnych rozdziałach)

- Minimalny kontrakt `IFileSystem`/Workspace w v0.1: co jest „must-have”, co dopiero w v0.2?
- Czy OMWiA jest *zawsze* dostępny jako moduł systemowy, czy 100% jak inne extension?
- Jak wersjonujemy kontrakty extension (semver, feature flags, capability names)?
- Jak wygląda „layout persistence” paneli: od v0.1 czy v0.2?

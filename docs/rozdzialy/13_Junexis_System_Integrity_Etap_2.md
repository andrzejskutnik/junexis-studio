# 13. Junexis System Integrity – Etap 2

## Cel

Zdefiniować zasady integralności systemu Junexis w kontekście budowy systemów rozproszonych.

Etap 2 jest etapem przejścia od „języka i kompilatora” do spójnego systemu budowy architektury rozproszonej.

Integralność w Junexis nie oznacza jedynie poprawności składniowej ani typowej poprawności kompilacji. Oznacza:

- Spójność intencji
- Spójność architektury
- Spójność wykonania
- Zgodność implementacji z deklarowanym sensem systemu

## Warstwy systemu

System Junexis składa się z trzech odrębnych, lecz powiązanych warstw.

### Warstwa infrastrukturalna

- Workspace
- Moduły
- Języki implementacyjne
- Partitions (generated/manual/external)
- Toolchainy
- Pipeline wykonania

Warstwa ta odpowiada za budowę artefaktów.

### Warstwa aktorów

- Task jako aktor
- Kanały komunikacyjne
- Platformy (@embedded, @server, @cloud)
- Pipeline jako plan współpracy

Warstwa ta odpowiada za strukturę wykonawczą systemu.

### Warstwa semantyczna

- Narrative
- Intent
- Scope
- Trace policy

Warstwa ta odpowiada za sens istnienia systemu.

Narrative nie steruje wykonaniem. Narrative definiuje znaczenie.

## Zasada nadrzędności narracji

Narracja jest źródłem prawdy systemu.

Jej brak oznacza brak zdefiniowanej intencji.

Zasada fundamentalna:

> Każdy pipeline musi być objęty co najmniej jedną narracją.

Pipeline bez narracji jest systemowo niekompletny.

## Poziomy budowy systemu

Integralność narracyjna zależy od poziomu budowy.

### Development Mode

- Brak narracji → Ostrzeżenie
- System może zostać zbudowany
- IDE wskazuje brakujące elementy
- AI może sugerować uzupełnienie narracji

### Release Mode

- Brak narracji → Błąd semantyczny
- Build systemu zostaje zatrzymany
- Artefakty nie są uznane za finalne

Poziom rygoru jest globalny dla całego Workspace.

## Narrative Integrity Rule

Reguła integralności narracyjnej:

1. Każdy pipeline musi być objęty narracją.
2. Narracja musi wskazywać istniejące elementy systemu.
3. W trybie Release brak pokrycia skutkuje błędem.

Błąd ten nie jest błędem składniowym ani typowym.

Jest to błąd integralności systemowej.

## Rola JunexisStudio

JunexisStudio nie jest kompilatorem języka.

Jest:

- Orkiestratorem systemu
- Strażnikiem integralności
- Wizualizatorem grafu aktorów
- Analizatorem pokrycia narracyjnego

IDE może:

- Obliczać Narrative Coverage
- Wskazywać nieobjęte pipeline’y
- Wspierać tworzenie narracji
- W trybie offline wymuszać refleksję architektoniczną

## Jak to wpływa na roadmap JunexisStudio

- W v0.1–v0.2 JunexisStudio buduje fundamenty (workspace, extension-host, procesy, diagnostyka) – to jest warstwa infrastrukturalna.
- Mechanizmy integralności narracyjnej (coverage, tryby Development/Release, reguły semantyczne) są docelowo elementem warstwy semantycznej i powinny wejść jako rozszerzenia/serwisy, nie jako logika w Shell.

## Konsekwencje filozoficzne

Junexis nie pozwala budować systemów bez sensu.

Można eksperymentować.
Można prototypować.

Ale system w trybie Release musi posiadać:

- Zdefiniowaną intencję
- Określony zakres
- Świadomą architekturę

To odróżnia Junexis od tradycyjnych języków programowania.

Junexis jest systemem budowy systemów rozproszonych.

Elegancja oznacza brak luk.
Arystokracja oznacza dyscyplinę.

## Zamknięcie Etapu 2

Etap 2 zostaje uznany za zamknięty, gdy:

- Workspace posiada model modułów
- Pipeline jest formalnie zdefiniowany
- NarrativeIntegrityRule jest zaimplementowana
- BuildLevel (Development/Release) jest obsługiwany

Od tego momentu Junexis nie jest wyłącznie językiem.

Staje się zwartym systemem budowy systemów rozproszonych.

## DoD (Etap 2 – integrity)

Etap 2 jest uznany za zamknięty, gdy:

- Workspace posiada model modułów
- Pipeline jest formalnie zdefiniowany
- `NarrativeIntegrityRule` jest zaimplementowana
- `BuildLevel` (Development/Release) jest obsługiwany

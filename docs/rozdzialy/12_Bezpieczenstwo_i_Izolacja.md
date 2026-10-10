# 12 – Bezpieczeństwo i Izolacja

---

# 1. Cel

Bezpieczeństwo w JunexisStudio ma dwa wymiary:

1. Stabilność systemu (odporność na błędy rozszerzeń).
2. Granice zaufania (rozszerzenia i integracje zewnętrzne).

Celem nie jest budowa „pancernego sandboxa” na start.
Celem jest zapewnienie, że:

* pojedyncze rozszerzenie nie może wywrócić systemu,
* zewnętrzne dane (MCP, procesy, pliki) są traktowane jako nieufne,
* rdzeń pozostaje mały i przewidywalny.

---

# 2. Granice Zaufania

System rozróżnia źródła danych:

* użytkownik (akcje w UI) – wysoki poziom zaufania,
* pliki na dysku – średni poziom zaufania,
* procesy zewnętrzne – niski poziom zaufania,
* MCP i usługi sieciowe – niski poziom zaufania.

Zasada:

* wszystko spoza rdzenia i UI użytkownika traktujemy jako potencjalnie wadliwe.

---

# 3. Izolacja Rozszerzeń – poziomy

## 3.1. Poziom 0 (v0.1) – izolacja logiczna

* przechwytywanie wyjątków na granicy host ↔ rozszerzenie,
* logowanie do Output,
* możliwość dezaktywacji rozszerzenia.

## 3.2. Poziom 1 – izolacja ładowania (AssemblyLoadContext)

* ładowanie rozszerzeń w dedykowanym AssemblyLoadContext,
* ograniczenie konfliktów wersji,
* możliwość odpinania i aktualizacji (docelowo).

## 3.3. Poziom 2 – izolacja procesowa (przyszłość)

* uruchamianie rozszerzeń w osobnym procesie,
* komunikacja IPC,
* twarda izolacja.

Na start nie jest wymagana.

---

# 4. AssemblyLoadContext

Docelowo każde rozszerzenie jest ładowane:

* w osobnym AssemblyLoadContext,
* z kontrolą rozwiązywania zależności (resolver),
* z jasną polityką: co może współdzielić, a co nie.

Cel:

* uniknięcie konfliktów wersji,
* możliwość wspierania wielu wersji bibliotek,
* kontrola cyklu życia DLL.

---

# 5. Przechwytywanie wyjątków

Host:

* opakowuje Initialize i Shutdown w try/catch,
* opakowuje handler eventów (subskrypcje) w bezpieczny wrapper,
* raportuje wyjątki do kanału Extensions.

Zasada:

* wyjątek w rozszerzeniu nigdy nie zabija hosta.

---

# 6. Polityka uruchamiania procesów

Procesy zewnętrzne (kompilatory, narzędzia) są uruchamiane przez:

* IProcessService (capability)

Polityka:

* kontrola czasu wykonania (timeout) – opcjonalnie,
* kontrola katalogu roboczego,
* kontrola zmiennych środowiskowych,
* przechwytywanie stdout/stderr,
* raportowanie exit code.

Wynik procesu jest zawsze logowany do Output.

---

# 7. Dostęp do systemu plików

Rozszerzenia używają:

* IFileSystem

Na poziomie v0.1 IFileSystem może mapować 1:1 na dysk.

W przyszłości IFileSystem może:

* ograniczać dostęp do obszaru workspace,
* oferować tryb read-only,
* zapewniać wirtualne źródła.

---

# 8. Dane z MCP i sieci

Wszystkie dane z MCP:

* są walidowane,
* są traktowane jako nieufne,
* nie mogą automatycznie powodować akcji.

Akcje muszą przejść przez CommandRegistry i zgodę użytkownika.

---

# 9. Minimalny zakres v0.1

Pierwsza wersja powinna zapewnić:

* przechwytywanie wyjątków rozszerzeń,
* kanał Extensions w Output,
* możliwość wyłączenia wadliwego rozszerzenia,
* jasne granice zaufania dla MCP i procesów.

Bez:

* izolacji procesowej,
* pełnego sandboxa,
* podpisywania rozszerzeń.

---

Kolejny dokument:

00_Plan_Implementacji_i_Milestones.md

W nim rozpiszemy plan realizacji (milestones) od v0.1 do kolejnych iteracji.

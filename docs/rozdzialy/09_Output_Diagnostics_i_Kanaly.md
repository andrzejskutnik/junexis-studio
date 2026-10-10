# 09 – Output, Diagnostyka i Kanały

---

# 1. Cel

System Output i Diagnostyki odpowiada za obserwowalność JunexisStudio.

Ma umożliwiać:

* raportowanie błędów rozszerzeń,
* raportowanie zdarzeń systemowych,
* prezentację wyników kompilatora,
* raportowanie komunikatów MCP,
* wsparcie debugowania architektury.

Brak Output nie może blokować systemu, ale system musi zawsze logować zdarzenia krytyczne.

---

# 2. Output jako System Kanałów

Output działa w oparciu o kanały logiczne.

Przykładowe kanały:

* Core
* Extensions
* Build
* MCP
* Workspace
* Audit

Każde rozszerzenie może:

* utworzyć własny kanał,
* pisać do istniejącego kanału.

---

# 3. Model Wpisu Output

Każdy wpis powinien zawierać:

* Timestamp
* Channel
* Severity (Trace / Info / Warning / Error / Critical) – docelowo
* Message
* Source (ExtensionId)
* Optional Exception

Wpisy powinny być niemutowalne.

---

# 4. Diagnostyka Systemowa

Diagnostyka systemowa dotyczy:

* błędów rozszerzeń,
* problemów ładowania DLL,
* konfliktów wersji,
* nieobsłużonych wyjątków.

Błąd rozszerzenia:

* jest przechwytywany przez host,
* logowany do kanału Extensions,
* nie zatrzymuje systemu.

Rozszerzenie może zostać dezaktywowane.

---

# 5. Diagnostyka Domenowa

Diagnostyka domenowa pochodzi z rozszerzeń, np.:

* kompilator → błędy składni,
* MCP → błędy komunikacji,
* parser DSL → ostrzeżenia strukturalne.

Diagnostyka domenowa powinna być publikowana jako zdarzenie:

* DiagnosticsUpdated

Panel "Problems" jest osobnym rozszerzeniem konsumującym to zdarzenie.

---

# 6. Integracja z Output Panel

Output Panel:

* subskrybuje wpisy z kanałów,
* umożliwia filtrowanie po kanale i severity,
* umożliwia czyszczenie logów,
* nie przechowuje logiki biznesowej.

Panel nie powinien przechowywać historii w nieskończoność.

Możliwa przyszłość:

* eksport do pliku,
* integracja z systemem audytu.

---

# 7. Audit

Kanał Audit przeznaczony jest dla:

* zdarzeń zawierających wrażliwe dane,
* operacji administracyjnych,
* zmian konfiguracji.

Audit może być obsługiwany przez osobne rozszerzenie zapisujące dane trwale.

---

# 8. Minimalny zakres v0.1

Pierwsza wersja powinna zawierać:

* rejestr kanałów,
* logowanie wpisów (Timestamp + Channel + Message),
* panel Output bez obowiązkowego filtrowania,
* przechwytywanie wyjątków rozszerzeń.

Severity i filtrowanie po severity można wprowadzić jako rozszerzenie modelu w v0.2.

Bez:

* zewnętrznych backendów logowania,
* systemu agregacji rozproszonej,
* zaawansowanej analizy logów.

---

Kolejny dokument:

10_System_Kontroli_Wersji_jako_Rozszerzenie.md

W nim zostanie opisany model VCS jako capability oraz integracja z Explorer i Workspace.

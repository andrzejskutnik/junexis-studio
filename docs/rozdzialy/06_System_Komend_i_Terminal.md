# 06 – System Komend i Terminal

---

# 1. Rola Systemu Komend

System komend jest centralnym mechanizmem wywoływania akcji w JunexisStudio.

Komendy:

* są rejestrowane przez rozszerzenia,
* mogą być wywoływane przez UI,
* mogą być wywoływane przez inne rozszerzenia,
* nie są bezpośrednio powiązane z konkretnym elementem UI.

UI jest tylko jednym z konsumentów komend.

---

# 2. Model Komendy

Każda komenda powinna posiadać:

* CommandId
* DisplayName
* Category (np. File, Build, View)
* Execute(context)
* CanExecute(context)

Komenda nie powinna zawierać logiki UI.

Uwaga wersjonowania:

* v0.1 może zacząć od uproszczenia: `Execute()` / `CanExecute()` bez kontekstu.
* v0.2+ wprowadza `CommandContext` i warianty `Execute(context)` / `CanExecute(context)`.

---

# 3. Typy Komend

## 3.1. Globalne

Dostępne zawsze.

Przykłady:

* OpenFolder
* SaveAll
* BuildProject

---

## 3.2. Kontekstowe

Dostępne w zależności od kontekstu.

Przykłady:

* OpenFile (Explorer)
* RunCurrentFile (Editor)
* ShowOutline (Document)

Kontekst przekazywany jest jako obiekt CommandContext.

---

# 4. CommandContext

CommandContext powinien umożliwiać przekazanie:

* aktywnego dokumentu,
* wybranego pliku w Explorer,
* workspace,
* aktywnego panelu.

Rozszerzenie decyduje, czy obsługuje dany kontekst.

---

# 5. Integracja z UI

Shell:

* buduje menu na podstawie CommandRegistry,
* przypisuje skróty klawiaturowe,
* obsługuje aktywację komend.

Rozszerzenia nie manipulują bezpośrednio menu.

---

# 6. Pipeline Wykonania Komendy

1. Użytkownik aktywuje komendę.
2. Shell sprawdza CanExecute.
3. Wywoływane jest Execute.
4. Komenda może:

   * opublikować zdarzenie,
   * wywołać capability,
   * uruchomić proces zewnętrzny.

System powinien umożliwiać:

* logowanie wykonania,
* przechwytywanie wyjątków,
* raportowanie błędów do Output.

---

# 7. Terminal

Terminal jest rozszerzeniem.

Nie jest częścią rdzenia.

Terminal odpowiada za:

* uruchamianie procesów systemowych,
* wyświetlanie stdout / stderr,
* obsługę interaktywnej powłoki.

Terminal może publikować zdarzenia:

* ProcessStarted
* ProcessOutputReceived
* ProcessExited

---

# 8. Kompilator a Terminal

Kompilator jako rozszerzenie może:

* używać TerminalProvider capability,
* lub bezpośrednio uruchamiać procesy przez IProcessService.

Rekomendacja:

Wprowadzić IProcessService jako capability systemową.

Terminal może być jednym z konsumentów tej capability.

---

# 9. Output Panel

Output jest niezależny od Terminala.

Output służy do:

* logów systemowych,
* błędów rozszerzeń,
* komunikatów kompilatora,
* komunikatów MCP.

Terminal służy do interakcji z procesem.

Output służy do informacji.

---

# 10. Minimalny zakres v0.1

Pierwsza wersja powinna zawierać:

* rejestr komend,
* obsługę globalnych komend,
* prosty terminal (bez zaawansowanych funkcji),
* output panel z kanałami.

Bez:

* makr,
* pipeline automatyzacji,
* zaawansowanego routingu komend.

---

Kolejny dokument:

07_Kompilator_jako_Rozszerzenie.md

W nim zostanie opisany model kompilatora, diagnostyka oraz integracja z workspace i systemem komend.

# 15 – Rozpoczęcie Implementacji v0.1

---

# 1. Cel

Celem v0.1 jest uruchomienie minimalnego, działającego środowiska:

* Shell startuje,
* Host ładuje rozszerzenia,
* można otworzyć folder,
* można otworzyć i zapisać plik,
* działa Output.

Bez kompilatora. Bez MCP. Bez VCS.

Najpierw stabilny fundament.

---

# 2. Kolejność Realizacji (Ściśle Zachować)

## Krok 1 – Solution i Projekty

Utworzyć:

* Junexis.Studio.Abstractions
* Junexis.Studio.Core
* Junexis.Studio.Host
* Junexis.Studio.Shell

Referencje:

* Core → Abstractions
* Host → Core, Abstractions
* Shell → Host, Abstractions

Na tym etapie brak Extensions.

---

## Krok 2 – Minimalne Abstractions

Zdefiniować tylko:

* IJunexisExtension
* IHostContext
* IEventBus
* ICommandRegistry
* IPanelRegistry
* IOutput
* IWorkspace

Bez nadmiaru metod.
Minimalny kontrakt.

---

## Krok 3 – Minimalny Core

Zaimplementować:

* EventBus (Publish/Subscribe)
* CommandRegistry (rejestr + Execute)
* Output (kanał + lista wpisów)
* Workspace (OpenFolder)

Bez FileWatcher na start.

---

## Krok 4 – Minimalny Host

Zaimplementować:

* ExtensionLoader (na razie ładowanie statyczne z listy)
* Cykl życia: Initialize / Shutdown
* Bez AssemblyLoadContext w pierwszym kroku
* Try/Catch wokół Initialize

Host tworzy IHostContext i przekazuje do rozszerzeń.

---

## Krok 5 – Minimalny Shell (Avalonia)

Zaimplementować:

* MainWindow
* Prosty layout (lewy panel + centrum + dół)
* Integrację z CommandRegistry
* Integrację z PanelRegistry

Bez zapisu layoutu.

---

## Krok 6 – Pierwsze Rozszerzenie: Explorer

Zaimplementować jako projekt:

* JunexisStudio.Extension.Explorer

Funkcje:

* wyświetlenie drzewa folderu z Workspace
* publikacja zdarzenia FileOpenRequested

Bez ikon VCS. Bez zaawansowanych funkcji.

---

## Krok 7 – Drugie Rozszerzenie: Editor

* reaguje na FileOpenRequested
* ładuje zawartość pliku
* umożliwia edycję
* zapisuje plik przez Workspace

Bez składni, bez IntelliSense.

---

## Krok 8 – Trzecie Rozszerzenie: Output

* panel Output
* subskrypcja wpisów
* wyświetlanie kanałów

---

# 3. Pierwszy Punkt Kontrolny

Po zakończeniu v0.1 aplikacja powinna:

* uruchamiać się bez błędów,
* ładować rozszerzenia,
* pozwalać otworzyć folder,
* pozwalać otworzyć plik,
* pozwalać zapisać plik,
* pokazywać logi w Output.

To jest zamknięty, działający system.

---

# 4. Czego NIE robić w v0.1

* Nie implementować kompilatora.
* Nie implementować MCP.
* Nie implementować VCS.
* Nie implementować izolacji AssemblyLoadContext.
* Nie pisać marketplace.

Jeśli coś nie jest konieczne do uruchomienia Explorer + Editor → nie robimy.

---

# 5. Zasada v0.1

Ma działać.
Ma być proste.
Ma być stabilne.
Ma dawać fundament pod v0.2.

---

Kolejny dokument:

16_Minimalne_Interfejsy_i_Pierwszy_Kod.md

W nim zdefiniujemy minimalne wersje interfejsów i pierwsze szkielety klas (bez nadmiaru abstrakcji).

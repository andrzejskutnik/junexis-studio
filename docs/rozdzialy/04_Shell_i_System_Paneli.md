# 04 – Shell i System Paneli

---

# 1. Rola Shell

Shell jest warstwą UI odpowiedzialną wyłącznie za:

* główne okno aplikacji,
* system dockowania,
* pasek menu i skróty klawiaturowe,
* obsługę layoutu,
* integrację z CommandRegistry.

Shell nie zawiera logiki domenowej.
Nie zna szczegółów kompilatora, MCP ani OMWiA.

---

# 2. Typy Paneli

System paneli dzieli się na trzy główne typy:

## 2.1. Tool Window

Panele narzędziowe:

* Explorer
* Output
* Terminal
* OMWiA
* Outline

Charakterystyka:

* mogą być dockowane po bokach
* mogą być auto-hide
* mogą być wielokrotnie otwierane

---

## 2.2. Document Panel

Panele dokumentowe (zakładki edytora):

* edytor pliku
* podgląd AST
* podgląd grafu

Charakterystyka:

* otwierane jako tab
* posiadają kontekst dokumentu
* mogą być zamykane niezależnie

---

## 2.3. Temporary Panel

Panele tymczasowe:

* quick preview
* podgląd wyników
* modalne narzędzia pomocnicze

Nie są częścią trwałego layoutu.

---

# 3. Docking System

Docking musi umożliwiać:

* podział poziomy i pionowy,
* grupowanie paneli w zakładki,
* zapisywanie i przywracanie layoutu,
* dynamiczne dodawanie paneli przez rozszerzenia.

Layout jest odpowiedzialnością Shell.

Rozszerzenia rejestrują panele, ale nie zarządzają ich rozmieszczeniem.

---

# 4. Rejestracja Paneli

Rozszerzenie rejestruje panel poprzez PanelRegistry.

Rejestracja powinna zawierać:

* PanelId
* PanelType (Tool / Document / Temporary)
* Factory tworzące View lub ViewModel

Rejestracja jest rozszerzona o:

* Title
* DockGroup (Left / Right / Bottom / Center)

Shell odpowiada za:

* utworzenie instancji panelu,
* wpięcie go w system dockowania,
* zarządzanie jego cyklem życia.

---

# 5. Bieżący układ i persystencja

Shell tworzy sześć podstawowych obszarów:

* menu u góry,
* pełnowysoki Explorer po lewej,
* edytor w centrum,
* panel narzędziowy pod edytorem,
* pełnowysoki panel narzędziowy po prawej,
* pasek stanu na dole.

Domyślne rozmieszczenie:

* `DockGroup.Bottom` — Output, Problems, Terminal, Artifacts, Analysis,
  Language Services, Toolchains i pozostałe narzędzia ogólne,
* `DockGroup.Right` — Debug, Changes, OMWiA i OMWiA Chat.

Panel dolny nie rozciąga się pod Explorerem ani panelem prawym. Dzięki temu
układ zachowuje czytelny obszar edytora i przypomina sprawdzony model VS Code.

Shell zapisuje przy zamykaniu i przywraca przy starcie:

* szerokość Explorera,
* wysokość panelu dolnego,
* szerokość panelu prawego.

Dane trafiają do `shell-layout.json` w katalogu danych użytkownika
`JunexisStudio`. Błąd odczytu lub zapisu nie może zatrzymać aplikacji; Shell
wraca wtedy do bezpiecznych wartości domyślnych.

Rozszerzenie nie może zakładać, że jego panel zostanie zawsze automatycznie otwarty.

---

# 6. Integracja z CommandRegistry

Shell integruje menu oraz skróty klawiaturowe z CommandRegistry.

Przykłady:

* File → Open
* Build → Run
* View → Explorer
* View → Output

Rozszerzenia mogą dodawać swoje komendy do istniejących grup lub tworzyć nowe.

---

# 7. Otwarcie Dokumentu

Scenariusz:

1. Użytkownik otwiera plik w Explorer.
2. Explorer publikuje zdarzenie FileOpenRequested.
3. Odpowiednie rozszerzenie (np. edytor tekstowy) reaguje.
4. Rejestruje dokument jako Document Panel.
5. Shell tworzy zakładkę w centrum.

Shell nie decyduje, jak renderować dokument.
To odpowiedzialność rozszerzenia.

---

# 8. Minimalny Zestaw Paneli w v0.1

Pierwsza wersja systemu powinna zawierać:

* Explorer (Tool)
* Editor (Document)
* Output (Tool)

Terminal i Outline mogą wejść w kolejnej iteracji jako niezależne rozszerzenia (v0.2+).

OMWiA może być dodany jako osobne rozszerzenie w kolejnej fazie.

---

# 9. Stan ekranu powitalnego

Gdy `IWorkspace.RootPath` jest pusty, widok Explorera nie renderuje drzewa
przypadkowego katalogu. Pokazuje trzy jawne wejścia:

* Open Folder,
* Clone Repository,
* Recent Workspaces.

Shell jest właścicielem natywnych dialogów, obsługi niezapisanych kart i
uruchomienia procesu klonowania. Core przechowuje historię folderów, a Host
buduje neutralny model widoku. Szczegóły opisuje
[Workspace i repozytorium — przepływ użytkownika](../WORKSPACE_REPOSITORY_WORKFLOW.md).

---

Kolejny dokument:

05_Workspace_i_System_Plikow.md

W nim zostanie opisany model workspace, zarządzanie dokumentami oraz reakcja na zmiany plików.

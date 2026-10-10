# 14 – Struktura Repozytorium i Solution

---

# 1. Cel

Celem tego dokumentu jest ustalenie spójnej, długowiecznej struktury solution .NET oraz repozytorium.

Struktura musi:

* wspierać modularność,
* ułatwiać testowanie,
* oddzielać rdzeń od rozszerzeń,
* umożliwiać niezależny rozwój extensionów.

---

# 2. Struktura Solution (.sln)

Proponowana struktura logiczna:

JunexisStudio.sln

## 2.1. Projekty rdzeniowe

* Junexis.Studio.Shell
* Junexis.Studio.Host
* Junexis.Studio.Core
* Junexis.Studio.Abstractions

## 2.2. Projekty infrastrukturalne

* JunexisStudio.Infrastructure (opcjonalnie)
* JunexisStudio.DefaultExtensions (opcjonalnie bootstrap)

## 2.3. Projekty rozszerzeń (w solution lub osobno)

* JunexisStudio.Extension.Explorer
* JunexisStudio.Extension.Editor
* JunexisStudio.Extension.Output
* JunexisStudio.Extension.Compiler.Gpp
* JunexisStudio.Extension.Mcp.Basic
* JunexisStudio.Extension.Vcs.Git
* JunexisStudio.Extension.OMWiA

---

# 3. Odpowiedzialności Projektów

## 3.1. Junexis.Studio.Abstractions

Zawiera wyłącznie:

* interfejsy (IHostContext, IEventBus, ICompilerService itd.),
* modele kontraktowe (Diagnostic, CommandContext, OutputEntry),
* kontrakty zdarzeń.

Nie zawiera implementacji.

Jest wspólną warstwą referencyjną dla Host i Extensionów.

---

## 3.2. Junexis.Studio.Core

Zawiera:

* implementację EventBus,
* implementację CommandRegistry,
* CapabilityRegistry,
* Output system,
* Workspace model.

Nie zawiera UI.

---

## 3.3. Junexis.Studio.Host

Zawiera:

* ExtensionLoader,
* zarządzanie cyklem życia rozszerzeń,
* integrację Core z Shell,
* obsługę izolacji (AssemblyLoadContext).

---

## 3.4. Junexis.Studio.Shell

Zawiera:

* Avalonia UI,
* Docking,
* integrację z CommandRegistry,
* Layout management.

Nie zawiera logiki biznesowej.

---

# 4. Struktura Repozytorium

Proponowana struktura katalogów:

/src
/Shell
/Host
/Core
/Abstractions
/Extensions
/Explorer
/Editor
/Output
/Compiler.Gpp
/Mcp.Basic
/Vcs.Git
/OMWiA
/tests
/docs
/extensions (folder runtime – ładowane DLL)

---

# 5. Folder Runtime Extensions

Folder runtime:

./extensions/

Zawiera:

* DLL rozszerzeń,
* pliki manifestu,
* zależności prywatne rozszerzenia.

Ten folder nie musi być częścią solution.

---

# 6. Manifest Rozszerzenia

Każde rozszerzenie powinno posiadać plik manifestu, np.:

extension.json

Zawiera:

* Id
* Version
* EntryType
* ExposedCapabilities
* MinimalIdeVersion

Manifest służy do fazy Discover.

---

# 7. Wersjonowanie

Propozycja:

* IDE: SemVer
* Extensions: SemVer
* Manifest zawiera minimalną wersję IDE

Host podczas ładowania sprawdza kompatybilność.

---

# 8. Testy

Każda warstwa powinna mieć testy:

* Core → testy jednostkowe
* Host → testy integracyjne (ładowanie extension)
* Extensions → testy jednostkowe capability

UI nie musi mieć pełnego pokrycia w v0.1.

---

# 9. Minimalny Start Projektu

1. Utworzyć solution.
2. Utworzyć Abstractions.
3. Utworzyć Core.
4. Utworzyć Host.
5. Utworzyć Shell.
6. Uruchomić pustą aplikację z działającym Host.

Dopiero potem pisać pierwsze rozszerzenie.

---

Kolejny dokument:

15_Rozpoczecie_Implementacji_v0_1.md

W nim rozpiszemy pierwsze konkretne kroki kodowe (bez nadmiaru), zgodnie z milestone v0.1.

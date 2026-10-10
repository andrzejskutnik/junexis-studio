# 17 – Przewodnik po kodzie v0.1

## Cel

Ten dokument opisuje „jak czytać kod” w aktualnym stanie v0.1: gdzie są moduły, jakie są minimalne kontrakty oraz jaka jest najkrótsza ścieżka rozszerzania.

Zakres: tylko to, co jest już w repo.

## Struktura solution

Solution: `JunexisStudio.sln`

Projekty:

- `src/Abstractions/Junexis.Studio.Abstractions.csproj`
- `src/Core/Junexis.Studio.Core.csproj`
- `src/Host/Junexis.Studio.Host.csproj`
- `src/Shell/Junexis.Studio.Shell.csproj`

Wszystkie projekty celują w `net10.0`.

Repo pin-uje SDK przez `global.json`.

## Abstractions – kontrakty

Folder: `src/Abstractions/`

Minimalny zestaw interfejsów (v0.1):

- `IJunexisExtension` – rozszerzenie z `Initialize(context)` i `Shutdown()`
- `IHostContext` – paczka usług dostępnych dla rozszerzeń
- `IEventBus` – Publish/Subscribe/Unsubscribe
- `ICommandRegistry` – rejestr komend (bez kontekstu; kontekst dopiero v0.2)
- `IPanelRegistry` – rejestr paneli (tool/document) na potrzeby Shell
- `IOutput` – zapis do kanału
- `IWorkspace` – minimalny workspace (OpenFolder + Read/Write)

Ważne założenie v0.1: **Abstractions nie zależy od UI**.

## Core – minimalne implementacje

Folder: `src/Core/`

Co już jest:

- `EventBus` – prosta implementacja oparta o listy handlerów per typ
- `CommandRegistry` – `Dictionary<string, Action>`
- `PanelRegistry` – rejestr paneli (na start tylko nazwy + fabryki)
- `OutputService` – lista wpisów + event `Written` (do podpięcia panelu Output)
- `WorkspaceService` – `OpenFolder`, `ReadFile`, `WriteFile` + `FileOpened`

Cel Core w v0.1: umożliwić działanie fundamentu bez DI/marketplace/capabilities.

## Host – uruchamianie i lifecycle

Folder: `src/Host/`

- `JunexisStudioHost` tworzy instancje usług Core, składa `HostContext` i uruchamia loader.
- `ExtensionLoader` na start jest statyczny (lista instancji). Docelowo w kolejnych iteracjach będzie to dynamiczne ładowanie.
- `HelloExtension` jest „smoke testem”: potwierdza, że Host potrafi zainicjalizować rozszerzenie.

Zasada v0.1: **wyjątki z `Initialize` nie mogą wysadzić hosta** – loader loguje błąd do `IOutput`.

## Shell – UI (Avalonia)

Folder: `src/Shell/`

- `App` tworzy `JunexisStudioHost`, wywołuje `Start()` i podpina `Shutdown()` na `Exit`.
- `MainWindow` ma minimalny layout: lewy panel / centrum / dół.

W v0.1 Shell jest tylko „powłoką” – nie implementuje logiki domenowej.

## Jak uruchomić

Build:

```
+dotnet build JunexisStudio.sln -c Debug
```

Run:

```
+dotnet run --project src/Shell/Junexis.Studio.Shell.csproj -c Debug
```

## Jak dopinać kolejne elementy (następne kroki)

Najbliższe rozszerzenia v0.1 (zgodnie z planem):

1) `JunexisStudio.Extension.Explorer` – widok drzewa folderu na bazie `IWorkspace`
2) `JunexisStudio.Extension.Editor` – otwarcie/edycja/zapis (na start zwykły TextBox)
3) `JunexisStudio.Extension.Output` – panel Output oparty o `OutputService.Written`

Kiedy wchodzi v0.2:

- `CommandContext` w komendach
- `IProcessService`
- diagnostyka z severity
- dynamiczne ładowanie DLL rozszerzeń

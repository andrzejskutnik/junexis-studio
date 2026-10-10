# Junexis Studio

IDE oparte o .NET i Avalonia dla języka Junexis oraz kodu C++, Go, C# i
TypeScript używanego przez projekty Junexis.

Studio integruje kompilator, artefakty i analizę, Mentora, adaptery debugowania
DAP oraz Narrative Diagnostic Story. Projekt nadal jest eksperymentalny, ale
ma już samodzielne paczki dla Windows x64, Linux x64 i macOS ARM64.

> **Status:** active component

## Co to jest

`junexis-studio` jest produktowym środowiskiem pracy dla platformy Junexis.
Nie jest tylko viewerem plików `.jnx`, ale miejscem, w którym spotykają się
compiler, tooling, Mentor, Narrative, debugowanie i workflow projektu.

## Co działa dziś

Studio jest już aktywnym komponentem, a nie samym szkieletem UI. Dziś można je
uruchomić lokalnie z checkoutu źródeł i przejść przez podstawowy workflow pracy
z workspace, plikami `.jnx`, toolchainami, diagnostyką, buildem i wybranymi
ścieżkami debugowania.

Najważniejsze praktyczne fakty:

- Studio integruje compiler i Mentora jako realne backendy pracy,
- posiada onboarding przez Start Center i Command Palette,
- obsługuje wspólny workflow Build / Run / Debug,
- spina debugowanie DAP z Narrative Diagnostic Story.

## Jak czytać to README

Pierwsza część ma dać szybkie wejście do produktu: od czego zacząć, jakie są
minimalne wymagania i jak uruchomić Studio lokalnie. Dalsza część schodzi do
architektury, DAP, struktury repo i zasad pracy nad projektem.

## Zacznij tutaj

- [Pierwsze 10 minut w Junexis Studio](docs/GETTING_STARTED.md)
- [Kompilator Junexis](https://github.com/andrzejskutnik/junexis-lang)
- [Podręcznik użytkownika Studio](docs/USER_GUIDE.md)
- [Skróty klawiaturowe](docs/KEYBOARD_SHORTCUTS.md)
- [Przepływ workspace i repozytorium](docs/WORKSPACE_REPOSITORY_WORKFLOW.md)
- [Rozwiązywanie problemów](docs/TROUBLESHOOTING.md)
- [Dokumentacja architektury i implementacji](docs/README.md)
- [Samodzielne Studio na trzech systemach](docs/m19-5-1-multiplatform-studio.md)

## Minimalne wymagania

- .NET SDK 10,
- zbudowany compiler Junexis,
- zbudowany Mentor dla zakładki findings.

Pełna solucja Studio ma również referencje projektowe do
`junexis-observability` i `junexis-mcp-studio`. Te repozytoria nie są jeszcze
dostępne publicznie, dlatego sam klon tego repo nie wystarcza do zbudowania i
uruchomienia całej aplikacji. Pełny build wymaga workspace z tymi projektami
oraz z compilerem i Mentorem. Dwa testy kontraktowe Core korzystają z fixture'ów
z repozytorium Mentor i są pomijane w publicznym CI; pozostałe testy Core
uruchamia workflow GitHub Actions. Nie jest to walidacja całej solucji ani UI.

## Szybki start

```powershell
# W workspace zawierającym wymagane repozytoria junexis-*
dotnet build .\junexis-lang\compiler\JunexisLang.Compiler.CLI\JunexisLang.Compiler.CLI.csproj
dotnet build .\junexis-mentor\JunexisLang.Mentor.CLI\JunexisLang.Mentor.CLI.csproj
dotnet build .\junexis-studio\JunexisStudio.sln -c Debug
dotnet run --project .\junexis-studio\src\Shell\Junexis.Studio.Shell.csproj -c Debug
```

Po starcie Studio przywróci ostatni poprawny folder albo pokaże ekran z
**Otwórz folder**, **Klonuj repozytorium** i listą ostatnich projektów. Otwórz
`.jnx`, odśwież panel **Toolchains** i użyj `F6`. Git aktywuje się wyłącznie
dla folderu zawierającego bezpośrednio znacznik `.git`.

`Ctrl+P` otwiera plik z aktywnego workspace po nazwie lub fragmencie ścieżki,
a `Ctrl+Shift+P` uruchamia przeszukiwalną paletę wszystkich aktualnie
dostępnych poleceń Studio.

Gdy nie ma otwartej karty, centralny **Start Center** pokazuje bieżący
workspace, ostatnie foldery oraz skróty do otwierania projektu, Toolchain
Doctora, Mentora, Narrative i wewnętrznego **Help Center**. Te same tematy
pomocy są dostępne z menu Help i Command Palette.

Pionowy **Activity Bar** po lewej przełącza Explorer, Problems, Source Control,
Run & Debug i Narrative. Standardowe skróty to odpowiednio `Ctrl+Shift+E`,
`Ctrl+Shift+M`, `Ctrl+Shift+G` oraz `Ctrl+Shift+D`.
Badge przy Problems i Source Control pokazuje bieżącą liczbę diagnostyk oraz
zmienionych ścieżek. Ponowne wybranie aktywnego Explorera, Search, Debug albo
Source Control zwija jego panel.
Activity Bar, Explorer, wyszukiwanie, terminal i pasek Run & Debug korzystają
ze wspólnego zestawu ikon wektorowych, dzięki czemu ich wygląd nie zależy od
fontu systemowego. Aktywny obszar oraz focus klawiatury mają widoczny akcent.

Górny pasek **Run & Debug** pokazuje workspace, aktywny plik i wybrany profil
języka. Przyciski Build, Run i Debug wykonują automatyczny preflight Toolchain
Doctora; brak wymaganego narzędzia blokuje start i prowadzi do jego diagnozy.
Problems i Output są dostępne bez zmiany kontekstu, a **Why?** uaktywnia się
po zatrzymaniu sesji DAP.

Build i Run dla Junexis, C/C++, C#/.NET, Go oraz TypeScript, a także sesje
Debug dla C/C++, Go i TypeScript raportują wspólny lifecycle
`Starting → Running → Succeeded/Failed/Cancelled`. Pasek pokazuje rzeczywisty
czas i liczbę diagnostyk, a Stop jest aktywny tylko podczas trwającej operacji.
Szczegółowy stan zatrzymania, stosu i zmiennych nadal pochodzi bezpośrednio
z sesji DAP.

Panel **Deploy** prowadzi przepływy `M7.5`–`M7.7` dla wdrożeń Junexis w Studio. Pracuje
wyłącznie na zweryfikowanym `latest-build receipt`, uruchamia `package` i
`deploy preflight`, a następnie może wystartować ograniczoną czasowo sesję
`deploy local` dla targetów `server` i `report`. Dla `embedded` udostępnia
osobny, bezpieczny przepływ: read-only probe, jawny wybór urządzenia, pakowanie
dokładnie zweryfikowanego Build ID i flash dopiero po dodatkowym potwierdzeniu
operatora. Wynik flash jest zapisany w deterministycznym receipt.

`Ctrl+Shift+F` zastępuje Explorer panelem globalnego wyszukiwania. Wyniki są
grupowane według pliku, a każde trafienie prowadzi do dokładnej linii i kolumny.
Panel obsługuje match case oraz filtry plików include/exclude.

Przy pustym workspace Studio zwija panele boczne i pozostawia centralny Start
Center jako jedyne miejsce onboardingu. Zakładki narzędziowe pozostają w jednym
rzędzie; przycisk `…` udostępnia kompletną listę paneli. Ostatni panel dolny i
prawy są przywracane po ponownym uruchomieniu.

Edytor korzysta z LSP dla Junexis, C/C++, Go i TypeScript/JavaScript:
semantic highlighting działa automatycznie, `F2` zmienia nazwę symbolu,
`Ctrl+.` stosuje Code Action, `F12` przechodzi do definicji, a `Shift+F12`
zbiera referencje.

## Architektura

- Abstractions: kontrakty bez zależności od UI
- Core: usługi niezależne od UI
- Host: lifecycle, rozszerzenia i integracje procesowe
- Shell: UI Avalonia bez logiki domenowej
- extensions: rozszerzenia compilerów i innych capability

## Debug Adapter Protocol

Studio ma wspólną sesję DAP dla profili C++, Go i TypeScript. Rejestr adapterów
automatycznie wykrywa `OpenDebugAD7` z rozszerzenia VS Code C/C++, `dlv`
z `PATH` lub katalogu `~/go/bin` oraz przypięty standalone `js-debug` w lokalnym
cache Junexis (`%LOCALAPPDATA%\\Junexis\\toolchains\\js-debug`). Delve i
`js-debug` są uruchamiane jako lokalne serwery TCP. Każde automatyczne
wykrywanie można nadpisać odpowiednią zmienną:

- `JUNEXIS_CPP_DAP`,
- `JUNEXIS_GO_DAP`,
- `JUNEXIS_TYPESCRIPT_DAP`.

Opcjonalne argumenty procesu, identyfikator `type` i transport konfigurują
odpowiednio zmienne z sufiksem `_ARGS`, `_TYPE` i `_TRANSPORT`, np.
`JUNEXIS_CPP_DAP_ARGS`. Transport może mieć wartość `stdio` albo `tcp`; dla
TCP argumenty muszą zawierać znacznik `{port}`. Ścieżkę do GDB można ustawić
przez `JUNEXIS_GDB`, do GNU C++ przez `JUNEXIS_GXX`, a do Delve przez
`JUNEXIS_DLV`. Na Windows Studio automatycznie wykrywa również instalacje
GDB/G++ w `C:\\msys64\\ucrt64\\bin` i `C:\\msys64\\mingw64\\bin`.
Toolchain Doctor sprawdza każdą capability DAP przed uruchomieniem profilu.
`F7` rozpoczyna debugowanie aktywnego języka, `F9` przełącza breakpoint w
bieżącej linii, a `F10`/`F11` realizują Step Over/Step Into.

Build C++ korzysta z tego samego wykrytego G++ co Toolchain Doctor i dodaje
`-g -O0`, aby wynik `junexis_build.exe` zawierał symbole potrzebne przez GDB.
Żądanie DAP `initialize` otrzymuje identyfikator aktywnego adaptera (`cppdbg`,
`go` albo `pwa-node`); jest to wymagane przez `OpenDebugAD7`, który na tej
podstawie wybiera właściwy silnik debuggera.

Przypięty adapter TypeScript można zainstalować poleceniem
`pwsh -File scripts/install-js-debug.ps1`. Skrypt pobiera oficjalny standalone
`vscode-js-debug` 1.117.0 i weryfikuje SHA-256 przed rozpakowaniem do cache
Junexis. Repozytorium nie przechowuje binariów zewnętrznych.

Build Junexis publikuje `debug/junexis.source-map.json` (schema 1.0). Studio
używa stable symbol ID, stabilnych ścieżek statementów IR i wygenerowanych
zakresów, aby tłumaczyć breakpointy `.jnx` na dokładne linie C++/Go/TypeScript
oraz klatki runtime z powrotem na instrukcje Junexis. Starsze buildy bez mapy
zachowują fallback po nazwie symbolu.

Lowering 1:N jest obsługiwany przez wiele wpisów o tym samym `irPath`; Studio
wysyła każdą linię i kolumnę runtime. Odpowiedź DAP `setBreakpoints` jest
widoczna w sekcji `Adapter verification`, wraz ze stanem `verified`, faktyczną
lokalizacją, komunikatem adaptera i jego identyfikatorem breakpointu.

Edytor pokazuje te same dane bezpośrednio w lewym marginesie. Kliknięcie glyphu
przełącza breakpoint we wspólnym magazynie i, gdy sesja działa, natychmiast
ponawia `setBreakpoints`. Kolor rozróżnia stan oczekujący, potwierdzony,
przesunięty przez adapter, częściowo potwierdzony przy mapowaniu 1:N oraz
odrzucony; szczegóły są dostępne w tooltipie. Zdarzenia DAP `breakpoint`
aktualizują glyph bez restartowania sesji. Transport TCP obsługuje IPv4/IPv6
oraz dodatkowe sesje `startDebugging`, których standalone `js-debug` używa do
uruchomienia właściwego procesu Node.

Po każdym zdarzeniu `stopped` panel Debug automatycznie wybiera pierwszą klatkę,
nawiguje edytor do zmapowanej lokalizacji Junexis, ładuje scopes i preferuje
scope `Locals` do natychmiastowego pokazania zmiennych. Sekcja `Watches`
przechowuje wyrażenia dodawane przyciskiem lub Enterem i odświeża je przez DAP
`evaluate` w aktualnej klatce po każdym breakpointcie oraz kroku. Błąd jednego
wyrażenia jest pokazany przy tym watchu i nie blokuje pozostałych.

Zmienne i watches z niezerowym `variablesReference` są prezentowane jako
rozwijane drzewa; dzieci są pobierane leniwie dopiero po rozwinięciu węzła.
Lista watches jest zapisywana per workspace w
`.junexis/studio-watches.json` i odtwarzana po ponownym otwarciu projektu.
Podczas zatrzymanej sesji najechanie na identyfikator lub prosty łańcuch pól w
edytorze (`sensor.value`) wywołuje anulowalne DAP `evaluate` i pokazuje wynik
oraz typ w tooltipie. Przesunięcie wskaźnika anuluje nieaktualne zapytanie.

M6.9 łączy ten przepływ z Narrative. Każde zmapowane zdarzenie DAP `stopped`
automatycznie koreluje pierwszą klatkę z eventem aktywnego build ID i ładuje
jego Diagnostic Story. `Continue`, kolejny krok albo zakończenie sesji anuluje
nieaktualną korelację, więc wynik poprzedniej pauzy nie może pojawić się po
wznowieniu programu. Panel Why pozwala zatrzymać wyłącznie auto-follow
(`Pause follow`) bez zatrzymywania Collectora i debuggee, a `Resume` natychmiast
wybiera najnowszy pasujący event. Opcjonalne dokładne filtry Narrative ID i
event kind są stosowane zarówno do auto-follow, jak i korelacji klatki.

M6.10 dodaje pracę historyczną bez odłączania debuggera. `Previous` i `Next`
wybierają najbliższy event zgodny z aktywnym build ID oraz filtrami
Narrative/kind i automatycznie pauzują auto-follow. Bieżącą Diagnostic Story
można przypiąć jako stabilny punkt odniesienia, wrócić do innego eventu, a
następnie porównać oba punkty. Zakładka `Comparison` pokazuje zgodność buildu,
dodane i usunięte dowody oraz delty timeline, causal chain, concurrency i
komunikacji. Nawigacja ani przypięcie nie usuwają korelacji zatrzymanej klatki.

M6.11 utrwala wyniki diagnozy per workspace. Zakładka `Bookmarks` zapisuje
nazwaną, kompletną Diagnostic Story atomowo w
`.junexis/narrative-session.json`; ponowne otwarcie nie wymaga działającego
Collectora. Nazwa działa jak stabilny slot — ponowny zapis aktualizuje
bookmark zamiast tworzyć duplikat. `Export session bundle` tworzy ZIP schema
1.0 z aktualną i przypiętą Story, porównaniem, korelacją debuggera, wszystkimi
bookmarkami, README oraz manifestem SHA-256 dla każdego pliku dowodowego.
Bundle nie zapisuje tokenów ani ephemeral TraceCollector URL.

M6.12 domyka przepływ przekazywania diagnozy. `Import bundle` najpierw
weryfikuje schema 1.0, dozwoloną strukturę archiwum, limity rozmiaru, kompletność
manifestu SHA-256 oraz zgodność identyfikatorów Story, porównania i bookmarków.
Archiwum nie jest rozpakowywane do workspace. Dopiero poprawny pakiet otwiera
się jako jawna sesja `Imported · read-only`; można przeglądać Story, porównanie,
korelację i bookmarki bez Collectora, ale nie można zmienić lokalnego magazynu.
`Close imported` przywraca bookmarki bieżącego workspace.

M6.13 udostępnia w zakładce `Session` jawne provenance pakietu: nazwę pliku,
czas eksportu, źródłowy workspace, build ID, eventy, liczbę bookmarków oraz
inwentarz każdego pliku zweryfikowanego przez SHA-256 wraz z rozmiarem i
hashem. Jeśli przed importem była otwarta live Diagnostic Story, Studio
zachowuje ją wyłącznie jako oznaczonego kandydata porównania. `Compare imported
with live` raportuje zgodność buildu, kierunkowe różnice evidence i delty
struktur, nie łącząc obu źródeł w jedną historię.

M6.14 oddziela autentyczność od integralności. `Export signed bundle` podpisuje
dokładne bajty manifestu kluczem ECDSA P-256 z zewnętrznego pliku PEM; klucz
prywatny nie trafia do ZIP ani ustawień Studio. Import pokazuje `Unsigned`,
`SignatureVerified`, `TrustedSigner` albo `InvalidSignature`, nazwę podpisującego
i fingerprint SHA-256 klucza publicznego. `Trust signer` zapisuje wyłącznie
publiczny klucz w lokalnym magazynie użytkownika, a `Remove trust` cofa tę
decyzję. Nazwa podpisującego nigdy nie zastępuje porównania fingerprintu.

M6.15 dodaje osobną warstwę `Review`, nie zmieniając pakietu dowodowego.
Observation, Hypothesis, Decision i Action są jawnie rozróżnione, mogą wskazać
event, stabilny symbol oraz zweryfikowany plik evidence. `Save revision`
utrwala pełny snapshot i status sprawy (`Open`, `Investigating`, `Resolved`,
`Rejected`), a `Compare latest` pokazuje kierunkową zmianę dwóch rewizji.
Recenzja jest sidecarem związanym z dokładnym Bundle ID. Może mieć własny
podpis; każda edycja usuwa podpis z bieżącej wersji. `Export case` pakuje
niezmienny bundle i recenzję, zaś `Import case` weryfikuje oba kontrakty offline.

M6.16 włącza Mentora do tego przepływu bez przekazania mu autorstwa sprawy.
`Generate from Story` tworzy osobną kolejkę wyłącznie z findings zapisanych w
Story, zgodnych z dokładnym build ID i stabilnym symbolem. Mentor może
zaproponować Observation, Hypothesis lub Action, ale nigdy Decision. Dopiero
`Accept selected` kopiuje propozycję do wersji roboczej z jawnym
`Source=Mentor`, kodem reguły i build ID; `Dismiss selected` nie zmienia
recenzji. Zapis rewizji i status sprawy nadal wymagają działania człowieka.

Zatrzymanie na breakpointcie lub po kroku podświetla całą aktualnie wykonywaną
linię w zmapowanym pliku Junexis albo źródle języka docelowego. Panel Debug
pozwala edytować `condition`, `hitCondition` i `logMessage`; wartości są
zachowywane podczas mapowania 1:N i wysyłane bezpośrednio w DAP
`setBreakpoints`. Logpoint ma rombowy glyph, a breakpoint ograniczony warunkiem
jest oznaczony punktem wewnątrz glyphu.

Breakpointy wraz z condition, hit condition i log message są zapisywane per
workspace w `.junexis/studio-breakpoints.json`; ścieżki pozostają względne, więc
konfiguracja jest przenośna po sklonowaniu lub przeniesieniu projektu. Panel
Debug pokazuje filtry wyjątków reklamowane przez adapter i pozwala zastosować
np. `uncaught` lub `all` przez DAP `setExceptionBreakpoints`. `Debug/Run to
Cursor` (`Ctrl+F10`) mapuje pozycję kursora Junexis na kod wygenerowany, pobiera
`gotoTargets` i wznawia właściwy wątek żądaniem `goto`.

Piąta sekcja panelu Debug zawiera interaktywną Debug Console. Polecenia są
wysyłane przez DAP `evaluate` z kontekstem `repl`, a wyniki, typy i błędy
pozostają w historii; strzałki góra/dół odtwarzają wcześniejsze polecenia.
Jeśli adapter reklamuje `supportsSetVariable`, zaznaczoną zmienną można zmienić
przez `setVariable`, a drzewo odświeża wartość i ewentualne dzieci. Dla
`supportsDataBreakpoints` Studio wykonuje `dataBreakpointInfo`, pozwala wybrać
`write`, `read` lub `readWrite`, condition i hit condition, po czym synchronizuje
watchpointy przez `setDataBreakpoints`.

Górna sekcja panelu Debug jest edytorem trwałych konfiguracji uruchomienia.
Profile C++, Go i TypeScript/Node mogą działać jako `launch` albo `attach`, z
własnym programem, katalogiem roboczym, argumentami, `stopOnEntry` lub PID-em.
Lista procesów systemowych pozwala wskazać cel `Attach to Process`, a wybrana
konfiguracja jest uruchamiana przyciskiem `Start` albo komendą
`Debug/Start Selected Configuration`. Konfiguracje i aktywny wybór są zapisywane
per workspace w `.junexis/studio-debug.json`; nowy workspace otrzymuje trzy
gotowe profile startowe.

Wybór procesu jest evidence-first. Studio odróżnia korelację ścieżki procesu z
wersjonowanym `junexis.artifacts.json` od słabszej heurystyki znanego runtime,
rekomenduje profil i pokazuje konflikt z wyborem użytkownika. Jeśli build ma
`debug/junexis.source-map.json`, panel potwierdza gotowość mapowania runtime →
Junexis, ale nie utożsamia jej z przyczynowością: odpowiedź `WHY` wymaga
zarejestrowanych zdarzeń Narrative. Odłączenie sesji `attach` nie kończy
obserwowanego procesu; `launch` zachowuje własność uruchomionego debuggee.

Po każdym DAP `stopped` panel `Why?` koreluje pierwszą zmapowaną klatkę ze
zdarzeniem TraceCollector przez stabilny symbol i dokładną lokalizację `.jnx`.
Pokazuje klasę dowodu i nie wybiera zdarzenia przy braku zgodności. Przycisk
`Why did this happen?` w panelu Debug aktywuje panel Narrative, przekazuje
klatkę, wybiera skorelowany event i ładuje jego causal chain. Ten przepływ jest
również dostępny jako komenda `Debug/Why Did This Happen?`.

Od M6.5 Studio zarządza lokalną sesją Narrative dla zweryfikowanego
latest-build. Po publikacji lub odtworzeniu receiptu uruchamia TraceCollector
na losowym porcie `127.0.0.1`, przekazuje właściwy debug manifest, monitoruje
health i wybiera najnowszy event o zgodnym build ID. Panel **Why?** pokazuje
stan sesji oraz jawne operacje Start, Refresh i Stop; ręczny URL i event ID
pozostają dostępne dla zewnętrznego Collectora.

Od M6.6 zdrowa sesja jest także rzeczywistym kontekstem uruchomienia. Komendy
Run dla C++, Go i TypeScript atomowo przekazują procesowi potomnemu
`JUNEXIS_TRACE_URL` oraz `JUNEXIS_BUILD_ID`. DAP dodaje tę samą parę jako
`env` dla konfiguracji `launch`; nie robi tego dla `attach`, ponieważ Studio
nie może zmienić środowiska już działającego procesu. Kontekst jest dostępny
wyłącznie dla stanu `Ready`, adresu loopback i niepustego build ID. Go i
TypeScript odczytują zmienne bezpośrednio. Fizyczny target embedded C++ nadal
wymaga równoważnych definicji kompilatora, ponieważ mikrokontroler nie ma
środowiska procesu hosta.

M6.7 usuwa wyścig pomiędzy publikacją latest-build a natychmiastowym Run lub
Debug. Gdy zarządzana sesja jest właśnie w stanie `Starting` albo przełącza
build, launch coordinator czeka maksymalnie 3 sekundy na `Ready`. Pasek
operacji pokazuje wtedy przygotowanie Narrative. Sukces dołącza atomowy
kontekst M6.6; timeout albo stan `Failed` uruchamia artefakt bez tracingu i
zapisuje jawne ostrzeżenie. Stop anuluje także oczekujący start DAP. Nieaktywna
sesja nie powoduje oczekiwania ani fałszywego komunikatu o timeout, a `attach`
nadal rozpoczyna się natychmiast.

M6.8 zamyka aktywną pętlę runtime → Studio bez dodawania drugiego pollera.
Monitor zarządzanej sesji klasyfikuje stan jako `Preparing`,
`WaitingForEvents`, `Live` albo `Disconnected`, publikuje deltę liczby eventów
i czas ostatniej zaobserwowanej aktywności. Panel **Why?** pokazuje ten stan
oddzielnie od lifecycle Collectora. Nowy event bieżącego buildu automatycznie
ustawia Event ID i pobiera jego Diagnostic Story. Opcję **Auto-follow latest
event** można wyłączyć, zachowując automatyczny wybór eventu bez automatycznego
zapytania. Zmiana buildu unieważnia poprzednią historię, a szybka seria eventów
preferuje najnowszy zaobserwowany event.

Po wybraniu zdarzenia panel pobiera `Diagnostic Story` 1.1 z Narrative:
lokalną oś czasu, causal chain, zdarzenia współbieżne, stabilne komunikacje i
evidence wraz z `buildId` rzeczywistego zdarzenia. Mentor analizuje bieżący
snapshot kompilatora. Studio dołącza jego opinię tylko wtedy, gdy build ID
Story, aktywnego snapshotu i wyniku Mentora są identyczne; w przeciwnym razie
zakładka Mentor jawnie pokazuje powód pominięcia. Zakładka Mentor w
panelu Analysis pokazuje findings wraz z wyjaśnieniem i następną akcją,
architektoniczne insights oraz rozpoznane patterns. Diagnostic Story nadal
pokazuje wyłącznie findings powiązane stable symbol ID z taskiem, pipeline'em
lub komunikacją obecną w historii i nie przedstawia analizy statycznej jako
przyczyny runtime. Wynik można zapisać jako JSON albo Markdown wraz z build ID.

M8.9 dodaje kartę `Analysis → Mentor → Conformance`. Studio szuka
`semantic-conformance.json` przy Analysis Snapshot, w `out/`, `.junexis/` lub
katalogu projektu. Pokazuje status, liczniki i luki jako nieblokującą opinię
Mentora; `partial` nie zmienia wyniku kompilacji. Korelacja raportu z Build ID
i Bundle ID jest domknięta w M8.10: raport 0.2 jest pokazywany jako
`BOUND build <skrót> / bundle <skrót>`, a raport projektowy 0.1 jako `UNBOUND`.
Mentor odrzuca raport 0.2, którego Build ID nie odpowiada aktywnemu snapshotowi.

Projekt może jawnie zapisać decyzje i wąskie wyciszenia w
`.junexis/mentor.yml`. Studio stosuje tylko poprawną politykę 1.0 wymagającą
dokładnego kodu `Mxxxx`, stabilnego `symbolId` i uzasadnienia. Nieważny plik
nie ukrywa żadnego findingu. Decyzje, konfiguracja i każde wyciszenie
zastosowane w bieżącym buildzie pozostają widoczne w
`Analysis → Mentor → Policy`.

Dokumentacja projektowa (rozdziały i plany) jest w katalogu docs:

- docs/README.md

## Struktura repo

- src/Abstractions – interfejsy i kontrakty
- src/Core – minimalne implementacje
- src/Host – host + loader
- Host publikuje najnowszy raport sesji Agenta w kanale Output `Agent`
- Komenda `agent.refresh-session` odświeża raport bez restartu Studio
- Host automatycznie obserwuje `.junexis/sessions` i odświeża kanał `Agent` po zapisaniu sesji
- Panel `Agent Report` pokazuje status, cel i liczbę zmienionych plików
- Panel `Agent Report` pokazuje listę zmienionych plików i otwiera wybrany plik w Editorze
- Lista oznacza pliki jako `Changed`, `Unchanged` albo `Missing` na podstawie hashy sesji Agenta
- Panel pokazuje także zapisany bounded unified diff pod każdym plikiem
- src/Shell – UI (Avalonia)
- docs – dokumentacja architektury i planów
- tests – testy jednostkowe i traceability do issue
- extensions – docelowo runtime folder na DLL rozszerzeń (nie jest częścią solution)

## Workflow (issue-driven)

- Pracujemy tygodniowymi iteracjami: pn (plan/retro), wt–pt (kod/testy)
- Każde issue ma osobny branch: feat/issue_<nr>
- Push robimy dopiero przy zamykaniu issue

Kolumny na Issue Board:

- w toku
- w testach

## Zasady v0.1

- Najpierw stabilny fundament: Explorer + Editor + Output
- MCP/Agent integration jest dostępna przez `StudioMcpBridge`; Host pokazuje najnowszy raport w Output i panelu `Agent Report`, odświeża go automatycznie, umożliwia otwarcie zmienionego pliku i udostępnia ręczne odświeżenie. Statusy plików korzystają z baseline/hash zapisanych w Agent session.

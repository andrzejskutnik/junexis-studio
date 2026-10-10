# Plan Git i VCS

## Cel

Git w Junexis Studio ma być bezpieczną, nieblokującą capability workspace.
Żadna pozycja menu nie może niejawnie stage'ować plików, tworzyć commita ani
blokować wątku UI. Brak Git lub repozytorium nie może wpływać na edytor,
kompilator, debugger ani Mentora.

## Zasady bezpieczeństwa

- `VCS → Commit...` otwiera panel Changes; nie wykonuje operacji zapisu.
- Commit obejmuje wyłącznie jawnie staged zmiany.
- `Stage All` pozostaje osobną i jednoznaczną akcją.
- Każda operacja procesu jest asynchroniczna, anulowalna i ma widoczny stan.
- Po operacji Studio ponownie odczytuje Git zamiast zakładać oczekiwany stan.
- Argumenty Git są przekazywane strukturalnie, bez składania polecenia shell.
- Testy integracyjne korzystają z tymczasowych repozytoriów, nigdy z repo
  deweloperskiego.

## P0 — bezpieczeństwo i responsywność

Stan: ukończony.

- Zamienić bezpośredni commit z menu na aktywację panelu Changes.
- Usunąć niejawne `git add -A` ze starej ścieżki.
- Wykonywać commit panelu poza wątkiem UI.
- Blokować równoległy commit i pokazywać stan zajętości.
- Dodać regresję: kliknięcie menu nie wywołuje backendu.
- Dodać regresję: wolny backend nie blokuje wywołującego.

Kryterium ukończenia: przypadkowe kliknięcie nie zmienia indeksu, a wolna
operacja nie zamraża Shell.

## P1 — jeden kontrakt i jedno źródło prawdy

Stan: ukończony dla aktywnej ścieżki produktu.

- Aktywny Shell, panel Changes, dekoracje i Explorer używają jednego
  asynchronicznego `IVcsService`.
- `VcsRepositorySnapshot` jest wspólnym źródłem prawdy, a `VcsFileState`
  modeluje osobno index i working tree.
- `GitWorkspaceVcsService` izoluje Git CLI w Host i reaguje na otwarcie
  workspace oraz zapis pliku.
- Stage, Unstage, Refresh, Commit, Diff i Log nie używają synchronicznych
  mostków `.GetAwaiter().GetResult()`.
- Dawny `IVersionControlService` pozostaje wyłącznie jako nieaktywna warstwa
  zgodności dla starych rozszerzeń i zostanie usunięty po ich migracji.

Kryterium ukończenia: spełnione — Shell i Explorer czytają ten sam snapshot,
a Stage, Unstage i Commit operują na rzeczywistym indeksie.

## P2 — panel Changes i diff

Stan: ukończony.

- Panel ma osobne sekcje Conflicts, Staged, Changes i Untracked.
- Stage/Unstage obsługuje plik, wielokrotne zaznaczenie i cały zakres.
- Panel pokazuje diff working tree oraz diff staged przed commitem.
- Zapisy plików uruchamiają jedno odświeżenie po 250 ms debounce.
- Status używa `git status --porcelain=v2 --branch -z`, zachowuje rename/copy,
  Unicode, spacje i znaki nowej linii w ścieżkach.
- Argumenty Git są przekazywane strukturalnie przez `ProcessStartInfo.ArgumentList`.
- Snapshot pokazuje branch, detached HEAD, upstream, ahead/behind oraz aktywny
  merge, rebase, cherry-pick, revert lub bisect.

Kryterium ukończenia: spełnione — użytkownik przed commitem widzi dokładny
zakres staged i może otworzyć jego diff.

## P3 — commit produkcyjny

Stan: ukończony.

- Preflight waliduje wiadomość, konflikty, konfigurację `user.name` /
  `user.email`, obecność staged changes i dostępność statystyki.
- Panel pokazuje autora, liczbę staged plików i `git diff --cached --stat`.
- Natywny `git commit` wykonuje hooki i skonfigurowane podpisywanie GPG;
  stderr jest pokazywany użytkownikowi, proces jest nieinteraktywny, ma
  dwuminutowy timeout i może zostać anulowany.
- HEAD jest odczytywany przed i po procesie. Po przerwaniu Studio ponownie
  sprawdza HEAD i oznacza wynik jako niepewny zamiast zakładać rezultat.
- Po sukcesie publikowany jest zweryfikowany nowy HEAD; po błędzie, timeoutcie
  i anulowaniu wiadomość pozostaje w panelu.
- Zakres co najmniej 50 plików lub 1000 zmienionych linii wymaga drugiego,
  jawnego działania `Confirm large commit`.

Kryterium ukończenia: spełnione — commit jest deterministyczny, pokazuje
dokładny staged scope i nigdy nie rozszerza samodzielnie zakresu.

## P4 — historia, branche i operacje zdalne

Stan: ukończony.

- Panel Repository pokazuje 100 ostatnich commitów, autora, rodziców,
  dekoracje, body, statystykę i listę zmienionych ścieżek z rename/copy.
- Można tworzyć branch na HEAD lub wybranym commicie, przełączać wyłącznie
  przy czystym index/worktree oraz porównywać dwa refy przez ahead/behind i
  three-dot diff stat.
- Stash obejmuje jawnie tracked i untracked; apply/pop działa na wybranym
  `stash@{n}`, tylko na czystym drzewie, i po operacji ponownie odczytuje
  konflikty.
- Remote i URL-e są widoczne bez danych uwierzytelniających. Fetch wskazuje
  remote, pull wymaga zgodnego aktualnego brancha i używa `--ff-only`, a push
  wymaga jawnego remote, brancha i opcjonalnego `set-upstream`.
- Operacje repozytorium są anulowalne, mają pięciominutowy limit panelu i nie
  uruchamiają się automatycznie.

Kryterium ukończenia: spełnione — historia i lokalny workflow są dostępne w
jednym panelu, a każda operacja zdalna pokazuje i przekazuje strukturalnie swój
cel.

## P5 — integracja narrative

Stan: ukończony.

Każda mutująca operacja publikuje historię dowodową: intencję użytkownika,
repozytorium, branch, HEAD przed i po, wybrane ścieżki, czas trwania oraz
rezultat. Commit może być korelowany z build ID, analizą Mentora i Diagnostic
Story. Domyślnie nie zapisujemy treści diffu ani sekretów w telemetrycznym
śladzie.

- `GitVcsEvidenceService` utrzymuje ograniczoną historię 200 najnowszych
  wpisów, publikuje `VcsOperationEvidenceRecordedEvent` i zapisuje ten sam
  dowód do wspólnego `IAuditService`.
- Repozytorium jest opisane nazwą i nieodwracalnym identyfikatorem SHA-256;
  pełna ścieżka nie trafia do dowodu. HEAD jest skracany do 12 znaków.
- Wynik ma stabilny kod zamiast surowego stdout/stderr. Wiadomość commita,
  treść diffu, URL remote, snapshot path i sekrety nie są utrwalane.
- Korelacja commita z Mentorem i Diagnostic Story jest przyjmowana tylko,
  gdy ich build ID jest zgodny z aktualnym snapshotem kompilatora.
- Zakładka **Evidence** w panelu Repository pokazuje operację, wynik, przejście
  branch/HEAD, wybrane ścieżki, czas oraz korelację Junexis.

Kryterium ukończenia: spełnione — każda ścieżka mutacji zapisuje wynik
bez rozszerzania danych audytowych, a korelacja Narrative jest deterministyczna.

## Macierz testów

- Jednostkowe: parser statusu, walidacja, przejścia stanów i blokada operacji.
- Procesowe: wolny proces, anulowanie, timeout, stderr i niezerowy exit code.
- Integracyjne: tymczasowe repo dla stage, unstage, commit, rename i conflict.
- UI: menu tylko aktywuje panel, busy state blokuje ponowne wywołanie.
- Windows: spacje, Unicode, CRLF oraz brak terminala interaktywnego.

# M17.11.3 — anulowanie i ponowienie odtwarzania

Zamykanie rozszerzenia lub Studio podczas `junexis restore` nie może zostawić
działającego Instalatora ani jego procesów potomnych. Studio anuluje token
operacji przekazany do `IProcessService`, a usługa procesu kończy całe drzewo
przez `Kill(entireProcessTree: true)`.

## Rytm bezpiecznego przerwania

1. Studio przestaje przyjmować kolejne polecenie odtwarzania, gdy jedno już trwa.
2. Zamknięcie rozszerzenia anuluje bieżącą operację.
3. `IProcessService` kończy proces publicznego `junexis` razem z procesami
   potomnymi i czeka na zamknięcie strumieni wyjściowych.
4. Rozszerzenie usuwa stan bieżącej operacji i zapisuje komunikat o anulowaniu.
5. Po ponownym uruchomieniu rozszerzenia zwykłe odtwarzanie można wykonać ponownie.

Instalator pozostaje właścicielem transakcji profilu dostawców. Anulowanie
przechodzi tym samym tokenem do pobierania i instalacji, a istniejący mechanizm
wycofania usuwa katalog przygotowawczy i przywraca poprzedni profil. Studio nie
usuwa ręcznie blokad, pamięci podręcznej ani plików transakcji.

## Dowód

Test hosta uruchamia rzeczywisty proces nadrzędny tworzący proces potomny,
anuluje operację i potwierdza zakończenie obu. Drugi test uruchamia odtwarzanie w
rozszerzeniu, przerywa je przez `Shutdown`, ponownie inicjalizuje rozszerzenie i
potwierdza sukces drugiej próby oraz dostępność kolejnego polecenia.

# M17.11.1 — postęp odtwarzania zależności

Studio prowadzi użytkownika przez odtwarzanie zależności, lecz nie przejmuje
od Instalatora odpowiedzialności za Registry. Po wybraniu polecenia **Odtwórz
zależności Junexis** uruchamia publiczne `junexis restore` przez
`IProcessService`.

## Trzy etapy

Kanał **Output → Restore** pokazuje stały rytm:

1. **Etap 1 — przygotowanie** — wskazuje bezpieczną nazwę źródła: Registry, lokalny
   zestaw wydania, lokalna pamięć podręczna albo skonfigurowany profil.
2. **Etap 2 — pobieranie i weryfikacja** — rozpoczyna pracę publicznego CLI, a kolejne
   wiersze pokazują jego bieżący postęp.
3. **Etap 3 — wynik** — potwierdza gotowość zależności albo podaje kod zakończenia.

Adres Registry pochodzi z `JUNEXIS_REGISTRY` albo z opcji
`RegistryBaseAddress`. Studio akceptuje wyłącznie bazowy adres HTTPS bez danych
użytkownika, parametrów zapytania i fragmentu. W komunikacie etapu pokazuje
tylko nazwę „Registry”, a nie właściwy adres.

## Wybór źródła

W trybie z dostępem do sieci kolejność jest jawna:

1. Registry, jeśli zostało skonfigurowane;
2. lokalny zestaw wydania wskazany przez `JUNEXIS_LOCAL_PACKAGES`;
3. profil skonfigurowany dla publicznego CLI.

Polecenie wykonywane bez sieci przekazuje wyłącznie `--offline`. Dzięki temu źródła nie są
mieszane, a publiczne CLI odczytuje istniejącą pamięć podręczną bez próby
połączenia z Registry.

## Granica odpowiedzialności

Studio nie zawiera klienta HTTP Registry, mechanizmu rozwiązywania wersji ani własnej pamięci
podręcznej. Buduje bezpieczne argumenty, uruchamia `junexis restore`, pokazuje
postęp i umożliwia anulowanie procesu. Rozwiązywanie zależności, weryfikacja
podpisów i atomowa aktualizacja profilu pozostają odpowiedzialnością Instalatora.

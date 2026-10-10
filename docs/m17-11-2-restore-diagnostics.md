# M17.11.2 — diagnostyka odtwarzania zależności

Niepowodzenie `junexis restore` ma prowadzić do decyzji, a nie pozostawiać
użytkownika z samym kodem zakończenia. Studio rozpoznaje stabilne klasy błędów
publicznego CLI i publikuje lokalny, polski komunikat w kanale **Restore** oraz
w panelu **Problems** (Problemy).

## Kody i działania

| Kod Studio | Sytuacja | Następny krok |
|---|---|---|
| `JXR001` | wymaganej zależności nie ma w pamięci dostępnej bez sieci | odzyskaj połączenie, wykonaj **Odtwórz zależności Junexis**, następnie ponów tryb bez sieci |
| `JXR002` | Registry przekroczyło limit czasu | sprawdź połączenie i ponów zwykłe odtwarzanie |
| `JXR003` | Registry ograniczyło częstotliwość żądań | odczekaj czas wskazany przez usługę i ponów |
| `JXR004` | artefakt lub katalog przekracza limit rozmiaru | sprawdź manifest albo zgłoś problem administratorowi Registry; nie ponawiaj bez zmiany przyczyny |
| `JXR005` | istnieje konflikt niezmiennego artefaktu | wybierz inną wersję albo zgłoś konflikt administratorowi Registry |
| `JXR006` | dostawca nie obsługuje wymaganej wersji SDK | dopasuj `providerVersion` i `providerSdkVersion` albo opublikuj zgodnego dostawcę |

Ograniczenie częstotliwości i limit rozmiaru są rozdzielone celowo. Pierwszy
problem jest przejściowy, natomiast drugi wymaga zmiany artefaktu, manifestu
lub konfiguracji Registry.

## Bezpieczeństwo i odpowiedzialność

Klasyfikator nie przechowuje pełnego wyjścia procesu. Rozpoznaje stabilne kody
kontraktu Registry oraz jednoznaczne komunikaty publicznego Instalatora, po czym
tworzy własną diagnostykę bez ścieżek użytkownika, adresów i treści wyjątków.

Studio nie naprawia profilu samodzielnie i nie uruchamia prywatnego
`junexis-compiler restore`. Wszystkie ponowienia przechodzą przez publiczne
polecenie **Odtwórz zależności Junexis**, czyli przez właściciela transakcji
pakietów i dostawców.

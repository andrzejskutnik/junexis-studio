# JunexisStudio – Przewodnik Budowy Systemu

---

# Wstęp

JunexisStudio nie jest klasycznym edytorem kodu ani próbą stworzenia konkurencji dla VSCode czy Visual Studio. Jest wyspecjalizowanym środowiskiem operacyjnym zaprojektowanym jako fundament dla systemu JunexisLang oraz powiązanych modułów (MCP, Runtime, OMWiA, DSL, narzędzia eksperymentalne).

Celem tego dokumentu nie jest opis gotowej implementacji, lecz świadome zaprojektowanie architektury przed rozpoczęciem kodowania.

Najpierw projektujemy.
Dopiero potem implementujemy.

Ten przewodnik ma:

* uporządkować wizję systemu,
* zdefiniować zasady architektoniczne,
* wyznaczyć granice odpowiedzialności modułów,
* zapobiec przypadkowej złożoności,
* stworzyć plan realizacji.

---

# Cel Systemu

JunexisStudio ma być:

1. Modularnym środowiskiem uruchomieniowym dla rozszerzeń.
2. Hostem dla kompilatorów, MCP oraz narzędzi analitycznych.
3. Środowiskiem pracy nad JunexisLang i powiązanymi projektami.
4. Platformą eksperymentalną dla rozwoju OMWiA.

JunexisStudio nie ma być:

* Ogólnym IDE do wszystkiego.
* Monolityczną aplikacją z twardymi zależnościami.
* Systemem, który przestaje działać przy braku jednego modułu.

System ma być odporny, rozszerzalny i długowieczny.

---

# Zasady Architektoniczne

## 1. Zasada Nieblokujących Rozszerzeń

Żadne rozszerzenie nie może blokować uruchomienia systemu.

Brak rozszerzenia:

* nie może powodować wyjątku krytycznego,
* nie może uniemożliwiać pracy w innych obszarach,
* nie może być twardą zależnością innego modułu.

System musi degradować się łagodnie.

---

## 2. Brak Twardych Zależności Między Rozszerzeniami

Rozszerzenia:

* nie mogą wymagać innych rozszerzeń w konstruktorze,
* nie mogą zakładać ich obecności,
* mogą jedynie opcjonalnie wyszukiwać dostępne możliwości (capabilities).

Komunikacja odbywa się wyłącznie przez:

* EventBus,
* rejestr capabilities,
* usługi hosta.

---

## 3. Host Nie Zawiera Logiki Domenowej

Shell / Host:

* nie zna kompilatora,
* nie zna MCP,
* nie zna OMWiA,
* nie zawiera logiki biznesowej.

Host udostępnia wyłącznie infrastrukturę:

* system paneli,
* rejestr komend,
* EventBus,
* rejestr capabilities,
* system ładowania rozszerzeń,
* logowanie / output.

---

## 4. Capability Over Dependency

Rozszerzenia mogą publikować swoje możliwości (capabilities).

Inne rozszerzenia mogą:

* sprawdzić, czy dana capability istnieje,
* opcjonalnie z niej skorzystać,
* działać bez niej.

Brak capability nie jest błędem systemowym.

---

## 5. Extension First

Każda funkcjonalność wyższego poziomu jest rozszerzeniem:

* kompilator,
* MCP,
* OMWiA,
* graf zależności,
* parser DSL,
* runtime monitor.

Jeśli coś może być rozszerzeniem – powinno nim być.

---

## 6. Architektura Ewolucyjna

System musi pozwalać na:

* eksperymenty,
* równoległe implementacje (np. dwa kompilatory),
* łatwe wyłączanie modułów,
* rozwój bez przepisywania rdzenia.

Projekt nie ma być jednorazową aplikacją.
Ma być fundamentem.

---

Kolejny dokument:

01_Wizja_Systemu.md

W nim zostanie zdefiniowane:

* czym jest JunexisStudio jako środowisko operacyjne,
* jakie problemy rozwiązuje,
* jakie są jego granice.

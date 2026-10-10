# M20.6.2 — gotowość Studio po inicjalizacji interfejsu

Studio udostępnia wewnętrzne polecenie instalatora:

```powershell
junexis-studio --readiness-probe <plik-wyniku>
```

Nie jest to skrót do `--help`. Aplikacja inicjalizuje Avalonię, tworzy hosta,
uruchamia rozszerzenia, buduje główne okno z kontekstem i wykonuje co najmniej
jedną turę dyspozytora interfejsu. Dopiero wtedy tworzy raport
`junexis.studio-readiness/0.1` i sama kończy pracę.

Raport jest tworzony tylko w nieistniejącym wcześniej pliku. Potwierdza:

- stan `ready`;
- inicjalizację Avalonia;
- uruchomienie hosta Studio;
- utworzenie głównego okna wraz z kontekstem danych.

Instalator uruchamia próbę w odizolowanym katalogu profilu, bez odziedziczonego
`PATH` i z wyłączoną telemetrią Avalonia. Ogranicza czas procesu i rozmiar
raportu, sprawdza dokładny zestaw pól, a następnie usuwa raport. Brak pliku,
inna treść, błąd inicjalizacji, niezerowy kod zakończenia albo przekroczenie
czasu oznaczają brak gotowości.

Próba została wykonana na zbudowanym Studio Windows x64. Potwierdza techniczną
gotowość powłoki i hosta, ale nie zastępuje testów użyteczności, renderowania
każdego panelu ani ręcznej kontroli klawiatury i dostępności.

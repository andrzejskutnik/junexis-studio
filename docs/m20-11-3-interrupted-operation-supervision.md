# M20.11.3 — przejęcie nadzoru nad przerwaną operacją

Nowa sesja Studio może odczytać publiczną historię i przejąć **nadzór** nad
najnowszą operacją ze stanem `in-progress` albo `recovery-required`.

```text
junexis update history --json
```

Studio sprawdza wersję kontraktu, odrzuca powtórzone pola JSON i dopuszcza
wyłącznie bezpieczne identyfikatory operacji oraz faz. Następnie pokazuje
identyfikator, ostatnią fazę i informację, czy potrzebna jest naprawa.

## Nadzór nie jest wznowieniem

Przycisk **Przejmij nadzór nad przerwaną operacją** nie wysyła `apply`, nie
otwiera dziennika na dysku i nie zmienia profilu. Publiczne CLI nie udostępnia
jeszcze ogólnego `update resume`, dlatego Studio uczciwie ogranicza się do
obserwacji oraz skierowania użytkownika do naprawy.

## Redakcja wszystkich wyników panelu

Jedna granica redakcji jest stosowana do stdout, stderr i komunikatów wyjątków
dla planu, historii, naprawy oraz powrotu. Usuwa:

- nazwane hasła, tokeny, sekrety i klucze API;
- samodzielne tokeny `Bearer`;
- dane użytkownika osadzone w adresie URL;
- poufne parametry zapytania URL;
- bezwzględne ścieżki Windows, UNC i Unix.

Surowy stdout jest przechowywany wyłącznie w ograniczonej pamięci bieżącej
operacji i służy do sprawdzenia zamkniętego kontraktu. Do widoku oraz wspólnego
kanału Studio trafia tylko wersja zredagowana.

# Aplikacja `@report` w Junexis Studio

Studio zarządza lokalnym pionem `server + report` jako jedną ograniczoną sesją.
Użytkownik nie uruchamia osobno Go, npm ani serwera statycznego. Publiczne CLI
Junexis pozostaje właścicielem budowania i procesów, a Studio prowadzi cykl
życia, prezentację stanu i diagnostykę.

## Rytuał użytkownika

1. Otwórz katalog zawierający `junexis.project.toml`.
2. Otwórz dowolny plik `.jnx` projektu.
3. Naciśnij `F5`, wybierz **Build → Uruchom aplikację Junexis** albo użyj
   przycisku **Uruchom** w panelu **Aplikacja**.
4. Studio wykonuje preflight. Błędy języka oraz `user/report/view.json` trafiają
   do panelu Problemów i zatrzymują start.
5. Po komunikacie `READY report` Studio pokazuje adres i otwiera panel w
   domyślnej przeglądarce.
6. **Zatrzymaj** anuluje sesję oraz całe drzewo procesów `server + report`.

```text
F5 / Uruchom
  → preflight
  → junexis run <projekt> --duration 300 --no-open
  → READY report · <url>
  → Studio otwiera panel i nadzoruje sesję
  → Zatrzymaj / limit czasu
  → zamknięcie całego drzewa procesów
```

## Diagnostyka

- diagnostyki preflightu zachowują kod, źródło i lokalizację kompilatora;
- niepoprawny `view.json` jest widoczny jako `B0005` w Problemach;
- błąd budowania własnego adaptera jest widoczny jako `RPT001` ze źródła
  `junexis-report-runtime`;
- pełny stdout i stderr sesji pozostaje w kanale **Report**;
- mapa `out/debug/junexis.source-map.json` pozostaje kanonicznym powiązaniem
  wygenerowanego TypeScriptu z plikiem `.jnx`.

## Kontrakty

Rozszerzenie rejestruje capability `IReportApplicationSession`, panel
`junexis.report.application` oraz komendy:

| Komenda | Znaczenie |
|---|---|
| `run.jnx` | sprawdź, zbuduj i uruchom aplikację |
| `run.jnx.open` | ponownie otwórz gotowy adres |
| `run.jnx.stop` | zatrzymaj procesy należące do sesji |

Profil Junexis wymaga dla uruchomienia capability `junexis`, `go`, `node` i
`npm`. Brak narzędzia powinien zostać wykryty przez Toolchain Doctor przed
uruchomieniem.

## Granice

Pierwsza integracja otwiera aplikację w domyślnej przeglądarce, nie osadza
silnika WWW w procesie Studio. Jedna instancja Studio zarządza jedną lokalną
sesją aplikacji Junexis. Sesja ma limit pięciu minut i może zostać wcześniej
zatrzymana przez użytkownika.

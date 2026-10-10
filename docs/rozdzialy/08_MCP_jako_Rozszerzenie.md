# 08 – MCP jako Rozszerzenie

---

# 1. Cel

MCP w JunexisStudio nie jest częścią rdzenia.

Każda integracja MCP jest rozszerzeniem, które:

* udostępnia konektor do konkretnego zasobu/usługi,
* publikuje zdarzenia do EventBus,
* (opcjonalnie) oferuje panel UI,
* działa niezależnie od innych MCP.

Brak MCP nie blokuje IDE.

---

# 2. Model Konektora

Rozszerzenie MCP publikuje capability:

* IMcpConnector

IMcpConnector powinien umożliwiać:

* Connect()
* Disconnect()
* IsConnected
* Send(request)

Oraz publikować zdarzenia:

* McpConnected
* McpDisconnected
* McpMessageReceived
* McpError

---

# 3. Wiele Konektorów

System musi umożliwiać:

* wiele konektorów MCP jednocześnie,
* niezależne zarządzanie połączeniami,
* selekcję aktywnego konektora per panel lub per zadanie.

Konektory nie wiedzą o sobie nawzajem.

---

# 4. Konfiguracja MCP

Konfiguracja konektora:

* adres usługi,
* token / credentials (jeśli dotyczy),
* tryb pracy (dev/prod),
* parametry transportu.

Konfiguracja jest przechowywana w workspace lub globalnie.

Rdzeń nie interpretuje tej konfiguracji.

---

# 5. Bezpieczeństwo i Izolacja

Założenia:

* MCP działa w sieci lokalnej / zaufanej przestrzeni.
* IDE nie wykonuje poleceń z internetu.

Konektor MCP:

* waliduje odpowiedzi,
* nie zakłada zaufania do payloadu,
* raportuje anomalie do Output.

Rozszerzenie nie może wpływać na inne moduły poza publikacją eventów i rejestracją capabilities.

---

# 6. Integracja z UI

Rozszerzenie MCP może rejestrować:

* panel statusu połączenia,
* panel komunikacji,
* panel zasobów MCP.

UI jest opcjonalne.

MCP może działać jako „headless connector” bez panelu.

---

# 7. MCP jako Dostawca Kontekstu

MCP może publikować informacje kontekstowe, np.:

* indeks dokumentacji,
* wyniki zapytań,
* stan runtime,
* dane analityczne.

Te informacje powinny pojawiać się jako zdarzenia:

* McpContextUpdated

Konsumentami mogą być:

* OMWiA,
* panel wyników,
* wyszukiwarka,
* system diagnostyki.

---

# 8. Minimalny zakres v0.1

Pierwsza wersja integracji MCP powinna:

* obsługiwać jeden konektor,
* umożliwiać connect/disconnect,
* publikować status do Output,
* udostępnić prosty panel komunikacji (opcjonalnie).

Bez:

* automatycznego discovery usług,
* złożonego routingu,
* multi-tenant security.

---

Kolejny dokument:

09_Output_Diagnostics_i_Kanaly.md

W nim zostanie opisany model Output (kanały), diagnostyka systemowa oraz raportowanie błędów rozszerzeń.

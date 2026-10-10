# 01 – Wizja Systemu

---

# 1. Czym jest JunexisStudio

JunexisStudio jest modularnym środowiskiem operacyjnym zaprojektowanym jako fundament dla:

* JunexisLang,
* systemów MCP,
* narzędzi analitycznych,
* eksperymentalnych modułów OMWiA,
* przyszłych narzędzi DSL oraz runtime.

Nie jest to klasyczne IDE skoncentrowane wyłącznie na edycji kodu.

Jest to host rozszerzeń, którego zadaniem jest:

* organizowanie przestrzeni pracy,
* zarządzanie modułami,
* umożliwienie komunikacji między nimi,
* zapewnienie stabilnej infrastruktury.

---

# 2. Problem, który rozwiązujemy

Typowe IDE:

* są monolityczne,
* zawierają silne zależności między modułami,
* trudno je przekształcić w środowisko eksperymentalne,
* zakładają konkretny sposób pracy.

W ekosystemie JunexisLang potrzebne jest środowisko, które:

* pozwala dynamicznie włączać i wyłączać moduły,
* nie blokuje pracy przy braku komponentu,
* może ewoluować razem z architekturą języka i systemów,
* obsługuje zarówno kompilację, jak i analizę, runtime, komunikację MCP oraz moduły eksperymentalne.

JunexisStudio ma być narzędziem do budowy systemów, a nie tylko programów.

---

# 3. JunexisStudio jako środowisko operacyjne

JunexisStudio należy rozumieć jako mikrosystem operacyjny dla rozszerzeń.

Host zapewnia:

* system paneli,
* rejestr komend,
* magistralę zdarzeń,
* rejestr capabilities,
* ładowanie rozszerzeń,
* zarządzanie workspace.

Cała logika wyższego poziomu znajduje się w rozszerzeniach.

System może funkcjonować nawet przy minimalnym zestawie modułów.

---

# 4. Granice systemu

JunexisStudio nie:

* implementuje kompilatora w rdzeniu,
* nie zawiera MCP w rdzeniu,
* nie zawiera OMWiA w rdzeniu,
* nie zawiera parsera DSL w rdzeniu.

Te elementy są rozszerzeniami.

Rdzeń odpowiada wyłącznie za infrastrukturę.

---

# 5. Kierunek długoterminowy

W długiej perspektywie JunexisStudio ma:

* umożliwiać rozwój JunexisLang jako języka systemowego,
* integrować narzędzia obserwacyjne (analyzers),
* integrować moduły narracyjne (OMWiA),
* stać się przestrzenią eksperymentów architektonicznych.

Nie jest to projekt jednorazowy.

Jest to fundament pod system, który będzie ewoluował przez lata.

---

Kolejny dokument:

02_Architektura_Wysokiego_Poziomu.md

W nim zostanie zdefiniowany logiczny podział warstw oraz główne komponenty systemu.

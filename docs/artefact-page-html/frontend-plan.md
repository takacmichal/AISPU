# Plán frontendu SolarOps

Podklad pre návrh UI. Popisuje **čo** majú obrazovky obsahovať (sekcie, dáta, akcie, stavy). **Ako** majú vyzerať (farby, typografia, rozostupy) určuje priložený `DESIGN.md`. Ak `DESIGN.md` chýba, platí súčasný štýl z `frontend/src/index.css`: tmavá téma v štýle Lineara, limetkový akcent `#e4f222` len pre jednu hlavnú akciu na obrazovke, písmo Inter.

- **Produkt:** SolarOps, informačný systém firmy, ktorá predáva, inštaluje a servisuje fotovoltické elektrárne (FVE).
- **Jazyk UI:** slovenčina. Čísla vo formáte `12 450,5 kWh`, dátumy `4. 11. 2026`, mena `€`.
- **Technológia:** React 19 + Vite + TypeScript, bez UI knižnice (vlastné komponenty v `frontend/src/components`).
- **Priorita:** P0 = Landing page a Dashboard (návrh hneď), P1 = existujúce obrazovky (zjednotiť vzhľad), P2 = chýbajúce časti.

---

## 1. Používatelia a role

| Rola (kód) | Kto to je | Čo potrebuje vidieť hneď po prihlásení |
|---|---|---|
| Admin | správca systému | všetko, hlavne stav systému a používateľov |
| Obchod (`Sales`) | obchodník | dopyty, ponuky, zmluvy na podpis, zálohy |
| Projektant (`Designer`) | navrhuje FVE | návrhy čakajúce na obhliadku alebo spracovanie |
| Technik (`Technician`) | montáž a servis v teréne | svoje výjazdy na dnes a tento týždeň, pridelené inštalácie |
| Operátor (`Operator`) | dispečing, monitoring | výroba všetkých elektrární, podvýroba, otvorené tikety |
| Zákazník (`Customer`) | majiteľ elektrárne | svoja elektráreň, výroba, stav montáže, nahlásenie poruchy |

Role sa dajú kombinovať. Menu a obsah dashboardu sa skladajú podľa rolí. Čo rola nesmie, sa nezobrazí (nie je to len šedé).

---

## 2. Životný cyklus FVE (kostra celého UI)

Tento rad krokov sa opakuje na landing page (sekcia „Ako to funguje“), v dashboarde (pipeline) aj v zákazníckom portáli (stepper).

1. **Dopyt**: zákazník a odberné miesto (EIC kód).
2. **Ponuka**: zostavená z katalógu (panely, striedače, práca).
3. **Návrh FVE**: obhliadka, parametre strechy, konfigurácia, odhad ročnej výroby.
4. **Zmluva**: PDF, podpis, zálohová platba.
5. **Pripojenie k distribučke**: malý zdroj ≤ 10,8 kW alebo väčší zdroj, s lehotami.
6. **Inštalácia**: objednanie komponentov, montáž, revízia, uvedenie do prevádzky, odovzdanie.
7. **Prevádzka a servis**: monitoring výroby, detekcia podvýroby, servisné tikety a výjazdy.

### Stavy (pre badge komponent)

| Entita | Stavy (slovenské popisy v UI) | Farebný tón |
|---|---|---|
| Ponuka | Rozpracovaná, Odoslaná, Prijatá, Zamietnutá, Expirovaná | neutrálny / info / úspech / chyba / neutrálny |
| Inštalácia | Naplánovaná, Komponenty objednané, Komponenty prijaté, Montáž, Montáž dokončená, Revízia, Revízia nevyhovela, Revízia vyhovela, Uvedená do prevádzky, Odovzdaná | neutrálny → info → úspech; „Revízia nevyhovela“ = chyba |
| Servisný tiket | Nový, Zatriedený, Diagnostika na diaľku, Výjazd naplánovaný, Technik na mieste, Overený, Vyriešený, Uzavretý | Nový = chyba, priebeh = info, Overený/Vyriešený = úspech |
| Priorita tiketu | Nízka, Stredná, Vysoká | neutrálny / info / chyba |
| Pôvod tiketu | Hlásenie zákazníka, Ručne vytvorený, Automatická detekcia | ikona alebo tag |

---

## 3. P0: Landing page (verejná, bez prihlásenia)

**Účel:** predstaviť firmu a systém, presvedčiť návštevníka o dopyte a dať existujúcim zákazníkom cestu do portálu.
**Adresa:** `/` pre neprihláseného. Prihlásený používateľ ide rovno na dashboard (presun dashboardu je v časti 7).

### Sekcie (zhora nadol)

1. **Horná lišta**
   - Logo SolarOps (slnko nad panelom), odkazy na kotvy: Služby, Ako to funguje, Monitoring, Kontakt.
   - Vpravo „Prihlásiť sa“ (sekundárne tlačidlo) a „Nezáväzný dopyt“ (hlavné tlačidlo).
2. **Hero**
   - Nadpis, napr. „Fotovoltika od návrhu po servis. Všetko na jednom mieste.“
   - Podnadpis: návrh na mieru, vybavenie pripojenia u distribučky, montáž s revíziou, monitoring výroby 24/7.
   - CTA: „Chcem cenovú ponuku“ (hlavné), „Mám už elektráreň – prihlásiť“ (sekundárne).
   - Vizuál: náhľad dashboardu alebo grafu výroby (skutočná vs. predikovaná krivka počas dňa) v rámiku zariadenia.
3. **Čísla** (pás 3 až 4 ukazovateľov, *ilustratívne údaje*)
   - napr. `120+` inštalovaných elektrární · `1,4 MWp` celkového výkonu · `≤ 2 h` do zachytenia poruchy · `10 pracovných dní` na vybavenie pripojenia malého zdroja.
4. **Služby** (3 karty)
   - **Predaj a návrh:** obhliadka, návrh konfigurácie, odhad ročnej výroby, cenová ponuka z overeného katalógu.
   - **Inštalácia:** komponenty so sériovými číslami, montáž, revízia, preberací protokol v PDF.
   - **Servis a monitoring:** sledovanie výroby každých 5 minút, automatické upozornenie na podvýrobu, výjazd technika.
5. **Ako to funguje**
   - Časová os 7 krokov z časti 2. Pri každom kroku ikona, krátky popis a kto ho robí (Obchod / Projektant / Technik / distribučka).
6. **Monitoring a inteligentná detekcia** (najsilnejší argument)
   - Graf skutočnej a predikovanej výroby; úsek pod 85 % predikcie zvýraznený ako „Podvýroba“.
   - Text: predikcia z predpovede počasia; ak výroba klesne pod 85 % očakávanej aspoň na 2 hodiny, systém sám založí servisný tiket.
   - Malá ukážka toho, ako tiket vyzerá: „Automatická detekcia · Vysoká priorita“.
7. **Zákaznícky portál**
   - Náhľad obrazovky „Moje elektrárne“: stav montáže, pripojenie k sieti, výroba, nahlásenie poruchy jedným klikom.
8. **Kalkulačka** (voliteľné, P1)
   - Vstupy: výkon (kWp), orientácia strechy (juh / východ-západ), lokalita (kraj).
   - Výstup: odhad ročnej výroby v kWh a orientačná úspora. Počíta sa na klientovi (približný koeficient), pod výsledkom je disclaimer.
9. **FAQ** (4 až 6 otázok)
   - Ako dlho trvá pripojenie? Čo je malý zdroj? Čo ak elektráreň vyrába menej? Ako vidím svoju výrobu? Kto robí revíziu?
10. **Kontakt / CTA pás**
    - Formulár dopytu: meno, e-mail, telefón, adresa odberného miesta, približná ročná spotreba (kWh), správa.
    - Alternatíva: „Vytvoriť účet“ (vedie na `/register`).
11. **Pätička**
    - Kontakt, odkazy (Prihlásenie, Registrácia), poznámka „Školský projekt AISPU“.

### Stavy na návrh
- Mobil (menu ako hamburger, časová os zvislo), tablet, desktop.
- Formulár dopytu: prázdny, s chybou validácie, odoslaný (poďakovanie).

---

## 4. P0: Dashboard („Prehľad“, po prihlásení)

**Adresa:** `/` (prihlásený) alebo `/app`, pozri časť 7.
**Obal aplikácie (app shell):**
- Ľavý sidebar so skupinami: (bez skupiny) Prehľad · **Obchod:** Zákazníci, Katalóg, Návrhy FVE, Ponuky, Zmluvy · **Prevádzka:** Inštalácie, Monitoring výroby, Servisné tikety, Moje elektrárne · **Administrácia:** Používatelia a roly.
- Dole v sidebari avatar, e-mail a tlačidlo odhlásenia.
- V obsahu breadcrumbs a hlavička stránky (nadpis, podnadpis, hlavná akcia vpravo).

### Rozloženie
1. **Hlavička:** „Dobrý deň, {meno}“, dnešný dátum a hlavná akcia podľa roly (Obchod: „Nový zákazník“, Technik: „Moje výjazdy“, Zákazník: „Nahlásiť poruchu“).
2. **Rad KPI kariet** (3 až 5, podľa roly). Každá karta má label, veľké číslo, detail pod ním a odkaz na zoznam. Treba stav načítania (skeleton) aj chyby s tlačidlom „Skúsiť znova“.
3. **Hlavný blok** (2/3 šírky) a **bočný blok** (1/3 šírky), obsah podľa roly nižšie.
4. **Posledná aktivita:** naposledy zmenené ponuky, inštalácie a tikety (typ, názov, stav, „pred 2 h“).
5. **Pätička:** stav API (`SolarOps API v1.4.0 · Production`, zelená bodka).

### Obsah podľa roly

| Rola | KPI karty | Hlavný blok | Bočný blok |
|---|---|---|---|
| Obchod / Admin | Zákazníci · Otvorené ponuky (odoslané / rozpracované) · Zmluvy na podpis · Zálohy po splatnosti | **Pipeline**: počty v krokoch Dopyt → Ponuka → Návrh → Zmluva → Pripojenie → Inštalácia → Prevádzka, kliknutie otvorí filtrovaný zoznam | **Na riešenie**: ponuky pred expiráciou, zmluvy čakajúce na podpis, žiadosti o pripojenie s blížiacou sa lehotou |
| Projektant | Návrhy na obhliadku · Návrhy v spracovaní · Čakajú na schválenie | Zoznam návrhov s ďalším krokom (Obhliadka / Parametre / Konfigurácia) | Posledné verzie návrhov |
| Technik | Dnešné výjazdy · Tento týždeň · Pridelené inštalácie · Otvorené tikety | **Môj harmonogram**: výjazdy na dnes a zajtra (čas, adresa, zákazník, typ: montáž/revízia/servis) | Inštalácie v stave Montáž/Revízia s rozpracovaným checklistom |
| Operátor | Výroba dnes (kWh, všetky FVE) · Elektrárne v prevádzke · Podvýroba (počet) · Otvorené tikety | **Graf výroby** celej flotily (skutočná vs. predikovaná, posledných 7 dní) | **Upozornenia**: elektrárne s podvýrobou, zariadenia bez dát > 1 h, nové tikety s vysokou prioritou |
| Zákazník | Výroba dnes · Výroba tento mesiac · Výkon teraz (kW) · Stav elektrárne | **Moja elektráreň**: graf výroby dnes (skutočná vs. predikovaná) alebo stepper montáže, ak ešte nie je v prevádzke | Moje tikety, dokumenty (zmluva, preberací protokol PDF), „Nahlásiť poruchu“ |
| Admin (navyše) | Používatelia · Aktívne API kľúče zariadení | – | Stav systému (API, ML služba, posledný import počasia) |

### Ukážkové dáta (pre mockup)
- Zákazníci **48** · Otvorené ponuky **12** (Odoslané 7 · Rozpracované 5) · Zmluvy na podpis **3** · Inštalácie v realizácii **9** (Montáž 4 · Revízia 2 · Komponenty objednané 3).
- Pipeline: Dopyt 6 · Ponuka 12 · Návrh 5 · Zmluva 3 · Pripojenie 4 · Inštalácia 9 · Prevádzka 41.
- Aktivita: „Ponuka P-2026-0142 · Odoslaná · pred 25 min“, „Inštalácia RD Novák, Košice · Revízia vyhovela · pred 2 h“, „Tiket #318 Pokles výkonu · Automatická detekcia · Vysoká · pred 3 h“.
- Zákazník: FVE 9,6 kWp, Prešov · dnes 31,4 kWh (predikcia 34,0) · mesiac 512 kWh · výkon teraz 4,2 kW · stav „V prevádzke“.

### Stavy na návrh
- Načítanie (skeleton kariet), chyba jednej karty (zvyšok funguje), prázdny stav (nová rola bez dát: „Vitajte v SolarOps“), mobil (KPI pod sebou, sidebar ako drawer).

---

## 5. P1: Ostatné obrazovky (už existujú, zjednotiť dizajn)

| Obrazovka | Adresa | Role | Obsah |
|---|---|---|---|
| Prihlásenie, Registrácia, Zabudnuté heslo, Nové heslo | `/login`, `/register`, `/forgot-password`, `/reset-password` | verejné | Karta s formulárom, odkazy medzi nimi; ladiť s landing page |
| Zákazníci (zoznam, detail, formulár) | `/customers…` | Admin, Obchod | Tabuľka s vyhľadávaním a stránkovaním; detail so záložkami Odberné miesta (EIC), Ponuky, Zmluvy; archivácia |
| Katalóg komponentov | `/catalog` | Admin, Obchod, Projektant | Panely, striedače a práca; cena, jednotka, aktívny/neaktívny |
| Návrhy FVE | `/designs…` | Admin, Obchod, Projektant | Sekcie Obhliadka, Parametre strechy a spotreby, Konfigurácia (náhľad odhadu výroby), Verzie; „Nerealizovateľné“ cez dialóg |
| Ponuky | `/quotes…` | Admin, Obchod, Projektant | Konfigurátor položiek z katalógu, súčty s DPH, odoslať / prijať / zamietnuť, história |
| Zmluvy | `/contracts…` | Admin, Obchod | PDF, podpis, platba zálohy, audit |
| Žiadosť o pripojenie | `/connection-requests/:id` | interné | **Stepper** krokov distribučky s lehotami, prílohy (jednopólová schéma, súhlas vlastníka) |
| Inštalácie | `/installations…` | Admin, Projektant, Technik, Operátor | Záložky Prehľad, Plán (termín, technici), Zariadenia (sériové čísla, výmena), Stringy, Pripojenie; checklist montáže, revízne správy, preberací protokol |
| Monitoring výroby | `/monitoring` | Admin, Operátor, Technik, Projektant | Výber inštalácie, obdobie (Dnes / 7 dní / 30 dní / Rok / Vlastné), granularita, KPI (energia, špička, priemer/deň), graf skutočná vs. predikovaná, export CSV, sekcia anomálií |
| Servisné tikety | `/tickets…` | Admin, Technik, Operátor, Zákazník | Tabuľka alebo **kanban** podľa stavu, filtre (stav, priorita, pôvod), detail s časovou osou, komentármi a výjazdmi |
| Moje elektrárne (portál) | `/my-plants…` | Zákazník | Karty elektrární (výkon, EIC, ďalší krok, pripojenie), detail so stavom montáže a výrobou |
| Používatelia a roly | `/admin/users` | Admin | Tabuľka používateľov, priradenie rolí |
| 403 / 404 | – | – | Krátka správa a „Späť na prehľad“ |

---

## 6. Spoločné komponenty (design system)

Navrhnúť raz a použiť všade:

- **Tlačidlá:** hlavné (1 na obrazovku), sekundárne, ghost, nebezpečné, malé; stav disabled a loading.
- **Formuláre:** input, select, textarea, checkbox, chyba pri poli, chyba formulára, povinné pole.
- **Tabuľka:** hlavička so zoradením, riadok s hover stavom, stránkovanie, prázdny stav, filter nad tabuľkou.
- **Badge:** tóny neutrálny / info / úspech / chyba (stavy z časti 2).
- **Karta, KPI karta, Stepper** (pripojenie, montáž), **Časová os** (história tiketu, ponuky), **Záložky**.
- **Dialóg a potvrdzovací dialóg, Toast** (úspech, chyba), **Skeleton, Spinner, Empty state**.
- **Graf výroby:** stĺpce alebo plocha (skutočná), čiara (predikovaná), zvýraznenie podvýroby, tooltip s hodnotou.
- **Prístupnosť:** kontrast WCAG 2.1 AA, viditeľný focus, skip-link, ovládanie klávesnicou.

---

## 7. Na zváženie (pred implementáciou)

- **Routing:** dnes je `/` dashboard za prihlásením. Landing page potrebuje `/` pre neprihláseného, dashboard sa presunie na `/app` alebo sa na `/` vetví podľa toho, či je používateľ prihlásený.
- **Formulár dopytu na landing page:** backend nemá verejný (anonymný) endpoint na dopyt. Buď pribudne `POST /api/inquiries`, alebo CTA zatiaľ vedie na registráciu.
- **Anomálie a upozornenia:** ML detekcia funguje, ale backend zatiaľ nemá endpoint pre upozornenia (frontend má sekciu pripravenú a vypnutú, `ANOMALIES_API_ENABLED = false`). Kým nebude, dashboard Operátora a sekcia „Upozornenia“ zobrazujú len tikety s pôvodom „Automatická detekcia“.
- **Agregované dáta pre dashboard:** pipeline, výroba celej flotily a zálohy po splatnosti zatiaľ nemajú vlastný endpoint. Dajú sa poskladať zo zoznamov, ale pri väčšom počte záznamov bude lepší `GET /api/dashboard/summary`.
- **Čísla na landing page** sú ilustratívne, v produkcii ich treba nahradiť skutočnými alebo vynechať.

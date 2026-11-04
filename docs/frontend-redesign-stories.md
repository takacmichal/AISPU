# Stories: prepracovanie frontendu podľa nového dizajnu

Podklad pre GitHub Board. Vychádza z `docs/frontend-redesign-plan.md`. Stories sú založené ako sub-issues feature #387 (pod epicom #10), milestone „Sprint 2 - Monitoring a ML“. Riadok „Závisí od“ uvádza poradové čísla stories v tomto súbore.

Spolu 11 stories, 85 SP. Väčšie stories (13 SP) sa dajú pri plánovaní šprintu rozdeliť podľa odrážok v kritériách.

| # | Issue | Story | SP | Závisí od |
|---|---|---|---|---|
| 1 | #386 | Dizajnový základ: svetlá téma, tokeny a písmo | 3 | – |
| 2 | #388 | Knižnica UI komponentov | 8 | 1 |
| 3 | #389 | Nový obal aplikácie a navigácia | 8 | 1 |
| 4 | #390 | Verejná landing page | 8 | 1, 3 |
| 5 | #391 | Prihlásenie a registrácia v novom dizajne | 3 | 1 |
| 6 | #392 | Prehľad a zákaznícky portál | 8 | 2, 3 |
| 7 | #393 | Obchod: zákazníci a ponuky | 8 | 2, 3 |
| 8 | #394 | Obchod: návrhy, zmluvy, katalóg a pripojenie k distribučke | 5 | 2, 3 |
| 9 | #395 | Realizácia: inštalácie a monitoring výroby | 13 | 2, 3 |
| 10 | #396 | Servis: tikety a výjazdy technika | 13 | 2, 3 |
| 11 | #397 | Administrácia, prístupnosť a dočistenie | 8 | 6 až 10 |

---

### 1. Dizajnový základ: svetlá téma, tokeny a písmo

Nadradené: #387

**Ako** používateľ **chcem**, aby aplikácia mala nový svetlý vzhľad podľa dizajnu, **aby** pôsobila jednotne s landing page a ďalšie obrazovky sa dali stavať na spoločných tokenoch.

## Akceptačné kritériá
- [ ] Dizajnové podklady (`docs/artefact-page-html/`) a plán prepracovania sú v repe, rozhodnutia z Fázy 0 sú odsúhlasené
- [ ] Tokeny v `index.css` podľa `Main.dc.html` (farby, zaoblenia, tiene), svetlá téma
- [ ] Písmo Plus Jakarta Sans je v repe (OFL), bez Google Fonts
- [ ] V CSS modulov nie sú natvrdo zadané farby, `frontend/DESIGN.md` popisuje tokeny a pravidlá
- [ ] Kontrast textu spĺňa WCAG AA, `pnpm lint`, `build` a `test` prechádzajú

## Odhad
3 SP

### 2. Knižnica UI komponentov

Nadradené: #387
Závisí od: 1

**Ako** vývojár **chcem** mať hotové komponenty podľa knižnice K-01 až K-10 z wireframu, **aby** som obrazovky skladal z rovnakých dielov a nemusel ich štýlovať zvlášť.

## Akceptačné kritériá
- [ ] Základ: tlačidlá (hlavné, sekundárne, ghost, nebezpečné, loading), formulár so súhrnom chýb, potvrdzovací dialóg, prázdny stav, toast
- [ ] Priebeh a ukazovatele: `Stepper` (zovšeobecnený `ConnectionStepper`), `KpiCard` s trendom alebo cieľom, `ProgressBar`, `SegmentedControl`
- [ ] Tabuľky a detaily: `Toolbar` s vyhľadávaním a filtrami, `Pager` s číslami strán, `Avatar`, `KeyValueList`, `ActivityTimeline`
- [ ] Spoločný komponent grafu výroby (plocha, predikcia, pásmo podvýroby; svetlá aj tmavá varianta), rozpor s ADR #224 zapísaný v PR
- [ ] Komponenty sa ovládajú klávesnicou a majú unit testy
- [ ] Voliteľne: ukážka všetkých komponentov na `/dev/ui` len v dev režime

## Odhad
8 SP

### 3. Nový obal aplikácie a navigácia

Nadradené: #387
Závisí od: 1

**Ako** prihlásený používateľ **chcem** prehľadné menu, hornú lištu a rovnakú hlavičku na každej stránke, **aby** som sa v aplikácii rýchlo orientoval aj na mobile.

## Akceptačné kritériá
- [ ] Sidebar so skupinami Obchod, Realizácia, Servis a Systém podľa rolí, zbaliteľný na ikony
- [ ] Horná lišta s menu používateľa (avatar, meno, rola, odhlásenie) a rýchlym skokom `⌘K` na moduly
- [ ] Spoločná hlavička stránky: breadcrumbs, nadpis, stav, podnadpis, jedna hlavná akcia
- [ ] Na mobile je sidebar ako vysúvacie menu
- [ ] Prehľad je na `/app`, adresa `/` patrí landing page, po prihlásení návrat na pôvodnú stránku
- [ ] Testy navigácie a routingu sú upravené a prechádzajú

## Odhad
8 SP

### 4. Verejná landing page

Nadradené: #387
Závisí od: 1, 3

**Ako** návštevník webu **chcem** vidieť, čo firma robí, odhadnúť si výrobu a poslať dopyt, **aby** som sa mohol rozhodnúť pre fotovoltiku.

## Akceptačné kritériá
- [ ] Sekcie podľa `Main.dc.html`: horná lišta, hero s náhľadom, čísla, služby, 7 krokov, tmavý pás Monitoring, portál, FAQ, pätička
- [ ] Kalkulačka ročnej výroby a úspory (výkon, orientácia, kraj) ako čistá funkcia s unit testom a upozornením, že ide o odhad
- [ ] Formulár dopytu so stavmi prázdny, chyba a odoslaný; kým nie je anonymný endpoint, sekcia vedie na registráciu
- [ ] Mobil: hamburger menu, kroky pod sebou; ukážkové čísla majú poznámku „Ilustratívne údaje“
- [ ] Stránka sa načítava lazy a spĺňa prístupnosť (skip-link, poradie nadpisov)

## Odhad
8 SP

### 5. Prihlásenie a registrácia v novom dizajne

Nadradené: #387
Závisí od: 1

**Ako** používateľ **chcem** prihlásenie, registráciu a obnovu hesla v novom dizajne, **aby** nadväzovali na landing page.

## Akceptačné kritériá
- [ ] Rozloženie z wireframu: formulár s prepínačom Prihlásenie/Registrácia a vedľa životný cyklus FVE
- [ ] Zabudnuté a nové heslo v rovnakom štýle
- [ ] SSO a „Zapamätať si ma“ nie sú (backend ich nemá)
- [ ] Existujúce testy auth prechádzajú

## Odhad
3 SP

### 6. Prehľad a zákaznícky portál

Nadradené: #387
Závisí od: 2, 3

**Ako** interný používateľ **chcem** po prihlásení vidieť KPI a to, čo mám riešiť, a **ako** zákazník chcem vidieť výrobu a stav svojej elektrárne, **aby** každý hneď vedel, čo je pre neho dôležité.

## Akceptačné kritériá
- [ ] Dashboard: KPI karty podľa roly (skryté pri chýbajúcej role alebo 403), bočný blok alarmov z tiketov s pôvodom Automatická detekcia
- [ ] Stavy načítania, chyby jednej karty so „Skúsiť znova“ a prázdny stav
- [ ] Portál: pozdrav, KPI, karty elektrární s aktuálnym výkonom, moje tikety, hlavná akcia „Nahlásiť poruchu“
- [ ] Elektráreň mimo prevádzky zobrazí stepper montáže
- [ ] Zákazník vidí len svoje dáta (test), testy pre aspoň 2 interné role

## Odhad
8 SP

### 7. Obchod: zákazníci a ponuky

Nadradené: #387
Závisí od: 2, 3

**Ako** obchodník **chcem** rýchlo nájsť zákazníka, vidieť jeho prehľad a stav ponúk, **aby** som vedel, s kým a čo mám ďalej riešiť.

## Akceptačné kritériá
- [ ] Zoznam zákazníkov: vyhľadávanie, záložky podľa stavu s počtami, avatar, stránkovanie
- [ ] Detail zákazníka: záložky s počtami, kontaktné údaje, súhrnné dlaždice, nasledujúci krok, aktivita
- [ ] Detail ponuky: stepper zo skutočných stavov, rozpočet so súčtami a DPH, karta zmluvy a zákazníka
- [ ] Interné schvaľovanie z wireframu sa nerobí (zapísané v „Na zváženie“), zobrazujú sa len polia z backendu

## Odhad
8 SP

### 8. Obchod: návrhy, zmluvy, katalóg a pripojenie k distribučke

Nadradené: #387
Závisí od: 2, 3

**Ako** projektant a obchodník **chcem** návrhy, zmluvy, katalóg a žiadosti o pripojenie v jednotnom dizajne, **aby** mi neutiekla žiadna lehota distribučky.

## Akceptačné kritériá
- [ ] Návrhy FVE, zmluvy a katalóg používajú spoločnú hlavičku, toolbar a zoznam údajov; logika sa nemení
- [ ] Nová stránka `/connection-requests` so zoznamom žiadostí, KPI (otvorené, blíži sa lehota, po lehote) a lehotou ako progress bar
- [ ] Testy vrátane 403 pre nepovolenú rolu

## Odhad
5 SP

### 9. Realizácia: inštalácie a monitoring výroby

Nadradené: #387
Závisí od: 2, 3

**Ako** projektant, technik alebo operátor **chcem** vidieť fázu inštalácie a porovnať skutočnú a očakávanú výrobu, **aby** som vedel, kde montáž je, a včas zachytil podvýrobu.

## Akceptačné kritériá
- [ ] Detail inštalácie: workflow so 6 krokmi namapovanými z 10 stavov backendu, karty komponentov so sériovými číslami, karta Realizácia, stringy
- [ ] Hlavná akcia „Posunúť do ďalšej fázy“ volá existujúce prechody; zoznam má záložky podľa fázy
- [ ] Monitoring: filtrovací pás (inštalácia, obdobie, rozsah, zvýraznenie podvýroby), KPI, graf zo spoločného komponentu
- [ ] Klik na obdobie podvýroby otvorí nový tiket s predvyplnenou inštaláciou
- [ ] Alarmy ostávajú za prepínačom `ANOMALIES_API_ENABLED`

## Odhad
13 SP

### 10. Servis: tikety a výjazdy technika

Nadradené: #387
Závisí od: 2, 3

**Ako** operátor **chcem** riadiť tikety v kanbane a v detaile priradiť technika, a **ako** technik chcem na mobile vidieť svoje výjazdy a vyplniť protokol, **aby** sa porucha vyriešila bez telefonovania a papierov.

## Akceptačné kritériá
- [ ] Kanban a zoznam tiketov: karty s prioritou a technikom, filtre, stĺpce podľa 8 stavov backendu
- [ ] Detail tiketu: popis s prílohami, výjazdy, servisný protokol, komentáre, výber technika, karta inštalácie
- [ ] Stránka `/my-visits` pre technika: Dnes / Zajtra / Týždeň, detail výjazdu (Zavolať, Navigovať, kontrolný zoznam), formulár protokolu
- [ ] Mobil: hlavná akcia dole, ovládacie prvky aspoň 44 px
- [ ] Zákazník nevidí interné komentáre ani cudzí tiket (test)

## Odhad
13 SP

### 11. Administrácia, prístupnosť a dočistenie

Nadradené: #387
Závisí od: 6 až 10

**Ako** admin **chcem** prehľadnú správu používateľov a **ako** používateľ s klávesnicou alebo čítačkou obrazovky chcem ovládať celú aplikáciu, **aby** bol nový dizajn dokončený a použiteľný pre všetkých.

## Akceptačné kritériá
- [ ] Používatelia a roly: tabuľka s avatarom, záložky podľa stavu, priradenie rolí v dialógu
- [ ] 403 a 404 v novom štýle
- [ ] Kontrast všetkých tónov spĺňa WCAG AA, kanban, prepínače a dialógy sa ovládajú klávesnicou, tabuľky sa na mobile dajú posúvať
- [ ] Nepoužívané štýly starej témy sú zmazané, `DESIGN.md` a README so snímkami obrazoviek sú aktuálne

## Odhad
8 SP

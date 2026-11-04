# Plán prepracovania frontendu podľa nového dizajnu

Podklady: `docs/artefact-page-html/`

| Súbor | Čo obsahuje | Ako ho čítať |
|---|---|---|
| `Main.dc.html` | Hotový vizuál landing page (hi-fi): farby, písmo, zaoblenia, grafy, formuláre | **Vizuálny jazyk** celého produktu |
| `SolarOps-Wireframe.dc.html` | Klikateľná kostra aplikácie v0.1 (sivá škála + 1 akcent): shell, všetky obrazovky, prepínač rolí, knižnica komponentov K-01 až K-10 | **Rozloženie a obsah** obrazoviek, nie farby |
| `frontend-plan.md` | Zadanie, podľa ktorého dizajn vznikol | Doménové pravidlá (stavy, role, formáty) |

Náhľad: `cd docs/artefact-page-html && python3 -m http.server`, potom `Main.dc.html` alebo `SolarOps-Wireframe.dc.html` (v hornej lište wireframu sa prepína rola a obrazovka).

**Základný princíp:** rozloženie berieme z wireframu, vzhľad z landing page. Wireframe má čierne „pill“ tlačidlá a Helveticu len preto, že je to sivá kostra; v aplikácii budú tlačidlá, farby a písmo ako na landing page.

---

## 1. Čo sa mení (súčasný stav → nový dizajn)

| Oblasť | Dnes (`frontend/src/index.css`) | Nový dizajn |
|---|---|---|
| Téma | Tmavá (Linear), `color-scheme: dark` | Svetlá, krémové pozadie `#fdfdf7`, biele karty |
| Akcent | Limetková `#e4f222` | Modrá `#0072c0` (akcia), `#0096f7` (ikony, čiary grafu) |
| Písmo | Inter | Plus Jakarta Sans 300/400/500/600, tabuľkové číslice (`tnum`, `lnum`) |
| Nadpisy | 510, malé | Veľké a tenké (300), záporný letter-spacing |
| Zaoblenie | 6 px tlačidlá, 12 px karty | 12 px tlačidlá a inputy, 18 px karty, 24 px sekcie a pilulky |
| Shell | Sidebar + obsah | Sidebar so skupinami a počítadlami, horná lišta (vyhľadávanie, notifikácie, používateľ), obsah v zaoblenom paneli |
| Verejná časť | Len prihlásenie | Landing page na `/` + nové prihlásenie (formulár + životný cyklus FVE) |

Dobrá správa: CSS je takmer celé na tokenoch (`--color-*`, `--radius-*`), natvrdo zadané farby sú len v 3 súboroch (`connection.css`, `auth.css`, `monitoring.css`). Zmena tokenov preto preklopí celú aplikáciu naraz a ďalšie fázy už riešia hlavne rozloženie.

---

## 2. Dizajnové tokeny (vytiahnuté z `Main.dc.html`)

| Token (návrh) | Hodnota | Použitie |
|---|---|---|
| `--color-bg` | `#fdfdf7` | Pozadie stránky |
| `--color-surface` | `#ffffff` | Karty, panel obsahu, dialógy |
| `--color-sand` | `#f5f2de` | Zvýraznené karty (služby, kalkulačka, KPI v náhľade) |
| `--color-fill` | `rgba(0,0,0,.03)` až `.05` | Pozadie inputov, sekundárne plochy |
| `--color-border` / `--color-border-strong` | `rgba(0,0,0,.08)` / `rgba(0,0,0,.12)` | Oddeľovače, okraje |
| `--color-text` | `#000000` | Text, nadpisy |
| `--color-text-muted` | `#666666` (5,6 : 1 na `#fdfdf7`) | Podnadpisy, popisy |
| `--color-primary` / `-hover` / `-active` | `#0072c0` / `#005a99` / `#00548c` | Hlavné tlačidlo, odkazy |
| `--color-accent` | `#0096f7` | Ikony, bodky, čiara „Skutočná“ v grafe, focus ring. **Nie na text** (kontrast ~3 : 1) |
| `--color-secondary` | `#e7e3e1` | Sekundárne tlačidlo |
| Tón `success` | pozadie `#e4ecdf`, text `#2c4229`, bodka `#375033` | „V prevádzke“, „Prijatá“ |
| Tón `info` | `#e1f0fc` / `#00548c` | „Automatická detekcia“, rozpracované stavy |
| Tón `error` | `#ffe4d9` / `#a32900`; chyba poľa: okraj `#b83200`, pozadie `#fff6f2` | „Vysoká priorita“, „Nový“ tiket, validácia |
| Podvýroba (tmavý graf) | `#ff4000`, `#ff6a33`, `rgba(255,64,0,.45)` | Zvýraznené pásmo podvýroby |
| Tmavý pás | `#000000`, karta `#141414` | Sekcia Monitoring na landing page |
| Tieň karty / vyvýšený | `rgba(0,0,0,.12) 0 .5px 2px` / `rgba(0,0,0,.08) 0 2px 10px` | Karty, rozbaľovacie menu |
| Focus | `2px solid #0096f7`, offset 3 px | Všetky ovládacie prvky |
| Dotyková plocha | min. 44 px (mobil 48 px) | Tlačidlá, položky menu |

Typografia: telo 15–16 px, popisky 12–14 px, nadpis stránky v aplikácii ~30 px/300, nadpisy landing page `clamp(40px, 5vw, 64px)` až `clamp(48px, 7.2vw, 104px)`, letter-spacing −0,041 em.

Tóny badge: dnes existujú `''` (neutrálny), `success`, `info`, `error`. Wireframe má navyše „vyžaduje akciu“ (obrys) a „neaktívne“ (prerušovaný obrys). Navrhujem doplniť `attention` a `muted`, ostatné stavy namapovať na existujúce tóny (tabuľka stavov v `frontend-plan.md`, časť 2).

---

## 3. Rozhodnutia pred začatím (Fáza 0)

Tieto body treba odsúhlasiť v tíme, ostatné fázy na nich stoja. Pri každom je odporúčanie.

1. **Vizuál aplikácie = landing page, rozloženie = wireframe.** *Odporúčané.* Alternatíva je držať sa wireframu doslova (čierne pill tlačidlá, sivá), ale potom by landing a aplikácia vyzerali ako dva produkty.
2. **Routing:** landing page na `/` pre neprihláseného, Prehľad sa presunie na `/app`. Prihlásený používateľ, ktorý otvorí `/`, ide na `/app`; po prihlásení sa vracia na `from`, inak na `/app`. Ostatné adresy (`/customers`, `/tickets` …) sa nemenia. *Odporúčané* oproti vetveniu na `/` (to by miešalo verejný a chránený layout v jednej route).
3. **Písmo:** Plus Jakarta Sans (licencia OFL) ako `woff2` priamo v repe (`frontend/src/assets/fonts/`) cez `@font-face`. Bez novej npm závislosti a bez volania Google Fonts (súkromie, CSP). *Odporúčané.*
4. **Grafy:** ADR spike #224 vybral Recharts, ale dnešné grafy sú vlastné SVG (`ProductionChart.tsx`) a fungujú vrátane ovládania klávesnicou. Pre redizajn stačí vlastné SVG prerobiť na plochu + čiaru + pásmo podvýroby. Pridanie Recharts je nová závislosť a podľa CLAUDE.md potrebuje súhlas. *Odporúčané: zostať pri vlastnom SVG, rozpor s ADR zapísať do „Na zváženie“.*
5. **Prvky z dizajnu bez backendu sa nezobrazujú** (nie sú len sivé), rovnako ako role. Zoznam je v časti 5. Pre každý vznikne samostatné backend issue.
6. **Názvy a skupiny v menu** podľa wireframu (časť 4, Fáza 2), ale moduly ostávajú tak, ako ich rozdeľuje backend (Návrhy, Ponuky a Zmluvy zostávajú samostatné položky).

Výstup fázy: tento dokument schválený, rozdelený na issues, a `frontend/DESIGN.md` s tokenmi z časti 2 (na `DESIGN.md` sa odvoláva už `frontend-plan.md`).

---

## 4. Fázy

Každá odrážka „PR“ je samostatné issue a draft PR podľa CLAUDE.md (vetva `sprint<N>/<NN>-<issue>-<slug>`). Po každom PR musí prejsť `pnpm lint && pnpm build && pnpm test`. Poradie je zvolené tak, aby aplikácia bola po každom merge použiteľná.

### Fáza 1: Základ (tokeny a komponenty)

Cieľ: celá aplikácia je svetlá, s novým písmom a novými komponentmi. Rozloženie obrazoviek sa ešte nemení.

- **PR 1.1 Tokeny a téma.** Prepísať `:root` v `index.css` podľa časti 2, `color-scheme: light`, `@font-face` pre Plus Jakarta Sans, prepísať natvrdo zadané farby v `connection.css`, `auth.css`, `monitoring.css`. Overiť kontrast WCAG AA (text `#666` a `#0072c0` na krémovej aj bielej).
- **PR 1.2 Základné komponenty podľa K-01 až K-10.** Tlačidlá (primárne modré, sekundárne `#e7e3e1`, ghost, nebezpečné, malé, loading), inputy (výplň `--color-fill`, chybový stav `#b83200`/`#fff6f2`, súhrn chýb nad formulárom), badge s bodkou a tónmi `attention`/`muted`, karta, dialóg, potvrdzovací dialóg s ikonou, toast (tmavý pruh dole), skeleton, prázdny stav s ikonou a akciou.
- **PR 1.3 Nové zdieľané komponenty** v `frontend/src/components/`:
  - `SegmentedControl` (prepínač Kanban/Zoznam, Deň/Týždeň/Mesiac/Rok, záložky s počtom),
  - `Stepper` (zovšeobecniť `ConnectionStepper`; použije sa pri ponuke, inštalácii a v portáli),
  - `KpiCard` (ikona, hodnota, popis, voliteľne trend `+8 %` alebo cieľ s progress barom),
  - `ProgressBar` (lehota distribučky, SLA, plnenie výroby),
  - `KeyValueList` (`<dl>` pre „Kontaktné údaje“, „Realizácia“, „Detaily“),
  - `Avatar` (iniciály), `ActivityTimeline` (aktivita zákazníka, história tiketu),
  - `Toolbar` nad tabuľkou (vyhľadávanie, filtre ako „chip“ tlačidlá), `Pager` s číslami strán.
- **PR 1.4 (voliteľné) Ukážka komponentov** na `/dev/ui`, dostupná len v `import.meta.env.DEV`. Zodpovedá obrazovke „Knižnica komponentov“ z wireframu a uľahčí review ďalších PR.

Pozor na testy: 5 testov sa viaže na CSS triedy (`.skeleton`, `.mono`, `.quote-meta .badge`), tieto triedy zachovať.

### Fáza 2: Obal aplikácie (shell a navigácia)

- **PR 2.1 Sidebar.** Šírka 248 px, zbaliteľný na 68 px (stav v `localStorage`), logo s podtitulom „{rola} · riadiace centrum“, skupiny a aktívna položka vyplnená akcentom. Nové skupiny v `auth/navigation.ts`:
  - **Prehľad:** Prehľad (`/app`); Zákazník: Moje elektrárne
  - **Obchod:** Zákazníci, Návrhy FVE, Ponuky, Zmluvy, Pripojenie k distribučke (nová položka, Fáza 5)
  - **Realizácia:** Inštalácie, Monitoring výroby
  - **Servis:** Servisné tikety, Moje výjazdy (nová položka pre Technika, Fáza 7)
  - **Systém:** Používatelia a roly, Katalóg komponentov
- **PR 2.2 Horná lišta.** Menu používateľa (avatar s iniciálami, meno, rola, odhlásenie) presunuté zo spodku sidebaru. Vyhľadávanie `⌘K` zatiaľ len ako rýchly skok na moduly dostupné pre rolu (bez backendu). Zvonček notifikácií sa nezobrazí, kým nebude endpoint.
- **PR 2.3 Obsah a hlavička stránky.** Obsah v zaoblenom paneli, spoločný komponent `PageHeader` (breadcrumbs, nadpis, stavový badge, podnadpis, sekundárna a jedna hlavná akcia vpravo), mobil: sidebar ako drawer.
- **PR 2.4 Routing.** Presun Prehľadu na `/app` podľa rozhodnutia 2 (`App.tsx`, `NAVIGATION`, `RequireAuth`, breadcrumbs a ich testy).

Voliteľné (P2): počítadlá v menu (248, 18 …) a widget „Výroba dnes“ dole v sidebari. Oba potrebujú agregačný endpoint (časť 5).

### Fáza 3: Verejná časť (landing page a prihlásenie)

- **PR 3.1 Landing page** `features/landing/` na `/`, načítaná cez `React.lazy`, aby nezaťažila aplikáciu. Sekcie presne podľa `Main.dc.html`: horná lišta (sticky, blur, hamburger pod 860 px), hero s ikonami v nadpise a náhľadom „Moja elektráreň“ v rámiku zariadenia, pás čísel (s poznámkou „Ilustratívne údaje“), 3 karty služieb, 7 krokov „Ako to funguje“ (na mobile zvislo), tmavý pás Monitoring s grafom podvýroby a ukážkou tiketu, náhľad zákazníckeho portálu, FAQ (`<details>`), pätička. Statické ukážkové dáta v `landingData.ts`.
- **PR 3.2 Kalkulačka.** Výkon (posuvník), orientácia (Juh / Východ–západ, koeficient 0,86), kraj (koeficienty z návrhu, napr. BA 1100, PO 1030 kWh/kWp), výstup ročnej výroby a úspory (60 % vlastná spotreba, 0,20 €/kWh) s disclaimerom. Čistá funkcia s unit testom.
- **PR 3.3 Formulár dopytu.** Stavy prázdny / chyba / odoslaný podľa návrhu. Kým nebude anonymný endpoint (časť 5), sekcia ukazuje výzvu „Vytvoriť účet“ a formulár je za prepínačom ako `ANOMALIES_API_ENABLED`.
- **PR 3.4 Prihlásenie, registrácia, zabudnuté a nové heslo.** Rozloženie z wireframu: vľavo formulár s prepínačom Prihlásenie/Registrácia, vpravo 5 krokov životného cyklu FVE. „Prihlásiť sa firemným účtom (SSO)“ a „Zapamätať si ma“ vynechať (backend ich nemá).

### Fáza 4: Prehľad a zákaznícky portál

- **PR 4.1 Dashboard (interné role).** Hlavička „Dashboard · Prehľad za dnes · {dátum}“, rad KPI kariet podľa roly (zachovať logiku `useSource`, ktorá skryje kartu pri chýbajúcej role alebo 403), hlavný blok 2/3 a bočný 1/3. Bočný blok „Posledné alarmy“ zatiaľ plnia tikety s pôvodom „Automatická detekcia“. Graf „Výroba v čase“ (14 dní / 30 dní / 12 mesiacov) až s agregačným endpointom, dovtedy sa na jeho mieste ukáže posledná aktivita (dnešný `pulse`).
- **PR 4.2 Portál zákazníka** (`/my-plants` a detail). „Dobrý deň, {meno}“, KPI (výroba dnes, mesiac, výkon teraz, otvorené tikety), karty elektrární so stavom a aktuálnym výkonom, graf vlastnej výroby, tabuľka „Moje tikety“, hlavná akcia „Nahlásiť poruchu“. Ak elektráreň ešte nie je v prevádzke, namiesto grafu `Stepper` montáže (ako na landing page: „Krok 6 zo 7“).

### Fáza 5: Obchod

- **PR 5.1 Zákazníci, zoznam.** `Toolbar` (hľadať podľa mena, IČO, e-mailu, EIC), záložky so stavmi a počtami podľa `CustomerStatus`, avatar s iniciálami v prvom stĺpci, `Pager`.
- **PR 5.2 Zákazník, detail.** Hlavička s badge stavu, `SegmentedControl` Prehľad / Odberné miesta / Ponuky / Zmluvy / Inštalácie s počtami. Prehľad: Kontaktné údaje (`KeyValueList`), súhrnné dlaždice (klik prepne záložku), „Nasledujúci krok“, Aktivita. Pole „Obchodník“, „Zdroj“ a „Súhlas GDPR“ zobraziť, len ak ich backend vracia.
- **PR 5.3 Ponuka, detail.** Stepper „Priebeh ponuky“ zo skutočných stavov (Rozpracovaná → Odoslaná → Prijatá → Zmluva; Zamietnutá/Expirovaná ako koncový stav), dlaždice parametrov návrhu FVE, tabuľka Rozpočet so súčtami a DPH, vpravo karta Zmluvy (PDF, podpis) a karta zákazníka. Blok „Schvaľovanie návrhu“ z wireframu sa nerobí, backend nemá interné schvaľovanie.
- **PR 5.4 Návrhy FVE, Zmluvy, Katalóg.** Rovnaké vzory (PageHeader, Toolbar, KeyValueList, Stepper verzií), bez zmeny logiky.
- **PR 5.5 Pripojenie k distribučke, nový zoznam** `/connection-requests` nad existujúcim `GET /api/connection-requests`: KPI (otvorené, blíži sa lehota, po lehote, priemerná doba), záložky, stĺpec „Lehota“ ako `ProgressBar` (po lehote tmavý). Detail už existuje, len sa prestyluje.

### Fáza 6: Realizácia a monitoring

- **PR 6.1 Inštalácie, zoznam a detail.** Detail: `Stepper` „Workflow inštalácie“. Backend má 10 stavov, wireframe 6 krokov, preto mapovanie: Naplánovaná (Naplánovaná, Komponenty objednané/prijaté) → Montáž (Montáž, Montáž dokončená) → Revízia (vrátane „nevyhovela“ ako chybový stav kroku) → Pripojenie DS → Uvedenie do prevádzky → Odovzdaná. Karty Panely / Striedače / Batéria s počtom sériových čísel, karta Realizácia, tabuľka Stringy (stále za prepínačom PR #342). Hlavná akcia „Posunúť do ďalšej fázy“ volá existujúce prechody.
- **PR 6.2 Monitoring výroby.** Filtrovací pás (inštalácia, Deň/Týždeň/Mesiac/Rok, rozsah dátumov, prepínač „Zvýrazniť podvýrobu“), 4 KPI (skutočná, očakávaná, odchýlka, a namiesto Performance ratio počet období podvýroby, kým nebude PR z backendu), graf skutočná vs. očakávaná so šrafovaným pásmom podvýroby, „chipy“ období podvýroby (klik otvorí nový tiket s predvyplnenou inštaláciou), tabuľka alarmov za existujúcim prepínačom `ANOMALIES_API_ENABLED`.
- **PR 6.3 Komponent grafu.** Spoločný `ProductionChart` v `components/charts/` (podľa ADR #224, bod 5): plocha + čiara + pásmo podvýroby, svetlá aj tmavá varianta (tmavá pre landing page). Zachovať ovládanie klávesnicou a tabuľkovú alternatívu.

### Fáza 7: Servis a technik

- **PR 7.1 Servisné tikety, zoznam.** Kanban už existuje, prestylovať karty (číslo, predmet, inštalácia, badge priority, iniciály technika), `SegmentedControl` Kanban/Zoznam, filtre Technik a Priorita. Stĺpce zostávajú podľa 8 stavov backendu (wireframe má 5 ukážkových).
- **PR 7.2 Tiket, detail.** Dva stĺpce: vľavo Popis problému s prílohami, Výjazdy (`GET /api/tickets/{id}/visits`), Servisný protokol (z reportu výjazdu: príchod, odchod, zistenie, vykonané práce, materiál), Komentáre; vpravo Detaily (stav, priorita, pôvod), Priradený technik ako výber z kariet (`GET /api/technicians`, `PUT /technician`), karta inštalácie. SLA sa nezobrazuje (backend ho nemá).
- **PR 7.3 Moje výjazdy (Technik), nová stránka** `/my-visits`, mobile-first podľa obrazoviek 9a–9c: zoznam výjazdov Dnes/Zajtra/Týždeň (`GET /api/technicians/me/visits`), detail výjazdu (zákazník, „Zavolať“ ako `tel:`, „Navigovať“ ako odkaz do máp, zariadenie, kontrolný zoznam), formulár protokolu (`POST /api/visits/{id}/start` a `/report`). Hlavná akcia vždy dole na dosah palca. Spodná navigácia pre mobil len na tejto stránke. Fotodokumentácia a podpis zákazníka vynechať, kým ich backend nepodporuje.

### Fáza 8: Dočistenie

- **PR 8.1 Administrácia.** Používatelia a roly: tabuľka s avatarom a e-mailom, záložky podľa stavu, priradenie rolí v dialógu. Stĺpec 2FA vynechať.
- **PR 8.2 403 a 404** v novom štýle.
- **PR 8.3 Prístupnosť a responzivita.** Kontrola kontrastu všetkých tónov, focus, skip-link, klávesnica (kanban, segmentové prepínače, dialógy), zalomenie tabuliek na mobile (`data-table__scroll`), `prefers-reduced-motion`.
- **PR 8.4 Odstránenie starej témy.** Zmazať nepoužívané tokeny a triedy tmavej témy, aktualizovať `frontend/DESIGN.md` a README (snímky obrazoviek).

---

## 5. Prvky z dizajnu, ktoré backend zatiaľ nepodporuje

| Prvok v dizajne | Stav backendu | Postup vo frontende | Backend issue |
|---|---|---|---|
| Formulár „Nezáväzný dopyt“ (landing) | Chýba anonymný endpoint | Výzva „Vytvoriť účet“, formulár za prepínačom | `POST /api/inquiries` (anonymný, rate limit, vznikne potenciálny zákazník) |
| Graf výroby celej flotily, widget „Výroba dnes“, počítadlá v menu | Výroba je len per inštalácia (`/api/installations/{id}/production`) | Nezobrazovať; na dashboarde posledná aktivita | `GET /api/dashboard/summary` |
| Alarmy a anomálie | Chýba endpoint (`ANOMALIES_API_ENABLED = false`) | Tikety s pôvodom „Automatická detekcia“ | API pre anomálie (#251, #252) |
| Notifikácie (zvonček) | Chýba | Nezobrazovať | Voliteľné, P2 |
| Globálne vyhľadávanie | Chýba | Len skok na moduly | Voliteľné, P2 |
| Interné schvaľovanie ponuky | Chýba (stavy Draft, Sent, Accepted, Rejected, Expired) | Stepper zo skutočných stavov | Len ak o to tím stojí |
| SLA tiketu | Chýba | Nezobrazovať | Voliteľné |
| Performance ratio | Nepočíta sa | Namiesto neho počet období podvýroby | Voliteľné (ML) |
| SSO, „Zapamätať si ma“, 2FA | Chýba | Vynechať | Nie |
| Fotky a podpis v servisnom protokole | Report má len časy, zistenie, práce, materiál | Vynechať | Voliteľné |

---

## 6. Riziká a na zváženie

- **Rozsah testov.** Zmena textov a skupín v menu rozbije testy navigácie a `App.test.tsx`; opravujú sa v tom istom PR. Testy hľadajú prvky hlavne podľa roly a textu, preto by zmena vzhľadu nemala inak vadiť.
- **Paralelná práca.** Fáza 1 a 2 menia zdieľané súbory (`index.css`, `AppShell`, `navigation.ts`), preto ich robí jeden človek za sebou. Fázy 4 až 7 sa dajú deliť medzi viac ľudí, lebo menia len vlastné `features/<modul>`.
- **Rozpor s ADR #224** (Recharts vs. vlastné SVG): zapísať do „Na zváženie“ v PR 6.3.
- **Ukážkové čísla na landing page** sú ilustratívne a musia mať poznámku (už je v návrhu).
- **Dizajn je v0.1.** Wireframe nemá návrh pre Návrhy FVE, Zmluvy, Katalóg a formuláre vytvárania; tie sa robia podľa vzorov z ostatných obrazoviek. Ak dizajnér dodá ďalšie obrazovky, doplnia sa do príslušnej fázy.

## 7. Odhad

| Fáza | PR | Náročnosť |
|---|---|---|
| 0 Rozhodnutia | – | malá (stretnutie + `DESIGN.md`) |
| 1 Základ | 3–4 | stredná, najväčší dopad na vzhľad |
| 2 Shell | 4 | stredná |
| 3 Verejná časť | 4 | stredná až väčšia (landing je dlhá) |
| 4 Prehľad a portál | 2 | stredná |
| 5 Obchod | 5 | väčšia |
| 6 Realizácia | 3 | stredná |
| 7 Servis | 3 | väčšia (nová stránka technika) |
| 8 Dočistenie | 4 | malá až stredná |

Po fázach 1 a 2 vyzerá aplikácia už „nová“ (farby, písmo, shell). Fázy 3 až 7 potom prerábajú jednotlivé obrazovky a dajú sa robiť v ľubovoľnom poradí podľa priorít sprintu.

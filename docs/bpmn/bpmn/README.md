# BPMN dokumentácia SolarOps

Procesné modely SolarOps (od dopytu zákazníka po servis a prediktívnu údržbu). Rieši story #186 (parent #56, epic #2). Podklad: `solarops-bpmn.drawio` (originál, needitovaný). `.bpmn` súbory vznikli automatickou konverziou z neho (pozri [Pôvod a obmedzenia](#pôvod-a-obmedzenia)).

## Súbory

| Úroveň | Súbor | PNG | Story |
|---|---|---|---|
| L1 | `L1-prehlad-kolaboracia.bpmn` | `png/L1-prehlad-kolaboracia.png` | Epic #2 |
| L2 | `L2-61-dopyt-a-ponuka.bpmn` | `png/L2-61-dopyt-a-ponuka.png` | #61 |
| L2 | `L2-62-navrh-fve.bpmn` | `png/L2-62-navrh-fve.png` | #62 |
| L2 | `L2-63-zmluva-a-zaloha.bpmn` | `png/L2-63-zmluva-a-zaloha.png` | #63 |
| L2 | `L2-64-pripojenie-k-distribucke.bpmn` | `png/L2-64-pripojenie-k-distribucke.png` | #64 |
| L2 | `L2-65-instalacia-a-reviza.bpmn` | `png/L2-65-instalacia-a-reviza.png` | #65 |
| L2 | `L2-66-uvedenie-do-prevadzky.bpmn` | `png/L2-66-uvedenie-do-prevadzky.png` | #66 |
| L2 | `L2-67-servis-z-hlasenia.bpmn` | `png/L2-67-servis-z-hlasenia.png` | #67 |
| L2 | `L2-68-automaticky-servis.bpmn` | `png/L2-68-automaticky-servis.png` | #68 |
| zdroj | `solarops-bpmn.drawio`, `solarops-bpmn.drawio.pdf` | | #205 |

Konvencie (z #205 / Sprint 0): L1 je kolaborácia, v ktorej Zákazník a Distribučka sú black-box pooly a SolarOps má lanes Obchod, Projektant, Technik. L2 je jeden proces na súbor; lanes sú len tie, ktoré daný proces používa. Správy medzi poolmi sú message flowy (čiarkované).

## Ako súbory otvoriť

- **Camunda Modeler** alebo **bpmn.io** (demo.bpmn.io): File > Open / drag and drop `.bpmn`.
- **draw.io**: File > Import from > Device (import BPMN XML je len približný, pre úpravy používaj Camunda Modeler).
- Pôvodný `.drawio` sa otvára priamo v draw.io; stránky: L1 + #61 až #68.
- Každý L2 proces sa volá `Process_<číslo story>` (napr. `Process_61`). Aktivity v L1 sú `callActivity` s `calledElement` na tieto ID; odkazy sú medzi súbormi, Modeler ich nerozlišuje, slúžia ako dokumentácia.

## Procesy a roly

Roly: **Obchod**, **Projektant**, **Technik** (lanes v SolarOps), **Zákazník**, **Distribučka**, **Detekčný systém** (black-box pooly mimo firmy). Pole „Vykonáva" = lane, v ktorom sú úlohy; „Komunikuje" = black-box pooly so správami.

| Proces | Vykonáva | Komunikuje (správy) | Trigger / výstup |
|---|---|---|---|
| L1 Prehľad | Obchod, Projektant, Technik | Zákazník (dopyt, ponuka, zmluva, protokol, hlásenie poruchy), Distribučka (žiadosť, zmluva o pripojení, oznámenie) | dopyt zákazníka / FVE v prevádzke |
| #61 Dopyt a ponuka | Obchod | Zákazník (dopyt, ponuka, schválenie/zamietnutie) | dopyt / ponuka prijatá alebo zamietnutá |
| #62 Návrh FVE | Obchod (podklady, schválenie, previazanie s ponukou), Projektant (obhliadka, parametre, návrh, schéma) | žiadne (vnútorný proces) | ponuka prijatá / návrh pripravený na zmluvu |
| #63 Zmluva a záloha | Obchod | Zákazník (zmluva, podpis, úhrada zálohy) | schválený návrh / zmluva podpísaná a záloha uhradená, alebo zanikla (timer) |
| #64 Pripojenie | Obchod (podklady, doplnenie, zmluva zákazníkovi), Projektant (žiadosť, komunikácia s distribučkou, kontrola podmienok) | Distribučka (žiadosť, rozhodnutie, doplnenie, podpísaná zmluva), Zákazník (zmluva na podpis) | zmluva a záloha prijaté / zmluva o pripojení uzavretá alebo zamietnutie |
| #65 Inštalácia | Obchod (plán, objednávka), Technik (prevzatie, montáž, revízia, náprava) | Zákazník (termín) | zmluva o pripojení uzavretá / revízna správa vydaná |
| #66 Uvedenie do prevádzky | Technik (funkčná skúška, spustenie, odovzdanie), Obchod (podklady k zmluve o dodávke) | Distribučka (funkčná skúška, elektromer, oznámenie), Zákazník (podklady, protokol) | revízia v poriadku / FVE v prevádzke, aktivácia monitoringu |
| #67 Servis z hlásenia | Obchod (prijatie, triedenie, diaľková diagnostika, uzavretie), Technik (výjazd, zásah, overenie výroby) | Zákazník (hlásenie, potvrdenie, termín, uzavretie) | hlásenie poruchy / tiket uzavretý |
| #68 Automatický servis | Obchod (kontrola upozornenia, založenie tiketu); podproces #67 | Detekčný systém (upozornenie, spätná väzba o falošnom poplachu), Zákazník (notifikácia) | upozornenie na podvýrobu / porucha odstránená alebo falošný poplach |

Poznámka: v #68 nie je lane Technik, servisný zásah je skrytý v call activity `Servisný proces (#67)`. Rola Admin (z #135) v BPMN nevystupuje.

## Sémantika použitých prvkov

- Message start/intermediate/end udalosti = príjem/odoslanie správy od black-box pool. Timer (#63) = lehota na podpis a splatnosť zálohy, za event-based bránou.
- Exclusive gateway = rozhodnutie; brány bez názvu (#64, #66) sú zlúčenia.
- #65: Montáž a Revízia sú zbalené podprocesy (kolapsnuté, bez vnútorného rozpadu) s chybovými hraničnými udalosťami (chýbajúci komponent, neúspešná revízia), ktoré vracajú tok na opravu.
- #66: koncová udalosť je message end (aktivácia monitoringu), textová anotácia odkazuje na #67 a #68.

## Mapovanie krokov na systém

Zdroj mapovania: názvy a rodičovské väzby issues v GitHube (`takacmichal/AISPU`) a navigácia podľa rolí z #227. **Zdrojový kód v tomto adresári nie je** (len `docs/` a `ml/`), preto nie sú uvedené konkrétne názvy endpointov, tried ani routov. Stav issues je k 28. 10. 2026 (zatvorené = hotové, otvorené = plánované). Backend = .NET API (Clean Architecture), UI = Angular + PrimeNG, ML = Python služba.

Feature issues: Zákazníci #18, Ponuky #19, Návrh #20, Zmluvy #21, Inštalácie #22, Pripojenie #23, Montáž #24, Revízia #25, Telemetria #26-#28, ML #30-#34, Tikety #35, Výjazdy #36, Zásah #37, Notifikácie #38, UI #226, #231, #236, #242, #248, #254, #261.

### #61 Dopyt a ponuka (epic #3)

| Krok | Modul (backend) | Obrazovka (UI) | Issues |
|---|---|---|---|
| Zaznamenať zákazníka a odberné miesto | Správa zákazníkov (#18) | Zoznam, formulár a detail zákazníka | #70, #71, #72 (hotové); UI #151, #232, #233, #234 |
| Zostaviť cenovú ponuku z komponentov | Cenové ponuky (#19), katalóg | Konfigurátor ponuky, katalóg komponentov | #73, #74 (hotové); UI #237, #238, #152 |
| Odoslať ponuku, sledovať stav (prijatá/zamietnutá/úprava) | Cenové ponuky | Konfigurátor (stavy ponuky) | #75 (hotové); UI #238 |

### #62 Návrh FVE

| Krok | Modul | Obrazovka | Issues |
|---|---|---|---|
| Zadať parametre strechy a spotreby | Technický návrh (#20) | Zadanie parametrov strechy a spotreby | #76 (hotové); UI #239 |
| Navrhnúť konfiguráciu FVE a odhad výroby | Technický návrh | Výsledok návrhu FVE | #77 (hotové); UI #240 |
| Schváliť návrh a previazať s ponukou | Technický návrh | Obrazovky ponuky a návrhu | #78; UI #152 |
| Jednopólová schéma, stringy a rozloženie panelov | Technický návrh | nie je explicitne | #84 (najbližšia story) |
| Obhliadka objektu, rozhodnutie o realizovateľnosti | **nenamapované**, v backlogu nie je samostatná story | | pozri Nenamapované |

### #63 Zmluva a záloha

| Krok | Modul | Obrazovka | Issues |
|---|---|---|---|
| Vytvoriť zmluvu z prijatej ponuky | Zmluvy (#21) | Zmluva: náhľad PDF, podpis, záloha | #79 (hotové); UI #241, #153 |
| Vygenerovať PDF zmluvy | Zmluvy | náhľad PDF | #80, #219 (spike) |
| Zaznamenať podpis a úhradu zálohy | Zmluvy | podpis a záloha | #81 (hotové); UI #241 |
| Zálohová faktúra, timer lehoty podpisu/splatnosti | **nenamapované** (pozri nižšie) | | |

### #64 Pripojenie k distribučke (epic #4)

| Krok | Modul | Obrazovka | Issues |
|---|---|---|---|
| Pripraviť podklady, vytvoriť žiadosť (malý/väčší zdroj) | Žiadosť o pripojenie (#23) | Stepper procesu pripojenia | #85 (hotové); UI #244, #153; spike #203, #217 |
| Sledovať stav a lehoty (10 / 30 dní) | Žiadosť o pripojenie | Stepper | #86; UI #244 |
| Rozhodnutie distribučky (simulácia) | Žiadosť o pripojenie | Stepper | #87 |
| Zmluva o pripojení: odoslať zákazníkovi, podpis, odoslať distribučke | Žiadosť o pripojenie / Zmluvy | Obrazovky zmlúv a žiadosti o pripojenie | #153, #244; presné krok-na-stav mapovanie nie je v issues |

### #65 Inštalácia a revízia

| Krok | Modul | Obrazovka | Issues |
|---|---|---|---|
| Naplánovať inštaláciu a termín, pridelenie technikov | Plánovanie inštalácie (#24) | Plánovanie inštalácie a pridelenie technikov | #88, #89; UI #245 |
| Objednať komponenty | **nenamapované** | | |
| Prevziať panely a invertory (sériové čísla) | Evidencia inštalácií (#22) | Detail elektrárne s panelmi a stringmi | #82, #83 (hotové); UI #243 |
| Montáž (checklist) | Plánovanie a realizácia (#24) | Checklist montáže | #90; UI #246 |
| Revízia, revízna správa | Revízia (#25) | Checklist montáže a revízna správa | #91; UI #246 |
| Výmena/doobjednanie komponentu, odstránenie nedostatkov | Evidencia zariadení (#22) | Detail elektrárne | čiastočne #83; stav „revízia neúspešná" nie je explicitne v issues |

### #66 Uvedenie do prevádzky

| Krok | Modul | Obrazovka | Issues |
|---|---|---|---|
| Funkčná skúška, spustenie FVE, overenie výroby | Revízia a uvedenie do prevádzky (#25) | Uvedenie do prevádzky | #92; UI #247 |
| Odovzdať FVE a preberací protokol zákazníkovi | Revízia a uvedenie do prevádzky | Odovzdanie dokumentácie | #93; UI #247 |
| Aktivovať monitoring (koncová udalosť) | Telemetria (#26-#28), príjem dát | Dashboard výroby (#248) | #95, #96, #97; #94 simulátor (hotové) |
| Podklady k zmluve o dodávke, oznámenie distribučky, elektromer | **nenamapované** | | |

### #67 Servis z hlásenia (epic #8)

| Krok | Modul | Obrazovka | Issues |
|---|---|---|---|
| Prijať hlásenie a vytvoriť tiket | Správa servisných tiketov (#35) | Formulár nahlásenia poruchy a ručného tiketu | #120; UI #255 |
| Roztriediť tiket (typ, priorita), stavy | Tikety | Zoznam/detail tiketov, Kanban | #121, #122; UI #154, #256, #257 |
| Diagnostikovať na diaľku | Tikety + dashboard výroby | Detail tiketu, grafy výroby | komentáre #123; UI #258, #148 |
| Naplánovať výjazd, pridelenie technika | Plánovanie výjazdov (#36) | Kalendár výjazdov, Moje výjazdy | #124, #125, #126; UI #259, #155, #263 |
| Opraviť/vymeniť komponent, zapísať zásah | Zásah technika (#37) | Servisný protokol (desktop aj mobil) | #127, #128; UI #156, #264 |
| Overiť obnovenie výroby | Prehľady výroby (#28) | Dashboard výroby | #100; UI #249 |
| Uzavrieť tiket, informovať zákazníka | Tikety, Notifikácie (#38) | Detail tiketu, notifikácie | #129, #130, #131; UI #266, #271 |

### #68 Automatický servis (epic #7)

| Krok | Modul | Obrazovka | Issues |
|---|---|---|---|
| Upozornenie na podvýrobu (výroba < 85 % očakávanej) | Detekcia podvýroby (#32), predikcia (#30, #31) | Zoznam anomálií a alertov | #112, #113, #114, #194 (prah); UI #251, #252 |
| Preveriť upozornenie (dáta, počasie, výpadok) | Detekcia podvýroby | Detail anomálie | #114; UI #252 |
| Vytvoriť servisný tiket | Automatické založenie tiketu (#33) | Detail anomálie s odkazom na tiket | #115, #116, #117; UI #252 |
| Falošný poplach, label pre ML | Vyhodnotenie a ladenie detekcie (#34) | Spätná väzba technika k alarmu | #119, #118; UI #260 |
| Servisný proces (#67) | pozri #67 | | |

### Nenamapované alebo neisté kroky

Pre tieto kroky som v backlogu nenašiel zodpovedajúcu story; ide o kandidátov na doplnenie alebo vedomé vynechanie z rozsahu (#69 vyžaduje odôvodnenie):

- obhliadka objektu a rozhodnutie „Realizovateľné?" (#62),
- zálohová faktúra a timery lehoty podpisu/splatnosti (#63),
- objednávka komponentov (#65),
- podklady k zmluve o dodávke, montáž elektromera (#66, externý krok distribučky),
- prechody stavov entít (ponuka, zmluva, žiadosť, tiket) a matica rolí BPMN na roly aplikácie: spike #207 je v backlogu otvorený, výstup (ADR a matica) som nenašiel. Z #135 sú roly aplikácie Obchod, Projektant, Technik, Zákazník, Admin.

## Pôvod a obmedzenia

- Konverzia z `.drawio` je skript (jednorazový, mimo repozitára). Prvky sa mapujú: štart/koniec/medziľahlé udalosti podľa `outline`+`symbol`, brány `gateway2` na exclusive (event-based pri `catching`+`multiple`), `taskMarker`/`isLoopSub` na zbalený podproces, `bpmnShapeType=call` na call activity, čiarkované hrany na message flowy. Rozloženie (BPMNDI) preberá súradnice z drawio; trasy hrán bez explicitných bodov sú dopočítané, v 5 hranách (1x #61, 4x #65) ručne upravené.
- Všetky úlohy sú generické `task` (nerozlišuje user/service task). Brány nemajú podmienkové výrazy, len popisky vetiev. Procesy sú `isExecutable="false"`.
- V drawio mali na strane #65 dve hrany a dva uzly rovnaké ID (`p5-e1`, `p5-e2`); v `.bpmn` sú uzly premenované (boundary udalosti `BoundaryEvent_65_b1/b2`). V originálnom `.drawio` som to neopravoval.
- Overené: XML je well-formed (`xmllint`), načíta sa bez varovaní v `bpmn-moddle`, každý prvok má BPMNDI tvar, súbory sa vykreslia v bpmn-js (z toho PNG; obsahujú bpmn.io logo). V Camunda Modeler som ich neotvoril.
- Doménové hodnoty v diagramoch (hranica malého zdroja 10,8 kW, lehoty 10/30 pracovných dní, 5 pracovných dní na elektromer) preberá model z drawio. Sprint 0 poznámky ich označujú ako neoverené (definícia malého zdroja 10 / 10,8 / 11 kW, funkčné skúšky, poplatky), preto ich netreba brať ako právne správne.
- PDF `solarops-bpmn.drawio.pdf` som obsahovo neporovnával s XML (chýba nástroj na vykreslenie); za zdroj pravdy som bral `.drawio`.

> Poznámka: mapovanie na entity, stavy a endpointy (#69) je v [`../mapovanie-entit.md`](../mapovanie-entit.md); tabuľky nižšie vychádzajú len z názvov issues, kroky označené „nenamapované“ treba skontrolovať proti nemu.

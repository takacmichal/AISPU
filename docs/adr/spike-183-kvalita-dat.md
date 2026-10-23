# ADR spike #183: Kvalita dát, chýbajúce hodnoty a licencie (DKASC)

- **Stav:** Navrhnuté (prepracované pre DKASC po rozhodnutí tímu v #178/#180)
- **Dátum:** 2026-10-23
- **Issue:** #183 (nadradené #5)
- **Súvisí:** #178 (dataset), #180 (počasie), #189 (join), #187 (poruchy), #220 (časové rady)

## Kontext

Tím rozhodol (komentáre v #178 a #180), že produkčný dataset je **DKA Solar Centre (DKASC), Alice Springs**, roky 2023 a 2024: záznamy po 5 minút z ~49 meračov jednotlivých FV polí, 2 hlavných meračov areálu a meteostanice, s metadátami poľa (výkon, technológia, sklon, azimut). Počasie: Open-Meteo Historical Forecast API na tréning a Forecast API v prevádzke. PVDAQ a Kaggle už **nie sú primárny dataset** (zostávajú len ako vedľajšia referencia, napr. pôvodné profilovanie nižšie).

Dáta idú do ML služby (Python 3.12, pandas), čas sa drží v UTC (ADR #189). Hodinový agregát je rozhodnutý v #189 a **#189 nahrádza zmienku o 15-minútovej agregácii v #178**: model aj join bežia na hodinách, 5 minút je len vstup do agregácie.

### Čo je overené o DKASC (stav k 2026-10-23)

| Zistenie | Zdroj | Stav |
|---|---|---|
| Stránka download uvádza polia typu `1A Trina, 10.5kW, mono-Si, Dual, 2009`, `3 BP Solar, 5.0kW, poly-Si, Fixed, 2008, Roof Mounted`; teda výkon, technológia, typ sledovania (Fixed/Single/Dual) a rok inštalácie sú v názve poľa. Na stránke je ~44 aktívnych polí s číslom plus ukončené polia; "~49 meračov" z #178 sa teda nedá z tejto stránky presne potvrdiť | dkasolarcentre.com.au/download?location=alice-springs | overené (počet neoverený) |
| Ročné súbory "All Individual Technologies (yearly data set with weather data)" pre 2008 až 2026 a súbor "Combined Output" | tá istá stránka | overené |
| Pred stiahnutím sa treba zaviazať k T&C a uviesť rolu a účel použitia | tá istá stránka | overené |
| Glossary: `5-Min-Avg kW` = AC výkon v **kW**, priemer z 1 s hodnôt za 5 min (meter ION6200, trieda 0,5, ±0,5 %); hlavný meter areálu `5-Min-Avg kWtot` / `kWa,b,c` v kW (ION7550, trieda 0,2) | dkasolarcentre.com.au/glossary | overené |
| Glossary: PR sa počíta len pre fixné polia s POA pyranometrom od mája 2015 | glossary | overené |
| Čas v CSV (časové pásmo, či je značka začiatok alebo koniec 5-min intervalu): v glossary, T&C ani Notes on the Data **nie je uvedené** | prehľadané glossary, T&C, notes, download | **neoverené** (očakáva sa Australia/Darwin, UTC+9:30 bez DST; overí sa testom posunu, ADR #189) |
| Notes on the Data: výpadky webu/downloadu (okt. 2023 re-sync CSV, jan. 2023 odstávka siete 24.-30. 1. 2023, výmena invertorov 21 v jul. 2023) | notes-on-the-data | overené |
| Poruchy z #178 (M19 C od 2024-02-05, M15 C Phase II január až apríl 2024), ~11 chýbajúcich dní v 2023, prázdny stĺpec rýchlosti vetra, merače bez dát | komentár v #178 (tím) | prevzaté od tímu, **nepreverené nami**; označenie "M19 C" neodpovedá číslovaniu polí na stránke, mapovanie na stĺpce CSV treba urobiť pri profilovaní |
| Sklon a azimut: na stránke sú len typ sledovania a pri 16A-D svetová strana; číselný sklon/azimut tam nie sú | download | **neoverené** (zdroj metadát sklonu/azimutu doplniť; sledujúce polia Single/Dual nemajú pevný sklon/azimut) |

Open-Meteo (Alice Springs, dotaz 2024-01-01 až 2024-12-31): 8784 hodín bez prázdnych hodnôt (GHI, DNI, DHI, teplota, vietor), vráti bod mriežky -23,796/133,858, výška 559 m; detail v ADR #189.

## Zvažované možnosti (čistenie chýbajúcich hodnôt)

| Možnosť | Výhody | Nevýhody |
|---|---|---|
| A. Vyradiť všetky riadky s chýbaním | jednoduché, bez umelých dát | strata dát, diery v časových oknách |
| B. Interpolovať všetko (lineárne) | súvislý rad | vymyslí cieľovú premennú (výkon), skreslí metriky, "zahladí" výpadky, ktoré chceme detegovať (M19 C, M15 C) |
| C. Hybrid: označiť (`is_missing`), cieľ nikdy neinterpolovať, krátke diery len vo vstupoch | zachová pravdivosť cieľa, výpadok je informácia | zložitejšie pravidlá |

## Rozhodnutie

Možnosť **C**. Konkrétne pravidlá (konfigurovateľné konštanty v `ml/`). Kľúč dát je `(meter_id, timestamp_utc)`.

### Pravidlá na úrovni záznamu (5 min)

1. **Duplicity:** pri duplicite `(meter_id, timestamp_utc)` ponechať posledný záznam a počítať ich v reporte.
2. **Časová os:** DKASC čas sa konvertuje na UTC pásmom `Australia/Darwin` (parametrizované, kým sa nepotvrdí). Overenie per stiahnutý súbor: čas dennej špičky výkonu / vzostupu žiarenia vs. solárne poludnie z `pvlib` na ±30 min (postup v ADR #189). Konvenciu značky intervalu (začiatok/koniec) rieši #189; do overenia je `neoverené`.
3. **Jednotky (explicitne):** DKASC dáva výkon v **kW** (viď glossary). Pri načítaní sa hneď prevedie `power_w = power_kw * 1000` a stĺpec sa pomenuje s jednotkou (`ac_power_w`). Kontrola rádu: `max(power_w)` musí byť v rozsahu 0,3 až 1,3 × menovitého výkonu poľa v **W** (kW z názvu poľa × 1000); mimo rozsahu = podozrenie na zlú jednotku, meter sa vyradí do ručnej kontroly. Do ML ide `power_w / (kWp * 1000)`, teda bezrozmerný pomer (W/Wp, ekvivalent kW/kWp). Žiarenie W/m², teplota °C. Open-Meteo vždy s `wind_speed_unit=ms` a `timezone=UTC`.
4. **Noc:** pri výške Slnka < 0° (`pvlib`) sa výkon v rozsahu ±1 % menovitého výkonu nastaví na 0 (offset); väčšia nenulová hodnota v noci = `night_nonzero`, riadok vyradiť.
5. **Outliery:** výkon < -1 % alebo > 120 % menovitého výkonu poľa = vyradiť; záporné žiarenie orezať na 0; GHI nad 1,5 × clear-sky GHI len flag (cloud enhancement môže byť reálny).
6. **Zamrznutá hodnota (`stuck`) definovaná v minútach:** nenulový výkon s **identickou** hodnotou (v rozlíšení merača) po dobu **>= 30 minút** (teda >= 6 po sebe idúcich 5-min vzoriek; pravidlo je v minútach, takže platí nezávisle od rozlíšenia) sa označí `stuck` a vyradí. **Výnimka (clipping):** vzorky, v ktorých je výkon >= **95 %** limitu AC (strop invertora; `ac_limit_w`), sa na `stuck` nekontrolujú, lebo orezaný výkon je pri plnom slnku legitímne konštantný. Limit AC: ak je známy rating invertora, použije sa ten; inak náhrada = menovitý výkon poľa v kW z názvu (**neoverené**, DKASC AC limit per pole neuvádza) a navyše empirický strop = 99. percentil výkonu poľa v dennej hodine. Výnimka sa vzťahuje aj na hodiny, kde je hodinový priemer >= 95 % limitu.

### Pravidlá na úrovni hodiny a medzier

7. **Hodinový platný agregát (#189):** hodina je platná, ak je prítomných aspoň 75 % vzoriek (9 z 12 pri 5 min), inak `NaN` a v tréningu sa vynechá. Výkon (cieľ) sa **neinterpoluje**. Počasie (vstup) sa smie lineárne doplniť len pri dierach <= 2 h.
8. **Medzery 2023:** chýba ~11 dní (tím, neoverené počtom). Tieto dni sa nedopĺňajú a nedelia dátum medzi train/val/test (split po celých dňoch, aby diera nespôsobila únik). Notes on the Data hlásia v 2023 odstávky (24.-30. 1. 2023 a re-sync CSV v októbri 2023), pri profilovaní sa porovná, či diery zodpovedajú.
9. **Stĺpec rýchlosti vetra:** prázdny (podľa tímu) = vylúčený z dát DKASC; vietor sa berie z Open-Meteo (`wind_speed_10m`). Všeobecne: stĺpec s > 95 % `NaN` za rok sa vyradí a zapíše do reportu.

### Pravidlá per merač (kvalita merača)

10. **Merač bez dát:** merač, ktorý má za 2023 aj 2024 menej ako 50 % platných denných hodín, alebo je jeho stĺpec úplne prázdny, sa vylúči z datasetu (v súlade s tímom: "tieto merače a stĺpce vylúčime").
11. **Mŕtvy / nízky merač (okno):** v okne 14 dní, kde je podiel denných hodín (GHI > 200 W/m²) s výkonom < 2 % menovitého výkonu **> 95 %**, je merač v stave `dead_window`. Merač s mediánom dennej normalizovanej výroby (kWh/kWp) pod 50 % mediánu ostatných polí rovnakej technológie a typu sledovania je `low_output` (prahy sú návrh). Merače v `dead_window` / `low_output` sa **nepoužijú na tréning modelu očakávanej výroby**.
12. **Známe poruchy sú testovacie dáta, nie šum:** M19 C (bez výroby od 2024-02-05) a M15 C Phase II (takmer bez výroby január až apríl 2024) sa označia `known_fault` s intervalom, vynechajú z tréningu a použijú sa **iba ako overovacia množina** pre detekciu porúch (#187). Pri reporte (T&C) sa pri nich uvedie výpadok ako "mitigating factor".
13. **Výber polí:** do modelu sa berú polia s platnými hodinami >= 90 % denných hodín v 2023 a 2024 (okrem diery z bodu 8) a bez `dead_window`; presný zoznam vznikne z profilovacieho skriptu.
14. **Reportovanie:** profilovací skript vytvorí pre každý merač JSON/Markdown report (podiel chýbajúcich, duplicity, outliery, `stuck`, `dead_window`, vyradené riadky podľa dôvodu); report sa verziuje, surové dáta nie.

### Licencie a čo smieme commitnúť

Zdroj: Terms & Conditions DKASC (dkasolarcentre.com.au/download/terms-conditions), prečítané 2026-10-23.

| Zdroj | Podmienky | Dôsledok pre repo |
|---|---|---|
| DKASC, Alice Springs | Autorské právo vlastní Desert Knowledge Australia (DKA), spravuje Ekistica; všetky práva vyhradené okrem toho, čo T&C výslovne dovoľujú. **Bez formálneho súhlasu** smie byť zobrazených, reprodukovaných a distribuovaných najviac **5 000 buniek dát, v nezmenenej forme**; nad to treba súhlas DKA cez Ekistica (info@dkasolarcentre.com.au). Nejde o otvorenú licenciu (nie CC) | **Žiadne surové ani odvodené dáta v repe** (odvodené = zmenené, spadá mimo výnimku). Len skript na stiahnutie, zoznam polí a roky. Testovacie fixtury **syntetické**; reálna fixtúra max. do 5 000 buniek a nezmenená, inak až po písomnom súhlase |
| Povinná prezentácia dát | Dáta musia byť zobrazené tak, aby ich tretia osoba mohla overiť: presné dátumy analýzy, jednotky, **rok inštalácie poľa** (ak sa spomína konkrétna technológia, rok musí byť v názve, napr. "BP Solar Mono Si (2008)"), zmierňujúce faktory (výpadky) | UI a dokumentácia: pri každom grafe polia uviesť obdobie, jednotky, rok inštalácie, a že dáta obsahujú výpadky |
| Citácia | `Desert Knowledge Australia Centre. dd/mm/yyyy. Download Data: Array ## Brand Name. Alice Springs. http://dkasolarcentre.com.au/download, date accessed: dd/mm/yyyy.` | Zapísať do README a do reportov |
| Povinné vyhlásenie (disclaimer) | "Desert Knowledge Australia, the Australian Government, the Northern Territory Government and the project managers, Ekistica do not endorse, and accept no legal liability whatsoever arising from, or connected to, the outcomes and conclusions associated with the use of data from the Desert Knowledge Australia Solar Centre." musí byť priamo pri publikovaných dátach; DKA treba uviesť ako zdroj | Vložiť do UI/prezentácie/README |
| Stiahnutie | Formulár vyžaduje súhlas s T&C a uvedenie role a účelu | Skript nesmie obchádzať formulár; sťahuje sa ručne alebo po konzultácii s DKA; stiahnutie vykoná jeden člen tímu, súbory do `ml/data/` (v `.gitignore`) |
| Open-Meteo | dáta CC BY 4.0 (atribúcia), free API len nekomerčne (#180) | Nie surové odpovede; cache lokálne/v DB, atribúcia v dokumentácii a UI |
| PVDAQ / Kaggle | už nie primárny dataset (#178); Kaggle "Data files © Original Authors" | Len odkaz, nič nenahrávať |

Súhlas DKA: pre školský projekt je vhodné poslať Ekistica krátku správu o použití a požiadať o súhlas na odvodené dáta/ukážky nad 5 000 buniek (nadväzujúce issue).

## Dôsledky

- Cieľová premenná ostáva pravdivá, výpadky sa dajú detegovať (nie sú zamaskované interpoláciou); M19 C a M15 C ostávajú použiteľné ako overovacia množina.
- `stuck` pravidlo nevyradí legitímne orezaný výkon pri plnom slnku (Alice Springs má veľa plne slnečných hodín).
- Zámena W/kW je najčastejšia chyba: jednotka je v názve stĺpca a kontrolovaná rádom veľkosti.
- Prahy (75 %, 30 min, 95 %, 2 %, 14 dní, 50 %, 120 %, 90 %) sú návrhy; po profilovaní sa môžu upraviť, zmena sa zapíše do tohto ADR.

## Riziká / neoverené

- **Neoverené:** časové pásmo a značka intervalu v CSV z DKASC (v dokumentácii nie je); očakávané Australia/Darwin, UTC+9:30, bez DST.
- **Neoverené:** presný počet meračov (~49 podľa tímu vs. ~44 aktívnych polí na stránke), mapovanie "M19 C"/"M15 C Phase II" na stĺpce CSV, počet chýbajúcich dní 2023, prázdny stĺpec vetra, zdroj sklonu/azimutu a AC limitu per pole.
- **Neoverené:** reálne dáta DKASC sme v tomto spiku nestiahli (stiahnutie vyžaduje vyplnenie formulára s T&C); profilovanie reálnych súborov ešte nebolo spustené.
- Riziko: Alice Springs (púšť, takmer žiadna oblačnosť) nie je reprezentatívne pre SK; model natrénovaný na DKASC je v SK podhodnotený vo variabilite oblačnosti (viď #178/#187).
- Riziko: licencia DKASC neumožňuje zdieľať odvodené dáta; tím musí dáta držať lokálne alebo získať súhlas.

## Zdroje

- DKASC download: https://dkasolarcentre.com.au/download?location=alice-springs
- DKASC Terms & Conditions: https://dkasolarcentre.com.au/download/terms-conditions
- DKASC Notes on the Data: https://dkasolarcentre.com.au/download/notes-on-the-data
- DKASC Glossary (jednotky): https://dkasolarcentre.com.au/glossary
- Open-Meteo licencia a limity: https://open-meteo.com/en/pricing
- pvlib solar position: https://pvlib-python.readthedocs.io/

## Nadväzujúce issues

1. Stiahnutie 2023 a 2024 (ručne cez formulár), profilovací skript a report kvality per merač.
2. ETL: CSV -> parquet, UTC, kW -> W, hodinové agregáty s pravidlami z tohto ADR, testy `stuck` + clipping.
3. Žiadosť o súhlas DKA/Ekistica (odvodené dáta, ukážky nad 5 000 buniek) a doplnenie citácie a disclaimeru do README.

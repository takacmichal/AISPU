# ADR spike #189: Join výroby s počasím podľa lokality a času (DKASC + Open-Meteo)

- **Stav:** Navrhnuté (prepracované pre DKASC po rozhodnutí tímu v #178/#180)
- **Dátum:** 2026-10-23
- **Issue:** #189 (nadradené #6)
- **Súvisí:** #178 (dataset), #180 (zdroj počasia), #183 (kvalita dát), #191 (model), #220 (časové rady, `telemetry_hourly`)

## Kontext

Tím rozhodol: produkčný dataset je **DKA Solar Centre (DKASC), Alice Springs**, roky 2023 a 2024, 5-minútové záznamy z ~49 meračov polí, 2 meračov areálu a meteostanice. Počasie: Open-Meteo **Historical Forecast API** na tréning a **Forecast API** v prevádzke (rovnaké modely a premenné). PVDAQ/Kaggle už nie sú primárne.

Chybný posun o hodinu alebo zlé pásmo zníži kvalitu modelu viac než voľba algoritmu. Tento ADR definuje jednu konvenciu označovania hodín pre celý systém a upozorňuje, že ADR #220 sa jej musí prispôsobiť. **Granularita je hodinová: #189 nahrádza zmienku o 15-minútovej agregácii v #178** (model aj join bežia na hodinách; 5 min je len vstup do agregácie).

### Overené fakty (spustené 2026-10-23)

| Fakt | Ako | Stav |
|---|---|---|
| Open-Meteo hodinové `shortwave_radiation` s časom `t` (UTC) je priemer za **predchádzajúcu hodinu `(t-1h, t]`** | Historical Forecast API, Alice Springs, celé roky 2023 a 2024 (8760 a 8784 hodín, 0 prázdnych hodnôt). Porovnanie s vlastným výpočtom slnečnej geometrie (priemer cos(zenit) po 5 min v okne začínajúcom `t+shift`) na 73 až 74 najjasnejších dňoch: najvyššia korelácia pre okno `(t-1h, t]` (r = 0,9928 v 2024, 0,9958 v 2023); okno posunuté o 30 min späť r = 0,984 (2024) / 0,986 (2023), o 30 min dopredu 0,977 / 0,981; okno `[t, t+1h)` (značka = začiatok) r = 0,951 / 0,952; okno `[t+1h, t+2h)` r = 0,822 / 0,820 | **overené** |
| Mriežka: dotaz na `-23.76, 133.875` vráti bod `-23.796133, 133.85835`, výška 559 m; Forecast API aj Historical Forecast API vrátili ten istý bod | priame volanie API | **overené** (vzdialenosť ~4 km, vlastný výpočet) |
| Open-Meteo s `timezone=Australia/Darwin` vráti `utc_offset_seconds = 34200` (+9:30) a abbreviation `GMT+9:30` | priame volanie API | **overené** |
| Súradnice DKASC (-23.76, 133.875) sú len približné (z pamäti, nie z dokumentácie DKASC) | | **neoverené** |
| Pásmo a značka intervalu v CSV z DKASC nie sú v glossary, T&C ani Notes on the Data uvedené | prečítané 2026-10-23 | **neoverené** (očakávané Australia/Darwin, UTC+9:30, bez DST) |
| DKASC výkon je v **kW**, 5-min priemer z 1 s hodnôt | dkasolarcentre.com.au/glossary | overené (viď #183) |
| Korelačný test posunu na reálnych DKASC dátach | nespustené: stiahnutie CSV vyžaduje formulár s T&C a reálne súbory sme nemali | **nespustené** |

## Zvažované možnosti

| Téma | Možnosť | Posúdenie |
|---|---|---|
| Časová os | lokálny čas | `Australia/Darwin` nemá DST, no kód by sa viazal na lokalitu; zlé pre ďalšie lokality (SK má DST) |
| | **UTC všade** | jednoznačné; konverzia na lokálny čas len v UI |
| Značka hodiny | **začiatok hodiny `hour_start_utc`** | zhoduje sa s `date_trunc('hour', ts)` v #220; Open-Meteo čas treba posunúť o -1 h |
| | koniec hodiny `hour_end_utc` | zhoduje sa s Open-Meteo, ale nezhoduje sa s `date_trunc` (#220) |
| Granularita | interpolovať počasie na 5 min | vymyslí informáciu, hodinové žiarenie je priemer |
| | **agregovať výrobu na hodinu** | zodpovedá rozlíšeniu vstupu |
| | 15-min počasie (`minutely_15`) | v #180 spomenuté pre strednú Európu; zamietnuté: pre Alice Springs nebolo overené a model je hodinový |
| Lokalita | **súradnice poľa -> API -> uložiť vrátené súradnice** | jednoduché, API samo vyberá bod mriežky |
| | vlastný najbližší bod mriežky | zbytočná zložitosť |

## Rozhodnutie

**Join na hodinu UTC a lokalitu, hodina označená ZAČIATKOM.**

1. **Časová os a konverzia:** všetko UTC. DKASC lokálny čas `Australia/Darwin` (UTC+9:30, bez DST) sa konvertuje na UTC (`zoneinfo`/`tz_localize("Australia/Darwin")`, nikdy pevný offset v kóde okrem testu). Lokálna polnoc = 14:30 UTC predošlého dňa. **Dôsledok:** hranice lokálnych hodín (`:30` v UTC) nesedia na UTC hodiny; agregácia ide po UTC hodinách a lokálne hodiny sa používajú len na zobrazenie. Denné agregáty v lokálnom dni (`telemetry_daily`, #220) treba rátať z UTC hodín posunutých o 9:30 (alebo priamo z 5-min dát), nie `date_trunc('day', ts)` v UTC. Open-Meteo sa volá s `timezone=UTC` (nie `auto`).
2. **Konvencia označenia hodiny (jediná v celom systéme):** kľúč `(location_id, hour_start_utc)`, kde `hour_start_utc = h` znamená interval `[h, h+1h)`. Preto:
   - **Open-Meteo:** hodnota s časom `t` je priemer za `(t-1h, t]`, teda patrí hodine `hour_start_utc = t - 1h` (overené vyššie). Vo weather klientovi sa `time` hneď posunie o -1 h a stĺpec sa pomenuje `hour_start_utc`.
   - **Výroba (DKASC):** priemer výkonu zo vzoriek hodiny `[h, h+1h)`. Ak je 5-min značka na **začiatku** intervalu, ide o vzorky so značkou `h, h+5, ..., h+55`; ak na **konci**, o vzorky `h+5, ..., h+60`. Značka DKASC nie je zdokumentovaná (**neoverené**), kým ju nepotvrdí test posunu (bod 7); do vtedy parameter `DKASC_STAMP_LABEL` (`end` ako pracovný predpoklad, rozdiel je najviac 5 min z 60 a model ho necíti, no musí byť zapísaný).
   - **#220 `telemetry_hourly(hour_start)`** používa `date_trunc('hour', ts)`, teda značku začiatku: **je v súlade s touto konvenciou** a nemení sa. Naopak, pôvodný návrh #189 (značka konca, `resample(closed="right", label="right")`) sa **zrušuje**, lebo bol v rozpore s #220. Pri agregácii 5-min dát z telemetrie SolarOps treba sedieť na tom, ktorý okraj intervalu značka telemetrie reprezentuje (kontrakt #185), inak vznikne posun o jednu vzorku medzi `date_trunc` a skutočným intervalom; reviewer by to mal v #220 skontrolovať.
   - pandas pre značku začiatku: `resample("1h", closed="left", label="left")` (predvolené pre hodinu), pri 5-min značkách na konci `closed="right", label="left"`.
3. **Platnosť hodiny:** podľa ADR #183 (>= 75 % vzoriek, 9 z 12), inak `NaN`. Výkon sa neinterpoluje.
4. **Počasie sa neinterpoluje** na frekvenciu výroby. Model beží na hodinách, 5-min zobrazenie používa hodinovú hodnotu ako konštantu.
5. **Lokalita a najbližší bod mriežky (Alice Springs):** volať API so súradnicami areálu DKASC (približne -23,76; 133,875; **neoverené**, spresniť) a `elevation` ak je známa. API vráti **najbližší bod mriežky** (overené: -23,796 / 133,858, 559 m, ~4 km od dotazu). Uložiť skutočne vrátené `latitude`, `longitude`, `elevation` spolu s odpoveďou. Všetky polia a merače DKASC sú v jednom areáli, preto zdieľajú **jeden** bod počasia (`location_id` = DKASC Alice Springs); sklon/azimut rozlišuje len voliteľný GTI na pole. Žiadna vlastná interpolácia medzi bodmi. Forecast API a Historical Forecast API vrátili rovnaký bod, takže tréning a prevádzka majú rovnakú lokalitu.
6. **Premenné a jednotky:** `shortwave_radiation` (GHI), `direct_normal_irradiance`, `diffuse_radiation`, `cloud_cover`, `temperature_2m`, `wind_speed_10m` s `wind_speed_unit=ms`. Stĺpec vetra z DKASC je prázdny (#183), vietor je len z Open-Meteo. Pre fixné polia sa dá žiadať `global_tilted_irradiance` s `tilt` a `azimuth` poľa (zdroj sklonu/azimutu je **neoverený**, #183); sledovacie polia (Single/Dual) pevný sklon nemajú, použije sa GHI/DNI. Zdroje: Historical Forecast API na tréning, Forecast API v prevádzke, stĺpec `weather_source`.
7. **Overenie joinu (kontrola posunu):**
   - Vstup: jedno pole (napr. fixné, bez poruchy) a GHI z Open-Meteo, hodiny s GHI > 50 W/m², výkon v hodinovej norme (`power_w / (kWp * 1000)`), aspoň 30 jasných dní.
   - Postup: Pearson aj Spearman korelácia medzi GHI(h) a výkonom(h + s) pre posuny `s` = -2 až +2 h (po celých hodinách); pre jemnosť aj posuny po 30 min z 5-min dát. Vyhodnotí sa `argmax_s`.
   - Kritérium: maximum musí byť pri `s = 0`. Maximum pri +/-1 h = join je chybný (pásmo alebo značka intervalu). Navyše nezávislý test mimo počasia: čas vzostupu a špičky výkonu proti solárnemu poludniu z `pvlib` na +/-30 min (rovnaký test ako #183, bod 2).
   - **Prah r >= 0,9 je len predbežný.** Z overenej analýzy: v Alice Springs je aj pri chybnej značke o 1 h (okno `[t, t+1h)` namiesto `(t-1h, t]`) korelácia na jasných dňoch ~0,95, teda prah 0,9 by chybný join nezachytil. Rozhoduje preto poloha maxima (`s = 0`) a odstup `r(0) - max(r(+/-1h))` (predbežne >= 0,02, treba doladiť na reálnych DKASC dátach), nie absolútna hodnota r.
   - Stav: metodika bola **spustená iba na strane Open-Meteo** (GHI vs. slnečná geometria, výsledok v tabuľke vyššie); **test výroby DKASC vs. GHI nebol spustený** (chýbajú stiahnuté dáta).

## Dôsledky

- Jedna konvencia (`hour_start_utc`) naprieč #183, #189 a #220; chyba o hodinu sa zachytí testom.
- Pozor na half-hour offset Darwinu: UTC hodiny a lokálne hodiny nie sú zarovnané; denné agregáty a UI treba testovať.
- Strata 5-min detailu pre model (OK: počasie je hodinové).
- Pri 5-min monitoringu v reálnom čase treba samostatnú logiku (#194).
- Treba ukladať skutočné súradnice a zdroj počasia.

## Riziká / neoverené

- **Neoverené:** časové pásmo a značka intervalu v DKASC CSV; konverzia `Australia/Darwin` je predpoklad.
- **Neoverené:** presné súradnice areálu DKASC (použité približné).
- **Nespustené:** korelačný test posunu na reálnej výrobe DKASC; prah r >= 0,9 je provizórny (viď bod 7).
- Rozdiel train/serve: Historical Forecast API je archív predpovedí (nie reanalýza), čo je zámer (#180), no jemné rozdiely medzi archívom a živou predpoveďou sa môžu objaviť pri zmene modelov Open-Meteo.
- Open-Meteo free API len nekomerčne (#180). Bod mriežky je ~4 km od areálu a s výškou 559 m; lokálne efekty (oblačnosť) sa nezachytia.

## Zdroje

- Open-Meteo Historical Forecast API: https://open-meteo.com/en/docs/historical-forecast-api
- Open-Meteo Forecast API (preceding hour, `timezone`): https://open-meteo.com/en/docs
- DKASC download a glossary: https://dkasolarcentre.com.au/download?location=alice-springs , https://dkasolarcentre.com.au/glossary
- pandas resample: https://pandas.pydata.org/docs/reference/api/pandas.DataFrame.resample.html
- IANA zóna `Australia/Darwin`: https://www.iana.org/time-zones

## Nadväzujúce issues

1. Skript/notebook: join DKASC + Open-Meteo a test posunu (korelácia pre -2 až +2 h aj 30 min) na 2 až 3 poliach po stiahnutí dát; potvrdenie pásma a značky intervalu DKASC.
2. Weather client s cache, posunom `t - 1h`, ukladaním vrátených súradníc a atribúciou (CC BY 4.0).
3. Unit testy: hranice hodín, polhodinový offset Darwinu (lokálna polnoc = 14:30 UTC), neúplné hodiny, súlad `date_trunc` (#220) s `hour_start_utc`.

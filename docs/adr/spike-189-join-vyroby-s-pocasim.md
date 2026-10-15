# ADR spike #189: Join výroby s počasím podľa lokality a času

- **Stav:** Navrhnuté
- **Dátum:** 2026-10-15
- **Issue:** #189 (nadradené #6)
- **Súvisí:** #180 (zdroj počasia), #183 (kvalita dát), #191 (model)

## Kontext

Výroba (PVDAQ 1 až 15 min; telemetria SolarOps 5 min, kontrakt zo spiku #185) sa musí spárovať s počasím z Open-Meteo (hodinové, mriežka). Chybný posun o hodinu alebo zlé pásmo zníži kvalitu modelu viac než voľba algoritmu. Fakty z #180: hodinové žiarenie Open-Meteo je priemer za **predchádzajúcu hodinu**; API zaokrúhľuje súradnice na mriežku (Košice 48.72/21.26 -> 48.68/21.28 v archíve); wind je predvolene v km/h.

## Zvažované možnosti

| Téma | Možnosť | Posúdenie |
|---|---|---|
| Časová os | lokálny čas | zlé: DST (jedna hodina chýba, jedna je dvojitá) |
| | **UTC všade** | jednoznačné, bez DST; konverzia na lokálny čas len v UI |
| Granularita | interpolovať počasie na 5/15 min | vymyslí informáciu, hodinové žiarenie je priemer, nie bodová hodnota |
| | **agregovať výrobu na hodinu** | zodpovedá reálnemu rozlíšeniu vstupu, jednoduché, bez falošnej presnosti |
| | 15-min počasie (Open-Meteo `minutely_15`) | možné v strednej Európe v predpovedi, nie v ERA5 historii; nesúlad train/serve; odložené |
| Lokalita | **súradnice inštalácie -> API -> uložiť vrátené súradnice** | jednoduché, API samo vyberá bod/interpoluje |
| | vlastný najbližší bod mriežky | zbytočná zložitosť |

## Rozhodnutie

**Join na hodinu UTC a lokalitu.**

1. **Os:** UTC. Kľúč `(location_id, hour_end_utc)`. Všetky vstupy sa konvertujú na UTC pred joinom; lokálne pásmo systému berieme z metadát (PVDAQ `timezone_or_utc_offset`, SolarOps inštalácia). DST sa tým rieši konverziou, nie joinom. Open-Meteo sa vždy volá s `timezone=UTC`.
2. **Hodinový agregát výroby:** priemer výkonu (W) vo výseku `(t - 1 h, t]`, označený koncovým časom `t` (right-closed, right-labelled). Presne to zodpovedá významu Open-Meteo "preceding hour" a pandas `resample("1h", closed="right", label="right")`. Platnosť hodiny podľa ADR #183 (>= 75 % vzoriek), inak `NaN`.
3. **Počasie sa neinterpoluje** na frekvenciu výroby. Pre 5-min zobrazenie v dashboarde sa použije hodinová hodnota ako konštanta; model beží na hodinách.
4. **Lokalita:** volať API so súradnicami inštalácie (a `elevation` ak je známa). Uložiť skutočne vrátené `latitude`, `longitude`, `elevation` spolu s odpoveďou. Inštalácie, ktoré padnú do rovnakej bunky (rovnaké vrátené súradnice), zdieľajú cache. Žiadna vlastná interpolácia medzi bodmi.
5. **Premenné a jednotky:** `shortwave_radiation` (GHI), `direct_normal_irradiance`, `diffuse_radiation`, `cloud_cover`, `temperature_2m`, `wind_speed_10m` s `wind_speed_unit=ms`.
6. **Zdroj:** pre tréning Historical Forecast API (od ~2022) tam, kde sa dá, inak Historical Weather API (ERA5/IFS); do dát pridať stĺpec `weather_source` (rozhodnutie #180).
7. **Overenie joinu (kontrola, nie testy modelu):** na vzorke systému spočítať Pearson a Spearman korelácie medzi GHI a výkonom (W/kWp) cez deň (GHI > 50 W/m²) pre posuny -2 až +2 h; maximum musí byť pri posune 0. Akceptačný návrh: r >= 0,9 pre hodinové denné dáta (neoverené, upraviť podľa PVDAQ systému). Ak je maximum pri ±1 h, join sa považuje za chybný (pásmo/označenie intervalu).

## Dôsledky

- Jednoduchý, reprodukovateľný join bez DST chýb; rovnaká logika v tréningu aj produkcii.
- Strata 5/15-min detailu pre model (OK: predpoveď počasia je hodinová).
- Pri 5-min monitoringu v reálnom čase treba samostatnú logiku (porovnanie okamžitého výkonu s hodinovým očakávaním je hrubé); riešime v #194 na hodinách.
- Treba ukladať skutočné súradnice a zdroj počasia, inak nie je dôvod posunu zrekonštruovateľný.

## Riziká / neoverené

- Korelácia na vzorke (posledný bod checklistu) ešte **nebola spustená**: v tomto spiku je len metodika; skript je v nadväzujúcom issue. Dostupná je len 1-dňová vzorka PVDAQ systému 4.
- Neoverené: konkrétna časová konvencia v PVDAQ súboroch (lokálny štandardný čas vs. DST), overí sa testom posunu (bod 7).
- ERA5 (0,25°) vs. ICON (~2 km): rozdielne rozlíšenie pri mixe zdrojov (viď #180).
- Open-Meteo free API len nekomerčne (#180).

## Zdroje

- Open-Meteo Historical Forecast API: https://open-meteo.com/en/docs/historical-forecast-api
- Open-Meteo Historical Weather API: https://open-meteo.com/en/docs/historical-weather-api
- Open-Meteo Forecast API (preceding hour, `timezone`): https://open-meteo.com/en/docs
- pandas resample: https://pandas.pydata.org/docs/reference/api/pandas.DataFrame.resample.html
- PVDAQ `systems.csv` (`timezone_or_utc_offset`): https://oedi-data-lake.s3.amazonaws.com/pvdaq/csv/systems_20250729.csv

## Nadväzujúce issues

1. Skript/notebook: join PVDAQ + Open-Meteo a test posunu (korelácia pre -2 až +2 h) na 2 až 3 systémoch.
2. Weather client s cache, ukladaním vrátených súradníc a atribúciou (CC BY 4.0).
3. Unit testy: DST jar/jeseň, hranice hodín, neúplné hodiny.

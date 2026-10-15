# ADR-180: Zdroj historického počasia na trénovanie a zdroj predpovede

- Stav: prijaté (Sprint 0)
- Dátum: 2026-10-15
- Issue: #180

> Dátum overenia: 2026-10-15. Všetky čísla označené "overené" som zistil priamo (curl / fetch stránky); "neoverené" znamená, že som to nevedel potvrdiť z primárneho zdroja.

## Kontext

Model produkcie potrebuje vstupy GHI/DNI/DHI, oblačnosť, teplotu, vietor. Pri trénovaní použijeme históriu, pri nasadení predpoveď. Dôležitá je **train/serve konzistencia**: ak trénujeme na reanalýze a servujeme na NWP predpovedi, model vidí iné rozdelenie chýb. Potrebujeme pokrytie pre slovenské lokality (napr. Košice 48.72, 21.26), bezplatný prístup pre školský projekt a rozumné limity.

## Zvažované možnosti

| Zdroj | Priestorové / časové rozlíšenie | Premenné | História | Predpoveď | Limity a licencia |
|---|---|---|---|---|---|
| Open-Meteo Historical Weather API (ERA5 / ERA5-Land / IFS) | ERA5 0,25° (~25 km), ERA5-Land 0,1° (~11 km, bez žiarenia), IFS 9 km; hodinové | GHI (`shortwave_radiation`), DNI, DHI, GTI, teplota, oblačnosť, vietor | ERA5 od 1940, IFS od 2017; oneskorenie 5 dní | nie (len história) | viď nižšie |
| Open-Meteo Historical Forecast API | modely od ~2 km (ICON-D2) do 44 km; hodinové, 15-min pre Strednú Európu | rovnaké ako forecast API vrátane GHI/DNI/DHI/GTI | od ~2022 (niektoré modely 2021) | rovnaký formát ako forecast API | viď nižšie |
| Open-Meteo Forecast API | ICON-D2/AROME v strednej Európe, 15-min; hodinové | rovnaké | `past_days` 0 až 92 | `forecast_days` 0 až 16, ICON aktualizácia každé 3 h | viď nižšie |
| ERA5 priamo (Copernicus CDS) | 0,25°, hodinové | `ssrd` (akumulované, treba prevod) | od 1940 | nie | CC BY 4.0 (zdroje sa nezhodujú, overiť na CDS stránke); vyžaduje CDS účet a token, fronta požiadaviek, neoverený čas |
| NASA POWER | hodinové od 2001-01-01 do NRT (overené); rozlíšenie "pôvodné zdrojové" (neoverené číslo) | solárne a meteo parametre, max 15 parametrov na požiadavku | od 2001 | nie (len história a NRT) | limit neuvedený v docs; fórum NASA: bez pevného limitu, ale monitorované; neoverená licencia |
| SHMÚ | stanice, nie mriežka | nižšia dostupnosť žiarenia na stanice | rôzna | predpoveď existuje, ale API/licenciu pre žiarenie som nenašiel | neoverené, nepoužiteľné ako primárne bez ďalšieho prieskumu |

Open-Meteo limity a licencia (overené na https://open-meteo.com/en/pricing): free tier 600 volaní/min, 5 000/h, 10 000/deň, 300 000/mesiac (mesačný limit zatiaľ nevynucovaný); **free API je len pre nekomerčné použitie**, bez záruky dostupnosti. Dáta sú CC BY 4.0 (vyžaduje sa atribúcia), kód AGPLv3. Komerčné nasadenie = platený plán.

### Overená ukážka (Košice, 48.72, 21.26)

Historical (ERA5) , `https://archive-api.open-meteo.com/v1/archive`:

```bash
curl -s "https://archive-api.open-meteo.com/v1/archive?latitude=48.72&longitude=21.26&start_date=2024-06-20&end_date=2024-06-21&hourly=temperature_2m,cloud_cover,shortwave_radiation,direct_normal_irradiance,diffuse_radiation,wind_speed_10m&timezone=Europe%2FBratislava"
```

Skrátená odpoveď (grid bod sa zaokrúhlil na 48.68, 21.28; 2024-06-20 10:00 až 13:00):

```json
{"latitude":48.681896,"longitude":21.283783,"elevation":214.0,"timezone":"Europe/Bratislava",
 "hourly_units":{"shortwave_radiation":"W/m²","direct_normal_irradiance":"W/m²","diffuse_radiation":"W/m²","cloud_cover":"%","wind_speed_10m":"km/h"},
 "hourly":{"time":["2024-06-20T10:00","...T11:00","...T12:00","...T13:00"],
  "temperature_2m":[20.7,21.6,22.4,23.0],
  "cloud_cover":[26,62,100,96],
  "shortwave_radiation":[673.0,798.0,876.0,886.0],
  "direct_normal_irradiance":[776.5,830.3,849.0,785.6],
  "diffuse_radiation":[119.0,123.0,131.0,177.0],
  "wind_speed_10m":[24.3,20.9,19.0,16.4]}}
```

Forecast (dnešok 2026-10-15, 10:00 až 13:00, overené):

```bash
curl -s "https://api.open-meteo.com/v1/forecast?latitude=48.72&longitude=21.26&hourly=shortwave_radiation,direct_normal_irradiance,diffuse_radiation,cloud_cover,temperature_2m&forecast_days=1&timezone=Europe%2FBratislava"
# hourly: shortwave_radiation [324.0,417.0,270.0,252.0], direct_normal_irradiance [586.9,334.1,0.0,1.9],
#         diffuse_radiation [109.0,262.0,270.0,251.0], cloud_cover [0,100,23,100], temperature_2m [12.7,14.1,13.7,13.3]
```

Taktiež overené: `minutely_15=shortwave_radiation` na forecast API vracia 15-min kroky pre Košice, a Historical Forecast API (`https://historical-forecast-api.open-meteo.com/v1/forecast`) vracia dáta pre 2023-01-01 aj 2024-06-20 s `shortwave_radiation` a `cloud_cover`.

## Rozhodnutie

**Open-Meteo ako jediný zdroj pre train aj serve**, s týmto rozdelením:

1. **Trénovanie:** Historical Forecast API (archivované predpovede, od ~2022) tam, kde je to možné, lebo ide o rovnaký model a formát ako produkčná predpoveď, čím minimalizujeme train/serve rozdiel. Pre dlhšiu históriu (sezónnosť pred 2022 a pre PVDAQ dáta z USA) doplniť Historical Weather API (ERA5/IFS) a v dokumentácii zdokumentovať, že ide o reanalýzu.
2. **Produkcia:** Forecast API (15-min v Strednej Európe, 16 dní, `past_days` do 92 na doplnenie nedávnej histórie).
3. Uložiť si surové odpovede (cache v DB/parquet) a pridať atribúciu CC BY 4.0.

ERA5 priamo nie je potrebný (Open-Meteo ho už sprístupňuje bez fronty). NASA POWER a SHMÚ len ako záloha/validácia, nie primárne.

## Dôsledky

- Jedno API, jeden formát, bezplatné pre školský projekt, GHI/DNI/DHI k dispozícii pre Slovensko (overené Košice).
- **Riziko komerčného nasadenia:** free tier je nekomerčný, pre reálny produkt treba platený plán alebo vlastný hosting (kód je AGPLv3). Zapísať do rozhodnutí tímu.
- Historical Forecast dáta nemajú dlhú históriu a modely sa časom menia (uvádza sama dokumentácia), preto bude možný mierny drift modelu; treba pravidelné pretrénovanie a monitoring.
- Rozdiel medzi ERA5 (0,25°) a ICON-D2 (~2 km) je pri mixe zdrojov reálny; do feature engineeringu pridať príznak zdroja alebo trénovať iba na Historical Forecast a ERA5 použiť len na predtréning.
- Hodinové žiarenie je priemer za predchádzajúcu hodinu (preceding hour); pri zarovnaní s 15-min dátami elektrárne treba dbať na posun o hodinu.
- Open-Meteo "snapuje" súradnice na mriežku (Košice -> 48.68/21.28 v archíve, 48.724/21.275 v predpovedi); výška terénu 214 m. Uložiť skutočne použité súradnice.
- Neoverené: presná zhoda SHMÚ (API, licencia), NASA POWER priestorové rozlíšenie a licencia, aktuálna fronta ERA5 CDS, presné rozlíšenie ICON-D2 v dokumentácii Open-Meteo (uvádza sa rozsah 2 až 11 km pre celú rodinu ICON).

## Zdroje

- Open-Meteo pricing a licencia: https://open-meteo.com/en/pricing
- Historical Weather API: https://open-meteo.com/en/docs/historical-weather-api
- Historical Forecast API: https://open-meteo.com/en/docs/historical-forecast-api
- Forecast API: https://open-meteo.com/en/docs
- NASA POWER hourly: https://power.larc.nasa.gov/docs/services/api/temporal/hourly/ ; rate limit fórum: https://forum.earthdata.nasa.gov/viewtopic.php?p=15146
- ERA5 (informatívne, nepotvrdené z oficiálnej stránky): https://docs.rcac.purdue.edu/datasets/meteorological/ERA5
- PVGIS (SARAH3 2005 až 2023): https://pvgis.com/

## Nadväzujúce issues

- Weather client (.NET alebo Python ML služba) s cache a atribúciou.
- Pipeline: Historical Forecast -> feature store pre trénovanie, porovnanie s ERA5 (backtest rozdielu zdrojov).
- Rozhodnutie o plate/hostingu Open-Meteo pre komerčné nasadenie.
- Prieskum SHMÚ otvorených dát (merané žiarenie na validáciu) a NASA POWER licencie.
- Zarovnanie časových pásiem a priemerovania (hodinové vs 15-min).

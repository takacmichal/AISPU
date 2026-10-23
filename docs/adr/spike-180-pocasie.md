# ADR-180: Zdroj historického počasia na trénovanie a zdroj predpovede

- Stav: prijaté (rozhodnutie tímu, upravené po Sprinte 0)
- Dátum: 2026-10-23 (pôvodná verzia 2026-10-15)
- Issue: #180

> Dátum overenia: 2026-10-15 a pre Historical Forecast API (Alice Springs, Košice) 2026-10-23. Všetky čísla označené "overené" som zistil priamo (curl / fetch stránky); "neoverené" znamená, že som to nevedel potvrdiť z primárneho zdroja.

## Kontext

Model produkcie potrebuje vstupy GHI/DNI/DHI, oblačnosť, teplotu, vietor. Pri trénovaní použijeme históriu, pri nasadení predpoveď. Dôležitá je **train/serve konzistencia**: ak trénujeme na reanalýze a servujeme na NWP predpovedi, model vidí iné rozdelenie chýb. Potrebujeme pokrytie pre slovenské lokality (napr. Košice 48.72, 21.26) **aj pre Alice Springs (-23.76, 133.87)**, kde leží trénovací dataset DKASC (ADR-178, roky 2023 a 2024), bezplatný prístup pre školský projekt a rozumné limity. Rozhodnutie tímu (komentár v issue #180): na trénovanie Open-Meteo Historical Forecast API, na prevádzku bežné Forecast API.

## Zvažované možnosti

| Zdroj | Priestorové / časové rozlíšenie | Premenné | História | Predpoveď | Limity a licencia |
|---|---|---|---|---|---|
| Open-Meteo Historical Weather API (ERA5 / ERA5-Land / IFS) | ERA5 0,25° (~25 km), ERA5-Land 0,1° (~11 km, bez žiarenia), IFS 9 km; hodinové | GHI (`shortwave_radiation`), DNI, DHI, GTI, teplota, oblačnosť, vietor | ERA5 od 1940, IFS od 2017; oneskorenie 5 dní | nie (len história) | viď nižšie |
| Open-Meteo Historical Forecast API | modely od ~2 km (ICON-D2) do 44 km; hodinové (overené pre Alice Springs aj Košice), 15-min výstup existuje (`minutely_15`), natívne len pre niektoré modely/oblasti (z dokumentácie, neoverené), pre Alice Springs neoverené, či je natívny alebo interpolovaný | rovnaké ako forecast API vrátane GHI/DNI/DHI/GTI | od ~2022 (niektoré modely 2021) | rovnaký formát ako forecast API | viď nižšie |
| Open-Meteo Forecast API | ICON-D2/AROME v strednej Európe; hodinové, voliteľne 15-min (`minutely_15`) | rovnaké | `past_days` 0 až 92 | `forecast_days` 0 až 16, ICON aktualizácia každé 3 h | viď nižšie |
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

### Overenie Historical Forecast API pre Alice Springs a Košice (2026-10-23)

Ukážkové volanie (Alice Springs, 2023-01-01 až 2024-12-31, všetky premenné z tímového zadania, GTI pre sklon 23,8° a azimut 0° = sever; sklon a azimut sú tu len ukážkové, skutočné hodnoty polí DKASC sú v metadátach):

```bash
curl -s "https://historical-forecast-api.open-meteo.com/v1/forecast?latitude=-23.76&longitude=133.87&start_date=2023-01-01&end_date=2024-12-31&hourly=shortwave_radiation,direct_normal_irradiance,diffuse_radiation,global_tilted_irradiance,cloud_cover,temperature_2m,wind_speed_10m&timezone=Australia%2FDarwin&tilt=23.8&azimuth=0"
```

Výsledok (overené, spracované skriptom nad celou odpoveďou):

| | Alice Springs | Košice (48.72, 21.26, `timezone=Europe/Bratislava`, tilt 35, azimut 0) |
|---|---|---|
| Riadkov (hodinové) | 17 544 (2023-01-01T00:00 až 2024-12-31T23:00) | 17 544 (rovnaký rozsah) |
| Chýbajúce hodnoty (null) | 0 vo všetkých 7 premenných | 0 vo všetkých 7 premenných |
| Skutočný bod mriežky | -23.796, 133.858; výška 559 m | 48.724, 21.275; výška 214 m |
| Časová zóna odpovede | Australia/Darwin, `utc_offset_seconds` 34200 (UTC+9:30, bez DST) | Europe/Bratislava, `utc_offset_seconds` 7200 (API uvádza jeden offset pre celú odpoveď; hodnoty `time` sú v miestnom čase, DST sa v nich prejaví) |
| Rozsah GHI / DNI / DHI / GTI (W/m²) | max 1122 / 1010 / 510 / 1037 | max 930 / 904 / 814 / 1030 |
| Mesačný priemer GHI (hodinový, vrátane noci) | 177 (júl 2023), 345 (dec. 2023) | 33 (jan. 2023), 247 (jún 2023) |

Kontrolné krátke volanie, Alice Springs 2024-02-05 (deň, odkedy podľa tímu nevyrába M19 C), `shortwave_radiation` v W/m² pre 06:00 až 17:00 miestneho času: 2, 109, 341, 582, 781, 956, 1048, 1074, 1014, 804, 465, 429 (cloud_cover 0 až 100 %), odpoveď s `timezone` Australia/Darwin. Dáta z roku 2022 (`2022-01-01`) API tiež vrátilo, pre naše roky to nie je potrebné.

Forecast API pre Alice Springs tiež vracia hodinové dáta (volanie na dnešok 2026-10-23, `shortwave_radiation` vrcholí na 954 W/m² o 12:00 miestneho času), takže train aj serve funguje s rovnakými názvami premenných a rovnakým formátom.

Čo to **nedokazuje**: že hodnoty sú presné. Overil som pokrytie, rozsah, absenciu null a metadáta, nie zhodu s meraním (korelácia s meteostanicou DKASC je nadväzujúca úloha). Neoverené: ktorý model Open-Meteo pre Alice Springs v danom období použil (pri vyššom rozlíšení mimo Európy ide zrejme o globálny model; názov modelu API v odpovedi neuviedlo), či je 15-min výstup (`minutely_15`) pre Alice Springs natívny alebo interpolovaný (volanie odpovedá, ale pôvod hodnôt som neskúmal), a či sa model pre 2023 až 2024 menil.

## Rozhodnutie

**Open-Meteo ako jediný zdroj pre train aj serve**, s týmto rozdelením (rozhodnutie tímu, issue #180):

1. **Trénovanie:** Historical Forecast API (archivované predpovede), rovnaký model a formát ako produkčná predpoveď, čím minimalizujeme train/serve rozdiel. Pre dataset DKASC (Alice Springs, 2023 a 2024) a pre slovenské lokality (Košice) overené pokrytie 2023 až 2024 bez chýbajúcich hodnôt. Premenné: GHI (`shortwave_radiation`), DNI, DHI, GTI (v rovine panelu cez `tilt` a `azimuth`), oblačnosť, teplota, vietor. Pre dlhšiu históriu (pred 2022) prípadne doplniť Historical Weather API (ERA5/IFS), v dokumentácii označiť, že ide o reanalýzu.
2. **Produkcia:** Forecast API (16 dní, `past_days` do 92 na doplnenie nedávnej histórie).
3. **Granularita: hodinová.** Join na výrobu sa robí na hodinu (hodinový agregát výroby z 5-min dát, pozri ADR-178 a issue #189 pre pravidlá zarovnania a UTC). 15-min výstup (`minutely_15`) existuje a môžeme ho použiť v prevádzke pre Slovensko, ale **trénovací pipeline ho nepredpokladá**, aby sa nelíšil train a serve a aby fungoval aj pre Alice Springs, kde nie je overené, že je 15-min natívne.
4. Uložiť si surové odpovede (cache v DB/parquet) vrátane skutočne použitých súradníc mriežky a pridať atribúciu CC BY 4.0.
5. Meteostanica DKASC je referencia na validáciu (backtest chyby žiarenia z Open-Meteo oproti meraniu), nie vstup modelu.

ERA5 priamo nie je potrebný (Open-Meteo ho už sprístupňuje bez fronty). NASA POWER a SHMÚ len ako záloha/validácia, nie primárne.

## Dôsledky

- Jedno API, jeden formát, bezplatné pre školský projekt, GHI/DNI/DHI/GTI k dispozícii pre Alice Springs aj Slovensko (overené Košice aj Alice Springs).
- **Rozpor s pôvodným textom opravený:** pôvodná verzia hovorila, že 15-min dáta sú k dispozícii pre Strednú Európu a trénovanie sa malo zarovnať na 15-min výrobu. To sa nezhodovalo s hodinovým joinom (#189) a s 5-min datasetom DKASC. Teraz platí: **model sa trénuje a joinuje hodinovo**, 15-min je len voliteľná optimalizácia prevádzky pre Slovensko. (ADR-189 v repozitári zatiaľ neexistuje; #189 je otvorený spike. Ak jeho výsledok bude iný, tento bod treba upraviť.)
- **Časová zóna:** Alice Springs = Australia/Darwin, UTC+9:30 bez DST (overené `utc_offset_seconds` 34200); Košice mení +1/+2 podľa DST. Všetko ukladať v UTC (`timezone=UTC` alebo prevod), inak vzniká zmena pri DST na Slovensku a polhodinový posun pri Alice Springs.
- **Riziko komerčného nasadenia:** free tier je nekomerčný, pre reálny produkt treba platený plán alebo vlastný hosting (kód je AGPLv3). Zapísať do rozhodnutí tímu.
- Historical Forecast dáta nemajú dlhú históriu a modely sa časom menia (uvádza sama dokumentácia), preto bude možný mierny drift modelu; treba pravidelné pretrénovanie a monitoring.
- Rozdiel medzi ERA5 (0,25°) a ICON-D2 (~2 km) je pri mixe zdrojov reálny; do feature engineeringu pridať príznak zdroja alebo trénovať iba na Historical Forecast a ERA5 použiť len na predtréning.
- Hodinové žiarenie je priemer za predchádzajúcu hodinu (preceding hour); pri zarovnaní s výrobou treba dbať na posun o hodinu.
- Open-Meteo "snapuje" súradnice na mriežku (Košice -> 48.724/21.275 v predpovedi, 48.68/21.28 v archíve ERA5; Alice Springs -> -23.796/133.858, výška 559 m). Uložiť skutočne použité súradnice a výšku.
- Alice Springs je púštna klíma južnej pologule, Košice nie. Open-Meteo vie obe lokality, ale model natrénovaný na Alice Springs sa nemá používať na Slovensko bez dotrénovania na slovenských (syntetických) dátach; pozri ADR-178.
- Neoverené: presná zhoda SHMÚ (API, licencia), NASA POWER priestorové rozlíšenie a licencia, aktuálna fronta ERA5 CDS, presné rozlíšenie ICON-D2 v dokumentácii Open-Meteo (uvádza sa rozsah 2 až 11 km pre celú rodinu ICON), model použitý pre Alice Springs a pôvod 15-min hodnôt tam, kvalita (presnosť) žiarenia oproti meraniu DKASC.

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
- Zarovnanie časových pásiem (UTC, Australia/Darwin) a hodinového priemerovania výroby voči počasiu (nadväzuje na #189).
- Backtest: chyba GHI z Open-Meteo Historical Forecast oproti meteostanici DKASC (Alice Springs 2023 a 2024).

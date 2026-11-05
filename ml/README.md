# solarops-ml

ML služba SolarOps (FastAPI). Spustenie: `uv run uvicorn solarops_ml.api:app --reload --port 8000`.

Testy a lint (rovnako ako CI `ml.yml`): `uv run pytest`, `uv run ruff check .`, `uv run ruff format --check .`.

## Simulátor striedačov (`solarops_ml.simulator`, issue #94)

Generuje 5-minútovú telemetriu FVE podľa ingest kontraktu (ADR #185, `docs/adr/assets/spike-185-telemetry.schema.json`, `schemaVersion 1.0`, dávky po max. 288 čítaní, `source: "simulator"`) z polohy, polohy slnka (čistá numpy/pandas, bez pvlib), sezóny a oblačnosti (AR(1) proces). Výstup je reprodukovateľný cez `--seed`. `installationId` sa berie z `--installation-id` (inak sa odvodí od zariadenia); musí to byť inštalácia, v ktorej je invertor `--device-id` zaregistrovaný, inak ingest odmietne dáta. Poruchy: `power_drop` (pokles výkonu o `severity`) a `inverter_fault` (`FAULT`, výkon 0). Labely (`expected_power_w`, `fault_active`, `fault_type`) idú do samostatného CSV, nie do ingest payloadu. Orientácia panelov (`tilt`, `azimuth`) sa berie z `locations.toml` (0 = juh, 180 = sever).

```bash
cd ml
uv run python -m solarops_ml.simulator --location alice_springs --start 2024-06-20 --end 2024-06-22 \
    --seed 42 --fault power_drop,2024-06-20T02:00,2024-06-20T05:00,0.5 \
    --out data/sim/alice.json --labels-out data/sim/alice_labels.csv
```

## Historické počasie (`solarops_ml.weather`)

Sťahuje počasie z [Open-Meteo](https://open-meteo.com) podľa ADR #180 a #189:

- **Historical Forecast API** (`historical-forecast-api.open-meteo.com/v1/forecast`; archivované predpovede, od 2022, rovnaké modely a premenné ako Forecast API na serving; **tímové rozhodnutie pre trénovacie dáta**, issue #180) alebo
  **Historical Weather API / archive** (ERA5 reanalýza, od 1940, oneskorenie cca 5 dní).
  `--source auto` (predvolené) zvolí historical-forecast pre začiatok od 2022-01-01, inak archive.
- Rozlíšenie hodinové (predvolené, join podľa ADR #189) alebo `--resolution minutely_15` (len historical-forecast, stredná Európa).
- Požiadavky idú vždy s `timezone=UTC`, `timeformat=unixtime`, `wind_speed_unit=ms`.
- Dlhé obdobia sa delia po mesiacoch, medzi volaniami je pauza (≥ 0,25 s), 429/5xx/sieťové chyby sa opakujú s exponenciálnym čakaním (rešpektuje `Retry-After`). Free tier: 600 volaní/min, 10 000/deň, **len nekomerčne**.
- Cache: `ml/data/weather/cache/<source>/<resolution>/lat<..>_lon<..>/<od>_<do>.parquet` (adresár `ml/data/` je v `.gitignore`, dá sa zmeniť cez `SOLAROPS_DATA_DIR` alebo `--data-dir`). Mesiace končiace menej ako 7 dní pred dneškom sa necachujú (dáta sa ešte môžu meniť).

```bash
cd ml
uv run python -m solarops_ml.weather locations                       # lokality z config/locations.toml
uv run python -m solarops_ml.weather fetch --location kosice --start 2024-01-01 --end 2024-12-31
uv run python -m solarops_ml.weather fetch --lat 48.72 --lon 21.26 --start 2024-06-20 --end 2024-06-21 \
    --source archive --format csv --out data/weather/kosice.csv
uv run python -m solarops_ml.weather fetch-all --start 2024-01-01 --end 2024-03-31  # všetky lokality
```

Lokality sú v `config/locations.toml` (`[locations.<meno>]` s `latitude`, `longitude`, `label`; voliteľne `tilt` a `azimuth`). Obsahuje slovenské mestá a `alice_springs` (DKA Solar Centre, -23.76 / 133.87, produkčné dáta 2023-2024). Pokrytie Historical Forecast API 2023-01-01 až 2024-12-31 bolo overené pre Alice Springs aj Košice (hodinové, bez chýbajúcich hodnôt).

**GTI (`gti_wm2`):** rovina panelu sa nastavuje cez `tilt` (0 = horizontálne, 90 = zvislo) a `azimuth` (0 = juh, -90 = východ, 90 = západ, ±180 = sever) v `locations.toml` alebo `--tilt/--azimuth`. Bez nich API počíta GTI pre horizontálnu rovinu, teda `gti_wm2` = `ghi_wm2`. Na južnej pologuli smerujú pevné panely na sever (`azimuth = 180`). Skutočná geometria DKA zatiaľ nie je zadaná. Orientácia je súčasťou kľúča cache.

### Formát dát (`solarops-weather-2`)

Zmena oproti `solarops-weather-1`: pribudol stĺpec `gti_wm2`. Cache je v podadresári `cache/solarops-weather-2/`, staré súbory sa nepoužijú.

Výstup: `data/weather/<lokalita>_<od>_<do>_<source>_<resolution>.parquet` (alebo `.csv`). Jeden riadok = jeden interval.

| stĺpec | jednotka | význam |
|---|---|---|
| `time` | UTC | časová značka riadku. Žiarenie (priemer) a zrážky (súčet) sú za **predchádzajúci** interval, teda `time` je jeho koniec (Open-Meteo „preceding hour“). Teplota, vietor a oblačnosť sú **okamžité hodnoty** v čase `time`. Parquet `timestamp[ns, tz=UTC]`, CSV ISO 8601 so `Z` |
| `ghi_wm2` | W/m² | globálne horizontálne žiarenie (`shortwave_radiation`) |
| `dni_wm2` | W/m² | priame normálové žiarenie (`direct_normal_irradiance`) |
| `dhi_wm2` | W/m² | difúzne horizontálne žiarenie (`diffuse_radiation`) |
| `gti_wm2` | W/m² | globálne žiarenie na naklonenú rovinu (`global_tilted_irradiance`, pozri GTI vyššie) |
| `cloud_cover_pct` | % | celková oblačnosť |
| `temp_air_c` | °C | teplota vzduchu 2 m |
| `wind_speed_ms` | m/s | rýchlosť vetra 10 m |
| `precipitation_mm` | mm | zrážky za interval (reset soilingu, ADR #187) |
| `latitude`, `longitude`, `elevation` | °, m | bod mriežky, ktorý API skutočne použilo (ADR #189) |
| `weather_source` | text | `historical-forecast` alebo `archive` |

Chýbajúce hodnoty (`null` z API) sú `NaN`. Parquet nesie v metadátach `solarops.attribution` a `solarops.format`.
Načítanie: `solarops_ml.weather.read_dataset(path)` vráti `DataFrame` indexovaný UTC časom.

**Atribúcia (povinná, CC BY 4.0):** Weather data by [Open-Meteo.com](https://open-meteo.com), licensed under [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/).

## Porovnanie modelov pre predikciu výroby (spike #191)

Experiment z ADR #191 porovnáva perzistenciu, fyzikálny baseline, lineárnu regresiu, LightGBM a malú LSTM na hodinových dátach. Keďže reálne DKASC dáta nie sú v repe (licencia, ADR #183), beh používa **syntetické dáta zo simulátora #94** – je to metodický dry-run, nie dôkaz o produkčnej presnosti. Výsledky sú v `docs/adr/assets/spike-191-results.json` a `.md`, zhrnutie v ADR #191.

Experimentálne závislosti (`lightgbm`, `scikit-learn`, `torch`) sú v samostatnej skupine, takže produkčný image a CI ich neinštalujú:

```bash
cd ml
uv sync --group experiment

# celý beh: 5 polí, 2023–2024, walk-forward + leave-one-array-out
uv run python -m solarops_ml.forecast \
    --out-json ../docs/adr/assets/spike-191-results.json \
    --out-markdown ../docs/adr/assets/spike-191-results.md

# rýchly dymový beh bez LSTM
uv run python -m solarops_ml.forecast --quick \
    --out-json /tmp/spike-191-quick.json --out-markdown /tmp/spike-191-quick.md
```

Notebook: `ml/notebooks/191-model-comparison.ipynb` (načíta vygenerovaný JSON, prípadne experiment spustí). LightGBM a PyTorch si prinášajú vlastný OpenMP runtime; `solarops_ml.forecast` preto nastavuje `OMP_NUM_THREADS=1`, aby v jednom procese nedochádzalo k deadlocku alebo segfaultu. Pri vlastnom skripte nastavte premennú pred importom knižníc:

```bash
OMP_NUM_THREADS=1 uv run python ...
```

## Detekcia podvýroby (`solarops_ml.detection`, issues #112–#114, #118)

Pravidlo z ADR #194 nad hodinovou radou skutočného a predikovaného (očakávaného) výkonu. Detektor model netrénuje ani nevolá, predikciu berie ako vstup. Hodina je platná pri dostatočnom žiarení (GHI > 50 W/m²), `P_expected >= 5 % P_rated`, pokrytí vzoriek >= 75 %, stave striedača mimo `OFF/SLEEPING/FAULT` a bez snehu; `P_expected` sa orezáva na AC limit. Podvýroba = `r = P_actual / P_expected < 0,85`; udalosť vznikne po >= 2 po sebe idúcich platných hodinách a zatvorí sa po 2 platných hodinách s `r >= 0,85`. Neplatné hodiny (noc, chýbajúce dáta) udalosť ani neprerušia, ani nepredĺžia. Pri oblačnosti >= 85 % klesá prah na 0,70 (znížená citlivosť). Závažnosť: medián `r` < 0,50 high, < 0,70 medium, inak low; udalosť dlhšia ako 72 h stupeň zvýši. Všetky prahy sú v `DetectionConfig`.

Endpoint `POST /v1/detections/underproduction` (kontrakt v `solarops_ml.detection.api`, OpenAPI na `/docs`): vstup `installationId`, `ratedPowerW`, `acLimitW?`, `points[]` (`timestamp` = koniec hodiny v UTC, `actualPowerW` alebo `null`, `expectedPowerW`, `ghiWm2?`, `cloudCoverPct?`, `snowDepthCm?`, `sampleCoverage?`, `status?`), `parameters?`, `includeHours?`; výstup `alarms[]` (`alarmId` = UUIDv5 z inštalácie a začiatku, `start`, `end`, `detectedAt`, `ongoing`, `severity`, `ticketPriority`, `medianRatio`, `estimatedLostKwh`, `reasonCode`, `reason`, `evidence[]`) a `summary` s počtom vylúčených hodín podľa dôvodu. Z 5-minútovej telemetrie hodinovú radu vyrobí `hourly_from_readings` (priemer prijatých vzoriek, chýbajúce vzorky nie sú 0 W).

Vyhodnotenie na simulovaných poruchách (40 scenárov, seed 42, optimistická horná hranica podľa ADR #187): výsledky sú v `reports/detection-eval.md` a `.json`, beh:

```bash
cd ml
uv run python -m solarops_ml.detection --seed 42 --scenarios 40 --days 14
```

## Predikcia výroby: tréning a servovanie (#30 a #31, stories #106 až #109)

Balík `solarops_ml.prediction`. Podľa ADR #191 je MVP **fyzikálny model** (PVWatts-like, NOCT teplota bunky, AC clipping). Tréning kalibruje dva parametre (`loss_factor`, `gamma_per_k`) metódou najmenších štvorcov v čistom numpy, takže servis nepotrebuje LightGBM ani scikit-learn. Súbor modelu je JSON, nie pickle. LightGBM je voliteľný challenger (`--challenger`, `uv sync --group experiment`) a nasadí sa len po schválení tímu.

### Príznaky (#106)

Jedna funkcia `build_features` slúži tréningu aj API (žiadny train/serve skew). Hodina je označená **začiatkom** (`hour_start_utc`, ADR #189); poloha slnka sa počíta v strede hodiny. Zoznam príznakov, jednotky a pravidlá sú v docstringu `solarops_ml/prediction/features.py`. Bez úniku dát: riadok používa len počasie tej istej hodiny a statické metadáta (žiadne lagy výroby, žiadna telemetrická `temperature_c`; test mení budúce riadky a kontroluje, že minulé príznaky ostanú rovnaké).

### Tréning (#107, #108)

```bash
cd ml
# offline na simulátore (#94), reprodukovateľné cez --seed
uv run python -m solarops_ml.prediction train --synthetic --start 2023-01-01 --end 2025-01-01 --seed 42
uv run python -m solarops_ml.prediction list           # verzie, * = aktuálna
uv run python -m solarops_ml.prediction use <verzia>   # rollback
```

Porovnáva sa `persistence`, `physical`, `physical_calibrated` (a `lightgbm` s `--challenger`) na **rovnakom** časovom delení (posledných `--test-days` testovacích, `--gap-days` medzera) a rovnakých riadkoch (denné hodiny, ADR #193); metriky MAE, RMSE, nMAE, wMAPE, R² sú mediánom po poliach. Výber a odôvodnenie (vrátane brány ADR #191: skill ≥ 0,20 a nMAE ≤ 6 %) sa uložia do metadát.

Artefakt: `ml/data/models/<YYYYMMDD>-<model>-<hash dát 8>/` (v `.gitignore`; zmena cez `SOLAROPS_MODEL_DIR`) s `model.json`, `metadata.json` (verzia, seed, SHA-256 dát, delenie, metriky, výber, verzie python/numpy/pandas) a `metrics.md`. `current.json` ukazuje na verziu, ktorú servis používa.

### Tréning na reálnych dátach (DKASC + Open-Meteo)

Stĺpce CSV z DKASC nie sú v ADR overené, preto sa očakávajú tri vlastné súbory, ktoré pripraví ETL:

1. **výroba** (`.csv`/`.parquet`): `hour_start_utc, array_id, power_w`. Hodinový priemer AC výkonu v W, hodina označená začiatkom v UTC (`Australia/Darwin` = UTC+9:30, lokálna polnoc = 14:30 UTC), prázdne pri menej než 9 z 12 vzoriek (ADR #183).
2. **polia** (`.csv`): `array_id, tilt, azimuth, capacity_kwp[, dc_ac_ratio]` (azimut 0 = juh, 180 = sever).
3. **počasie**: `solarops-weather-2` z `python -m solarops_ml.weather fetch` (Historical Forecast). Čas koncový sa pri načítaní posúva o -1 h.

```bash
uv run python -m solarops_ml.weather fetch --lat -23.76 --lon 133.87 --start 2023-01-01 --end 2024-12-31 --out data/weather/alice.parquet
uv run python -m solarops_ml.prediction build-table --production data/dkasc/hourly.csv --weather data/weather/alice.parquet \
    --arrays data/dkasc/arrays.csv --lat -23.76 --lon 133.87 --out data/train/table.parquet
uv run python -m solarops_ml.prediction train --table data/train/table.parquet --seed 42
```

GTI z počasia sa pri `build-table` predvolene ignoruje (platí len pre jednu rovinu); použije sa transpozícia GHI/DNI/DHI na sklon a azimut poľa (`--poa-source auto` použije GTI zo súboru). Výsledok na syntetických dátach je dôkaz pipeline, nie presnosti (ADR #191).

### Služba (#109)

`uv run uvicorn solarops_ml.api:app --port 8000`, dokumentácia na `/docs` a `/openapi.json`.

- `GET /health` -> `{"status":"ok","version":"0.1.0","modelVersion":"..."}` (503 `status: unavailable`, ak nastavený artefakt nejde načítať).
- `POST /predict` (rovnaký handler aj na `/v1/predictions/production`, ADR #196). Požiadavka podľa ADR #196: `plant` (`latitude`, `longitude`, `dcCapacityKw`, `tiltDeg`, `azimuthDeg` s 0 = juh, voliteľne `dcAcRatio`, predvolene 1,2) a `weather` (1 až 168 hodín: `timestamp` = začiatok hodiny v UTC, `ghiWm2`+`dniWm2`+`dhiWm2` alebo `gtiWm2`, `temperatureC`, voliteľne `cloudCoverPct`, `windSpeedMs`). Odpoveď: `modelVersion`, `generatedAt`, `predictions[{timestamp, expectedPowerW, expectedEnergyWh}]`.
- Chyby sú `application/problem+json` s `traceId`: 422 pri neplatnom vstupe, 503 pri nenačítanom modeli.

Bez uloženého modelu beží nekalibrovaný fyzikálny baseline (verzia `physical-baseline-<verzia kódu>`).

# solarops-ml

ML služba SolarOps (FastAPI). Spustenie: `uv run uvicorn solarops_ml.api:app --reload --port 8000`.

Testy a lint (rovnako ako CI `ml.yml`): `uv run pytest`, `uv run ruff check .`, `uv run ruff format --check .`.

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

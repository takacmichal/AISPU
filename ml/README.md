# solarops-ml

ML služba SolarOps (FastAPI). Spustenie: `uv run uvicorn solarops_ml.api:app --reload --port 8000`.

Testy a lint (rovnako ako CI `ml.yml`): `uv run pytest`, `uv run ruff check .`, `uv run ruff format --check .`.

## Historické počasie (`solarops_ml.weather`)

Sťahuje počasie z [Open-Meteo](https://open-meteo.com) podľa ADR #180 a #189:

- **Historical Forecast API** (archivované predpovede, od 2022, rovnaký model ako produkčná predpoveď) alebo
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

Lokality sú v `config/locations.toml` (`[locations.<meno>]` s `latitude`, `longitude`, `label`).

### Formát dát (`solarops-weather-1`)

Výstup: `data/weather/<lokalita>_<od>_<do>_<source>_<resolution>.parquet` (alebo `.csv`). Jeden riadok = jeden interval.

| stĺpec | jednotka | význam |
|---|---|---|
| `time` | UTC | **koniec** intervalu; hodnoty sú priemer/súčet za predchádzajúci interval (Open-Meteo „preceding hour“). Parquet `timestamp[ns, tz=UTC]`, CSV ISO 8601 so `Z` |
| `ghi_wm2` | W/m² | globálne horizontálne žiarenie (`shortwave_radiation`) |
| `dni_wm2` | W/m² | priame normálové žiarenie (`direct_normal_irradiance`) |
| `dhi_wm2` | W/m² | difúzne horizontálne žiarenie (`diffuse_radiation`) |
| `cloud_cover_pct` | % | celková oblačnosť |
| `temp_air_c` | °C | teplota vzduchu 2 m |
| `wind_speed_ms` | m/s | rýchlosť vetra 10 m |
| `precipitation_mm` | mm | zrážky za interval (reset soilingu, ADR #187) |
| `latitude`, `longitude`, `elevation` | °, m | bod mriežky, ktorý API skutočne použilo (ADR #189) |
| `weather_source` | text | `historical-forecast` alebo `archive` |

Chýbajúce hodnoty (`null` z API) sú `NaN`. Parquet nesie v metadátach `solarops.attribution` a `solarops.format`.
Načítanie: `solarops_ml.weather.read_dataset(path)` vráti `DataFrame` indexovaný UTC časom.

**Atribúcia (povinná, CC BY 4.0):** Weather data by [Open-Meteo.com](https://open-meteo.com), licensed under [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/).

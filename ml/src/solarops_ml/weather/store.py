"""Chunked download with a parquet cache and the documented on-disk dataset format.

Dataset format (one row per interval, see ``ml/README.md``):

=================  =========  ==========================================================
column             unit       meaning
=================  =========  ==========================================================
time               UTC        end of the interval for radiation (mean) and precipitation
                              (sum) over the preceding interval; temperature, wind and cloud
                              cover are instantaneous at ``time``
                              (``timestamp[ns, tz=UTC]`` in parquet)
ghi_wm2            W/m²       global horizontal irradiance (``shortwave_radiation``)
dni_wm2            W/m²       direct normal irradiance
dhi_wm2            W/m²       diffuse horizontal irradiance (``diffuse_radiation``)
gti_wm2            W/m²       global tilted irradiance (plane set by tilt/azimuth, else = GHI)
cloud_cover_pct    %          total cloud cover
temp_air_c         °C         air temperature 2 m
wind_speed_ms      m/s        wind speed 10 m
precipitation_mm   mm         precipitation sum over the interval
latitude           °          grid point actually used by Open-Meteo
longitude          °          grid point actually used by Open-Meteo
elevation          m          elevation of the grid point
weather_source     text       ``historical-forecast`` or ``archive`` (ADR #180, #189)
=================  =========  ==========================================================
"""

from __future__ import annotations

import logging
import os
from datetime import UTC, date, datetime, timedelta
from pathlib import Path

import pandas as pd
import pyarrow as pa
import pyarrow.parquet as pq

from solarops_ml.weather.client import (
    ATTRIBUTION,
    VARIABLES,
    OpenMeteoClient,
    Resolution,
    WeatherSource,
)

log = logging.getLogger(__name__)

FORMAT = "solarops-weather-2"  # v2 added ``gti_wm2`` (v1 had no GTI column)

COLUMNS = [*VARIABLES.values(), "latitude", "longitude", "elevation", "weather_source"]

# Recent days may still be revised (ERA5 lags ~5 days), so chunks that end this close to
# "today" are fetched but never written to the cache.
CACHE_SAFETY_DAYS = 7


def default_data_dir() -> Path:
    """``$SOLAROPS_DATA_DIR`` or ``ml/data`` (gitignored) next to the package sources."""
    env = os.environ.get("SOLAROPS_DATA_DIR")
    if env:
        return Path(env)
    return Path(__file__).resolve().parents[3] / "data"


def month_chunks(start: date, end: date) -> list[tuple[date, date]]:
    """Split an inclusive date range into calendar-month pieces (inclusive, gap-free)."""
    if end < start:
        raise ValueError(f"end {end} is before start {start}")
    chunks: list[tuple[date, date]] = []
    cursor = start
    while cursor <= end:
        next_month = (cursor.replace(day=1) + timedelta(days=32)).replace(day=1)
        chunk_end = min(end, next_month - timedelta(days=1))
        chunks.append((cursor, chunk_end))
        cursor = chunk_end + timedelta(days=1)
    return chunks


def chunk_cache_path(
    cache_dir: Path,
    source: WeatherSource,
    resolution: Resolution,
    latitude: float,
    longitude: float,
    start: date,
    end: date,
    tilt: float | None = None,
    azimuth: float | None = None,
) -> Path:
    cell = f"lat{latitude:.4f}_lon{longitude:.4f}"
    if tilt is not None or azimuth is not None:
        cell += f"_tilt{tilt}_az{azimuth}"
    return (
        cache_dir
        / source.value
        / resolution.value
        / cell
        / f"{start.isoformat()}_{end.isoformat()}.parquet"
    )


def fetch_weather(
    client: OpenMeteoClient,
    latitude: float,
    longitude: float,
    start: date,
    end: date,
    *,
    source: WeatherSource | None = None,
    resolution: Resolution = Resolution.HOURLY,
    data_dir: Path | None = None,
    use_cache: bool = True,
    today: date | None = None,
    tilt: float | None = None,
    azimuth: float | None = None,
) -> pd.DataFrame:
    """Download (or load from cache) weather for one location; returns the documented format."""
    source = source or WeatherSource.auto(start)
    cache_dir = (data_dir or default_data_dir()) / "weather" / "cache" / FORMAT
    today = today or datetime.now(UTC).date()
    parts: list[pd.DataFrame] = []
    for chunk_start, chunk_end in month_chunks(start, end):
        path = chunk_cache_path(
            cache_dir,
            source,
            resolution,
            latitude,
            longitude,
            chunk_start,
            chunk_end,
            tilt,
            azimuth,
        )
        if use_cache and path.exists():
            log.debug("cache hit %s", path)
            parts.append(read_dataset(path))
            continue
        frame, grid = client.fetch(
            latitude,
            longitude,
            chunk_start,
            chunk_end,
            source,
            resolution,
            tilt=tilt,
            azimuth=azimuth,
        )
        frame["latitude"] = grid.latitude
        frame["longitude"] = grid.longitude
        frame["elevation"] = grid.elevation if grid.elevation is not None else float("nan")
        frame["weather_source"] = source.value
        frame = frame[COLUMNS]
        if use_cache and chunk_end <= today - timedelta(days=CACHE_SAFETY_DAYS):
            write_dataset(frame, path)
        parts.append(frame)
    combined = pd.concat(parts).sort_index()
    return combined[~combined.index.duplicated(keep="first")]


def write_dataset(frame: pd.DataFrame, path: Path) -> Path:
    """Write the dataset to parquet (or CSV if the suffix is ``.csv``) with attribution."""
    path.parent.mkdir(parents=True, exist_ok=True)
    table_frame = frame.reset_index()
    if path.suffix == ".csv":
        out = table_frame.copy()
        out["time"] = out["time"].dt.strftime("%Y-%m-%dT%H:%M:%SZ")
        out.to_csv(path, index=False)
        return path
    table = pa.Table.from_pandas(table_frame, preserve_index=False)
    metadata = dict(table.schema.metadata or {})
    metadata[b"solarops.attribution"] = ATTRIBUTION.encode()
    metadata[b"solarops.format"] = FORMAT.encode()
    pq.write_table(table.replace_schema_metadata(metadata), path)
    return path


def read_dataset(path: Path) -> pd.DataFrame:
    """Read a dataset written by :func:`write_dataset` (parquet or CSV) indexed by UTC time."""
    if path.suffix == ".csv":
        frame = pd.read_csv(path)
        frame["time"] = pd.to_datetime(frame["time"], utc=True)
    else:
        frame = pd.read_parquet(path)
    frame["time"] = pd.to_datetime(frame["time"], utc=True).astype("datetime64[ns, UTC]")
    return frame.set_index("time")

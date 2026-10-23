"""Historical weather from Open-Meteo (ADR #180, #189), cached as parquet under ``ml/data``."""

from solarops_ml.weather.client import (
    ATTRIBUTION,
    GridPoint,
    OpenMeteoClient,
    OpenMeteoError,
    Resolution,
    WeatherSource,
    parse_response,
)
from solarops_ml.weather.locations import Location, load_locations
from solarops_ml.weather.store import (
    COLUMNS,
    default_data_dir,
    fetch_weather,
    month_chunks,
    read_dataset,
    write_dataset,
)

__all__ = [
    "ATTRIBUTION",
    "COLUMNS",
    "GridPoint",
    "Location",
    "OpenMeteoClient",
    "OpenMeteoError",
    "Resolution",
    "WeatherSource",
    "default_data_dir",
    "fetch_weather",
    "load_locations",
    "month_chunks",
    "parse_response",
    "read_dataset",
    "write_dataset",
]

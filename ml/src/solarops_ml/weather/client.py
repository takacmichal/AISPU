"""Open-Meteo client for historical weather (ADR #180, #189).

All requests use ``timezone=UTC`` and ``timeformat=unixtime`` so timestamps are unambiguous,
and ``wind_speed_unit=ms``. Radiation (and precipitation) values are the mean (sum) of the
*preceding* hour, so the timestamp of a row is the end of the interval it describes
(right-labelled). Temperature, wind speed and cloud cover are instantaneous values at the timestamp.

Training data comes from the Historical Forecast API (archived operational forecasts, same models
as the Forecast API used for serving; team decision on issue #180). The archive (ERA5) API is only
the fallback for dates before ``HISTORICAL_FORECAST_FIRST_DAY``.
"""

from __future__ import annotations

import logging
import time
from collections.abc import Callable
from dataclasses import dataclass
from datetime import date
from enum import StrEnum

import httpx
import pandas as pd

log = logging.getLogger(__name__)

ATTRIBUTION = "Weather data by Open-Meteo.com (https://open-meteo.com), licensed under CC BY 4.0"

HISTORICAL_FORECAST_URL = "https://historical-forecast-api.open-meteo.com/v1/forecast"
ARCHIVE_URL = "https://archive-api.open-meteo.com/v1/archive"

# The Historical Forecast API archives model runs from roughly 2022 on (spike #180).
HISTORICAL_FORECAST_FIRST_DAY = date(2022, 1, 1)

# Open-Meteo variable name -> column name in the stored dataset (units in the suffix).
VARIABLES: dict[str, str] = {
    "shortwave_radiation": "ghi_wm2",
    "direct_normal_irradiance": "dni_wm2",
    "diffuse_radiation": "dhi_wm2",
    "global_tilted_irradiance": "gti_wm2",
    "cloud_cover": "cloud_cover_pct",
    "temperature_2m": "temp_air_c",
    "wind_speed_10m": "wind_speed_ms",
    "precipitation": "precipitation_mm",
}

RETRYABLE_STATUS = frozenset({429, 500, 502, 503, 504})


class WeatherSource(StrEnum):
    """Which Open-Meteo API the data came from (stored as ``weather_source``)."""

    HISTORICAL_FORECAST = "historical-forecast"
    ARCHIVE = "archive"

    @property
    def url(self) -> str:
        return HISTORICAL_FORECAST_URL if self is WeatherSource.HISTORICAL_FORECAST else ARCHIVE_URL

    @classmethod
    def auto(cls, start: date) -> WeatherSource:
        """Prefer archived forecasts (same model as production, ADR #180) when they exist."""
        return cls.HISTORICAL_FORECAST if start >= HISTORICAL_FORECAST_FIRST_DAY else cls.ARCHIVE


class Resolution(StrEnum):
    HOURLY = "hourly"
    MINUTELY_15 = "minutely_15"

    @property
    def freq(self) -> str:
        return "1h" if self is Resolution.HOURLY else "15min"


class OpenMeteoError(RuntimeError):
    """Open-Meteo returned an error that retrying will not fix (or retries ran out)."""


@dataclass(frozen=True)
class GridPoint:
    """Coordinates the API actually used (it snaps requests to its model grid)."""

    latitude: float
    longitude: float
    elevation: float | None


class OpenMeteoClient:
    """Thin, rate-limit friendly wrapper over the Open-Meteo historical APIs.

    - retries transport errors, 429 and 5xx with exponential backoff (honours ``Retry-After``),
    - keeps at least ``min_interval_s`` between requests,
    - never retries 4xx other than 429 (bad parameters, e.g. a date range out of coverage).
    """

    def __init__(
        self,
        http: httpx.Client | None = None,
        *,
        max_retries: int = 4,
        backoff_s: float = 1.0,
        min_interval_s: float = 0.25,
        timeout_s: float = 60.0,
        sleep: Callable[[float], None] = time.sleep,
        clock: Callable[[], float] = time.monotonic,
    ) -> None:
        self._http = http or httpx.Client(timeout=timeout_s, headers={"User-Agent": "solarops-ml"})
        self._max_retries = max_retries
        self._backoff_s = backoff_s
        self._min_interval_s = min_interval_s
        self._sleep = sleep
        self._clock = clock
        self._last_request: float | None = None

    def close(self) -> None:
        self._http.close()

    def __enter__(self) -> OpenMeteoClient:
        return self

    def __exit__(self, *exc: object) -> None:
        self.close()

    def fetch(
        self,
        latitude: float,
        longitude: float,
        start: date,
        end: date,
        source: WeatherSource,
        resolution: Resolution = Resolution.HOURLY,
        *,
        tilt: float | None = None,
        azimuth: float | None = None,
    ) -> tuple[pd.DataFrame, GridPoint]:
        """Fetch one date range (inclusive) and return a UTC-indexed frame plus the grid point.

        ``tilt`` (0 = horizontal, 90 = vertical) and ``azimuth`` (0 = south, -90 = east,
        90 = west, +-180 = north) set the plane for ``gti_wm2``. When omitted the API uses a
        horizontal plane, so GTI equals GHI.
        """
        if end < start:
            raise ValueError(f"end {end} is before start {start}")
        if resolution is Resolution.MINUTELY_15 and source is WeatherSource.ARCHIVE:
            raise ValueError("15-minute data is only available from the historical-forecast API")
        params = {
            "latitude": latitude,
            "longitude": longitude,
            "start_date": start.isoformat(),
            "end_date": end.isoformat(),
            resolution.value: ",".join(VARIABLES),
            "timezone": "UTC",
            "timeformat": "unixtime",
            "wind_speed_unit": "ms",
        }
        if tilt is not None:
            params["tilt"] = tilt
        if azimuth is not None:
            params["azimuth"] = azimuth
        payload = self._get_json(source.url, params)
        return parse_response(payload, resolution), GridPoint(
            latitude=float(payload["latitude"]),
            longitude=float(payload["longitude"]),
            elevation=_float_or_none(payload.get("elevation")),
        )

    def _get_json(self, url: str, params: dict[str, object]) -> dict:
        attempt = 0
        while True:
            self._throttle()
            try:
                response = self._http.get(url, params=params)
            except httpx.TransportError as exc:
                if attempt >= self._max_retries:
                    raise OpenMeteoError(f"request to {url} failed: {exc}") from exc
                delay = self._backoff_s * 2**attempt
                log.warning("Open-Meteo transport error (%s), retrying in %.1fs", exc, delay)
            else:
                if response.status_code == 200:
                    return response.json()
                if response.status_code not in RETRYABLE_STATUS or attempt >= self._max_retries:
                    raise OpenMeteoError(
                        f"Open-Meteo HTTP {response.status_code}: {_reason(response)}"
                    )
                delay = _retry_after(response) or self._backoff_s * 2**attempt
                log.warning("Open-Meteo HTTP %s, retrying in %.1fs", response.status_code, delay)
            attempt += 1
            self._sleep(delay)

    def _throttle(self) -> None:
        now = self._clock()
        if self._last_request is not None:
            wait = self._min_interval_s - (now - self._last_request)
            if wait > 0:
                self._sleep(wait)
                now += wait
        self._last_request = now


def parse_response(payload: dict, resolution: Resolution = Resolution.HOURLY) -> pd.DataFrame:
    """Convert an Open-Meteo JSON block into a frame indexed by tz-aware UTC ``time``."""
    block = payload.get(resolution.value)
    if not block or "time" not in block:
        raise OpenMeteoError(f"response has no '{resolution.value}' block")
    times = pd.to_datetime(block["time"], unit="s", utc=True).astype("datetime64[ns, UTC]")
    index = pd.DatetimeIndex(times, name="time")
    columns = {
        column: pd.to_numeric(pd.Series(block.get(variable), dtype="float64"), errors="coerce")
        if block.get(variable) is not None
        else pd.Series([float("nan")] * len(index), dtype="float64")
        for variable, column in VARIABLES.items()
    }
    frame = pd.DataFrame({k: v.to_numpy() for k, v in columns.items()}, index=index)
    return frame


def _retry_after(response: httpx.Response) -> float | None:
    value = response.headers.get("Retry-After")
    if value is None:
        return None
    try:
        return max(0.0, float(value))
    except ValueError:
        return None


def _reason(response: httpx.Response) -> str:
    try:
        return str(response.json().get("reason", response.text))
    except ValueError:
        return response.text[:200]


def _float_or_none(value: object) -> float | None:
    return None if value is None else float(value)  # type: ignore[arg-type]

"""Feature engineering for the production forecast (issue #106, ADR #189, #191).

One function, :func:`build_features`, serves both training and the prediction API, so the
features cannot drift between the two (train/serve skew).

Time convention (ADR #189): every row is one UTC hour labelled by its **start**
(``hour_start_utc``), i.e. it describes ``[h, h + 1h)``. Raw Open-Meteo rows are labelled by
the *end* of the hour; :func:`from_open_meteo` shifts them by -1 h.

Input weather columns (hourly means for irradiance, values for the hour otherwise):

=================  =====  ========  ===============================================================
column             unit   required  meaning
=================  =====  ========  ===============================================================
ghi_wm2            W/m²   *         global horizontal irradiance
dni_wm2            W/m²   *         direct normal irradiance
dhi_wm2            W/m²   *         diffuse horizontal irradiance
gti_wm2            W/m²   *         irradiance on the panel plane (Open-Meteo GTI for this plane)
temp_air_c         °C     yes       air temperature 2 m (never the inverter/module temperature)
cloud_cover_pct    %      no        total cloud cover (missing -> 0, only a model feature)
wind_speed_ms      m/s    no        not used by the physical model, kept for challengers
=================  =====  ========  ===============================================================

\\* Per row either ``gti_wm2`` or all three of GHI/DNI/DHI are needed.

Output features (``FEATURES`` is the set the spike #191 compared, plus derived columns):

==============  ======  =====================================================================
column          unit    meaning
==============  ======  =====================================================================
gti_wm2         W/m²    plane-of-array irradiance: weather GTI if given (``poa_source="auto"``),
                        otherwise isotropic transposition of GHI/DNI/DHI at the hour midpoint
cloud_cover_pct %       cloud cover from the weather (0 when missing)
air_temp_c      °C      air temperature from the weather
clearsky_gti    W/m²    clear-sky irradiance on the plane at the hour midpoint
kt              1       clearness on the plane ``gti / clearsky_gti`` (0 at night, max 1.5)
solar_zenith    °       sun zenith at the hour midpoint
solar_azimuth   °       sun azimuth at the hour midpoint (compass, 0 = north)
hour_sin/cos    1       cyclic UTC hour of the midpoint
doy_sin/cos     1       cyclic day of year
tilt, azimuth   °       panel geometry (azimuth 0 = south, -90 = east, 90 = west, ±180 = north)
capacity_kwp    kWp     installed DC capacity
dc_ac_ratio     1       DC/AC ratio (AC clipping limit = capacity / ratio)
cell_temp_c     °C      NOCT cell temperature from ``air_temp_c`` and ``gti_wm2``
p_baseline      W/Wp    uncalibrated physical model (ADR #191 MVP), AC power per installed Wp
is_day          bool    ``gti_wm2 > 50 W/m²`` (evaluation population, ADR #193)
==============  ======  =====================================================================

No leakage: each row uses only the weather of the same hour and static plant metadata. No
lagged or future production values, and no telemetry ``temperature_c`` (it contains a part
of the target, ADR #191).
"""

from __future__ import annotations

from dataclasses import dataclass
from typing import Literal

import numpy as np
import pandas as pd

from solarops_ml.simulator.generator import GAMMA_PER_K, INVERTER_EFFICIENCY, NOCT_C
from solarops_ml.simulator.solar import clear_sky, plane_irradiance, solar_position

INDEX_NAME = "hour_start_utc"
DAY_GTI_WM2 = 50.0  # daylight threshold of ADR #193/#194
DEFAULT_DC_AC_RATIO = 1.2

FEATURES = [
    "gti_wm2",
    "cloud_cover_pct",
    "air_temp_c",
    "clearsky_gti",
    "kt",
    "solar_zenith",
    "solar_azimuth",
    "hour_sin",
    "hour_cos",
    "doy_sin",
    "doy_cos",
    "tilt",
    "azimuth",
    "capacity_kwp",
    "dc_ac_ratio",
]

PoaSource = Literal["auto", "transpose"]


@dataclass(frozen=True)
class PlantSpec:
    """Static metadata of one installation (or one array of a site)."""

    latitude: float
    longitude: float
    capacity_kwp: float
    tilt: float
    azimuth: float  # 0 = south, -90 = east, 90 = west, ±180 = north (PVGIS convention)
    dc_ac_ratio: float = DEFAULT_DC_AC_RATIO

    def __post_init__(self) -> None:
        checks = [
            (-90 <= self.latitude <= 90, "latitude out of range"),
            (-180 <= self.longitude <= 180, "longitude out of range"),
            (self.capacity_kwp > 0, "capacity_kwp must be positive"),
            (0 <= self.tilt <= 90, "tilt must be within 0..90"),
            (-180 <= self.azimuth <= 180, "azimuth must be within -180..180"),
            (self.dc_ac_ratio > 0, "dc_ac_ratio must be positive"),
        ]
        for ok, message in checks:
            if not ok:
                raise ValueError(message)


def from_open_meteo(frame: pd.DataFrame) -> pd.DataFrame:
    """Relabel an Open-Meteo frame (``time`` = end of the hour) to ``hour_start_utc``."""
    shifted = frame.copy()
    shifted.index = pd.DatetimeIndex(frame.index - pd.Timedelta(hours=1), name=INDEX_NAME)
    return shifted


def physical_power(
    gti_wm2: np.ndarray,
    air_temp_c: np.ndarray,
    dc_ac_ratio: float | np.ndarray,
    *,
    loss_factor: float = 1.0,
    gamma_per_k: float = GAMMA_PER_K,
    noct_c: float = NOCT_C,
    inverter_efficiency: float = INVERTER_EFFICIENCY,
) -> np.ndarray:
    """AC power per installed Wp (W/Wp): PVWatts-like DC, NOCT temperature, AC clipping.

    Same equations as the simulator (#94) and the spike #191 baseline; ``loss_factor``
    scales the DC output (soiling, wiring, mismatch) and is what calibration fits.
    """
    gti = np.asarray(gti_wm2, dtype=float)
    cell = np.asarray(air_temp_c, dtype=float) + gti * (noct_c - 20) / 800
    temp_factor = np.clip(1 + gamma_per_k * (cell - 25), 0, None)
    ac = loss_factor * inverter_efficiency * gti / 1000 * temp_factor
    return np.clip(np.minimum(ac, 1 / np.asarray(dc_ac_ratio, dtype=float)), 0, None)


def build_features(
    weather: pd.DataFrame, plant: PlantSpec, *, poa_source: PoaSource = "auto"
) -> pd.DataFrame:
    """Return one feature row per weather hour (index ``hour_start_utc``)."""
    index = _validate_index(weather.index)
    if "temp_air_c" not in weather.columns:
        raise ValueError("weather needs column temp_air_c (air temperature, °C)")
    if poa_source not in ("auto", "transpose"):
        raise ValueError("poa_source must be 'auto' or 'transpose'")

    middle = index + pd.Timedelta(minutes=30)
    position = solar_position(middle, plant.latitude, plant.longitude)
    zenith = position["zenith_deg"].to_numpy()
    sun_azimuth = position["azimuth_deg"].to_numpy()
    doy = np.asarray(middle.dayofyear)
    cs_ghi, cs_dni, cs_dhi = clear_sky(zenith, doy)
    clearsky_gti = plane_irradiance(
        zenith, sun_azimuth, cs_ghi, cs_dni, cs_dhi, plant.tilt, plant.azimuth
    )

    def column(name: str) -> np.ndarray:
        if name in weather.columns:
            return weather[name].to_numpy(dtype=float)
        return np.full(len(index), np.nan)

    transposed = plane_irradiance(
        zenith,
        sun_azimuth,
        column("ghi_wm2"),
        column("dni_wm2"),
        column("dhi_wm2"),
        plant.tilt,
        plant.azimuth,
    )
    gti = transposed
    if poa_source == "auto":
        weather_gti = column("gti_wm2")
        gti = np.where(np.isfinite(weather_gti), weather_gti, transposed)
    gti = np.clip(gti, 0, None)
    air_temp = column("temp_air_c")

    frame = pd.DataFrame(index=pd.DatetimeIndex(index, name=INDEX_NAME))
    frame["gti_wm2"] = gti
    frame["cloud_cover_pct"] = np.nan_to_num(column("cloud_cover_pct"), nan=0.0)
    frame["air_temp_c"] = air_temp
    frame["clearsky_gti"] = clearsky_gti
    safe_clearsky = np.where(clearsky_gti > DAY_GTI_WM2, clearsky_gti, np.nan)
    with np.errstate(divide="ignore", invalid="ignore"):
        kt = gti / safe_clearsky
    frame["kt"] = np.nan_to_num(kt, nan=0.0).clip(0, 1.5)
    frame["solar_zenith"] = zenith
    frame["solar_azimuth"] = sun_azimuth
    hour = np.asarray(middle.hour + middle.minute / 60)
    frame["hour_sin"] = np.sin(2 * np.pi * hour / 24)
    frame["hour_cos"] = np.cos(2 * np.pi * hour / 24)
    frame["doy_sin"] = np.sin(2 * np.pi * doy / 365.25)
    frame["doy_cos"] = np.cos(2 * np.pi * doy / 365.25)
    frame["tilt"] = float(plant.tilt)
    frame["azimuth"] = float(plant.azimuth)
    frame["capacity_kwp"] = float(plant.capacity_kwp)
    frame["dc_ac_ratio"] = float(plant.dc_ac_ratio)
    frame["cell_temp_c"] = air_temp + gti * (NOCT_C - 20) / 800
    frame["p_baseline"] = physical_power(gti, air_temp, plant.dc_ac_ratio)
    frame["is_day"] = gti > DAY_GTI_WM2
    return frame


def _validate_index(index: pd.Index) -> pd.DatetimeIndex:
    if not isinstance(index, pd.DatetimeIndex):
        raise ValueError("weather must be indexed by a DatetimeIndex (hour_start_utc)")
    if index.tz is None:
        raise ValueError("weather index must be timezone-aware (UTC)")
    index = index.tz_convert("UTC")
    if len(index) and not (index == index.floor("h")).all():
        raise ValueError("weather timestamps must be on a full hour (hour_start_utc)")
    if not index.is_monotonic_increasing or index.has_duplicates:
        raise ValueError("weather timestamps must be strictly increasing")
    return index

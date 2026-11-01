"""Hourly synthetic dataset and time splits for the model comparison spike (#191).

The real DKASC data cannot be committed (license, ADR #183) and is not available in
the repository, so the experiment runs on the deterministic simulator from issue #94.
All arrays share the same weather seed (the same cloud sequence) and differ only in
geometry, capacity and AC/DC ratio; that mimics one site with several arrays and keeps
leave-one-array-out honest about the intended generalisation target.
"""

from __future__ import annotations

from dataclasses import dataclass
from datetime import UTC, datetime

import numpy as np
import pandas as pd

from solarops_ml.simulator import SimulatorConfig, simulate
from solarops_ml.simulator.generator import GAMMA_PER_K, INVERTER_EFFICIENCY, NOCT_C
from solarops_ml.simulator.solar import clear_sky, plane_irradiance, solar_position

LATITUDE = -23.76
LONGITUDE = 133.87
UTC_OFFSET_HOURS = 9.5
DAY_GHI_WM2 = 50.0
MIN_VALID_SAMPLES_PER_HOUR = 9  # ADR #183: at least 75 % of 5-minute samples

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


@dataclass(frozen=True)
class ArraySpec:
    """One simulated array on the shared site."""

    name: str
    tilt: float
    azimuth: float
    capacity_kwp: float
    dc_ac_ratio: float = 1.2

    def __post_init__(self) -> None:
        if not self.name:
            raise ValueError("array name must not be empty")
        if not 0 <= self.tilt <= 90:
            raise ValueError("tilt must be within 0..90")
        if not -180 <= self.azimuth <= 180:
            raise ValueError("azimuth must be within -180..180")
        if self.capacity_kwp <= 0:
            raise ValueError("capacity_kwp must be positive")
        if self.dc_ac_ratio <= 0:
            raise ValueError("dc_ac_ratio must be positive")


DEFAULT_ARRAYS = (
    ArraySpec("south_15", tilt=15, azimuth=180, capacity_kwp=6, dc_ac_ratio=1.15),
    ArraySpec("south_20", tilt=20, azimuth=180, capacity_kwp=8, dc_ac_ratio=1.20),
    ArraySpec("south_25", tilt=25, azimuth=180, capacity_kwp=10, dc_ac_ratio=1.25),
    ArraySpec("south_30", tilt=30, azimuth=180, capacity_kwp=12, dc_ac_ratio=1.30),
    ArraySpec("south_35", tilt=35, azimuth=180, capacity_kwp=15, dc_ac_ratio=1.20),
)


@dataclass(frozen=True)
class Fold:
    """Expanding-window fold with a gap between training and test (ADR #191)."""

    name: str
    train_start: pd.Timestamp
    test_start: pd.Timestamp
    test_end: pd.Timestamp
    gap_days: int = 7

    def __post_init__(self) -> None:
        for value in (self.train_start, self.test_start, self.test_end):
            if value.tzinfo is None:
                raise ValueError("fold timestamps must be timezone-aware")
        if not self.train_start < self.test_start < self.test_end:
            raise ValueError("expected train_start < test_start < test_end")
        if self.gap_days < 0:
            raise ValueError("gap_days must be >= 0")

    @property
    def train_end(self) -> pd.Timestamp:
        return self.test_start - pd.Timedelta(days=self.gap_days)


def default_folds(start: pd.Timestamp, end: pd.Timestamp) -> tuple[Fold, Fold]:
    """The two walk-forward folds documented in ADR #191 (2024 H1 and H2)."""
    start = _utc(start)
    end = _utc(end)
    if start > pd.Timestamp("2023-01-01T00:00Z") or end < pd.Timestamp("2025-01-01T00:00Z"):
        raise ValueError("default folds need data from 2023-01-01 through 2024-12-31")
    first = Fold(
        "2024H1",
        train_start=start,
        test_start=pd.Timestamp("2024-01-01T00:00Z"),
        test_end=pd.Timestamp("2024-07-01T00:00Z"),
    )
    second = Fold(
        "2024H2",
        train_start=start,
        test_start=pd.Timestamp("2024-07-01T00:00Z"),
        test_end=end,
    )
    if not start <= first.train_start < first.test_start < first.test_end <= end:
        raise ValueError("default folds need data from 2023-01-01 through 2024-12-31")
    return first, second


def _utc(value: pd.Timestamp | datetime) -> pd.Timestamp:
    stamp = pd.Timestamp(value)
    if stamp.tzinfo is None:
        return stamp.tz_localize(UTC)
    return stamp.tz_convert(UTC)


def _hourly_readings(
    spec: ArraySpec, start: pd.Timestamp, end: pd.Timestamp, seed: int
) -> pd.DataFrame:
    config = SimulatorConfig(
        device_id=f"spike-191-{spec.name}",
        latitude=LATITUDE,
        longitude=LONGITUDE,
        tilt=spec.tilt,
        azimuth=spec.azimuth,
        capacity_kwp=spec.capacity_kwp,
        dc_ac_ratio=spec.dc_ac_ratio,
        utc_offset_hours=UTC_OFFSET_HOURS,
        cloudiness=0.3,
        seed=seed,
    )
    result = simulate(config, start.to_pydatetime(), end.to_pydatetime())
    readings = result.readings
    group = readings.resample("1h", closed="left", label="left")
    hourly = group[["ac_power_w", "gti_wm2", "cloud_cover_pct", "temperature_c"]].mean()
    counts = group["ac_power_w"].count()
    hourly = hourly.reindex(pd.date_range(start, end, freq="1h", inclusive="left", tz="UTC"))
    hourly.loc[counts.reindex(hourly.index, fill_value=0) < MIN_VALID_SAMPLES_PER_HOUR] = np.nan
    return hourly


def _air_temperature(index: pd.DatetimeIndex) -> np.ndarray:
    """Exogenous air temperature, mirroring the simulator's deterministic weather model.

    The simulator's telemetry column ``temperature_c`` is a module-temperature proxy
    derived partly from actual AC power, so it must never be a model feature. This
    helper reconstructs only the weather part (season + time of day) from the timestamp.
    """
    local = index + pd.Timedelta(hours=UTC_OFFSET_HOURS)
    lat = abs(LATITUDE)
    mean = 28 - 0.4 * lat
    seasonal_amp = 3 + 0.18 * lat
    peak_doy = 200 if LATITUDE >= 0 else 20
    doy = np.asarray(local.dayofyear)
    hour = np.asarray(local.hour + local.minute / 60)
    seasonal = seasonal_amp * np.cos(2 * np.pi * (doy - peak_doy) / 365)
    diurnal = 6 * np.cos(2 * np.pi * (hour - 15) / 24)
    return mean + seasonal + diurnal


def _add_features(hourly: pd.DataFrame, spec: ArraySpec) -> pd.DataFrame:
    frame = hourly.copy()
    index = frame.index
    position = solar_position(index, LATITUDE, LONGITUDE)
    clear_ghi, clear_dni, clear_dhi = clear_sky(
        position["zenith_deg"].to_numpy(), np.asarray(index.dayofyear)
    )
    clearsky_gti = plane_irradiance(
        position["zenith_deg"].to_numpy(),
        position["azimuth_deg"].to_numpy(),
        clear_ghi,
        clear_dni,
        clear_dhi,
        spec.tilt,
        spec.azimuth,
    )
    capacity_w = spec.capacity_kwp * 1000
    gti = frame["gti_wm2"].to_numpy(dtype=float)
    air_temp = _air_temperature(index)
    # NOCT cell-temperature model; no target or telemetry-derived module temperature.
    cell_temp = air_temp + gti * (NOCT_C - 20) / 800
    temp_factor = np.clip(1 + GAMMA_PER_K * (cell_temp - 25), 0, None)
    # Same units as the simulator: kWp * W/m2 gives W (GTI is per 1000 W/m2).
    dc_w = spec.capacity_kwp * gti * temp_factor
    ac_w = np.minimum(dc_w * INVERTER_EFFICIENCY, capacity_w / spec.dc_ac_ratio)

    frame["y"] = frame["ac_power_w"].to_numpy(dtype=float) / capacity_w
    frame["p_baseline"] = np.clip(ac_w, 0, None) / capacity_w
    frame["clearsky_gti"] = clearsky_gti
    safe_clearsky = np.where(clearsky_gti > DAY_GHI_WM2, clearsky_gti, np.nan)
    with np.errstate(divide="ignore", invalid="ignore"):
        kt = gti / safe_clearsky
    frame["kt"] = np.nan_to_num(kt, nan=0.0).clip(0, 1.5)
    frame["air_temp_c"] = air_temp
    frame["cell_temp_c"] = cell_temp
    frame["solar_zenith"] = position["zenith_deg"].to_numpy()
    frame["solar_azimuth"] = position["azimuth_deg"].to_numpy()
    hour = index.hour + index.minute / 60
    frame["hour_sin"] = np.sin(2 * np.pi * hour / 24)
    frame["hour_cos"] = np.cos(2 * np.pi * hour / 24)
    frame["doy_sin"] = np.sin(2 * np.pi * index.dayofyear / 365.25)
    frame["doy_cos"] = np.cos(2 * np.pi * index.dayofyear / 365.25)
    frame["tilt"] = spec.tilt
    frame["azimuth"] = spec.azimuth
    frame["capacity_kwp"] = spec.capacity_kwp
    frame["dc_ac_ratio"] = spec.dc_ac_ratio
    frame["array_id"] = spec.name
    frame["is_day"] = (gti > DAY_GHI_WM2) & frame["y"].notna()
    frame["y_lag_24h"] = frame["y"].shift(24)
    return frame


def build_dataset(
    start: pd.Timestamp | datetime,
    end: pd.Timestamp | datetime,
    arrays: tuple[ArraySpec, ...] = DEFAULT_ARRAYS,
    seed: int = 42,
) -> pd.DataFrame:
    """Simulate all arrays and return an hourly frame indexed by UTC time."""
    start_utc, end_utc = _utc(start), _utc(end)
    if end_utc <= start_utc:
        raise ValueError("end must be after start")
    if not arrays:
        raise ValueError("at least one array is required")
    frames = []
    for spec in arrays:
        hourly = _hourly_readings(spec, start_utc, end_utc, seed)
        frames.append(_add_features(hourly, spec))
    frame = pd.concat(frames, axis=0)
    frame.index.name = "timestamp"
    frame = frame.sort_index(kind="stable")
    return frame


def evaluation_mask(frame: pd.DataFrame, fold: Fold) -> pd.Series:
    """Rows comparable across all models: daylight, target and 24 h persistence known."""
    index = frame.index
    return (
        (index >= fold.test_start)
        & (index < fold.test_end)
        & frame["is_day"].fillna(False)
        & frame["y"].notna()
        & frame["y_lag_24h"].notna()
        & frame[FEATURES].notna().all(axis=1)
    )


def train_rows(frame: pd.DataFrame, fold: Fold) -> pd.DataFrame:
    index = frame.index
    mask = (index >= fold.train_start) & (index < fold.train_end)
    return frame.loc[mask]


def test_rows(frame: pd.DataFrame, fold: Fold) -> pd.DataFrame:
    index = frame.index
    mask = (index >= fold.test_start) & (index < fold.test_end)
    return frame.loc[mask]

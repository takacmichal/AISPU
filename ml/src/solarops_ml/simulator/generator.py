"""5-minute PV inverter telemetry generator (ADR #185, #187; issue #94).

Model: solar position + clear-sky plane-of-array irradiance, scaled by a seeded AR(1)
cloud-cover process, converted to DC/AC power (PVWatts-like, with inverter clipping).
Faults are applied as a transformation of the output; the fault-free ``expected_power_w``
and the fault flags go to ``labels`` (kept out of the ingest contract, ADR #185).

All random numbers are drawn regardless of faults, so a fault changes output only inside
its window and the same seed always yields the same series.
"""

from __future__ import annotations

import math
from dataclasses import dataclass, field
from datetime import datetime, timedelta
from typing import Literal

import numpy as np
import pandas as pd

from solarops_ml.simulator.solar import clear_sky, plane_irradiance, solar_position

INTERVAL_S = 300
FaultType = Literal["power_drop", "inverter_fault"]
FAULT_TYPES = ("power_drop", "inverter_fault")
INVERTER_FAULT_ERROR_CODE = 100

GAMMA_PER_K = -0.004  # module power temperature coefficient
NOCT_C = 45.0
INVERTER_EFFICIENCY = 0.96
NOISE_SIGMA = 0.005
MIN_PRODUCING_GTI_WM2 = 10.0
CLOUD_STD = 0.25
CLOUD_PHI = 0.97  # 5-min autocorrelation, correlation time of a few hours


@dataclass(frozen=True)
class SimulatorConfig:
    device_id: str
    latitude: float
    longitude: float
    tilt: float = 20.0  # degrees, 0 = horizontal
    azimuth: float = 180.0  # 0 = south, -90 = east, 90 = west, 180 = north (southern hemisphere)
    capacity_kwp: float = 10.0
    dc_ac_ratio: float = 1.2
    utc_offset_hours: float = 9.5  # local time for the dayWh reset (default: Alice Springs)
    cloudiness: float = 0.3  # mean cloud cover 0..1
    seed: int = 0
    string_count: int = 2
    initial_total_wh: float = 0.0

    def __post_init__(self) -> None:
        checks = [
            (-90 <= self.latitude <= 90, "latitude out of range"),
            (-180 <= self.longitude <= 180, "longitude out of range"),
            (0 <= self.tilt <= 90, "tilt out of range"),
            (-180 <= self.azimuth <= 180, "azimuth out of range"),
            (self.capacity_kwp > 0, "capacity_kwp must be positive"),
            (self.dc_ac_ratio > 0, "dc_ac_ratio must be positive"),
            (0 <= self.cloudiness <= 1, "cloudiness must be within 0..1"),
            (1 <= self.string_count <= 24, "string_count must be within 1..24"),
            (self.initial_total_wh >= 0, "initial_total_wh must be >= 0"),
        ]
        for ok, message in checks:
            if not ok:
                raise ValueError(message)


@dataclass(frozen=True)
class FaultSpec:
    """A fault active in ``[start, end)``.

    ``power_drop``: output multiplied by ``1 - severity`` (0..1).
    ``inverter_fault``: status ``FAULT``, ``errorCode`` != 0, output 0 (severity ignored).
    """

    type: str
    start: datetime
    end: datetime
    severity: float = 0.5

    def __post_init__(self) -> None:
        if self.type not in FAULT_TYPES:
            raise ValueError(f"unknown fault type {self.type!r}, expected one of {FAULT_TYPES}")
        if not 0 <= self.severity <= 1:
            raise ValueError("severity must be within 0..1")
        if self.start.tzinfo is None or self.end.tzinfo is None:
            raise ValueError("fault start/end must be timezone-aware")
        if self.end <= self.start:
            raise ValueError("fault end must be after start")


@dataclass(frozen=True)
class SimulationResult:
    config: SimulatorConfig
    readings: pd.DataFrame  # UTC index "timestamp"; telemetry columns in snake_case
    labels: pd.DataFrame = field(repr=False)  # expected_power_w, fault_active, fault_type


def _cloud_cover(n: int, mean: float, rng: np.random.Generator) -> np.ndarray:
    innovations = rng.standard_normal(n)
    sigma = CLOUD_STD * math.sqrt(1 - CLOUD_PHI**2)
    z = np.empty(n)
    prev = CLOUD_STD * rng.standard_normal()
    for i in range(n):
        prev = CLOUD_PHI * prev + sigma * innovations[i]
        z[i] = prev
    return np.clip(mean + z, 0.0, 1.0)


def _air_temperature(index: pd.DatetimeIndex, config: SimulatorConfig) -> np.ndarray:
    lat = abs(config.latitude)
    mean = 28 - 0.4 * lat
    seasonal_amp = 3 + 0.18 * lat
    peak_doy = 200 if config.latitude >= 0 else 20
    local = index + timedelta(hours=config.utc_offset_hours)
    doy = np.asarray(local.dayofyear)
    hour = np.asarray(local.hour + local.minute / 60)
    seasonal = seasonal_amp * np.cos(2 * np.pi * (doy - peak_doy) / 365)
    diurnal = 6 * np.cos(2 * np.pi * (hour - 15) / 24)
    return mean + seasonal + diurnal


def simulate(
    config: SimulatorConfig,
    start: datetime,
    end: datetime,
    faults: list[FaultSpec] | tuple[FaultSpec, ...] = (),
) -> SimulationResult:
    """Generate one reading per 5 minutes for ``start <= t < end`` (UTC, tz-aware)."""
    if start.tzinfo is None or end.tzinfo is None:
        raise ValueError("start and end must be timezone-aware")
    if end <= start:
        raise ValueError("end must be after start")
    index = pd.date_range(start, end, freq=f"{INTERVAL_S}s", inclusive="left", tz="UTC")
    index.name = "timestamp"
    n = len(index)
    rng = np.random.default_rng(config.seed)

    cloud = _cloud_cover(n, config.cloudiness, rng)
    power_noise = 1 + NOISE_SIGMA * rng.standard_normal(n)
    voltage_noise = rng.standard_normal(n)
    freq_noise = rng.standard_normal(n)
    string_bias = 1 + 0.01 * rng.standard_normal(config.string_count)

    pos = solar_position(index, config.latitude, config.longitude)
    zen = pos["zenith_deg"].to_numpy()
    ghi, dni, dhi = clear_sky(zen, np.asarray(index.dayofyear))
    gti_clear = plane_irradiance(
        zen, pos["azimuth_deg"].to_numpy(), ghi, dni, dhi, config.tilt, config.azimuth
    )
    gti = gti_clear * (1 - 0.75 * cloud**3.4)  # Kasten-Czeplak
    producing = gti >= MIN_PRODUCING_GTI_WM2

    air_c = _air_temperature(index, config)
    cell_c = air_c + gti * (NOCT_C - 20) / 800
    rated_ac_w = config.capacity_kwp * 1000 / config.dc_ac_ratio
    dc = config.capacity_kwp * gti * (1 + GAMMA_PER_K * (cell_c - 25)) * power_noise
    dc = np.where(producing, np.clip(dc, 0, None), 0.0)
    ac_unclipped = dc * INVERTER_EFFICIENCY
    ac = np.minimum(ac_unclipped, rated_ac_w)
    clipped = ac_unclipped > rated_ac_w
    expected_ac = ac.copy()

    # Fault injection (transformation of the output).
    fault_active = np.zeros(n, dtype=bool)
    fault_type = np.full(n, "", dtype=object)
    inverter_fault = np.zeros(n, dtype=bool)
    factor = np.ones(n)
    for fault in faults:
        window = np.asarray((index >= fault.start) & (index < fault.end))
        fault_active |= window
        fault_type[window] = fault.type
        if fault.type == "power_drop":
            factor = np.where(window, factor * (1 - fault.severity), factor)
        else:
            inverter_fault |= window
    factor = np.where(inverter_fault, 0.0, factor)
    ac = ac * factor
    dc = dc * factor

    status = np.where(producing, np.where(clipped, "THROTTLED", "MPPT"), "SLEEPING").astype(object)
    status[inverter_fault] = "FAULT"
    error_code = np.where(inverter_fault, INVERTER_FAULT_ERROR_CODE, 0)

    ac = np.round(ac, 1)
    dc = np.maximum(np.round(dc, 1), ac)
    live = ac > 0
    ac_voltage = np.round(230 + 1.5 * voltage_noise, 1)
    ac_current = np.where(live, np.round(ac / (ac_voltage * 0.99), 2), 0.0)
    string_v = np.where(live, np.round(600 * (1 + GAMMA_PER_K * (cell_c - 25) * 2), 1), 0.0)

    step_h = INTERVAL_S / 3600
    total_wh = config.initial_total_wh + np.cumsum(ac) * step_h
    local_day = (index + timedelta(hours=config.utc_offset_hours)).normalize()
    day_wh = pd.Series(ac * step_h, index=index).groupby(local_day.to_numpy()).cumsum().to_numpy()

    data: dict[str, np.ndarray] = {
        "status": status,
        "ac_power_w": ac,
        "dc_power_w": dc,
        "ac_voltage_v": ac_voltage,
        "ac_current_a": ac_current,
        "frequency_hz": np.round(50 + 0.03 * freq_noise, 2),
        "power_factor": np.where(live, 0.99, np.nan),
        "temperature_c": np.round(air_c + 25 * ac / rated_ac_w, 1),
    }
    for i in range(config.string_count):
        share = dc * string_bias[i] / config.string_count
        current = np.where(live & (string_v > 0), share / np.where(string_v > 0, string_v, 1), 0.0)
        data[f"string_{i + 1}_voltage_v"] = string_v
        data[f"string_{i + 1}_current_a"] = np.round(current, 2)
        data[f"string_{i + 1}_power_w"] = np.round(string_v * np.round(current, 2), 1)
    data["day_wh"] = np.round(day_wh, 1)
    data["total_wh"] = np.round(total_wh, 1)
    data["error_code"] = error_code
    data["gti_wm2"] = np.round(gti, 1)
    data["cloud_cover_pct"] = np.round(cloud * 100, 1)

    readings = pd.DataFrame(data, index=index)
    labels = pd.DataFrame(
        {
            "expected_power_w": np.round(expected_ac, 1),
            "fault_active": fault_active,
            "fault_type": fault_type,
        },
        index=index,
    )
    return SimulationResult(config=config, readings=readings, labels=labels)

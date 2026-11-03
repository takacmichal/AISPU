"""Aggregate 5-minute telemetry (ADR #185) to the hourly axis of the detector (ADR #189).

The hour is labelled by its end (``10:05..11:00`` -> ``11:00``), like Open-Meteo hourly
irradiance (mean of the preceding hour). Power is the mean of the samples that arrived;
missing samples are never treated as 0 W, they only lower ``coverage`` (ADR #183: the hour
is valid with >= 75 % of the samples).
"""

from __future__ import annotations

import pandas as pd

from solarops_ml.simulator.generator import INTERVAL_S


def _hour_status(statuses: pd.Series) -> str:
    present = statuses.dropna().astype(str)
    if present.empty:
        return ""
    for blocking in ("FAULT", "OFF"):
        if (present == blocking).any():
            return blocking
    return str(present.mode().iloc[0])


def hourly_from_readings(
    readings: pd.DataFrame,
    expected_power_w: pd.Series | None = None,
    irradiance_column: str = "gti_wm2",
    interval_s: int = INTERVAL_S,
) -> pd.DataFrame:
    """Hourly frame for :func:`detect_underproduction` from 5-minute readings.

    ``readings`` has a UTC index and ``ac_power_w`` (NaN = missing sample); optional
    ``status``, ``cloud_cover_pct`` and the irradiance column (copied to ``ghi_wm2``; the
    simulator only has GTI, used as a proxy). ``expected_power_w`` (same 5-minute index,
    or already hourly) becomes the hourly mean expected power.
    """
    if readings.index.tz is None:
        raise ValueError("readings must have a timezone-aware index")
    samples_per_hour = 3600 // interval_s
    power = readings["ac_power_w"].astype(float)
    grouper = {"rule": "1h", "label": "right", "closed": "right"}

    hourly = pd.DataFrame(
        {
            "actual_power_w": power.resample(**grouper).mean(),
            "coverage": (power.resample(**grouper).count() / samples_per_hour).clip(upper=1.0),
        }
    )
    if "status" in readings.columns:
        hourly["status"] = readings["status"].resample(**grouper).apply(_hour_status)
    if irradiance_column in readings.columns:
        hourly["ghi_wm2"] = readings[irradiance_column].astype(float).resample(**grouper).mean()
    if "cloud_cover_pct" in readings.columns:
        hourly["cloud_cover_pct"] = readings["cloud_cover_pct"].resample(**grouper).mean()
    if expected_power_w is not None:
        expected = expected_power_w.astype(float).resample(**grouper).mean()
        hourly["expected_power_w"] = expected.reindex(hourly.index)
    hourly.index.name = "timestamp"
    return hourly

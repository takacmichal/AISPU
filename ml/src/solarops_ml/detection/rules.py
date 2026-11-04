"""Hourly deviation of actual from expected production and hour classification.

Implements the hour rule of ADR #194 (issues #112 and #114). An hour is *valid* when:

1. actual power exists and at least ``min_coverage`` of the samples were received,
2. irradiance (GHI, or GTI as a proxy) is above ``min_irradiance_wm2`` (if provided),
3. ``P_expected >= min_expected_fraction * P_rated`` (no division by tiny numbers),
4. the inverter status is not ``OFF``/``SLEEPING``/``FAULT`` (separate outage alerts),
5. there is no snow on the panels (if ``snow_depth_cm`` is provided).

``P_expected`` is clipped to the AC limit before ``r = P_actual / P_expected``, so
inverter clipping is not underproduction. A valid hour is ``underproduction`` when
``r < threshold`` and ``normal`` when ``r >= ratio_threshold``. Under heavy cloud the
threshold drops to ``cloudy_ratio_threshold`` (reduced sensitivity); ratios between the
two thresholds are ``ambiguous`` and neither open nor close an event.
"""

from __future__ import annotations

from dataclasses import asdict, dataclass

import numpy as np
import pandas as pd

INVALID_STATUSES = frozenset({"OFF", "SLEEPING", "FAULT"})

# Exclusion reasons, in the order they are checked (first match wins).
EXCLUSION_REASONS = (
    "missing_data",
    "low_irradiance",
    "low_expected",
    "inverter_status",
    "snow",
)

STATE_UNDER = "underproduction"
STATE_NORMAL = "normal"
STATE_AMBIGUOUS = "ambiguous"
STATE_EXCLUDED = "excluded"


@dataclass(frozen=True)
class DetectionConfig:
    """Thresholds of the underproduction rule; defaults are the ADR #194 decision."""

    ratio_threshold: float = 0.85
    min_consecutive_hours: int = 2
    recovery_hours: int = 2
    min_irradiance_wm2: float = 50.0
    min_expected_fraction: float = 0.05
    min_coverage: float = 0.75
    heavy_cloud_pct: float = 85.0
    cloudy_ratio_threshold: float = 0.70
    snow_depth_min_cm: float = 1.0
    severity_medium_ratio: float = 0.70
    severity_high_ratio: float = 0.50
    escalation_hours: float = 72.0

    def __post_init__(self) -> None:
        checks = [
            (0 < self.ratio_threshold <= 1, "ratio_threshold must be within (0, 1]"),
            (self.min_consecutive_hours >= 1, "min_consecutive_hours must be >= 1"),
            (self.recovery_hours >= 1, "recovery_hours must be >= 1"),
            (self.min_irradiance_wm2 >= 0, "min_irradiance_wm2 must be >= 0"),
            (0 < self.min_expected_fraction < 1, "min_expected_fraction must be within (0, 1)"),
            (0 <= self.min_coverage <= 1, "min_coverage must be within 0..1"),
            (0 <= self.heavy_cloud_pct <= 100, "heavy_cloud_pct must be within 0..100"),
            (
                0 < self.cloudy_ratio_threshold <= self.ratio_threshold,
                "cloudy_ratio_threshold must be within (0, ratio_threshold]",
            ),
            (self.snow_depth_min_cm > 0, "snow_depth_min_cm must be positive"),
            (
                0 < self.severity_high_ratio <= self.severity_medium_ratio <= self.ratio_threshold,
                "severity ratios must satisfy 0 < high <= medium <= ratio_threshold",
            ),
            (self.escalation_hours > 0, "escalation_hours must be positive"),
        ]
        for ok, message in checks:
            if not ok:
                raise ValueError(message)

    def to_dict(self) -> dict[str, float | int]:
        return asdict(self)


def _column(frame: pd.DataFrame, name: str, default: float | str) -> pd.Series:
    if name in frame.columns:
        return frame[name]
    return pd.Series(default, index=frame.index)


def prepare_hours(frame: pd.DataFrame) -> pd.DataFrame:
    """Validate the index (UTC, unique) and sort by time."""
    if not isinstance(frame.index, pd.DatetimeIndex):
        raise ValueError("frame must be indexed by timestamps")
    if frame.index.tz is None:
        raise ValueError("timestamps must be timezone-aware (UTC)")
    if frame.index.has_duplicates:
        raise ValueError("timestamps must be unique")
    for required in ("actual_power_w", "expected_power_w"):
        if required not in frame.columns:
            raise ValueError(f"missing column {required!r}")
    return frame.tz_convert("UTC").sort_index()


def classify_hours(
    frame: pd.DataFrame,
    rated_power_w: float,
    ac_limit_w: float | None = None,
    config: DetectionConfig | None = None,
) -> pd.DataFrame:
    """Return ``frame`` with deviation columns and the ADR #194 hour state.

    Input columns: ``actual_power_w`` (NaN = no data), ``expected_power_w``; optional
    ``ghi_wm2``, ``cloud_cover_pct``, ``snow_depth_cm``, ``coverage`` (0..1, default 1)
    and ``status`` (SunSpec name). Added columns: ``expected_clipped_w``, ``ratio``,
    ``deviation_w``, ``relative_deviation``, ``threshold``, ``reduced_sensitivity``,
    ``valid``, ``excluded_reason`` and ``state``.
    """
    if rated_power_w <= 0:
        raise ValueError("rated_power_w must be positive")
    if ac_limit_w is not None and ac_limit_w <= 0:
        raise ValueError("ac_limit_w must be positive")
    config = config or DetectionConfig()
    out = prepare_hours(frame).copy()

    actual = out["actual_power_w"].astype(float)
    expected = out["expected_power_w"].astype(float).clip(lower=0)
    if ac_limit_w is not None:
        expected = expected.clip(upper=ac_limit_w)
    coverage = _column(out, "coverage", 1.0).astype(float).fillna(0.0)
    irradiance = _column(out, "ghi_wm2", np.nan).astype(float)
    cloud = _column(out, "cloud_cover_pct", np.nan).astype(float)
    snow = _column(out, "snow_depth_cm", np.nan).astype(float)
    status = _column(out, "status", "").fillna("").astype(str).str.upper()

    conditions = {
        "missing_data": actual.isna() | expected.isna() | (coverage < config.min_coverage),
        "low_irradiance": irradiance.notna() & (irradiance <= config.min_irradiance_wm2),
        "low_expected": expected < config.min_expected_fraction * rated_power_w,
        "inverter_status": status.isin(INVALID_STATUSES),
        "snow": snow.notna() & (snow >= config.snow_depth_min_cm),
    }
    reason = pd.Series("", index=out.index, dtype=object)
    for name in reversed(EXCLUSION_REASONS):  # first reason in the tuple wins
        reason = reason.mask(conditions[name], name)
    valid = reason == ""

    # Ratio only where the expected power is meaningful: no division by zero.
    safe_expected = expected.where(~conditions["low_expected"] & expected.notna() & (expected > 0))
    ratio = actual / safe_expected

    reduced = cloud.notna() & (cloud >= config.heavy_cloud_pct)
    threshold = pd.Series(config.ratio_threshold, index=out.index).mask(
        reduced, config.cloudy_ratio_threshold
    )
    state = pd.Series(STATE_EXCLUDED, index=out.index, dtype=object)
    state = state.mask(valid & (ratio >= config.ratio_threshold), STATE_NORMAL)
    state = state.mask(valid & (ratio < config.ratio_threshold), STATE_AMBIGUOUS)
    state = state.mask(valid & (ratio < threshold), STATE_UNDER)

    out["expected_clipped_w"] = expected
    out["ratio"] = ratio
    out["deviation_w"] = actual - expected
    out["relative_deviation"] = ratio - 1
    out["threshold"] = threshold
    out["reduced_sensitivity"] = valid & reduced
    out["valid"] = valid
    out["excluded_reason"] = reason
    out["state"] = state
    return out

"""Underproduction rule of ADR #194 (issues #112, #113, #114)."""

from __future__ import annotations

import math
import uuid
from datetime import UTC, datetime, timedelta

import numpy as np
import pandas as pd
import pytest

from solarops_ml.detection import (
    DetectionConfig,
    classify_hours,
    detect_underproduction,
    hourly_from_readings,
)
from solarops_ml.simulator import FaultSpec, SimulatorConfig, simulate

INSTALLATION = "7f3c2d9e-1b4a-4c55-9a1e-2d6f0b8e1a10"
RATED_W = 10_000.0


def dt(*args: int) -> datetime:
    return datetime(*args, tzinfo=UTC)


def day_profile(days: int, peak_w: float = 8_000.0, start: datetime | None = None) -> pd.DataFrame:
    """Hourly frame: sunny days 06:00-18:00 UTC, expected = actual, night = 0 W."""
    start = start or dt(2024, 6, 1)
    index = pd.date_range(start, periods=24 * days, freq="1h", tz="UTC")
    hour = np.asarray(index.hour)
    shape = np.clip(np.sin(np.pi * (hour - 6) / 12), 0, None)
    expected = peak_w * shape
    return pd.DataFrame(
        {
            "actual_power_w": expected.copy(),
            "expected_power_w": expected,
            "ghi_wm2": 1000.0 * shape,
            "status": np.where(shape > 0, "MPPT", "SLEEPING"),
            "coverage": 1.0,
        },
        index=index,
    )


def scale_actual(frame: pd.DataFrame, start: datetime, end: datetime, factor: float) -> None:
    window = (frame.index >= start) & (frame.index < end)
    frame.loc[window, "actual_power_w"] *= factor


# --- #112: deviation ---------------------------------------------------------


def test_deviation_ratio_and_absolute_difference() -> None:
    frame = day_profile(1)
    frame["actual_power_w"] *= 0.5
    hours = classify_hours(frame, RATED_W)
    noon = hours.loc[pd.Timestamp("2024-06-01T12:00Z")]
    assert noon["ratio"] == pytest.approx(0.5)
    assert noon["relative_deviation"] == pytest.approx(-0.5)
    assert noon["deviation_w"] == pytest.approx(-4_000.0)
    assert noon["state"] == "underproduction"


def test_zero_or_small_expected_does_not_divide_by_zero() -> None:
    frame = day_profile(1)
    frame["expected_power_w"] = 0.0
    frame["actual_power_w"] = 100.0
    hours = classify_hours(frame, RATED_W)
    assert hours["ratio"].isna().all()
    assert (hours["state"] == "excluded").all()
    assert not np.isinf(hours["relative_deviation"].fillna(0)).any()


def test_deviation_only_in_hours_with_enough_irradiance() -> None:
    frame = day_profile(1)
    frame["actual_power_w"] = 0.0
    frame["ghi_wm2"] = 40.0  # dull light below 50 W/m2
    hours = classify_hours(frame, RATED_W)
    assert (hours["state"] == "excluded").all()
    assert set(hours["excluded_reason"]) <= {"low_irradiance", "low_expected"}


def test_expected_power_is_clipped_to_ac_limit() -> None:
    frame = day_profile(1, peak_w=9_000.0)
    frame["actual_power_w"] = frame["expected_power_w"].clip(upper=7_000.0)
    hours = classify_hours(frame, RATED_W, ac_limit_w=7_000.0)
    noon = hours.loc[pd.Timestamp("2024-06-01T12:00Z")]
    assert noon["expected_clipped_w"] == 7_000.0
    assert noon["ratio"] == pytest.approx(1.0)
    assert (hours["state"] != "underproduction").all()


# --- #114: invalid hours, weather, outages ----------------------------------


def test_missing_data_low_coverage_and_inverter_status_are_excluded() -> None:
    frame = day_profile(1)
    frame["actual_power_w"] = 0.0
    frame.loc[pd.Timestamp("2024-06-01T10:00Z"), "actual_power_w"] = np.nan
    frame.loc[pd.Timestamp("2024-06-01T11:00Z"), "coverage"] = 0.5
    frame.loc[pd.Timestamp("2024-06-01T12:00Z"), "status"] = "FAULT"
    frame.loc[pd.Timestamp("2024-06-01T13:00Z"), "status"] = "OFF"
    hours = classify_hours(frame, RATED_W)
    reasons = hours["excluded_reason"]
    assert reasons[pd.Timestamp("2024-06-01T10:00Z")] == "missing_data"
    assert reasons[pd.Timestamp("2024-06-01T11:00Z")] == "missing_data"
    assert reasons[pd.Timestamp("2024-06-01T12:00Z")] == "inverter_status"
    assert reasons[pd.Timestamp("2024-06-01T13:00Z")] == "inverter_status"


def test_heavy_cloud_reduces_sensitivity() -> None:
    frame = day_profile(1)
    frame["actual_power_w"] *= 0.78  # under 0.85 but above the cloudy threshold 0.70
    frame["cloud_cover_pct"] = 95.0
    hours = classify_hours(frame, RATED_W)
    day = hours[hours["ghi_wm2"] > 50]
    assert (day["state"] == "ambiguous").all()
    assert day["reduced_sensitivity"].all()

    frame["actual_power_w"] = frame["expected_power_w"] * 0.5
    hours = classify_hours(frame, RATED_W)
    assert (hours[hours["ghi_wm2"] > 50]["state"] == "underproduction").all()


def test_snow_hours_are_excluded() -> None:
    frame = day_profile(1)
    frame["actual_power_w"] = 0.0
    frame["snow_depth_cm"] = 12.0
    result = detect_underproduction(frame, INSTALLATION, RATED_W)
    assert result.alarms == []
    assert result.summary["excludedHours"]["snow"] > 0


def test_data_outage_does_not_raise_an_alarm() -> None:
    frame = day_profile(3)
    window = (frame.index >= dt(2024, 6, 2)) & (frame.index < dt(2024, 6, 3))
    frame.loc[window, "actual_power_w"] = np.nan
    frame.loc[window, "coverage"] = 0.0
    result = detect_underproduction(frame, INSTALLATION, RATED_W)
    assert result.alarms == []
    assert result.summary["excludedHours"]["missing_data"] == 24


def test_missing_irradiance_falls_back_to_expected_power() -> None:
    frame = day_profile(1).drop(columns=["ghi_wm2", "status", "coverage"])
    frame["actual_power_w"] *= 0.4
    result = detect_underproduction(frame, INSTALLATION, RATED_W)
    assert len(result.alarms) == 1


# --- #113: sustained underproduction ----------------------------------------


def test_healthy_series_has_no_alarm() -> None:
    result = detect_underproduction(day_profile(5), INSTALLATION, RATED_W)
    assert result.alarms == []
    assert result.summary["underproducingHours"] == 0


def test_single_underproducing_hour_is_not_an_alarm() -> None:
    frame = day_profile(1)
    scale_actual(frame, dt(2024, 6, 1, 12), dt(2024, 6, 1, 13), 0.3)
    assert detect_underproduction(frame, INSTALLATION, RATED_W).alarms == []


def test_two_consecutive_hours_raise_alarm_with_start_end_and_detection_time() -> None:
    frame = day_profile(1)
    scale_actual(frame, dt(2024, 6, 1, 10), dt(2024, 6, 1, 14), 0.6)
    alarms = detect_underproduction(frame, INSTALLATION, RATED_W).alarms
    assert len(alarms) == 1
    alarm = alarms[0]
    assert alarm.start == dt(2024, 6, 1, 10)
    assert alarm.detected_at == dt(2024, 6, 1, 11)
    assert alarm.end == dt(2024, 6, 1, 13)
    assert not alarm.ongoing
    assert alarm.underproducing_hours == 4
    assert alarm.median_ratio == pytest.approx(0.6)
    assert alarm.severity == "medium"
    assert alarm.ticket_priority == "Medium"
    assert alarm.reason_code == "sustained_underproduction"
    assert alarm.reason  # human readable text for the ticket


def test_multi_day_fault_is_one_alarm_because_nights_are_skipped() -> None:
    frame = day_profile(4)
    scale_actual(frame, dt(2024, 6, 1, 9), dt(2024, 6, 3, 15), 0.3)
    alarms = detect_underproduction(frame, INSTALLATION, RATED_W).alarms
    assert len(alarms) == 1
    assert alarms[0].start == dt(2024, 6, 1, 9)
    assert alarms[0].end == dt(2024, 6, 3, 14)
    assert alarms[0].severity == "high"


def test_event_closes_after_two_normal_hours_and_reopens_as_new_alarm() -> None:
    frame = day_profile(1)
    scale_actual(frame, dt(2024, 6, 1, 8), dt(2024, 6, 1, 10), 0.5)
    scale_actual(frame, dt(2024, 6, 1, 13), dt(2024, 6, 1, 15), 0.5)
    alarms = detect_underproduction(frame, INSTALLATION, RATED_W).alarms
    assert [a.start for a in alarms] == [dt(2024, 6, 1, 8), dt(2024, 6, 1, 13)]
    assert alarms[0].alarm_id != alarms[1].alarm_id


def test_single_normal_hour_does_not_close_event() -> None:
    frame = day_profile(1)
    scale_actual(frame, dt(2024, 6, 1, 8), dt(2024, 6, 1, 15), 0.5)
    frame.loc[pd.Timestamp("2024-06-01T11:00Z"), "actual_power_w"] = frame.loc[
        pd.Timestamp("2024-06-01T11:00Z"), "expected_power_w"
    ]
    assert len(detect_underproduction(frame, INSTALLATION, RATED_W).alarms) == 1


def test_event_open_at_end_of_series_is_ongoing() -> None:
    frame = day_profile(1)
    scale_actual(frame, dt(2024, 6, 1, 14), dt(2024, 6, 2), 0.5)
    alarm = detect_underproduction(frame, INSTALLATION, RATED_W).alarms[0]
    assert alarm.ongoing
    assert alarm.end is None
    assert alarm.last_underproduction_at == dt(2024, 6, 1, 17)


def test_thresholds_are_configurable() -> None:
    frame = day_profile(1)
    scale_actual(frame, dt(2024, 6, 1, 9), dt(2024, 6, 1, 12), 0.8)
    assert len(detect_underproduction(frame, INSTALLATION, RATED_W).alarms) == 1
    strict = DetectionConfig(ratio_threshold=0.75)
    assert detect_underproduction(frame, INSTALLATION, RATED_W, config=strict).alarms == []
    slow = DetectionConfig(min_consecutive_hours=4)
    assert detect_underproduction(frame, INSTALLATION, RATED_W, config=slow).alarms == []


def test_invalid_config_is_rejected() -> None:
    with pytest.raises(ValueError):
        DetectionConfig(ratio_threshold=1.5)
    with pytest.raises(ValueError):
        DetectionConfig(min_consecutive_hours=0)
    with pytest.raises(ValueError):
        DetectionConfig(cloudy_ratio_threshold=0.9)  # must not exceed ratio_threshold


def test_severity_levels_and_escalation_by_duration() -> None:
    frame = day_profile(1)
    scale_actual(frame, dt(2024, 6, 1, 9), dt(2024, 6, 1, 12), 0.78)
    assert detect_underproduction(frame, INSTALLATION, RATED_W).alarms[0].severity == "low"

    frame = day_profile(5)
    scale_actual(frame, dt(2024, 6, 1, 9), dt(2024, 6, 5, 15), 0.78)
    alarm = detect_underproduction(frame, INSTALLATION, RATED_W).alarms[0]
    assert alarm.duration_hours >= 72
    assert alarm.severity == "medium"  # low lasting >= 3 days escalates


def test_lost_energy_is_summed_over_valid_hours_only() -> None:
    frame = day_profile(1)
    scale_actual(frame, dt(2024, 6, 1, 10), dt(2024, 6, 1, 14), 0.5)
    frame.loc[pd.Timestamp("2024-06-01T12:00Z"), ["actual_power_w", "coverage"]] = [np.nan, 0.0]
    alarm = detect_underproduction(frame, INSTALLATION, RATED_W).alarms[0]
    valid = [10, 11, 13]
    expected_kwh = sum(
        frame.loc[pd.Timestamp(f"2024-06-01T{h}:00Z"), "expected_power_w"] for h in valid
    )
    assert alarm.estimated_lost_kwh == pytest.approx(expected_kwh * 0.5 / 1000, abs=0.01)
    assert alarm.expected_kwh == pytest.approx(expected_kwh / 1000, abs=0.01)


def test_alarm_id_is_deterministic_uuid() -> None:
    frame = day_profile(1)
    scale_actual(frame, dt(2024, 6, 1, 10), dt(2024, 6, 1, 14), 0.5)
    a = detect_underproduction(frame, INSTALLATION, RATED_W).alarms[0]
    b = detect_underproduction(frame.iloc[5:], INSTALLATION, RATED_W).alarms[0]
    assert a.alarm_id == b.alarm_id
    assert uuid.UUID(a.alarm_id).version == 5


def test_summary_counts_hours() -> None:
    frame = day_profile(1)
    scale_actual(frame, dt(2024, 6, 1, 10), dt(2024, 6, 1, 14), 0.5)
    summary = detect_underproduction(frame, INSTALLATION, RATED_W).summary
    assert summary["hours"] == 24
    assert summary["underproducingHours"] == 4
    assert summary["validHours"] + sum(summary["excludedHours"].values()) == 24


def test_unsorted_input_and_duplicates() -> None:
    frame = day_profile(1)
    scale_actual(frame, dt(2024, 6, 1, 10), dt(2024, 6, 1, 14), 0.5)
    shuffled = frame.sample(frac=1.0, random_state=1)
    assert len(detect_underproduction(shuffled, INSTALLATION, RATED_W).alarms) == 1
    with pytest.raises(ValueError):
        detect_underproduction(pd.concat([frame, frame.iloc[:1]]), INSTALLATION, RATED_W)


# --- hourly aggregation of 5-minute telemetry ---------------------------------


def test_hourly_aggregation_uses_mean_of_present_samples_and_coverage() -> None:
    index = pd.date_range("2024-06-01T10:05Z", periods=12, freq="5min")
    readings = pd.DataFrame(
        {"ac_power_w": [1000.0] * 12, "status": ["MPPT"] * 12, "gti_wm2": [800.0] * 12},
        index=index,
    )
    readings.iloc[:4, 0] = np.nan  # 4 missing samples: mean must not treat them as 0 W
    hourly = hourly_from_readings(readings)
    row = hourly.loc[pd.Timestamp("2024-06-01T11:00Z")]
    assert row["actual_power_w"] == pytest.approx(1000.0)
    assert row["coverage"] == pytest.approx(8 / 12)
    assert row["ghi_wm2"] == pytest.approx(800.0)
    assert row["status"] == "MPPT"


def test_hourly_aggregation_marks_fault_hours() -> None:
    index = pd.date_range("2024-06-01T10:05Z", periods=12, freq="5min")
    status = ["MPPT"] * 11 + ["FAULT"]
    readings = pd.DataFrame({"ac_power_w": [500.0] * 12, "status": status}, index=index)
    assert hourly_from_readings(readings)["status"].iloc[0] == "FAULT"


def test_detects_simulated_power_drop() -> None:
    """Acceptance criterion of #113: the rule catches a fault from the simulator."""
    config = SimulatorConfig(
        device_id=INSTALLATION, latitude=-23.76, longitude=133.87, cloudiness=0.2, seed=3
    )
    fault = FaultSpec("power_drop", dt(2024, 6, 3, 0), dt(2024, 6, 5, 0), severity=0.5)
    sim = simulate(config, dt(2024, 6, 1), dt(2024, 6, 7), faults=[fault])
    hourly = hourly_from_readings(sim.readings, expected_power_w=sim.labels["expected_power_w"])
    rated = config.capacity_kwp * 1000
    alarms = detect_underproduction(hourly, INSTALLATION, rated).alarms
    assert len(alarms) == 1
    assert fault.start <= alarms[0].start <= fault.start + timedelta(hours=24)
    assert alarms[0].estimated_lost_kwh > 0
    assert math.isclose(alarms[0].median_ratio, 0.5, abs_tol=0.05)

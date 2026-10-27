from __future__ import annotations

from datetime import UTC, datetime, timedelta

import pandas as pd
import pytest

from solarops_ml.simulator import (
    FaultSpec,
    SimulatorConfig,
    simulate,
    to_payloads,
)
from solarops_ml.simulator.solar import solar_position

DEVICE = "7f3c2d9e-1b4a-4c55-9a1e-2d6f0b8e1a10"
READING_KEYS = {
    "messageId", "timestamp", "intervalSeconds", "status", "acPowerW", "dcPowerW",
    "acVoltageV", "acCurrentA", "frequencyHz", "powerFactor", "temperatureC", "strings",
    "energy", "errorCode",
}  # fmt: skip
STATUSES = {"OFF", "SLEEPING", "STARTING", "MPPT", "THROTTLED", "SHUTTING_DOWN", "FAULT", "STANDBY"}


def dt(y: int, m: int, d: int, h: int = 0, mi: int = 0) -> datetime:
    return datetime(y, m, d, h, mi, tzinfo=UTC)


def kosice(seed: int = 1, **kw) -> SimulatorConfig:
    # Fixed panels face south (azimuth 0), 35 deg tilt, UTC+2 in summer.
    return SimulatorConfig(
        device_id=DEVICE, latitude=48.72, longitude=21.26, tilt=35, azimuth=0,
        utc_offset_hours=2, seed=seed, **kw,
    )  # fmt: skip


def alice(seed: int = 1, **kw) -> SimulatorConfig:
    return SimulatorConfig(
        device_id=DEVICE, latitude=-23.76, longitude=133.87, tilt=20, azimuth=180,
        utc_offset_hours=9.5, seed=seed, **kw,
    )  # fmt: skip


def peak_power(config: SimulatorConfig, day: datetime) -> float:
    r = simulate(config, day, day + timedelta(days=1))
    return float(r.readings["ac_power_w"].max())


def test_same_seed_is_reproducible_and_other_seed_differs() -> None:
    a = simulate(kosice(5), dt(2024, 6, 20), dt(2024, 6, 22))
    b = simulate(kosice(5), dt(2024, 6, 20), dt(2024, 6, 22))
    c = simulate(kosice(6), dt(2024, 6, 20), dt(2024, 6, 22))
    assert a.readings.equals(b.readings)
    assert to_payloads(a, DEVICE) == to_payloads(b, DEVICE)
    assert not a.readings["ac_power_w"].equals(c.readings["ac_power_w"])


def test_night_has_zero_power_and_sleeping_status() -> None:
    r = simulate(kosice(), dt(2024, 6, 20), dt(2024, 6, 21)).readings
    night = r[(r.index.hour >= 21) | (r.index.hour <= 1)]  # 23:00-03:00 local
    assert len(night) > 0
    assert (night["ac_power_w"] == 0).all()
    assert (night["status"] == "SLEEPING").all()


def test_summer_noon_beats_winter_noon_in_both_hemispheres() -> None:
    assert peak_power(kosice(), dt(2024, 6, 21)) > peak_power(kosice(), dt(2024, 12, 21))
    # Southern hemisphere: the seasons are flipped.
    assert peak_power(alice(), dt(2024, 12, 21)) > peak_power(alice(), dt(2024, 6, 21))


def test_summer_day_produces_more_energy_than_winter_day() -> None:
    def energy(day: datetime) -> float:
        r = simulate(kosice(cloudiness=0.1), day, day + timedelta(days=1)).readings
        return float(r["ac_power_w"].sum() * 300 / 3600)

    assert energy(dt(2024, 6, 21)) > 2 * energy(dt(2024, 12, 21))


def test_cloudier_weather_yields_less_energy() -> None:
    def energy(cloudiness: float) -> float:
        r = simulate(kosice(cloudiness=cloudiness), dt(2024, 6, 1), dt(2024, 6, 8)).readings
        return float(r["ac_power_w"].sum())

    assert energy(0.9) < 0.7 * energy(0.05)


def test_power_drop_lowers_power_only_inside_window() -> None:
    start, end = dt(2024, 6, 20), dt(2024, 6, 21)
    w0, w1 = dt(2024, 6, 20, 8), dt(2024, 6, 20, 12)  # 10:00-14:00 local
    base = simulate(kosice(3), start, end)
    faulty = simulate(kosice(3), start, end, faults=[FaultSpec("power_drop", w0, w1, 0.5)])
    inside = (base.readings.index >= w0) & (base.readings.index < w1)
    b, f = base.readings["ac_power_w"], faulty.readings["ac_power_w"]
    assert (f[inside] < b[inside]).all()
    assert f[inside].sum() == pytest.approx(0.5 * b[inside].sum(), rel=0.02)
    assert b[~inside].equals(f[~inside])
    # Labels live outside the ingest contract and mark the window.
    assert faulty.labels.loc[inside, "fault_active"].all()
    assert not faulty.labels.loc[~inside, "fault_active"].any()
    assert (faulty.labels.loc[inside, "fault_type"] == "power_drop").all()
    assert faulty.labels["expected_power_w"].equals(base.labels["expected_power_w"])
    assert not base.labels["fault_active"].any()


def test_inverter_fault_sets_status_and_error_code() -> None:
    w0, w1 = dt(2024, 6, 20, 8), dt(2024, 6, 20, 10)
    r = simulate(
        kosice(), dt(2024, 6, 20), dt(2024, 6, 21), faults=[FaultSpec("inverter_fault", w0, w1, 1)]
    ).readings
    win = r[(r.index >= w0) & (r.index < w1)]
    assert (win["status"] == "FAULT").all()
    assert (win["ac_power_w"] == 0).all()
    assert (win["error_code"] != 0).all()


def test_payload_matches_documented_telemetry_format() -> None:
    result = simulate(alice(), dt(2024, 3, 1), dt(2024, 3, 3))
    payloads = to_payloads(result, DEVICE)
    assert [len(p["readings"]) for p in payloads] == [288, 288]
    totals: list[float] = []
    for p in payloads:
        assert set(p) == {"schemaVersion", "deviceId", "readings"}
        assert p["schemaVersion"] == "1.0" and p["deviceId"] == DEVICE
        for rd in p["readings"]:
            assert set(rd) == READING_KEYS
            assert rd["timestamp"].endswith("Z")
            assert rd["intervalSeconds"] == 300
            assert rd["status"] in STATUSES
            assert rd["acPowerW"] >= 0 and rd["dcPowerW"] >= rd["acPowerW"]
            assert 40 <= rd["frequencyHz"] <= 70
            # powerFactor is null while the inverter produces nothing (schema allows null)
            assert (rd["powerFactor"] is None) == (rd["acPowerW"] == 0)
            assert rd["powerFactor"] is None or -1 <= rd["powerFactor"] <= 1
            assert {s["id"] for s in rd["strings"]} == {1, 2}
            assert set(rd["energy"]) == {"dayWh", "totalWh"}
            totals.append(rd["energy"]["totalWh"])
    assert totals == sorted(totals) and totals[-1] > totals[0]
    stamps = [rd["timestamp"] for p in payloads for rd in p["readings"]]
    assert len(set(stamps)) == len(stamps)
    ids = [rd["messageId"] for p in payloads for rd in p["readings"]]
    assert len(set(ids)) == len(ids)
    # dayWh resets at local midnight (UTC+9.5 -> 14:30Z) and is non-negative.
    day = [rd["energy"]["dayWh"] for p in payloads for rd in p["readings"]]
    assert min(day) == 0 and any(b < a for a, b in zip(day, day[1:], strict=False))


def test_invalid_input_is_rejected() -> None:
    with pytest.raises(ValueError):
        simulate(kosice(), dt(2024, 6, 2), dt(2024, 6, 1))
    with pytest.raises(ValueError):
        simulate(kosice(), datetime(2024, 6, 1), datetime(2024, 6, 2))  # noqa: DTZ001
    with pytest.raises(ValueError):
        FaultSpec("power_drop", dt(2024, 6, 1), dt(2024, 6, 2), 1.5)
    with pytest.raises(ValueError):
        FaultSpec("unknown", dt(2024, 6, 1), dt(2024, 6, 2), 0.5)
    with pytest.raises(ValueError):
        kosice(cloudiness=1.5)


def test_solar_position_sanity() -> None:
    # Kosice, summer solstice, near solar noon (~10:35 UTC): sun due south, zenith ~ lat - 23.44.
    pos = solar_position(pd.DatetimeIndex([dt(2024, 6, 20, 10, 35)]), 48.72, 21.26)
    assert pos["zenith_deg"].iloc[0] == pytest.approx(48.72 - 23.44, abs=1.0)
    assert pos["azimuth_deg"].iloc[0] == pytest.approx(180, abs=5)
    # Alice Springs, December solstice, solar noon (~03:00 UTC): sun almost overhead.
    pos2 = solar_position(pd.DatetimeIndex([dt(2024, 12, 21, 3, 0)]), -23.76, 133.87)
    assert pos2["zenith_deg"].iloc[0] < 5

from __future__ import annotations

import json
from datetime import UTC, datetime, timedelta
from pathlib import Path

import numpy as np
import pandas as pd
import pytest
from jsonschema import Draft202012Validator, FormatChecker

from solarops_ml.prediction.features import PlantSpec, build_features
from solarops_ml.simulator import (
    FaultSpec,
    SimulatorConfig,
    simulate,
    to_payloads,
)
from solarops_ml.simulator.cli import main as cli_main
from solarops_ml.simulator.solar import clear_sky, solar_position
from solarops_ml.weather.store import write_dataset

DEVICE = "7f3c2d9e-1b4a-4c55-9a1e-2d6f0b8e1a10"
INSTALLATION = "0b8e1a10-7f3c-4d9e-8b4a-2d6f0c551a9e"
SCHEMA_PATH = (
    Path(__file__).resolve().parents[2] / "docs/adr/assets/spike-185-telemetry.schema.json"
)
READING_KEYS = {
    "deviceId", "timestamp", "intervalSec", "status", "vendorStatusCode", "acPowerW", "dcPowerW",
    "acVoltageV", "acCurrentA", "gridFrequencyHz", "temperatureC", "strings",
    "totalEnergyWh", "dayEnergyWh",
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


def test_payload_matches_ingest_contract() -> None:
    result = simulate(alice(), dt(2024, 3, 1), dt(2024, 3, 3))
    payloads = to_payloads(result, DEVICE, INSTALLATION)
    assert [len(p["readings"]) for p in payloads] == [288, 288]
    totals: list[float] = []
    for p in payloads:
        assert set(p) == {"schemaVersion", "installationId", "sentAt", "source", "readings"}
        assert p["schemaVersion"] == "1.0" and p["installationId"] == INSTALLATION
        assert p["source"] == "simulator"
        assert p["sentAt"] == p["readings"][-1]["timestamp"]
        for rd in p["readings"]:
            assert set(rd) == READING_KEYS
            assert rd["deviceId"] == DEVICE
            assert rd["timestamp"].endswith("Z")
            assert rd["intervalSec"] == 300
            assert rd["status"] in STATUSES
            assert rd["acPowerW"] >= 0 and rd["dcPowerW"] >= rd["acPowerW"]
            assert 40 <= rd["gridFrequencyHz"] <= 70
            assert {s["index"] for s in rd["strings"]} == {1, 2}
            totals.append(rd["totalEnergyWh"])
    assert totals == sorted(totals) and totals[-1] > totals[0]
    stamps = [rd["timestamp"] for p in payloads for rd in p["readings"]]
    assert len(set(stamps)) == len(stamps)
    # dayEnergyWh resets at local midnight (UTC+9.5 -> 14:30Z) and is non-negative.
    day = [rd["dayEnergyWh"] for p in payloads for rd in p["readings"]]
    assert min(day) == 0 and any(b < a for a, b in zip(day, day[1:], strict=False))


def test_payloads_validate_against_adr_185_schema() -> None:
    validator = Draft202012Validator(
        json.loads(SCHEMA_PATH.read_text(encoding="utf-8")), format_checker=FormatChecker()
    )
    faults = [FaultSpec("inverter_fault", dt(2024, 6, 20, 8), dt(2024, 6, 20, 10), 1)]
    result = simulate(kosice(), dt(2024, 6, 20), dt(2024, 6, 22), faults=faults)
    payloads = to_payloads(result, DEVICE)
    assert payloads
    for payload in payloads:
        assert list(validator.iter_errors(payload)) == []
    assert payloads == to_payloads(result, DEVICE)


def test_payload_rejects_non_uuid_installation_id() -> None:
    result = simulate(kosice(), dt(2024, 6, 20), dt(2024, 6, 21))
    with pytest.raises(ValueError):
        to_payloads(result, DEVICE, "not-a-uuid")


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


# --- weather-driven mode (#188): the plant follows real weather like the prediction model


def hourly_weather(start: datetime, hours: int, plant: SimulatorConfig) -> pd.DataFrame:
    """Hourly GHI/DNI/DHI (hour-start labels) from the clear sky, with a cloudy afternoon."""
    index = pd.date_range(start, periods=hours, freq="h", tz="UTC", name="hour_start_utc")
    middle = index + pd.Timedelta(minutes=30)
    zen = solar_position(middle, plant.latitude, plant.longitude)["zenith_deg"].to_numpy()
    ghi, dni, dhi = clear_sky(zen, np.asarray(middle.dayofyear))
    cloudy = np.asarray(index.hour >= 13)
    return pd.DataFrame(
        {
            "ghi_wm2": np.where(cloudy, ghi * 0.4, ghi),
            "dni_wm2": np.where(cloudy, dni * 0.1, dni),
            "dhi_wm2": np.where(cloudy, dhi * 1.3, dhi),
            "temp_air_c": 18.0 + 6 * np.sin(np.pi * np.asarray(index.hour) / 24),
            "cloud_cover_pct": np.where(cloudy, 85.0, 5.0),
        },
        index=index,
    )


def test_weather_drives_power_like_the_prediction_baseline() -> None:
    # The detection compares telemetry with the baseline model, so a healthy simulated plant
    # must stay close to it in every daylight hour (no false underproduction alarms).
    config = kosice(4, capacity_kwp=9.9, dc_ac_ratio=0.99)
    start = dt(2024, 6, 20)
    weather = hourly_weather(start, 48, config)
    result = simulate(config, start, start + timedelta(days=2), weather=weather)

    plant = PlantSpec(
        latitude=config.latitude, longitude=config.longitude, capacity_kwp=9.9,
        tilt=config.tilt, azimuth=config.azimuth, dc_ac_ratio=0.99,
    )  # fmt: skip
    features = build_features(weather, plant)
    expected = features["p_baseline"] * 9.9 * 1000
    actual = result.readings["ac_power_w"].resample("h").mean()
    day = features["gti_wm2"] > 200
    ratio = actual[day] / expected[day]
    assert day.sum() >= 12
    assert ratio.between(0.9, 1.1).all(), ratio[~ratio.between(0.9, 1.1)]
    # Cloudy hours produce less, and the reported cloud cover is the weather's.
    afternoon = result.readings.between_time("13:00", "15:55")["ac_power_w"].mean()
    morning = result.readings.between_time("08:00", "10:55")["ac_power_w"].mean()
    assert afternoon < morning
    assert result.readings.loc[dt(2024, 6, 20, 14), "cloud_cover_pct"] == 85.0


def test_weather_mode_keeps_faults_and_reproducibility() -> None:
    config = kosice(4)
    start, end = dt(2024, 6, 20), dt(2024, 6, 21)
    weather = hourly_weather(start, 24, config)
    w0, w1 = dt(2024, 6, 20, 8), dt(2024, 6, 20, 12)
    base = simulate(config, start, end, weather=weather)
    again = simulate(config, start, end, weather=weather)
    faulty = simulate(config, start, end, [FaultSpec("power_drop", w0, w1, 0.5)], weather=weather)
    assert base.readings.equals(again.readings)
    inside = (base.readings.index >= w0) & (base.readings.index < w1)
    b, f = base.readings["ac_power_w"], faulty.readings["ac_power_w"]
    assert f[inside].sum() == pytest.approx(0.5 * b[inside].sum(), rel=0.02)
    assert b[~inside].equals(f[~inside])


def test_weather_must_cover_every_hour() -> None:
    config = kosice()
    start = dt(2024, 6, 20)
    weather = hourly_weather(start, 24, config)
    with pytest.raises(ValueError, match="weather"):
        simulate(config, start, start + timedelta(days=2), weather=weather)
    with pytest.raises(ValueError, match="weather"):
        simulate(config, start, start + timedelta(days=1), weather=weather.drop(columns="dni_wm2"))


def test_cli_reads_open_meteo_weather_and_coordinates(tmp_path: Path) -> None:
    # The dataset of `python -m solarops_ml.weather fetch` labels each hour by its end.
    plant = kosice()
    weather = hourly_weather(dt(2024, 6, 20), 24, plant)
    weather.index = pd.DatetimeIndex(weather.index + pd.Timedelta(hours=1), name="time")
    weather_file = write_dataset(weather, tmp_path / "weather.csv")
    out = tmp_path / "batches.json"

    code = cli_main([
        "--lat", "48.31", "--lon", "18.08", "--tilt", "35", "--azimuth", "0",
        "--capacity-kwp", "9.9", "--dc-ac-ratio", "0.99", "--weather", str(weather_file),
        "--start", "2024-06-20T00:00Z", "--end", "2024-06-21T00:00Z",
        "--device-id", DEVICE, "--installation-id", INSTALLATION, "--out", str(out),
    ])  # fmt: skip

    assert code == 0
    batches = json.loads(out.read_text())
    assert [b["installationId"] for b in batches] == [INSTALLATION]
    readings = batches[0]["readings"]
    assert len(readings) == 288
    assert max(r["acPowerW"] for r in readings) > 1000

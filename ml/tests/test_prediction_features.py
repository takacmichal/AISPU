from __future__ import annotations

import numpy as np
import pandas as pd
import pytest

from solarops_ml.prediction.features import (
    FEATURES,
    PlantSpec,
    build_features,
    from_open_meteo,
)

PLANT = PlantSpec(latitude=48.72, longitude=21.26, capacity_kwp=8.0, tilt=30, azimuth=0)


def hourly_weather(start: str = "2024-06-20T00:00Z", hours: int = 48) -> pd.DataFrame:
    index = pd.date_range(start, periods=hours, freq="1h", tz="UTC", name="hour_start_utc")
    hour = np.asarray(index.hour)
    # Bell-shaped irradiance around 10-11 UTC (local solar noon in Košice), zero at night.
    shape = np.clip(np.cos((hour + 0.5 - 10.6) / 24 * 2 * np.pi), 0, None) ** 2
    ghi = 900 * shape
    return pd.DataFrame(
        {
            "ghi_wm2": ghi,
            "dni_wm2": 700 * shape,
            "dhi_wm2": 120 * shape,
            "cloud_cover_pct": 20.0,
            "temp_air_c": 15 + 10 * shape,
            "wind_speed_ms": 2.0,
        },
        index=index,
    )


def test_plant_spec_validates_inputs() -> None:
    with pytest.raises(ValueError, match="latitude"):
        PlantSpec(latitude=91, longitude=0, capacity_kwp=5, tilt=30, azimuth=0)
    with pytest.raises(ValueError, match="longitude"):
        PlantSpec(latitude=0, longitude=181, capacity_kwp=5, tilt=30, azimuth=0)
    with pytest.raises(ValueError, match="capacity"):
        PlantSpec(latitude=0, longitude=0, capacity_kwp=0, tilt=30, azimuth=0)
    with pytest.raises(ValueError, match="tilt"):
        PlantSpec(latitude=0, longitude=0, capacity_kwp=5, tilt=95, azimuth=0)
    with pytest.raises(ValueError, match="azimuth"):
        PlantSpec(latitude=0, longitude=0, capacity_kwp=5, tilt=30, azimuth=200)
    with pytest.raises(ValueError, match="dc_ac"):
        PlantSpec(latitude=0, longitude=0, capacity_kwp=5, tilt=30, azimuth=0, dc_ac_ratio=0)


def test_build_features_has_documented_columns_and_hourly_index() -> None:
    features = build_features(hourly_weather(), PLANT)

    assert list(features.index) == list(hourly_weather().index)
    assert set(FEATURES) <= set(features.columns)
    assert {"cell_temp_c", "p_baseline", "is_day"} <= set(features.columns)
    assert features[FEATURES].notna().all().all()
    assert (features["capacity_kwp"] == 8.0).all()


def test_night_hours_have_zero_irradiance_and_baseline() -> None:
    features = build_features(hourly_weather(), PLANT)
    night = features.loc["2024-06-20T22:00Z"]

    assert night["gti_wm2"] == 0
    assert night["p_baseline"] == 0
    assert not night["is_day"]


def test_solar_position_uses_the_middle_of_the_hour() -> None:
    weather = hourly_weather()
    features = build_features(weather, PLANT)
    # Hour 10:00-11:00 UTC contains solar noon in Košice; the midpoint zenith is the minimum.
    day = features.loc["2024-06-20"]
    assert day["solar_zenith"].idxmin() == pd.Timestamp("2024-06-20T10:00Z")
    assert day.loc["2024-06-20T10:00Z", "solar_zenith"] == pytest.approx(25.6, abs=1.0)


def test_gti_from_weather_is_used_when_present_otherwise_transposed() -> None:
    weather = hourly_weather()
    transposed = build_features(weather, PLANT)["gti_wm2"]

    with_gti = weather.copy()
    with_gti["gti_wm2"] = np.nan
    with_gti.loc["2024-06-20T10:00Z", "gti_wm2"] = 123.0
    features = build_features(with_gti, PLANT)

    assert features.loc["2024-06-20T10:00Z", "gti_wm2"] == 123.0
    assert features.loc["2024-06-20T11:00Z", "gti_wm2"] == pytest.approx(
        transposed.loc["2024-06-20T11:00Z"]
    )
    ignored = build_features(with_gti, PLANT, poa_source="transpose")
    assert ignored.loc["2024-06-20T10:00Z", "gti_wm2"] == pytest.approx(
        transposed.loc["2024-06-20T10:00Z"]
    )


def test_features_do_not_leak_future_rows() -> None:
    weather = hourly_weather()
    cut = pd.Timestamp("2024-06-20T12:00Z")
    original = build_features(weather, PLANT)

    changed = weather.copy()
    changed.loc[changed.index > cut, ["ghi_wm2", "dni_wm2", "dhi_wm2", "temp_air_c"]] = 999.0
    altered = build_features(changed, PLANT)

    past = original.index <= cut
    pd.testing.assert_frame_equal(original.loc[past], altered.loc[past])


def test_build_features_rejects_bad_index() -> None:
    weather = hourly_weather()
    with pytest.raises(ValueError, match="timezone"):
        build_features(weather.tz_localize(None), PLANT)
    with pytest.raises(ValueError, match="full hour"):
        build_features(weather.set_axis(weather.index + pd.Timedelta(minutes=5)), PLANT)
    with pytest.raises(ValueError, match="increasing"):
        build_features(weather.iloc[::-1], PLANT)
    with pytest.raises(ValueError, match="temp_air_c"):
        build_features(weather.drop(columns="temp_air_c"), PLANT)


def test_from_open_meteo_shifts_end_labels_to_hour_start() -> None:
    index = pd.date_range("2024-06-20T01:00Z", periods=3, freq="1h", name="time")
    raw = pd.DataFrame({"ghi_wm2": [1.0, 2.0, 3.0]}, index=index)

    shifted = from_open_meteo(raw)

    assert shifted.index.name == "hour_start_utc"
    assert shifted.index[0] == pd.Timestamp("2024-06-20T00:00Z")
    assert shifted["ghi_wm2"].tolist() == [1.0, 2.0, 3.0]

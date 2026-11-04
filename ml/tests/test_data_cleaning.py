"""Time zones and missing values in the data pipeline and feature building (#173)."""

from __future__ import annotations

from datetime import UTC, datetime
from pathlib import Path

import numpy as np
import pandas as pd
import pytest

from solarops_ml.forecast.data import _utc
from solarops_ml.prediction.features import PlantSpec, build_features, from_open_meteo
from solarops_ml.weather import read_dataset, write_dataset
from solarops_ml.weather.client import OpenMeteoError, parse_response

PLANT = PlantSpec(latitude=48.72, longitude=21.26, capacity_kwp=8.0, tilt=30, azimuth=0)
LOCAL = "Europe/Bratislava"


def weather(start: str, hours: int = 72) -> pd.DataFrame:
    index = pd.date_range(start, periods=hours, freq="1h", tz="UTC", name="hour_start_utc")
    shape = np.clip(np.cos((np.asarray(index.hour) + 0.5 - 10.6) / 24 * 2 * np.pi), 0, None) ** 2
    return pd.DataFrame(
        {
            "ghi_wm2": 900 * shape,
            "dni_wm2": 700 * shape,
            "dhi_wm2": 120 * shape,
            "cloud_cover_pct": 20.0,
            "temp_air_c": 15 + 10 * shape,
        },
        index=index,
    )


# --- time zones ---------------------------------------------------------------------------------


@pytest.mark.parametrize(
    "start",
    [
        "2024-03-30T00:00Z",  # spring DST change (CET -> CEST) on 2024-03-31
        "2024-10-26T00:00Z",  # autumn DST change (CEST -> CET) on 2024-10-27
    ],
)
def test_local_time_index_gives_the_same_features_as_utc_across_dst(start: str) -> None:
    utc = weather(start)
    local = utc.tz_convert(LOCAL)

    from_utc = build_features(utc, PLANT)
    from_local = build_features(local, PLANT)

    assert str(from_local.index.tz) == "UTC"
    pd.testing.assert_frame_equal(from_utc, from_local)


def test_open_meteo_end_label_shift_crosses_midnight_in_utc() -> None:
    index = pd.date_range("2024-06-21T00:00Z", periods=2, freq="1h", name="time")

    shifted = from_open_meteo(pd.DataFrame({"temp_air_c": [10.0, 11.0]}, index=index))

    assert shifted.index[0] == pd.Timestamp("2024-06-20T23:00Z")
    assert str(shifted.index.tz) == "UTC"


def test_naive_timestamps_are_utc_and_local_ones_are_converted() -> None:
    assert _utc(datetime(2024, 6, 21, 12, 0)) == pd.Timestamp("2024-06-21T12:00Z")
    local = pd.Timestamp("2024-06-21T12:00", tz=LOCAL)  # CEST = UTC+2

    converted = _utc(local)

    assert converted == pd.Timestamp("2024-06-21T10:00Z")
    assert str(converted.tz) == "UTC"
    assert _utc(datetime(2024, 6, 21, 12, 0, tzinfo=UTC)) == converted + pd.Timedelta(hours=2)


@pytest.mark.parametrize("suffix", [".parquet", ".csv"])
def test_dataset_round_trip_keeps_utc_index_and_missing_values(tmp_path: Path, suffix: str) -> None:
    frame = weather("2024-06-20T00:00Z", 6).rename_axis("time")
    frame.index = frame.index.astype("datetime64[ns, UTC]")  # the reader normalises to ns
    frame.iloc[2, frame.columns.get_loc("temp_air_c")] = np.nan
    frame.iloc[3, frame.columns.get_loc("ghi_wm2")] = np.nan

    path = write_dataset(frame, tmp_path / f"w{suffix}")
    back = read_dataset(path)

    assert str(back.index.tz) == "UTC"
    assert back.index.equals(frame.index)
    assert back["temp_air_c"].isna().tolist() == frame["temp_air_c"].isna().tolist()
    assert back["ghi_wm2"].isna().tolist() == frame["ghi_wm2"].isna().tolist()
    pd.testing.assert_frame_equal(back, frame, check_freq=False, check_dtype=False)


# --- missing values -----------------------------------------------------------------------------


def test_parse_response_turns_nulls_and_garbage_into_nan() -> None:
    times = [1718928000, 1718931600, 1718935200]  # 2024-06-21T00:00Z + n hours
    payload = {
        "hourly": {
            "time": times,
            "temperature_2m": [10.0, None, 12.0],
            "cloud_cover": [None, None, None],
            "shortwave_radiation": [1.0, "n/a", 3.0],
        }
    }

    frame = parse_response(payload)

    assert str(frame.index.tz) == "UTC"
    assert frame.index[0] == pd.Timestamp("2024-06-21T00:00Z")
    assert frame["temp_air_c"].isna().tolist() == [False, True, False]
    assert frame["cloud_cover_pct"].isna().all()
    assert frame["ghi_wm2"].isna().tolist() == [False, True, False]
    assert frame["wind_speed_ms"].isna().all()  # variable absent from the payload


def test_parse_response_without_the_block_fails_loudly() -> None:
    with pytest.raises(OpenMeteoError, match="hourly"):
        parse_response({"timezone": "UTC"})


def test_missing_cloud_cover_defaults_to_clear_and_stays_finite() -> None:
    data = weather("2024-06-20T00:00Z", 24)
    data["cloud_cover_pct"] = np.nan

    features = build_features(data, PLANT)

    assert (features["cloud_cover_pct"] == 0).all()
    assert np.isfinite(features["kt"]).all()


def test_missing_gti_falls_back_to_transposed_irradiance_per_hour() -> None:
    data = weather("2024-06-20T00:00Z", 24)
    data["gti_wm2"] = 800.0
    data.iloc[10, data.columns.get_loc("gti_wm2")] = np.nan  # one missing hour

    mixed = build_features(data, PLANT)
    transposed = build_features(data.drop(columns="gti_wm2"), PLANT)

    assert mixed["gti_wm2"].iloc[9] == pytest.approx(800.0)
    assert mixed["gti_wm2"].iloc[10] == pytest.approx(transposed["gti_wm2"].iloc[10])
    assert mixed["gti_wm2"].iloc[10] > 0


def test_missing_irradiance_is_not_silently_turned_into_darkness() -> None:
    data = weather("2024-06-20T00:00Z", 24)
    data.iloc[11, [data.columns.get_loc(c) for c in ("ghi_wm2", "dni_wm2", "dhi_wm2")]] = np.nan

    features = build_features(data, PLANT)

    assert not features["is_day"].iloc[11]
    assert features["gti_wm2"].iloc[10] > 0  # neighbours untouched
    assert features["gti_wm2"].iloc[12] > 0

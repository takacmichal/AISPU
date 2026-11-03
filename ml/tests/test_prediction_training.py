from __future__ import annotations

import json
from datetime import UTC, datetime, timedelta
from pathlib import Path

import numpy as np
import pandas as pd
import pytest

from solarops_ml.forecast.data import ArraySpec
from solarops_ml.prediction.cli import main
from solarops_ml.prediction.features import FEATURES
from solarops_ml.prediction.model import PhysicalModel
from solarops_ml.prediction.registry import (
    ModelLoadError,
    data_hash,
    list_versions,
    load_model,
    resolve_current,
    save_model,
    set_current,
)
from solarops_ml.prediction.training import (
    Split,
    load_table,
    select_model,
    synthetic_table,
    time_split,
    train,
)
from solarops_ml.weather import write_dataset

START = datetime(2023, 3, 1, tzinfo=UTC)
ARRAY = ArraySpec("south_20", tilt=20, azimuth=180, capacity_kwp=8, dc_ac_ratio=1.2)
NOW = datetime(2026, 11, 3, 12, 0, tzinfo=UTC)


@pytest.fixture(scope="module")
def table() -> pd.DataFrame:
    return synthetic_table(START, START + timedelta(days=40), (ARRAY,), seed=7)


@pytest.fixture(scope="module")
def result(table: pd.DataFrame):
    return train(table, seed=7, test_days=10, gap_days=2)


def test_synthetic_table_is_deterministic_and_documented(table: pd.DataFrame) -> None:
    again = synthetic_table(START, START + timedelta(days=40), (ARRAY,), seed=7)
    other = synthetic_table(START, START + timedelta(days=40), (ARRAY,), seed=8)

    pd.testing.assert_frame_equal(table, again)
    assert not table["power_w"].equals(other["power_w"])
    assert set(FEATURES) <= set(table.columns)
    assert {"array_id", "power_w", "y", "y_lag_24h"} <= set(table.columns)
    assert "temperature_c" not in table.columns  # telemetry temperature leaks the target
    assert table.index.name == "hour_start_utc"
    assert len(table) == 40 * 24


def test_synthetic_irradiance_components_transpose_to_the_plane_irradiance(
    table: pd.DataFrame,
) -> None:
    from solarops_ml.prediction.features import PlantSpec, build_features

    weather = table[["ghi_wm2", "dni_wm2", "dhi_wm2", "air_temp_c"]].rename(
        columns={"air_temp_c": "temp_air_c"}
    )
    plant = PlantSpec(-23.76, 133.87, ARRAY.capacity_kwp, ARRAY.tilt, ARRAY.azimuth)
    transposed = build_features(weather, plant)["gti_wm2"]
    midday = table["solar_zenith"] < 60

    ratio = transposed[midday] / table.loc[midday, "gti_wm2"]
    assert ratio.median() == pytest.approx(1.0, abs=0.03)


def test_time_split_leaves_a_gap_before_the_test_window(table: pd.DataFrame) -> None:
    split = time_split(table.index, test_days=10, gap_days=2)

    assert split.test_end == table.index.max() + pd.Timedelta(hours=1)
    assert split.test_start == split.test_end - pd.Timedelta(days=10)
    assert split.train_end == split.test_start - pd.Timedelta(days=2)
    assert split.train_start == table.index.min()
    with pytest.raises(ValueError, match="test_days"):
        time_split(table.index, test_days=60, gap_days=2)


def test_train_compares_models_on_the_same_rows(result) -> None:
    summary = result.metrics["summary"]

    assert {"persistence", "physical", "physical_calibrated"} <= set(summary)
    counts = {name: stats["n"] for name, stats in summary.items()}
    assert len(set(counts.values())) == 1 and next(iter(counts.values())) > 50
    assert summary["physical"]["nmae_pct"] < summary["persistence"]["nmae_pct"]
    assert result.selected in ("physical", "physical_calibrated")
    assert result.selection["reason"]
    assert isinstance(result.model, PhysicalModel)
    # The simulator uses the same physics, so calibration must land near the defaults.
    assert result.model.loss_factor == pytest.approx(1.0, abs=0.05)


def test_train_picks_calibration_when_the_plant_has_extra_losses(table: pd.DataFrame) -> None:
    lossy = table.copy()
    lossy["power_w"] *= 0.85
    lossy["y"] *= 0.85
    lossy["y_lag_24h"] *= 0.85

    result = train(lossy, seed=7, test_days=10, gap_days=2)

    summary = result.metrics["summary"]
    assert result.selected == "physical_calibrated"
    assert summary["physical_calibrated"]["nmae_pct"] < summary["physical"]["nmae_pct"]
    assert result.model.loss_factor == pytest.approx(0.85, abs=0.03)


def test_select_model_applies_the_adr_191_gate() -> None:
    summary = {
        "persistence": {"nmae_pct": 9.0},
        "physical": {"nmae_pct": 5.0},
        "physical_calibrated": {"nmae_pct": 4.8},
        "lightgbm": {"nmae_pct": 3.5},
    }

    selection = select_model(summary)

    assert selection["selected"] == "physical_calibrated"
    assert selection["gate"]["lightgbm"]["passed"] is True
    assert selection["gate"]["lightgbm"]["skill_vs_physical"] == pytest.approx(0.3)
    assert "lightgbm" in selection["note"]

    worse = select_model({**summary, "physical_calibrated": {"nmae_pct": 5.5}})
    assert worse["selected"] == "physical"
    failing = select_model({**summary, "lightgbm": {"nmae_pct": 4.5}})
    assert failing["gate"]["lightgbm"]["passed"] is False


def test_lightgbm_challenger_is_optional(table: pd.DataFrame) -> None:
    pytest.importorskip("lightgbm")

    result = train(table, seed=7, test_days=10, gap_days=2, include_challenger=True)

    assert "lightgbm" in result.metrics["summary"]
    assert result.selected in ("physical", "physical_calibrated")


def test_data_hash_is_stable_and_sensitive(table: pd.DataFrame) -> None:
    changed = table.copy()
    changed.iloc[100, changed.columns.get_loc("power_w")] += 1

    assert data_hash(table) == data_hash(table.copy())
    assert data_hash(table) == data_hash(table[list(reversed(table.columns))])
    assert data_hash(table) != data_hash(changed)
    assert len(data_hash(table)) == 64


def test_save_and_load_versioned_model(tmp_path: Path, result) -> None:
    path = save_model(result, tmp_path, now=NOW)

    version = path.name
    assert version.startswith("20261103-physical")
    assert version.endswith(result.data["sha256"][:8])
    metadata = json.loads((path / "metadata.json").read_text())
    for key in ("version", "created_at", "seed", "model", "data", "split", "metrics", "selection"):
        assert key in metadata
    assert metadata["seed"] == 7
    assert metadata["data"]["sha256"] == result.data["sha256"]
    assert metadata["environment"]["numpy"] == np.__version__
    assert (path / "model.json").exists()
    assert "| physical |" in (path / "metrics.md").read_text()
    assert resolve_current(tmp_path) == path

    loaded = load_model(path)
    assert loaded.version == version
    assert loaded.model == result.model
    assert loaded.metadata["selection"]["selected"] == result.selected


def test_saving_the_same_run_twice_is_idempotent(tmp_path: Path, result) -> None:
    first = save_model(result, tmp_path, now=NOW)
    second = save_model(result, tmp_path, now=NOW)

    assert first == second
    assert list_versions(tmp_path) == [first.name]


def test_rollback_switches_the_current_pointer(tmp_path: Path, result) -> None:
    old = save_model(result, tmp_path, now=NOW - timedelta(days=1))
    new = save_model(result, tmp_path, now=NOW)
    assert resolve_current(tmp_path) == new

    set_current(tmp_path, old.name)

    assert resolve_current(tmp_path) == old
    assert list_versions(tmp_path) == [old.name, new.name]
    with pytest.raises(ModelLoadError, match="unknown"):
        set_current(tmp_path, "nope")


def test_load_model_reports_broken_artifacts(tmp_path: Path) -> None:
    with pytest.raises(ModelLoadError):
        load_model(tmp_path / "missing")
    broken = tmp_path / "broken"
    broken.mkdir()
    (broken / "model.json").write_text("{not json")
    (broken / "metadata.json").write_text("{}")
    with pytest.raises(ModelLoadError):
        load_model(broken)
    assert resolve_current(tmp_path / "empty") is None


def write_real_inputs(tmp_path: Path, table: pd.DataFrame) -> tuple[Path, Path, Path]:
    production = tmp_path / "production.csv"
    out = table.reset_index()[["hour_start_utc", "array_id", "power_w"]].copy()
    out["hour_start_utc"] = out["hour_start_utc"].dt.strftime("%Y-%m-%dT%H:%M:%SZ")
    out.to_csv(production, index=False)

    arrays = tmp_path / "arrays.csv"
    pd.DataFrame(
        [
            {
                "array_id": ARRAY.name,
                "tilt": ARRAY.tilt,
                "azimuth": ARRAY.azimuth,
                "capacity_kwp": ARRAY.capacity_kwp,
                "dc_ac_ratio": ARRAY.dc_ac_ratio,
            }
        ]
    ).to_csv(arrays, index=False)

    # Weather in the solarops-weather-2 format: Open-Meteo end-of-hour labels.
    weather = table[["ghi_wm2", "dni_wm2", "dhi_wm2", "cloud_cover_pct", "air_temp_c"]].rename(
        columns={"air_temp_c": "temp_air_c"}
    )
    weather.index = pd.DatetimeIndex(weather.index + pd.Timedelta(hours=1), name="time")
    weather["gti_wm2"] = weather["ghi_wm2"]  # horizontal GTI, must be ignored by default
    weather_path = write_dataset(weather, tmp_path / "weather.parquet")
    return production, arrays, weather_path


def test_load_table_joins_files_on_hour_start(tmp_path: Path, table: pd.DataFrame) -> None:
    production, arrays, weather = write_real_inputs(tmp_path, table)

    loaded = load_table(production, weather, arrays, latitude=-23.76, longitude=133.87)

    assert loaded.index.name == "hour_start_utc"
    assert loaded.index[0] == table.index[0]
    np.testing.assert_allclose(loaded["power_w"], table["power_w"])
    np.testing.assert_allclose(loaded["temp_air_c"], table["air_temp_c"])
    # Transposed POA, not the horizontal GTI column of the weather file.
    midday = table["solar_zenith"] < 60
    ratio = loaded.loc[midday, "gti_wm2"] / table.loc[midday, "gti_wm2"]
    assert ratio.median() == pytest.approx(1.0, abs=0.03)


def test_load_table_rejects_unknown_arrays(tmp_path: Path, table: pd.DataFrame) -> None:
    production, arrays, weather = write_real_inputs(tmp_path, table)
    pd.DataFrame([{"array_id": "other", "tilt": 20, "azimuth": 0, "capacity_kwp": 5}]).to_csv(
        arrays, index=False
    )

    with pytest.raises(ValueError, match="south_20"):
        load_table(production, weather, arrays, latitude=-23.76, longitude=133.87)


def test_cli_trains_on_synthetic_data(tmp_path: Path, capsys) -> None:
    code = main(
        [
            "train",
            "--synthetic",
            "--start",
            "2023-03-01",
            "--end",
            "2023-04-10",
            "--array",
            "south_20,20,180,8,1.2",
            "--test-days",
            "10",
            "--gap-days",
            "2",
            "--seed",
            "7",
            "--model-dir",
            str(tmp_path),
        ]
    )

    assert code == 0
    current = resolve_current(tmp_path)
    assert current is not None
    assert "physical" in capsys.readouterr().out
    metadata = json.loads((current / "metadata.json").read_text())
    assert metadata["data"]["source"] == "synthetic"


def test_cli_builds_training_table_from_files(tmp_path: Path, table: pd.DataFrame) -> None:
    production, arrays, weather = write_real_inputs(tmp_path, table)
    out = tmp_path / "table.parquet"

    code = main(
        [
            "build-table",
            "--production",
            str(production),
            "--weather",
            str(weather),
            "--arrays",
            str(arrays),
            "--lat",
            "-23.76",
            "--lon",
            "133.87",
            "--out",
            str(out),
        ]
    )

    assert code == 0
    assert set(FEATURES) <= set(pd.read_parquet(out).columns)


def test_cli_use_and_list(tmp_path: Path, result, capsys) -> None:
    old = save_model(result, tmp_path, now=NOW - timedelta(days=1))
    save_model(result, tmp_path, now=NOW)

    assert main(["use", old.name, "--model-dir", str(tmp_path)]) == 0
    assert resolve_current(tmp_path) == old
    assert main(["list", "--model-dir", str(tmp_path)]) == 0
    assert old.name in capsys.readouterr().out
    assert main(["use", "missing", "--model-dir", str(tmp_path)]) == 2


def test_split_is_serializable(result) -> None:
    assert isinstance(result.split, Split)
    assert set(result.split.to_dict()) == {
        "train_start",
        "train_end",
        "test_start",
        "test_end",
        "gap_days",
    }

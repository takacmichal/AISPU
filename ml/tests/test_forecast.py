from __future__ import annotations

from datetime import UTC, datetime, timedelta

import numpy as np
import pandas as pd
import pytest

from solarops_ml.forecast.data import (
    FEATURES,
    ArraySpec,
    Fold,
    build_dataset,
    default_folds,
    evaluation_mask,
    train_rows,
)
from solarops_ml.forecast.metrics import regression_metrics, skill_score
from solarops_ml.forecast.models import fit_predict

START = datetime(2023, 1, 1, tzinfo=UTC)
SPEC = ArraySpec("test_array", tilt=20, azimuth=180, capacity_kwp=8, dc_ac_ratio=1.2)


def tiny_dataset(days: int = 40) -> pd.DataFrame:
    return build_dataset(START, START + timedelta(days=days), (SPEC,))


def test_array_spec_validates_inputs() -> None:
    with pytest.raises(ValueError, match="name"):
        ArraySpec("", tilt=20, azimuth=180, capacity_kwp=8)
    with pytest.raises(ValueError, match="tilt"):
        ArraySpec("a", tilt=91, azimuth=180, capacity_kwp=8)
    with pytest.raises(ValueError, match="azimuth"):
        ArraySpec("a", tilt=20, azimuth=181, capacity_kwp=8)
    with pytest.raises(ValueError, match="capacity"):
        ArraySpec("a", tilt=20, azimuth=180, capacity_kwp=0)
    with pytest.raises(ValueError, match="dc_ac"):
        ArraySpec("a", tilt=20, azimuth=180, capacity_kwp=8, dc_ac_ratio=0)


def test_fold_validates_order_and_gap() -> None:
    with pytest.raises(ValueError, match="timezone"):
        Fold(
            "bad",
            pd.Timestamp("2024-01-01"),
            pd.Timestamp("2024-07-01"),
            pd.Timestamp("2025-01-01"),
        )
    with pytest.raises(ValueError, match="train_start"):
        Fold(
            "bad",
            train_start=pd.Timestamp("2024-07-01T00:00Z"),
            test_start=pd.Timestamp("2024-01-01T00:00Z"),
            test_end=pd.Timestamp("2025-01-01T00:00Z"),
        )
    with pytest.raises(ValueError, match="gap_days"):
        Fold(
            "bad",
            train_start=pd.Timestamp("2023-01-01T00:00Z"),
            test_start=pd.Timestamp("2024-01-01T00:00Z"),
            test_end=pd.Timestamp("2025-01-01T00:00Z"),
            gap_days=-1,
        )


def test_default_folds_are_expanding_and_need_two_years() -> None:
    first, second = default_folds(
        pd.Timestamp("2023-01-01T00:00Z"), pd.Timestamp("2025-01-01T00:00Z")
    )
    assert (first.name, second.name) == ("2024H1", "2024H2")
    assert first.train_end == pd.Timestamp("2023-12-25T00:00Z")
    assert second.train_start == first.train_start
    assert second.train_end == pd.Timestamp("2024-06-24T00:00Z")
    with pytest.raises(ValueError, match="default folds"):
        default_folds(pd.Timestamp("2023-01-01T00:00Z"), pd.Timestamp("2024-07-01T00:00Z"))


def test_build_dataset_has_unique_array_timestamps_and_features() -> None:
    frame = tiny_dataset(days=10)
    assert set(FEATURES).issubset(frame.columns)
    assert {"y", "p_baseline", "array_id", "is_day"}.issubset(frame.columns)
    assert {"air_temp_c", "cell_temp_c", "temperature_c"}.issubset(frame.columns)
    assert not frame.reset_index().duplicated(["array_id", "timestamp"]).any()
    assert frame["y"].dropna().between(0, 1.2).all()
    assert frame["p_baseline"].dropna().ge(0).all()
    assert frame.loc[frame["is_day"], FEATURES].notna().all().all()
    assert frame["y_lag_24h"].isna().sum() >= 24


def test_leaky_telemetry_columns_are_not_features() -> None:
    # temperature_c is derived from actual AC power in the simulator and would leak y.
    assert "temperature_c" not in FEATURES
    assert "air_temp_c" in FEATURES
    # p_baseline is a model output, not an input to the compared ML models.
    assert "p_baseline" not in FEATURES


def test_evaluation_mask_uses_daylight_and_persistence_lag() -> None:
    frame = tiny_dataset(days=10)
    fold = Fold(
        "test",
        train_start=pd.Timestamp(START),
        test_start=pd.Timestamp(START + timedelta(days=8)),
        test_end=pd.Timestamp(START + timedelta(days=10)),
    )
    mask = evaluation_mask(frame, fold)
    selected = frame.loc[mask]
    assert len(selected) > 0
    assert selected["is_day"].all()
    assert selected["y_lag_24h"].notna().all()
    assert selected.index.min() >= fold.test_start
    assert selected.index.max() < fold.test_end
    assert train_rows(frame, fold).index.max() < fold.train_end


def test_metrics_match_manual_values() -> None:
    y_true = np.array([0.2, 0.4, 0.8])
    y_pred = np.array([0.1, 0.5, 0.6])
    result = regression_metrics(y_true, y_pred)
    assert result["n"] == 3
    assert result["mae"] == pytest.approx(0.4 / 3)
    assert result["rmse"] == pytest.approx(np.sqrt(0.06 / 3))
    assert result["nmae_pct"] == pytest.approx(100 * 0.4 / 3)
    assert result["wmape_pct"] == pytest.approx(100 * 0.4 / 1.4)
    assert result["r2"] == pytest.approx(1 - 0.06 / 0.1866666667)
    assert skill_score(0.05, 0.10) == pytest.approx(0.5)
    assert np.isnan(skill_score(0.05, 0.0))


def test_metrics_reject_bad_input() -> None:
    with pytest.raises(ValueError, match="same shape"):
        regression_metrics(np.array([1.0]), np.array([1.0, 2.0]))
    with pytest.raises(ValueError, match="at least one"):
        regression_metrics(np.array([]), np.array([]))
    with pytest.raises(ValueError, match="finite"):
        regression_metrics(np.array([1.0, np.nan]), np.array([1.0, 1.0]))


def test_persistence_and_physical_baselines_are_passthrough() -> None:
    frame = tiny_dataset(days=5)
    fold = Fold(
        "test",
        train_start=pd.Timestamp(START),
        test_start=pd.Timestamp(START + timedelta(days=3)),
        test_end=pd.Timestamp(START + timedelta(days=5)),
    )
    history = frame.loc[frame.index < fold.train_end]
    test = frame.loc[(frame.index >= fold.test_start) & (frame.index < fold.test_end)]
    persistence = fit_predict("persistence", history, test, history)
    physical = fit_predict("physical", history, test, history)
    np.testing.assert_allclose(persistence.y_pred, test["y_lag_24h"].to_numpy(), equal_nan=True)
    np.testing.assert_allclose(physical.y_pred, test["p_baseline"].to_numpy(), equal_nan=True)
    assert persistence.train_seconds == 0.0
    assert physical.train_seconds == 0.0


def test_unknown_model_raises() -> None:
    frame = tiny_dataset(days=2)
    with pytest.raises(ValueError, match="unknown model"):
        fit_predict("transformer", frame, frame, frame)


def test_experiment_runs_all_models_on_a_small_sample() -> None:
    pytest.importorskip("lightgbm")
    pytest.importorskip("sklearn")
    pytest.importorskip("torch")

    from solarops_ml.forecast.experiment import ExperimentConfig, render_markdown, run_experiment

    start = pd.Timestamp("2023-01-01T00:00Z")
    end = start + pd.Timedelta(days=120)
    fold = Fold(
        "smoke",
        train_start=start,
        test_start=start + pd.Timedelta(days=90),
        test_end=end,
    )
    result = run_experiment(
        ExperimentConfig(
            start=start,
            end=end,
            arrays=(SPEC,),
            folds=(fold,),
            include_lstm=True,
            include_loao=False,
            lstm_epochs=1,
        )
    )
    models = {row["model"] for row in result["details"]}
    assert models == {"persistence", "physical", "linear", "lightgbm", "lstm"}
    assert result["summary"]["walk_forward"]
    assert result["details"][0]["n"] > 0
    markdown = render_markdown(result)
    assert "Walk-forward" in markdown
    assert "lightgbm" in markdown

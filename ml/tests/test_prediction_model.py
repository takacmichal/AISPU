from __future__ import annotations

import numpy as np
import pandas as pd
import pytest

from solarops_ml.prediction.features import PlantSpec, build_features
from solarops_ml.prediction.model import PhysicalModel


def features_frame(days: int = 20, seed: int = 0) -> pd.DataFrame:
    rng = np.random.default_rng(seed)
    index = pd.date_range("2024-05-01T00:00Z", periods=24 * days, freq="1h", tz="UTC")
    hour = np.asarray(index.hour)
    shape = np.clip(np.cos((hour + 0.5 - 10.6) / 24 * 2 * np.pi), 0, None) ** 2
    cloud = rng.uniform(0.3, 1.0, len(index))
    weather = pd.DataFrame(
        {
            "ghi_wm2": 950 * shape * cloud,
            "dni_wm2": 750 * shape * cloud,
            "dhi_wm2": 110 * shape * cloud,
            "cloud_cover_pct": 100 * (1 - cloud),
            "temp_air_c": 12 + 15 * shape + rng.normal(0, 2, len(index)),
        },
        index=index,
    )
    plant = PlantSpec(latitude=48.72, longitude=21.26, capacity_kwp=10, tilt=30, azimuth=0)
    return build_features(weather, plant)


def test_default_model_matches_the_physical_baseline() -> None:
    features = features_frame()

    predicted = PhysicalModel().predict_normalized(features)

    np.testing.assert_allclose(predicted, features["p_baseline"].to_numpy())


def test_prediction_is_zero_at_night_and_clipped_at_ac_limit() -> None:
    features = features_frame()
    features.loc[features.index[12], "gti_wm2"] = 1400.0
    features.loc[features.index[12], "air_temp_c"] = -10.0

    predicted = PhysicalModel().predict_normalized(features)

    night = ~features["is_day"].to_numpy() & (features["gti_wm2"].to_numpy() == 0)
    assert (predicted[night] == 0).all()
    assert predicted.max() == pytest.approx(1 / 1.2)


def test_power_is_normalized_prediction_times_capacity() -> None:
    features = features_frame()
    model = PhysicalModel()

    np.testing.assert_allclose(
        model.predict_power_w(features), model.predict_normalized(features) * 10_000
    )


def test_fit_recovers_loss_factor_and_temperature_coefficient() -> None:
    features = features_frame()
    truth = PhysicalModel(loss_factor=0.9, gamma_per_k=-0.005)
    y = truth.predict_normalized(features)

    fitted = PhysicalModel().fit(features, y)

    assert fitted.loss_factor == pytest.approx(0.9, abs=1e-6)
    assert fitted.gamma_per_k == pytest.approx(-0.005, abs=1e-6)
    assert fitted.fitted_rows > 100


def test_fit_ignores_missing_targets_and_needs_enough_rows() -> None:
    features = features_frame()
    y = PhysicalModel(loss_factor=0.95).predict_normalized(features)
    y[::3] = np.nan

    assert PhysicalModel().fit(features, y).loss_factor == pytest.approx(0.95, abs=1e-6)
    with pytest.raises(ValueError, match="daytime"):
        PhysicalModel().fit(features.iloc[:10], y[:10])


def test_fit_keeps_parameters_within_physical_bounds() -> None:
    features = features_frame()
    y = PhysicalModel(gamma_per_k=0.02).predict_normalized(features)

    fitted = PhysicalModel().fit(features, y)

    assert -0.01 <= fitted.gamma_per_k <= 0.0
    assert 0.3 <= fitted.loss_factor <= 1.5


def test_round_trip_through_dict() -> None:
    model = PhysicalModel(loss_factor=0.93, gamma_per_k=-0.0042, fitted_rows=123)

    assert PhysicalModel.from_dict(model.to_dict()) == model
    with pytest.raises(ValueError, match="family"):
        PhysicalModel.from_dict({**model.to_dict(), "family": "lightgbm"})

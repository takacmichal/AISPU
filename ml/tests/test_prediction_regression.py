"""Regression guard on the model metrics (#174): the build fails when the error exceeds the limit.

The synthetic table is generated with the same physics as the model, so the absolute error is tiny
(nMAE about 0.09 %). The limit is deliberately loose (about 10x) so only a real defect trips it,
for example a broken feature, unit or time shift. It guards the code, not the accuracy on real
plants (ADR #191 targets nMAE around 6 % there).
"""

from __future__ import annotations

from datetime import UTC, datetime, timedelta

import pandas as pd
import pytest
from fastapi.testclient import TestClient

from solarops_ml.api import app
from solarops_ml.forecast.data import ArraySpec
from solarops_ml.prediction.training import synthetic_table, train

START = datetime(2023, 3, 1, tzinfo=UTC)
ARRAY = ArraySpec("south_20", tilt=20, azimuth=180, capacity_kwp=8, dc_ac_ratio=1.2)

MAX_NMAE_PCT = 1.0  # normalised MAE as % of capacity; measured 0.085 on this table
MAX_MAE = 0.01  # MAE in units of installed capacity; measured 0.00085
MIN_R2 = 0.999  # measured 0.99997


@pytest.fixture(scope="module")
def table() -> pd.DataFrame:
    return synthetic_table(START, START + timedelta(days=40), (ARRAY,), seed=7)


def test_selected_model_stays_within_the_error_limit(table: pd.DataFrame) -> None:
    result = train(table, seed=7, test_days=10, gap_days=2)

    stats = result.metrics["summary"][result.selected]

    assert stats["nmae_pct"] <= MAX_NMAE_PCT, f"nMAE {stats['nmae_pct']:.3f} % is above the limit"
    assert stats["mae"] <= MAX_MAE, f"MAE {stats['mae']:.5f} is above the limit"
    assert stats["r2"] >= MIN_R2, f"R2 {stats['r2']:.5f} is below the limit"


def test_the_limit_catches_a_degraded_model(table: pd.DataFrame) -> None:
    # A plant that really produces 15 % less: the uncalibrated baseline looks like a regression.
    lossy = table.copy()
    lossy["power_w"] *= 0.85
    lossy["y"] *= 0.85
    lossy["y_lag_24h"] *= 0.85

    summary = train(lossy, seed=7, test_days=10, gap_days=2).metrics["summary"]

    assert summary["physical"]["nmae_pct"] > MAX_NMAE_PCT
    assert summary["physical"]["mae"] > MAX_MAE


# Golden values (#174): fixed plant and weather through the real endpoint. Unlike the synthetic
# table, these numbers do not depend on the code under test, so a shifted time axis, wrong unit or
# changed physics moves them. Update them only after a deliberate model change (bump the version).
GOLDEN_REQUEST = {
    "installationId": "7f3c2d9e-1b4a-4c55-9a1e-2d6f0b8e1a10",
    "plant": {
        "latitude": 48.15,
        "longitude": 17.11,
        "dcCapacityKw": 8.4,
        "tiltDeg": 30,
        "azimuthDeg": 0,
    },
    "weather": [
        {
            "timestamp": f"2026-06-21T{hour:02d}:00:00Z",
            "ghiWm2": 700.0,
            "dniWm2": 600.0,
            "dhiWm2": 110.0,
            "cloudCoverPct": 20.0,
            "temperatureC": 24.5,
            "windSpeedMs": 2.1,
        }
        for hour in (8, 10, 12)
    ],
}
GOLDEN_POWER_W = {  # measured with physical-baseline-0.1.0
    "2026-06-21T08:00:00Z": 4561.2,
    "2026-06-21T10:00:00Z": 5207.7,
    "2026-06-21T12:00:00Z": 4915.3,
}
GOLDEN_TOLERANCE = 0.01  # 1 %


def test_golden_predictions_do_not_drift() -> None:
    response = TestClient(app).post("/predict", json=GOLDEN_REQUEST)

    assert response.status_code == 200
    got = {p["timestamp"]: p["expectedPowerW"] for p in response.json()["predictions"]}
    assert got == pytest.approx(GOLDEN_POWER_W, rel=GOLDEN_TOLERANCE)

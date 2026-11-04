from __future__ import annotations

from collections.abc import Iterator
from datetime import UTC, datetime
from pathlib import Path

import pytest
from fastapi.testclient import TestClient

from solarops_ml.api import app
from solarops_ml.prediction.model import PhysicalModel
from solarops_ml.prediction.service import get_clock, get_model_store

NOW = datetime(2026, 6, 20, 8, 0, 3, tzinfo=UTC)
HOURS = [8, 9, 10, 11, 12]


def weather_rows(hours: list[int] = HOURS, **overrides: float) -> list[dict]:
    rows = []
    for h in hours:
        row = {
            "timestamp": f"2026-06-21T{h:02d}:00:00Z",
            "ghiWm2": 700.0,
            "dniWm2": 600.0,
            "dhiWm2": 110.0,
            "cloudCoverPct": 20.0,
            "temperatureC": 24.5,
            "windSpeedMs": 2.1,
        }
        row.update(overrides)
        rows.append(row)
    return rows


def body(**plant: float) -> dict:
    return {
        "installationId": "7f3c2d9e-1b4a-4c55-9a1e-2d6f0b8e1a10",
        "plant": {
            "latitude": 48.15,
            "longitude": 17.11,
            "dcCapacityKw": 8.4,
            "tiltDeg": 30,
            "azimuthDeg": 0,
            **plant,
        },
        "weather": weather_rows(),
    }


@pytest.fixture
def client() -> Iterator[TestClient]:
    app.dependency_overrides[get_clock] = lambda: lambda: NOW
    yield TestClient(app)
    app.dependency_overrides.clear()


def test_predict_returns_hourly_power_and_energy(client: TestClient) -> None:
    response = client.post("/predict", json=body())

    assert response.status_code == 200
    data = response.json()
    assert data["modelVersion"].startswith("physical-baseline")
    assert data["generatedAt"] == "2026-06-20T08:00:03Z"
    assert [p["timestamp"] for p in data["predictions"]] == [
        f"2026-06-21T{h:02d}:00:00Z" for h in HOURS
    ]
    first = data["predictions"][0]
    assert 0 < first["expectedPowerW"] <= 8400
    assert first["expectedEnergyWh"] == pytest.approx(first["expectedPowerW"])


def test_versioned_route_is_the_same_handler(client: TestClient) -> None:
    a = client.post("/predict", json=body()).json()
    b = client.post("/v1/predictions/production", json=body()).json()

    assert a == b


def test_night_hours_predict_zero_and_ac_limit_is_respected(client: TestClient) -> None:
    payload = body(dcCapacityKw=10)
    payload["weather"] = weather_rows([0], ghiWm2=0.0, dniWm2=0.0, dhiWm2=0.0) + weather_rows(
        [11], ghiWm2=1300.0, dniWm2=1100.0, dhiWm2=150.0
    )
    payload["plant"]["dcAcRatio"] = 1.25

    predictions = client.post("/predict", json=payload).json()["predictions"]

    assert predictions[0]["expectedPowerW"] == 0
    assert predictions[1]["expectedPowerW"] == pytest.approx(8000)


def test_gti_in_request_is_used(client: TestClient) -> None:
    plain = client.post("/predict", json=body()).json()["predictions"][2]["expectedPowerW"]
    payload = body()
    payload["weather"] = weather_rows(gtiWm2=300.0)

    with_gti = client.post("/predict", json=payload).json()["predictions"][2]["expectedPowerW"]

    assert with_gti < plain


def test_larger_capacity_scales_linearly(client: TestClient) -> None:
    small = client.post("/predict", json=body(dcCapacityKw=4)).json()["predictions"]
    large = client.post("/predict", json=body(dcCapacityKw=8)).json()["predictions"]

    for s, big in zip(small, large, strict=True):
        assert big["expectedPowerW"] == pytest.approx(2 * s["expectedPowerW"], rel=1e-3)


@pytest.mark.parametrize(
    "mutate",
    [
        lambda b: b["plant"].update(latitude=95),
        lambda b: b["plant"].update(tiltDeg=120),
        lambda b: b["plant"].update(azimuthDeg=270),
        lambda b: b["plant"].update(dcCapacityKw=0),
        lambda b: b["plant"].pop("longitude"),
        lambda b: b.update(weather=[]),
        lambda b: b["weather"][0].update(timestamp="2026-06-21T08:30:00Z"),
        lambda b: b["weather"][0].update(timestamp="2026-06-21T08:00:00"),
        lambda b: b["weather"][1].update(timestamp=b["weather"][0]["timestamp"]),
        lambda b: b["weather"][0].pop("temperatureC"),
        lambda b: b["weather"][0].update(ghiWm2=-5),
        lambda b: b["weather"][0].update(cloudCoverPct=120),
        lambda b: b["weather"][0].pop("ghiWm2"),
        lambda b: b.update(weather=b["weather"] * 40),
        lambda b: b.update(installationId="not-a-uuid"),
    ],
)
def test_invalid_input_returns_422_problem_json(client: TestClient, mutate) -> None:
    payload = body()
    mutate(payload)

    response = client.post("/predict", json=payload)

    assert response.status_code == 422
    assert response.headers["content-type"].startswith("application/problem+json")
    problem = response.json()
    assert problem["status"] == 422
    assert problem["errors"]
    assert problem["traceId"]


def test_missing_irradiance_without_gti_is_422(client: TestClient) -> None:
    payload = body()
    del payload["weather"][0]["dniWm2"]

    response = client.post("/predict", json=payload)

    assert response.status_code == 422


def test_unsorted_weather_is_422(client: TestClient) -> None:
    payload = body()
    payload["weather"].reverse()

    assert client.post("/predict", json=payload).status_code == 422


def test_health_reports_model_version(client: TestClient) -> None:
    response = client.get("/health")

    assert response.status_code == 200
    data = response.json()
    assert data["status"] == "ok"
    assert data["modelVersion"].startswith("physical-baseline")
    assert "version" in data


def test_openapi_documents_predict(client: TestClient) -> None:
    schema = client.get("/openapi.json").json()

    assert "/predict" in schema["paths"]
    assert "/v1/predictions/production" in schema["paths"]
    assert "422" in schema["paths"]["/predict"]["post"]["responses"]
    assert "PredictionRequest" in schema["components"]["schemas"]


def test_stored_model_is_served_and_changes_predictions(tmp_path: Path) -> None:
    from dataclasses import replace

    from solarops_ml.prediction.registry import LoadedModel
    from solarops_ml.prediction.service import ModelStore

    default = ModelStore.baseline()
    lossy = ModelStore.fixed(
        LoadedModel("20261103-physical_calibrated-abcd1234", PhysicalModel(loss_factor=0.8), {})
    )
    app.dependency_overrides[get_clock] = lambda: lambda: NOW
    try:
        app.dependency_overrides[get_model_store] = lambda: default
        base = TestClient(app).post("/predict", json=body()).json()
        app.dependency_overrides[get_model_store] = lambda: lossy
        low = TestClient(app).post("/predict", json=body()).json()
        health = TestClient(app).get("/health").json()
    finally:
        app.dependency_overrides.clear()

    assert low["modelVersion"] == "20261103-physical_calibrated-abcd1234"
    assert health["modelVersion"] == "20261103-physical_calibrated-abcd1234"
    ratio = low["predictions"][2]["expectedPowerW"] / base["predictions"][2]["expectedPowerW"]
    assert ratio == pytest.approx(0.8, rel=0.02)
    assert replace(PhysicalModel(), fitted_rows=1).fitted_rows == 1


def test_broken_configured_artifact_returns_503(tmp_path: Path) -> None:
    from solarops_ml.prediction.service import ModelStore

    (tmp_path / "current.json").write_text('{"version": "gone"}')
    store = ModelStore.from_dir(tmp_path)
    app.dependency_overrides[get_model_store] = lambda: store
    try:
        client = TestClient(app)
        predict = client.post("/predict", json=body())
        health = client.get("/health")
    finally:
        app.dependency_overrides.clear()

    assert predict.status_code == 503
    assert predict.headers["content-type"].startswith("application/problem+json")
    assert health.status_code == 503
    assert health.json()["status"] == "unavailable"


def test_empty_model_dir_falls_back_to_baseline(tmp_path: Path) -> None:
    from solarops_ml.prediction.service import ModelStore

    store = ModelStore.from_dir(tmp_path)

    assert store.version().startswith("physical-baseline")

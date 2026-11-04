"""POST /v1/detections/underproduction (contract for the backend, #33 / #115-#117)."""

from __future__ import annotations

import math
from datetime import UTC, datetime, timedelta
from typing import Any

from fastapi.testclient import TestClient

from solarops_ml.api import app
from solarops_ml.detection.api import MAX_POINTS

client = TestClient(app)
URL = "/v1/detections/underproduction"
INSTALLATION = "7f3c2d9e-1b4a-4c55-9a1e-2d6f0b8e1a10"


def iso(value: datetime) -> str:
    return value.strftime("%Y-%m-%dT%H:%M:%SZ")


def points(days: int = 1, factor: float = 1.0, fault_hours: range = range(0)) -> list[dict]:
    start = datetime(2024, 6, 1, tzinfo=UTC)
    out = []
    for i in range(24 * days):
        ts = start + timedelta(hours=i)
        shape = max(math.sin(math.pi * (ts.hour - 6) / 12), 0.0)
        expected = 8000.0 * shape
        actual = expected * (factor if i in fault_hours else 1.0)
        out.append(
            {
                "timestamp": iso(ts),
                "actualPowerW": actual,
                "expectedPowerW": expected,
                "ghiWm2": 1000.0 * shape,
                "cloudCoverPct": 10.0,
                "status": "MPPT" if shape > 0 else "SLEEPING",
                "sampleCoverage": 1.0,
            }
        )
    return out


def body(**overrides: Any) -> dict[str, Any]:
    payload: dict[str, Any] = {
        "installationId": INSTALLATION,
        "ratedPowerW": 10000.0,
        "points": points(1, factor=0.4, fault_hours=range(9, 15)),
    }
    payload.update(overrides)
    return payload


def test_detect_returns_alarm_contract() -> None:
    response = client.post(URL, json=body())
    assert response.status_code == 200
    data = response.json()
    assert data["installationId"] == INSTALLATION
    assert data["parameters"]["ratioThreshold"] == 0.85
    assert data["parameters"]["minConsecutiveHours"] == 2
    assert data["summary"]["underproducingHours"] == 6
    assert len(data["alarms"]) == 1
    alarm = data["alarms"][0]
    assert alarm["type"] == "underproduction"
    assert alarm["installationId"] == INSTALLATION
    assert alarm["start"] == "2024-06-01T09:00:00Z"
    assert alarm["end"] == "2024-06-01T14:00:00Z"
    assert alarm["detectedAt"] == "2024-06-01T10:00:00Z"
    assert alarm["ongoing"] is False
    assert alarm["severity"] == "high"
    assert alarm["ticketPriority"] == "High"
    assert alarm["reasonCode"] == "sustained_underproduction"
    assert alarm["estimatedLostKwh"] > 0
    assert alarm["medianRatio"] == 0.4
    assert len(alarm["evidence"]) == 6
    assert set(alarm["evidence"][0]) == {"timestamp", "actualPowerW", "expectedPowerW", "ratio"}
    assert "hours" not in data or data["hours"] is None


def test_parameters_can_be_overridden_per_request() -> None:
    response = client.post(URL, json=body(parameters={"minConsecutiveHours": 8}))
    assert response.status_code == 200
    assert response.json()["alarms"] == []


def test_include_hours_returns_deviation_per_hour() -> None:
    response = client.post(URL, json=body(includeHours=True))
    hours = response.json()["hours"]
    assert len(hours) == 24
    noon = next(h for h in hours if h["timestamp"] == "2024-06-01T12:00:00Z")
    assert noon["ratio"] == 0.4
    assert noon["state"] == "underproduction"
    night = next(h for h in hours if h["timestamp"] == "2024-06-01T00:00:00Z")
    assert night["ratio"] is None
    assert night["state"] == "excluded"
    assert night["excludedReason"] == "low_irradiance"


def test_missing_actual_values_are_an_outage_not_underproduction() -> None:
    pts = points(1)
    for p in pts:
        p["actualPowerW"] = None
    response = client.post(URL, json=body(points=pts))
    assert response.status_code == 200
    assert response.json()["alarms"] == []
    assert response.json()["summary"]["excludedHours"]["missing_data"] == 24


def test_healthy_series_has_no_alarm() -> None:
    response = client.post(URL, json=body(points=points(2)))
    assert response.status_code == 200
    assert response.json()["alarms"] == []


def test_validation_errors_return_422() -> None:
    assert client.post(URL, json=body(points=[])).status_code == 422
    assert client.post(URL, json=body(installationId="not-a-uuid")).status_code == 422
    assert client.post(URL, json=body(ratedPowerW=0)).status_code == 422
    duplicated = points(1)
    duplicated.append(duplicated[0])
    assert client.post(URL, json=body(points=duplicated)).status_code == 422
    naive = points(1)
    naive[0]["timestamp"] = "2024-06-01T00:00:00"
    assert client.post(URL, json=body(points=naive)).status_code == 422
    bad_param = body(parameters={"ratioThreshold": 2})
    assert client.post(URL, json=bad_param).status_code == 422


def test_too_many_points_is_rejected() -> None:
    pts = points(MAX_POINTS // 24 + 1)
    assert len(pts) > MAX_POINTS
    assert client.post(URL, json=body(points=pts)).status_code == 422

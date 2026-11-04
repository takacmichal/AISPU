"""Prediction service: request/response contract and routes (issue #109, ADR #196).

Two paths serve the same handler: ``POST /predict`` (issue #109) and
``POST /v1/predictions/production`` (path proposed in ADR #196). Time is ISO 8601 UTC; a
weather row labelled ``h`` describes ``[h, h + 1h)`` (ADR #189, ``hour_start_utc``). Raw
Open-Meteo hours are end-labelled, so the caller shifts them by -1 h.

Without a stored model the service runs the uncalibrated physical baseline, the MVP model of
ADR #191 (its version is the code version). With ``$SOLAROPS_MODEL_DIR/current.json`` it
serves that stored version; a configured but unreadable artifact is a 503, never a silent
fallback.
"""

from __future__ import annotations

from collections.abc import Callable
from datetime import UTC, datetime
from functools import lru_cache
from pathlib import Path
from typing import Annotated, Self
from uuid import UUID

import pandas as pd
from fastapi import APIRouter, Depends
from pydantic import AwareDatetime, BaseModel, ConfigDict, Field, model_validator
from pydantic.alias_generators import to_camel

from solarops_ml import __version__
from solarops_ml.prediction.features import DEFAULT_DC_AC_RATIO, PlantSpec, build_features
from solarops_ml.prediction.model import PhysicalModel
from solarops_ml.prediction.registry import (
    LoadedModel,
    ModelLoadError,
    default_model_dir,
    load_model,
    resolve_current,
)

MAX_HOURS = 168
TIME_FORMAT = "%Y-%m-%dT%H:%M:%SZ"
BASELINE_VERSION = f"physical-baseline-{__version__}"

router = APIRouter(tags=["predictions"])


class _Camel(BaseModel):
    model_config = ConfigDict(alias_generator=to_camel, populate_by_name=True, extra="forbid")


class Plant(_Camel):
    latitude: float = Field(ge=-90, le=90, description="WGS84 latitude, degrees")
    longitude: float = Field(ge=-180, le=180, description="WGS84 longitude, degrees")
    dc_capacity_kw: float = Field(gt=0, le=100_000, description="Installed DC capacity, kWp")
    tilt_deg: float = Field(ge=0, le=90, description="Panel tilt, 0 = horizontal, 90 = vertical")
    azimuth_deg: float = Field(
        ge=-180,
        le=180,
        description="Panel azimuth, PVGIS convention as in the .NET API: "
        "0 = south, -90 = east, 90 = west, +-180 = north",
    )
    dc_ac_ratio: float = Field(
        default=DEFAULT_DC_AC_RATIO, gt=0, le=3, description="DC/AC ratio; AC limit = kWp / ratio"
    )


class WeatherHour(_Camel):
    timestamp: AwareDatetime = Field(
        description="Start of the hour in UTC (e.g. 2026-06-21T10:00:00Z); describes [t, t+1h)"
    )
    ghi_wm2: float | None = Field(default=None, ge=0, le=1500, description="Global horizontal")
    dni_wm2: float | None = Field(default=None, ge=0, le=1500, description="Direct normal")
    dhi_wm2: float | None = Field(default=None, ge=0, le=1500, description="Diffuse horizontal")
    gti_wm2: float | None = Field(
        default=None,
        ge=0,
        le=1800,
        description="Irradiance on this plant's plane (Open-Meteo GTI with its tilt/azimuth); "
        "when given it replaces the transposition of GHI/DNI/DHI",
    )
    cloud_cover_pct: float | None = Field(default=None, ge=0, le=100)
    temperature_c: float = Field(ge=-90, le=60, description="Air temperature 2 m, degrees C")
    wind_speed_ms: float | None = Field(default=None, ge=0, le=120)

    @model_validator(mode="after")
    def _hour_and_irradiance(self) -> Self:
        stamp = self.timestamp.astimezone(UTC)
        if stamp.minute or stamp.second or stamp.microsecond:
            raise ValueError("timestamp must be on a full hour (hour start, UTC)")
        has_components = None not in (self.ghi_wm2, self.dni_wm2, self.dhi_wm2)
        if self.gti_wm2 is None and not has_components:
            raise ValueError("provide gtiWm2 or all of ghiWm2, dniWm2 and dhiWm2")
        return self


class PredictionRequest(_Camel):
    installation_id: UUID | None = Field(default=None, description="Echoed for tracing only")
    plant: Plant
    weather: list[WeatherHour] = Field(min_length=1, max_length=MAX_HOURS)

    @model_validator(mode="after")
    def _strictly_increasing(self) -> Self:
        stamps = [w.timestamp.astimezone(UTC) for w in self.weather]
        if any(b <= a for a, b in zip(stamps, stamps[1:], strict=False)):
            raise ValueError("weather timestamps must be strictly increasing and unique")
        return self


class PredictionHour(_Camel):
    timestamp: str = Field(description="Hour start, UTC, ISO 8601 with Z")
    expected_power_w: float = Field(description="Mean AC power over the hour, W")
    expected_energy_wh: float = Field(description="AC energy over the hour, Wh")


class PredictionResponse(_Camel):
    model_version: str
    generated_at: str
    predictions: list[PredictionHour]


class ModelStore:
    """Holds the model the service uses, or the reason it could not be loaded."""

    def __init__(self, loaded: LoadedModel | None, error: str | None = None) -> None:
        self._loaded = loaded
        self._error = error

    @classmethod
    def baseline(cls) -> ModelStore:
        return cls(LoadedModel(BASELINE_VERSION, PhysicalModel(), {}))

    @classmethod
    def fixed(cls, loaded: LoadedModel) -> ModelStore:
        return cls(loaded)

    @classmethod
    def from_dir(cls, model_dir: Path) -> ModelStore:
        try:
            current = resolve_current(model_dir)
            if current is None:
                return cls.baseline()
            return cls(load_model(current))
        except ModelLoadError as exc:
            return cls(None, str(exc))

    def _get(self) -> LoadedModel:
        if self._loaded is None:
            raise ModelLoadError(self._error or "model not loaded")
        return self._loaded

    def version(self) -> str:
        return self._get().version

    def model(self) -> PhysicalModel:
        return self._get().model


@lru_cache(maxsize=1)
def _default_store() -> ModelStore:
    return ModelStore.from_dir(default_model_dir())


def get_model_store() -> ModelStore:
    return _default_store()


def get_clock() -> Callable[[], datetime]:
    return lambda: datetime.now(UTC)


StoreDep = Annotated[ModelStore, Depends(get_model_store)]
ClockDep = Annotated[Callable[[], datetime], Depends(get_clock)]


def predict_hours(request: PredictionRequest, model: PhysicalModel) -> list[PredictionHour]:
    rows = {
        "ghi_wm2": [w.ghi_wm2 for w in request.weather],
        "dni_wm2": [w.dni_wm2 for w in request.weather],
        "dhi_wm2": [w.dhi_wm2 for w in request.weather],
        "gti_wm2": [w.gti_wm2 for w in request.weather],
        "cloud_cover_pct": [w.cloud_cover_pct for w in request.weather],
        "temp_air_c": [w.temperature_c for w in request.weather],
        "wind_speed_ms": [w.wind_speed_ms for w in request.weather],
    }
    index = pd.DatetimeIndex([w.timestamp.astimezone(UTC) for w in request.weather]).as_unit("ns")
    weather = pd.DataFrame(rows, index=index, dtype=float)
    plant = PlantSpec(
        latitude=request.plant.latitude,
        longitude=request.plant.longitude,
        capacity_kwp=request.plant.dc_capacity_kw,
        tilt=request.plant.tilt_deg,
        azimuth=request.plant.azimuth_deg,
        dc_ac_ratio=request.plant.dc_ac_ratio,
    )
    power = model.predict_power_w(build_features(weather, plant, poa_source="auto"))
    return [
        PredictionHour(
            timestamp=stamp.strftime(TIME_FORMAT),
            expected_power_w=round(float(p), 1),
            expected_energy_wh=round(float(p), 1),  # constant over a 1 h interval
        )
        for stamp, p in zip(index, power, strict=True)
    ]


@router.post(
    "/predict",
    response_model=PredictionResponse,
    response_model_by_alias=True,
    summary="Hourly production forecast of one installation",
    responses={
        422: {"description": "Invalid input (application/problem+json)"},
        503: {"description": "Model artifact could not be loaded (application/problem+json)"},
    },
)
@router.post(
    "/v1/predictions/production",
    response_model=PredictionResponse,
    response_model_by_alias=True,
    summary="Hourly production forecast of one installation (ADR #196 path)",
    responses={
        422: {"description": "Invalid input (application/problem+json)"},
        503: {"description": "Model artifact could not be loaded (application/problem+json)"},
    },
)
def predict(request: PredictionRequest, store: StoreDep, clock: ClockDep) -> PredictionResponse:
    """Predict mean AC power (W) and energy (Wh) for every submitted weather hour."""
    model = store.model()
    return PredictionResponse(
        model_version=store.version(),
        generated_at=clock().astimezone(UTC).strftime(TIME_FORMAT),
        predictions=predict_hours(request, model),
    )

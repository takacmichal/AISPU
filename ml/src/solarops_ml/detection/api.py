"""``POST /v1/detections/underproduction``: alarms for the backend ticketing (#33).

The backend sends hourly actual and predicted (expected) power of one installation and
gets underproduction alarms back. ``alarmId`` is a UUIDv5 of (installationId, start), so
repeated calls over overlapping windows return the same id for the same event.
"""

from __future__ import annotations

from typing import Literal
from uuid import UUID

import numpy as np
import pandas as pd
from fastapi import APIRouter
from pydantic import AwareDatetime, BaseModel, ConfigDict, Field, field_validator, model_validator
from pydantic.alias_generators import to_camel

from solarops_ml.detection.detector import Alarm, detect_underproduction
from solarops_ml.detection.rules import DetectionConfig

MAX_POINTS = 24 * 92  # one quarter of hourly points per request

router = APIRouter(prefix="/v1/detections", tags=["detection"])


class _Camel(BaseModel):
    model_config = ConfigDict(alias_generator=to_camel, populate_by_name=True)


class HourPoint(_Camel):
    """Hourly mean of one installation; ``timestamp`` is the end of the hour (UTC)."""

    timestamp: AwareDatetime
    actual_power_w: float | None = Field(
        description="Mean AC power in the hour; null = no data (outage, not underproduction)"
    )
    expected_power_w: float | None = Field(description="Predicted AC power for the hour")
    ghi_wm2: float | None = Field(default=None, ge=0, description="Measured/analysed GHI")
    cloud_cover_pct: float | None = Field(default=None, ge=0, le=100)
    snow_depth_cm: float | None = Field(default=None, ge=0)
    sample_coverage: float | None = Field(
        default=None, ge=0, le=1, description="Share of telemetry samples received (default 1)"
    )
    status: str | None = Field(default=None, description="Dominant inverter status (SunSpec)")


class DetectionParameters(_Camel):
    """Optional per-request override of the ADR #194 thresholds."""

    ratio_threshold: float | None = None
    min_consecutive_hours: int | None = None
    recovery_hours: int | None = None
    min_irradiance_wm2: float | None = None
    min_expected_fraction: float | None = None
    min_coverage: float | None = None
    heavy_cloud_pct: float | None = None
    cloudy_ratio_threshold: float | None = None
    snow_depth_min_cm: float | None = None
    severity_medium_ratio: float | None = None
    severity_high_ratio: float | None = None
    escalation_hours: float | None = None

    def to_config(self) -> DetectionConfig:
        return DetectionConfig(**self.model_dump(exclude_none=True))


class DetectionRequest(_Camel):
    installation_id: UUID
    rated_power_w: float = Field(gt=0, description="Nominal power used for the 5 % floor")
    ac_limit_w: float | None = Field(default=None, gt=0, description="Inverter AC limit")
    points: list[HourPoint] = Field(min_length=1, max_length=MAX_POINTS)
    parameters: DetectionParameters | None = None
    include_hours: bool = False

    @field_validator("points")
    @classmethod
    def _unique_timestamps(cls, points: list[HourPoint]) -> list[HourPoint]:
        stamps = [p.timestamp for p in points]
        if len(set(stamps)) != len(stamps):
            raise ValueError("timestamps must be unique")
        return points

    @model_validator(mode="after")
    def _valid_parameters(self) -> DetectionRequest:
        if self.parameters is not None:
            self.parameters.to_config()  # ValueError -> 422
        return self


class EvidenceHour(_Camel):
    timestamp: AwareDatetime
    actual_power_w: float
    expected_power_w: float
    ratio: float


class AlarmOut(_Camel):
    alarm_id: UUID
    type: Literal["underproduction"]
    installation_id: UUID
    start: AwareDatetime
    end: AwareDatetime | None
    detected_at: AwareDatetime
    last_underproduction_at: AwareDatetime
    ongoing: bool
    severity: Literal["low", "medium", "high"]
    ticket_priority: Literal["Low", "Medium", "High"]
    median_ratio: float
    min_ratio: float
    underproducing_hours: int
    valid_hours: int
    duration_hours: float
    expected_kwh: float
    actual_kwh: float
    estimated_lost_kwh: float
    reason_code: str
    reason: str
    evidence: list[EvidenceHour]


class HourOut(_Camel):
    timestamp: AwareDatetime
    actual_power_w: float | None
    expected_power_w: float | None
    ratio: float | None
    deviation_w: float | None
    relative_deviation: float | None
    state: Literal["underproduction", "normal", "ambiguous", "excluded"]
    excluded_reason: str | None
    reduced_sensitivity: bool


class DetectionSummary(_Camel):
    hours: int
    valid_hours: int
    underproducing_hours: int
    ambiguous_hours: int
    reduced_sensitivity_hours: int
    excluded_hours: dict[str, int]
    alarms: int


class DetectionResponse(_Camel):
    installation_id: UUID
    parameters: dict[str, float | int]
    summary: DetectionSummary
    alarms: list[AlarmOut]
    hours: list[HourOut] | None = None


def _frame(points: list[HourPoint]) -> pd.DataFrame:
    def num(value: float | None) -> float:
        return np.nan if value is None else value

    index = pd.DatetimeIndex([p.timestamp for p in points]).tz_convert("UTC")
    return pd.DataFrame(
        {
            "actual_power_w": [num(p.actual_power_w) for p in points],
            "expected_power_w": [num(p.expected_power_w) for p in points],
            "ghi_wm2": [num(p.ghi_wm2) for p in points],
            "cloud_cover_pct": [num(p.cloud_cover_pct) for p in points],
            "snow_depth_cm": [num(p.snow_depth_cm) for p in points],
            "coverage": [1.0 if p.sample_coverage is None else p.sample_coverage for p in points],
            "status": [p.status or "" for p in points],
        },
        index=index,
    )


def _opt(value: float, digits: int = 4) -> float | None:
    f = float(value)
    return None if np.isnan(f) else round(f, digits)


def _alarm_out(alarm: Alarm) -> AlarmOut:
    return AlarmOut(
        alarm_id=UUID(alarm.alarm_id),
        type="underproduction",
        installation_id=UUID(alarm.installation_id),
        start=alarm.start,
        end=alarm.end,
        detected_at=alarm.detected_at,
        last_underproduction_at=alarm.last_underproduction_at,
        ongoing=alarm.ongoing,
        severity=alarm.severity,
        ticket_priority=alarm.ticket_priority,
        median_ratio=alarm.median_ratio,
        min_ratio=alarm.min_ratio,
        underproducing_hours=alarm.underproducing_hours,
        valid_hours=alarm.valid_hours,
        duration_hours=alarm.duration_hours,
        expected_kwh=alarm.expected_kwh,
        actual_kwh=alarm.actual_kwh,
        estimated_lost_kwh=alarm.estimated_lost_kwh,
        reason_code=alarm.reason_code,
        reason=alarm.reason,
        evidence=[EvidenceHour(**e) for e in alarm.evidence],
    )


@router.post("/underproduction", response_model=DetectionResponse, response_model_by_alias=True)
def detect(request: DetectionRequest) -> DetectionResponse:
    """Detect sustained underproduction of one installation (ADR #194)."""
    config = request.parameters.to_config() if request.parameters else DetectionConfig()
    result = detect_underproduction(
        _frame(request.points),
        str(request.installation_id),
        request.rated_power_w,
        request.ac_limit_w,
        config,
    )
    hours = None
    if request.include_hours:
        hours = [
            HourOut(
                timestamp=ts.to_pydatetime(),
                actual_power_w=_opt(row["actual_power_w"], 1),
                expected_power_w=_opt(row["expected_clipped_w"], 1),
                ratio=_opt(row["ratio"]),
                deviation_w=_opt(row["deviation_w"], 1),
                relative_deviation=_opt(row["relative_deviation"]),
                state=row["state"],
                excluded_reason=row["excluded_reason"] or None,
                reduced_sensitivity=bool(row["reduced_sensitivity"]),
            )
            for ts, row in result.hours.iterrows()
        ]
    return DetectionResponse(
        installation_id=request.installation_id,
        parameters={to_camel(k): v for k, v in config.to_dict().items()},
        summary=DetectionSummary(**result.summary),
        alarms=[_alarm_out(a) for a in result.alarms],
        hours=hours,
    )

"""Sustained underproduction events and alarms (ADR #194, issue #113).

An event opens after ``min_consecutive_hours`` consecutive *valid* underproduction
hours and closes after ``recovery_hours`` consecutive valid normal hours. Excluded hours
(night, missing data, inverter status, snow) and ambiguous hours (heavy cloud) are
skipped: they neither interrupt nor extend an event, so a fault lasting several days is
one alarm, not one alarm per day.
"""

from __future__ import annotations

import uuid
from dataclasses import dataclass, field
from datetime import datetime
from typing import Any

import pandas as pd

from solarops_ml.detection.rules import (
    EXCLUSION_REASONS,
    STATE_NORMAL,
    STATE_UNDER,
    DetectionConfig,
    classify_hours,
)

ALARM_TYPE = "underproduction"
REASON_CODE = "sustained_underproduction"
TICKET_PRIORITY = {"low": "Low", "medium": "Medium", "high": "High"}
MAX_EVIDENCE = 48


@dataclass(frozen=True)
class Alarm:
    """One sustained underproduction event, ready to become a service ticket."""

    alarm_id: str
    installation_id: str
    start: datetime  # first underproduction hour
    detected_at: datetime  # hour in which the rule fired
    last_underproduction_at: datetime
    end: datetime | None  # last underproduction hour; None while ongoing
    ongoing: bool
    severity: str  # low | medium | high
    ticket_priority: str  # Low | Medium | High
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
    evidence: list[dict[str, Any]] = field(default_factory=list, repr=False)
    type: str = ALARM_TYPE


@dataclass(frozen=True)
class DetectionResult:
    alarms: list[Alarm]
    hours: pd.DataFrame  # output of classify_hours
    summary: dict[str, Any]
    config: DetectionConfig


def _alarm_id(installation_id: str, start: datetime) -> str:
    stamp = start.strftime("%Y-%m-%dT%H:%M:%SZ")
    return str(uuid.uuid5(uuid.UUID(installation_id), f"{ALARM_TYPE}|{stamp}"))


def _severity(median_ratio: float, duration_hours: float, config: DetectionConfig) -> str:
    if median_ratio < config.severity_high_ratio:
        level = 2
    elif median_ratio < config.severity_medium_ratio:
        level = 1
    else:
        level = 0
    if duration_hours >= config.escalation_hours:
        level = min(level + 1, 2)
    return ("low", "medium", "high")[level]


def _reason(median_ratio: float, hours: int, lost_kwh: float, severity: str) -> str:
    loss_pct = round((1 - median_ratio) * 100)
    return (
        f"Výroba je trvalo nižšia ako predikcia: medián {loss_pct} % pod očakávaním "
        f"počas {hours} platných hodín, odhadovaná strata {lost_kwh:.1f} kWh "
        f"(závažnosť {severity})."
    )


def _build_alarm(
    hours: pd.DataFrame,
    installation_id: str,
    start: pd.Timestamp,
    detected_at: pd.Timestamp,
    last_under: pd.Timestamp,
    ongoing: bool,
    config: DetectionConfig,
) -> Alarm:
    window = hours.loc[start:last_under]
    valid = window[window["valid"]]
    under = valid[valid["state"] == STATE_UNDER]
    ratios = valid["ratio"].astype(float)
    median_ratio = float(ratios.median())
    # Hourly means: W x 1 h = Wh. Only valid hours count (missing data is not a loss).
    expected_kwh = float(valid["expected_clipped_w"].sum()) / 1000
    actual_kwh = float(valid["actual_power_w"].sum()) / 1000
    lost_kwh = float((valid["expected_clipped_w"] - valid["actual_power_w"]).clip(lower=0).sum())
    lost_kwh /= 1000
    duration = (last_under - start).total_seconds() / 3600 + 1
    severity = _severity(median_ratio, duration, config)
    evidence = [
        {
            "timestamp": ts.to_pydatetime(),
            "actual_power_w": round(float(row["actual_power_w"]), 1),
            "expected_power_w": round(float(row["expected_clipped_w"]), 1),
            "ratio": round(float(row["ratio"]), 4),
        }
        for ts, row in under.nsmallest(MAX_EVIDENCE, "ratio").sort_index().iterrows()
    ]
    return Alarm(
        alarm_id=_alarm_id(installation_id, start.to_pydatetime()),
        installation_id=installation_id,
        start=start.to_pydatetime(),
        detected_at=detected_at.to_pydatetime(),
        last_underproduction_at=last_under.to_pydatetime(),
        end=None if ongoing else last_under.to_pydatetime(),
        ongoing=ongoing,
        severity=severity,
        ticket_priority=TICKET_PRIORITY[severity],
        median_ratio=round(median_ratio, 4),
        min_ratio=round(float(ratios.min()), 4),
        underproducing_hours=len(under),
        valid_hours=len(valid),
        duration_hours=round(duration, 1),
        expected_kwh=round(expected_kwh, 3),
        actual_kwh=round(actual_kwh, 3),
        estimated_lost_kwh=round(lost_kwh, 3),
        reason_code=REASON_CODE,
        reason=_reason(median_ratio, len(under), lost_kwh, severity),
        evidence=evidence,
    )


def detect_underproduction(
    frame: pd.DataFrame,
    installation_id: str,
    rated_power_w: float,
    ac_limit_w: float | None = None,
    config: DetectionConfig | None = None,
) -> DetectionResult:
    """Run the ADR #194 rule over an hourly series of one installation.

    ``frame`` is hourly (mean power of the hour, index = UTC end of the hour), see
    :func:`classify_hours` for the columns. ``installation_id`` must be a UUID string.
    """
    uuid.UUID(installation_id)  # raises ValueError for a non-UUID id
    config = config or DetectionConfig()
    hours = classify_hours(frame, rated_power_w, ac_limit_w, config)

    alarms: list[Alarm] = []
    run: list[pd.Timestamp] = []  # consecutive underproduction hours before opening
    event: dict[str, pd.Timestamp] | None = None
    normal_streak = 0
    for ts, state in hours["state"].items():
        if state == STATE_UNDER:
            normal_streak = 0
            if event is not None:
                event["last"] = ts
                continue
            run.append(ts)
            if len(run) >= config.min_consecutive_hours:
                event = {"start": run[0], "detected": ts, "last": ts}
                run = []
        elif state == STATE_NORMAL:
            run = []
            if event is None:
                continue
            normal_streak += 1
            if normal_streak >= config.recovery_hours:
                alarms.append(
                    _build_alarm(
                        hours,
                        installation_id,
                        event["start"],
                        event["detected"],
                        event["last"],
                        False,
                        config,
                    )
                )
                event = None
                normal_streak = 0
        # excluded / ambiguous hours are skipped
    if event is not None:
        alarms.append(
            _build_alarm(
                hours,
                installation_id,
                event["start"],
                event["detected"],
                event["last"],
                True,
                config,
            )
        )

    excluded = hours["excluded_reason"].value_counts()
    summary = {
        "hours": len(hours),
        "validHours": int(hours["valid"].sum()),
        "underproducingHours": int((hours["state"] == STATE_UNDER).sum()),
        "ambiguousHours": int((hours["state"] == "ambiguous").sum()),
        "reducedSensitivityHours": int(hours["reduced_sensitivity"].sum()),
        "excludedHours": {name: int(excluded.get(name, 0)) for name in EXCLUSION_REASONS},
        "alarms": len(alarms),
    }
    return DetectionResult(alarms=alarms, hours=hours, summary=summary, config=config)

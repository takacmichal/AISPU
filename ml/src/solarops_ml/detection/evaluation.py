"""Evaluate the underproduction rule on simulated faults (issue #118, ADR #187 and #193).

Each scenario simulates one installation (simulator #94) for ``days`` days and injects
one situation: a ``power_drop`` above or below the detection threshold, an
``inverter_fault``, a healthy control, a telemetry outage (dropped samples) or snow on
the panels (actual 0 W with a snow flag; the simulator has no snow, so the harness adds
it). The "predicted" power is the simulator's fault-free output with a separately seeded,
autocorrelated model error that grows with cloud cover (target nMAE ~6 %, ADR #193), so
the detector is not scored against the exact injector output (circular truth, ADR #187).

Alarms are matched to labels as in ADR #193 (TP / DUP / LATE / FP, tolerance
``<start - 2 h; start + 24 h>``). Results on synthetic faults are an optimistic upper
bound; the real DKASC faults stay the held-out evaluation (ADR #187).
"""

from __future__ import annotations

import json
import math
import statistics
import uuid
from collections.abc import Iterable, Sequence
from dataclasses import asdict, dataclass, field, replace
from datetime import UTC, datetime, timedelta
from pathlib import Path
from typing import Any

import numpy as np
import pandas as pd

from solarops_ml.detection.detector import detect_underproduction
from solarops_ml.detection.hourly import hourly_from_readings
from solarops_ml.detection.rules import DetectionConfig
from solarops_ml.simulator.generator import FaultSpec, SimulatorConfig, simulate

EARLY_TOLERANCE = timedelta(hours=2)
LATE_TOLERANCE = timedelta(hours=24)
# Ground-truth definition (ADR #187): fixed across experiments, independent of the grid.
LABEL_RATIO = 0.85
LABEL_MIN_SEVERITY = 1 - LABEL_RATIO
MIN_OBSERVABLE_HOURS = 2
MIN_IRRADIANCE_WM2 = 50.0
MIN_EXPECTED_FRACTION = 0.05
FAR_FUTURE = datetime(9999, 1, 1, tzinfo=UTC)
NAMESPACE = uuid.UUID("5c1f0a3e-8a59-4d8e-9f53-0e7a3b2d9c41")

SCENARIO_PATTERN = (
    "power_drop",
    "power_drop",
    "power_drop",
    "power_drop",
    "power_drop_small",
    "inverter_fault",
    "healthy",
    "healthy",
    "data_outage",
    "snow",
)
FAULT_KINDS = frozenset({"power_drop", "power_drop_small", "inverter_fault"})

# name: (latitude, longitude, tilt, azimuth, utc_offset_hours); see config/locations.toml
LOCATIONS = {
    "alice_springs": (-23.76, 133.87, 20.0, 180.0, 9.5),
    "kosice": (48.72, 21.26, 30.0, 0.0, 1.0),
}


@dataclass(frozen=True)
class Span:
    """An alarm or a labelled fault; ``end = None`` means still open."""

    start: datetime
    end: datetime | None
    detected_at: datetime | None = None

    @property
    def stop(self) -> datetime:
        return self.end or FAR_FUTURE

    def overlaps(self, other: Span) -> bool:
        return self.start <= other.stop and other.start <= self.stop


@dataclass
class MatchResult:
    classes: list[str] = field(default_factory=list)  # one per alarm, sorted by start
    n_labels: int = 0
    detected_labels: int = 0
    relaxed_detected_labels: int = 0
    delays_hours: list[float] = field(default_factory=list)  # TP and LATE

    def count(self, cls: str) -> int:
        return self.classes.count(cls)


def match_events(
    alarms: Sequence[Span],
    labels: Sequence[Span],
    ignored: Sequence[Span] = (),
    early: timedelta = EARLY_TOLERANCE,
    late: timedelta = LATE_TOLERANCE,
) -> MatchResult:
    """Classify alarms against labelled events as in ADR #193.

    TP: first alarm overlapping a label with ``start`` in ``<label - early; label + late>``;
    DUP: another alarm on a label that already has a TP; LATE: overlapping alarm starting
    after ``label + late`` while the label has no TP; FP: everything else (also premature
    alarms). Alarms overlapping only ``ignored`` spans (below-threshold faults, inverter
    faults) are ``IGNORED`` and count neither way.
    """
    result = MatchResult(n_labels=len(labels))
    has_tp = [False] * len(labels)
    has_late = [False] * len(labels)
    for alarm in sorted(alarms, key=lambda a: a.start):
        detected = alarm.detected_at or alarm.start
        hits = [i for i, label in enumerate(labels) if alarm.overlaps(label)]
        if not hits:
            result.classes.append("IGNORED" if any(alarm.overlaps(s) for s in ignored) else "FP")
            continue
        tp = next(
            (
                i
                for i in hits
                if not has_tp[i]
                and labels[i].start - early <= alarm.start <= labels[i].start + late
            ),
            None,
        )
        if tp is not None:
            has_tp[tp] = True
            result.classes.append("TP")
            result.delays_hours.append((detected - labels[tp].start).total_seconds() / 3600)
            continue
        first = hits[0]
        if has_tp[first]:
            result.classes.append("DUP")
        elif alarm.start > labels[first].start + late:
            result.classes.append("LATE")
            if not has_late[first]:
                has_late[first] = True
                result.delays_hours.append((detected - labels[first].start).total_seconds() / 3600)
        else:
            result.classes.append("FP")
    result.detected_labels = sum(has_tp)
    result.relaxed_detected_labels = sum(a or b for a, b in zip(has_tp, has_late, strict=True))
    return result


@dataclass(frozen=True)
class EvaluationConfig:
    n_scenarios: int = 40
    days: int = 14
    seed: int = 42
    capacity_kwp: float = 10.0
    model_error_sigma: float = 0.04  # base relative error of the predicted power
    model_error_cloud_sigma: float = 0.10  # extra error at 100 % cloud cover
    model_error_phi: float = 0.8  # hourly autocorrelation of the error
    model_bias_sigma: float = 0.02  # per-installation bias
    detection: DetectionConfig = field(default_factory=DetectionConfig)
    grid_thresholds: tuple[float, ...] = (0.75, 0.80, 0.85, 0.90)
    grid_hours: tuple[int, ...] = (1, 2, 3, 4)

    def __post_init__(self) -> None:
        if self.n_scenarios < 1:
            raise ValueError("n_scenarios must be >= 1")
        if self.days < 4:
            raise ValueError("days must be >= 4")


@dataclass(frozen=True)
class Scenario:
    id: int
    kind: str
    location: str
    start: datetime
    days: int
    cloudiness: float
    seed: int
    fault: FaultSpec | None = None
    outage: tuple[datetime, datetime] | None = None
    snow: tuple[datetime, datetime] | None = None

    @property
    def end(self) -> datetime:
        return self.start + timedelta(days=self.days)

    @property
    def device_id(self) -> str:
        return str(uuid.uuid5(NAMESPACE, f"scenario-{self.seed}-{self.id}"))


def build_scenarios(config: EvaluationConfig) -> list[Scenario]:
    """Deterministic scenario set (same ``seed`` -> same scenarios)."""
    rng = np.random.default_rng(config.seed)
    scenarios = []
    for i in range(config.n_scenarios):
        kind = SCENARIO_PATTERN[i % len(SCENARIO_PATTERN)]
        location = "alice_springs" if i % 2 == 0 else "kosice"
        if location == "kosice":  # spring to autumn; winter days are mostly below 5 % P_rated
            start = datetime(2024, 4, 1, tzinfo=UTC) + timedelta(days=int(rng.integers(0, 150)))
        else:
            start = datetime(2024, 1, 1, tzinfo=UTC) + timedelta(days=int(rng.integers(0, 330)))
        cloudiness = round(float(rng.uniform(0.05, 0.55)), 2)
        event_start = start + timedelta(
            days=int(rng.integers(1, config.days - 2)), hours=int(rng.integers(0, 24))
        )
        latest_end = start + timedelta(days=config.days) - timedelta(hours=1)

        def window(
            hours: int, begin: datetime = event_start, limit: datetime = latest_end
        ) -> tuple[datetime, datetime]:
            return begin, min(begin + timedelta(hours=hours), limit)

        fault = outage = snow = None
        if kind == "power_drop":
            severity = float(rng.choice([0.2, 0.3, 0.4, 0.5, 0.7, 1.0]))
            fault = FaultSpec("power_drop", *window(int(rng.choice([6, 12, 24, 48, 96]))), severity)
        elif kind == "power_drop_small":
            severity = round(float(rng.uniform(0.05, 0.12)), 3)
            fault = FaultSpec("power_drop", *window(int(rng.choice([24, 48, 96]))), severity)
        elif kind == "inverter_fault":
            fault = FaultSpec("inverter_fault", *window(int(rng.integers(1, 49))), 1.0)
        elif kind == "data_outage":
            outage = window(int(rng.integers(6, 49)))
        elif kind == "snow":
            snow = window(int(rng.choice([24, 48, 72])))
        scenarios.append(
            Scenario(
                id=i,
                kind=kind,
                location=location,
                start=start,
                days=config.days,
                cloudiness=cloudiness,
                seed=config.seed * 1000 + i,
                fault=fault,
                outage=outage,
                snow=snow,
            )
        )
    return scenarios


@dataclass
class PreparedScenario:
    scenario: Scenario
    hourly: pd.DataFrame  # actual, predicted (expected_power_w), oracle, weather, status
    rated_power_w: float
    labels: list[Span]
    ignored: list[Span]
    label_status: str | None
    prediction_nmae: float


def _model_error(hourly: pd.DataFrame, config: EvaluationConfig, seed: int) -> np.ndarray:
    rng = np.random.default_rng(seed)  # independent of the simulator's random stream
    n = len(hourly)
    cloud = hourly.get("cloud_cover_pct", pd.Series(0.0, index=hourly.index)).fillna(0) / 100
    sigma = config.model_error_sigma + config.model_error_cloud_sigma * cloud.to_numpy()
    z = np.empty(n)
    prev = rng.standard_normal()
    innovation_scale = math.sqrt(1 - config.model_error_phi**2)
    for i, eps in enumerate(rng.standard_normal(n)):
        prev = config.model_error_phi * prev + innovation_scale * eps
        z[i] = prev
    bias = config.model_bias_sigma * rng.standard_normal()
    return bias + sigma * z


def _hours_in(index: pd.DatetimeIndex, start: datetime, end: datetime) -> np.ndarray:
    # Hour labelled by its end: the hour ending at ``t`` covers ``(t - 1 h, t]``.
    return np.asarray((index > start) & (index < end + timedelta(hours=1)))


def prepare(scenario: Scenario, config: EvaluationConfig) -> PreparedScenario:
    lat, lon, tilt, azimuth, utc_offset = LOCATIONS[scenario.location]
    sim_config = SimulatorConfig(
        device_id=scenario.device_id,
        latitude=lat,
        longitude=lon,
        tilt=tilt,
        azimuth=azimuth,
        capacity_kwp=config.capacity_kwp,
        utc_offset_hours=utc_offset,
        cloudiness=scenario.cloudiness,
        seed=scenario.seed,
    )
    faults = [scenario.fault] if scenario.fault else []
    sim = simulate(sim_config, scenario.start, scenario.end, faults)
    readings = sim.readings.copy()
    if scenario.outage:
        start, end = scenario.outage
        lost = (readings.index >= start) & (readings.index < end)
        readings.loc[lost, ["ac_power_w", "status"]] = np.nan
    hourly = hourly_from_readings(readings, expected_power_w=sim.labels["expected_power_w"])
    oracle = hourly["expected_power_w"].copy()
    hourly["oracle_power_w"] = oracle
    error = _model_error(hourly, config, seed=10_000_000 + scenario.seed)
    hourly["expected_power_w"] = (oracle * (1 + error)).clip(lower=0)
    hourly["snow_depth_cm"] = np.nan
    if scenario.snow:
        covered = _hours_in(hourly.index, *scenario.snow)
        hourly.loc[covered, "snow_depth_cm"] = 10.0
        hourly.loc[covered & (hourly["actual_power_w"] > 0).to_numpy(), "actual_power_w"] = 0.0

    rated = config.capacity_kwp * 1000
    daylight = (oracle >= MIN_EXPECTED_FRACTION * rated) & (hourly["ghi_wm2"] > MIN_IRRADIANCE_WM2)
    nmae = float(
        (hourly["expected_power_w"] - oracle).abs()[daylight].mean() / rated
        if daylight.any()
        else 0.0
    )

    labels: list[Span] = []
    ignored: list[Span] = []
    status = None
    fault = scenario.fault
    if fault is not None:
        spanned = Span(fault.start, fault.end)
        observable = int((daylight & _hours_in(hourly.index, fault.start, fault.end)).sum())
        if fault.type == "inverter_fault":
            status = "inverter_fault"
            ignored.append(spanned)
        elif fault.severity < LABEL_MIN_SEVERITY:
            status = "below_threshold"
            ignored.append(spanned)
        elif observable < MIN_OBSERVABLE_HOURS:
            status = "unobservable"
            ignored.append(spanned)
        else:
            status = "positive"
            labels.append(spanned)
    return PreparedScenario(scenario, hourly, rated, labels, ignored, status, nmae)


def _naive(hourly: pd.DataFrame) -> pd.DataFrame:
    """Rule without the #114 filters: gaps are 0 W, no status, cloud or snow handling."""
    out = hourly.drop(columns=["status", "cloud_cover_pct", "snow_depth_cm"], errors="ignore")
    out["actual_power_w"] = out["actual_power_w"].fillna(0.0)
    out["coverage"] = 1.0
    return out


def _detect(prepared: PreparedScenario, detection: DetectionConfig, naive: bool) -> list[Span]:
    frame = _naive(prepared.hourly) if naive else prepared.hourly
    result = detect_underproduction(
        frame, prepared.scenario.device_id, prepared.rated_power_w, config=detection
    )
    return [Span(a.start, a.end, a.detected_at) for a in result.alarms]


def _ratio(num: float, den: float) -> float:
    return num / den if den else 0.0


def _metrics(matches: Iterable[MatchResult], installation_days: float) -> dict[str, Any]:
    matches = list(matches)
    tp, dup, late, fp, ign = (
        sum(m.count(c) for m in matches) for c in ("TP", "DUP", "LATE", "FP", "IGNORED")
    )
    n_labels = sum(m.n_labels for m in matches)
    detected = sum(m.detected_labels for m in matches)
    relaxed = sum(m.relaxed_detected_labels for m in matches)
    alarms = tp + dup + late + fp
    precision, recall = _ratio(tp, alarms), _ratio(detected, n_labels)
    r_precision, r_recall = _ratio(tp + late, alarms), _ratio(relaxed, n_labels)
    delays = [d for m in matches for d in m.delays_hours]

    def f1(p: float, r: float) -> float:
        return _ratio(2 * p * r, p + r)

    def f05(p: float, r: float) -> float:
        return _ratio(1.25 * p * r, 0.25 * p + r)

    return {
        "primary": {
            "precision": round(precision, 4),
            "recall": round(recall, 4),
            "f1": round(f1(precision, recall), 4),
            "f05": round(f05(precision, recall), 4),
            "tp": tp,
            "dup": dup,
            "late": late,
            "fp": fp,
            "fn": n_labels - detected,
            "ignored": ign,
            "labels": n_labels,
        },
        "relaxed": {
            "precision": round(r_precision, 4),
            "recall": round(r_recall, 4),
            "f1": round(f1(r_precision, r_recall), 4),
        },
        "medianDelayHours": round(statistics.median(delays), 1) if delays else None,
        "alertsPerEvent": round(_ratio(tp + dup + late, relaxed), 2) if relaxed else None,
        "falseAlarmsPer30Days": round(_ratio(fp + dup, installation_days / 30), 3),
    }


def _bucket(p: PreparedScenario) -> str:
    fault = p.scenario.fault
    if p.label_status != "positive" or fault is None:
        return p.label_status or p.scenario.kind
    hours = (fault.end - fault.start).total_seconds() / 3600
    loss = "loss 15-35 %" if fault.severity < 0.35 else "loss 35-65 %"
    if fault.severity >= 0.65:
        loss = "loss >= 65 %"
    return f"power_drop, {loss}, {'< 24 h' if hours < 24 else '>= 24 h'}"


def run_evaluation(config: EvaluationConfig | None = None) -> dict[str, Any]:
    """Simulate every scenario, detect, match and aggregate (deterministic for a seed)."""
    config = config or EvaluationConfig()
    prepared = [prepare(s, config) for s in build_scenarios(config)]
    installation_days = config.n_scenarios * config.days

    def evaluate(detection: DetectionConfig, naive: bool = False) -> list[MatchResult]:
        return [match_events(_detect(p, detection, naive), p.labels, p.ignored) for p in prepared]

    matches = evaluate(config.detection)

    by_type: dict[str, dict[str, Any]] = {}
    for p, m in zip(prepared, matches, strict=True):
        row = by_type.setdefault(
            _bucket(p),
            {"scenarios": 0, "labels": 0, "detected": 0, "late": 0, "alarms": 0, "falseAlarms": 0},
        )
        row["scenarios"] += 1
        row["labels"] += m.n_labels
        row["detected"] += m.detected_labels
        row["late"] += m.relaxed_detected_labels - m.detected_labels
        row["alarms"] += len(m.classes)
        row["falseAlarms"] += m.count("FP") + m.count("DUP")
    for row in by_type.values():
        row["recall"] = round(_ratio(row["detected"], row["labels"]), 4) if row["labels"] else None

    grid = []
    for threshold in config.grid_thresholds:
        for hours in config.grid_hours:
            detection = replace(
                config.detection,
                ratio_threshold=threshold,
                min_consecutive_hours=hours,
                cloudy_ratio_threshold=min(config.detection.cloudy_ratio_threshold, threshold),
                severity_medium_ratio=min(config.detection.severity_medium_ratio, threshold),
                severity_high_ratio=min(config.detection.severity_high_ratio, threshold),
            )
            cell = _metrics(evaluate(detection), installation_days)
            grid.append(
                {
                    "ratioThreshold": threshold,
                    "minConsecutiveHours": hours,
                    **cell["primary"],
                    "falseAlarmsPer30Days": cell["falseAlarmsPer30Days"],
                    "medianDelayHours": cell["medianDelayHours"],
                }
            )

    nmae = [p.prediction_nmae for p in prepared]
    return {
        "schema": "solarops-detection-eval-1",
        "issue": 118,
        "config": {
            **{k: v for k, v in asdict(config).items() if k != "detection"},
            "detection": config.detection.to_dict(),
            "labelMinSeverity": round(LABEL_MIN_SEVERITY, 4),
            "earlyToleranceHours": EARLY_TOLERANCE.total_seconds() / 3600,
            "lateToleranceHours": LATE_TOLERANCE.total_seconds() / 3600,
        },
        "predictionNmae": {"mean": round(statistics.fmean(nmae), 4), "max": round(max(nmae), 4)},
        "metrics": _metrics(matches, installation_days),
        "byType": dict(sorted(by_type.items())),
        "ablation": {
            "rule": _metrics(matches, installation_days),
            "naive": _metrics(evaluate(config.detection, naive=True), installation_days),
        },
        "grid": grid,
        "scenarios": [
            {
                "id": p.scenario.id,
                "kind": p.scenario.kind,
                "location": p.scenario.location,
                "start": _iso(p.scenario.start),
                "days": p.scenario.days,
                "cloudiness": p.scenario.cloudiness,
                "fault": None
                if p.scenario.fault is None
                else {
                    "type": p.scenario.fault.type,
                    "start": _iso(p.scenario.fault.start),
                    "end": _iso(p.scenario.fault.end),
                    "severity": p.scenario.fault.severity,
                },
                "outage": _span_json(p.scenario.outage),
                "snow": _span_json(p.scenario.snow),
                "labelStatus": p.label_status,
                "alarmClasses": m.classes,
                "predictionNmae": round(p.prediction_nmae, 4),
            }
            for p, m in zip(prepared, matches, strict=True)
        ],
    }


def _iso(value: datetime) -> str:
    return value.astimezone(UTC).strftime("%Y-%m-%dT%H:%M:%SZ")


def _span_json(span: tuple[datetime, datetime] | None) -> dict[str, str] | None:
    return None if span is None else {"start": _iso(span[0]), "end": _iso(span[1])}


def _fmt(value: Any) -> str:
    if value is None:
        return "–"
    if isinstance(value, float):
        return f"{value:.2f}"
    return str(value)


def to_markdown(report: dict[str, Any]) -> str:
    cfg = report["config"]
    m = report["metrics"]
    p = m["primary"]
    lines = [
        "# Vyhodnotenie detekcie podvýroby na simulovaných poruchách (#118)",
        "",
        f"Scenáre: {cfg['n_scenarios']} × {cfg['days']} dní, seed {cfg['seed']}. "
        f"Pravidlo ADR #194 (prah {cfg['detection']['ratio_threshold']}, "
        f">= {cfg['detection']['min_consecutive_hours']} h). "
        f"Chyba predikcie (nMAE voči bezporuchovému výkonu): priemer "
        f"{report['predictionNmae']['mean']:.1%}, max {report['predictionNmae']['max']:.1%}.",
        "",
        "Syntetické poruchy sú optimistická horná hranica (ADR #187); hlavné hodnotenie "
        "zostáva na reálnych poruchách DKASC.",
        "",
        "## Hlavné metriky (udalosti, ADR #193)",
        "",
        "| Metrika | Primárna | Overlap-only |",
        "|---|---|---|",
        f"| Precision | {_fmt(p['precision'])} | {_fmt(m['relaxed']['precision'])} |",
        f"| Recall | {_fmt(p['recall'])} | {_fmt(m['relaxed']['recall'])} |",
        f"| F1 | {_fmt(p['f1'])} | {_fmt(m['relaxed']['f1'])} |",
        "",
        f"TP {p['tp']}, DUP {p['dup']}, LATE {p['late']}, FP {p['fp']}, FN {p['fn']} "
        f"(labelov {p['labels']}, ignorovaných alarmov {p['ignored']}). "
        f"Medián oneskorenia {_fmt(m['medianDelayHours'])} h, alerts-per-event "
        f"{_fmt(m['alertsPerEvent'])}, falošné poplachy (FP + DUP) na inštaláciu za 30 dní "
        f"{_fmt(m['falseAlarmsPer30Days'])}.",
        "",
        "## Podľa typu scenára",
        "",
        "| Typ | Scenáre | Labely | Detegované | Neskoro | Recall | Alarmy | Falošné |",
        "|---|---|---|---|---|---|---|---|",
    ]
    for name, row in report["byType"].items():
        lines.append(
            f"| {name} | {row['scenarios']} | {row['labels']} | {row['detected']} | "
            f"{row['late']} | {_fmt(row['recall'])} | {row['alarms']} | {row['falseAlarms']} |"
        )
    rule, naive = report["ablation"]["rule"], report["ablation"]["naive"]
    lines += [
        "",
        "`below_threshold` = strata < 15 % (pod prahom detekcie, nepočíta sa ako FN), "
        "`unobservable` = porucha bez 2 denných hodín, `inverter_fault` = rieši alert stavu "
        "striedača (hodiny sú neplatné).",
        "",
        "## Falošné poplachy s pravidlami #114 a bez nich",
        "",
        "| Variant | Precision | Recall | FP | DUP | Falošné / 30 dní |",
        "|---|---|---|---|---|---|",
        f"| Pravidlo ADR #194 + filtre #114 | {_fmt(rule['primary']['precision'])} | "
        f"{_fmt(rule['primary']['recall'])} | {rule['primary']['fp']} | "
        f"{rule['primary']['dup']} | {_fmt(rule['falseAlarmsPer30Days'])} |",
        f"| Naivné (výpadok = 0 W, bez stavu, oblačnosti, snehu) | "
        f"{_fmt(naive['primary']['precision'])} | {_fmt(naive['primary']['recall'])} | "
        f"{naive['primary']['fp']} | {naive['primary']['dup']} | "
        f"{_fmt(naive['falseAlarmsPer30Days'])} |",
        "",
        "## Mriežka prah × trvanie (ADR #194)",
        "",
        "| Prah r | Hodiny | Precision | Recall | F1 | F0,5 | Falošné / 30 dní | Oneskorenie (h) |",
        "|---|---|---|---|---|---|---|---|",
    ]
    for cell in report["grid"]:
        lines.append(
            f"| {cell['ratioThreshold']:.2f} | {cell['minConsecutiveHours']} | "
            f"{_fmt(cell['precision'])} | {_fmt(cell['recall'])} | {_fmt(cell['f1'])} | "
            f"{_fmt(cell['f05'])} | {_fmt(cell['falseAlarmsPer30Days'])} | "
            f"{_fmt(cell['medianDelayHours'])} |"
        )
    lines.append("")
    return "\n".join(lines)


def write_report(report: dict[str, Any], json_path: Path, markdown_path: Path) -> None:
    json_path.parent.mkdir(parents=True, exist_ok=True)
    markdown_path.parent.mkdir(parents=True, exist_ok=True)
    json_path.write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    markdown_path.write_text(to_markdown(report), encoding="utf-8")

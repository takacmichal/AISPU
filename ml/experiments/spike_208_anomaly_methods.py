"""Spike #208: rule on residuals vs. robust z-score vs. Isolation Forest.

All methods share the hour validation of ``classify_hours`` (ADR #194) and the event matching of
ADR #193 on the simulated faults of ``detection.evaluation``; only the decision rule differs.

Run: ``uv run --group experiment python experiments/spike_208_anomaly_methods.py``
"""

from __future__ import annotations

import json
import time
from collections.abc import Callable
from pathlib import Path

import numpy as np
import pandas as pd
from sklearn.ensemble import IsolationForest

from solarops_ml.detection.evaluation import (
    EvaluationConfig,
    PreparedScenario,
    Span,
    _detect,
    _metrics,
    build_scenarios,
    match_events,
    prepare,
)
from solarops_ml.detection.rules import classify_hours

SEED = 42
MIN_RUN = 2  # consecutive flagged valid hours that open an event (same as the rule)
RECOVERY = 2  # consecutive normal valid hours that close it (same as the rule)


def events_from_flags(valid: pd.Series, flag: pd.Series) -> list[Span]:
    """Turn per-hour flags into events with the same open/close logic as the rule."""
    spans: list[Span] = []
    run: list[pd.Timestamp] = []
    start = detected = last = None
    normal = 0
    for ts in valid.index:
        if not valid[ts]:
            continue  # excluded hours neither open nor close an event
        if flag[ts]:
            normal = 0
            if start is not None:
                last = ts
                continue
            run.append(ts)
            if len(run) >= MIN_RUN:
                start, detected, last = run[0], ts, ts
                run = []
        else:
            run = []
            if start is not None:
                normal += 1
                if normal >= RECOVERY:
                    spans.append(Span(start, last, detected))
                    start = None
                    normal = 0
    if start is not None:
        spans.append(Span(start, None, detected))
    return spans


def zscore_flags(hours: pd.DataFrame, z_threshold: float) -> pd.Series:
    """Robust z-score (median/MAD) of the ratio over the installation's own valid hours."""
    ratio = hours["ratio"].where(hours["valid"])
    median = ratio.median()
    mad = (ratio - median).abs().median() * 1.4826
    z = (ratio - median) / (mad if mad > 0 else np.nan)
    return (z < -z_threshold).fillna(False)


def iforest_flags(hours: pd.DataFrame, rated_w: float, contamination: float) -> pd.Series:
    """Isolation Forest on [ratio, actual/rated, hour sin/cos]; only the low side counts."""
    valid = hours["valid"]
    flags = pd.Series(False, index=hours.index)
    if valid.sum() < 20:
        return flags
    hour = hours.index.hour.to_numpy()
    features = pd.DataFrame(
        {
            "ratio": hours["ratio"],
            "actual": hours["actual_power_w"] / rated_w,
            "sin": np.sin(2 * np.pi * hour / 24),
            "cos": np.cos(2 * np.pi * hour / 24),
        },
        index=hours.index,
    )[valid]
    model = IsolationForest(contamination=contamination, random_state=SEED, n_estimators=200)
    outlier = pd.Series(model.fit_predict(features) == -1, index=features.index)
    median_ratio = features["ratio"].median()
    flags.loc[outlier.index] = outlier & (features["ratio"] < median_ratio)
    return flags


def evaluate(
    prepared: list[PreparedScenario], method: Callable[[PreparedScenario], list[Span]], days: float
) -> dict:
    started = time.perf_counter()
    matches = [match_events(method(p), p.labels, p.ignored) for p in prepared]
    elapsed = time.perf_counter() - started
    result = _metrics(matches, days)
    result["seconds"] = round(elapsed, 2)
    return result


def run() -> dict:
    config = EvaluationConfig()
    prepared = [prepare(s, config) for s in build_scenarios(config)]
    days = config.n_scenarios * config.days

    def classified(p: PreparedScenario) -> pd.DataFrame:
        return classify_hours(p.hourly, p.rated_power_w, config=config.detection)

    def rule(p: PreparedScenario) -> list[Span]:
        return _detect(p, config.detection, naive=False)

    def zscore(z: float) -> Callable[[PreparedScenario], list[Span]]:
        def method(p: PreparedScenario) -> list[Span]:
            hours = classified(p)
            return events_from_flags(hours["valid"], zscore_flags(hours, z))

        return method

    def forest(c: float) -> Callable[[PreparedScenario], list[Span]]:
        def method(p: PreparedScenario) -> list[Span]:
            hours = classified(p)
            return events_from_flags(hours["valid"], iforest_flags(hours, p.rated_power_w, c))

        return method

    rows = {"rule (ADR #194, ratio < 0.85, >= 2 h)": evaluate(prepared, rule, days)}
    for z in (2.0, 3.0, 4.0):
        rows[f"robust z-score < -{z:g}"] = evaluate(prepared, zscore(z), days)
    for c in (0.02, 0.05, 0.10):
        rows[f"Isolation Forest, contamination {c:g}"] = evaluate(prepared, forest(c), days)
    return {
        "schema": "solarops-spike-208-1",
        "scenarios": config.n_scenarios,
        "days": config.days,
        "seed": config.seed,
        "methods": rows,
    }


def to_markdown(report: dict) -> str:
    lines = [
        "| Metóda | Precision | Recall | F1 | TP | FP | FN | Falošné / 30 dní "
        "| Medián oneskorenia (h) | Čas (s) |",
        "|---|---|---|---|---|---|---|---|---|---|",
    ]
    for name, m in report["methods"].items():
        p = m["primary"]
        lines.append(
            f"| {name} | {p['precision']:.2f} | {p['recall']:.2f} | {p['f1']:.2f} | {p['tp']} | "
            f"{p['fp']} | {p['fn']} | {m['falseAlarmsPer30Days']:.2f} | "
            f"{m['medianDelayHours']} | {m['seconds']} |"
        )
    return "\n".join(lines)


if __name__ == "__main__":
    report = run()
    out = Path(__file__).resolve().parents[1] / "reports"
    (out / "spike-208-comparison.json").write_text(json.dumps(report, indent=2) + "\n")
    table = to_markdown(report)
    (out / "spike-208-comparison.md").write_text(table + "\n")
    print(table)

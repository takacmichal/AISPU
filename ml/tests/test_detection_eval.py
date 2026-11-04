"""Evaluation of the detector on simulated faults (#118) and ADR #193 alarm matching."""

from __future__ import annotations

import json
from datetime import UTC, datetime, timedelta

import pytest

from solarops_ml.detection.evaluation import (
    EvaluationConfig,
    Span,
    build_scenarios,
    match_events,
    run_evaluation,
    write_report,
)

T0 = datetime(2024, 6, 1, tzinfo=UTC)


def h(hours: float) -> datetime:
    return T0 + timedelta(hours=hours)


def span(start: float, end: float | None, detected: float | None = None) -> Span:
    return Span(h(start), None if end is None else h(end), h(detected if detected else start))


# --- matching (ADR #193) -------------------------------------------------------


def test_first_alarm_in_tolerance_is_tp_and_next_is_dup() -> None:
    labels = [span(10, 60)]
    alarms = [span(11, 15, 12), span(30, 40)]
    m = match_events(alarms, labels)
    assert m.classes == ["TP", "DUP"]
    assert m.detected_labels == 1
    assert m.delays_hours == [2.0]


def test_alarm_starting_more_than_24h_after_label_is_late() -> None:
    m = match_events([span(40, 50)], [span(10, 60)])
    assert m.classes == ["LATE"]
    assert m.detected_labels == 0
    assert m.relaxed_detected_labels == 1


def test_premature_and_non_overlapping_alarms_are_fp() -> None:
    m = match_events([span(5, 12), span(100, 110)], [span(10, 60)])
    assert m.classes == ["FP", "FP"]
    assert m.detected_labels == 0


def test_ongoing_alarm_overlaps_until_end_of_series() -> None:
    m = match_events([span(12, None)], [span(10, 20)])
    assert m.classes == ["TP"]


def test_alarm_on_ignored_label_is_not_counted() -> None:
    m = match_events([span(12, 20)], [], ignored=[span(10, 30)])
    assert m.classes == ["IGNORED"]


# --- scenario set and report -------------------------------------------------------


def test_default_scenario_set_has_at_least_20_fault_scenarios() -> None:
    scenarios = build_scenarios(EvaluationConfig())
    faults = [
        s for s in scenarios if s.kind in {"power_drop", "power_drop_small", "inverter_fault"}
    ]
    assert len(faults) >= 20
    assert {s.kind for s in scenarios} >= {"healthy", "data_outage", "snow"}


@pytest.fixture(scope="module")
def small_report() -> dict:
    config = EvaluationConfig(
        n_scenarios=10, days=6, seed=7, grid_thresholds=(0.85,), grid_hours=(2,)
    )
    return run_evaluation(config)


def test_evaluation_reports_metrics(small_report: dict) -> None:
    primary = small_report["metrics"]["primary"]
    for key in ("precision", "recall", "f1", "tp", "fp", "dup", "late", "fn"):
        assert key in primary
    assert 0.0 <= primary["precision"] <= 1.0
    assert primary["recall"] > 0.0
    assert "falseAlarmsPer30Days" in small_report["metrics"]
    assert small_report["byType"]
    assert small_report["grid"][0]["ratioThreshold"] == 0.85
    assert "naive" in small_report["ablation"]
    assert len(small_report["scenarios"]) == 10


def test_evaluation_is_deterministic(small_report: dict, tmp_path) -> None:
    config = EvaluationConfig(
        n_scenarios=10, days=6, seed=7, grid_thresholds=(0.85,), grid_hours=(2,)
    )
    again = run_evaluation(config)
    assert json.dumps(small_report, sort_keys=True) == json.dumps(again, sort_keys=True)
    json_path, md_path = tmp_path / "r.json", tmp_path / "r.md"
    write_report(again, json_path=json_path, markdown_path=md_path)
    assert json.loads(json_path.read_text())["metrics"] == small_report["metrics"]
    text = md_path.read_text()
    assert "Precision" in text and "Recall" in text

"""Run the #191 baseline vs. gradient boosting vs. LSTM comparison."""

from __future__ import annotations

import json
import os
from dataclasses import asdict, dataclass, replace
from datetime import UTC, datetime
from pathlib import Path
from typing import Any

import numpy as np
import pandas as pd

from solarops_ml.forecast.data import (
    DEFAULT_ARRAYS,
    FEATURES,
    ArraySpec,
    Fold,
    build_dataset,
    default_folds,
    evaluation_mask,
)
from solarops_ml.forecast.metrics import regression_metrics, skill_score
from solarops_ml.forecast.models import fit_predict

# LightGBM and PyTorch each bundle an OpenMP runtime; mixing them in one process can
# deadlock or segfault on macOS. One thread keeps the comparison stable and deterministic.
os.environ.setdefault("OMP_NUM_THREADS", "1")

MODES = ("walk_forward", "leave_one_array_out")


@dataclass(frozen=True)
class ExperimentConfig:
    start: pd.Timestamp = pd.Timestamp("2023-01-01T00:00Z")
    end: pd.Timestamp = pd.Timestamp("2025-01-01T00:00Z")
    arrays: tuple[ArraySpec, ...] = DEFAULT_ARRAYS
    seed: int = 42
    folds: tuple[Fold, ...] | None = None
    include_lstm: bool = True
    include_loao: bool = True
    lstm_window: int = 24
    lstm_hidden: int = 32
    lstm_epochs: int = 20


def run_experiment(config: ExperimentConfig | None = None) -> dict[str, Any]:
    """Generate the synthetic dataset, train all models and return a JSON-ready report."""
    config = config or ExperimentConfig()
    frames = build_dataset(config.start, config.end, config.arrays, seed=config.seed)
    folds = config.folds or default_folds(config.start, config.end)
    models = ["persistence", "physical", "linear", "lightgbm"]
    if config.include_lstm:
        models.append("lstm")

    details: list[dict[str, Any]] = []
    modes = ["walk_forward"]
    if config.include_loao:
        modes.append("leave_one_array_out")
    arrays = sorted(frames["array_id"].unique())
    for mode in modes:
        for fold in folds:
            groups: list[str | None] = [None] if mode == "walk_forward" else arrays
            for group in groups:
                train_mask = (frames.index >= fold.train_start) & (frames.index < fold.train_end)
                test_mask = (frames.index >= fold.test_start) & (frames.index < fold.test_end)
                if group is not None:
                    train_mask &= frames["array_id"] != group
                    test_mask &= frames["array_id"] == group
                train = frames.loc[train_mask]
                test = frames.loc[test_mask]
                history = frames.loc[frames.index < fold.test_end]
                if train.empty or test.empty:
                    continue
                mask = (
                    evaluation_mask(test, fold).to_numpy()
                    if not test.empty
                    else np.zeros(0, dtype=bool)
                )
                for model in models:
                    prediction = fit_predict(
                        model,
                        train,
                        test,
                        history,
                        seed=config.seed,
                        lstm_window=config.lstm_window,
                        lstm_hidden=config.lstm_hidden,
                        lstm_epochs=config.lstm_epochs,
                    )
                    for array_id in test["array_id"].unique():
                        array_mask = mask & (test["array_id"].to_numpy() == array_id)
                        finite = np.isfinite(prediction.y_pred)
                        array_mask &= finite
                        if not array_mask.any():
                            continue
                        metrics = regression_metrics(
                            test["y"].to_numpy(dtype=float)[array_mask],
                            prediction.y_pred[array_mask],
                        )
                        details.append(
                            {
                                "mode": mode,
                                "fold": fold.name,
                                "array": array_id,
                                "model": model,
                                **metrics,
                                "train_seconds": prediction.train_seconds,
                                "inference_ms_per_1000": prediction.inference_seconds,
                                "model_bytes": prediction.model_bytes,
                            }
                        )

    _add_skill_scores(details)
    summary = _summarize(details, models)
    return {
        "generated_at": datetime.now(UTC).isoformat(),
        "config": _config_dict(config, folds),
        "dataset": {
            "arrays": arrays,
            "hourly_rows": int(len(frames)),
            "daylight_rows": int(frames["is_day"].fillna(False).sum()),
            "features": FEATURES,
            "note": (
                "Synthetic simulator data (#94), same weather seed for all arrays. "
                "Not a substitute for the real DKASC rerun required by ADR #191."
            ),
        },
        "summary": summary,
        "details": details,
    }


def _add_skill_scores(details: list[dict[str, Any]]) -> None:
    baseline = {
        (row["mode"], row["fold"], row["array"]): row["mae"]
        for row in details
        if row["model"] == "physical"
    }
    for row in details:
        mae = baseline.get((row["mode"], row["fold"], row["array"]))
        row["skill_vs_physical"] = skill_score(row["mae"], mae) if mae is not None else float("nan")


def _summarize(details: list[dict[str, Any]], models: list[str]) -> dict[str, dict[str, Any]]:
    metrics = [
        "mae",
        "rmse",
        "nmae_pct",
        "wmape_pct",
        "skill_vs_physical",
        "train_seconds",
        "inference_ms_per_1000",
        "model_bytes",
    ]
    summary: dict[str, dict[str, Any]] = {}
    for mode in MODES:
        mode_rows = [row for row in details if row["mode"] == mode]
        if not mode_rows:
            continue
        summary[mode] = {}
        for model in models:
            rows = [row for row in mode_rows if row["model"] == model]
            if not rows:
                continue
            stats: dict[str, dict[str, float]] = {}
            for key in metrics:
                values = np.asarray(
                    [row[key] for row in rows if np.isfinite(row[key])], dtype=float
                )
                if len(values) == 0:
                    continue
                stats[key] = {
                    "median": float(np.median(values)),
                    "mean": float(np.mean(values)),
                    "p25": float(np.percentile(values, 25)),
                    "p75": float(np.percentile(values, 75)),
                }
            summary[mode][model] = {"evaluations": len(rows), "metrics": stats}
    return summary


def _config_dict(config: ExperimentConfig, folds: tuple[Fold, ...]) -> dict[str, Any]:
    return {
        "start": str(config.start),
        "end": str(config.end),
        "seed": config.seed,
        "arrays": [asdict(array) for array in config.arrays],
        "folds": [
            {
                "name": fold.name,
                "train_start": str(fold.train_start),
                "train_end": str(fold.train_end),
                "test_start": str(fold.test_start),
                "test_end": str(fold.test_end),
                "gap_days": fold.gap_days,
            }
            for fold in folds
        ],
        "include_lstm": config.include_lstm,
        "include_loao": config.include_loao,
        "lstm_window": config.lstm_window,
        "lstm_hidden": config.lstm_hidden,
        "lstm_epochs": config.lstm_epochs,
    }


def write_report(result: dict[str, Any], *, json_path: Path, markdown_path: Path) -> None:
    json_path.parent.mkdir(parents=True, exist_ok=True)
    markdown_path.parent.mkdir(parents=True, exist_ok=True)
    json_path.write_text(json.dumps(result, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    markdown_path.write_text(render_markdown(result), encoding="utf-8")


def render_markdown(result: dict[str, Any]) -> str:
    arrays = result["dataset"]["arrays"]
    folds = [fold["name"] for fold in result["config"]["folds"]]
    modes = [mode for mode in MODES if result["summary"].get(mode)]
    mode_names = "walk-forward" + (
        " a leave-one-array-out" if "leave_one_array_out" in modes else ""
    )
    lines = [
        "# Spike #191: baseline vs. gradient boosting vs. LSTM",
        "",
        f"Generated: `{result['generated_at']}`",
        "",
        f"**Dáta:** syntetický simulátor #94 ({len(arrays)} polí: {', '.join(arrays)};"
        " rovnaké počasie, hodinová granularita). Reálne DKASC dáta treba zopakovať pred"
        " produkčným rozhodnutím.",
        "",
        f"**Delenie:** expanding-window {mode_names} s 7-dňovou medzerou"
        f" ({', '.join(folds)}). Metriky sú mediánom cez fold × pole; nMAE je v %"
        " menovitého DC výkonu.",
        "",
    ]
    for mode in MODES:
        mode_summary = result["summary"].get(mode)
        if not mode_summary:
            continue
        title = "Walk-forward" if mode == "walk_forward" else "Leave-one-array-out"
        lines.extend(
            [
                f"## {title}",
                "",
                "| Model | nMAE medián | IQR | MAE | RMSE | wMAPE | skill vs physical |"
                " tréning [s] | inferencia [ms/1000] | veľkosť [B] |",
                "|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|",
            ]
        )
        for model in ("persistence", "physical", "linear", "lightgbm", "lstm"):
            stats = mode_summary.get(model, {}).get("metrics")
            if not stats:
                continue
            nmae = stats["nmae_pct"]["median"]
            iqr = stats["nmae_pct"]["p75"] - stats["nmae_pct"]["p25"]
            lines.append(
                "| {model} | {nmae:.2f} | {iqr:.2f} | {mae:.4f} | {rmse:.4f} |"
                " {wmape:.2f} | {skill:+.3f} | {train:.2f} | {infer:.3f} | {size:.0f} |".format(
                    model=model,
                    nmae=nmae,
                    iqr=iqr,
                    mae=stats["mae"]["median"],
                    rmse=stats["rmse"]["median"],
                    wmape=stats["wmape_pct"]["median"],
                    skill=stats["skill_vs_physical"]["median"],
                    train=stats["train_seconds"]["median"],
                    infer=stats["inference_ms_per_1000"]["median"],
                    size=stats["model_bytes"]["median"],
                )
            )
        lines.append("")
    lines.extend(
        [
            "## Poznámky",
            "",
            "- Persistence = výkon pred 24 h; physical = pvlib-like clear-sky/GTI model"
            " bez tréningu.",
            "- LightGBM je trénovaný s early stopping na posledných 15 % tréningu"
            " (7-dňová medzera).",
        ]
    )
    if any("lstm" in summary for summary in result["summary"].values()):
        lines.append(
            "- LSTM má 1 vrstvu, 32 jednotiek, okno 24 h a rovnaké features;"
            " výsledok je orientačný."
        )
    lines.extend(
        [
            "- LightGBM v experimente nepreukázal zlepšenie voči fyzikálnemu baselineu"
            " (WF prakticky nerozhodné, LOAO horšie); baseline je preto MVP a GBM challenger.",
            "- `temperature_c` z telemetrie sa nepoužíva (obsahuje časť cieľa);"
            " features používajú externú `air_temp_c`.",
            "- Syntetické dáta majú rovnaký generátor ako fyzikálny baseline,"
            " preto je baseline silný; reálne dáta môžu poradie zmeniť.",
            "",
        ]
    )
    return "\n".join(lines)


def quick_config() -> ExperimentConfig:
    """Small configuration for tests and notebook smoke runs (no LSTM by default)."""
    start = pd.Timestamp("2023-01-01T00:00Z")
    end = pd.Timestamp("2024-07-01T00:00Z")
    folds = (
        Fold(
            "smoke",
            train_start=start,
            test_start=pd.Timestamp("2024-01-01T00:00Z"),
            test_end=end,
        ),
    )
    return replace(
        ExperimentConfig(),
        start=start,
        end=end,
        arrays=DEFAULT_ARRAYS[:2],
        folds=folds,
        include_lstm=False,
        include_loao=False,
        lstm_epochs=2,
    )

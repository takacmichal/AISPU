"""Regression metrics from ADR #193, kept dependency-free for the spike report."""

from __future__ import annotations

import numpy as np


def regression_metrics(y_true: np.ndarray, y_pred: np.ndarray) -> dict[str, float]:
    """Return MAE, RMSE, nMAE (%), wMAPE (%) and R² for normalized power y (W/Wp)."""
    y_true = np.asarray(y_true, dtype=float)
    y_pred = np.asarray(y_pred, dtype=float)
    if y_true.shape != y_pred.shape:
        raise ValueError("y_true and y_pred must have the same shape")
    if len(y_true) == 0:
        raise ValueError("metrics need at least one row")
    if not np.isfinite(y_true).all() or not np.isfinite(y_pred).all():
        raise ValueError("metrics require finite values")
    error = y_pred - y_true
    mae = float(np.mean(np.abs(error)))
    rmse = float(np.sqrt(np.mean(error**2)))
    wmape = (
        float(np.sum(np.abs(error)) / np.sum(np.abs(y_true)) * 100)
        if np.sum(np.abs(y_true))
        else float("nan")
    )
    variance = float(np.sum((y_true - y_true.mean()) ** 2))
    r2 = 1 - float(np.sum(error**2)) / variance if variance else float("nan")
    return {
        "n": int(len(y_true)),
        "mae": mae,
        "rmse": rmse,
        "nmae_pct": mae * 100,
        "wmape_pct": wmape,
        "r2": r2,
    }


def skill_score(model_mae: float, baseline_mae: float) -> float:
    """1 - MAE_model / MAE_baseline; positive means better than the baseline."""
    if baseline_mae <= 0:
        return float("nan")
    return 1 - model_mae / baseline_mae

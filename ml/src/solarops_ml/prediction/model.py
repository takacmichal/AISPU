"""Calibrated physical model, the MVP production forecast (ADR #191).

ADR #191 picked the physical baseline for the MVP (LightGBM is the challenger). Training
calibrates two interpretable parameters with ordinary least squares (numpy only, so the
service image needs no LightGBM/scikit-learn):

* ``loss_factor`` - DC output scaling (soiling, wiring, mismatch, irradiance bias),
* ``gamma_per_k`` - module power temperature coefficient.

With ``g = gti / 1000`` and the NOCT cell temperature, unclipped AC power per Wp is
``loss * eta * g * (1 + gamma * (T_cell - 25))`` = ``a * x1 + b * x2`` with
``x1 = eta * g``, ``x2 = eta * g * (T_cell - 25)``, so ``loss = a`` and ``gamma = b / a``.
Only daytime rows below the AC clipping limit enter the fit.
"""

from __future__ import annotations

from dataclasses import asdict, dataclass, replace
from typing import Any

import numpy as np
import pandas as pd

from solarops_ml.prediction.features import physical_power
from solarops_ml.simulator.generator import GAMMA_PER_K, INVERTER_EFFICIENCY, NOCT_C

FAMILY = "physical"
MIN_FIT_ROWS = 24
CLIP_MARGIN = 0.97  # rows within 3 % of the AC limit may be clipped and are not fitted
LOSS_BOUNDS = (0.3, 1.5)
GAMMA_BOUNDS = (-0.01, 0.0)


@dataclass(frozen=True)
class PhysicalModel:
    """PVWatts-like model with NOCT cell temperature and AC clipping (W per installed Wp)."""

    loss_factor: float = 1.0
    gamma_per_k: float = GAMMA_PER_K
    noct_c: float = NOCT_C
    inverter_efficiency: float = INVERTER_EFFICIENCY
    fitted_rows: int = 0

    def predict_normalized(self, features: pd.DataFrame) -> np.ndarray:
        """AC power per installed Wp for every feature row."""
        return physical_power(
            features["gti_wm2"].to_numpy(dtype=float),
            features["air_temp_c"].to_numpy(dtype=float),
            features["dc_ac_ratio"].to_numpy(dtype=float),
            loss_factor=self.loss_factor,
            gamma_per_k=self.gamma_per_k,
            noct_c=self.noct_c,
            inverter_efficiency=self.inverter_efficiency,
        )

    def predict_power_w(self, features: pd.DataFrame) -> np.ndarray:
        """Mean AC power over the hour in W (the hourly energy in Wh has the same value)."""
        return self.predict_normalized(features) * features["capacity_kwp"].to_numpy() * 1000

    def fit(self, features: pd.DataFrame, y: np.ndarray) -> PhysicalModel:
        """Return a copy with ``loss_factor`` and ``gamma_per_k`` fitted to ``y`` (W/Wp)."""
        y = np.asarray(y, dtype=float)
        if len(y) != len(features):
            raise ValueError("features and y must have the same length")
        gti = features["gti_wm2"].to_numpy(dtype=float)
        air = features["air_temp_c"].to_numpy(dtype=float)
        limit = 1 / features["dc_ac_ratio"].to_numpy(dtype=float)
        cell = air + gti * (self.noct_c - 20) / 800
        x1 = self.inverter_efficiency * gti / 1000
        x2 = x1 * (cell - 25)
        mask = (
            features["is_day"].to_numpy(dtype=bool)
            & np.isfinite(y)
            & np.isfinite(x1)
            & np.isfinite(x2)
            & (y < CLIP_MARGIN * limit)
        )
        rows = int(mask.sum())
        if rows < MIN_FIT_ROWS:
            raise ValueError(f"need at least {MIN_FIT_ROWS} unclipped daytime rows, got {rows}")
        design = np.column_stack([x1[mask], x2[mask]])
        (a, b), *_ = np.linalg.lstsq(design, y[mask], rcond=None)
        gamma = float(np.clip(b / a, *GAMMA_BOUNDS)) if a > 0 else self.gamma_per_k
        # Refit the scale with the (possibly clipped) temperature coefficient.
        x = x1[mask] * (1 + gamma * (cell[mask] - 25))
        loss = float(np.clip(np.dot(x, y[mask]) / np.dot(x, x), *LOSS_BOUNDS))
        return replace(self, loss_factor=loss, gamma_per_k=gamma, fitted_rows=rows)

    def to_dict(self) -> dict[str, Any]:
        return {"family": FAMILY, **asdict(self)}

    @classmethod
    def from_dict(cls, data: dict[str, Any]) -> PhysicalModel:
        values = dict(data)
        family = values.pop("family", FAMILY)
        if family != FAMILY:
            raise ValueError(f"unsupported model family {family!r}, expected {FAMILY!r}")
        return cls(**values)

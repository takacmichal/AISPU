"""Training table, model comparison and selection for the production forecast (#106, #107).

Candidates, all evaluated on the **same** rows of one walk-forward split (expanding training
window, gap, last ``test_days`` as test; ADR #191):

* ``persistence`` - production 24 h earlier (lower bound),
* ``physical`` - uncalibrated physical baseline (ADR #191 MVP),
* ``physical_calibrated`` - the same model with ``loss_factor``/``gamma_per_k`` fitted on train,
* ``lightgbm`` - optional challenger (``--challenger``, needs ``uv sync --group experiment``).

Only the physical family is servable without new runtime dependencies. A challenger that
passes the ADR #191 gate is reported in the selection note, not deployed.

Training data: the real DKASC dataset is not in the repository (license, ADR #178/#183), so
:func:`synthetic_table` builds a deterministic table from the simulator (#94). Real data go
through :func:`load_table` (documented file formats, see ``ml/README.md``).
"""

from __future__ import annotations

from dataclasses import dataclass, field
from datetime import datetime
from pathlib import Path
from typing import Any

import numpy as np
import pandas as pd

from solarops_ml.forecast.data import DEFAULT_ARRAYS, ArraySpec
from solarops_ml.forecast.metrics import regression_metrics
from solarops_ml.prediction.features import (
    FEATURES,
    INDEX_NAME,
    PlantSpec,
    PoaSource,
    build_features,
    from_open_meteo,
)
from solarops_ml.prediction.model import PhysicalModel
from solarops_ml.simulator import SimulatorConfig, simulate
from solarops_ml.simulator.generator import _air_temperature
from solarops_ml.simulator.solar import clear_sky, solar_position
from solarops_ml.weather import read_dataset

TARGET = "y"
MIN_VALID_SAMPLES_PER_HOUR = 9  # ADR #183: at least 75 % of the 5-minute samples
SERVABLE = ("physical", "physical_calibrated")
BASELINE = "physical"
GATE_MIN_SKILL = 0.20  # ADR #191: challenger must cut nMAE by at least 20 %
GATE_MAX_NMAE_PCT = 6.0  # ADR #191/#193
WEATHER_COLUMNS = ("ghi_wm2", "dni_wm2", "dhi_wm2", "temp_air_c", "wind_speed_ms")


@dataclass(frozen=True)
class Split:
    """Split: train ``[train_start, train_end)``, gap, test ``[test_start, test_end)``."""

    train_start: pd.Timestamp
    train_end: pd.Timestamp
    test_start: pd.Timestamp
    test_end: pd.Timestamp
    gap_days: int

    def to_dict(self) -> dict[str, Any]:
        return {
            "train_start": self.train_start.isoformat(),
            "train_end": self.train_end.isoformat(),
            "test_start": self.test_start.isoformat(),
            "test_end": self.test_end.isoformat(),
            "gap_days": self.gap_days,
        }


@dataclass(frozen=True)
class TrainingResult:
    model: PhysicalModel  # final model, refitted on all rows when calibration was selected
    selected: str
    selection: dict[str, Any]
    metrics: dict[str, Any]
    split: Split
    seed: int
    data: dict[str, Any]
    evaluation_model: PhysicalModel = field(default_factory=PhysicalModel)


def time_split(index: pd.DatetimeIndex, *, test_days: int = 90, gap_days: int = 7) -> Split:
    """Last ``test_days`` are the test window; ``gap_days`` before it are dropped."""
    if gap_days < 0:
        raise ValueError("gap_days must be >= 0")
    start = index.min()
    end = index.max() + pd.Timedelta(hours=1)
    test_start = end - pd.Timedelta(days=test_days)
    train_end = test_start - pd.Timedelta(days=gap_days)
    if test_days <= 0 or train_end - start < pd.Timedelta(days=test_days):
        raise ValueError(
            f"test_days={test_days} and gap_days={gap_days} leave less training than test data"
        )
    return Split(start, train_end, test_start, end, gap_days)


def make_table(
    weather: dict[str, pd.DataFrame],
    plants: dict[str, PlantSpec],
    power_w: dict[str, pd.Series],
    *,
    poa_source: PoaSource = "auto",
) -> pd.DataFrame:
    """Join hourly weather and production per array into the documented training table."""
    frames = []
    for array_id, plant in plants.items():
        hourly = weather[array_id]
        features = build_features(hourly, plant, poa_source=poa_source)
        for column in WEATHER_COLUMNS:
            if column in hourly.columns:
                features[column] = hourly[column].to_numpy(dtype=float)
        features["array_id"] = array_id
        power = power_w[array_id].reindex(features.index)
        features["power_w"] = power.to_numpy(dtype=float)
        features[TARGET] = features["power_w"] / (plant.capacity_kwp * 1000)
        # Only for the persistence reference; never a model feature (no production lags).
        lagged = features[TARGET].copy()
        lagged.index = lagged.index + pd.Timedelta(hours=24)
        features["y_lag_24h"] = lagged.reindex(features.index).to_numpy()
        frames.append(features)
    table = pd.concat(frames).sort_index(kind="stable")
    table.index.name = INDEX_NAME
    return table


def synthetic_table(
    start: datetime,
    end: datetime,
    arrays: tuple[ArraySpec, ...] = DEFAULT_ARRAYS,
    *,
    latitude: float = -23.76,
    longitude: float = 133.87,
    seed: int = 42,
    cloudiness: float = 0.3,
) -> pd.DataFrame:
    """Simulated hourly training table (one site, shared weather seed, several arrays).

    Weather columns mimic Open-Meteo: GHI/DNI/DHI are clear-sky components scaled by the same
    cloud factor the simulator uses (so they transpose to the simulated GTI), ``gti_wm2`` is
    the simulator's plane irradiance and ``temp_air_c`` its air temperature model.
    """
    if not arrays:
        raise ValueError("at least one array is required")
    weather: dict[str, pd.DataFrame] = {}
    plants: dict[str, PlantSpec] = {}
    power: dict[str, pd.Series] = {}
    for spec in arrays:
        config = SimulatorConfig(
            device_id=f"training-{spec.name}",
            latitude=latitude,
            longitude=longitude,
            tilt=spec.tilt,
            azimuth=spec.azimuth,
            capacity_kwp=spec.capacity_kwp,
            dc_ac_ratio=spec.dc_ac_ratio,
            utc_offset_hours=round(longitude / 15 * 2) / 2,
            cloudiness=cloudiness,
            seed=seed,
        )
        readings = simulate(config, start, end).readings
        index = readings.index
        position = solar_position(index, latitude, longitude)
        ghi, dni, dhi = clear_sky(position["zenith_deg"].to_numpy(), np.asarray(index.dayofyear))
        cloud = readings["cloud_cover_pct"].to_numpy() / 100
        factor = 1 - 0.75 * cloud**3.4  # Kasten-Czeplak, as in the simulator
        five_min = pd.DataFrame(
            {
                "ghi_wm2": ghi * factor,
                "dni_wm2": dni * factor,
                "dhi_wm2": dhi * factor,
                "gti_wm2": readings["gti_wm2"].to_numpy(),
                "cloud_cover_pct": readings["cloud_cover_pct"].to_numpy(),
                "temp_air_c": _air_temperature(index, config),
                "power_w": readings["ac_power_w"].to_numpy(),
            },
            index=index,
        )
        group = five_min.resample("1h", closed="left", label="left")
        hourly = group.mean()
        hourly.loc[group["power_w"].count() < MIN_VALID_SAMPLES_PER_HOUR, "power_w"] = np.nan
        hourly.index = pd.DatetimeIndex(hourly.index, name=INDEX_NAME)
        weather[spec.name] = hourly.drop(columns="power_w")
        power[spec.name] = hourly["power_w"]
        plants[spec.name] = PlantSpec(
            latitude, longitude, spec.capacity_kwp, spec.tilt, spec.azimuth, spec.dc_ac_ratio
        )
    return make_table(weather, plants, power, poa_source="auto")


def load_table(
    production_path: Path,
    weather_path: Path,
    arrays_path: Path,
    *,
    latitude: float,
    longitude: float,
    poa_source: PoaSource = "transpose",
) -> pd.DataFrame:
    """Build the training table from files (see ``ml/README.md``, "Tréning na reálnych dátach").

    * production: CSV/parquet ``hour_start_utc, array_id, power_w`` (hourly mean AC power in W,
      hour labelled by its start in UTC, empty when the hour has < 9 of 12 samples, ADR #183),
    * weather: ``solarops-weather-2`` dataset from ``solarops_ml.weather`` (end-labelled, shifted
      here to ``hour_start_utc``),
    * arrays: CSV ``array_id, tilt, azimuth, capacity_kwp[, dc_ac_ratio]``.

    ``poa_source="transpose"`` (default) ignores the weather GTI, which is only valid for the
    plane it was requested for; use ``"auto"`` when the weather was fetched with the array's
    own ``--tilt/--azimuth`` and there is a single array.
    """
    production = _read_frame(production_path)
    missing = {"hour_start_utc", "array_id", "power_w"} - set(production.columns)
    if missing:
        raise ValueError(f"{production_path}: missing columns {sorted(missing)}")
    production["hour_start_utc"] = pd.to_datetime(production["hour_start_utc"], utc=True)
    production["array_id"] = production["array_id"].astype(str)

    arrays = _read_frame(arrays_path)
    arrays["array_id"] = arrays["array_id"].astype(str)
    plants = {
        row.array_id: PlantSpec(
            latitude,
            longitude,
            float(row.capacity_kwp),
            float(row.tilt),
            float(row.azimuth),
            float(getattr(row, "dc_ac_ratio", 1.2)),
        )
        for row in arrays.itertuples(index=False)
    }
    unknown = sorted(set(production["array_id"]) - set(plants))
    if unknown:
        raise ValueError(f"production has arrays without metadata in {arrays_path}: {unknown}")

    hourly_weather = from_open_meteo(read_dataset(weather_path))
    full_index = pd.date_range(
        hourly_weather.index.min(), hourly_weather.index.max(), freq="1h", name=INDEX_NAME
    )
    hourly_weather = hourly_weather.reindex(full_index)
    power = {
        array_id: group.set_index("hour_start_utc")["power_w"].astype(float)
        for array_id, group in production.groupby("array_id", sort=True)
    }
    used = {array_id: plants[array_id] for array_id in power}
    return make_table(dict.fromkeys(used, hourly_weather), used, power, poa_source=poa_source)


def train(
    table: pd.DataFrame,
    *,
    seed: int = 42,
    test_days: int = 90,
    gap_days: int = 7,
    include_challenger: bool = False,
    source: str = "synthetic",
    inputs: dict[str, Any] | None = None,
) -> TrainingResult:
    """Compare candidates on one split, select the servable model and refit it on all rows."""
    from solarops_ml.prediction.registry import data_hash

    split = time_split(table.index, test_days=test_days, gap_days=gap_days)
    index = table.index
    train_rows = table.loc[(index >= split.train_start) & (index < split.train_end)]
    test_rows = table.loc[(index >= split.test_start) & (index < split.test_end)]
    if train_rows.empty or test_rows.empty:
        raise ValueError("split produced an empty training or test set")

    calibrated = PhysicalModel().fit(train_rows, train_rows[TARGET].to_numpy(dtype=float))
    predictions = {
        "persistence": test_rows["y_lag_24h"].to_numpy(dtype=float),
        "physical": PhysicalModel().predict_normalized(test_rows),
        "physical_calibrated": calibrated.predict_normalized(test_rows),
    }
    if include_challenger:
        predictions["lightgbm"] = _lightgbm_predictions(table, train_rows, test_rows, seed)

    metrics = _evaluate(test_rows, predictions)
    selection = select_model(metrics["summary"])
    selected = selection["selected"]
    if selected == "physical_calibrated":
        final = PhysicalModel().fit(table, table[TARGET].to_numpy(dtype=float))
    else:
        final = PhysicalModel()

    data = {
        "source": source,
        "sha256": data_hash(table),
        "rows": int(len(table)),
        "daylight_rows": int(table["is_day"].sum()),
        "target_rows": int(table[TARGET].notna().sum()),
        "start": index.min().isoformat(),
        "end": (index.max() + pd.Timedelta(hours=1)).isoformat(),
        "arrays": sorted(table["array_id"].unique().tolist()),
        "features": FEATURES,
        "inputs": inputs or {},
    }
    return TrainingResult(
        model=final,
        selected=selected,
        selection=selection,
        metrics=metrics,
        split=split,
        seed=seed,
        data=data,
        evaluation_model=calibrated,
    )


def select_model(summary: dict[str, dict[str, Any]]) -> dict[str, Any]:
    """Pick the servable model and evaluate challengers against the ADR #191 gate."""
    baseline = summary[BASELINE]["nmae_pct"]
    calibrated = summary["physical_calibrated"]["nmae_pct"]
    if calibrated <= baseline:
        selected = "physical_calibrated"
        reason = (
            f"Kalibrovaný fyzikálny model má nMAE {calibrated:.3f} % ≤ {baseline:.3f} %"
            " nekalibrovaného baselineu na rovnakom teste; je vysvetliteľný a bez nových"
            " závislostí (ADR #191)."
        )
    else:
        selected = BASELINE
        reason = (
            f"Kalibrácia zhoršila nMAE ({calibrated:.3f} % > {baseline:.3f} %), preto ostáva"
            " nekalibrovaný fyzikálny baseline (MVP podľa ADR #191)."
        )
    gate: dict[str, dict[str, Any]] = {}
    notes = []
    for name, stats in summary.items():
        if name in SERVABLE or name == "persistence":
            continue
        nmae = stats["nmae_pct"]
        skill = 1 - nmae / baseline if baseline > 0 else float("nan")
        passed = bool(skill >= GATE_MIN_SKILL and nmae <= GATE_MAX_NMAE_PCT)
        gate[name] = {"nmae_pct": nmae, "skill_vs_physical": skill, "passed": passed}
        if passed:
            notes.append(
                f"Challenger {name} prešiel bránou ADR #191 (skill {skill:+.3f}, nMAE"
                f" {nmae:.3f} %), ale servovanie vyžaduje runtime závislosť mimo MVP;"
                " treba schválenie tímu."
            )
        else:
            notes.append(f"Challenger {name} neprešiel bránou ADR #191 (skill {skill:+.3f}).")
    return {
        "selected": selected,
        "reason": reason,
        "gate": gate,
        "gate_rule": {"min_skill_vs_physical": GATE_MIN_SKILL, "max_nmae_pct": GATE_MAX_NMAE_PCT},
        "note": " ".join(notes),
    }


def _evaluate(test_rows: pd.DataFrame, predictions: dict[str, np.ndarray]) -> dict[str, Any]:
    """Per-array metrics on rows every model can predict (daylight, ADR #193), plus medians."""
    y = test_rows[TARGET].to_numpy(dtype=float)
    mask = test_rows["is_day"].to_numpy(dtype=bool) & np.isfinite(y)
    for values in predictions.values():
        mask &= np.isfinite(values)
    if not mask.any():
        raise ValueError("no daylight test rows with a target and all predictions")
    array_ids = test_rows["array_id"].to_numpy()
    per_array: dict[str, dict[str, dict[str, float]]] = {}
    summary: dict[str, dict[str, Any]] = {}
    for name, values in predictions.items():
        per_array[name] = {}
        for array_id in sorted(set(array_ids[mask])):
            rows = mask & (array_ids == array_id)
            per_array[name][array_id] = regression_metrics(y[rows], values[rows])
        stats = list(per_array[name].values())
        summary[name] = {
            "n": int(sum(s["n"] for s in stats)),
            **{
                key: float(np.median([s[key] for s in stats]))
                for key in ("mae", "rmse", "nmae_pct", "wmape_pct", "r2")
            },
        }
    return {
        "population": "test window, daylight (gti > 50 W/m²), target and all predictions known",
        "aggregation": "median over arrays (ADR #193)",
        "summary": summary,
        "per_array": per_array,
    }


def _lightgbm_predictions(
    table: pd.DataFrame, train_rows: pd.DataFrame, test_rows: pd.DataFrame, seed: int
) -> np.ndarray:
    try:
        from solarops_ml.forecast.models import fit_predict
    except ImportError as exc:  # pragma: no cover - import of numpy/pandas only
        raise ImportError("challenger needs: uv sync --group experiment") from exc
    history = table.loc[table.index < test_rows.index.max()]
    try:
        prediction = fit_predict("lightgbm", train_rows, test_rows, history, seed=seed)
    except ImportError as exc:
        raise ImportError("LightGBM challenger needs: uv sync --group experiment") from exc
    return prediction.y_pred


def _read_frame(path: Path) -> pd.DataFrame:
    if path.suffix == ".parquet":
        return pd.read_parquet(path)
    return pd.read_csv(path)

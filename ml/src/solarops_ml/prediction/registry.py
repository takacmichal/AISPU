"""Versioned model store (issue #108, spike #198): one directory per version, JSON only.

Layout under ``$SOLAROPS_MODEL_DIR`` (default ``ml/data/models``, gitignored)::

    <model_dir>/<version>/model.json       parameters of the model (no pickle)
    <model_dir>/<version>/metadata.json    data hash, split, seed, metrics, selection, versions
    <model_dir>/<version>/metrics.md       human readable metrics table
    <model_dir>/current.json               {"version": "..."} pointer used by the service

Version name: ``YYYYMMDD-<model>-<first 8 chars of the data sha256>``. Rollback is
``set_current(model_dir, old_version)`` (CLI: ``python -m solarops_ml.prediction use <version>``).
"""

from __future__ import annotations

import hashlib
import json
import os
import platform
from dataclasses import dataclass
from datetime import UTC, datetime
from pathlib import Path
from typing import TYPE_CHECKING, Any

import numpy as np
import pandas as pd

from solarops_ml import __version__
from solarops_ml.prediction.model import PhysicalModel

if TYPE_CHECKING:
    from solarops_ml.prediction.training import TrainingResult

CURRENT_FILE = "current.json"
SCHEMA = "solarops-model-1"


class ModelLoadError(RuntimeError):
    """A model artifact is missing or corrupt."""


@dataclass(frozen=True)
class LoadedModel:
    version: str
    model: PhysicalModel
    metadata: dict[str, Any]


def default_model_dir() -> Path:
    env = os.environ.get("SOLAROPS_MODEL_DIR")
    if env:
        return Path(env)
    return Path(__file__).resolve().parents[3] / "data" / "models"


def data_hash(table: pd.DataFrame) -> str:
    """SHA-256 over the content of the table (column order and parquet encoding independent)."""
    frame = table[sorted(table.columns)]
    hashed = pd.util.hash_pandas_object(frame, index=True).to_numpy(dtype="uint64")
    digest = hashlib.sha256()
    digest.update(",".join(frame.columns).encode())
    digest.update(hashed.tobytes())
    return digest.hexdigest()


def save_model(result: TrainingResult, model_dir: Path, *, now: datetime | None = None) -> Path:
    """Write a version directory and make it current. Same run + same day -> same path."""
    now = (now or datetime.now(UTC)).astimezone(UTC)
    version = f"{now:%Y%m%d}-{result.selected}-{result.data['sha256'][:8]}"
    path = model_dir / version
    path.mkdir(parents=True, exist_ok=True)
    metadata = {
        "schema": SCHEMA,
        "version": version,
        "created_at": now.isoformat(),
        "seed": result.seed,
        "model": result.model.to_dict(),
        "data": result.data,
        "split": result.split.to_dict(),
        "metrics": result.metrics,
        "selection": result.selection,
        "environment": {
            "solarops_ml": __version__,
            "python": platform.python_version(),
            "numpy": np.__version__,
            "pandas": pd.__version__,
        },
    }
    _write_json(path / "model.json", result.model.to_dict())
    _write_json(path / "metadata.json", metadata)
    (path / "metrics.md").write_text(render_metrics(metadata), encoding="utf-8")
    set_current(model_dir, version)
    return path


def render_metrics(metadata: dict[str, Any]) -> str:
    selection = metadata["selection"]
    lines = [
        f"# Model {metadata['version']}",
        "",
        f"Vybraný: `{selection['selected']}`. {selection['reason']}",
        "",
        "| Model | n | MAE | RMSE | nMAE [%] | wMAPE [%] | R2 |",
        "|---|---:|---:|---:|---:|---:|---:|",
    ]
    for name, s in metadata["metrics"]["summary"].items():
        lines.append(
            f"| {name} | {s['n']} | {s['mae']:.4f} | {s['rmse']:.4f} | {s['nmae_pct']:.3f} |"
            f" {s['wmape_pct']:.3f} | {s['r2']:.4f} |"
        )
    lines += ["", f"Populácia: {metadata['metrics']['population']}", ""]
    if selection.get("note"):
        lines += [selection["note"], ""]
    return "\n".join(lines)


def list_versions(model_dir: Path) -> list[str]:
    if not model_dir.is_dir():
        return []
    return sorted(p.name for p in model_dir.iterdir() if (p / "model.json").is_file())


def set_current(model_dir: Path, version: str) -> None:
    if version not in list_versions(model_dir):
        raise ModelLoadError(f"unknown model version {version!r} in {model_dir}")
    _write_json(model_dir / CURRENT_FILE, {"version": version})


def resolve_current(model_dir: Path) -> Path | None:
    pointer = model_dir / CURRENT_FILE
    if not pointer.is_file():
        return None
    try:
        version = json.loads(pointer.read_text(encoding="utf-8"))["version"]
    except (ValueError, KeyError) as exc:
        raise ModelLoadError(f"{pointer} is not a valid pointer") from exc
    return model_dir / version


def load_model(path: Path) -> LoadedModel:
    try:
        model = PhysicalModel.from_dict(json.loads((path / "model.json").read_text("utf-8")))
        metadata = json.loads((path / "metadata.json").read_text("utf-8"))
    except (OSError, ValueError, TypeError) as exc:
        raise ModelLoadError(f"cannot load model from {path}: {exc}") from exc
    return LoadedModel(version=path.name, model=model, metadata=metadata)


def _write_json(path: Path, payload: dict[str, Any]) -> None:
    path.write_text(json.dumps(payload, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")

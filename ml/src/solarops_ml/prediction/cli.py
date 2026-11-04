"""CLI: ``python -m solarops_ml.prediction train|build-table|list|use`` (issues #106-#108)."""

from __future__ import annotations

import argparse
import sys
from collections.abc import Sequence
from datetime import UTC, datetime
from pathlib import Path

import pandas as pd

from solarops_ml.forecast.data import DEFAULT_ARRAYS, ArraySpec
from solarops_ml.prediction.registry import (
    ModelLoadError,
    default_model_dir,
    list_versions,
    resolve_current,
    save_model,
    set_current,
)
from solarops_ml.prediction.training import load_table, synthetic_table, train


def _utc(value: str) -> datetime:
    parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
    return parsed if parsed.tzinfo else parsed.replace(tzinfo=UTC)


def _array(value: str) -> ArraySpec:
    try:
        name, tilt, azimuth, capacity, ratio = value.split(",")
        return ArraySpec(name, float(tilt), float(azimuth), float(capacity), float(ratio))
    except ValueError as exc:
        raise argparse.ArgumentTypeError(
            f"expected NAME,TILT,AZIMUTH,CAPACITY_KWP,DC_AC_RATIO: {exc}"
        ) from exc


def build_parser() -> argparse.ArgumentParser:
    p = argparse.ArgumentParser(prog="solarops_ml.prediction", description=__doc__)
    sub = p.add_subparsers(dest="command", required=True)

    def where(sp: argparse.ArgumentParser) -> None:
        sp.add_argument("--model-dir", type=Path, default=None, help="default: $SOLAROPS_MODEL_DIR")

    t = sub.add_parser("train", help="compare models, select and store a version")
    src = t.add_mutually_exclusive_group(required=True)
    src.add_argument("--synthetic", action="store_true", help="simulator data (offline)")
    src.add_argument("--table", type=Path, help="table from build-table (.parquet)")
    t.add_argument("--start", type=_utc, default=datetime(2023, 1, 1, tzinfo=UTC))
    t.add_argument("--end", type=_utc, default=datetime(2025, 1, 1, tzinfo=UTC))
    t.add_argument("--array", type=_array, action="append", dest="arrays")
    t.add_argument("--seed", type=int, default=42)
    t.add_argument("--test-days", type=int, default=90)
    t.add_argument("--gap-days", type=int, default=7)
    t.add_argument("--challenger", action="store_true", help="also train LightGBM (experiment)")
    where(t)

    b = sub.add_parser("build-table", help="join real production + weather into a table")
    b.add_argument("--production", type=Path, required=True)
    b.add_argument("--weather", type=Path, required=True)
    b.add_argument("--arrays", type=Path, required=True)
    b.add_argument("--lat", type=float, required=True)
    b.add_argument("--lon", type=float, required=True)
    b.add_argument("--poa-source", choices=["transpose", "auto"], default="transpose")
    b.add_argument("--out", type=Path, required=True)

    where(sub.add_parser("list", help="list stored versions"))
    u = sub.add_parser("use", help="make a stored version current (rollback)")
    u.add_argument("version")
    where(u)
    return p


def main(argv: Sequence[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
    try:
        if args.command == "build-table":
            table = load_table(
                args.production,
                args.weather,
                args.arrays,
                latitude=args.lat,
                longitude=args.lon,
                poa_source=args.poa_source,
            )
            args.out.parent.mkdir(parents=True, exist_ok=True)
            table.to_parquet(args.out)
            print(f"wrote {len(table)} rows -> {args.out}")
            return 0
        model_dir = args.model_dir or default_model_dir()
        if args.command == "list":
            current = resolve_current(model_dir)
            for version in list_versions(model_dir):
                print(f"{'*' if current and current.name == version else ' '} {version}")
            return 0
        if args.command == "use":
            set_current(model_dir, args.version)
            print(f"current model: {args.version}")
            return 0
        if args.synthetic:
            table = synthetic_table(
                args.start,
                args.end,
                tuple(args.arrays) if args.arrays else DEFAULT_ARRAYS,
                seed=args.seed,
            )
            source, inputs = "synthetic", {"simulator": "solarops_ml.simulator", "seed": args.seed}
        else:
            table = pd.read_parquet(args.table)
            source, inputs = "table", {"file": args.table.name}
        result = train(
            table,
            seed=args.seed,
            test_days=args.test_days,
            gap_days=args.gap_days,
            include_challenger=args.challenger,
            source=source,
            inputs=inputs,
        )
        path = save_model(result, model_dir)
    except (ValueError, ImportError, ModelLoadError, OSError) as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 2
    print(f"model {path.name} -> {path}")
    for name, s in result.metrics["summary"].items():
        print(f"  {name:20s} nMAE {s['nmae_pct']:.3f} %  MAE {s['mae']:.4f}  RMSE {s['rmse']:.4f}")
    print(f"selected: {result.selected}. {result.selection['reason']}")
    return 0

"""CLI: ``python -m solarops_ml.forecast --out-json ... --out-markdown ...``.

The experiment trains every model on a synthetic hourly dataset from the simulator
(#94), because the real DKASC data is not committed. Results are therefore a
methodological dry run; the ADR requires a rerun on real data before production use.
"""

from __future__ import annotations

import argparse
import json
import sys
from datetime import UTC, datetime
from pathlib import Path

from solarops_ml.forecast.data import DEFAULT_ARRAYS, ArraySpec, Fold
from solarops_ml.forecast.experiment import (
    ExperimentConfig,
    quick_config,
    run_experiment,
    write_report,
)


def _utc(value: str) -> datetime:
    parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
    return parsed if parsed.tzinfo else parsed.replace(tzinfo=UTC)


def _array(value: str) -> ArraySpec:
    try:
        name, tilt, azimuth, capacity, dc_ac = value.split(",")
        return ArraySpec(name, float(tilt), float(azimuth), float(capacity), float(dc_ac))
    except ValueError as exc:
        raise argparse.ArgumentTypeError(
            "expected NAME,TILT,AZIMUTH,CAPACITY_KWP,DC_AC_RATIO "
            f"(e.g. south_20,20,180,8,1.2): {exc}"
        ) from exc


def _fold(value: str) -> tuple[str, datetime, datetime, int]:
    try:
        name, test_start, test_end, gap_days = value.split(",")
        return name, _utc(test_start), _utc(test_end), int(gap_days)
    except ValueError as exc:
        raise argparse.ArgumentTypeError(
            "expected NAME,TEST_START,TEST_END,GAP_DAYS "
            f"(e.g. 2024H1,2024-01-01,2024-07-01,7): {exc}"
        ) from exc


def build_parser() -> argparse.ArgumentParser:
    p = argparse.ArgumentParser(prog="solarops_ml.forecast", description=__doc__)
    p.add_argument("--start", type=_utc, default=datetime(2023, 1, 1, tzinfo=UTC))
    p.add_argument("--end", type=_utc, default=datetime(2025, 1, 1, tzinfo=UTC))
    p.add_argument("--seed", type=int, default=42)
    p.add_argument(
        "--array",
        type=_array,
        action="append",
        dest="arrays",
        help="override the simulated arrays (repeatable; default: five south-facing arrays)",
    )
    p.add_argument(
        "--fold",
        type=_fold,
        action="append",
        dest="folds",
        help="override walk-forward folds (repeatable; default: 2024H1 and 2024H2)",
    )
    p.add_argument("--skip-lstm", action="store_true", help="do not train the LSTM")
    p.add_argument("--skip-loao", action="store_true", help="skip leave-one-array-out")
    p.add_argument("--lstm-window", type=int, default=24)
    p.add_argument("--lstm-hidden", type=int, default=32)
    p.add_argument("--lstm-epochs", type=int, default=20)
    p.add_argument("--quick", action="store_true", help="small smoke configuration")
    p.add_argument("--out-json", type=Path, default=Path("spike-191-results.json"))
    p.add_argument("--out-markdown", type=Path, default=Path("spike-191-results.md"))
    p.add_argument("--print", action="store_true", help="print the JSON report to stdout")
    return p


def main(argv: list[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
    if args.quick:
        config = quick_config()
    else:
        folds = (
            tuple(
                Fold(
                    name,
                    train_start=args.start,
                    test_start=test_start,
                    test_end=test_end,
                    gap_days=gap,
                )
                for name, test_start, test_end, gap in args.folds
            )
            if args.folds
            else None
        )
        config = ExperimentConfig(
            start=args.start,
            end=args.end,
            arrays=tuple(args.arrays) if args.arrays else DEFAULT_ARRAYS,
            seed=args.seed,
            folds=folds,
            include_lstm=not args.skip_lstm,
            include_loao=not args.skip_loao,
            lstm_window=args.lstm_window,
            lstm_hidden=args.lstm_hidden,
            lstm_epochs=args.lstm_epochs,
        )
    try:
        result = run_experiment(config)
    except (ValueError, ImportError) as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 2
    write_report(result, json_path=args.out_json, markdown_path=args.out_markdown)
    print(
        f"wrote {args.out_json} and {args.out_markdown} ({len(result['details'])} evaluations)",
        file=sys.stderr,
    )
    if args.print:
        print(json.dumps(result, indent=2, ensure_ascii=False))
    return 0

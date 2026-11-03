"""CLI: ``python -m solarops_ml.detection --out-json ... --out-markdown ...`` (issue #118)."""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

from solarops_ml.detection.evaluation import EvaluationConfig, run_evaluation, write_report


def build_parser() -> argparse.ArgumentParser:
    p = argparse.ArgumentParser(prog="solarops_ml.detection", description=__doc__)
    p.add_argument("--seed", type=int, default=42)
    p.add_argument("--scenarios", type=int, default=40, help="number of simulated scenarios")
    p.add_argument("--days", type=int, default=14, help="length of each scenario in days")
    p.add_argument("--out-json", type=Path, default=Path("reports/detection-eval.json"))
    p.add_argument("--out-markdown", type=Path, default=Path("reports/detection-eval.md"))
    return p


def main(argv: list[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
    config = EvaluationConfig(n_scenarios=args.scenarios, days=args.days, seed=args.seed)
    report = run_evaluation(config)
    write_report(report, json_path=args.out_json, markdown_path=args.out_markdown)
    primary = report["metrics"]["primary"]
    print(
        f"wrote {args.out_json} and {args.out_markdown}: precision {primary['precision']}, "
        f"recall {primary['recall']}, f1 {primary['f1']}",
        file=sys.stderr,
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

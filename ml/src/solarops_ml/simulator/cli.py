"""CLI: ``python -m solarops_ml.simulator --location alice_springs --start ... --end ...``."""

from __future__ import annotations

import argparse
import json
import sys
import uuid
from datetime import UTC, datetime
from pathlib import Path

from solarops_ml.simulator.generator import FaultSpec, SimulatorConfig, simulate
from solarops_ml.simulator.telemetry import to_payloads
from solarops_ml.weather.locations import load_locations


def _utc(value: str) -> datetime:
    parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
    return parsed if parsed.tzinfo else parsed.replace(tzinfo=UTC)


def _fault(value: str) -> FaultSpec:
    try:
        kind, start, end, severity = value.split(",")
        return FaultSpec(kind, _utc(start), _utc(end), float(severity))
    except ValueError as exc:
        raise argparse.ArgumentTypeError(
            "expected TYPE,START,END,SEVERITY "
            f"(e.g. power_drop,2024-06-20T08:00,2024-06-20T12:00,0.5): {exc}"
        ) from exc


def build_parser() -> argparse.ArgumentParser:
    p = argparse.ArgumentParser(prog="solarops_ml.simulator", description=__doc__)
    p.add_argument("--location", default="alice_springs", help="name from config/locations.toml")
    p.add_argument("--start", type=_utc, required=True, help="UTC, ISO 8601 (inclusive)")
    p.add_argument("--end", type=_utc, required=True, help="UTC, ISO 8601 (exclusive)")
    p.add_argument("--seed", type=int, default=0)
    p.add_argument("--device-id", help="UUID; default derived from location and seed")
    p.add_argument("--capacity-kwp", type=float, default=10.0)
    p.add_argument("--cloudiness", type=float, default=0.3, help="mean cloud cover 0..1")
    p.add_argument("--utc-offset", type=float, help="local UTC offset in hours for dayWh reset")
    p.add_argument(
        "--fault",
        type=_fault,
        action="append",
        default=[],
        metavar="SPEC",
        help="TYPE,START,END,SEVERITY; TYPE is power_drop or inverter_fault",
    )
    p.add_argument(
        "--out", type=Path, help="write payloads (JSON array of batches); default stdout"
    )
    p.add_argument("--labels-out", type=Path, help="write fault labels as CSV (not part of ingest)")
    return p


def main(argv: list[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
    locations = load_locations()
    if args.location not in locations:
        print(f"unknown location {args.location!r}; known: {', '.join(locations)}", file=sys.stderr)
        return 2
    loc = locations[args.location]
    device_id = args.device_id or str(
        uuid.uuid5(uuid.NAMESPACE_URL, f"solarops-sim/{args.location}/{args.seed}")
    )
    offset = args.utc_offset if args.utc_offset is not None else round(loc.longitude / 15 * 2) / 2
    try:
        config = SimulatorConfig(
            device_id=device_id,
            latitude=loc.latitude,
            longitude=loc.longitude,
            tilt=loc.tilt if loc.tilt is not None else 20.0,
            azimuth=loc.azimuth
            if loc.azimuth is not None
            else (180.0 if loc.latitude < 0 else 0.0),
            capacity_kwp=args.capacity_kwp,
            cloudiness=args.cloudiness,
            utc_offset_hours=offset,
            seed=args.seed,
        )
        result = simulate(config, args.start, args.end, args.fault)
        payloads = to_payloads(result)
    except ValueError as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 2
    text = json.dumps(payloads, indent=2)
    if args.out:
        args.out.parent.mkdir(parents=True, exist_ok=True)
        args.out.write_text(text + "\n", encoding="utf-8")
    else:
        print(text)
    if args.labels_out:
        args.labels_out.parent.mkdir(parents=True, exist_ok=True)
        result.labels.to_csv(args.labels_out)
    return 0

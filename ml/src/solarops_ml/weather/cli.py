"""CLI: ``uv run python -m solarops_ml.weather fetch --location kosice --start ... --end ...``."""

from __future__ import annotations

import argparse
import logging
import sys
from collections.abc import Sequence
from datetime import date
from pathlib import Path

from solarops_ml.weather.client import ATTRIBUTION, OpenMeteoClient, Resolution, WeatherSource
from solarops_ml.weather.locations import DEFAULT_LOCATIONS_FILE, load_locations, slug_for
from solarops_ml.weather.store import default_data_dir, fetch_weather, write_dataset

SOURCE_CHOICES = ["auto", *(s.value for s in WeatherSource)]


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="python -m solarops_ml.weather",
        description="Download historical weather from Open-Meteo. " + ATTRIBUTION + ".",
    )
    sub = parser.add_subparsers(dest="command", required=True)

    def common(p: argparse.ArgumentParser) -> None:
        p.add_argument("--start", type=date.fromisoformat, required=True, help="YYYY-MM-DD")
        p.add_argument("--end", type=date.fromisoformat, required=True, help="YYYY-MM-DD")
        p.add_argument("--source", choices=SOURCE_CHOICES, default="auto")
        p.add_argument(
            "--resolution", choices=[r.value for r in Resolution], default=Resolution.HOURLY.value
        )
        p.add_argument("--locations-file", type=Path, default=DEFAULT_LOCATIONS_FILE)
        p.add_argument("--data-dir", type=Path, default=None, help="default: $SOLAROPS_DATA_DIR")
        p.add_argument("--format", choices=["parquet", "csv"], default="parquet")
        p.add_argument("--no-cache", action="store_true", help="ignore and do not write cache")
        p.add_argument("-v", "--verbose", action="store_true")

    fetch = sub.add_parser("fetch", help="one location (named or by coordinates)")
    where = fetch.add_mutually_exclusive_group(required=True)
    where.add_argument("--location", help="name from the locations file")
    where.add_argument("--lat", type=float, help="latitude (requires --lon)")
    fetch.add_argument("--lon", type=float, help="longitude")
    fetch.add_argument("--out", type=Path, default=None, help="output file (.parquet or .csv)")
    common(fetch)

    fetch_all = sub.add_parser("fetch-all", help="every location in the locations file")
    common(fetch_all)

    listing = sub.add_parser("locations", help="list configured locations")
    listing.add_argument("--locations-file", type=Path, default=DEFAULT_LOCATIONS_FILE)
    return parser


def main(argv: Sequence[str] | None = None, client: OpenMeteoClient | None = None) -> int:
    parser = build_parser()
    args = parser.parse_args(argv)

    if args.command == "locations":
        for loc in load_locations(args.locations_file).values():
            print(f"{loc.name}\t{loc.latitude}\t{loc.longitude}\t{loc.label}")
        return 0

    logging.basicConfig(
        level=logging.DEBUG if args.verbose else logging.INFO, format="%(levelname)s %(message)s"
    )
    if args.end < args.start:
        parser.error("--end must not be before --start")

    targets: list[tuple[str, float, float]]
    if args.command == "fetch":
        if args.location:
            locations = load_locations(args.locations_file)
            if args.location not in locations:
                parser.error(f"unknown location '{args.location}', known: {', '.join(locations)}")
            loc = locations[args.location]
            targets = [(loc.name, loc.latitude, loc.longitude)]
        else:
            if args.lon is None:
                parser.error("--lat requires --lon")
            targets = [(slug_for(args.lat, args.lon), args.lat, args.lon)]
    else:
        targets = [
            (loc.name, loc.latitude, loc.longitude)
            for loc in load_locations(args.locations_file).values()
        ]

    source = None if args.source == "auto" else WeatherSource(args.source)
    resolution = Resolution(args.resolution)
    data_dir = args.data_dir or default_data_dir()
    owns_client = client is None
    client = client or OpenMeteoClient()
    try:
        for name, lat, lon in targets:
            effective = source or WeatherSource.auto(args.start)
            frame = fetch_weather(
                client,
                lat,
                lon,
                args.start,
                args.end,
                source=effective,
                resolution=resolution,
                data_dir=data_dir,
                use_cache=not args.no_cache,
            )
            out = (
                args.out
                if args.command == "fetch" and args.out
                else data_dir
                / "weather"
                / (
                    f"{name}_{args.start.isoformat()}_{args.end.isoformat()}"
                    f"_{effective.value}_{resolution.value}.{args.format}"
                )
            )
            write_dataset(frame, out)
            print(f"{name}: {len(frame)} rows -> {out}")
    finally:
        if owns_client:
            client.close()
    return 0


if __name__ == "__main__":
    sys.exit(main())

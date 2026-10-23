"""Named locations (TOML) for weather downloads."""

from __future__ import annotations

import re
import tomllib
from dataclasses import dataclass
from pathlib import Path

DEFAULT_LOCATIONS_FILE = Path(__file__).resolve().parents[3] / "config" / "locations.toml"


@dataclass(frozen=True)
class Location:
    name: str
    latitude: float
    longitude: float
    label: str = ""
    tilt: float | None = None  # panel tilt for GTI, degrees (0 = horizontal)
    azimuth: float | None = None  # panel azimuth for GTI, degrees (0 = south, 180 = north)

    def __post_init__(self) -> None:
        if not -90 <= self.latitude <= 90:
            raise ValueError(f"{self.name}: latitude {self.latitude} out of range")
        if not -180 <= self.longitude <= 180:
            raise ValueError(f"{self.name}: longitude {self.longitude} out of range")
        if self.tilt is not None and not 0 <= self.tilt <= 90:
            raise ValueError(f"{self.name}: tilt {self.tilt} out of range")
        if self.azimuth is not None and not -180 <= self.azimuth <= 180:
            raise ValueError(f"{self.name}: azimuth {self.azimuth} out of range")


def load_locations(path: Path = DEFAULT_LOCATIONS_FILE) -> dict[str, Location]:
    """Load ``[locations.<name>]`` tables.

    Keys: ``latitude``, ``longitude``; optional ``label``, ``tilt``, ``azimuth``.
    """
    with path.open("rb") as fh:
        data = tomllib.load(fh)
    tables = data.get("locations", {})
    if not tables:
        raise ValueError(f"{path} defines no [locations.<name>] tables")
    return {
        name: Location(
            name=name,
            latitude=float(table["latitude"]),
            longitude=float(table["longitude"]),
            label=str(table.get("label", "")),
            tilt=_opt_float(table.get("tilt")),
            azimuth=_opt_float(table.get("azimuth")),
        )
        for name, table in tables.items()
    }


def slug_for(latitude: float, longitude: float) -> str:
    """File-name friendly identifier for ad-hoc coordinates."""
    return re.sub(r"[^0-9a-zA-Z_.-]", "_", f"lat{latitude:.4f}_lon{longitude:.4f}")


def _opt_float(value: object) -> float | None:
    return None if value is None else float(value)  # type: ignore[arg-type]

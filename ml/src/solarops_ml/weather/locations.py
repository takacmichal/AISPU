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

    def __post_init__(self) -> None:
        if not -90 <= self.latitude <= 90:
            raise ValueError(f"{self.name}: latitude {self.latitude} out of range")
        if not -180 <= self.longitude <= 180:
            raise ValueError(f"{self.name}: longitude {self.longitude} out of range")


def load_locations(path: Path = DEFAULT_LOCATIONS_FILE) -> dict[str, Location]:
    """Load ``[locations.<name>]`` tables (``latitude``, ``longitude``, optional ``label``)."""
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
        )
        for name, table in tables.items()
    }


def slug_for(latitude: float, longitude: float) -> str:
    """File-name friendly identifier for ad-hoc coordinates."""
    return re.sub(r"[^0-9a-zA-Z_.-]", "_", f"lat{latitude:.4f}_lon{longitude:.4f}")

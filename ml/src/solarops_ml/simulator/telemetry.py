"""Serialise simulator output to the ingest contract (ADR #185, spike-185-telemetry.schema.json)."""

from __future__ import annotations

import math
import uuid
from typing import Any

import pandas as pd

from solarops_ml.simulator.generator import INTERVAL_S, SimulationResult

SCHEMA_VERSION = "1.0"
MAX_READINGS_PER_BATCH = 288  # maxItems in the ADR JSON schema
SOURCE = "simulator"


def _num(value: Any) -> float | None:
    """NaN becomes ``None`` (JSON null; the contract allows null for measured values)."""
    f = float(value)
    return None if math.isnan(f) else f


def _reading(device_id: str, ts: pd.Timestamp, row: pd.Series, string_count: int) -> dict[str, Any]:
    timestamp = ts.strftime("%Y-%m-%dT%H:%M:%SZ")
    return {
        "deviceId": device_id,
        "timestamp": timestamp,
        "intervalSec": INTERVAL_S,
        "status": str(row["status"]),
        "vendorStatusCode": int(row["error_code"]),
        "acPowerW": _num(row["ac_power_w"]),
        "dcPowerW": _num(row["dc_power_w"]),
        "acVoltageV": _num(row["ac_voltage_v"]),
        "acCurrentA": _num(row["ac_current_a"]),
        "gridFrequencyHz": _num(row["frequency_hz"]),
        "temperatureC": _num(row["temperature_c"]),
        "strings": [
            {
                "index": i,
                "voltageV": _num(row[f"string_{i}_voltage_v"]),
                "currentA": _num(row[f"string_{i}_current_a"]),
                "powerW": _num(row[f"string_{i}_power_w"]),
            }
            for i in range(1, string_count + 1)
        ],
        "totalEnergyWh": float(row["total_wh"]),
        "dayEnergyWh": _num(row["day_wh"]),
    }


def to_payloads(
    result: SimulationResult,
    device_id: str | None = None,
    installation_id: str | None = None,
    sent_at: str | None = None,
) -> list[dict[str, Any]]:
    """Split the readings into ingest batches (<= 288 readings each, ADR #185 schema).

    ``installation_id`` is the UUID of the installation the inverter ``device_id`` is registered in;
    by default it is derived from the device id. ``sent_at`` (UTC, suffix Z) defaults to the
    timestamp of the last reading in the batch, so identical runs give identical payloads. The API
    ignores re-sent readings (key: deviceId + timestamp). Fault labels are NOT included.
    """
    device = device_id or result.config.device_id
    uuid.UUID(device)  # raises ValueError for a non-UUID device id
    installation = installation_id or str(uuid.uuid5(uuid.UUID(device), "installation"))
    uuid.UUID(installation)
    count = result.config.string_count
    readings = [_reading(device, ts, row, count) for ts, row in result.readings.iterrows()]
    batches = [
        readings[i : i + MAX_READINGS_PER_BATCH]
        for i in range(0, len(readings), MAX_READINGS_PER_BATCH)
    ]
    return [
        {
            "schemaVersion": SCHEMA_VERSION,
            "installationId": installation,
            "sentAt": sent_at or batch[-1]["timestamp"],
            "source": SOURCE,
            "readings": batch,
        }
        for batch in batches
    ]

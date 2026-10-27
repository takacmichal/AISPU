"""Serialise simulator output to the telemetry contract of ADR #185 (schemaVersion 1.0)."""

from __future__ import annotations

import math
import uuid
from typing import Any

import pandas as pd

from solarops_ml.simulator.generator import INTERVAL_S, SimulationResult

SCHEMA_VERSION = "1.0"
MAX_READINGS_PER_BATCH = 288  # maxItems in the ADR JSON schema


def _num(value: Any) -> float | None:
    """NaN becomes ``None`` (JSON null; the contract allows null for measured values)."""
    f = float(value)
    return None if math.isnan(f) else f


def _reading(device_id: str, ts: pd.Timestamp, row: pd.Series, string_count: int) -> dict[str, Any]:
    timestamp = ts.strftime("%Y-%m-%dT%H:%M:%SZ")
    return {
        "messageId": str(uuid.uuid5(uuid.UUID(device_id), timestamp)),
        "timestamp": timestamp,
        "intervalSeconds": INTERVAL_S,
        "status": str(row["status"]),
        "acPowerW": _num(row["ac_power_w"]),
        "dcPowerW": _num(row["dc_power_w"]),
        "acVoltageV": _num(row["ac_voltage_v"]),
        "acCurrentA": _num(row["ac_current_a"]),
        "frequencyHz": _num(row["frequency_hz"]),
        "powerFactor": _num(row["power_factor"]),
        "temperatureC": _num(row["temperature_c"]),
        "strings": [
            {
                "id": i,
                "voltageV": _num(row[f"string_{i}_voltage_v"]),
                "currentA": _num(row[f"string_{i}_current_a"]),
                "powerW": _num(row[f"string_{i}_power_w"]),
            }
            for i in range(1, string_count + 1)
        ],
        "energy": {"dayWh": _num(row["day_wh"]), "totalWh": _num(row["total_wh"])},
        "errorCode": int(row["error_code"]),
    }


def to_payloads(result: SimulationResult, device_id: str | None = None) -> list[dict[str, Any]]:
    """Split the readings into ingest batches (<= 288 readings each, ADR #185 schema).

    ``messageId`` is a UUIDv5 of (deviceId, timestamp), so identical runs give identical
    payloads and re-sending a batch is idempotent. Fault labels are NOT included.
    """
    device = device_id or result.config.device_id
    uuid.UUID(device)  # raises ValueError for a non-UUID device id
    count = result.config.string_count
    readings = [_reading(device, ts, row, count) for ts, row in result.readings.iterrows()]
    return [
        {
            "schemaVersion": SCHEMA_VERSION,
            "deviceId": device,
            "readings": readings[i : i + MAX_READINGS_PER_BATCH],
        }
        for i in range(0, len(readings), MAX_READINGS_PER_BATCH)
    ]

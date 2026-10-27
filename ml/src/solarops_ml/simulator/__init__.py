"""PV inverter telemetry simulator (issue #94, ADR #185/#187)."""

from solarops_ml.simulator.generator import (
    FAULT_TYPES,
    INTERVAL_S,
    FaultSpec,
    SimulationResult,
    SimulatorConfig,
    simulate,
)
from solarops_ml.simulator.telemetry import (
    MAX_READINGS_PER_BATCH,
    SCHEMA_VERSION,
    to_payloads,
)

__all__ = [
    "FAULT_TYPES",
    "INTERVAL_S",
    "MAX_READINGS_PER_BATCH",
    "SCHEMA_VERSION",
    "FaultSpec",
    "SimulationResult",
    "SimulatorConfig",
    "simulate",
    "to_payloads",
]

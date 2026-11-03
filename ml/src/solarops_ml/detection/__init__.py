"""Underproduction detection: actual vs predicted production (ADR #194, issues #112-#114).

The detector does not train or call the production model; it takes the predicted
(expected) power as an input series, so it works with any model (#191) or the
simulator's fault-free output.
"""

from solarops_ml.detection.detector import (
    Alarm,
    DetectionResult,
    detect_underproduction,
)
from solarops_ml.detection.hourly import hourly_from_readings
from solarops_ml.detection.rules import (
    EXCLUSION_REASONS,
    DetectionConfig,
    classify_hours,
)

__all__ = [
    "EXCLUSION_REASONS",
    "Alarm",
    "DetectionConfig",
    "DetectionResult",
    "classify_hours",
    "detect_underproduction",
    "hourly_from_readings",
]

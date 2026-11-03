"""FastAPI application of the SolarOps ML service."""

from fastapi import FastAPI

from solarops_ml import __version__
from solarops_ml.detection.api import router as detection_router

app = FastAPI(title="SolarOps ML", version=__version__)
app.include_router(detection_router)


@app.get("/health")
def health() -> dict[str, str]:
    """Liveness check."""
    return {"status": "ok", "version": __version__}

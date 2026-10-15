"""FastAPI application of the SolarOps ML service."""

from fastapi import FastAPI

from solarops_ml import __version__

app = FastAPI(title="SolarOps ML", version=__version__)


@app.get("/health")
def health() -> dict[str, str]:
    """Liveness check."""
    return {"status": "ok", "version": __version__}

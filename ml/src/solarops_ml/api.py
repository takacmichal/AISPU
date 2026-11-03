"""FastAPI application of the SolarOps ML service."""

from uuid import uuid4

from fastapi import FastAPI, Request
from fastapi.exceptions import RequestValidationError
from fastapi.responses import JSONResponse

from solarops_ml import __version__
from solarops_ml.detection.api import router as detection_router
from solarops_ml.prediction.registry import ModelLoadError
from solarops_ml.prediction.service import StoreDep, router

PROBLEM_JSON = "application/problem+json"

app = FastAPI(title="SolarOps ML", version=__version__)
app.include_router(router)
app.include_router(detection_router)


def _problem(status: int, title: str, **extra: object) -> JSONResponse:
    body = {"title": title, "status": status, "traceId": uuid4().hex, **extra}
    return JSONResponse(body, status_code=status, media_type=PROBLEM_JSON)


@app.exception_handler(RequestValidationError)
async def validation_problem(_: Request, exc: RequestValidationError) -> JSONResponse:
    errors = [
        {"loc": [str(part) for part in e["loc"]], "msg": e["msg"], "type": e["type"]}
        for e in exc.errors()
    ]
    return _problem(422, "Invalid request", errors=errors)


@app.exception_handler(ModelLoadError)
async def model_problem(_: Request, exc: ModelLoadError) -> JSONResponse:
    return _problem(503, "Model not available", detail=str(exc))


@app.get("/health")
def health(store: StoreDep) -> JSONResponse:
    """Liveness check; ``modelVersion`` is the model the service predicts with."""
    try:
        body = {"status": "ok", "version": __version__, "modelVersion": store.version()}
        return JSONResponse(body)
    except ModelLoadError as exc:
        body = {"status": "unavailable", "version": __version__, "detail": str(exc)}
        return JSONResponse(body, status_code=503)

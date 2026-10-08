"""FastAPI para consulta local del estado del gemelo desde Unity."""

from __future__ import annotations

import os
from pathlib import Path
from typing import Annotated

from fastapi import Body, FastAPI, HTTPException
from fastapi.responses import FileResponse, RedirectResponse
from fastapi.staticfiles import StaticFiles

from api.manager import SimulationManager
from api.schemas import ActionResponse, StartRequest

PROJECT_ROOT = Path(__file__).resolve().parents[1]


def _configured_path(environment_name: str, default: Path) -> Path:
    configured = Path(os.environ.get(environment_name, str(default)))
    return configured if configured.is_absolute() else PROJECT_ROOT / configured


def _configured_non_negative_float(environment_name: str, default: float) -> float:
    configured = float(os.environ.get(environment_name, str(default)))
    if configured < 0:
        raise ValueError(f"{environment_name} must be greater than or equal to zero")
    return configured


CONFIG_PATH = _configured_path("PET_CONFIG_PATH", PROJECT_ROOT / "data" / "input_pet.json")
RESULTS_DIR = _configured_path("PET_RESULTS_DIR", PROJECT_ROOT / "results")
DASHBOARD_PATH = PROJECT_ROOT / "dashboard" / "dist" / "index.html"
UNITY_WEBGL_DIR = _configured_path("PET_UNITY_WEBGL_DIR", PROJECT_ROOT / "unity" / "webgl")
STEP_DELAY_SECONDS = _configured_non_negative_float("PET_STEP_DELAY_SECONDS", 0.02)
manager = SimulationManager(CONFIG_PATH, RESULTS_DIR, step_delay=STEP_DELAY_SECONDS)

app = FastAPI(
    title="PET Digital Twin API",
    version="0.1.0",
    description="Interfaz local para controlar y observar la simulacion PET.",
)


@app.get("/", include_in_schema=False)
def dashboard_redirect() -> RedirectResponse:
    return RedirectResponse(url="/dashboard")


@app.get("/dashboard", include_in_schema=False)
def get_dashboard() -> FileResponse:
    return FileResponse(DASHBOARD_PATH)


@app.get("/config")
def get_config() -> dict:
    return manager.config_payload()


@app.get("/status")
def get_status() -> dict:
    return manager.status_payload()


@app.get("/machines")
def get_machines() -> list[dict]:
    return manager.machine_states_payload()


@app.get("/machines/{machine_id}")
def get_machine(machine_id: str) -> dict:
    machine = manager.machine_payload(machine_id)
    if machine is None:
        raise HTTPException(status_code=404, detail=f"Maquina no encontrada: {machine_id}")
    return machine


@app.get("/kpis")
def get_kpis() -> dict:
    return manager.kpis_payload()


@app.get("/simulation/time")
def get_simulation_time() -> dict:
    status = manager.status_payload()
    return {"simulation_time": status["simulation_time"], "unit": status["time_unit"]}


@app.post("/simulation/start", response_model=ActionResponse)
def start_simulation(
    request: Annotated[StartRequest | None, Body()] = None,
) -> ActionResponse:
    try:
        status = manager.start(request.config if request else None)
    except (TypeError, ValueError, KeyError) as exc:
        raise HTTPException(status_code=422, detail=str(exc)) from exc
    return ActionResponse(status=status)


@app.post("/simulation/pause", response_model=ActionResponse)
def pause_simulation() -> ActionResponse:
    return ActionResponse(status=manager.pause())


@app.post("/simulation/reset", response_model=ActionResponse)
def reset_simulation() -> ActionResponse:
    return ActionResponse(status=manager.reset())


# Build WebGL de Unity publicado en /unity/. Es un artefacto estatico, ajeno al
# contrato JSON: si la carpeta no existe la API responde igual y el dashboard
# informa que el visor no esta disponible.
if UNITY_WEBGL_DIR.is_dir():
    app.mount(
        "/unity",
        StaticFiles(directory=str(UNITY_WEBGL_DIR), html=True),
        name="unity-webgl",
    )

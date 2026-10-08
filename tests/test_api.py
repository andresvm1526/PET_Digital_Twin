from __future__ import annotations

import json
import time
from pathlib import Path

from fastapi.testclient import TestClient

import api.main as api_main
from api.manager import SimulationManager


def test_api_lifecycle(small_config_dict: dict, tmp_path: Path, monkeypatch) -> None:
    config_path = tmp_path / "input.json"
    config_path.write_text(json.dumps(small_config_dict), encoding="utf-8")
    test_manager = SimulationManager(config_path, tmp_path / "results", step_delay=0)
    monkeypatch.setattr(api_main, "manager", test_manager)
    client = TestClient(api_main.app)

    assert client.get("/").status_code == 200
    assert client.get("/dashboard").status_code == 200
    assert client.get("/config").json()["demanda"] == small_config_dict["demanda"]
    assert client.get("/status").json()["status"] == "READY"
    machines = client.get("/machines").json()
    assert machines
    assert client.get(f"/machines/{machines[0]['machine_id']}").status_code == 200
    assert client.get("/machines/DOES-NOT-EXIST").status_code == 404

    response = client.post("/simulation/start", json={})
    assert response.status_code == 200
    deadline = time.monotonic() + 3
    while client.get("/status").json()["status"] != "FINISHED" and time.monotonic() < deadline:
        time.sleep(0.01)
    assert client.get("/status").json()["status"] == "FINISHED"
    assert client.get("/kpis").json()["production_total"] == small_config_dict["demanda"]
    assert client.get("/simulation/time").json()["simulation_time"] > 0
    assert client.post("/simulation/reset").json()["status"] == "READY"


def test_unity_webgl_static_mount() -> None:
    """El build WebGL se publica en /unity/ sin alterar el contrato JSON."""
    client = TestClient(api_main.app)

    assert api_main.UNITY_WEBGL_DIR.is_dir()

    assets = client.get("/unity/README.md")
    assert assets.status_code == 200
    assert "WebGL" in assets.text

    # Sin index.html el visor no esta disponible y el dashboard muestra el respaldo.
    assert client.get("/unity/index.html").status_code == 404
    assert client.get("/unity/").status_code == 404

    # El montaje no interfiere con las rutas de datos.
    assert client.get("/status").status_code == 200
    assert client.get("/machines").status_code == 200
    assert client.get("/dashboard").status_code == 200

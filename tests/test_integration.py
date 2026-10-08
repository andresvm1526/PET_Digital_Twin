from __future__ import annotations

import csv
import json
import math
from pathlib import Path

import pytest
from fastapi.testclient import TestClient
from jsonschema import Draft202012Validator

import api.main as api_main
from api.manager import SimulationManager
from simulation.config import SimulationConfig
from simulation.constants import OFFICIAL_MACHINE_IDS
from simulation.model import PETSimulation


ROOT = Path(__file__).resolve().parents[1]
CONTRACTS = ROOT / "contracts"
INPUT_PATH = ROOT / "data" / "input_pet.json"


def validate(instance: object, contract_name: str) -> None:
    schema = json.loads((CONTRACTS / contract_name).read_text(encoding="utf-8"))
    Draft202012Validator(schema).validate(instance)


@pytest.fixture(scope="module")
def completed_run(tmp_path_factory: pytest.TempPathFactory) -> tuple[PETSimulation, Path]:
    output_dir = tmp_path_factory.mktemp("results")
    model = PETSimulation(SimulationConfig.from_json(INPUT_PATH))
    model.run()
    model.write_results(output_dir)
    return model, output_dir


def test_input_matches_contract() -> None:
    validate(json.loads(INPUT_PATH.read_text(encoding="utf-8")), "simulation_input.schema.json")


def test_simulation_uses_exact_official_ids(completed_run: tuple[PETSimulation, Path]) -> None:
    model, _ = completed_run
    assert tuple(model.machines) == OFFICIAL_MACHINE_IDS
    assert {event.machine_id for event in model.events} == set(OFFICIAL_MACHINE_IDS)


def test_results_and_contracts(completed_run: tuple[PETSimulation, Path]) -> None:
    model, output_dir = completed_run
    expected = {"events.csv", "machine_states.csv", "kpis.json"}
    assert expected == {path.name for path in output_dir.iterdir()}

    kpis = json.loads((output_dir / "kpis.json").read_text(encoding="utf-8"))
    validate(kpis, "kpis.schema.json")
    for state in model.latest_machine_states():
        validate(state, "machine_state.schema.json")

    with (output_dir / "machine_states.csv").open(newline="", encoding="utf-8") as file:
        rows = list(csv.DictReader(file))
    assert rows
    assert set(row["machine_id"] for row in rows) == set(model.machines)


def test_physical_kpi_invariants(completed_run: tuple[PETSimulation, Path]) -> None:
    model, _ = completed_run
    kpis = model.kpis
    assert kpis["production_total"] == kpis["good_production"] + kpis["rejected_units"]
    assert kpis["production_total"] == kpis["demand"] == model.config.demanda
    assert kpis["rejected_units"] == sum(kpis["rejections_by_stage"].values())
    assert math.isclose(
        kpis["throughput_units_per_hour"],
        round(kpis["good_production"] / (model.simulation_time / 3600), 4),
        rel_tol=1e-9,
    )
    assert 0 <= kpis["scrap_percent"] <= 100
    assert 0 <= kpis["demand_compliance_percent"] <= 100
    assert 0 <= kpis["average_wip_units"] <= kpis["demand"]
    assert 0 <= kpis["average_lead_time_seconds"] <= kpis["order_completion_time_seconds"]
    assert kpis["average_queue_time_seconds"] >= 0
    assert kpis["bottleneck"]["machine_type"] in {
        machine.machine_type for machine in model.machines.values()
    }
    for metrics in kpis["machine_metrics"].values():
        assert 0 <= metrics["utilization_percent"] <= 100
        assert 0 <= metrics["availability_percent"] <= 100
        assert metrics["downtime_seconds"] >= 0
        assert metrics["average_queue_length"] >= 0


def test_fastapi_serves_persisted_simpy_run(
    completed_run: tuple[PETSimulation, Path], monkeypatch: pytest.MonkeyPatch
) -> None:
    model, output_dir = completed_run
    manager = SimulationManager(INPUT_PATH, output_dir)
    monkeypatch.setattr(api_main, "manager", manager)

    with TestClient(api_main.app) as client:
        status_response = client.get("/status")
        machines_response = client.get("/machines")
        kpis_response = client.get("/kpis")

    assert status_response.status_code == 200
    assert machines_response.status_code == 200
    assert kpis_response.status_code == 200
    status = status_response.json()
    machines = machines_response.json()
    kpis = kpis_response.json()
    validate(status, "status.schema.json")
    assert status["status"] == "FINISHED"
    assert status["simulation_time"] == pytest.approx(model.simulation_time)
    assert [state["machine_id"] for state in machines] == list(model.machines)
    for state in machines:
        validate(state, "machine_state.schema.json")
    validate(kpis, "kpis.schema.json")
    assert kpis == model.kpis


def test_unity_dtos_and_array_adapter_match_api_names() -> None:
    dto = (ROOT / "unity/PET_DigitalTwin/Assets/Scripts/Core/MachineState.cs").read_text(encoding="utf-8")
    client = (ROOT / "unity/PET_DigitalTwin/Assets/Scripts/API/DigitalTwinApiClient.cs").read_text(encoding="utf-8")
    for field in (
        "machine_id", "machine_type", "state", "simulation_time", "progress",
        "queue", "temperature", "pressure", "produced", "rejected",
        "production_total", "good_production", "rejected_units",
        "throughput_units_per_hour", "average_wip_units", "demand_compliance_percent",
    ):
        assert field in dto
    assert "Regex.Replace(" in client
    assert 'string wrapped = "{\\"machines\\":" + normalized + "}";' in client

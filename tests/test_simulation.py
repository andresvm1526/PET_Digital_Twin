from __future__ import annotations

import csv
import json
from pathlib import Path

from jsonschema import validate

from simulation.config import SimulationConfig
from simulation.model import PETSimulation


PROJECT_ROOT = Path(__file__).resolve().parents[1]


def test_end_to_end_flow_and_results(small_config_dict: dict, tmp_path: Path) -> None:
    model = PETSimulation(SimulationConfig.from_dict(small_config_dict))
    kpis = model.run()
    model.write_results(tmp_path)

    assert kpis["production_total"] == small_config_dict["demanda"]
    assert kpis["good_production"] == small_config_dict["demanda"]
    assert kpis["rejected_units"] == 0
    assert kpis["demand_compliance_percent"] == 100
    assert kpis["order_completion_time_seconds"] > 0
    assert {"PROCESS_START", "PROCESS_END", "WAIT_START", "WAIT_END", "RESOURCE_RELEASE"} <= {
        event.event_type for event in model.events
    }
    assert {"PETLot", "Preform", "PETBottle"} <= {event.entity_type for event in model.events}
    assert len(model.pools["injection"]) == 1
    assert len(model.pools["blow_molding"]) == 1
    assert model.pools["injection"][0].machine_id == "IMM-101"
    assert model.pools["injection"][0].resource_capacity == small_config_dict["numero_maquinas_inyeccion"]
    assert model.pools["blow_molding"][0].resource_capacity == small_config_dict["numero_maquinas_soplado"]

    for name in ("events.csv", "machine_states.csv", "kpis.json"):
        assert (tmp_path / name).is_file()
    with (tmp_path / "events.csv").open(encoding="utf-8") as file:
        assert len(list(csv.DictReader(file))) > small_config_dict["demanda"]


def test_machine_state_conforms_to_contract(small_config_dict: dict) -> None:
    model = PETSimulation(SimulationConfig.from_dict(small_config_dict))
    model.run()
    schema = json.loads((PROJECT_ROOT / "contracts" / "machine_state.schema.json").read_text(encoding="utf-8"))
    for state in model.latest_machine_states():
        validate(instance=state, schema=schema)


def test_failures_add_downtime(small_config_dict: dict) -> None:
    small_config_dict["demanda"] = 1
    small_config_dict["MTBF"] = 0.00001
    small_config_dict["MTTR"] = 0.00001
    model = PETSimulation(SimulationConfig.from_dict(small_config_dict))
    model.run()
    assert any(event.event_type == "FAILURE" for event in model.events)
    assert sum(machine.downtime for machine in model.machines.values()) > 0

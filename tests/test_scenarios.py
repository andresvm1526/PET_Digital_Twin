from __future__ import annotations

import csv
from pathlib import Path

from simulation.config import SimulationConfig
from simulation.scenarios import build_scenarios, run_scenarios


def test_builds_required_base_and_sensitivity_scenarios(small_config_dict: dict) -> None:
    scenarios = build_scenarios(SimulationConfig.from_dict(small_config_dict), demand=100)

    assert [scenario.name for scenario in scenarios[:3]] == [
        "A_1_inyectora",
        "B_2_inyectoras",
        "C_3_inyectoras",
    ]
    assert {scenario.group for scenario in scenarios} >= {
        "tiempo_ciclo",
        "scrap",
        "capacidad_secador",
        "numero_sopladoras",
        "MTBF",
        "MTTR",
    }
    assert all(scenario.config.demanda == 100 for scenario in scenarios)


def test_runs_scenarios_and_exports_comparison(small_config_dict: dict, tmp_path: Path) -> None:
    output = tmp_path / "scenario_comparison.csv"
    comparison = run_scenarios(
        SimulationConfig.from_dict(small_config_dict), demand=12, output_path=output
    )

    assert output.is_file()
    assert len(comparison.rows) == 15
    assert comparison.best_injection_capacity in {1, 2, 3}
    assert comparison.recommended_scenario in {row["escenario"] for row in comparison.rows}
    assert comparison.limiting_resource

    with output.open(encoding="utf-8", newline="") as file:
        rows = list(csv.DictReader(file))
    assert len(rows) == 15
    assert {
        "throughput_unidades_hora",
        "lead_time_promedio_horas",
        "produccion_buena",
        "scrap_unidades",
        "cumplimiento_demanda_porcentaje",
        "wip_promedio_unidades",
        "cuello_de_botella",
        "mejor_capacidad_inyeccion",
        "recurso_limitante_recomendado",
        "configuracion_sobredimensionada",
        "escenario_recomendado",
    } <= set(rows[0])

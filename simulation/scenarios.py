"""Comparacion automatizada de capacidad y sensibilidad para la linea PET.

El modulo reutiliza el modelo discreto existente.  Las corridas comparativas no
retienen el detalle de eventos por pieza, ya que ese detalle no participa en los
KPIs y consumiría una cantidad de memoria desproporcionada para 100 000 unidades.
"""

from __future__ import annotations

import argparse
import csv
import json
from dataclasses import dataclass, replace
from pathlib import Path
from typing import Any

from simulation.config import SimulationConfig, TimeDistribution
from simulation.machine import Machine
from simulation.model import PETSimulation


DEFAULT_DEMAND = 100_000
PERFORMANCE_TOLERANCE = 0.01
PROJECT_ROOT = Path(__file__).resolve().parents[1]
DEFAULT_CONFIG_PATH = PROJECT_ROOT / "data" / "input_pet.json"
DEFAULT_OUTPUT_PATH = PROJECT_ROOT / "results" / "scenario_comparison.csv"


@dataclass(frozen=True, slots=True)
class ScenarioDefinition:
    """Configuracion inmutable y metadatos de una corrida comparativa."""

    name: str
    group: str
    description: str
    config: SimulationConfig
    cycle_time_factor: float = 1.0
    scrap_factor: float = 1.0
    dryer_capacity_factor: float = 1.0


@dataclass(frozen=True, slots=True)
class ScenarioComparison:
    """Resultado de todas las corridas y de las reglas de seleccion aplicadas."""

    rows: tuple[dict[str, Any], ...]
    best_injection_capacity: int
    limiting_resource: str | None
    recommended_scenario: str
    output_path: Path


class _ScenarioSimulation(PETSimulation):
    """Modelo PET sin historico exhaustivo, adecuado para analisis masivos.

    Se conserva el calculo de areas de cola requerido por los KPIs.  Solo se
    omiten eventos y snapshots que no se exportan en una comparacion agregada.
    """

    def _record_event(self, *args: Any, **kwargs: Any) -> None:
        return None

    def _snapshot(self, machine: Machine) -> None:
        machine.update_queue_area()


def _scale_time(spec: TimeDistribution, factor: float) -> TimeDistribution:
    """Escala todos los parametros temporales, conservando distribucion y unidad."""

    values = {
        name: value * factor if value is not None else None
        for name, value in (
            ("value", spec.value),
            ("mean", spec.mean),
            ("stddev", spec.stddev),
            ("min", spec.min),
            ("mode", spec.mode),
            ("max", spec.max),
        )
    }
    return replace(spec, **values)


def _scale_cycle_time(config: SimulationConfig, factor: float) -> SimulationConfig:
    """Modifica los ciclos de las operaciones que definen la transformacion PET."""

    return replace(
        config,
        tiempo_inyeccion=_scale_time(config.tiempo_inyeccion, factor),
        tiempo_soplado=_scale_time(config.tiempo_soplado, factor),
    )


def _scale_scrap(config: SimulationConfig, factor: float) -> SimulationConfig:
    return replace(
        config,
        scrap_inyeccion=min(1.0, config.scrap_inyeccion * factor),
        scrap_soplado=min(1.0, config.scrap_soplado * factor),
        scrap_calidad=min(1.0, config.scrap_calidad * factor),
    )


def _reliability_levels(value: float, *, lower_when_disabled: float, upper_when_disabled: float) -> tuple[float, float]:
    """Genera niveles de sensibilidad aun si la confiabilidad esta desactivada."""

    if value > 0:
        return value * 0.5, value * 1.5
    return lower_when_disabled, upper_when_disabled


def build_scenarios(base_config: SimulationConfig, demand: int = DEFAULT_DEMAND) -> list[ScenarioDefinition]:
    """Construye tres capacidades de inyeccion y sensibilidades de un factor.

    Las sensibilidades parten de la configuracion B (dos inyectoras), para no
    mezclar en una misma fila el efecto de dos cambios distintos.  El tiempo de
    ciclo escala inyeccion y soplado; el scrap escala los tres puntos de rechazo.
    """

    if demand <= 0:
        raise ValueError("La demanda del analisis debe ser positiva")

    reference = replace(base_config, demanda=demand, numero_maquinas_inyeccion=2)
    mtbf_low, mtbf_high = _reliability_levels(
        reference.MTBF, lower_when_disabled=1.0, upper_when_disabled=4.0
    )
    mttr_low, mttr_high = _reliability_levels(
        reference.MTTR, lower_when_disabled=0.05, upper_when_disabled=0.15
    )

    scenarios = [
        ScenarioDefinition(
            "A_1_inyectora",
            "capacidad_inyeccion",
            "Escenario base A: una inyectora.",
            replace(reference, numero_maquinas_inyeccion=1),
        ),
        ScenarioDefinition(
            "B_2_inyectoras",
            "capacidad_inyeccion",
            "Escenario base B: dos inyectoras.",
            reference,
        ),
        ScenarioDefinition(
            "C_3_inyectoras",
            "capacidad_inyeccion",
            "Escenario base C: tres inyectoras.",
            replace(reference, numero_maquinas_inyeccion=3),
        ),
        ScenarioDefinition(
            "ciclo_20_por_ciento_menor",
            "tiempo_ciclo",
            "Ciclo de inyeccion y soplado 20 % menor respecto a B.",
            _scale_cycle_time(reference, 0.8),
            cycle_time_factor=0.8,
        ),
        ScenarioDefinition(
            "ciclo_20_por_ciento_mayor",
            "tiempo_ciclo",
            "Ciclo de inyeccion y soplado 20 % mayor respecto a B.",
            _scale_cycle_time(reference, 1.2),
            cycle_time_factor=1.2,
        ),
        ScenarioDefinition(
            "scrap_50_por_ciento_menor",
            "scrap",
            "Scrap de inyeccion, soplado y calidad 50 % menor respecto a B.",
            _scale_scrap(reference, 0.5),
            scrap_factor=0.5,
        ),
        ScenarioDefinition(
            "scrap_50_por_ciento_mayor",
            "scrap",
            "Scrap de inyeccion, soplado y calidad 50 % mayor respecto a B.",
            _scale_scrap(reference, 1.5),
            scrap_factor=1.5,
        ),
        ScenarioDefinition(
            "secador_50_por_ciento_menor",
            "capacidad_secador",
            "Capacidad del secador 50 % menor respecto a B.",
            replace(reference, capacidad_secador=reference.capacidad_secador * 0.5),
            dryer_capacity_factor=0.5,
        ),
        ScenarioDefinition(
            "secador_50_por_ciento_mayor",
            "capacidad_secador",
            "Capacidad del secador 50 % mayor respecto a B.",
            replace(reference, capacidad_secador=reference.capacidad_secador * 1.5),
            dryer_capacity_factor=1.5,
        ),
        ScenarioDefinition(
            "sopladora_1",
            "numero_sopladoras",
            "Una sopladora; referencia B en los demas parametros.",
            replace(reference, numero_maquinas_soplado=1),
        ),
        ScenarioDefinition(
            "sopladora_3",
            "numero_sopladoras",
            "Tres sopladoras; referencia B en los demas parametros.",
            replace(reference, numero_maquinas_soplado=3),
        ),
        ScenarioDefinition(
            "mtbf_50_por_ciento_menor",
            "MTBF",
            "MTBF 50 % menor respecto a B.",
            replace(reference, MTBF=mtbf_low),
        ),
        ScenarioDefinition(
            "mtbf_50_por_ciento_mayor",
            "MTBF",
            "MTBF 50 % mayor respecto a B.",
            replace(reference, MTBF=mtbf_high),
        ),
        ScenarioDefinition(
            "mttr_50_por_ciento_menor",
            "MTTR",
            "MTTR 50 % menor respecto a B.",
            replace(reference, MTTR=mttr_low),
        ),
        ScenarioDefinition(
            "mttr_50_por_ciento_mayor",
            "MTTR",
            "MTTR 50 % mayor respecto a B.",
            replace(reference, MTTR=mttr_high),
        ),
    ]
    return scenarios


def _resource_utilization(model: PETSimulation) -> dict[str, float]:
    elapsed = model.simulation_time
    by_type: dict[str, list[float]] = {}
    for machine in model.machines.values():
        capacity_time = elapsed * machine.resource_capacity
        utilization = 100 * machine.busy_time / capacity_time if capacity_time else 0.0
        by_type.setdefault(machine.machine_type, []).append(utilization)
    return {name: round(sum(values) / len(values), 4) for name, values in sorted(by_type.items())}


def _run_scenario(definition: ScenarioDefinition) -> dict[str, Any]:
    model = _ScenarioSimulation(definition.config)
    kpis = model.run()
    utilization_by_resource = _resource_utilization(model)
    process_utilizations = [
        value
        for resource, value in utilization_by_resource.items()
        if resource not in {"buffer", "preform_buffer", "material_hopper"}
    ]

    return {
        "escenario": definition.name,
        "grupo": definition.group,
        "descripcion": definition.description,
        "demanda_preformas": definition.config.demanda,
        "numero_inyectoras": definition.config.numero_maquinas_inyeccion,
        "numero_sopladoras": definition.config.numero_maquinas_soplado,
        "capacidad_secador_kg": definition.config.capacidad_secador,
        "factor_tiempo_ciclo": definition.cycle_time_factor,
        "factor_scrap": definition.scrap_factor,
        "factor_capacidad_secador": definition.dryer_capacity_factor,
        "mtbf_horas": definition.config.MTBF,
        "mttr_horas": definition.config.MTTR,
        "throughput_unidades_hora": kpis["throughput_units_per_hour"],
        "lead_time_promedio_horas": round(kpis["average_lead_time_seconds"] / 3600, 4),
        "utilizacion_promedio_proceso_porcentaje": round(
            sum(process_utilizations) / len(process_utilizations), 4
        ) if process_utilizations else 0.0,
        "utilizacion_maxima_porcentaje": max(utilization_by_resource.values(), default=0.0),
        "utilizacion_por_recurso_porcentaje": json.dumps(utilization_by_resource, ensure_ascii=False, sort_keys=True),
        "produccion_buena": kpis["good_production"],
        "scrap_unidades": kpis["rejected_units"],
        "scrap_porcentaje": kpis["scrap_percent"],
        "cumplimiento_demanda_porcentaje": kpis["demand_compliance_percent"],
        "wip_promedio_unidades": kpis["average_wip_units"],
        "cuello_de_botella": kpis["bottleneck"]["machine_type"],
        "utilizacion_cuello_de_botella_porcentaje": kpis["bottleneck"]["average_utilization_percent"],
        "tiempo_total_horas": kpis["order_completion_time_hours"],
        "configuracion_sobredimensionada": False,
        "motivo_sobredimensionamiento": "",
    }


def _select_best_injection_capacity(rows: list[dict[str, Any]]) -> int:
    capacity_rows = [row for row in rows if row["grupo"] == "capacidad_inyeccion"]
    maximum = max(row["throughput_unidades_hora"] for row in capacity_rows)
    adequate = [
        row for row in capacity_rows
        if row["throughput_unidades_hora"] >= maximum * (1 - PERFORMANCE_TOLERANCE)
    ]
    return min(adequate, key=lambda row: row["numero_inyectoras"])["numero_inyectoras"]


def _mark_oversized_configurations(rows: list[dict[str, Any]], best_injection_capacity: int) -> None:
    baseline = next(row for row in rows if row["escenario"] == "B_2_inyectoras")
    for row in rows:
        reasons: list[str] = []
        if row["grupo"] == "capacidad_inyeccion" and row["numero_inyectoras"] > best_injection_capacity:
            reasons.append("Capacidad de inyeccion superior a la necesaria para el plateau de throughput")
        if (
            row["grupo"] == "numero_sopladoras"
            and row["numero_sopladoras"] > baseline["numero_sopladoras"]
            and row["throughput_unidades_hora"] < baseline["throughput_unidades_hora"] * (1 + PERFORMANCE_TOLERANCE)
        ):
            reasons.append("Sopladora adicional sin mejora de throughput de al menos 1 %")
        if (
            row["grupo"] == "capacidad_secador"
            and row["capacidad_secador_kg"] > baseline["capacidad_secador_kg"]
            and row["throughput_unidades_hora"] < baseline["throughput_unidades_hora"] * (1 + PERFORMANCE_TOLERANCE)
        ):
            reasons.append("Capacidad adicional de secado sin mejora de throughput de al menos 1 %")
        row["configuracion_sobredimensionada"] = bool(reasons)
        row["motivo_sobredimensionamiento"] = "; ".join(reasons)


def _select_recommended_scenario(rows: list[dict[str, Any]]) -> dict[str, Any]:
    """Prioriza servicio, flujo y eficiencia operativa; no usa costos."""

    return max(
        (row for row in rows if not row["configuracion_sobredimensionada"]),
        key=lambda row: (
            row["cumplimiento_demanda_porcentaje"],
            row["throughput_unidades_hora"],
            -row["lead_time_promedio_horas"],
            -row["wip_promedio_unidades"],
        ),
    )


def _write_comparison(rows: list[dict[str, Any]], output_path: Path) -> None:
    output_path.parent.mkdir(parents=True, exist_ok=True)
    fields = list(rows[0])
    with output_path.open("w", newline="", encoding="utf-8") as file:
        writer = csv.DictWriter(file, fieldnames=fields)
        writer.writeheader()
        writer.writerows(rows)


def run_scenarios(
    base_config: SimulationConfig | None = None,
    *,
    demand: int = DEFAULT_DEMAND,
    output_path: str | Path = DEFAULT_OUTPUT_PATH,
) -> ScenarioComparison:
    """Ejecuta escenarios, genera el CSV y devuelve su diagnostico agregado."""

    config = base_config or SimulationConfig.from_json(DEFAULT_CONFIG_PATH)
    rows = [_run_scenario(definition) for definition in build_scenarios(config, demand)]
    best_injection_capacity = _select_best_injection_capacity(rows)
    _mark_oversized_configurations(rows, best_injection_capacity)
    recommended = _select_recommended_scenario(rows)
    limiting_resource = recommended["cuello_de_botella"]

    for row in rows:
        row["mejor_capacidad_inyeccion"] = best_injection_capacity
        row["recurso_limitante_recomendado"] = limiting_resource
        row["escenario_recomendado"] = recommended["escenario"]
        row["es_escenario_recomendado"] = row["escenario"] == recommended["escenario"]

    target = Path(output_path)
    _write_comparison(rows, target)
    return ScenarioComparison(
        rows=tuple(rows),
        best_injection_capacity=best_injection_capacity,
        limiting_resource=limiting_resource,
        recommended_scenario=recommended["escenario"],
        output_path=target,
    )


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Comparar escenarios de manufactura PET")
    parser.add_argument("--config", default=str(DEFAULT_CONFIG_PATH), help="Archivo JSON de entrada")
    parser.add_argument("--output", default=str(DEFAULT_OUTPUT_PATH), help="CSV comparativo de salida")
    parser.add_argument("--demand", type=int, default=DEFAULT_DEMAND, help="Demanda de preformas a simular")
    return parser


def main() -> None:
    args = build_parser().parse_args()
    comparison = run_scenarios(
        SimulationConfig.from_json(args.config), demand=args.demand, output_path=args.output
    )
    print(
        json.dumps(
            {
                "output": str(comparison.output_path),
                "best_injection_capacity": comparison.best_injection_capacity,
                "limiting_resource": comparison.limiting_resource,
                "recommended_scenario": comparison.recommended_scenario,
            },
            ensure_ascii=False,
            indent=2,
        )
    )


if __name__ == "__main__":
    main()

"""Calculo centralizado de indicadores, separado de la logica de eventos."""

from __future__ import annotations

from collections import defaultdict
from typing import TYPE_CHECKING, Any

if TYPE_CHECKING:
    from simulation.model import PETSimulation


def _round(value: float) -> float:
    return round(value, 4)


def calculate_kpis(model: "PETSimulation") -> dict[str, Any]:
    elapsed = model.simulation_time
    elapsed_hours = elapsed / 3600
    machine_metrics: dict[str, dict[str, float]] = {}
    utilization_by_type: dict[str, list[float]] = defaultdict(list)
    queue_by_type: dict[str, list[float]] = defaultdict(list)

    for machine in model.machines.values():
        capacity_time = elapsed * machine.resource_capacity
        utilization = machine.busy_time / capacity_time if capacity_time else 0.0
        availability = 1.0 - (machine.downtime / capacity_time) if capacity_time else 1.0
        average_queue = machine.queue_area / elapsed if elapsed else 0.0
        utilization_by_type[machine.machine_type].append(utilization)
        queue_by_type[machine.machine_type].append(average_queue)
        machine_metrics[machine.machine_id] = {
            "utilization_percent": _round(100 * utilization),
            "availability_percent": _round(100 * max(0.0, availability)),
            "downtime_seconds": _round(machine.downtime),
            "average_queue_length": _round(average_queue),
        }

    aggregate = {
        machine_type: sum(values) / len(values)
        for machine_type, values in utilization_by_type.items()
    }
    bottleneck_type = max(aggregate, key=aggregate.get) if aggregate else None
    total = model.good_units + model.rejected_units
    return {
        "production_total": total,
        "good_production": model.good_units,
        "rejected_units": model.rejected_units,
        "rejections_by_stage": model.rejections_by_stage,
        "scrap_percent": _round(100 * model.rejected_units / total) if total else 0.0,
        "throughput_units_per_hour": _round(model.good_units / elapsed_hours) if elapsed_hours else 0.0,
        "average_lead_time_seconds": _round(sum(model.lead_times) / len(model.lead_times)) if model.lead_times else 0.0,
        "average_queue_time_seconds": _round(sum(model.queue_times) / len(model.queue_times)) if model.queue_times else 0.0,
        "average_wip_units": _round(model.wip_area / elapsed) if elapsed else 0.0,
        "machine_metrics": machine_metrics,
        "bottleneck": {
            "machine_type": bottleneck_type,
            "average_utilization_percent": _round(100 * aggregate[bottleneck_type]) if bottleneck_type else 0.0,
            "average_queue_length": _round(sum(queue_by_type[bottleneck_type]) / len(queue_by_type[bottleneck_type])) if bottleneck_type else 0.0,
        },
        "demand": model.config.demanda,
        "demand_compliance_percent": _round(100 * model.good_units / model.config.demanda),
        "order_completion_time_seconds": _round(elapsed),
        "order_completion_time_hours": _round(elapsed_hours),
        "scheduled_time_seconds": _round(model.config.horizonte_turnos_seconds),
        "completed_within_scheduled_time": elapsed <= model.config.horizonte_turnos_seconds,
        "extensions": {
            "oee": None,
            "energy_consumption_kwh": None,
            "cost_per_piece": None,
            "co2_emissions_kg": None
        }
    }

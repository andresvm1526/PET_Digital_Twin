"""Registros tabulares para trazabilidad y publicacion del estado."""

from __future__ import annotations

from dataclasses import asdict, dataclass
from typing import Any


@dataclass(slots=True)
class SimulationEvent:
    simulation_time: float
    event_type: str
    entity_id: str
    entity_type: str
    machine_id: str
    stage: str
    queue: int
    details: str = ""

    def as_dict(self) -> dict[str, Any]:
        return asdict(self)


@dataclass(slots=True)
class MachineState:
    machine_id: str
    machine_type: str
    state: str
    simulation_time: float
    progress: float
    queue: int
    temperature: float | None
    pressure: float | None
    produced: int
    rejected: int

    def as_dict(self) -> dict[str, Any]:
        return asdict(self)


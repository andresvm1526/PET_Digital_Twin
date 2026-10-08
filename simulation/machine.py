"""Recurso fisico y sus acumuladores de confiabilidad."""

from __future__ import annotations

from dataclasses import dataclass, field

import simpy

from simulation.events import MachineState


VALID_MACHINE_STATES = {"IDLE", "WAITING", "RUNNING", "FAULT", "MAINTENANCE", "FINISHED"}


@dataclass(slots=True)
class Machine:
    env: simpy.Environment
    machine_id: str
    machine_type: str
    temperature: float | None = None
    pressure: float | None = None
    resource_capacity: int = 1
    resource: simpy.Resource = field(init=False)
    state: str = "IDLE"
    progress: float = 0.0
    produced: int = 0
    rejected: int = 0
    busy_time: float = 0.0
    downtime: float = 0.0
    queue_area: float = 0.0
    _queue_last_time: float = 0.0
    _queue_last_value: int = 0

    def __post_init__(self) -> None:
        self.resource = simpy.Resource(self.env, capacity=self.resource_capacity)

    @property
    def queue_length(self) -> int:
        if self.machine_type == "preform_buffer":
            return self.resource.count
        return len(self.resource.queue)

    def update_queue_area(self) -> None:
        now = float(self.env.now)
        self.queue_area += self._queue_last_value * (now - self._queue_last_time)
        self._queue_last_time = now
        self._queue_last_value = self.queue_length

    def snapshot(self) -> MachineState:
        return MachineState(
            machine_id=self.machine_id,
            machine_type=self.machine_type,
            state=self.state,
            simulation_time=round(float(self.env.now), 6),
            progress=round(self.progress, 6),
            queue=self.queue_length,
            temperature=self.temperature,
            pressure=self.pressure,
            produced=self.produced,
            rejected=self.rejected,
        )

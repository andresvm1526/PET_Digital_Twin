"""Modelo SimPy de extremo a extremo para la linea de envases PET."""

from __future__ import annotations

import csv
import json
import logging
import math
import random
from collections.abc import Generator
from pathlib import Path
from typing import Any

import simpy

from simulation.config import SimulationConfig, TimeDistribution
from simulation.distributions import sample_seconds
from simulation.entities import PETBottle, PETLot, Preform
from simulation.events import MachineState, SimulationEvent
from simulation.machine import Machine

LOGGER = logging.getLogger(__name__)


class PETSimulation:
    """Gemelo discreto; toda magnitud temporal interna se expresa en segundos."""

    def __init__(self, config: SimulationConfig) -> None:
        self.config = config
        self.env = simpy.Environment()
        self.rng = random.Random(config.random_seed)
        self.events: list[SimulationEvent] = []
        self.machine_state_history: list[MachineState] = []
        self.machines: dict[str, Machine] = {}
        self.pools: dict[str, list[Machine]] = {}
        self.good_units = 0
        self.rejected_units = 0
        self.completed_units = 0
        self.rejections_by_stage: dict[str, int] = {"injection": 0, "blow_molding": 0, "quality": 0}
        self.lead_times: list[float] = []
        self.queue_times: list[float] = []
        self.current_wip = config.demanda
        self.wip_area = 0.0
        self._wip_last_time = 0.0
        self.finished = False
        self.kpis: dict[str, Any] = {}
        self._build_resources()
        self._create_order()

    def _add_pool(
        self,
        key: str,
        machine_type: str,
        machine_id: str,
        temperature: float | None = None,
        pressure: float | None = None,
        capacity: int = 1,
    ) -> None:
        machine = Machine(
            env=self.env,
            machine_id=machine_id,
            machine_type=machine_type,
            temperature=temperature,
            pressure=pressure,
            resource_capacity=capacity,
        )
        self.machines[machine.machine_id] = machine
        self.pools[key] = [machine]
        self.machine_state_history.append(machine.snapshot())

    def _build_resources(self) -> None:
        material = self.config.material
        machine_ids = self.config.machine_ids
        self._add_pool("drying", "dryer", machine_ids["drying"], temperature=material.temperatura_secado_c or 170.0)
        self._add_pool("hopper", "material_hopper", machine_ids["hopper"])
        self._add_pool(
            "injection",
            "injection_molding",
            machine_ids["injection"],
            temperature=material.temperatura_fusion_c or 275.0,
            pressure=85.0,
            capacity=self.config.numero_maquinas_inyeccion,
        )
        self._add_pool("cooling", "cooling_conveyor", machine_ids["cooling"], temperature=25.0)
        self._add_pool("buffer", "preform_buffer", machine_ids["buffer"], capacity=self.config.capacidad_buffer)
        self._add_pool("reheating", "reheat_oven", machine_ids["reheating"], temperature=110.0)
        self._add_pool(
            "blow_molding",
            "stretch_blow_molding",
            machine_ids["blow_molding"],
            temperature=20.0,
            pressure=35.0,
            capacity=self.config.numero_maquinas_soplado,
        )
        self._add_pool("quality", "quality_control", machine_ids["quality"])
        self._add_pool("packaging", "packaging", machine_ids["packaging"])

    def _create_order(self) -> None:
        batch_size = self.config.unidades_por_lote_secador
        remaining = self.config.demanda
        index = 1
        while remaining:
            quantity = min(batch_size, remaining)
            lot = PETLot(
                lot_id=f"LOT-{index:04d}",
                quantity=quantity,
                mass_kg=quantity * self.config.masa_pieza_kg,
            )
            self.env.process(self._process_lot(lot))
            remaining -= quantity
            index += 1

    @staticmethod
    def _entity_identity(entity: PETLot | Preform | PETBottle) -> tuple[str, str]:
        if isinstance(entity, PETLot):
            return entity.lot_id, "PETLot"
        if isinstance(entity, Preform):
            return entity.preform_id, "Preform"
        return entity.bottle_id, "PETBottle"

    def _record_event(
        self,
        event_type: str,
        entity: PETLot | Preform | PETBottle,
        machine: Machine,
        stage: str,
        details: str = "",
    ) -> None:
        entity_id, entity_type = self._entity_identity(entity)
        self.events.append(
            SimulationEvent(
                simulation_time=round(float(self.env.now), 6),
                event_type=event_type,
                entity_id=entity_id,
                entity_type=entity_type,
                machine_id=machine.machine_id,
                stage=stage,
                queue=machine.queue_length,
                details=details,
            )
        )

    def _snapshot(self, machine: Machine) -> None:
        machine.update_queue_area()
        self.machine_state_history.append(machine.snapshot())

    def _select_machine(self, pool_key: str) -> Machine:
        return min(
            self.pools[pool_key],
            key=lambda item: (item.resource.count + len(item.resource.queue), item.machine_id),
        )

    def _failure_delay(self) -> float:
        return self.rng.expovariate(1.0 / (self.config.MTTR * 3600)) if self.config.MTTR > 0 else 0.0

    def _operate(
        self,
        entity: PETLot | Preform | PETBottle,
        machine: Machine,
        stage: str,
        duration: float,
    ) -> Generator[simpy.Event, Any, None]:
        remaining = duration
        worked = 0.0
        mtbf_seconds = self.config.MTBF * 3600
        while remaining > 0:
            failure_after = self.rng.expovariate(1.0 / mtbf_seconds) if mtbf_seconds > 0 else math.inf
            interval = min(remaining, failure_after)
            if interval:
                yield self.env.timeout(interval)
                machine.busy_time += interval
                worked += interval
                remaining -= interval
                machine.progress = min(1.0, worked / duration) if duration else 1.0
            if failure_after < interval + 1e-12 and remaining > 1e-12:
                machine.state = "FAULT"
                self._record_event("FAILURE", entity, machine, stage, "Falla estocastica por MTBF")
                self._snapshot(machine)
                repair_time = self._failure_delay()
                machine.state = "MAINTENANCE"
                self._record_event("REPAIR_START", entity, machine, stage, f"duration_seconds={repair_time:.6f}")
                self._snapshot(machine)
                if repair_time:
                    yield self.env.timeout(repair_time)
                    machine.downtime += repair_time
                self._record_event("REPAIR_END", entity, machine, stage)
                machine.state = "RUNNING"
                self._snapshot(machine)

    def _process_on_machine(
        self,
        entity: PETLot | Preform | PETBottle,
        pool_key: str,
        stage: str,
        time_spec: TimeDistribution,
        units: int = 1,
    ) -> Generator[simpy.Event, Any, Machine]:
        machine = self._select_machine(pool_key)
        machine.update_queue_area()
        request = machine.resource.request()
        machine.update_queue_area()
        wait_start = float(self.env.now)
        self._record_event("WAIT_START", entity, machine, stage)
        self._snapshot(machine)
        yield request
        waited = float(self.env.now) - wait_start
        self.queue_times.append(waited)
        if hasattr(entity, "queue_time"):
            entity.queue_time += waited
        self._record_event("WAIT_END", entity, machine, stage, f"wait_seconds={waited:.6f}")
        duration = sample_seconds(time_spec, self.rng)
        machine.state = "RUNNING"
        machine.progress = 0.0
        self._record_event("PROCESS_START", entity, machine, stage, f"duration_seconds={duration:.6f}")
        self._snapshot(machine)
        yield from self._operate(entity, machine, stage, duration)
        if hasattr(entity, "process_time"):
            entity.process_time += duration
        machine.progress = 1.0
        machine.produced += units
        self._record_event("PROCESS_END", entity, machine, stage)
        self._snapshot(machine)
        machine.resource.release(request)
        machine.update_queue_area()
        machine.state = "RUNNING" if machine.resource.count else "IDLE"
        if not machine.resource.count:
            machine.progress = 0.0
        self._record_event("RESOURCE_RELEASE", entity, machine, stage)
        self._snapshot(machine)
        return machine

    def _process_lot(self, lot: PETLot) -> Generator[simpy.Event, Any, None]:
        yield from self._process_on_machine(
            lot, "drying", "drying", self.config.tiempo_secado, units=lot.quantity
        )
        # The hopper is an explicit physical hand-off with no configured residence
        # time. Keeping it in the event stream makes H-101 observable end to end.
        yield from self._process_on_machine(
            lot,
            "hopper",
            "hopper_transfer",
            TimeDistribution(distribution="constant", value=0.0),
            units=lot.quantity,
        )
        for offset in range(lot.quantity):
            preform = Preform(
                preform_id=f"PF-{lot.lot_id[4:]}-{offset + 1:05d}",
                lot_id=lot.lot_id,
                mass_kg=self.config.masa_pieza_kg,
                created_at=lot.created_at,
            )
            self.env.process(self._process_preform(preform))

    def _reject(
        self,
        entity: Preform | PETBottle,
        machine: Machine,
        stage: str,
        reason: str,
    ) -> None:
        machine.rejected += 1
        self.rejected_units += 1
        self.completed_units += 1
        self.rejections_by_stage[stage] += 1
        self._record_event("REJECTION", entity, machine, stage, reason)
        self._snapshot(machine)
        self._complete_entity(entity)

    def _complete_entity(self, entity: Preform | PETBottle) -> None:
        self.lead_times.append(float(self.env.now) - entity.created_at)
        now = float(self.env.now)
        self.wip_area += self.current_wip * (now - self._wip_last_time)
        self._wip_last_time = now
        self.current_wip -= 1

    def _buffer_enter(self, preform: Preform) -> Generator[simpy.Event, Any, simpy.Request]:
        buffer = self.pools["buffer"][0]
        buffer.update_queue_area()
        request = buffer.resource.request()
        buffer.update_queue_area()
        wait_start = float(self.env.now)
        self._record_event("WAIT_START", preform, buffer, "buffer")
        self._snapshot(buffer)
        yield request
        waited = float(self.env.now) - wait_start
        preform.queue_time += waited
        self.queue_times.append(waited)
        buffer.state = "WAITING"
        buffer.produced += 1
        self._record_event("WAIT_END", preform, buffer, "buffer", f"wait_seconds={waited:.6f}")
        self._record_event("PROCESS_START", preform, buffer, "buffer", "Preforma almacenada")
        self._snapshot(buffer)
        return request

    def _buffer_leave(self, preform: Preform, request: simpy.Request) -> None:
        buffer = self.pools["buffer"][0]
        self._record_event("PROCESS_END", preform, buffer, "buffer", "Preforma despachada")
        buffer.resource.release(request)
        buffer.update_queue_area()
        buffer.state = "WAITING" if buffer.resource.count else "IDLE"
        self._record_event("RESOURCE_RELEASE", preform, buffer, "buffer")
        self._snapshot(buffer)

    def _process_preform(self, preform: Preform) -> Generator[simpy.Event, Any, None]:
        injection_machine = yield from self._process_on_machine(
            preform, "injection", "injection", self.config.tiempo_inyeccion
        )
        if self.rng.random() < self.config.scrap_inyeccion:
            self._reject(preform, injection_machine, "injection", "Scrap de inyeccion")
            return

        yield from self._process_on_machine(preform, "cooling", "cooling", self.config.tiempo_enfriamiento)
        buffer_request = yield from self._buffer_enter(preform)

        # Se reserva primero el horno; la preforma ocupa buffer hasta ese instante.
        oven = self._select_machine("reheating")
        oven.update_queue_area()
        oven_request = oven.resource.request()
        oven.update_queue_area()
        wait_start = float(self.env.now)
        self._record_event("WAIT_START", preform, oven, "reheating")
        self._snapshot(oven)
        yield oven_request
        waited = float(self.env.now) - wait_start
        preform.queue_time += waited
        self.queue_times.append(waited)
        self._buffer_leave(preform, buffer_request)
        self._record_event("WAIT_END", preform, oven, "reheating", f"wait_seconds={waited:.6f}")
        duration = sample_seconds(self.config.tiempo_recalentamiento, self.rng)
        oven.state = "RUNNING"
        oven.progress = 0.0
        self._record_event("PROCESS_START", preform, oven, "reheating", f"duration_seconds={duration:.6f}")
        self._snapshot(oven)
        yield from self._operate(preform, oven, "reheating", duration)
        preform.process_time += duration
        oven.produced += 1
        oven.progress = 1.0
        self._record_event("PROCESS_END", preform, oven, "reheating")
        oven.resource.release(oven_request)
        oven.update_queue_area()
        oven.state = "IDLE"
        oven.progress = 0.0
        self._record_event("RESOURCE_RELEASE", preform, oven, "reheating")
        self._snapshot(oven)

        bottle = PETBottle(
            bottle_id=preform.preform_id.replace("PF-", "BT-", 1),
            preform_id=preform.preform_id,
            lot_id=preform.lot_id,
            mass_kg=preform.mass_kg,
            created_at=preform.created_at,
            queue_time=preform.queue_time,
            process_time=preform.process_time,
            history=preform.history,
        )
        blow_machine = yield from self._process_on_machine(
            bottle, "blow_molding", "blow_molding", self.config.tiempo_soplado
        )
        if self.rng.random() < self.config.scrap_soplado:
            self._reject(bottle, blow_machine, "blow_molding", "Scrap de soplado")
            return

        quality_machine = yield from self._process_on_machine(
            bottle, "quality", "quality", self.config.tiempo_inspeccion
        )
        if self.rng.random() < self.config.scrap_calidad:
            self._reject(bottle, quality_machine, "quality", "Rechazo de calidad")
            return

        yield from self._process_on_machine(bottle, "packaging", "packaging", self.config.tiempo_empaque)
        self.good_units += 1
        self.completed_units += 1
        self._complete_entity(bottle)

    def step(self) -> bool:
        """Ejecuta el siguiente evento; retorna False cuando termina la agenda."""
        if self.finished:
            return False
        if self.env.peek() == math.inf:
            self.finalize()
            return False
        self.env.step()
        return True

    def run(self) -> dict[str, Any]:
        while self.step():
            pass
        return self.kpis

    def finalize(self) -> None:
        if self.finished:
            return
        from simulation.kpis import calculate_kpis

        now = float(self.env.now)
        self.wip_area += self.current_wip * (now - self._wip_last_time)
        self._wip_last_time = now
        for machine in self.machines.values():
            machine.update_queue_area()
            machine.state = "FINISHED"
            machine.progress = 1.0
            self.machine_state_history.append(machine.snapshot())
        self.finished = True
        self.kpis = calculate_kpis(self)
        LOGGER.info("Simulacion finalizada en %.3f s: %d buenas, %d rechazadas", now, self.good_units, self.rejected_units)

    @property
    def simulation_time(self) -> float:
        return float(self.env.now)

    def latest_machine_states(self) -> list[dict[str, Any]]:
        return [machine.snapshot().as_dict() for machine in self.machines.values()]

    def write_results(self, output_dir: str | Path) -> None:
        if not self.finished:
            raise RuntimeError("La simulacion debe finalizar antes de exportar")
        target = Path(output_dir)
        target.mkdir(parents=True, exist_ok=True)
        event_fields = list(SimulationEvent.__dataclass_fields__)
        with (target / "events.csv").open("w", newline="", encoding="utf-8") as file:
            writer = csv.DictWriter(file, fieldnames=event_fields)
            writer.writeheader()
            writer.writerows(event.as_dict() for event in self.events)
        state_fields = list(MachineState.__dataclass_fields__)
        with (target / "machine_states.csv").open("w", newline="", encoding="utf-8") as file:
            writer = csv.DictWriter(file, fieldnames=state_fields)
            writer.writeheader()
            writer.writerows(state.as_dict() for state in self.machine_state_history)
        with (target / "kpis.json").open("w", encoding="utf-8") as file:
            json.dump(self.kpis, file, ensure_ascii=False, indent=2)

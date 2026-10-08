"""Control de ciclo de vida de una simulacion ejecutada en segundo plano."""

from __future__ import annotations

import csv
import json
import logging
import threading
import time
from dataclasses import asdict
from pathlib import Path
from typing import Any

from simulation.config import SimulationConfig
from simulation.constants import OFFICIAL_MACHINE_IDS
from simulation.kpis import calculate_kpis
from simulation.model import PETSimulation

LOGGER = logging.getLogger(__name__)


class SimulationManager:
    def __init__(self, config_path: str | Path, output_dir: str | Path, step_delay: float = 0.001) -> None:
        self.config_path = Path(config_path)
        self.output_dir = Path(output_dir)
        self.step_delay = step_delay
        self._published_machine_states: list[dict[str, Any]] | None = None
        self._published_kpis: dict[str, Any] | None = None
        self._published_simulation_time = 0.0
        self.model = self._new_model()
        self.status = "READY"
        self._thread: threading.Thread | None = None
        self._pause_event = threading.Event()
        self._pause_event.set()
        self._stop_event = threading.Event()
        self._lock = threading.RLock()
        if self._load_persisted_results():
            self.status = "FINISHED"

    def _new_model(self, config: dict[str, Any] | None = None) -> PETSimulation:
        parsed = SimulationConfig.from_dict(config) if config is not None else SimulationConfig.from_json(self.config_path)
        return PETSimulation(parsed)

    def _load_persisted_results(self) -> bool:
        """Load the latest CLI run when it is not older than its input file."""
        states_path = self.output_dir / "machine_states.csv"
        kpis_path = self.output_dir / "kpis.json"
        if not states_path.is_file() or not kpis_path.is_file():
            return False
        input_mtime = self.config_path.stat().st_mtime
        if min(states_path.stat().st_mtime, kpis_path.stat().st_mtime) < input_mtime:
            return False

        latest: dict[str, dict[str, Any]] = {}
        with states_path.open(newline="", encoding="utf-8") as file:
            for row in csv.DictReader(file):
                machine_id = row["machine_id"]
                latest[machine_id] = {
                    "machine_id": machine_id,
                    "machine_type": row["machine_type"],
                    "state": row["state"],
                    "simulation_time": float(row["simulation_time"]),
                    "progress": float(row["progress"]),
                    "queue": int(row["queue"]),
                    "temperature": float(row["temperature"]) if row["temperature"] else None,
                    "pressure": float(row["pressure"]) if row["pressure"] else None,
                    "produced": int(row["produced"]),
                    "rejected": int(row["rejected"]),
                }
        expected_ids = OFFICIAL_MACHINE_IDS
        if set(latest) != set(expected_ids):
            LOGGER.warning("Resultados ignorados: los IDs no coinciden con la configuracion actual")
            return False

        with kpis_path.open(encoding="utf-8") as file:
            kpis = json.load(file)
        self._published_machine_states = [latest[machine_id] for machine_id in expected_ids]
        self._published_kpis = kpis
        self._published_simulation_time = max(
            state["simulation_time"] for state in self._published_machine_states
        )
        return True

    def _clear_published_results(self) -> None:
        self._published_machine_states = None
        self._published_kpis = None
        self._published_simulation_time = 0.0

    def _publish_model_results(self) -> None:
        self._published_machine_states = self.model.latest_machine_states()
        self._published_kpis = self.model.kpis
        self._published_simulation_time = self.model.simulation_time

    def start(self, config: dict[str, Any] | None = None) -> str:
        with self._lock:
            if self.status == "PAUSED":
                self.status = "RUNNING"
                self._pause_event.set()
                return self.status
            if self.status == "RUNNING":
                return self.status
            if self.status in {"FINISHED", "STOPPED"} or config is not None:
                self.model = self._new_model(config)
            self._clear_published_results()
            self._stop_event.clear()
            self._pause_event.set()
            self.status = "RUNNING"
            self._thread = threading.Thread(target=self._run_loop, name="pet-simulation", daemon=True)
            self._thread.start()
            return self.status

    def _run_loop(self) -> None:
        try:
            while not self._stop_event.is_set():
                self._pause_event.wait()
                if self._stop_event.is_set():
                    break
                if not self.model.step():
                    self.model.write_results(self.output_dir)
                    with self._lock:
                        self._publish_model_results()
                        self.status = "FINISHED"
                    return
                if self.step_delay:
                    time.sleep(self.step_delay)
            with self._lock:
                if self.status != "READY":
                    self.status = "STOPPED"
        except Exception:
            LOGGER.exception("Fallo la ejecucion de la simulacion")
            with self._lock:
                self.status = "ERROR"

    def pause(self) -> str:
        with self._lock:
            if self.status == "RUNNING":
                self.status = "PAUSED"
                self._pause_event.clear()
            return self.status

    def reset(self) -> str:
        self._stop_event.set()
        self._pause_event.set()
        thread = self._thread
        if thread and thread.is_alive() and thread is not threading.current_thread():
            thread.join(timeout=2)
        with self._lock:
            self.model = self._new_model()
            self._clear_published_results()
            self.status = "READY"
            self._thread = None
            self._stop_event.clear()
            return self.status

    def status_payload(self) -> dict[str, Any]:
        simulation_time = (
            self._published_simulation_time
            if self.status == "FINISHED" and self._published_machine_states is not None
            else self.model.simulation_time
        )
        completed_units = (
            int(self._published_kpis["production_total"])
            if self.status == "FINISHED" and self._published_kpis is not None
            else self.model.completed_units
        )
        return {
            "status": self.status,
            "simulation_time": round(simulation_time, 6),
            "time_unit": "seconds",
            "completed_units": completed_units,
            "demand": self.model.config.demanda,
        }

    def config_payload(self) -> dict[str, Any]:
        with self._lock:
            return asdict(self.model.config)

    def machine_states_payload(self) -> list[dict[str, Any]]:
        if self.status == "FINISHED" and self._published_machine_states is not None:
            return self._published_machine_states
        return self.model.latest_machine_states()

    def machine_payload(self, machine_id: str) -> dict[str, Any] | None:
        return next(
            (state for state in self.machine_states_payload() if state["machine_id"] == machine_id),
            None,
        )

    def kpis_payload(self) -> dict[str, Any]:
        if self.status == "FINISHED" and self._published_kpis is not None:
            return self._published_kpis
        return self.model.kpis if self.model.finished else calculate_kpis(self.model)

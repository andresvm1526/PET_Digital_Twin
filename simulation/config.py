"""Carga y validacion de la configuracion desacoplada del modelo."""

from __future__ import annotations

import json
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

from simulation.constants import MACHINE_IDS


VALID_DISTRIBUTIONS = {"constant", "normal", "triangular", "uniform"}
VALID_TIME_UNITS = {"seconds", "minutes", "hours"}


@dataclass(frozen=True, slots=True)
class TimeDistribution:
    distribution: str
    unit: str = "seconds"
    value: float | None = None
    mean: float | None = None
    stddev: float | None = None
    min: float | None = None
    mode: float | None = None
    max: float | None = None

    @classmethod
    def from_dict(cls, data: dict[str, Any]) -> "TimeDistribution":
        instance = cls(**data)
        instance.validate()
        return instance

    def validate(self) -> None:
        if self.distribution not in VALID_DISTRIBUTIONS:
            raise ValueError(f"Distribucion no soportada: {self.distribution}")
        if self.unit not in VALID_TIME_UNITS:
            raise ValueError(f"Unidad de tiempo no soportada: {self.unit}")
        if self.distribution == "constant" and (self.value is None or self.value < 0):
            raise ValueError("constant requiere value >= 0")
        if self.distribution == "normal":
            if self.mean is None or self.mean < 0 or self.stddev is None or self.stddev < 0:
                raise ValueError("normal requiere mean >= 0 y stddev >= 0")
        if self.distribution == "triangular":
            if self.min is None or self.mode is None or self.max is None:
                raise ValueError("triangular requiere min, mode y max")
            if not 0 <= self.min <= self.mode <= self.max:
                raise ValueError("triangular requiere 0 <= min <= mode <= max")
        if self.distribution == "uniform":
            if self.min is None or self.max is None or not 0 <= self.min <= self.max:
                raise ValueError("uniform requiere 0 <= min <= max")


@dataclass(frozen=True, slots=True)
class MaterialConfig:
    nombre: str
    grado: str | None = None
    fuente: str = "manual"
    densidad_kg_m3: float | None = None
    temperatura_secado_c: float | None = None
    temperatura_fusion_c: float | None = None
    propiedades_granta: dict[str, Any] = field(default_factory=dict)


@dataclass(frozen=True, slots=True)
class SimulationConfig:
    material: MaterialConfig
    demanda: int
    masa_pieza: float
    numero_maquinas_inyeccion: int
    numero_maquinas_soplado: int
    capacidad_secador: float
    tiempo_secado: TimeDistribution
    tiempo_inyeccion: TimeDistribution
    tiempo_enfriamiento: TimeDistribution
    tiempo_recalentamiento: TimeDistribution
    tiempo_soplado: TimeDistribution
    tiempo_inspeccion: TimeDistribution
    tiempo_empaque: TimeDistribution
    scrap_inyeccion: float
    scrap_soplado: float
    scrap_calidad: float
    horas_turno: float
    numero_turnos: int
    MTBF: float
    MTTR: float
    unidad_masa_pieza: str = "grams"
    unidad_capacidad_secador: str = "kg"
    capacidad_buffer: int = 100
    random_seed: int = 42
    machine_ids: dict[str, str] = field(default_factory=lambda: dict(MACHINE_IDS))

    @classmethod
    def from_dict(cls, data: dict[str, Any]) -> "SimulationConfig":
        converted = dict(data)
        converted["material"] = MaterialConfig(**converted["material"])
        for name in (
            "tiempo_secado",
            "tiempo_inyeccion",
            "tiempo_enfriamiento",
            "tiempo_recalentamiento",
            "tiempo_soplado",
            "tiempo_inspeccion",
            "tiempo_empaque",
        ):
            converted[name] = TimeDistribution.from_dict(converted[name])
        config = cls(**converted)
        config.validate()
        return config

    @classmethod
    def from_json(cls, path: str | Path) -> "SimulationConfig":
        with Path(path).open(encoding="utf-8") as file:
            return cls.from_dict(json.load(file))

    def validate(self) -> None:
        positive = {
            "demanda": self.demanda,
            "masa_pieza": self.masa_pieza,
            "numero_maquinas_inyeccion": self.numero_maquinas_inyeccion,
            "numero_maquinas_soplado": self.numero_maquinas_soplado,
            "capacidad_secador": self.capacidad_secador,
            "horas_turno": self.horas_turno,
            "numero_turnos": self.numero_turnos,
            "capacidad_buffer": self.capacidad_buffer,
        }
        if any(value <= 0 for value in positive.values()):
            raise ValueError(f"Los valores deben ser positivos: {positive}")
        for name, value in {
            "scrap_inyeccion": self.scrap_inyeccion,
            "scrap_soplado": self.scrap_soplado,
            "scrap_calidad": self.scrap_calidad,
        }.items():
            if not 0 <= value <= 1:
                raise ValueError(f"{name} debe estar entre 0 y 1")
        if self.MTBF < 0 or self.MTTR < 0:
            raise ValueError("MTBF y MTTR no pueden ser negativos")
        if self.unidad_masa_pieza not in {"grams", "kg"}:
            raise ValueError("unidad_masa_pieza debe ser grams o kg")
        if self.unidad_capacidad_secador != "kg":
            raise ValueError("unidad_capacidad_secador debe ser kg")
        if self.machine_ids != MACHINE_IDS:
            raise ValueError(f"machine_ids debe coincidir exactamente con los IDs oficiales: {MACHINE_IDS}")

    @property
    def masa_pieza_kg(self) -> float:
        return self.masa_pieza / 1000 if self.unidad_masa_pieza == "grams" else self.masa_pieza

    @property
    def unidades_por_lote_secador(self) -> int:
        return max(1, int(self.capacidad_secador / self.masa_pieza_kg))

    @property
    def horizonte_turnos_seconds(self) -> float:
        return self.horas_turno * self.numero_turnos * 3600

"""Entidades que cambian de forma durante el flujo PET."""

from __future__ import annotations

from dataclasses import dataclass, field


@dataclass(slots=True)
class PETLot:
    lot_id: str
    quantity: int
    mass_kg: float
    created_at: float = 0.0


@dataclass(slots=True)
class Preform:
    preform_id: str
    lot_id: str
    mass_kg: float
    created_at: float
    queue_time: float = 0.0
    process_time: float = 0.0
    history: list[str] = field(default_factory=list)


@dataclass(slots=True)
class PETBottle:
    bottle_id: str
    preform_id: str
    lot_id: str
    mass_kg: float
    created_at: float
    queue_time: float = 0.0
    process_time: float = 0.0
    history: list[str] = field(default_factory=list)


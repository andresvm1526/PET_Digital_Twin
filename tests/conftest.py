from __future__ import annotations

import copy
import json
from pathlib import Path

import pytest


PROJECT_ROOT = Path(__file__).resolve().parents[1]


@pytest.fixture
def small_config_dict() -> dict:
    with (PROJECT_ROOT / "data" / "input_pet.json").open(encoding="utf-8") as file:
        config = json.load(file)
    config = copy.deepcopy(config)
    config.update(
        {
            "demanda": 12,
            "capacidad_buffer": 4,
            "scrap_inyeccion": 0.0,
            "scrap_soplado": 0.0,
            "scrap_calidad": 0.0,
            "MTBF": 0.0,
            "MTTR": 0.0,
            "random_seed": 7,
        }
    )
    for field in (
        "tiempo_secado",
        "tiempo_inyeccion",
        "tiempo_enfriamiento",
        "tiempo_recalentamiento",
        "tiempo_soplado",
        "tiempo_inspeccion",
        "tiempo_empaque",
    ):
        config[field] = {"distribution": "constant", "value": 1, "unit": "seconds"}
    return config


from __future__ import annotations

import random

import pytest

from simulation.config import SimulationConfig, TimeDistribution
from simulation.distributions import sample_seconds


@pytest.mark.parametrize(
    ("spec", "lower", "upper"),
    [
        ({"distribution": "constant", "value": 2, "unit": "minutes"}, 120, 120),
        ({"distribution": "uniform", "min": 2, "max": 4, "unit": "seconds"}, 2, 4),
        ({"distribution": "triangular", "min": 2, "mode": 3, "max": 4, "unit": "seconds"}, 2, 4),
        ({"distribution": "normal", "mean": 3, "stddev": 0.2, "min": 2, "max": 4, "unit": "seconds"}, 2, 4),
    ],
)
def test_supported_distributions(spec: dict, lower: float, upper: float) -> None:
    value = sample_seconds(TimeDistribution.from_dict(spec), random.Random(1))
    assert lower <= value <= upper


def test_config_derives_dryer_batch_size(small_config_dict: dict) -> None:
    config = SimulationConfig.from_dict(small_config_dict)
    assert config.unidades_por_lote_secador == int(config.capacidad_secador / config.masa_pieza_kg)


def test_invalid_scrap_is_rejected(small_config_dict: dict) -> None:
    small_config_dict["scrap_calidad"] = 1.1
    with pytest.raises(ValueError, match="scrap_calidad"):
        SimulationConfig.from_dict(small_config_dict)


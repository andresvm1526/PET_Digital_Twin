"""Muestreo reproducible de tiempos del proceso."""

from __future__ import annotations

import random

from simulation.config import TimeDistribution


UNIT_TO_SECONDS = {"seconds": 1.0, "minutes": 60.0, "hours": 3600.0}


def sample_seconds(spec: TimeDistribution, rng: random.Random) -> float:
    if spec.distribution == "constant":
        sampled = float(spec.value)
    elif spec.distribution == "normal":
        sampled = rng.gauss(float(spec.mean), float(spec.stddev))
        sampled = max(float(spec.min or 0.0), sampled)
        if spec.max is not None:
            sampled = min(spec.max, sampled)
    elif spec.distribution == "triangular":
        sampled = rng.triangular(float(spec.min), float(spec.max), float(spec.mode))
    elif spec.distribution == "uniform":
        sampled = rng.uniform(float(spec.min), float(spec.max))
    else:  # La configuracion valida antes de llegar aqui.
        raise ValueError(f"Distribucion no soportada: {spec.distribution}")
    return sampled * UNIT_TO_SECONDS[spec.unit]


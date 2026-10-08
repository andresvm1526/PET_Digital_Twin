"""Canonical equipment identifiers shared by the simulation and contracts."""

from __future__ import annotations


MACHINE_IDS = {
    "drying": "D-101",
    "hopper": "H-101",
    "injection": "IMM-101",
    "cooling": "CV-101",
    "buffer": "BF-101",
    "reheating": "OV-101",
    "blow_molding": "SBM-101",
    "quality": "QC-101",
    "packaging": "PKG-101",
}

OFFICIAL_MACHINE_IDS = tuple(MACHINE_IDS.values())

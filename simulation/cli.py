"""Punto de entrada de linea de comandos para corridas reproducibles."""

from __future__ import annotations

import argparse
import json
import logging
from pathlib import Path

from simulation.config import SimulationConfig
from simulation.model import PETSimulation


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Simular una linea de fabricacion PET")
    parser.add_argument("--config", default="data/input_pet.json", help="Archivo JSON de entrada")
    parser.add_argument("--output", default="results", help="Directorio de resultados")
    return parser


def main() -> None:
    args = build_parser().parse_args()
    logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(name)s: %(message)s")
    config = SimulationConfig.from_json(Path(args.config))
    simulation = PETSimulation(config)
    kpis = simulation.run()
    simulation.write_results(Path(args.output))
    print(json.dumps(kpis, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()


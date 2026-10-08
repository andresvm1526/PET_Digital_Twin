"""Lanzador del sistema completo del gemelo digital PET.

Prepara los resultados si hace falta, arranca FastAPI con el dashboard y abre el
navegador. Pensado para ejecutarse con doble clic desde ``deploy.bat`` o desde la
terminal como ``pet-deploy``.

No puede iniciar Unity: el repositorio no garantiza soporte CLI del Editor. El
lanzador deja escrita la ruta exacta que hay que abrir a mano.
"""

from __future__ import annotations

import argparse
import importlib
import json
import os
import signal
import socket
import subprocess
import sys
import time
import urllib.error
import urllib.request
import webbrowser
from pathlib import Path

PROJECT_ROOT = Path(__file__).resolve().parents[1]

DEFAULT_HOST = "127.0.0.1"
DEFAULT_PORT = 8000
DEFAULT_CONFIG = Path("data") / "input_pet.json"
DEFAULT_RESULTS = Path("results")
DEFAULT_DASHBOARD = Path("dashboard") / "dist" / "index.html"
UNITY_PROJECT = Path("unity") / "PET_DigitalTwin_EditorInit"
UNITY_SCENE = UNITY_PROJECT / "Assets" / "Scenes" / "PET_Plant.unity"

REQUIRED_OUTPUTS = ("events.csv", "machine_states.csv", "kpis.json")
REQUIRED_PACKAGES = ("simpy", "fastapi", "uvicorn")
API_TITLE = "PET Digital Twin API"
READY_TIMEOUT_SECONDS = 30.0


# ── consola ────────────────────────────────────────────────────────────────


def configure_console() -> None:
    """Escribe UTF-8 aunque la consola de Windows arranque en CP850."""
    for stream in (sys.stdout, sys.stderr):
        reconfigure = getattr(stream, "reconfigure", None)
        if reconfigure is None:
            continue
        try:
            reconfigure(encoding="utf-8", errors="replace")
        except (OSError, ValueError):
            pass


def say(label: str, value: str) -> None:
    print(f"  {label:<14} {value}")


# ── utilidades de red ──────────────────────────────────────────────────────


def http_get(url: str, timeout: float = 2.0) -> tuple[int, str] | None:
    """Devuelve (estado, cuerpo) o None si no hay respuesta."""
    try:
        with urllib.request.urlopen(url, timeout=timeout) as response:
            charset = response.headers.get_content_charset() or "utf-8"
            return response.status, response.read().decode(charset, errors="replace")
    except urllib.error.HTTPError as error:
        return error.code, ""
    except (urllib.error.URLError, OSError, TimeoutError):
        return None


def port_is_open(host: str, port: int) -> bool:
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as sock:
        sock.settimeout(0.4)
        return sock.connect_ex((host, port)) == 0


def listening_pid(host: str, port: int) -> int | None:
    """PID del proceso que escucha en el puerto; solo Windows, opcional."""
    if os.name != "nt":
        return None
    script = (
        f"Get-NetTCPConnection -LocalPort {port} -State Listen -ErrorAction SilentlyContinue "
        "| Select-Object -First 1 -ExpandProperty OwningProcess"
    )
    try:
        result = subprocess.run(
            ["powershell", "-NoProfile", "-Command", script],
            capture_output=True,
            text=True,
            timeout=20,
        )
    except (OSError, subprocess.SubprocessError):
        return None
    value = result.stdout.strip()
    return int(value) if value.isdigit() else None


def is_our_api(host: str, port: int) -> bool:
    payload = http_get(f"http://{host}:{port}/openapi.json", timeout=2.0)
    if payload is None or payload[0] != 200:
        return False
    try:
        document = json.loads(payload[1])
    except json.JSONDecodeError:
        return False
    return document.get("info", {}).get("title") == API_TITLE


def stop_listening_process(host: str, port: int) -> bool:
    pid = listening_pid(host, port)
    if pid is None:
        return False
    try:
        if os.name == "nt":
            subprocess.run(
                ["taskkill", "/PID", str(pid), "/F"],
                capture_output=True,
                timeout=20,
                check=False,
            )
        else:
            os.kill(pid, signal.SIGTERM)
    except (OSError, subprocess.SubprocessError):
        return False
    deadline = time.monotonic() + 10
    while time.monotonic() < deadline and port_is_open(host, port):
        time.sleep(0.3)
    return not port_is_open(host, port)


def wait_until_ready(host: str, port: int, process: subprocess.Popen | None = None) -> bool:
    deadline = time.monotonic() + READY_TIMEOUT_SECONDS
    url = f"http://{host}:{port}/dashboard"
    while time.monotonic() < deadline:
        if process is not None and process.poll() is not None:
            return False
        payload = http_get(url, timeout=2.0)
        if payload is not None and payload[0] == 200:
            return True
        time.sleep(0.3)
    return False


# ── preflight ──────────────────────────────────────────────────────────────


def configured_path(name: str, default: Path) -> Path:
    """Misma regla que api/main.py: absoluto o relativo a la raiz del proyecto."""
    configured = Path(os.environ.get(name, str(default)))
    return configured if configured.is_absolute() else PROJECT_ROOT / configured


def missing_dependencies() -> list[str]:
    missing = []
    for name in REQUIRED_PACKAGES:
        try:
            importlib.import_module(name)
        except ImportError:
            missing.append(name)
    return missing


def simulation_freshness(config_path: Path, results_dir: Path) -> str | None:
    """Motivo por el que hay que reejecutar la simulacion, o None si esta vigente."""
    missing = [name for name in REQUIRED_OUTPUTS if not (results_dir / name).is_file()]
    if missing:
        return "faltan " + ", ".join(missing)
    if not config_path.is_file():
        return f"no existe {config_path}"
    newest = max((results_dir / name).stat().st_mtime for name in REQUIRED_OUTPUTS)
    if newest < config_path.stat().st_mtime:
        return f"{results_dir.name}/ es anterior a {config_path.name}"
    return None


def run_simulation(config_path: Path, results_dir: Path) -> None:
    subprocess.run(
        [
            sys.executable,
            "-m",
            "simulation.main",
            "--config",
            str(config_path),
            "--output",
            str(results_dir),
        ],
        cwd=PROJECT_ROOT,
        check=True,
    )


def unity_build_published(host: str, port: int) -> bool:
    payload = http_get(f"http://{host}:{port}/unity/index.html", timeout=2.0)
    return payload is not None and payload[0] == 200


# ── servidor ───────────────────────────────────────────────────────────────


def start_server(host: str, port: int, config_path: Path, results_dir: Path, log_level: str) -> subprocess.Popen:
    env = os.environ.copy()
    env["PET_CONFIG_PATH"] = str(config_path)
    env["PET_RESULTS_DIR"] = str(results_dir)
    return subprocess.Popen(
        [
            sys.executable,
            "-m",
            "uvicorn",
            "api.server:app",
            "--host",
            host,
            "--port",
            str(port),
            "--log-level",
            log_level,
        ],
        cwd=PROJECT_ROOT,
        env=env,
    )


def stop_server(process: subprocess.Popen, grace: float = 0.0) -> None:
    """Para el servidor esperando primero su salida voluntaria (Ctrl+C llega a todos)."""
    if process.poll() is not None:
        return
    if grace:
        try:
            process.wait(timeout=grace)
            return
        except (subprocess.TimeoutExpired, KeyboardInterrupt, OSError):
            pass
    try:
        process.terminate()
        process.wait(timeout=10)
    except subprocess.TimeoutExpired:
        try:
            process.kill()
            process.wait(timeout=5)
        except (OSError, subprocess.TimeoutExpired):
            pass
    except OSError:
        pass


# ── informe ────────────────────────────────────────────────────────────────


def print_report(host: str, port: int, config_path: Path, results_dir: Path, published: bool) -> None:
    base = f"http://{host}:{port}"
    status = http_get(f"{base}/status", timeout=3.0)
    print()
    print("  Sistema desplegado")
    say("Configuracion", relative(config_path))
    if status is not None and status[0] == 200:
        try:
            data = json.loads(status[1])
            say(
                "Resultados",
                f"{data.get('completed_units', '?')} / {data.get('demand', '?')} unidades"
                f" · {data.get('status', '?')}"
                f" · {relative(results_dir)}",
            )
        except json.JSONDecodeError:
            say("Resultados", relative(results_dir))
    else:
        say("Resultados", relative(results_dir))
    say("API", base)
    say("Dashboard", f"{base}/dashboard")
    if published:
        say("Visor WebGL", "publicado en /unity/index.html")
    else:
        say("Visor WebGL", "sin build; copiar a unity/webgl/ (docs/deploy.md)")
    print()
    print("  Unity (manual):")
    say("Proyecto", str(UNITY_PROJECT))
    say("Escena", str(UNITY_SCENE))
    say("Accion", "abrir en Unity Hub y pulsar Play")
    print()
    print("  Ctrl+C para detener el servidor.")


def relative(path: Path) -> str:
    try:
        return str(path.relative_to(PROJECT_ROOT))
    except ValueError:
        return str(path)


# ── CLI ────────────────────────────────────────────────────────────────────


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="pet-deploy",
        description="Despliega el gemelo digital PET: simulacion, API, dashboard y visor WebGL.",
    )
    parser.add_argument("--host", default=DEFAULT_HOST, help=f"host de la API (por defecto {DEFAULT_HOST})")
    parser.add_argument("--port", type=int, default=DEFAULT_PORT, help=f"puerto de la API (por defecto {DEFAULT_PORT})")
    parser.add_argument("--config", default=None, help=f"input JSON (por defecto {DEFAULT_CONFIG})")
    parser.add_argument("--results", default=None, help=f"directorio de resultados (por defecto {DEFAULT_RESULTS})")
    parser.add_argument("--force-sim", action="store_true", help="reexecea la simulacion aunque los resultados esten vigentes")
    parser.add_argument("--skip-sim", action="store_true", help="no ejecuta la simulacion, sirve results/ tal cual")
    parser.add_argument("--restart", action="store_true", help="detiene y relanza una API propia que ya este corriendo")
    parser.add_argument("--no-browser", action="store_true", help="no abre el navegador")
    parser.add_argument("--log-level", default="info", choices=("critical", "error", "warning", "info", "debug", "trace"))
    return parser


def main(argv: list[str] | None = None) -> int:
    configure_console()
    parser = build_parser()
    args = parser.parse_args(argv)
    if args.force_sim and args.skip_sim:
        parser.error("--force-sim y --skip-sim son incompatibles")

    config_path = (
        Path(args.config).expanduser().resolve() if args.config else configured_path("PET_CONFIG_PATH", PROJECT_ROOT / DEFAULT_CONFIG)
    )
    results_dir = (
        Path(args.results).expanduser().resolve() if args.results else configured_path("PET_RESULTS_DIR", PROJECT_ROOT / DEFAULT_RESULTS)
    )
    dashboard_path = PROJECT_ROOT / DEFAULT_DASHBOARD

    print()
    print("PET Digital Twin · despliegue del sistema")

    # 1. preflight
    missing = missing_dependencies()
    if missing:
        print()
        print(f"  Faltan dependencias: {', '.join(missing)}")
        print("  Instalar con:  python -m pip install -e \".[test]\"")
        return 1
    if not config_path.is_file():
        print()
        print(f"  No existe el input: {relative(config_path)}")
        return 1
    if not dashboard_path.is_file():
        print()
        print(f"  AVISO: no se encuentra {relative(dashboard_path)}; el dashboard no se podra servir.")

    # 2. simulacion
    ran_simulation = False
    freshness = simulation_freshness(config_path, results_dir)
    if args.skip_sim:
        if freshness:
            print()
            print(f"  AVISO: {freshness}; --skip-sim impide regenerarlo.")
        else:
            print()
            print("  Resultados vigentes; --skip-sim no ejecuta SimPy.")
    elif freshness or args.force_sim:
        reason = "fuerza --force-sim" if (args.force_sim and not freshness) else freshness
        print()
        print(f"  Ejecutando simulacion ({reason}) ...")
        try:
            run_simulation(config_path, results_dir)
        except subprocess.CalledProcessError as error:
            print(f"  ERROR: la simulacion fallo con codigo {error.returncode}.")
            return 1
        ran_simulation = True
        print("  Simulacion terminada.")
    else:
        print()
        print("  Resultados vigentes; no hace falta reejecutar la simulacion.")

    # 3. puerto
    base = f"http://{args.host}:{args.port}"
    if port_is_open(args.host, args.port):
        if not is_our_api(args.host, args.port):
            pid = listening_pid(args.host, args.port)
            print()
            print(f"  El puerto {args.port} lo ocupa otro proceso" + (f" (PID {pid})" if pid else "") + ".")
            print(f"  Usa otro puerto:  pet-deploy --port {args.port + 10}")
            return 1
        if args.restart:
            print()
            print("  Deteniendo la API existente ...")
            if not stop_listening_process(args.host, args.port):
                print(f"  ERROR: no se pudo liberar el puerto {args.port}.")
                return 1
        else:
            print("  La API propia ya esta corriendo; se reutiliza.")
            if ran_simulation:
                print()
                print("  AVISO: se generaron resultados nuevos pero la API en marcha publica los anteriores.")
                print("  Relanzar con --restart para que los sirva.")
            return finish_existing(args, config_path, results_dir, dashboard_path)

    # 4. arranque
    print()
    print(f"  Arrancando API en {base} ...")
    process = start_server(args.host, args.port, config_path, results_dir, args.log_level)
    if not wait_until_ready(args.host, args.port, process):
        print("  ERROR: la API no respondio a tiempo.")
        stop_server(process)
        return 1

    published = unity_build_published(args.host, args.port)
    if not args.no_browser:
        try:
            webbrowser.open(f"{base}/")
        except webbrowser.Error:
            print("  AVISO: no se pudo abrir el navegador.")

    print_report(args.host, args.port, config_path, results_dir, published)

    try:
        return process.wait() or 0
    except KeyboardInterrupt:
        print()
        print("  Deteniendo el servidor ...")
        stop_server(process, grace=5.0)
        return 0


def finish_existing(args, config_path: Path, results_dir: Path, dashboard_path: Path) -> int:
    """Reporte y navegador cuando ya hay una API propia corriendo."""
    published = unity_build_published(args.host, args.port)
    if not args.no_browser:
        try:
            webbrowser.open(f"http://{args.host}:{args.port}/")
        except webbrowser.Error:
            pass
    print_report(args.host, args.port, config_path, results_dir, published)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

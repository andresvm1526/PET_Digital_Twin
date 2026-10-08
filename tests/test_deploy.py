from __future__ import annotations

import json
import os
import socket
import subprocess
import sys
import time
from pathlib import Path

from deployment import launcher
from deployment.launcher import REQUIRED_OUTPUTS, build_parser, configured_path, is_our_api, missing_dependencies, simulation_freshness


def _write_outputs(results_dir: Path, stamp: int) -> None:
    results_dir.mkdir(parents=True, exist_ok=True)
    for name in REQUIRED_OUTPUTS:
        path = results_dir / name
        path.write_text("x", encoding="utf-8")
        os.utime(path, (stamp, stamp))


def test_simulation_freshness_reports_missing_outputs(tmp_path: Path) -> None:
    config = tmp_path / "input.json"
    config.write_text("{}", encoding="utf-8")

    reason = simulation_freshness(config, tmp_path / "results")

    assert reason is not None
    assert "faltan" in reason
    assert "machine_states.csv" in reason


def test_simulation_freshness_reports_stale_results(tmp_path: Path) -> None:
    config = tmp_path / "input.json"
    config.write_text("{}", encoding="utf-8")
    os.utime(config, (2000, 2000))
    _write_outputs(tmp_path / "results", stamp=1000)

    reason = simulation_freshness(config, tmp_path / "results")

    assert reason is not None
    assert "anterior" in reason
    assert "input.json" in reason


def test_simulation_freshness_accepts_current_results(tmp_path: Path) -> None:
    config = tmp_path / "input.json"
    config.write_text("{}", encoding="utf-8")
    os.utime(config, (1000, 1000))
    _write_outputs(tmp_path / "results", stamp=2000)

    assert simulation_freshness(config, tmp_path / "results") is None


def test_configured_path_defaults_to_project_root(tmp_path: Path, monkeypatch) -> None:
    monkeypatch.delenv("PET_TEST_PATH", raising=False)
    monkeypatch.setattr(launcher, "PROJECT_ROOT", tmp_path)

    assert configured_path("PET_TEST_PATH", Path("data") / "input_pet.json") == tmp_path / "data" / "input_pet.json"


def test_configured_path_resolves_relative_and_absolute(tmp_path: Path, monkeypatch) -> None:
    monkeypatch.setattr(launcher, "PROJECT_ROOT", tmp_path)

    monkeypatch.setenv("PET_TEST_PATH", "results/custom")
    assert configured_path("PET_TEST_PATH", Path("ignored")) == tmp_path / "results" / "custom"

    absolute = tmp_path / "elsewhere"
    monkeypatch.setenv("PET_TEST_PATH", str(absolute))
    assert configured_path("PET_TEST_PATH", Path("ignored")) == absolute


def test_missing_dependencies_is_empty() -> None:
    assert missing_dependencies() == []


def test_is_our_api_accepts_own_title(monkeypatch) -> None:
    body = json.dumps({"info": {"title": "PET Digital Twin API"}})
    monkeypatch.setattr(launcher, "http_get", lambda url, timeout=2.0: (200, body))

    assert is_our_api("127.0.0.1", 8000) is True


def test_is_our_api_rejects_foreign_service(monkeypatch) -> None:
    body = json.dumps({"info": {"title": "Some Other API"}})
    monkeypatch.setattr(launcher, "http_get", lambda url, timeout=2.0: (200, body))

    assert is_our_api("127.0.0.1", 8000) is False


def test_is_our_api_handles_offline_and_bad_body(monkeypatch) -> None:
    monkeypatch.setattr(launcher, "http_get", lambda url, timeout=2.0: None)
    assert is_our_api("127.0.0.1", 8000) is False

    monkeypatch.setattr(launcher, "http_get", lambda url, timeout=2.0: (200, "no es json"))
    assert is_our_api("127.0.0.1", 8000) is False

    monkeypatch.setattr(launcher, "http_get", lambda url, timeout=2.0: (404, ""))
    assert is_our_api("127.0.0.1", 8000) is False


def test_parser_defaults_and_flags() -> None:
    defaults = build_parser().parse_args([])
    assert defaults.host == "127.0.0.1"
    assert defaults.port == 8000
    assert defaults.log_level == "info"
    assert defaults.force_sim is False
    assert defaults.skip_sim is False
    assert defaults.restart is False
    assert defaults.no_browser is False

    parsed = build_parser().parse_args(["--port", "8010", "--force-sim", "--skip-sim", "--restart", "--no-browser"])
    assert parsed.port == 8010
    assert parsed.force_sim and parsed.skip_sim and parsed.restart and parsed.no_browser


def test_force_sim_and_skip_sim_are_incompatible() -> None:
    import pytest

    with pytest.raises(SystemExit):
        launcher.main(["--force-sim", "--skip-sim"])


def test_port_is_open_detects_listener_then_released_port() -> None:
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as sock:
        sock.bind(("127.0.0.1", 0))
        sock.listen(1)
        port = sock.getsockname()[1]
        assert launcher.port_is_open("127.0.0.1", port) is True
    assert launcher.port_is_open("127.0.0.1", port) is False


def test_stop_server_terminates_running_child() -> None:
    process = subprocess.Popen([sys.executable, "-c", "import time; time.sleep(60)"])
    try:
        launcher.stop_server(process, grace=5.0)
        assert process.poll() is not None
    finally:
        if process.poll() is None:
            process.kill()
            process.wait()


def test_stop_server_is_noop_when_child_already_exited() -> None:
    process = subprocess.Popen([sys.executable, "-c", "raise SystemExit(0)"])
    process.wait()
    launcher.stop_server(process, grace=5.0)
    assert process.returncode == 0


def test_wait_until_ready_bails_out_when_process_died() -> None:
    process = subprocess.Popen([sys.executable, "-c", "raise SystemExit(0)"])
    process.wait()

    started = time.monotonic()
    ready = launcher.wait_until_ready("127.0.0.1", 1, process=process)

    assert ready is False
    assert time.monotonic() - started < 5

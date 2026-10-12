"""kicad-cli invocation. Never runs jobsets, plugins, or scripts from an archive."""

from __future__ import annotations

import os
import shutil
import subprocess
from pathlib import Path

from app.errors import kicad_failed, timed_out

# KiCad 10.0.6 (image kicad/kicad:10.0.6), from the 10.0.6 tag:
# eeschema/sch_file_versions.h SEXPR_SCHEMATIC_FILE_VERSION
# pcbnew/pcb_io/kicad_sexpr/pcb_io_kicad_sexpr.h SEXPR_BOARD_FILE_VERSION
KICAD_IMAGE_TAG = "10.0.6"
SUPPORTED_SCHEMATIC_VERSION = 20260306
SUPPORTED_BOARD_VERSION = 20260206

STDERR_LIMIT = 2000
_VERSION: str | None = None


def kicad_cli_path() -> str | None:
    return shutil.which("kicad-cli")


def kicad_version() -> str:
    global _VERSION
    if _VERSION is not None:
        return _VERSION
    if kicad_cli_path() is None:
        return "unknown"
    for args in (["kicad-cli", "version"], ["kicad-cli", "-v"]):
        try:
            proc = subprocess.run(args, capture_output=True, text=True, timeout=30, check=False)
        except (OSError, subprocess.TimeoutExpired):
            continue
        text = (proc.stdout or proc.stderr or "").strip()
        if text:
            _VERSION = text.splitlines()[0].strip()
            return _VERSION
    _VERSION = "unknown"
    return _VERSION


def config_version_dir() -> str:
    version = kicad_version()
    parts = version.split(".")
    if len(parts) >= 2 and parts[0].isdigit() and parts[1].isdigit():
        return f"{parts[0]}.{parts[1]}"
    return "10.0"


def assert_safe_args(args: list[str]) -> None:
    """Refuse to execute jobsets, plugins, or scripts shipped in a project archive."""
    for arg in args:
        lower = arg.replace("\\", "/").lower()
        base = lower.rsplit("/", 1)[-1]
        if base.endswith(".kicad_jobset") or base.endswith(".py") or base.endswith(".js"):
            raise kicad_failed("refusing to execute a jobset, plugin, or script from the archive")
        if "kicad-cli" in base:
            continue
    # The executable is always kicad-cli itself, never a file from the archive.
    if not args or Path(args[0]).name not in {"kicad-cli", "kicad-cli.exe"}:
        raise kicad_failed("refusing to execute a jobset, plugin, or script from the archive")


def run_kicad(args: list[str], *, cwd: Path, env: dict[str, str], timeout: float) -> subprocess.CompletedProcess[bytes]:
    assert_safe_args(args)
    if kicad_cli_path() is None:
        raise kicad_failed("kicad-cli is not installed")
    try:
        proc = subprocess.run(
            args,
            cwd=str(cwd),
            env=env,
            capture_output=True,
            timeout=timeout,
            check=False,
        )
    except subprocess.TimeoutExpired as exc:
        raise timed_out("kicad-cli timed out") from exc
    except OSError as exc:
        raise kicad_failed(f"kicad-cli failed to start: {exc}") from exc
    if proc.returncode != 0:
        err = proc.stderr.decode("utf-8", errors="replace").strip()
        if not err:
            err = proc.stdout.decode("utf-8", errors="replace").strip()
        if len(err) > STDERR_LIMIT:
            err = err[-STDERR_LIMIT:]
        raise kicad_failed(err or f"kicad-cli exited with status {proc.returncode}")
    return proc


def job_env(config_home: Path, home: Path) -> dict[str, str]:
    env = os.environ.copy()
    env["KICAD_CONFIG_HOME"] = str(config_home)
    env["HOME"] = str(home)
    env["XDG_CONFIG_HOME"] = str(config_home)
    env["XDG_CACHE_HOME"] = str(home / "cache")
    env.pop("KICAD_RUN_FROM_BUILD_DIR", None)
    return env


def write_empty_tables(config_home: Path) -> None:
    """KiCad appends the major.minor directory under KICAD_CONFIG_HOME."""
    version_dir = config_home / config_version_dir()
    version_dir.mkdir(parents=True, exist_ok=True)
    (version_dir / "sym-lib-table").write_text("(sym_lib_table\n  (version 7)\n)\n", encoding="utf-8")
    (version_dir / "fp-lib-table").write_text("(fp_lib_table\n  (version 7)\n)\n", encoding="utf-8")

"""Calls kicad-cli. Skipped when KiCad is not installed (Windows dev machines)."""

import io
import json
import tarfile
import zipfile
from pathlib import Path

import pytest

from app.analyze import analyze
from app.cli import job_env, kicad_cli_path, write_empty_tables
from app.outputs import produce
from app.render import render
from app.visual_diff import visual_diff

FIXTURES = Path(__file__).resolve().parents[1] / "fixtures"

pytestmark = pytest.mark.skipif(
    kicad_cli_path() is None,
    reason="kicad-cli is not installed",
)


def _env(tmp_path: Path) -> dict:
    config = tmp_path / "kicad-config"
    config.mkdir()
    write_empty_tables(config)
    return job_env(config, tmp_path)


def _tar_dir(folder: Path) -> bytes:
    buf = io.BytesIO()
    with tarfile.open(fileobj=buf, mode="w") as archive:
        for path in folder.rglob("*"):
            if path.is_file():
                archive.add(path, arcname=path.relative_to(folder).as_posix())
    return buf.getvalue()


def test_analyze_render_and_visual_diff(tmp_path: Path):
    env = _env(tmp_path)
    result = analyze(
        FIXTURES / "flat", {"projectFile": "flat.kicad_pro"},
        env=env, timeout=120, kicad="10.0.6",
    )
    refs = {item["reference"] for item in result["components"]}
    assert "R1" in refs
    assert result["components"]
    blob = render(
        FIXTURES / "flat",
        {"projectFile": "flat.kicad_pro", "schematic": True, "board": ["front", "back"]},
        env=env, timeout=180, kicad="10.0.6",
    )
    with zipfile.ZipFile(io.BytesIO(blob)) as archive:
        names = set(archive.namelist())
    assert "manifest.json" in names
    assert "sch/p1.svg" in names
    assert "pcb/front.svg" in names
    diff = visual_diff(
        FIXTURES / "flat", FIXTURES / "flat_v2",
        {"projectFile": "flat.kicad_pro", "base": "v1", "target": "v2", "board": ["front", "back"]},
        env=env, timeout=300, kicad="10.0.6",
        base_bytes=_tar_dir(FIXTURES / "flat"),
        target_bytes=_tar_dir(FIXTURES / "flat_v2"),
    )
    with zipfile.ZipFile(io.BytesIO(diff)) as archive:
        manifest = json.loads(archive.read("manifest.json"))
    assert manifest["base"] == "v1"
    assert any(layer.get("aligned") is True for page in manifest["pages"] for layer in page["layers"])
    hier = analyze(
        FIXTURES / "hier", {"projectFile": "hier.kicad_pro"},
        env=env, timeout=120, kicad="10.0.6",
    )
    by_ref = {item["reference"]: item["symbolPath"] for item in hier["components"]}
    assert "R1" in by_ref and "R11" in by_ref
    assert by_ref["R1"] != by_ref["R11"]


def test_outputs_pdf_bom_and_step(tmp_path: Path):
    env = _env(tmp_path)
    blob = produce(
        FIXTURES / "flat",
        {
            "projectFile": "flat.kicad_pro",
            "outputs": ["schematic_pdf", "bom_csv", "step"],
            "bom": [{
                "reference": "R1",
                "value": "10k",
                "footprint": "R_0805",
                "mpn": "RC0805",
                "quantity": 1,
            }],
        },
        env=env,
        timeout=180,
        kicad="10.0.6",
        work=tmp_path / "out",
    )
    with zipfile.ZipFile(io.BytesIO(blob)) as archive:
        manifest = json.loads(archive.read("manifest.json"))
        bom = archive.read("bom.csv").decode()
        assert archive.read("schematic.pdf")[:4] == b"%PDF"
    kinds = {item["kind"]: item["fileName"] for item in manifest["outputs"]}
    assert kinds["schematic_pdf"] == "schematic.pdf"
    assert kinds["bom_csv"] == "bom.csv"
    assert "RC0805" in bom
    if "step" in kinds:
        assert kinds["step"] == "board.step"

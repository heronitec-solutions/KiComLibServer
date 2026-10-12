"""Unit tests for fabrication outputs. kicad-cli is not required."""

import io
import json
import zipfile
from pathlib import Path

import pytest

from app.errors import RunnerError
from app.outputs import (
    bom_csv_bytes,
    bom_rows_from_netlist,
    build_manifest,
    check_summary,
    find_model_vars,
    parse_output_kinds,
    safe_extract_zip,
    step_command,
    step_environment,
)


def test_manifest_lists_kind_filename_and_check_counts():
    summary = check_summary(json.dumps({
        "violations": [
            {"severity": "error", "description": "pin"},
            {"severity": "warning", "description": "clearance"},
        ]
    }).encode())
    manifest = build_manifest("10.0.6", [
        {"kind": "schematic_pdf", "fileName": "schematic.pdf"},
        {"kind": "erc_json", "fileName": "erc.json", "summary": summary},
        {"kind": "drc_json", "fileName": "drc.json", "summary": {"errors": 2, "warnings": 0}},
    ])
    assert manifest["kicad"] == "10.0.6"
    assert manifest["outputs"][0] == {"kind": "schematic_pdf", "fileName": "schematic.pdf"}
    assert manifest["outputs"][1]["summary"] == {"errors": 1, "warnings": 1}
    assert manifest["outputs"][2]["summary"]["errors"] == 2


def test_unknown_output_is_invalid():
    with pytest.raises(RunnerError) as caught:
        parse_output_kinds({"outputs": ["schematic_pdf", "not_an_output"]})
    assert caught.value.status == 400
    assert caught.value.code == "invalid"
    assert "unknown output" in caught.value.message


def test_bom_csv_includes_mpn_and_quantity():
    data = bom_csv_bytes([
        {"reference": "R1, R2", "value": "10k", "footprint": "R_0805", "mpn": "RC0805", "quantity": 2},
    ])
    text = data.decode()
    assert text.splitlines()[0] == "reference,value,footprint,MPN,quantity"
    assert "R1, R2" in text
    assert "RC0805" in text
    assert text.endswith("2") or ",2" in text.splitlines()[1]


def test_bom_from_netlist_groups_and_skips_dnp():
    xml = """<?xml version="1.0"?>
    <export>
      <components>
        <comp ref="R2"><value>10k</value><footprint>R_0805</footprint>
          <fields><field name="MPN">RC0805</field></fields></comp>
        <comp ref="R1"><value>10k</value><footprint>R_0805</footprint>
          <fields><field name="MPN">RC0805</field></fields></comp>
        <comp ref="R9"><value>0R</value><footprint>R_0805</footprint>
          <property name="dnp" value="yes"/></comp>
      </components>
    </export>
    """
    rows = bom_rows_from_netlist(xml)
    assert len(rows) == 1
    assert rows[0]["reference"] == "R1, R2"
    assert rows[0]["mpn"] == "RC0805"
    assert rows[0]["quantity"] == 2


def test_step_define_var_uses_the_path_key_from_the_board(tmp_path: Path):
    board = tmp_path / "demo.kicad_pcb"
    board.write_text('(kicad_pcb (model "${DEMO_3DMODEL_DIR}/R.3dshapes/r.step"))\n', encoding="utf-8")
    names = find_model_vars(tmp_path)
    assert names == ["DEMO_3DMODEL_DIR"]
    models = tmp_path / "models"
    cmd = step_command(board, tmp_path / "board.step", models, names, None)
    assert cmd[0] == "kicad-cli"
    assert "export" in cmd and "step" in cmd
    assert "--define-var" in cmd
    defined = cmd[cmd.index("--define-var") + 1]
    assert defined.startswith("DEMO_3DMODEL_DIR=")
    assert defined.endswith("models") or defined.endswith("models".replace("/", "\\")) or str(models) in defined
    env = step_environment({"HOME": "/tmp"}, models, names)
    assert env["DEMO_3DMODEL_DIR"] == str(models)
    assert "KICAD10_3DMODEL_DIR" not in find_model_vars_with_stock(tmp_path)


def find_model_vars_with_stock(root: Path):
    text_path = root / "stock.kicad_pcb"
    text_path.write_text('(model "${KICAD10_3DMODEL_DIR}/x.step")\n', encoding="utf-8")
    return find_model_vars(root)


def test_models_zip_rejects_parent_paths(tmp_path: Path):
    buf = io.BytesIO()
    with zipfile.ZipFile(buf, "w") as archive:
        archive.writestr("../evil.step", b"nope")
    with pytest.raises(RunnerError) as caught:
        safe_extract_zip(buf.getvalue(), tmp_path / "models")
    assert caught.value.status == 400
    assert not (tmp_path / "evil.step").exists()

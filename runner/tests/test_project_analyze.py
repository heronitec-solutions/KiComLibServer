from pathlib import Path

import pytest

from app.analyze import build_analyze_response, components_from_netlist, join_symbol_path
from app.errors import RunnerError
from app.project import category_of, load_project
from app.sexpr import parse

FIXTURES = Path(__file__).resolve().parents[1] / "fixtures"


def test_symbol_path_joins_sheet_and_first_uuid():
    assert join_symbol_path("/", "aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee") == (
        "/aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee"
    )
    assert join_symbol_path("/28f12a69-0000-4000-8000-000000000001/", "00e27837-0000-4000-8000-000000000002") == (
        "/28f12a69-0000-4000-8000-000000000001/00e27837-0000-4000-8000-000000000002"
    )


def test_flat_project_pages_board_and_unlinked():
    project = load_project(FIXTURES / "flat", "flat.kicad_pro")
    assert project.schematic_version == 20250114
    assert project.board_version == 20250114
    assert project.title_block["title"] == "Flat"
    assert project.title_block["rev"] == "A"
    assert [page.number for page in project.pages] == ["1"]
    assert project.pages[0].sheet_path == "/"
    assert project.board is not None
    assert project.board.footprints == 6
    assert [item["reference"] for item in project.board.unlinked] == ["H1"]
    assert project.board.crop == [40.0, 30.0, 220.0, 160.0]


def test_hierarchy_repeated_sheet_instances():
    project = load_project(FIXTURES / "hier", "hier.kicad_pro")
    by_name = {page.sheet_names: page for page in project.pages}
    assert set(by_name) == {"/", "/CH1/", "/CH2/"}
    ch1 = by_name["/CH1/"]
    ch2 = by_name["/CH2/"]
    assert ch1.file == "channel.kicad_sch"
    assert ch2.file == "channel.kicad_sch"
    assert ch1.sheet_path == "/44444444-4444-4444-8444-444444444444/"
    assert ch2.sheet_path == "/55555555-5555-4555-8555-555555555555/"
    assert ch1.number == "2"
    assert ch2.number == "3"
    assert ch1.file_uuid == ch2.file_uuid
    assert ch1.full_path != ch2.full_path


def test_newer_file_version_is_unsupported(tmp_path: Path):
    (tmp_path / "x.kicad_pro").write_text("{}", encoding="utf-8")
    (tmp_path / "x.kicad_sch").write_text(
        '(kicad_sch (version 20990101) (uuid "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa") (paper "A4"))',
        encoding="utf-8",
    )
    with pytest.raises(RunnerError) as caught:
        load_project(tmp_path, "x.kicad_pro")
    assert caught.value.status == 422
    assert caught.value.code == "unsupported_version"


def test_missing_sheet_file(tmp_path: Path):
    (tmp_path / "x.kicad_pro").write_text("{}", encoding="utf-8")
    (tmp_path / "x.kicad_sch").write_text(
        """(kicad_sch (version 20250114) (uuid "11111111-1111-4111-8111-111111111111") (paper "A4")
          (sheet (uuid "44444444-4444-4444-8444-444444444444")
            (property "Sheetname" "CH1") (property "Sheetfile" "missing.kicad_sch")
            (instances (project "x" (path "/11111111-1111-4111-8111-111111111111" (page "2")))))
          (sheet_instances (path "/" (page "1"))))""",
        encoding="utf-8",
    )
    with pytest.raises(RunnerError) as caught:
        load_project(tmp_path, "x.kicad_pro")
    assert caught.value.code == "missing_files"
    assert "export_scope" in caught.value.message


def test_flags_from_property_or_schematic():
    project = load_project(FIXTURES / "flat", "flat.kicad_pro")
    r1 = "b1111111-1111-4111-8111-111111111111"
    d1 = "e1111111-1111-4111-8111-111111111111"
    xml = f"""<?xml version="1.0" encoding="UTF-8"?>
    <export version="E">
      <components>
        <comp ref="R1">
          <value>10k</value>
          <footprint>Resistor_SMD:R_0603_1608Metric</footprint>
          <fields>
            <field name="MPN">RC0603FR-0710KL</field>
            <field name="Footprint">ignored</field>
            <field name="KiComLibUUID">aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee</field>
          </fields>
          <libsource lib="Device" part="R"/>
          <property name="exclude_from_bom"/>
          <sheetpath names="/" tstamps="/"/>
          <tstamps>{r1}</tstamps>
        </comp>
        <comp ref="D1">
          <value>DNP</value>
          <footprint>Resistor_SMD:R_0603_1608Metric</footprint>
          <fields></fields>
          <libsource lib="Device" part="R"/>
          <sheetpath names="/" tstamps="/"/>
          <tstamps>{d1}</tstamps>
        </comp>
      </components>
    </export>
    """
    components, _warnings = components_from_netlist(xml, project)
    by_ref = {item["reference"]: item for item in components}
    assert by_ref["R1"]["symbolPath"] == f"/{r1}"
    assert by_ref["R1"]["libId"] == "Device:R"
    assert by_ref["R1"]["fields"]["MPN"] == "RC0603FR-0710KL"
    assert "Footprint" not in by_ref["R1"]["fields"]
    assert by_ref["R1"]["excludeFromBom"] is True
    assert by_ref["R1"]["dnp"] is False
    # No flag properties: read dnp / in_bom / on_board from the symbol.
    assert by_ref["D1"]["dnp"] is True
    assert by_ref["D1"]["excludeFromBom"] is True
    assert by_ref["D1"]["excludeFromBoard"] is True
    response = build_analyze_response(project, xml, "10.0.6")
    assert response["kicad"] == "10.0.6"
    assert response["files"]["schematicVersion"] == 20250114
    assert response["board"]["present"] is True
    assert response["netlistXml"].startswith("<?xml")


def test_hierarchy_symbol_paths_from_netlist():
    project = load_project(FIXTURES / "hier", "hier.kicad_pro")
    ch1 = "44444444-4444-4444-8444-444444444444"
    sym = "66666666-6666-4666-8666-666666666666"
    xml = f"""<export><components>
      <comp ref="R1">
        <value>10k</value>
        <libsource lib="Device" part="R"/>
        <fields><field name="MPN">RC0603FR-0710KL</field></fields>
        <sheetpath names="/CH1/" tstamps="/{ch1}/"/>
        <tstamps>{sym}</tstamps>
      </comp>
      <comp ref="R11">
        <value>10k</value>
        <libsource lib="Device" part="R"/>
        <fields><field name="MPN">RC0603FR-0710KL</field></fields>
        <sheetpath names="/CH2/" tstamps="/55555555-5555-4555-8555-555555555555/"/>
        <tstamps>{sym} </tstamps>
      </comp>
    </components></export>"""
    components, warnings = components_from_netlist(xml, project)
    assert warnings == []
    assert components[0]["symbolPath"] == f"/{ch1}/{sym}"
    assert components[0]["sheetNames"] == "/CH1/"
    assert components[1]["reference"] == "R11"
    assert components[1]["symbolUuids"] == [sym]


def test_categories():
    assert category_of("symbol", "R1") == "parts"
    assert category_of("symbol", "#PWR01") == "wiring"
    assert category_of("wire", None) == "wiring"
    assert category_of("text", None) == "graphics"
    assert category_of("arc", None, "schematic") == "graphics"
    assert category_of("arc", None, "board") == "routing"
    assert category_of("via", None, "board") == "routing"
    assert category_of("zone", None, "board") == "zones"
    assert category_of("gr_line", None, "board") == "graphics"
    assert category_of("footprint", "R1", "board") == "parts"
    parse('(symbol (property "Reference" "#PWR01"))')

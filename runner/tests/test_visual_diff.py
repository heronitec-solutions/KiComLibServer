import io
import json
import zipfile
from pathlib import Path

from app.project import Page, load_project
from app.render import _match_pages, board_svg_command, export_schematic_pages, schematic_svg_command
from app.sexpr import parse
from app.visual_diff import (
    build_zip,
    color_svg,
    compute_diff,
    filtered_document,
    rewrite_symbol_instances,
    write_sheet_as_root,
)

FIXTURES = Path(__file__).resolve().parents[1] / "fixtures"
SVG = (
    '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 297 210">'
    '<path style="fill:none;stroke:#000000;stroke-width:0.1" d="M0 0"/>'
    '<rect style="fill:#FFFF00;stroke:#0000FF;stroke-width:0.3"/>'
    "</svg>"
)


def _changes(page):
    return page["changes"]


def _find(pages, *, status, reference, kind=None):
    found = []
    for page in pages:
        for change in page["changes"]:
            if change["status"] == status and change.get("reference") == reference:
                if kind is None or change["kind"] == kind:
                    found.append((page, change))
    return found


def test_flat_revision_classification():
    base = load_project(FIXTURES / "flat", "flat.kicad_pro")
    target = load_project(FIXTURES / "flat_v2", "flat.kicad_pro")
    diff = compute_diff(base, target)
    sch = [page for page in diff["pages"] if page["kind"] == "schematic"]
    front = next(page for page in diff["pages"] if page.get("side") == "front")
    back = next(page for page in diff["pages"] if page.get("side") == "back")

    assert _find(sch, status="changed", reference="R1", kind="symbol")
    r1 = _find(sch, status="changed", reference="R1", kind="symbol")[0][1]
    assert r1["detail"]["Value"] == ["10k", "4.7k"]
    assert r1["category"] == "parts"

    assert _find(sch, status="added", reference="R2", kind="symbol")
    assert _find(sch, status="removed", reference="C1", kind="symbol")
    assert _find(sch, status="moved", reference="R3", kind="symbol")
    moved = _find(sch, status="moved", reference="R3", kind="symbol")[0][1]
    assert moved["at"] == [[160.0, 90.0]]

    renamed = _find(sch, status="renamed", reference="R40", kind="symbol")
    assert renamed and renamed[0][1]["detail"]["reference"] == ["R4", "R40"]

    replaced = _find(sch, status="replaced", reference="P1", kind="symbol")
    assert len(replaced) == 1
    assert replaced[0][1]["detail"]["from"]["libId"] == "Connector:Pin"
    assert replaced[0][1]["detail"]["to"]["libId"] == "Connector:TestPoint"

    graphic = _find(sch, status="changed", reference="C2", kind="symbol")[0][1]
    assert graphic["detail"]["graphic"] == "symbol graphic updated"

    u1 = _find(sch, status="changed", reference="U1", kind="symbol")
    assert len(u1) == 1
    assert len(u1[0][1]["at"]) == 2

    assert _find(sch, status="added", reference="", kind="wire") or any(
        change["kind"] == "wire" and change["status"] == "added"
        for page in sch for change in page["changes"]
    )
    assert any(change["kind"] == "text" and change["status"] == "moved" for change in sch[0]["changes"])

    assert _find([front], status="changed", reference="R1", kind="footprint")
    assert _find([front], status="moved", reference="R3", kind="footprint")
    assert _find([front], status="replaced", reference="P1", kind="footprint")
    assert not any(change.get("reference") == "H1" for change in front["changes"])
    assert not any(change["kind"] == "zone" for change in front["changes"])
    assert any(change["kind"] == "segment" and change["status"] == "changed" for change in front["changes"])
    assert any(change["kind"] == "via" and change["status"] == "moved" for change in front["changes"])

    assert front["crop"] == [40.0, 30.0, 220.0, 160.0]
    # A4 width 297 mm; back side is mirrored on X.
    assert back["crop"][0] == 297.0 - 220.0
    via = next(change for change in back["changes"] if change["kind"] == "via")
    assert via["at"][0][0] == 297.0 - 112.0

    summary = diff["summary"]
    assert summary["parts"]["replaced"] == 2
    assert summary["parts"]["renamed"] == 1
    assert summary["wiring"]["added"] == 1
    assert summary["graphics"]["moved"] == 1
    assert summary["routing"]["changed"] == 1
    assert summary["routing"]["moved"] == 1


def test_instance_rewrite_uses_sheet_file_uuid_and_instance_reference():
    project = load_project(FIXTURES / "hier", "hier.kicad_pro")
    page = next(item for item in project.pages if item.sheet_names == "/CH2/")
    doc = project.sheets["channel.kicad_sch"]
    symbol = next(obj for obj in doc.objects if obj.kind == "symbol")
    rewritten = rewrite_symbol_instances(
        symbol.raw,
        project="hier",
        sheet_file_uuid=doc.file_uuid,
        reference="R11",
        unit=1,
    )
    node = parse(rewritten)
    paths = []
    for block in node.children("instances"):
        for project_node in block.items[1:]:
            for path_node in project_node.items[1:]:
                if path_node.name == "path":
                    paths.append(path_node)
    assert len(paths) == 1
    assert paths[0].items[1].value == "/" + page.file_uuid
    assert paths[0].atom_arg("reference") == "R11"
    assert paths[0].atom_arg("unit") == "1"
    assert page.full_path.endswith("55555555-5555-4555-8555-555555555555")


def test_filtered_copy_keeps_header_and_drops_group():
    text = """(kicad_sch
  (version 20250114)
  (paper "A4")
  (lib_symbols (symbol "Device:R" (property "Value" "R")))
  (symbol (lib_id "Device:R") (uuid "keep") (at 1 2 0))
  (wire (pts (xy 0 0) (xy 1 1)) (uuid "drop"))
  (group (uuid "g") (members "keep"))
  (generated (uuid "gen"))
)"""
    filtered = filtered_document(text, {"keep"}, domain="schematic", as_root_sheet=False)
    assert "(version 20250114)" in filtered
    assert "lib_symbols" in filtered
    assert 'uuid "keep"' in filtered
    assert 'uuid "drop"' not in filtered
    assert "(group " not in filtered
    assert "(generated " not in filtered


def test_color_svg_recolors_widens_and_fades_fills():
    colored = color_svg(SVG, "#16a34a")
    assert "stroke:#16a34a" in colored
    assert "fill:#16a34a" in colored
    assert "fill:none" in colored
    assert "stroke-width:0.2" in colored
    assert "stroke-width:0.54" in colored
    assert "fill-opacity:0.25" in colored
    assert 'viewBox="0 0 297 210"' in colored
    # The unfilled stroke does not gain a fill opacity of its own beyond the filled rect.
    assert colored.count("fill-opacity:0.25") == 1


def test_manifest_schema_and_aligned_layers():
    base = load_project(FIXTURES / "flat", "flat.kicad_pro")
    target = load_project(FIXTURES / "flat_v2", "flat.kicad_pro")

    def schematic_exporter(_path, highlight, page):
        return SVG.encode("utf-8")

    def board_exporter(_path, side):
        return SVG.encode("utf-8")

    blob = build_zip(
        base, target,
        {"projectFile": "flat.kicad_pro", "base": "abc123", "target": "def456", "board": ["front"]},
        kicad="10.0.6",
        schematic_exporter=schematic_exporter,
        board_exporter=board_exporter,
    )
    with zipfile.ZipFile(io.BytesIO(blob)) as archive:
        manifest = json.loads(archive.read("manifest.json"))
        names = set(archive.namelist())
    assert manifest["version"] == 1
    assert manifest["kicad"] == "10.0.6"
    assert manifest["base"] == "abc123"
    assert manifest["target"] == "def456"
    assert "parts" in manifest["summary"]
    page = next(item for item in manifest["pages"] if item["id"] == "sch:1")
    assert page["viewBox"] == [0, 0, 297, 210]
    assert page["base"] == "sch/p1/base.svg"
    assert page["layers"]
    assert all(layer["aligned"] is True for layer in page["layers"])
    assert all(layer["file"] in names for layer in page["layers"])
    sample = archive_layer(blob, page["layers"][0]["file"])
    assert "#16a34a" in sample or "#dc2626" in sample or "#ea580c" in sample or "#2563eb" in sample
    board = next(item for item in manifest["pages"] if item["id"] == "pcb:front")
    assert "crop" in board
    assert all(item["id"] != "pcb:back" for item in manifest["pages"])


def archive_layer(blob: bytes, name: str) -> str:
    with zipfile.ZipFile(io.BytesIO(blob)) as archive:
        return archive.read(name).decode("utf-8")


def test_board_svg_command_never_uses_page_size_mode_2(tmp_path: Path):
    board = tmp_path / "flat.kicad_pcb"
    front = board_svg_command(board, tmp_path / "front.svg", "front")
    back = board_svg_command(board, tmp_path / "back.svg", "back")
    assert front[front.index("--page-size-mode") + 1] == "1"
    assert "--mirror" not in front
    assert "F.Cu,F.Silkscreen,F.Fab,Edge.Cuts" in front
    assert back[back.index("--page-size-mode") + 1] == "1"
    assert "--mirror" in back
    assert "B.Cu,B.Silkscreen,B.Fab,Edge.Cuts" in back
    assert "--page-size-mode" in front and "2" != front[front.index("--page-size-mode") + 1]
    highlight = schematic_svg_command(tmp_path / "flat.kicad_sch", tmp_path / "out", highlight=True)
    assert "-e" in highlight and "-n" in highlight
    assert "--page-size-mode" not in highlight


def test_match_pages_binds_child_sheets_by_sheet_name(tmp_path: Path):
    pages = [
        Page("1", "/", "/", "hier.kicad_sch", "A4", "/", "22222222-2222-4222-8222-222222222222"),
        Page("2", "/a", "/CH1/", "channel.kicad_sch", "A4", "/a", "33333333-3333-4333-8333-333333333333"),
        Page("3", "/b", "/CH2/", "channel.kicad_sch", "A4", "/b", "33333333-3333-4333-8333-333333333333"),
    ]
    files = [tmp_path / "hier.svg", tmp_path / "hier-CH1.svg", tmp_path / "hier-CH2.svg"]
    for path in files:
        path.write_text("<svg/>", encoding="utf-8")
    mapping = _match_pages(files, pages, "hier")
    assert mapping["1"] == files[0]
    assert mapping["2"] == files[1]
    assert mapping["3"] == files[2]


def test_child_sheet_copy_is_a_root_sheet_without_the_parent(tmp_path: Path):
    project = load_project(FIXTURES / "hier", "hier.kicad_pro")
    page = next(item for item in project.pages if item.number == "3")
    dest = write_sheet_as_root(tmp_path, project, page)
    text = dest.read_text(encoding="utf-8")
    assert "Hierarchy" not in text
    assert '(path "/" (page "1"))' in text
    assert "R11" in text
    assert "44444444-4444-4444-8444-444444444444" not in text


def test_schematic_pages_are_not_taken_from_the_root_plot(monkeypatch, tmp_path: Path):
    calls = []

    def fake_run(args, **kwargs):
        calls.append(list(args))
        out = Path(args[args.index("-o") + 1])
        out.mkdir(parents=True, exist_ok=True)
        schematic = Path(args[-1])
        if schematic.parent == (FIXTURES / "hier"):
            (out / "hier.svg").write_text("<svg>ROOT</svg>", encoding="utf-8")
            (out / "hier-CH1.svg").write_text("<svg>CH1</svg>", encoding="utf-8")
            (out / "hier-CH2.svg").write_text("<svg>CH2</svg>", encoding="utf-8")
            return
        (out / "page.svg").write_text("<svg>SHEET</svg>", encoding="utf-8")

    monkeypatch.setattr("app.render.run_kicad", fake_run)
    project = load_project(FIXTURES / "hier", "hier.kicad_pro")
    rendered = export_schematic_pages(project, tmp_path / "out", env={}, timeout=30)
    assert rendered["1"] == b"<svg>ROOT</svg>"
    assert rendered["2"] == b"<svg>CH1</svg>"
    assert rendered["3"] == b"<svg>CH2</svg>"
    assert len(calls) == 1
    assert "--pages" not in calls[0]


def test_unmatched_sheet_is_plotted_from_its_own_file(monkeypatch, tmp_path: Path):
    calls = []

    def fake_run(args, **kwargs):
        calls.append(list(args))
        out = Path(args[args.index("-o") + 1])
        out.mkdir(parents=True, exist_ok=True)
        schematic = Path(args[-1])
        if schematic.parent == (FIXTURES / "hier"):
            return
        (out / "page.svg").write_text(b"<svg>SHEET</svg>".decode("utf-8"), encoding="utf-8")

    monkeypatch.setattr("app.render.run_kicad", fake_run)
    project = load_project(FIXTURES / "hier", "hier.kicad_pro")
    rendered = export_schematic_pages(project, tmp_path / "out", env={}, timeout=30)
    assert set(rendered) == {"1", "2", "3"}
    assert all(value == b"<svg>SHEET</svg>" for value in rendered.values())
    root = FIXTURES / "hier" / "hier.kicad_sch"
    assert Path(calls[0][-1]) == root
    assert "--pages" not in calls[0]
    assert len(calls) == 4
    for call in calls[1:]:
        assert Path(call[-1]) != root
        assert call[call.index("--pages") + 1] == "1"


def test_visual_diff_plots_each_revision_once(monkeypatch):
    calls = []

    def fake_export(project, out_dir, **kwargs):
        calls.append(project.project_dir)
        return {page.number: SVG.encode("utf-8") for page in project.pages}

    monkeypatch.setattr("app.visual_diff.export_schematic_pages", fake_export)
    project = load_project(FIXTURES / "hier", "hier.kicad_pro")
    build_zip(project, project, {"projectFile": "hier.kicad_pro"}, kicad="10.0.6", env={}, timeout=30)
    assert calls == [project.project_dir]


def test_cli_refuses_jobset(tmp_path: Path):
    from app.cli import assert_safe_args
    from app.errors import RunnerError

    import pytest
    with pytest.raises(RunnerError):
        assert_safe_args(["kicad-cli", "jobset", str(tmp_path / "board.kicad_jobset")])

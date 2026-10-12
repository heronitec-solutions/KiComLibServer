"""Netlist export and component JSON (plan section 6.3)."""

from __future__ import annotations

import xml.etree.ElementTree as ET
from pathlib import Path

from app.cli import run_kicad
from app.errors import kicad_failed
from app.project import DrawnObject, Project, load_project

_SKIP_FIELDS = {"Reference", "Value", "Footprint", "Datasheet", "Description"}
_FLAG_NAMES = {"dnp", "exclude_from_bom", "exclude_from_board"}


def netlist_command(schematic: Path, output: Path, variant: str | None) -> list[str]:
    cmd = ["kicad-cli", "sch", "export", "netlist", "--format", "kicadxml", "-o", str(output)]
    if variant:
        cmd.extend(["--variant", variant])
    cmd.append(str(schematic))
    return cmd


def _local(tag: str) -> str:
    if "}" in tag:
        return tag.rsplit("}", 1)[-1]
    return tag


def _children(elem: ET.Element, name: str) -> list[ET.Element]:
    return [child for child in list(elem) if _local(child.tag) == name]


def _child(elem: ET.Element, name: str) -> ET.Element | None:
    found = _children(elem, name)
    return found[0] if found else None


def _text(elem: ET.Element, name: str) -> str:
    node = _child(elem, name)
    if node is None or node.text is None:
        return ""
    return node.text.strip()


def _flag_from_property(value: str | None, present: bool) -> bool | None:
    """KiCad 10 emits ``dnp`` / ``exclude_from_bom`` / ``exclude_from_board`` with no value when true."""
    if not present:
        return None
    if value is None or value == "":
        return True
    lowered = value.strip().lower()
    if lowered in {"yes", "true", "1"}:
        return True
    if lowered in {"no", "false", "0"}:
        return False
    return True


def _schematic_flag(symbol: DrawnObject | None, name: str) -> bool:
    if symbol is None:
        return False
    if name == "dnp":
        return (symbol.dnp or "no").lower() in {"yes", "true", "1"}
    if name == "exclude_from_bom":
        return (symbol.in_bom or "yes").lower() in {"no", "false", "0"}
    if name == "exclude_from_board":
        return (symbol.on_board or "yes").lower() in {"no", "false", "0"}
    return False


def join_symbol_path(sheet_tstamps: str, first_uuid: str) -> str:
    """``sheetpath/@tstamps`` has no root UUID. Append the first symbol UUID."""
    sheet = (sheet_tstamps or "/").strip()
    if not sheet.startswith("/"):
        sheet = "/" + sheet
    if not first_uuid:
        return sheet if sheet.endswith("/") else sheet
    if sheet.endswith("/"):
        return sheet + first_uuid
    return sheet + "/" + first_uuid


def split_tstamps(text: str) -> list[str]:
    return [part for part in text.replace(",", " ").split() if part]


def components_from_netlist(xml_text: str, project: Project) -> tuple[list[dict], list[str]]:
    warnings: list[str] = []
    try:
        root = ET.fromstring(xml_text)
    except ET.ParseError as exc:
        raise kicad_failed(f"netlist XML is not valid: {exc}") from exc
    components_node = None
    for child in root.iter():
        if _local(child.tag) == "components":
            components_node = child
            break
    comps_out: list[dict] = []
    if components_node is None:
        return comps_out, warnings
    for comp in _children(components_node, "comp"):
        reference = comp.get("ref") or ""
        value = _text(comp, "value")
        footprint = _text(comp, "footprint")
        lib = _child(comp, "libsource")
        if lib is not None and (lib.get("lib") or lib.get("part")):
            lib_name = lib.get("lib") or ""
            part_name = lib.get("part") or ""
            lib_id = f"{lib_name}:{part_name}" if lib_name else part_name
        else:
            lib_id = ""
        fields: dict[str, str] = {}
        fields_node = _child(comp, "fields")
        field_nodes = _children(fields_node, "field") if fields_node is not None else []
        if not field_nodes:
            field_nodes = _children(comp, "field")
        for field_node in field_nodes:
            fname = field_node.get("name") or ""
            if not fname or fname in _SKIP_FIELDS:
                continue
            fields[fname] = (field_node.text or "").strip()
        flags: dict[str, bool | None] = {}
        for prop in _children(comp, "property"):
            pname = prop.get("name") or ""
            if pname in _FLAG_NAMES:
                flags[pname] = _flag_from_property(prop.get("value"), True)
            elif pname and pname not in _SKIP_FIELDS and pname not in {"Sheetname", "Sheetfile"}:
                if pname not in fields and prop.get("value") is not None:
                    fields[pname] = prop.get("value") or ""
        sheet = _child(comp, "sheetpath")
        sheet_tstamps = sheet.get("tstamps") if sheet is not None and sheet.get("tstamps") else "/"
        sheet_names = sheet.get("names") if sheet is not None and sheet.get("names") else "/"
        uuids = split_tstamps(_text(comp, "tstamps"))
        first = uuids[0] if uuids else ""
        symbol = project.symbol_by_uuid(first) if first else None
        if first and symbol is None:
            for uuid in uuids:
                symbol = project.symbol_by_uuid(uuid)
                if symbol is not None:
                    break
            if symbol is None:
                warnings.append(f"{reference}: symbol {first} was not found in the schematic")
        dnp = flags["dnp"] if "dnp" in flags and flags["dnp"] is not None else _schematic_flag(symbol, "dnp")
        exclude_bom = (
            flags["exclude_from_bom"]
            if "exclude_from_bom" in flags and flags["exclude_from_bom"] is not None
            else _schematic_flag(symbol, "exclude_from_bom")
        )
        exclude_board = (
            flags["exclude_from_board"]
            if "exclude_from_board" in flags and flags["exclude_from_board"] is not None
            else _schematic_flag(symbol, "exclude_from_board")
        )
        comps_out.append({
            "symbolPath": join_symbol_path(sheet_tstamps, first),
            "symbolUuids": uuids,
            "sheetNames": sheet_names,
            "reference": reference,
            "value": value,
            "footprint": footprint,
            "libId": lib_id,
            "fields": fields,
            "dnp": bool(dnp),
            "excludeFromBom": bool(exclude_bom),
            "excludeFromBoard": bool(exclude_board),
        })
    comps_out.sort(key=lambda item: (item["reference"], item["symbolPath"]))
    return comps_out, warnings


def build_analyze_response(project: Project, xml_text: str, kicad: str) -> dict:
    components, warnings = components_from_netlist(xml_text, project)
    warnings = list(project.warnings) + warnings
    pages = [
        {
            "page": page.number,
            "sheetPath": page.sheet_path,
            "sheetNames": page.sheet_names,
            "file": page.file,
            "paper": page.paper,
        }
        for page in project.pages
    ]
    if project.board is None:
        board = {"present": False, "footprints": 0, "unlinkedFootprints": []}
    else:
        board = {
            "present": True,
            "footprints": project.board.footprints,
            "unlinkedFootprints": project.board.unlinked,
        }
    return {
        "kicad": kicad,
        "files": {
            "schematicVersion": project.schematic_version,
            "boardVersion": project.board_version,
            "generatorVersion": project.generator_version,
        },
        "titleBlock": project.title_block,
        "pages": pages,
        "components": components,
        "board": board,
        "netlistXml": xml_text,
        "warnings": warnings,
    }


def export_netlist(project: Project, work: Path, env: dict[str, str], timeout: float, variant: str | None) -> str:
    root_page = next(page for page in project.pages if page.sheet_path == "/")
    schematic = project.project_dir / root_page.file
    output = work / "netlist.xml"
    run_kicad(
        netlist_command(schematic, output, variant),
        cwd=project.project_dir,
        env=env,
        timeout=timeout,
    )
    if not output.is_file():
        raise kicad_failed("kicad-cli did not write a netlist")
    return output.read_text(encoding="utf-8")


def analyze(extract_root: Path, spec: dict, *, env: dict[str, str], timeout: float, kicad: str) -> dict:
    project_file = spec.get("projectFile")
    if not isinstance(project_file, str) or not project_file:
        from app.errors import invalid
        raise invalid("spec.projectFile is required")
    variant = spec.get("variant") or None
    if variant is not None and not isinstance(variant, str):
        from app.errors import invalid
        raise invalid("spec.variant must be a string")
    project = load_project(extract_root, project_file)
    xml_text = export_netlist(project, extract_root, env, timeout, variant)
    return build_analyze_response(project, xml_text, kicad)

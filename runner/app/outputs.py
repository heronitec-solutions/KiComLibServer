"""Fabrication outputs: PDF, Gerber, drill, position, BOM CSV, ERC/DRC, and STEP.

Stdlib only. kicad-cli is invoked for KiCad exports. BOM CSV is written here from
spec rows or from a kicadxml netlist. Jobsets and scripts from the archive are
never executed (see app.cli.assert_safe_args).
"""

from __future__ import annotations

import csv
import io
import json
import re
import shutil
import zipfile
from pathlib import Path, PurePosixPath
from xml.etree import ElementTree as ET

from app.analyze import netlist_command
from app.cli import run_kicad
from app.errors import invalid, kicad_failed
from app.project import load_project

KNOWN_OUTPUTS = (
    "schematic_pdf",
    "gerber_zip",
    "drill_zip",
    "pos_csv",
    "bom_csv",
    "erc_json",
    "drc_json",
    "step",
)

BOARD_OUTPUTS = {"gerber_zip", "drill_zip", "pos_csv", "drc_json", "step"}

FILE_NAMES = {
    "schematic_pdf": "schematic.pdf",
    "gerber_zip": "gerbers.zip",
    "drill_zip": "drill.zip",
    "pos_csv": "position.csv",
    "bom_csv": "bom.csv",
    "erc_json": "erc.json",
    "drc_json": "drc.json",
    "step": "board.step",
}

# ${PATH_KEY_3DMODEL_DIR} as stored in footprint (model ...) paths.
_MODEL_VAR = re.compile(r"\$\{([A-Za-z_][A-Za-z0-9_]*_3DMODEL_DIR)\}")
# Stock KiCad packages. Overriding these would hide the image's own libraries.
_STOCK_MODEL_VAR = re.compile(r"^KICAD\d*_3DMODEL_DIR$")

BOM_COLUMNS = ("reference", "value", "footprint", "MPN", "quantity")


def parse_output_kinds(spec: dict) -> list[str]:
    """Return the requested kinds. An unknown name is a 400."""
    raw = spec.get("outputs")
    if not isinstance(raw, list) or not raw:
        raise invalid("spec.outputs is required")
    kinds: list[str] = []
    for item in raw:
        if not isinstance(item, str) or item not in KNOWN_OUTPUTS:
            shown = item if isinstance(item, str) else type(item).__name__
            raise invalid(f"unknown output '{shown}'")
        if item not in kinds:
            kinds.append(item)
    return kinds


def build_manifest(kicad: str, outputs: list[dict], warnings: list[str] | None = None) -> dict:
    """manifest.json: kind, fileName, and ERC/DRC counts when those files were produced."""
    manifest: dict = {"kicad": kicad or "unknown", "outputs": outputs}
    if warnings:
        manifest["warnings"] = warnings
    return manifest


def bom_csv_bytes(rows: list[dict]) -> bytes:
    """CSV with reference, value, footprint, MPN, and quantity."""
    buf = io.StringIO()
    writer = csv.writer(buf, lineterminator="\n")
    writer.writerow(BOM_COLUMNS)
    for row in rows:
        writer.writerow([
            row.get("reference") or "",
            row.get("value") or "",
            row.get("footprint") or "",
            row.get("mpn") or row.get("MPN") or "",
            row.get("quantity") if row.get("quantity") is not None else 1,
        ])
    return buf.getvalue().encode("utf-8")


def bom_rows_from_spec(spec: dict) -> list[dict] | None:
    """Rows supplied by the backend (catalog MPN included). None means use the netlist."""
    if "bom" not in spec or spec.get("bom") is None:
        return None
    bom = spec.get("bom")
    if not isinstance(bom, list):
        raise invalid("spec.bom must be a list")
    rows: list[dict] = []
    for item in bom:
        if not isinstance(item, dict):
            raise invalid("spec.bom entries must be objects")
        reference = item.get("reference") or item.get("ref") or ""
        if not isinstance(reference, str) or not reference.strip():
            raise invalid("spec.bom reference is required")
        quantity = item.get("quantity", 1)
        if isinstance(quantity, bool) or not isinstance(quantity, int):
            raise invalid("spec.bom quantity must be an integer")
        rows.append({
            "reference": reference.strip(),
            "value": "" if item.get("value") is None else str(item.get("value")),
            "footprint": "" if item.get("footprint") is None else str(item.get("footprint")),
            "mpn": "" if item.get("mpn") is None else str(item.get("mpn") or item.get("MPN") or ""),
            "quantity": quantity,
        })
    return rows


def bom_rows_from_netlist(xml_text: str) -> list[dict]:
    """Group netlist components. DNP and exclude-from-BOM parts are left out."""
    try:
        root = ET.fromstring(xml_text)
    except ET.ParseError as exc:
        raise kicad_failed("kicad-cli netlist was not XML") from exc
    grouped: dict[tuple[str, str, str], list[str]] = {}
    for comp in root.iter():
        if _local(comp.tag) != "comp":
            continue
        if _excluded(comp):
            continue
        reference = (comp.attrib.get("ref") or "").strip()
        if not reference:
            continue
        value = _child_text(comp, "value")
        footprint = _child_text(comp, "footprint")
        mpn = _field(comp, "MPN")
        key = (value, footprint, mpn)
        grouped.setdefault(key, []).append(reference)
    rows = []
    for (value, footprint, mpn), refs in grouped.items():
        ordered = sorted(refs, key=str.upper)
        rows.append({
            "reference": ", ".join(ordered),
            "value": value,
            "footprint": footprint,
            "mpn": mpn,
            "quantity": len(ordered),
        })
    rows.sort(key=lambda row: row["reference"].upper())
    return rows


def check_summary(data: bytes) -> dict | None:
    """Count ERC/DRC violations. Warning severities are counted separately."""
    try:
        payload = json.loads(data.decode("utf-8"))
    except (UnicodeDecodeError, json.JSONDecodeError):
        return None
    if not isinstance(payload, dict):
        return None
    if isinstance(payload.get("error_count"), int) or isinstance(payload.get("warning_count"), int):
        return {
            "errors": int(payload.get("error_count") or 0),
            "warnings": int(payload.get("warning_count") or 0),
        }
    violations = payload.get("violations")
    if not isinstance(violations, list):
        items = payload.get("items")
        violations = items if isinstance(items, list) else None
    if not isinstance(violations, list):
        return None
    errors = 0
    warnings = 0
    for item in violations:
        severity = ""
        if isinstance(item, dict):
            severity = str(item.get("severity") or "").lower()
        if "warn" in severity:
            warnings += 1
        else:
            errors += 1
    return {"errors": errors, "warnings": warnings}


def find_model_vars(root: Path) -> list[str]:
    """Path variables already written in the KiCad files, stock KiCad dirs excluded."""
    found: list[str] = []
    seen: set[str] = set()
    if not root.is_dir():
        return found
    for path in root.rglob("*"):
        if not path.is_file():
            continue
        lower = path.name.lower()
        if not (lower.endswith(".kicad_pcb") or lower.endswith(".kicad_mod") or lower.endswith(".kicad_sch")):
            continue
        try:
            text = path.read_text(encoding="utf-8", errors="replace")
        except OSError:
            continue
        for match in _MODEL_VAR.finditer(text):
            name = match.group(1)
            if _STOCK_MODEL_VAR.match(name) or name in seen:
                continue
            seen.add(name)
            found.append(name)
    return found


def step_command(board: Path, output: Path, models_dir: Path | None, var_names: list[str], variant: str | None) -> list[str]:
    """pcb export step. --define-var points ${VAR} at the extracted KiComLib models."""
    cmd = ["kicad-cli", "pcb", "export", "step", "-o", str(output)]
    if variant:
        cmd.extend(["--variant", variant])
    if models_dir is not None:
        for name in var_names:
            cmd.extend(["--define-var", f"{name}={models_dir}"])
    cmd.append(str(board))
    return cmd


def step_environment(env: dict[str, str], models_dir: Path | None, var_names: list[str]) -> dict[str, str]:
    """Same variable names as --define-var, so the 3D filename resolver can see them."""
    copied = dict(env)
    if models_dir is None:
        return copied
    for name in var_names:
        copied[name] = str(models_dir)
    return copied


def produce(
    extract_root: Path,
    spec: dict,
    *,
    env: dict[str, str],
    timeout: float,
    kicad: str,
    models_zip: bytes | None = None,
    work: Path | None = None,
) -> bytes:
    """Build a zip of the requested files plus manifest.json."""
    if not isinstance(spec, dict):
        raise invalid("spec must be a JSON object")
    kinds = parse_output_kinds(spec)
    project_file = spec.get("projectFile")
    if not isinstance(project_file, str) or not project_file.strip():
        raise invalid("spec.projectFile is required")
    variant = _variant(spec)
    spec_rows = bom_rows_from_spec(spec)

    project = load_project(extract_root, project_file.strip())
    out = work if work is not None else extract_root.parent / "outputs"
    out.mkdir(parents=True, exist_ok=True)
    schematic = project.project_dir / f"{project.name}.kicad_sch"
    board = project.project_dir / project.board.rel if project.board is not None else None

    models_dir: Path | None = None
    if models_zip:
        models_dir = out / "models"
        safe_extract_zip(models_zip, models_dir)
    var_names = find_model_vars(project.project_dir) if models_dir is not None else []

    files: list[tuple[str, bytes]] = []
    manifest_rows: list[dict] = []
    warnings: list[str] = []

    for kind in kinds:
        if kind in BOARD_OUTPUTS and board is None:
            warnings.append(f"skipped {kind}: the project has no board")
            continue
        file_name = FILE_NAMES[kind]
        target = out / file_name
        if kind == "schematic_pdf":
            _run(["kicad-cli", "sch", "export", "pdf", "-o", str(target), *_variant_args(variant), str(schematic)],
                 project.project_dir, env, timeout)
            data = _read(target)
        elif kind == "gerber_zip":
            folder = out / "gerbers"
            if folder.exists():
                shutil.rmtree(folder)
            folder.mkdir()
            _run(["kicad-cli", "pcb", "export", "gerbers", "-o", str(folder), *_variant_args(variant), str(board)],
                 project.project_dir, env, timeout)
            data = _zip_dir(folder)
        elif kind == "drill_zip":
            folder = out / "drill"
            if folder.exists():
                shutil.rmtree(folder)
            folder.mkdir()
            _run(["kicad-cli", "pcb", "export", "drill", "-o", str(folder), *_variant_args(variant), str(board)],
                 project.project_dir, env, timeout)
            data = _zip_dir(folder)
        elif kind == "pos_csv":
            _run(["kicad-cli", "pcb", "export", "pos", "--format", "csv", "-o", str(target), *_variant_args(variant), str(board)],
                 project.project_dir, env, timeout)
            data = _read(target)
        elif kind == "bom_csv":
            rows = spec_rows if spec_rows is not None else bom_rows_from_netlist(
                _netlist(schematic, project.project_dir, env, timeout, variant))
            data = bom_csv_bytes(rows)
        elif kind == "erc_json":
            _run(["kicad-cli", "sch", "erc", "--format", "json", "-o", str(target), *_variant_args(variant), str(schematic)],
                 project.project_dir, env, timeout)
            data = _read(target)
        elif kind == "drc_json":
            _run(["kicad-cli", "pcb", "drc", "--format", "json", "-o", str(target), *_variant_args(variant), str(board)],
                 project.project_dir, env, timeout)
            data = _read(target)
        elif kind == "step":
            command = step_command(board, target, models_dir, var_names, variant)  # type: ignore[arg-type]
            _run(command, project.project_dir, step_environment(env, models_dir, var_names), timeout)
            data = _read(target)
        else:
            raise invalid(f"unknown output '{kind}'")

        entry: dict = {"kind": kind, "fileName": file_name}
        if kind in {"erc_json", "drc_json"}:
            summary = check_summary(data)
            if summary is not None:
                entry["summary"] = summary
        files.append((file_name, data))
        manifest_rows.append(entry)

    manifest = build_manifest(kicad, manifest_rows, warnings)
    return _pack(files, manifest)


def safe_extract_zip(data: bytes, dest: Path) -> None:
    """Unpack a models zip. Absolute paths and '..' are rejected."""
    dest.mkdir(parents=True, exist_ok=True)
    try:
        archive = zipfile.ZipFile(io.BytesIO(data))
    except zipfile.BadZipFile as exc:
        raise invalid(f"invalid models zip: {exc}") from exc
    with archive:
        for info in archive.infolist():
            name = info.filename.replace("\\", "/")
            if not name or name.endswith("/"):
                continue
            if name.startswith("/") or (len(name) >= 2 and name[1] == ":"):
                raise invalid("model archive path escapes the work directory")
            parts = PurePosixPath(name).parts
            if any(part == ".." for part in parts):
                raise invalid("model archive path escapes the work directory")
            target = (dest / name).resolve()
            try:
                target.relative_to(dest.resolve())
            except ValueError as exc:
                raise invalid("model archive path escapes the work directory") from exc
            target.parent.mkdir(parents=True, exist_ok=True)
            with archive.open(info, "r") as src, target.open("wb") as dst:
                shutil.copyfileobj(src, dst)


def _variant(spec: dict) -> str | None:
    variant = spec.get("variant")
    if variant is None or variant == "":
        return None
    if not isinstance(variant, str):
        raise invalid("spec.variant must be a string")
    return variant


def _variant_args(variant: str | None) -> list[str]:
    if not variant:
        return []
    return ["--variant", variant]


def _run(args: list[str], cwd: Path, env: dict[str, str], timeout: float) -> None:
    run_kicad([str(arg) for arg in args], cwd=cwd, env=env, timeout=timeout)


def _read(path: Path) -> bytes:
    if not path.is_file() or path.stat().st_size == 0:
        raise kicad_failed(f"kicad-cli did not write {path.name}")
    return path.read_bytes()


def _zip_dir(folder: Path) -> bytes:
    files = [path for path in folder.rglob("*") if path.is_file()]
    if not files:
        raise kicad_failed(f"kicad-cli wrote no files in {folder.name}")
    buf = io.BytesIO()
    with zipfile.ZipFile(buf, "w", compression=zipfile.ZIP_DEFLATED) as archive:
        for path in files:
            archive.write(path, path.relative_to(folder).as_posix())
    return buf.getvalue()


def _netlist(schematic: Path, cwd: Path, env: dict[str, str], timeout: float, variant: str | None) -> str:
    output = cwd / "_kicomlib_bom_netlist.xml"
    try:
        run_kicad(netlist_command(schematic, output, variant), cwd=cwd, env=env, timeout=timeout)
        if not output.is_file():
            raise kicad_failed("kicad-cli did not write a netlist")
        return output.read_text(encoding="utf-8", errors="replace")
    finally:
        if output.exists():
            output.unlink()


def _pack(files: list[tuple[str, bytes]], manifest: dict) -> bytes:
    buf = io.BytesIO()
    with zipfile.ZipFile(buf, "w", compression=zipfile.ZIP_DEFLATED) as archive:
        archive.writestr("manifest.json", json.dumps(manifest, indent=2).encode("utf-8"))
        for name, data in files:
            archive.writestr(name, data)
    return buf.getvalue()


def _local(tag: str) -> str:
    if "}" in tag:
        return tag.rsplit("}", 1)[-1]
    return tag


def _child_text(elem: ET.Element, name: str) -> str:
    for child in list(elem):
        if _local(child.tag) == name and child.text:
            return child.text.strip()
    return ""


def _field(elem: ET.Element, name: str) -> str:
    for child in elem.iter():
        if _local(child.tag) not in {"field", "property"}:
            continue
        field_name = child.attrib.get("name") or ""
        if field_name.lower() == name.lower():
            return (child.text or "").strip()
    return ""


def _excluded(comp: ET.Element) -> bool:
    for child in comp.iter():
        if _local(child.tag) != "property":
            continue
        name = (child.attrib.get("name") or "").lower()
        if name not in {"dnp", "exclude_from_bom"}:
            continue
        value = (child.attrib.get("value") or child.text or "").strip().lower()
        if value in {"", "yes", "true", "1"}:
            return True
    return False

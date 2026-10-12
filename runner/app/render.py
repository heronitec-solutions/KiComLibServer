"""Schematic page and board SVG export."""

from __future__ import annotations

import io
import json
import zipfile
from pathlib import Path

from app.cli import run_kicad
from app.errors import invalid
from app.project import Page, Project, load_project, paper_size

FRONT_LAYERS = "F.Cu,F.Silkscreen,F.Fab,Edge.Cuts"
BACK_LAYERS = "B.Cu,B.Silkscreen,B.Fab,Edge.Cuts"


def viewbox_of(svg: str) -> list[float] | None:
    import re
    match = re.search(r'viewBox="([^"]+)"', svg)
    if not match:
        return None
    parts = match.group(1).split()
    if len(parts) != 4:
        return None
    try:
        return [float(part) for part in parts]
    except ValueError:
        return None


def viewboxes_match(a: list[float] | None, b: list[float] | None, tol: float = 0.02) -> bool:
    if not a or not b or len(a) != 4 or len(b) != 4:
        return False
    return all(abs(x - y) <= tol for x, y in zip(a, b))


def schematic_svg_command(schematic: Path, output: Path, *, highlight: bool = False,
                          pages: str | None = None, variant: str | None = None) -> list[str]:
    cmd = ["kicad-cli", "sch", "export", "svg"]
    if highlight:
        # -e --exclude-drawing-sheet, -n --no-background-color. Never a page-size mode.
        cmd.extend(["-e", "-n"])
    if pages:
        cmd.extend(["--pages", pages])
    if variant:
        cmd.extend(["--variant", variant])
    cmd.extend(["-o", str(output), str(schematic)])
    return cmd


def board_svg_command(board: Path, output: Path, side: str) -> list[str]:
    if side not in {"front", "back"}:
        raise invalid(f"unknown board side '{side}'")
    layers = FRONT_LAYERS if side == "front" else BACK_LAYERS
    # page-size-mode 1 keeps the sheet size so highlight layers stack on the base image.
    # Mode 2 (board area only) is never used.
    cmd = [
        "kicad-cli", "pcb", "export", "svg",
        "--mode-single",
        "--page-size-mode", "1",
        "--exclude-drawing-sheet",
        "--layers", layers,
    ]
    if side == "back":
        cmd.append("--mirror")
    cmd.extend(["-o", str(output), str(board)])
    if "2" == _page_size_mode(cmd):
        raise invalid("refusing --page-size-mode 2")
    return cmd


def _page_size_mode(cmd: list[str]) -> str | None:
    if "--page-size-mode" not in cmd:
        return None
    return cmd[cmd.index("--page-size-mode") + 1]


def _only_svg(directory: Path) -> Path | None:
    svgs = [path for path in directory.rglob("*.svg") if path.is_file()]
    if len(svgs) == 1:
        return svgs[0]
    return None


def _sheet_leaf(sheet_names: str) -> str:
    parts = [part for part in sheet_names.split("/") if part]
    return parts[-1] if parts else ""


def _match_pages(svg_files: list[Path], pages: list[Page], project_name: str) -> dict[str, Path]:
    """Map page numbers to files from one schematic SVG export.

    KiCad names the root ``{project}.svg`` and each child ``{project}-{sheet}.svg``,
    using the sheet name only (not the full path). Pages whose name is missing or
    shared are omitted. Do not fill those with ``--pages``: KiCad 10 plots the root
    sheet for every page number, so the first page shows behind every other page.
    """
    if not svg_files or not pages:
        return {}
    if len(svg_files) == 1 and len(pages) == 1:
        return {pages[0].number: svg_files[0]}
    remaining = list(svg_files)
    leaves = [_sheet_leaf(page.sheet_names) for page in pages if page.sheet_path != "/"]
    mapping: dict[str, Path] = {}
    for page in pages:
        if page.sheet_path == "/":
            candidates = [
                svg for svg in remaining
                if svg.stem == project_name or svg.stem == Path(page.file).stem
            ]
        else:
            leaf = _sheet_leaf(page.sheet_names)
            if not leaf or leaves.count(leaf) != 1:
                continue
            wanted = f"{project_name}-{leaf}"
            candidates = [svg for svg in remaining if svg.stem == wanted]
        if len(candidates) != 1:
            continue
        mapping[page.number] = candidates[0]
        remaining.remove(candidates[0])
    return mapping


def _export_isolated_sheet(project: Project, page: Page, page_dir: Path, *, env: dict[str, str],
                           timeout: float, variant: str | None, highlight: bool) -> bytes:
    """Plot one sheet as its own root so the parent sheet is not drawn behind it."""
    from app.errors import kicad_failed
    from app.visual_diff import write_sheet_as_root

    sheet_dir = page_dir / "sheet"
    schematic = write_sheet_as_root(sheet_dir, project, page)
    out = page_dir / "svg"
    out.mkdir(parents=True, exist_ok=True)
    # ``--pages`` plots the root sheet of the file it is given. This file's root
    # sheet is the page itself, so page 1 is not drawn behind it.
    run_kicad(
        schematic_svg_command(schematic, out, highlight=highlight, pages="1", variant=variant),
        cwd=sheet_dir,
        env=env,
        timeout=timeout,
    )
    svgs = [path for path in out.glob("*.svg") if path.is_file()]
    if len(svgs) != 1:
        raise kicad_failed(f"kicad-cli produced no SVG for schematic page {page.number}")
    return svgs[0].read_bytes()


def export_schematic_pages(project: Project, out_dir: Path, *, env: dict[str, str], timeout: float,
                           variant: str | None = None, highlight: bool = False) -> dict[str, bytes]:
    """Return page number → SVG bytes. One kicad-cli call plots every uniquely named sheet."""
    out_dir.mkdir(parents=True, exist_ok=True)
    root = next(page for page in project.pages if page.sheet_path == "/")
    schematic = project.project_dir / root.file
    rendered: dict[str, bytes] = {}
    if not highlight:
        run_kicad(
            schematic_svg_command(schematic, out_dir, highlight=False, variant=variant),
            cwd=project.project_dir,
            env=env,
            timeout=timeout,
        )
        svgs = [path for path in out_dir.glob("*.svg") if path.is_file()]
        for number, path in _match_pages(svgs, project.pages, project.name).items():
            rendered[number] = path.read_bytes()
    for page in project.pages:
        if page.number in rendered:
            continue
        rendered[page.number] = _export_isolated_sheet(
            project, page, out_dir / f"page-{page.number}",
            env=env, timeout=timeout, variant=variant, highlight=highlight,
        )
    return rendered


def export_board_side(project: Project, side: str, output: Path, *, env: dict[str, str], timeout: float) -> bytes:
    if project.board is None:
        raise invalid("project has no board")
    output.parent.mkdir(parents=True, exist_ok=True)
    board = project.project_dir / project.board.rel
    run_kicad(
        board_svg_command(board, output, side),
        cwd=project.project_dir,
        env=env,
        timeout=timeout,
    )
    if not output.is_file():
        # mode-single sometimes writes next to a directory argument
        found = _only_svg(output.parent)
        if found is None:
            from app.errors import kicad_failed
            raise kicad_failed(f"kicad-cli produced no SVG for the {side} of the board")
        return found.read_bytes()
    return output.read_bytes()


def render(extract_root: Path, spec: dict, *, env: dict[str, str], timeout: float, kicad: str) -> bytes:
    project_file = spec.get("projectFile")
    if not isinstance(project_file, str) or not project_file:
        raise invalid("spec.projectFile is required")
    variant = spec.get("variant") or None
    if variant is not None and not isinstance(variant, str):
        raise invalid("spec.variant must be a string")
    want_schematic = spec.get("schematic", True)
    board_sides = spec.get("board", ["front", "back"])
    if board_sides is None:
        board_sides = []
    if not isinstance(board_sides, list) or any(side not in {"front", "back"} for side in board_sides):
        raise invalid("spec.board must be a list of 'front' and 'back'")
    project = load_project(extract_root, project_file)
    files: dict[str, bytes] = {}
    manifest_pages = []
    if want_schematic:
        rendered = export_schematic_pages(
            project, extract_root / "render-sch", env=env, timeout=timeout, variant=variant,
        )
        for page in project.pages:
            svg = rendered.get(page.number, b"")
            name = f"sch/p{page.number}.svg"
            files[name] = svg
            text = svg.decode("utf-8", errors="replace")
            manifest_pages.append({
                "page": page.number,
                "sheetPath": page.sheet_path,
                "sheetNames": page.sheet_names,
                "file": page.file,
                "paper": page.paper,
                "svg": name,
                "viewBox": viewbox_of(text),
            })
    manifest_board: dict[str, dict] = {}
    if project.board is not None:
        for side in board_sides:
            svg = export_board_side(
                project, side, extract_root / "render-pcb" / f"{side}.svg", env=env, timeout=timeout,
            )
            name = f"pcb/{side}.svg"
            files[name] = svg
            manifest_board[side] = {
                "file": name,
                "viewBox": viewbox_of(svg.decode("utf-8", errors="replace")),
            }
    manifest = {
        "version": 1,
        "kicad": kicad,
        "pages": manifest_pages,
        "board": manifest_board,
    }
    files["manifest.json"] = json.dumps(manifest, indent=2).encode("utf-8")
    return _zip(files)


def _zip(files: dict[str, bytes]) -> bytes:
    buf = io.BytesIO()
    with zipfile.ZipFile(buf, "w", compression=zipfile.ZIP_DEFLATED) as archive:
        for name, data in files.items():
            archive.writestr(name.replace("\\", "/"), data)
    return buf.getvalue()


def root_schematic(project: Project) -> Path:
    page = next(item for item in project.pages if item.sheet_path == "/")
    return project.project_dir / page.file


def mirror_point(x: float, y: float, page_width: float) -> tuple[float, float]:
    """Back-side SVG is mirrored on X. File Y matches SVG Y (KiCad export)."""
    return (page_width - x, y)


def page_width(project: Project, paper: str, root) -> float:
    return paper_size(paper, root)[0]

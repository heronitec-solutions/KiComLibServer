"""Load a KiCad project: root schematic, sheet hierarchy, pages, board."""

from __future__ import annotations

from dataclasses import dataclass, field
from pathlib import Path, PurePosixPath

from app.cli import SUPPORTED_BOARD_VERSION, SUPPORTED_SCHEMATIC_VERSION
from app.errors import invalid, missing_files, unsupported_version
from app.sexpr import Node, parse, raw_slice

# Landscape millimetres, matching KiCad's page table (common/page_info.cpp).
PAPER_MM: dict[str, tuple[float, float]] = {
    "A5": (210.0, 148.0),
    "A4": (297.0, 210.0),
    "A3": (420.0, 297.0),
    "A2": (594.0, 420.0),
    "A1": (841.0, 594.0),
    "A0": (1189.0, 841.0),
    "A": (279.4, 215.9),
    "B": (431.8, 279.4),
    "C": (558.8, 431.8),
    "D": (863.6, 558.8),
    "E": (1117.6, 863.6),
    "USLetter": (279.4, 215.9),
    "Letter": (279.4, 215.9),
    "USLegal": (355.6, 215.9),
    "Legal": (355.6, 215.9),
    "USLedger": (431.8, 279.4),
}

SCH_DRAWABLE = {
    "symbol", "wire", "bus", "junction", "no_connect", "bus_entry",
    "label", "global_label", "hierarchical_label", "netclass_flag", "directive_label",
    "text", "text_box", "polyline", "rectangle", "circle", "arc", "bezier",
    "image", "sheet", "rule_area", "table",
}
PCB_DRAWABLE_EXACT = {
    "footprint", "segment", "arc", "via", "zone", "dimension", "target", "image", "table",
}
SKIP_IN_FILTER = {"group", "generated"}

_MISSING_HINT = (
    " If sheets live outside the project directory, set export_scope to repository."
)


@dataclass
class SymbolInstance:
    project: str
    path: str
    reference: str
    unit: int


@dataclass
class DrawnObject:
    kind: str
    uuid: str | None
    path: str | None  # footprint (path ...) when present
    node: Node
    raw: str
    reference: str | None = None
    value: str | None = None
    lib_id: str | None = None
    unit: int | None = None
    dnp: str | None = None
    in_bom: str | None = None
    on_board: str | None = None
    layer: str | None = None
    at: tuple[float, float] | None = None
    instances: list[SymbolInstance] = field(default_factory=list)

    @property
    def key(self) -> str:
        if self.kind == "footprint" and self.path:
            return self.path
        return self.uuid or ""


@dataclass
class SheetDoc:
    rel: str
    text: str
    root: Node
    version: int
    generator_version: str
    file_uuid: str
    paper: str
    title_block: dict[str, str]
    objects: list[DrawnObject]
    lib_symbols: dict[str, str]


@dataclass
class Page:
    number: str
    sheet_path: str
    sheet_names: str
    file: str
    paper: str
    full_path: str
    file_uuid: str


@dataclass
class BoardDoc:
    rel: str
    text: str
    root: Node
    version: int
    generator_version: str
    paper: str
    objects: list[DrawnObject]
    footprints: int
    unlinked: list[dict[str, str]]
    crop: list[float] | None


@dataclass
class Project:
    name: str
    project_dir: Path
    extract_root: Path
    schematic_version: int
    board_version: int | None
    generator_version: str
    title_block: dict[str, str]
    pages: list[Page]
    sheets: dict[str, SheetDoc]
    board: BoardDoc | None
    warnings: list[str] = field(default_factory=list)

    def symbol_by_uuid(self, uuid: str) -> DrawnObject | None:
        for doc in self.sheets.values():
            for obj in doc.objects:
                if obj.kind == "symbol" and obj.uuid == uuid:
                    return obj
        return None

    def paper_size(self, paper: str, root: Node | None = None) -> tuple[float, float]:
        return paper_size(paper, root)


def paper_size(paper: str, root: Node | None = None) -> tuple[float, float]:
    portrait = False
    width = height = None
    if root is not None:
        node = root.child("paper")
        if node is not None:
            for arg in node.items[1:]:
                if arg.kind == "string" and not paper:
                    paper = str(arg.value)
                elif arg.kind == "atom" and arg.value == "portrait":
                    portrait = True
                elif arg.kind == "list" and arg.name == "width" and len(arg.items) >= 2:
                    width = float(str(arg.items[1].value))
                elif arg.kind == "list" and arg.name == "height" and len(arg.items) >= 2:
                    height = float(str(arg.items[1].value))
            strings = [str(a.value) for a in node.items[1:] if a.kind == "string"]
            atoms = [str(a.value) for a in node.items[1:] if a.kind == "atom"]
            if strings:
                paper = strings[0]
            if "portrait" in atoms:
                portrait = True
            if paper == "User" and width is None:
                nums = []
                for atom in atoms:
                    try:
                        nums.append(float(atom))
                    except ValueError:
                        continue
                if len(nums) >= 2:
                    width, height = nums[0], nums[1]
    if paper == "User" and width is not None and height is not None:
        return (width, height)
    size = PAPER_MM.get(paper, PAPER_MM["A4"])
    if portrait:
        return (size[1], size[0])
    return size


def _within(parent: Path, child: Path) -> bool:
    try:
        child.resolve().relative_to(parent.resolve())
        return True
    except ValueError:
        return False


def _rel_posix(path: Path, root: Path) -> str:
    return path.resolve().relative_to(root.resolve()).as_posix()


def _check_version(kind: str, version: int) -> None:
    limit = SUPPORTED_SCHEMATIC_VERSION if kind == "schematic" else SUPPORTED_BOARD_VERSION
    if version > limit:
        raise unsupported_version(
            f"{kind} file version {version} is newer than this runner "
            f"(KiCad 10.0.6 supports {limit})"
        )


def _int_version(root: Node) -> int:
    raw = root.atom_arg("version")
    if raw is None:
        raise invalid("KiCad file has no version")
    try:
        return int(raw)
    except ValueError as exc:
        raise invalid(f"KiCad file version is not an integer: {raw}") from exc


def _title_block(root: Node) -> dict[str, str]:
    block = root.child("title_block")
    if block is None:
        return {"title": "", "rev": "", "date": "", "company": ""}
    def grab(name: str) -> str:
        node = block.child(name)
        if node is None or len(node.items) < 2:
            return ""
        arg = node.items[1]
        if arg.kind in ("string", "atom"):
            return str(arg.value)
        return ""
    return {
        "title": grab("title"),
        "rev": grab("rev"),
        "date": grab("date"),
        "company": grab("company"),
    }


def _paper_name(root: Node) -> str:
    node = root.child("paper")
    if node is None:
        return "A4"
    for arg in node.items[1:]:
        if arg.kind == "string":
            return str(arg.value)
    return "A4"


def _bool_atom(node: Node, name: str) -> str | None:
    return node.atom_arg(name)


def _instances(node: Node) -> list[SymbolInstance]:
    found: list[SymbolInstance] = []
    for block in node.children("instances"):
        for project in block.items[1:]:
            if project.kind != "list" or project.name != "project" or len(project.items) < 2:
                continue
            pname = str(project.items[1].value) if project.items[1].kind in ("string", "atom") else ""
            for path_node in project.items[2:]:
                if path_node.name != "path" or len(path_node.items) < 2:
                    continue
                path = str(path_node.items[1].value) if path_node.items[1].kind in ("string", "atom") else ""
                reference = path_node.atom_arg("reference") or ""
                unit_raw = path_node.atom_arg("unit")
                page = path_node.atom_arg("page")
                try:
                    unit = int(unit_raw) if unit_raw is not None else 1
                except ValueError:
                    unit = 1
                inst = SymbolInstance(pname, path, reference, unit)
                # page numbers live on sheet instances; stash on reference field only for symbols.
                if page is not None and not reference:
                    inst.reference = ""
                    inst.unit = int(page) if page.isdigit() else unit
                    setattr(inst, "page", page)
                else:
                    setattr(inst, "page", page)
                found.append(inst)
    return found


def _lib_symbol_map(root: Node, text: str) -> dict[str, str]:
    block = root.child("lib_symbols")
    if block is None:
        return {}
    out: dict[str, str] = {}
    for sym in block.items[1:]:
        if sym.name != "symbol" or len(sym.items) < 2 or sym.items[1].kind != "string":
            continue
        out[str(sym.items[1].value)] = raw_slice(text, sym)
    return out


def _drawn(kind_root: str, node: Node, text: str) -> DrawnObject | None:
    kind = node.name or ""
    drawable = kind in SCH_DRAWABLE if kind_root == "kicad_sch" else (
        kind in PCB_DRAWABLE_EXACT or kind.startswith("gr_")
    )
    if not drawable:
        return None
    props = node.properties()
    uuid = node.atom_arg("uuid")
    path = node.atom_arg("path")
    lib_id = None
    if kind == "symbol" and len(node.items) >= 2:
        lib = node.child("lib_id")
        if lib is not None and len(lib.items) >= 2 and lib.items[1].kind == "string":
            lib_id = str(lib.items[1].value)
    elif kind == "footprint" and len(node.items) >= 2 and node.items[1].kind == "string":
        lib_id = str(node.items[1].value)
    unit_raw = node.atom_arg("unit")
    try:
        unit = int(unit_raw) if unit_raw is not None else None
    except ValueError:
        unit = None
    layer = node.atom_arg("layer")
    return DrawnObject(
        kind=kind,
        uuid=uuid,
        path=path,
        node=node,
        raw=raw_slice(text, node),
        reference=props.get("Reference"),
        value=props.get("Value"),
        lib_id=lib_id,
        unit=unit,
        dnp=_bool_atom(node, "dnp"),
        in_bom=_bool_atom(node, "in_bom"),
        on_board=_bool_atom(node, "on_board"),
        layer=layer,
        at=node.at_xy(),
        instances=_instances(node) if kind in {"symbol", "sheet"} else [],
    )


def _load_sch(path: Path, rel: str) -> SheetDoc:
    try:
        text = path.read_text(encoding="utf-8")
    except OSError as exc:
        raise missing_files(f"missing schematic sheet '{rel}'.{_MISSING_HINT}") from exc
    try:
        root = parse(text)
    except ValueError as exc:
        raise invalid(f"cannot parse {rel}: {exc}") from exc
    if root.name != "kicad_sch":
        raise invalid(f"{rel} is not a schematic")
    version = _int_version(root)
    _check_version("schematic", version)
    objects = []
    for child in root.items[1:]:
        if child.kind != "list":
            continue
        drawn = _drawn("kicad_sch", child, text)
        if drawn is not None:
            objects.append(drawn)
    file_uuid = root.atom_arg("uuid") or ""
    gen = root.atom_arg("generator_version") or ""
    return SheetDoc(
        rel=rel,
        text=text,
        root=root,
        version=version,
        generator_version=gen,
        file_uuid=file_uuid,
        paper=_paper_name(root),
        title_block=_title_block(root),
        objects=objects,
        lib_symbols=_lib_symbol_map(root, text),
    )


def _coords_of(node: Node, bucket: list[tuple[float, float]]) -> None:
    if node.name in {"xy", "start", "end", "center", "mid", "at"}:
        nums: list[float] = []
        for arg in node.items[1:]:
            if arg.kind != "atom":
                break
            try:
                nums.append(float(str(arg.value)))
            except ValueError:
                break
        if len(nums) >= 2 and node.name != "at":
            bucket.append((nums[0], nums[1]))
        elif node.name == "at" and len(nums) >= 2:
            bucket.append((nums[0], nums[1]))
    for child in node.items[1:]:
        if child.kind == "list" and child.name not in {"uuid", "stroke", "effects", "font"}:
            _coords_of(child, bucket)


def _edge_cuts_crop(objects: list[DrawnObject]) -> list[float] | None:
    points: list[tuple[float, float]] = []
    for obj in objects:
        layer = obj.layer or ""
        if layer != "Edge.Cuts" and not obj.kind.startswith("gr_"):
            # footprints are not the board outline
            if obj.kind != "footprint":
                pass
        on_edge = layer == "Edge.Cuts" or (
            obj.kind.startswith("gr_") and (obj.layer == "Edge.Cuts" or _layer_deep(obj.node) == "Edge.Cuts")
        )
        if not on_edge:
            continue
        _coords_of(obj.node, points)
    if not points:
        return None
    xs = [p[0] for p in points]
    ys = [p[1] for p in points]
    return [min(xs), min(ys), max(xs), max(ys)]


def _layer_deep(node: Node) -> str | None:
    return node.atom_arg("layer")


def _load_board(path: Path, rel: str) -> BoardDoc:
    try:
        text = path.read_text(encoding="utf-8")
    except OSError as exc:
        raise missing_files(f"missing board '{rel}'.") from exc
    try:
        root = parse(text)
    except ValueError as exc:
        raise invalid(f"cannot parse {rel}: {exc}") from exc
    if root.name != "kicad_pcb":
        raise invalid(f"{rel} is not a board")
    version = _int_version(root)
    _check_version("board", version)
    objects: list[DrawnObject] = []
    for child in root.items[1:]:
        if child.kind != "list":
            continue
        drawn = _drawn("kicad_pcb", child, text)
        if drawn is not None:
            objects.append(drawn)
    footprints = [obj for obj in objects if obj.kind == "footprint"]
    unlinked = []
    for fp in footprints:
        if fp.path:
            continue
        unlinked.append({
            "uuid": fp.uuid or "",
            "reference": fp.reference or "",
            "value": fp.value or "",
        })
    return BoardDoc(
        rel=rel,
        text=text,
        root=root,
        version=version,
        generator_version=root.atom_arg("generator_version") or "",
        paper=_paper_name(root),
        objects=objects,
        footprints=len(footprints),
        unlinked=unlinked,
        crop=_edge_cuts_crop(objects),
    )


def _page_of_sheet_instance(inst: SymbolInstance) -> str | None:
    page = getattr(inst, "page", None)
    return str(page) if page else None


def _resolve_sheet(base: Path, sheetfile: str, jail: Path) -> Path:
    if not sheetfile or sheetfile.startswith("/") or (len(sheetfile) >= 2 and sheetfile[1] == ":"):
        raise missing_files(f"missing schematic sheet '{sheetfile}'.{_MISSING_HINT}")
    parts = PurePosixPath(sheetfile.replace("\\", "/")).parts
    if any(part == ".." for part in parts):
        # `..` is allowed only when the result stays inside the extract root
        # (sheets outside the project directory, export_scope=repository).
        pass
    candidate = (base / sheetfile).resolve()
    if not _within(jail, candidate) or not candidate.is_file():
        raise missing_files(f"missing schematic sheet '{sheetfile}'.{_MISSING_HINT}")
    return candidate


def load_project(extract_root: Path, project_file: str) -> Project:
    """Load ``project_file`` (relative to ``extract_root``) and its sheet hierarchy."""
    rel = project_file.replace("\\", "/")
    if not rel or rel.endswith("/") or not rel.endswith(".kicad_pro"):
        raise invalid("projectFile must be a .kicad_pro path")
    parts = PurePosixPath(rel).parts
    if any(part == ".." for part in parts) or rel.startswith("/"):
        raise invalid("invalid projectFile")
    pro_path = (extract_root / rel).resolve()
    if not _within(extract_root, pro_path):
        raise invalid("invalid projectFile")
    if not pro_path.is_file():
        raise missing_files(f"missing project file '{rel}'.{_MISSING_HINT}")

    name = pro_path.stem
    project_dir = pro_path.parent
    sch_path = pro_path.with_suffix(".kicad_sch")
    if not sch_path.is_file():
        raise missing_files(f"missing schematic sheet '{name}.kicad_sch'.{_MISSING_HINT}")

    sheets: dict[str, SheetDoc] = {}
    pages: list[Page] = []

    def sheet_rel(path: Path) -> str:
        return _rel_posix(path, project_dir)

    root_doc = _load_sch(sch_path, sheet_rel(sch_path))
    sheets[root_doc.rel] = root_doc
    root_page_number = "1"
    for inst_node in root_doc.root.children("sheet_instances"):
        for path_node in inst_node.items[1:]:
            if path_node.name == "path" and path_node.atom_arg("page"):
                stamp = path_node.items[1].value if len(path_node.items) > 1 else ""
                if str(stamp) in {"/", ""}:
                    root_page_number = path_node.atom_arg("page") or "1"
    pages.append(Page(
        number=str(root_page_number),
        sheet_path="/",
        sheet_names="/",
        file=root_doc.rel,
        paper=root_doc.paper,
        full_path="/" + root_doc.file_uuid,
        file_uuid=root_doc.file_uuid,
    ))

    def walk(doc: SheetDoc, parent_page: Page) -> None:
        for obj in doc.objects:
            if obj.kind != "sheet":
                continue
            sheetfile = obj.node.properties().get("Sheetfile", "")
            sheetname = obj.node.properties().get("Sheetname", "")
            if not obj.uuid:
                continue
            child_path = _resolve_sheet(project_dir / Path(doc.rel).parent, sheetfile, extract_root)
            child_rel = sheet_rel(child_path)
            if child_rel not in sheets:
                sheets[child_rel] = _load_sch(child_path, child_rel)
            child = sheets[child_rel]
            instances = obj.instances or _instances(obj.node)
            matched = []
            for inst in instances:
                if inst.path.rstrip("/") == parent_page.full_path.rstrip("/"):
                    matched.append(inst)
            if not matched:
                matched = instances
            for inst in matched:
                page_number = _page_of_sheet_instance(inst) or str(len(pages) + 1)
                full = parent_page.full_path.rstrip("/") + "/" + obj.uuid
                if parent_page.sheet_path == "/":
                    sheet_path = "/" + obj.uuid + "/"
                else:
                    sheet_path = parent_page.sheet_path.rstrip("/") + "/" + obj.uuid + "/"
                if parent_page.sheet_names == "/":
                    sheet_names = "/" + sheetname + "/"
                else:
                    sheet_names = parent_page.sheet_names.rstrip("/") + "/" + sheetname + "/"
                page = Page(
                    number=str(page_number),
                    sheet_path=sheet_path,
                    sheet_names=sheet_names,
                    file=child.rel,
                    paper=child.paper,
                    full_path=full,
                    file_uuid=child.file_uuid,
                )
                pages.append(page)
                walk(child, page)

    walk(root_doc, pages[0])
    pages.sort(key=lambda page: (int(page.number) if page.number.isdigit() else 10**9, page.number))

    board: BoardDoc | None = None
    board_version: int | None = None
    pcb_path = pro_path.with_suffix(".kicad_pcb")
    if pcb_path.is_file():
        board = _load_board(pcb_path, sheet_rel(pcb_path))
        board_version = board.version

    max_sch = max(doc.version for doc in sheets.values())
    generator = root_doc.generator_version or (board.generator_version if board else "")
    return Project(
        name=name,
        project_dir=project_dir,
        extract_root=extract_root.resolve(),
        schematic_version=max_sch,
        board_version=board_version,
        generator_version=generator,
        title_block=root_doc.title_block,
        pages=pages,
        sheets=sheets,
        board=board,
    )


def instance_on_page(obj: DrawnObject, page: Page) -> SymbolInstance | None:
    for inst in obj.instances:
        if inst.path.rstrip("/") == page.full_path.rstrip("/"):
            return inst
    return None


def reference_on_page(obj: DrawnObject, page: Page) -> str:
    inst = instance_on_page(obj, page)
    if inst and inst.reference:
        return inst.reference
    return obj.reference or ""


def unit_on_page(obj: DrawnObject, page: Page) -> int:
    inst = instance_on_page(obj, page)
    if inst is not None:
        return inst.unit or obj.unit or 1
    return obj.unit or 1


def category_of(kind: str, reference: str | None, domain: str = "schematic") -> str:
    """``domain`` is ``schematic`` or ``board``. A board ``arc`` is a track; a schematic ``arc`` is graphics."""
    if domain == "board":
        if kind == "footprint":
            return "parts"
        if kind in {"segment", "arc", "via"}:
            return "routing"
        if kind == "zone":
            return "zones"
        return "graphics"
    if kind == "symbol":
        if reference and reference.startswith("#"):
            return "wiring"
        return "parts"
    if kind in {"wire", "bus", "junction", "no_connect", "bus_entry", "label",
                "global_label", "hierarchical_label", "netclass_flag", "directive_label"}:
        return "wiring"
    return "graphics"


def symbol_manifest_key(page: Page, uuid: str) -> str:
    if page.sheet_path == "/":
        return "/" + uuid
    return page.sheet_path.rstrip("/") + "/" + uuid

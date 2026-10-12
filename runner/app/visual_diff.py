"""UUID diff, filtered copies, recoloring, and the visual-diff manifest."""

from __future__ import annotations

import hashlib
import io
import json
import os
import re
import tempfile
import uuid
import zipfile
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

from app.errors import invalid
from app.project import (
    SKIP_IN_FILTER,
    BoardDoc,
    DrawnObject,
    Page,
    Project,
    SheetDoc,
    category_of,
    instance_on_page,
    load_project,
    paper_size,
    symbol_manifest_key,
)
from app.render import (
    board_svg_command,
    export_schematic_pages,
    mirror_point,
    schematic_svg_command,
    viewbox_of,
    viewboxes_match,
)
from app.sexpr import Node, escape, parse

COLORS = {
    "added": "#16a34a",
    "removed": "#dc2626",
    "changed": "#ea580c",
    "moved": "#2563eb",
}
COLORED = ("added", "removed", "changed", "moved")
ZONE_SKIP = frozenset({"filled_polygon", "fill_segments"})
_NUM = re.compile(r"^[+-]?(?:\d+\.?\d*|\.\d+)(?:[eE][+-]?\d+)?$")

_FILL_STYLE = re.compile(r"fill:\s*#[0-9A-Fa-f]{6}\b")
_STROKE_STYLE = re.compile(r"stroke:\s*#[0-9A-Fa-f]{6}\b")
_FILL_ATTR = re.compile(r'\bfill="#[0-9A-Fa-f]{6}"')
_STROKE_ATTR = re.compile(r'\bstroke="#[0-9A-Fa-f]{6}"')
_SW_STYLE = re.compile(r"stroke-width:\s*([0-9]*\.?[0-9]+)")
_SW_ATTR = re.compile(r'\bstroke-width="([0-9]*\.?[0-9]+)"')


def _canon_atom(value: str) -> str:
    if _NUM.match(value):
        try:
            return f"{float(value):.6g}"
        except ValueError:
            return value
    return value


def canonical(node: Node, *, blank_at: bool = False, skip: frozenset[str] = frozenset()) -> str | None:
    if node.kind == "atom":
        return _canon_atom(str(node.value))
    if node.kind == "string":
        return escape(str(node.value))
    if node.name in skip:
        return None
    parts: list[str] = []
    for item in node.items:
        if blank_at and item.kind == "list" and item.name == "at":
            parts.append("(at 0)")
            continue
        piece = canonical(item, blank_at=blank_at, skip=skip)
        if piece is None:
            continue
        parts.append(piece)
    return "(" + " ".join(parts) + ")"


def _prop_values(obj: DrawnObject, *, drop_reference: bool) -> tuple:
    props = obj.node.properties()
    if drop_reference:
        props.pop("Reference", None)
    return tuple(sorted(props.items()))


def _prop_positions(obj: DrawnObject) -> tuple:
    found = []
    for prop in obj.node.children("property"):
        if len(prop.items) < 2 or prop.items[1].kind != "string":
            continue
        found.append((str(prop.items[1].value), prop.at_xy()))
    return tuple(found)


def _side(layer: str | None) -> str:
    if not layer:
        return ""
    if layer.startswith("F.") or layer.startswith("F_"):
        return "F"
    if layer.startswith("B.") or layer.startswith("B_"):
        return "B"
    return layer


def _subtree_canon(obj: DrawnObject, names: set[str], prefix: str | None = None) -> tuple:
    parts = []
    for child in obj.node.items[1:]:
        if child.name in names or (prefix and child.name and str(child.name).startswith(prefix)):
            parts.append(canonical(child, blank_at=True))
    return tuple(parts)


def symbol_content(obj: DrawnObject) -> tuple:
    return (
        obj.lib_id,
        obj.unit,
        (obj.dnp or "no").lower(),
        (obj.in_bom or "yes").lower(),
        (obj.on_board or "yes").lower(),
        _prop_values(obj, drop_reference=True),
    )


def symbol_detail(base: DrawnObject, target: DrawnObject) -> dict:
    detail: dict = {}
    if base.lib_id != target.lib_id:
        detail["lib_id"] = [base.lib_id, target.lib_id]
    if base.unit != target.unit:
        detail["unit"] = [base.unit, target.unit]
    for label, old, new in (
        ("dnp", (base.dnp or "no").lower(), (target.dnp or "no").lower()),
        ("in_bom", (base.in_bom or "yes").lower(), (target.in_bom or "yes").lower()),
        ("on_board", (base.on_board or "yes").lower(), (target.on_board or "yes").lower()),
    ):
        if old != new:
            detail[label] = [old, new]
    old_props = dict(base.node.properties())
    new_props = dict(target.node.properties())
    old_props.pop("Reference", None)
    new_props.pop("Reference", None)
    for name in sorted(set(old_props) | set(new_props)):
        if old_props.get(name) != new_props.get(name):
            detail[name] = [old_props.get(name), new_props.get(name)]
    return detail


def footprint_content(obj: DrawnObject) -> tuple:
    return (
        obj.lib_id,
        _side(obj.layer),
        _prop_values(obj, drop_reference=True),
        _subtree_canon(obj, {"pad"}),
        _subtree_canon(obj, set(), prefix="fp_"),
    )


def footprint_detail(base: DrawnObject, target: DrawnObject) -> dict:
    detail: dict = {}
    if base.lib_id != target.lib_id:
        detail["lib_id"] = [base.lib_id, target.lib_id]
    if _side(base.layer) != _side(target.layer):
        detail["side"] = [base.layer, target.layer]
    old_props = dict(base.node.properties())
    new_props = dict(target.node.properties())
    old_props.pop("Reference", None)
    new_props.pop("Reference", None)
    for name in sorted(set(old_props) | set(new_props)):
        if old_props.get(name) != new_props.get(name):
            detail[name] = [old_props.get(name), new_props.get(name)]
    if _subtree_canon(base, {"pad"}) != _subtree_canon(target, {"pad"}):
        detail["pads"] = "changed"
    if _subtree_canon(base, set(), prefix="fp_") != _subtree_canon(target, set(), prefix="fp_"):
        detail["graphics"] = "changed"
    return detail


def _placement(obj: DrawnObject) -> tuple:
    return (obj.at, _prop_positions(obj))


def compare_pair(base: DrawnObject, target: DrawnObject, *, graphic: bool) -> tuple[str | None, dict]:
    if base.kind == "symbol":
        detail = symbol_detail(base, target)
        if graphic:
            detail["graphic"] = "symbol graphic updated"
        if detail:
            return "changed", detail
        if _placement(base) != _placement(target):
            return "moved", {}
        return None, {}
    if base.kind == "footprint":
        detail = footprint_detail(base, target)
        if detail:
            return "changed", detail
        if _placement(base) != _placement(target):
            return "moved", {}
        return None, {}
    skip = ZONE_SKIP if base.kind == "zone" else frozenset()
    if canonical(base.node, blank_at=True, skip=skip) != canonical(target.node, blank_at=True, skip=skip):
        return "changed", {}
    if canonical(base.node, blank_at=False, skip=skip) != canonical(target.node, blank_at=False, skip=skip):
        return "moved", {}
    return None, {}


def changed_lib_ids(base: SheetDoc, target: SheetDoc) -> set[str]:
    changed: set[str] = set()
    for lib_id, raw in base.lib_symbols.items():
        other = target.lib_symbols.get(lib_id)
        if other is None:
            continue
        try:
            if canonical(parse(raw)) != canonical(parse(other)):
                changed.add(lib_id)
        except ValueError:
            if raw != other:
                changed.add(lib_id)
    return changed


def ref_for(obj: DrawnObject, page: Page) -> str:
    inst = instance_on_page(obj, page)
    if inst and inst.reference:
        return inst.reference
    return obj.reference or ""


def unit_for(obj: DrawnObject, page: Page) -> int:
    inst = instance_on_page(obj, page)
    if inst is not None and inst.reference:
        return inst.unit or obj.unit or 1
    return obj.unit or 1


def manifest_key(obj: DrawnObject, page: Page | None, domain: str) -> str:
    if domain == "schematic" and obj.kind == "symbol" and obj.uuid and page is not None:
        return symbol_manifest_key(page, obj.uuid)
    if obj.kind == "footprint" and obj.path:
        return obj.path
    return obj.uuid or obj.key


def _point(obj: DrawnObject, *, mirror: bool, width: float) -> list[float] | None:
    if obj.at is None:
        return None
    x, y = obj.at
    if mirror:
        x, y = mirror_point(x, y, width)
    return [x, y]


def _all_unit_points(doc: SheetDoc, page: Page, reference: str, *, mirror: bool, width: float) -> list[list[float]]:
    points: list[list[float]] = []
    if not reference:
        return points
    for obj in doc.objects:
        if obj.kind != "symbol" or ref_for(obj, page) != reference:
            continue
        point = _point(obj, mirror=mirror, width=width)
        if point is not None and point not in points:
            points.append(point)
    return points


def _drawable(doc: SheetDoc | BoardDoc) -> list[DrawnObject]:
    return [obj for obj in doc.objects if obj.key]


def _index(objects: list[DrawnObject]) -> dict[str, DrawnObject]:
    return {obj.key: obj for obj in objects if obj.key}


def rewrite_symbol_instances(raw: str, *, project: str, sheet_file_uuid: str,
                             reference: str, unit: int) -> str:
    """Replace the symbol ``instances`` block with the single rendered instance.

    The path is ``/<sheet file uuid>`` so KiCad, opening the copy as the root
    sheet, shows ``reference`` and ``unit``.
    """
    node = parse(raw)
    block = (
        f"(instances (project {escape(project)} "
        f"(path {escape('/' + sheet_file_uuid)} "
        f"(reference {escape(reference)}) (unit {int(unit)}))))"
    )
    for child in node.items[1:]:
        if child.name == "instances":
            return raw[: child.start] + block + raw[child.end :]
    body = raw.rstrip()
    if body.endswith(")"):
        return body[:-1] + " " + block + ")"
    return raw + " " + block


def filtered_document(text: str, selected: set[str], *, domain: str,
                      as_root_sheet: bool = False, rewrite=None) -> str:
    """Header objects are copied unchanged. ``group`` and ``generated`` are omitted."""
    root = parse(text)
    head = str(root.items[0].value) if root.items else "kicad_sch"
    chunks = ["(" + head]
    for child in root.items[1:]:
        if child.kind != "list":
            chunks.append(text[child.start:child.end])
            continue
        name = child.name or ""
        if name in SKIP_IN_FILTER:
            continue
        drawable = (name in {"symbol", "wire", "bus", "junction", "no_connect", "bus_entry",
                             "label", "global_label", "hierarchical_label", "netclass_flag",
                             "directive_label", "text", "text_box", "polyline", "rectangle",
                             "circle", "arc", "bezier", "image", "sheet", "rule_area", "table"}
                    if domain == "schematic"
                    else name in {"footprint", "segment", "arc", "via", "zone", "dimension",
                                  "target", "image", "table"} or name.startswith("gr_"))
        raw = text[child.start:child.end]
        if drawable:
            key = _key_from_node(child)
            if key not in selected:
                continue
            if rewrite is not None and name == "symbol":
                raw = rewrite(child, raw)
            chunks.append(raw)
        else:
            if as_root_sheet and name == "sheet_instances":
                chunks.append('(sheet_instances (path "/" (page "1")))')
            else:
                chunks.append(raw)
    chunks.append(")")
    return "\n".join(chunks)


def _key_from_node(node: Node) -> str:
    if node.name == "footprint":
        path = node.atom_arg("path")
        if path:
            return path
    return node.atom_arg("uuid") or ""


def _widen(value: str) -> str:
    width = max(0.2, float(value) * 1.8)
    return f"{width:.4f}".rstrip("0").rstrip(".")


def color_svg(svg: str, color: str) -> str:
    """Recolor fills and strokes, widen strokes, and fade filled areas to 25%."""
    svg = _FILL_STYLE.sub("fill:" + color, svg)
    svg = _STROKE_STYLE.sub("stroke:" + color, svg)
    svg = _FILL_ATTR.sub(f'fill="{color}"', svg)
    svg = _STROKE_ATTR.sub(f'stroke="{color}"', svg)
    svg = _SW_STYLE.sub(lambda match: "stroke-width:" + _widen(match.group(1)), svg)
    svg = _SW_ATTR.sub(lambda match: f'stroke-width="{_widen(match.group(1))}"', svg)

    def _style_opacity(match: re.Match[str]) -> str:
        style = match.group(1)
        if re.search(r"fill:\s*#[0-9A-Fa-f]{6}", style) and "fill-opacity" not in style:
            if style and not style.endswith(";"):
                style += ";"
            style += "fill-opacity:0.25"
        return f'style="{style}"'

    svg = re.sub(r'style="([^"]*)"', _style_opacity, svg)

    def _attr_opacity(match: re.Match[str]) -> str:
        tag = match.group(0)
        if "fill-opacity" in tag or not re.search(r'\bfill="#[0-9A-Fa-f]{6}"', tag):
            return tag
        return tag[:-1] + ' fill-opacity="0.25">'

    svg = re.sub(r"<[^>]*\bfill=\"#[0-9A-Fa-f]{6}\"[^>]*>", _attr_opacity, svg)
    return svg


def _page_index(project: Project) -> dict[tuple[str, str], Page]:
    return {(page.file, page.sheet_path): page for page in project.pages}


def _change_record(status: str, obj: DrawnObject, page: Page, doc: SheetDoc | BoardDoc,
                   domain: str, detail: dict | None, *, mirror: bool, width: float) -> dict:
    reference = ref_for(obj, page) if domain == "schematic" else (obj.reference or "")
    if domain == "schematic" and obj.kind == "symbol" and reference:
        points = _all_unit_points(doc, page, reference, mirror=mirror, width=width)  # type: ignore[arg-type]
    else:
        point = _point(obj, mirror=mirror, width=width)
        points = [point] if point else []
    record = {
        "status": status,
        "kind": obj.kind,
        "category": category_of(obj.kind, reference, domain),
        "key": manifest_key(obj, page if domain == "schematic" else None, domain),
        "objKey": obj.key,
        "reference": reference,
        "value": obj.value,
        "libId": obj.lib_id,
        "unit": unit_for(obj, page) if domain == "schematic" else obj.unit,
        "at": points,
        "detail": detail or None,
        "source": "base" if status == "removed" else "target",
    }
    return record


def _file_pairs(base: Project, target: Project) -> list[tuple[str, SheetDoc | None, SheetDoc | None]]:
    names = sorted(set(base.sheets) | set(target.sheets))
    return [(name, base.sheets.get(name), target.sheets.get(name)) for name in names]


def _classify_sheet_pages(base: Project, target: Project) -> list[dict]:
    pages_out: list[dict] = []
    base_pages = _page_index(base)
    target_pages = _page_index(target)
    keys = sorted(set(base_pages) | set(target_pages), key=lambda item: (
        int(base_pages[item].number) if item in base_pages and base_pages[item].number.isdigit()
        else int(target_pages[item].number) if item in target_pages and target_pages[item].number.isdigit()
        else 10**9,
        item[1],
        item[0],
    ))
    lib_cache: dict[str, set[str]] = {}
    for key in keys:
        bpage = base_pages.get(key)
        tpage = target_pages.get(key)
        page = tpage or bpage
        assert page is not None
        bdoc = base.sheets.get(page.file) if bpage else None
        tdoc = target.sheets.get(page.file) if tpage else None
        # A repeated sheet uses the same file for several pages; look up by file name.
        if bdoc is None and page.file in base.sheets and bpage is not None:
            bdoc = base.sheets[page.file]
        if tdoc is None and page.file in target.sheets and tpage is not None:
            tdoc = target.sheets[page.file]
        domain = "schematic"
        raw: list[dict] = []
        if bpage is None and tdoc is not None and tpage is not None:
            for obj in _drawable(tdoc):
                raw.append(_change_record("added", obj, tpage, tdoc, domain, None, mirror=False, width=0))
        elif tpage is None and bdoc is not None and bpage is not None:
            for obj in _drawable(bdoc):
                raw.append(_change_record("removed", obj, bpage, bdoc, domain, None, mirror=False, width=0))
        elif bdoc is not None and tdoc is not None and bpage is not None and tpage is not None:
            libs = lib_cache.get(page.file)
            if libs is None:
                libs = changed_lib_ids(bdoc, tdoc)
                lib_cache[page.file] = libs
            bmap = _index(_drawable(bdoc))
            tmap = _index(_drawable(tdoc))
            for obj_key in sorted(set(bmap) | set(tmap)):
                left = bmap.get(obj_key)
                right = tmap.get(obj_key)
                if left is None and right is not None:
                    raw.append(_change_record("added", right, tpage, tdoc, domain, None, mirror=False, width=0))
                    continue
                if right is None and left is not None:
                    raw.append(_change_record("removed", left, bpage, bdoc, domain, None, mirror=False, width=0))
                    continue
                assert left is not None and right is not None
                graphic = bool(right.lib_id and right.lib_id in libs and right.kind == "symbol")
                status, detail = compare_pair(left, right, graphic=graphic)
                ref_b = ref_for(left, bpage)
                ref_t = ref_for(right, tpage)
                if ref_b and ref_t and ref_b != ref_t:
                    renamed = _change_record("renamed", right, tpage, tdoc, domain,
                                             {"reference": [ref_b, ref_t]}, mirror=False, width=0)
                    renamed["reference"] = ref_t
                    raw.append(renamed)
                if status:
                    src_doc = tdoc if status != "removed" else bdoc
                    src_page = tpage if status != "removed" else bpage
                    src_obj = right if status != "removed" else left
                    raw.append(_change_record(status, src_obj, src_page, src_doc, domain, detail,
                                              mirror=False, width=0))
        pages_out.append(_finish_page(
            page=tpage or bpage,
            kind="schematic",
            changes=_manifest_changes(raw),
            render_items=[item for item in raw if item["status"] in COLORED],
            crop=None,
            side=None,
        ))
    return pages_out


def _classify_board(base: Project, target: Project) -> list[dict]:
    if base.board is None and target.board is None:
        return []
    pages = []
    for side in ("front", "back"):
        mirror = side == "back"
        board = target.board or base.board
        assert board is not None
        width = paper_size(board.paper, board.root)[0]
        raw: list[dict] = []
        dummy = Page("0", "/", "/", board.rel, board.paper, "/", "")
        if base.board is None and target.board is not None:
            for obj in _drawable(target.board):
                raw.append(_change_record("added", obj, dummy, target.board, "board", None,
                                          mirror=mirror, width=width))
        elif target.board is None and base.board is not None:
            for obj in _drawable(base.board):
                raw.append(_change_record("removed", obj, dummy, base.board, "board", None,
                                          mirror=mirror, width=width))
        elif base.board is not None and target.board is not None:
            bmap = _index(_drawable(base.board))
            tmap = _index(_drawable(target.board))
            for obj_key in sorted(set(bmap) | set(tmap)):
                left = bmap.get(obj_key)
                right = tmap.get(obj_key)
                if left is None and right is not None:
                    raw.append(_change_record("added", right, dummy, target.board, "board", None,
                                              mirror=mirror, width=width))
                    continue
                if right is None and left is not None:
                    raw.append(_change_record("removed", left, dummy, base.board, "board", None,
                                              mirror=mirror, width=width))
                    continue
                assert left is not None and right is not None
                status, detail = compare_pair(left, right, graphic=False)
                ref_b = left.reference or ""
                ref_t = right.reference or ""
                if ref_b and ref_t and ref_b != ref_t:
                    renamed = _change_record("renamed", right, dummy, target.board, "board",
                                             {"reference": [ref_b, ref_t]}, mirror=mirror, width=width)
                    raw.append(renamed)
                if status:
                    src = target.board if status != "removed" else base.board
                    src_obj = right if status != "removed" else left
                    raw.append(_change_record(status, src_obj, dummy, src, "board", detail,
                                              mirror=mirror, width=width))
        crop = (target.board or base.board).crop if (target.board or base.board) else None
        if mirror and crop:
            crop = _mirror_crop(crop, width)
        pages.append(_finish_page(
            page=None,
            kind="board",
            changes=_manifest_changes(raw),
            render_items=[item for item in raw if item["status"] in COLORED],
            crop=crop,
            side=side,
        ))
    return pages


def _mirror_crop(crop: list[float], width: float) -> list[float]:
    x1, y1, x2, y2 = crop
    nx1, nx2 = width - x2, width - x1
    return [min(nx1, nx2), min(y1, y2), max(nx1, nx2), max(y1, y2)]


def _manifest_changes(raw: list[dict]) -> list[dict]:
    grouped = _group_symbol_units(raw)
    return _pair_replaced(grouped)


def _group_symbol_units(raw: list[dict]) -> list[dict]:
    passthrough = []
    buckets: dict[tuple, list[dict]] = {}
    for item in raw:
        if item["kind"] == "symbol" and item.get("reference") and item["status"] != "renamed":
            buckets.setdefault((item["status"], item["reference"]), []).append(item)
        else:
            passthrough.append(item)
    for items in buckets.values():
        primary = min(items, key=lambda entry: (entry.get("unit") or 1, entry["key"]))
        merged = dict(primary)
        points: list[list[float]] = []
        detail: dict = {}
        for entry in items:
            for point in entry.get("at") or []:
                if point not in points:
                    points.append(point)
            if isinstance(entry.get("detail"), dict):
                detail.update(entry["detail"])
        merged["at"] = points
        merged["detail"] = detail or None
        passthrough.append(merged)
    return passthrough


def _pair_replaced(changes: list[dict]) -> list[dict]:
    added = []
    removed = []
    rest = []
    for item in changes:
        if item["status"] == "added" and item.get("reference") and not str(item["reference"]).startswith("#"):
            added.append(item)
        elif item["status"] == "removed" and item.get("reference") and not str(item["reference"]).startswith("#"):
            removed.append(item)
        else:
            rest.append(item)
    used_added: set[int] = set()
    used_removed: set[int] = set()
    replaced = []
    for i, rem in enumerate(removed):
        for j, add in enumerate(added):
            if i in used_removed or j in used_added:
                continue
            if rem["reference"] == add["reference"] and rem["kind"] == add["kind"] and rem["category"] == add["category"]:
                used_removed.add(i)
                used_added.add(j)
                points = list(add.get("at") or [])
                for point in rem.get("at") or []:
                    if point not in points:
                        points.append(point)
                replaced.append({
                    "status": "replaced",
                    "kind": add["kind"],
                    "category": add["category"],
                    "key": add["key"],
                    "reference": add["reference"],
                    "value": add.get("value"),
                    "at": points,
                    "detail": {
                        "from": {"key": rem["key"], "value": rem.get("value"), "libId": rem.get("libId")},
                        "to": {"key": add["key"], "value": add.get("value"), "libId": add.get("libId")},
                    },
                })
    out = [item for i, item in enumerate(removed) if i not in used_removed]
    out.extend(item for j, item in enumerate(added) if j not in used_added)
    out.extend(rest)
    out.extend(replaced)
    return out


def _public_change(item: dict) -> dict:
    change = {
        "status": item["status"],
        "kind": item["kind"],
        "category": item["category"],
        "key": item["key"],
        "reference": item.get("reference") or "",
        "value": item.get("value"),
        "at": item.get("at") or [],
    }
    if item.get("detail"):
        change["detail"] = item["detail"]
    return change


def _finish_page(*, page: Page | None, kind: str, changes: list[dict], render_items: list[dict],
                 crop: list[float] | None, side: str | None) -> dict:
    public = [_public_change(item) for item in changes]
    if kind == "schematic" and page is not None:
        return {
            "id": f"sch:{page.number}",
            "kind": "schematic",
            "page": page.number,
            "sheetPath": page.sheet_path,
            "sheetNames": page.sheet_names,
            "file": page.file,
            "paper": page.paper,
            "_full_path": page.full_path,
            "_file_uuid": page.file_uuid,
            "changes": public,
            "_render": render_items,
        }
    return {
        "id": f"pcb:{side}",
        "kind": "board",
        "side": side,
        "crop": crop,
        "changes": public,
        "_render": render_items,
    }


def _summary(pages: list[dict]) -> dict:
    counts: dict[str, dict[str, int]] = {}
    for page in pages:
        # Front and back describe the same objects. Count the board once.
        if page.get("kind") == "board" and page.get("side") != "front":
            continue
        for change in page["changes"]:
            cat = change["category"]
            status = change["status"]
            counts.setdefault(cat, {})
            counts[cat][status] = counts[cat].get(status, 0) + 1
    return {
        cat: {status: count for status, count in statuses.items() if count}
        for cat, statuses in counts.items()
        if any(statuses.values())
    }


def compute_diff(base: Project, target: Project) -> dict:
    pages = _classify_sheet_pages(base, target) + _classify_board(base, target)
    return {"pages": pages, "summary": _summary(pages)}


def _wanted_categories(spec: dict) -> set[str] | None:
    categories = spec.get("categories")
    if categories is None:
        return None
    if not isinstance(categories, list) or any(not isinstance(item, str) for item in categories):
        raise invalid("spec.categories must be a list of strings")
    return set(categories)


def _wanted_sides(spec: dict) -> list[str]:
    sides = spec.get("board", ["front", "back"])
    if sides is None:
        return []
    if not isinstance(sides, list) or any(side not in {"front", "back"} for side in sides):
        raise invalid("spec.board must be a list of 'front' and 'back'")
    return list(sides)


def _revision_ids(spec: dict, base_bytes: bytes | None, target_bytes: bytes | None) -> tuple[str, str]:
    base_id = spec.get("base") or spec.get("baseSha")
    target_id = spec.get("target") or spec.get("targetSha")
    if not isinstance(base_id, str) or not base_id:
        base_id = hashlib.sha256(base_bytes or b"base").hexdigest()[:12]
    if not isinstance(target_id, str) or not target_id:
        target_id = hashlib.sha256(target_bytes or b"target").hexdigest()[:12]
    return base_id, target_id


def _only_svg(directory: Path) -> Path | None:
    svgs = [path for path in directory.rglob("*.svg") if path.is_file()]
    if len(svgs) == 1:
        return svgs[0]
    return svgs[0] if svgs else None


def _run_export(args_builder, dest: Path, *, env, timeout, cwd: Path) -> bytes:
    from app.cli import run_kicad
    folder = dest.parent / (dest.stem + "-out")
    folder.mkdir(parents=True, exist_ok=True)
    run_kicad(args_builder(folder), cwd=cwd, env=env, timeout=timeout)
    found = _only_svg(folder)
    if found is None:
        from app.errors import kicad_failed
        raise kicad_failed("kicad-cli produced no SVG")
    return found.read_bytes()


def _kicad_schematic_svg(sch: Path, *, highlight: bool, page: str | None, env, timeout, cwd: Path) -> bytes:
    # A private directory: every page used to share ``export-out`` and the first SVG
    # (the root sheet) was then reused as the background of every later page.
    parent = cwd if cwd.is_dir() else sch.parent
    work = Path(tempfile.mkdtemp(prefix="kicomlib-sch-", dir=str(parent)))
    dest = work / "export.svg"

    def build(folder: Path) -> list[str]:
        return schematic_svg_command(sch, folder, highlight=highlight, pages=page)

    return _run_export(build, dest, env=env, timeout=timeout, cwd=cwd)


def _kicad_board_svg(board: Path, side: str, *, env, timeout, cwd: Path) -> bytes:
    dest = board.parent / f"{side}.svg"

    def build(folder: Path) -> list[str]:
        # mode-single wants the output path to be the file, not a directory.
        return board_svg_command(board, folder / f"{side}.svg", side)

    svg = _run_export(build, dest, env=env, timeout=timeout, cwd=cwd)
    return svg


def write_sheet_as_root(directory: Path, source: Project, page: Page,
                        selected: set[str] | None = None) -> Path:
    """Write one sheet as ``{project}.kicad_sch`` with a single root instance.

    Plotting that file draws only this sheet. Plotting the root file with
    ``--pages`` draws the root sheet instead.
    """
    doc = source.sheets[page.file]
    if selected is None:
        selected = {obj.key for obj in doc.objects if obj.key}
    info = {
        "page": page.number,
        "sheetPath": page.sheet_path,
        "sheetNames": page.sheet_names,
        "file": page.file,
        "paper": page.paper,
        "_full_path": page.full_path,
        "_file_uuid": page.file_uuid,
    }
    return _write_filtered_schematic(directory, source, source, info, selected, [])


def _export_workers() -> int:
    try:
        return max(1, int(os.environ.get("RUNNER_MAX_PARALLEL", "2")))
    except ValueError:
        return 2


def _write_filtered_schematic(directory: Path, project: Project, source: Project, page_info: dict,
                              selected: set[str], items: list[dict]) -> Path:
    directory.mkdir(parents=True, exist_ok=True)
    pro = source.project_dir / f"{source.name}.kicad_pro"
    (directory / pro.name).write_bytes(pro.read_bytes())
    doc = source.sheets[page_info["file"]]
    by_key = {obj.key: obj for obj in doc.objects}

    def rewrite(node: Node, raw: str) -> str:
        key = _key_from_node(node)
        obj = by_key.get(key)
        if obj is None:
            return raw
        page = Page(
            page_info["page"], page_info["sheetPath"], page_info["sheetNames"], page_info["file"],
            page_info.get("paper", "A4"), page_info["_full_path"], page_info["_file_uuid"],
        )
        return rewrite_symbol_instances(
            raw,
            project=source.name,
            sheet_file_uuid=doc.file_uuid,
            reference=ref_for(obj, page),
            unit=unit_for(obj, page),
        )

    text = filtered_document(doc.text, selected, domain="schematic", as_root_sheet=True, rewrite=rewrite)
    dest = directory / f"{source.name}.kicad_sch"
    dest.write_text(text, encoding="utf-8")
    return dest


def _write_filtered_board(directory: Path, source: Project, selected: set[str]) -> Path:
    directory.mkdir(parents=True, exist_ok=True)
    assert source.board is not None
    text = filtered_document(source.board.text, selected, domain="board", as_root_sheet=False, rewrite=None)
    dest = directory / Path(source.board.rel).name
    dest.write_text(text, encoding="utf-8")
    return dest


def _lookup_obj(project: Project, file_rel: str, obj_key: str, domain: str) -> DrawnObject | None:
    if domain == "schematic":
        doc = project.sheets.get(file_rel)
        if doc is None:
            return None
        for obj in doc.objects:
            if obj.key == obj_key:
                return obj
        return None
    if project.board is None:
        return None
    for obj in project.board.objects:
        if obj.key == obj_key:
            return obj
    return None


def _can_write(path: Path) -> bool:
    """True when this process can create a directory in path. A mounted /work can exist and still be root-only."""
    try:
        probe = path / f".write-probe-{uuid.uuid4().hex}"
        probe.write_text("ok", encoding="utf-8")
        probe.unlink()
        return True
    except OSError:
        return False


def _scratch_parent(explicit: Path | None) -> str | None:
    if explicit is not None and explicit.is_dir() and _can_write(explicit):
        return str(explicit)
    configured = os.environ.get("RUNNER_WORK_DIR")
    if configured:
        candidate = Path(configured)
        if candidate.is_dir() and _can_write(candidate):
            return str(candidate)
    return None


def build_zip(base: Project, target: Project, spec: dict, *, kicad: str,
              base_bytes: bytes | None = None, target_bytes: bytes | None = None,
              env: dict | None = None, timeout: float = 600,
              schematic_exporter=None, board_exporter=None,
              scratch_dir: Path | None = None) -> bytes:
    """Classify, render base images and highlight layers, and return the zip bytes.

    ``schematic_exporter(sch_path, highlight, page) -> svg bytes`` and
    ``board_exporter(pcb_path, side) -> svg bytes`` override kicad-cli (tests).
    """
    categories = _wanted_categories(spec)
    sides = _wanted_sides(spec)
    diff = compute_diff(base, target)
    base_id, target_id = _revision_ids(spec, base_bytes, target_bytes)
    files: dict[str, bytes] = {}
    public_pages = []
    scratch_parent = _scratch_parent(scratch_dir)

    def export_sch(path: Path, highlight: bool, page: str | None) -> bytes:
        if schematic_exporter is not None:
            return schematic_exporter(path, highlight=highlight, page=page)
        if env is None:
            raise invalid("kicad environment is missing")
        return _kicad_schematic_svg(path, highlight=highlight, page=page, env=env, timeout=timeout, cwd=path.parent)

    def export_board(path: Path, side: str) -> bytes:
        if board_exporter is not None:
            return board_exporter(path, side=side)
        if env is None:
            raise invalid("kicad environment is missing")
        return _kicad_board_svg(path, side, env=env, timeout=timeout, cwd=path.parent)

    with tempfile.TemporaryDirectory(prefix="kicomlib-diff-", dir=scratch_parent) as scratch_name:
      scratch = Path(scratch_name)
      schematic_bases: dict[str, dict[str, bytes]] = {}

      def schematic_base(source: Project, page: dict) -> bytes:
          # Tests inject an exporter. The real plot is one export of every sheet,
          # not ``--pages N`` on the root (that SVG is the first page for every N).
          if schematic_exporter is not None:
              return export_sch(_root_schematic(source), highlight=False, page=page["page"])
          key = str(source.project_dir)
          if key not in schematic_bases:
              if env is None:
                  raise invalid("kicad environment is missing")
              folder = scratch / f"bases-{len(schematic_bases)}"
              schematic_bases[key] = export_schematic_pages(source, folder, env=env, timeout=timeout)
          try:
              return schematic_bases[key][page["page"]]
          except KeyError as exc:
              from app.errors import kicad_failed
              raise kicad_failed(f"kicad-cli produced no SVG for schematic page {page['page']}") from exc

      for page in diff["pages"]:
        if page["kind"] == "board" and page.get("side") not in sides:
            continue
        render_items = page.pop("_render")
        if page["kind"] == "schematic":
            base_name = f"sch/p{page['page']}/base.svg"
            source = target if _page_in(target, page) else base
            svg = schematic_base(source, page)
        else:
            base_name = f"pcb/{page['side']}/base.svg"
            source = target if target.board is not None else base
            assert source.board is not None
            svg = export_board(source.project_dir / source.board.rel, page["side"])
        files[base_name] = svg
        base_view = viewbox_of(svg.decode("utf-8", errors="replace"))
        layers = []
        groups: dict[tuple[str, str], list[dict]] = {}
        for item in render_items:
            if categories is not None and item["category"] not in categories:
                continue
            groups.setdefault((item["status"], item["category"]), []).append(item)

        def render_group(group: tuple[tuple[str, str], list[dict]]) -> dict | None:
            (status, category), items = group
            layer_name = (
                f"sch/p{page['page']}/{status}-{category}.svg"
                if page["kind"] == "schematic"
                else f"pcb/{page['side']}/{status}-{category}.svg"
            )
            selected = {item["objKey"] for item in items}
            ident = page.get("page") or page.get("side")
            work = scratch / f"{page['kind']}-{ident}-{status}-{category}"
            if page["kind"] == "schematic":
                revision = base if status == "removed" else target
                if page["file"] not in revision.sheets:
                    revision = base if status == "removed" else target
                sch = _write_filtered_schematic(work, revision, revision, page, selected, items)
                raw = export_sch(sch, highlight=True, page="1")
            else:
                revision = base if status == "removed" else target
                if revision.board is None:
                    return None
                pcb = _write_filtered_board(work, revision, selected)
                raw = export_board(pcb, page["side"])
            colored = color_svg(raw.decode("utf-8", errors="replace"), COLORS[status]).encode("utf-8")
            layer_view = viewbox_of(colored.decode("utf-8", errors="replace"))
            return {
                "status": status,
                "category": category,
                "file": layer_name,
                "aligned": viewboxes_match(base_view, layer_view),
                "_bytes": colored,
            }

        ordered = sorted(groups.items())
        if len(ordered) > 1:
            workers = min(len(ordered), _export_workers())
            with ThreadPoolExecutor(max_workers=workers) as pool:
                rendered_layers = list(pool.map(render_group, ordered))
        else:
            rendered_layers = [render_group(group) for group in ordered]
        for layer in rendered_layers:
            if layer is None:
                continue
            files[layer["file"]] = layer.pop("_bytes")
            layers.append(layer)
        public = {key: value for key, value in page.items() if not key.startswith("_")}
        public["viewBox"] = base_view
        public["base"] = base_name
        public["layers"] = layers
        public_pages.append(public)

    manifest = {
        "version": 1,
        "kicad": kicad,
        "base": base_id,
        "target": target_id,
        "summary": diff["summary"],
        "pages": public_pages,
    }
    files["manifest.json"] = json.dumps(manifest, indent=2).encode("utf-8")
    buf = io.BytesIO()
    with zipfile.ZipFile(buf, "w", compression=zipfile.ZIP_DEFLATED) as archive:
        for name, data in files.items():
            archive.writestr(name, data)
    return buf.getvalue()


def _page_in(project: Project, page: dict) -> bool:
    if page["kind"] == "board":
        return project.board is not None
    return any(item.number == page["page"] and item.sheet_path == page["sheetPath"] and item.file == page["file"]
               for item in project.pages)


def _root_schematic(project: Project) -> Path:
    root = next(item for item in project.pages if item.sheet_path == "/")
    return project.project_dir / root.file


def visual_diff(base_root: Path, target_root: Path, spec: dict, *, env: dict, timeout: float, kicad: str,
                base_bytes: bytes | None = None, target_bytes: bytes | None = None,
                scratch_dir: Path | None = None) -> bytes:
    project_file = spec.get("projectFile")
    if not isinstance(project_file, str) or not project_file:
        raise invalid("spec.projectFile is required")
    base = load_project(base_root, project_file)
    target = load_project(target_root, project_file)
    return build_zip(
        base, target, spec, kicad=kicad, base_bytes=base_bytes, target_bytes=target_bytes,
        env=env, timeout=timeout, scratch_dir=scratch_dir,
    )

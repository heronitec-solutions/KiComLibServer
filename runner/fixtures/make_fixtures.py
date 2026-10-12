"""Write the small original KiCad 10 fixtures. Not a runtime dependency."""

from __future__ import annotations

import json
from pathlib import Path

OUT = Path(__file__).resolve().parent
VERSION = 20250114

FLAT_ROOT = "11111111-1111-4111-8111-111111111111"
HIER_ROOT = "22222222-2222-4222-8222-222222222222"
CHANNEL_FILE = "33333333-3333-4333-8333-333333333333"
SHEET_CH1 = "44444444-4444-4444-8444-444444444444"
SHEET_CH2 = "55555555-5555-4555-8555-555555555555"
CHAN_R = "66666666-6666-4666-8666-666666666666"

R1 = "b1111111-1111-4111-8111-111111111111"
R2 = "b2222222-2222-4222-8222-222222222222"
R3 = "b3333333-3333-4333-8333-333333333333"
R4 = "b4444444-4444-4444-8444-444444444444"
C1 = "c1111111-1111-4111-8111-111111111111"
C2 = "c2222222-2222-4222-8222-222222222222"
P1_OLD = "d1111111-1111-4111-8111-111111111111"
P1_NEW = "d2222222-2222-4222-8222-222222222222"
D1 = "e1111111-1111-4111-8111-111111111111"
U1A = "f1111111-1111-4111-8111-111111111111"
U1B = "f2222222-2222-4222-8222-222222222222"
TEXT = "a1111111-1111-4111-8111-111111111111"
WIRE = "a2222222-2222-4222-8222-222222222222"
WIRE2 = "a3333333-3333-4333-8333-333333333333"

FP_R1 = "91b11111-1111-4111-8111-111111111111"
FP_R2 = "91b22222-2222-4222-8222-222222222222"
FP_R3 = "91b33333-3333-4333-8333-333333333333"
FP_C1 = "91c11111-1111-4111-8111-111111111111"
FP_C2 = "91c22222-2222-4222-8222-222222222222"
FP_P1_OLD = "91d11111-1111-4111-8111-111111111111"
FP_P1_NEW = "91d22222-2222-4222-8222-222222222222"
FP_H1 = "91e11111-1111-4111-8111-111111111111"
SEG = "92a11111-1111-4111-8111-111111111111"
VIA = "92a22222-2222-4222-8222-222222222222"
ZONE = "92a33333-3333-4333-8333-333333333333"
EDGE = "92a44444-4444-4444-8444-444444444444"


def pro(filename: str) -> str:
    data = {
        "board": {
            "design_settings": {
                "defaults": {},
                "diff_pair_dimensions": [],
                "drc_exclusions": [],
                "rules": {},
                "track_widths": [],
                "via_dimensions": [],
            },
            "layer_presets": [],
            "viewports": [],
        },
        "boards": [],
        "cvpcb": {"equivalence_files": []},
        "libraries": {"pinned_footprint_libs": [], "pinned_symbol_libs": []},
        "meta": {"filename": filename, "version": 3},
        "net_settings": {
            "classes": [{"name": "Default", "priority": 2147483647}],
            "meta": {"version": 4},
            "netclass_patterns": [],
        },
        "pcbnew": {
            "last_paths": {
                "gencad": "", "idf": "", "netlist": "", "plot": "", "pos_files": "",
                "specctra_dsn": "", "step": "", "svg": "", "vrml": "",
            },
            "page_layout_descr_file": "",
        },
        "schematic": {"legacy_lib_dir": "", "legacy_lib_list": []},
        "sheets": [],
        "text_variables": {},
    }
    return json.dumps(data, indent=2) + "\n"


def lib_symbol(lib_id: str, body: float) -> str:
    part = lib_id.split(":")[-1]
    return f"""(symbol "{lib_id}"
  (pin_numbers hide)
  (pin_names (offset 0))
  (exclude_from_sim no)
  (in_bom yes)
  (on_board yes)
  (property "Reference" "U" (at 2.032 0 0) (effects (font (size 1.27 1.27))))
  (property "Value" "U" (at 0 0 0) (effects (font (size 1.27 1.27))))
  (property "Footprint" "" (at 0 0 0) (effects (font (size 1.27 1.27)) (hide yes)))
  (property "Datasheet" "~" (at 0 0 0) (effects (font (size 1.27 1.27)) (hide yes)))
  (symbol "{part}_0_1"
    (rectangle (start {-body} {-body * 2}) (end {body} {body * 2})
      (stroke (width 0.254) (type default))
      (fill (type none))))
  (symbol "{part}_1_1"
    (pin passive line (at 0 3.81 270) (length 1.27)
      (name "~" (effects (font (size 1.27 1.27))))
      (number "1" (effects (font (size 1.27 1.27)))))
    (pin passive line (at 0 -3.81 90) (length 1.27)
      (name "~" (effects (font (size 1.27 1.27))))
      (number "2" (effects (font (size 1.27 1.27)))))))"""


def prop(name: str, value: str, x: float, y: float, hide: bool = False) -> str:
    hidden = " (hide yes)" if hide else ""
    return (
        f'(property "{name}" "{value}" (at {x} {y} 0)'
        f' (effects (font (size 1.27 1.27)){hidden}))'
    )


def symbol(uuid: str, lib: str, x: float, y: float, ref: str, value: str, footprint: str,
           project: str, instances: list[tuple[str, str, int]], unit: int = 1,
           dnp: str = "no", in_bom: str = "yes", on_board: str = "yes",
           fields: dict[str, str] | None = None) -> str:
    lines = [
        "(symbol",
        f'  (lib_id "{lib}")',
        f"  (at {x} {y} 0)",
        f"  (unit {unit})",
        "  (exclude_from_sim no)",
        f"  (in_bom {in_bom})",
        f"  (on_board {on_board})",
        f"  (dnp {dnp})",
        f'  (uuid "{uuid}")',
        "  " + prop("Reference", ref, x + 2, y - 2),
        "  " + prop("Value", value, x + 2, y + 2),
        "  " + prop("Footprint", footprint, x, y, hide=True),
        "  " + prop("Datasheet", "~", x, y, hide=True),
    ]
    for fname, fvalue in (fields or {}).items():
        lines.append("  " + prop(fname, fvalue, x, y, hide=True))
    lines.append(f'  (pin "1" (uuid "{uuid[:-2]}01"))')
    lines.append(f'  (pin "2" (uuid "{uuid[:-2]}02"))')
    inst = ["  (instances", f'    (project "{project}"']
    for path, iref, iunit in instances:
        inst.append(f'      (path "{path}" (reference "{iref}") (unit {iunit}))')
    inst.append("    )")
    inst.append("  )")
    lines.extend(inst)
    lines.append(")")
    return "\n".join(lines)


def sheet(uuid: str, x: float, y: float, name: str, filename: str, page: str,
          project: str, root_uuid: str) -> str:
    return f"""(sheet
  (at {x} {y})
  (size 30 20)
  (exclude_from_sim no)
  (in_bom yes)
  (on_board yes)
  (dnp no)
  (stroke (width 0.1524) (type solid))
  (fill (color 0 0 0 0))
  (uuid "{uuid}")
  (property "Sheetname" "{name}" (at {x} {y - 1} 0)
    (effects (font (size 1.27 1.27)) (justify left bottom)))
  (property "Sheetfile" "{filename}" (at {x} {y + 21} 0)
    (effects (font (size 1.27 1.27)) (justify left top)))
  (instances (project "{project}" (path "/{root_uuid}" (page "{page}"))))
)"""


def schematic(project: str, file_uuid: str, title: str, rev: str, libs: list[str],
              body: list[str], sheet_paths: list[tuple[str, str]]) -> str:
    pages = "\n".join(
        f'    (path "{path}" (page "{page}"))' for path, page in sheet_paths
    )
    lib_block = "\n".join(libs)
    objects = "\n".join(body)
    return f"""(kicad_sch
  (version {VERSION})
  (generator "eeschema")
  (generator_version "10.0")
  (uuid "{file_uuid}")
  (paper "A4")
  (title_block
    (title "{title}")
    (date "2026-09-27")
    (rev "{rev}")
    (company "KiComLib")
  )
  (lib_symbols
{lib_block}
  )
{objects}
  (sheet_instances
{pages}
  )
  (embedded_fonts no)
)
"""


def layers() -> str:
    rows = [
        (0, "F.Cu", "signal", None),
        (2, "B.Cu", "signal", None),
        (9, "F.Adhes", "user", "F.Adhesive"),
        (11, "B.Adhes", "user", "B.Adhesive"),
        (13, "F.Paste", "user", None),
        (15, "B.Paste", "user", None),
        (5, "F.SilkS", "user", "F.Silkscreen"),
        (7, "B.SilkS", "user", "B.Silkscreen"),
        (1, "F.Mask", "user", None),
        (3, "B.Mask", "user", None),
        (17, "Dwgs.User", "user", "User.Drawings"),
        (19, "Cmts.User", "user", "User.Comments"),
        (21, "Eco1.User", "user", "User.Eco1"),
        (23, "Eco2.User", "user", "User.Eco2"),
        (25, "Edge.Cuts", "user", None),
        (27, "Margin", "user", None),
        (31, "F.CrtYd", "user", "F.Courtyard"),
        (29, "B.CrtYd", "user", "B.Courtyard"),
        (35, "F.Fab", "user", None),
        (33, "B.Fab", "user", None),
    ]
    lines = []
    for idx, name, kind, user in rows:
        alias = f' "{user}"' if user else ""
        lines.append(f'    ({idx} "{name}" {kind}{alias})')
    return "\n".join(lines)


def footprint(lib: str, uuid: str, x: float, y: float, ref: str, value: str,
              path: str | None, pad: float) -> str:
    path_line = f'\n  (path "{path}")' if path else ""
    return f"""(footprint "{lib}"
  (layer "F.Cu")
  (uuid "{uuid}")
  (at {x} {y}){path_line}
  (attr smd)
  (property "Reference" "{ref}" (at 0 -1.5 0) (layer "F.SilkS")
    (uuid "{uuid[: -3]}a01") (effects (font (size 1 1) (thickness 0.15))))
  (property "Value" "{value}" (at 0 1.5 0) (layer "F.Fab")
    (uuid "{uuid[: -3]}a02") (effects (font (size 1 1) (thickness 0.15))))
  (fp_line (start -0.8 -0.4) (end 0.8 -0.4) (stroke (width 0.12) (type solid)) (layer "F.SilkS")
    (uuid "{uuid[: -3]}a03"))
  (fp_line (start 0.8 -0.4) (end 0.8 0.4) (stroke (width 0.12) (type solid)) (layer "F.SilkS")
    (uuid "{uuid[: -3]}a04"))
  (fp_line (start 0.8 0.4) (end -0.8 0.4) (stroke (width 0.12) (type solid)) (layer "F.SilkS")
    (uuid "{uuid[: -3]}a05"))
  (fp_line (start -0.8 0.4) (end -0.8 -0.4) (stroke (width 0.12) (type solid)) (layer "F.SilkS")
    (uuid "{uuid[: -3]}a06"))
  (pad "1" smd rect (at -0.8 0) (size {pad} 0.7) (layers "F.Cu" "F.Paste" "F.Mask")
    (uuid "{uuid[: -3]}a07"))
  (pad "2" smd rect (at 0.8 0) (size {pad} 0.7) (layers "F.Cu" "F.Paste" "F.Mask")
    (uuid "{uuid[: -3]}a08"))
)"""


def zone(fill: str) -> str:
    return f"""(zone
  (net 0)
  (net_name "")
  (layer "F.Cu")
  (uuid "{ZONE}")
  (name "ZONE")
  (hatch edge 0.5)
  (connect_pads yes (clearance 0.5))
  (min_thickness 0.25)
  (filled_areas_thickness no)
  (fill yes (thermal_gap 0.5) (thermal_bridge_width 0.5))
  (polygon (pts (xy 60 50) (xy 90 50) (xy 90 70) (xy 60 70)))
  (filled_polygon (layer "F.Cu") (pts {fill}))
)"""


def board(footprints: list[str], segment_end: str, via_at: str, fill: str) -> str:
    fps = "\n".join(footprints)
    return f"""(kicad_pcb
  (version {VERSION})
  (generator "pcbnew")
  (generator_version "10.0")
  (general (thickness 1.6) (legacy_teardrops no))
  (paper "A4")
  (layers
{layers()}
  )
  (setup (pad_to_mask_clearance 0))
  (net 0 "")
{fps}
  (gr_rect (start 40 30) (end 220 160)
    (stroke (width 0.05) (type solid)) (fill none) (layer "Edge.Cuts") (uuid "{EDGE}"))
  (segment (start 100 80) (end {segment_end}) (width 0.25) (layer "F.Cu") (net 0) (uuid "{SEG}"))
  (via (at {via_at}) (size 0.8) (drill 0.4) (layers "F.Cu" "B.Cu") (net 0) (uuid "{VIA}"))
  {zone(fill)}
  (embedded_fonts no)
)
"""


def flat_symbols(rev: str) -> list[str]:
    root = f"/{FLAT_ROOT}"
    moved = rev == "v2"
    r3 = (160, 90) if moved else (140, 80)
    text_at = (40, 30) if moved else (30, 30)
    r1_value = "4.7k" if moved else "10k"
    r4_ref = "R40" if moved else "R4"
    u_value = "LM358A" if moved else "LM358"
    symbols = [
        symbol(R1, "Device:R", 100, 80, "R1", r1_value, "Resistor_SMD:R_0603_1608Metric",
               "flat", [(root, "R1", 1)], fields={"MPN": "RC0603FR-0710KL", "KiComLibUUID": "aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee"}),
        symbol(R3, "Device:R", r3[0], r3[1], "R3", "1k", "Resistor_SMD:R_0603_1608Metric",
               "flat", [(root, "R3", 1)]),
        symbol(R4, "Device:R", 180, 80, r4_ref, "100", "Resistor_SMD:R_0603_1608Metric",
               "flat", [(root, r4_ref, 1)]),
        symbol(C2, "Device:C", 140, 120, "C2", "10n", "Capacitor_SMD:C_0603_1608Metric",
               "flat", [(root, "C2", 1)]),
        symbol(D1, "Device:R", 50, 100, "D1", "DNP", "Resistor_SMD:R_0603_1608Metric",
               "flat", [(root, "D1", 1)], dnp="yes", in_bom="no", on_board="no"),
        symbol(U1A, "Device:R", 50, 150, "U1", u_value, "Package_SO:SOIC-8",
               "flat", [(root, "U1", 1)], unit=1),
        symbol(U1B, "Device:R", 80, 150, "U1", u_value, "Package_SO:SOIC-8",
               "flat", [(root, "U1", 2)], unit=2),
    ]
    if moved:
        symbols.append(symbol(R2, "Device:R", 200, 80, "R2", "220", "Resistor_SMD:R_0603_1608Metric",
                              "flat", [(root, "R2", 1)]))
        symbols.append(symbol(P1_NEW, "Connector:TestPoint", 60, 60, "P1", "TP", "TestPoint:Pad",
                              "flat", [(root, "P1", 1)]))
    else:
        symbols.append(symbol(C1, "Device:C", 100, 120, "C1", "100n", "Capacitor_SMD:C_0603_1608Metric",
                              "flat", [(root, "C1", 1)]))
        symbols.append(symbol(P1_OLD, "Connector:Pin", 50, 50, "P1", "PIN", "Connector:Pin",
                              "flat", [(root, "P1", 1)]))
    symbols.append(
        f'(text "note" (exclude_from_sim no) (at {text_at[0]} {text_at[1]} 0)\n'
        f'  (effects (font (size 2.54 2.54))) (uuid "{TEXT}"))'
    )
    symbols.append(
        f"(wire (pts (xy 100 76.19) (xy 100 70)) (stroke (width 0) (type default)) (uuid \"{WIRE}\"))"
    )
    if moved:
        symbols.append(
            f"(wire (pts (xy 120 70) (xy 140 70)) (stroke (width 0) (type default)) (uuid \"{WIRE2}\"))"
        )
    return symbols


def flat_libs(rev: str) -> list[str]:
    libs = [lib_symbol("Device:R", 1.016), lib_symbol("Device:C", 2.0 if rev == "v2" else 1.016)]
    if rev == "v2":
        libs.append(lib_symbol("Connector:TestPoint", 0.5))
    else:
        libs.append(lib_symbol("Connector:Pin", 0.8))
    return libs


def flat_footprints(rev: str) -> list[str]:
    moved = rev == "v2"
    r3 = (160, 90) if moved else (140, 80)
    r1_pad = 0.9 if moved else 0.6
    items = [
        footprint("Resistor_SMD:R_0603_1608Metric", FP_R1, 100, 80, "R1",
                  "4.7k" if moved else "10k", f"/{R1}", r1_pad),
        footprint("Resistor_SMD:R_0603_1608Metric", FP_R3, r3[0], r3[1], "R3", "1k", f"/{R3}", 0.6),
        footprint("Capacitor_SMD:C_0603_1608Metric", FP_C2, 140, 120, "C2", "10n", f"/{C2}", 0.6),
        footprint("MountingHole:Hole", FP_H1, 45, 45, "H1", "MountingHole", None, 0.6),
    ]
    if moved:
        items.append(footprint("Resistor_SMD:R_0603_1608Metric", FP_R2, 200, 80, "R2", "220", f"/{R2}", 0.6))
        items.append(footprint("TestPoint:Pad", FP_P1_NEW, 60, 60, "P1", "TP", f"/{P1_NEW}", 0.6))
    else:
        items.append(footprint("Capacitor_SMD:C_0603_1608Metric", FP_C1, 100, 120, "C1", "100n", f"/{C1}", 0.6))
        items.append(footprint("Connector:Pin", FP_P1_OLD, 50, 50, "P1", "PIN", f"/{P1_OLD}", 0.6))
    return items


def write_flat(folder: str, rev: str, title_rev: str) -> None:
    dest = OUT / folder
    dest.mkdir(parents=True, exist_ok=True)
    (dest / "flat.kicad_pro").write_text(pro("flat.kicad_pro"), encoding="utf-8")
    sch = schematic(
        "flat", FLAT_ROOT, "Flat", title_rev, flat_libs(rev), flat_symbols(rev),
        [("/", "1")],
    )
    (dest / "flat.kicad_sch").write_text(sch, encoding="utf-8")
    fill = "(xy 62 52) (xy 88 52) (xy 88 68) (xy 62 68)" if rev == "v2" else "(xy 61 51) (xy 89 51) (xy 89 69) (xy 61 69)"
    pcb = board(
        flat_footprints(rev),
        "150 80" if rev == "v2" else "140 80",
        "112 90" if rev == "v2" else "110 90",
        fill,
    )
    (dest / "flat.kicad_pcb").write_text(pcb, encoding="utf-8")


def write_hier() -> None:
    dest = OUT / "hier"
    dest.mkdir(parents=True, exist_ok=True)
    (dest / "hier.kicad_pro").write_text(pro("hier.kicad_pro"), encoding="utf-8")
    root_body = [
        sheet(SHEET_CH1, 40, 40, "CH1", "channel.kicad_sch", "2", "hier", HIER_ROOT),
        sheet(SHEET_CH2, 90, 40, "CH2", "channel.kicad_sch", "3", "hier", HIER_ROOT),
    ]
    root = schematic("hier", HIER_ROOT, "Hierarchy", "A", [], root_body, [("/", "1")])
    (dest / "hier.kicad_sch").write_text(root, encoding="utf-8")
    chan_sym = symbol(
        CHAN_R, "Device:R", 100, 80, "R1", "10k", "Resistor_SMD:R_0603_1608Metric", "hier",
        [
            (f"/{HIER_ROOT}/{SHEET_CH1}", "R1", 1),
            (f"/{HIER_ROOT}/{SHEET_CH2}", "R11", 1),
        ],
        fields={"MPN": "RC0603FR-0710KL"},
    )
    chan = schematic(
        "hier", CHANNEL_FILE, "Channel", "A", [lib_symbol("Device:R", 1.016)], [chan_sym],
        [(f"/{HIER_ROOT}/{SHEET_CH1}", "2"), (f"/{HIER_ROOT}/{SHEET_CH2}", "3")],
    )
    (dest / "channel.kicad_sch").write_text(chan, encoding="utf-8")


def main() -> None:
    write_flat("flat", "v1", "A")
    write_flat("flat_v2", "v2", "B")
    write_hier()


if __name__ == "__main__":
    main()

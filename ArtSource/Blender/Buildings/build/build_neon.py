"""VEYRA CITY - D6 Neon Mile low-rise / commercial set (original designs).

    blender --factory-startup -b -noaudio -P build_neon.py -- <key|all>

From the data: D6 holds 119 records - 79 `BrickIndustrial` (short side ~56 m, long side 60 m for 39 of
them and ~105-125 m for 11, height 24-62 m, median 34) and 37 `ModernLowrise` (short ~57, long ~60 for 20
and ~120 for 7, height 13-22 m, median 18.5); all `box` shape. The seven models below were chosen by
simulating BuildingAssembler's fit rule over those records: they fit 109 of 116.

Signage is BLANK - frames, panels and a rooftop sign board with no lettering, no logo, nothing that reads
as a brand. Every emissive surface is on its own slot, `VC_Neon_Emissive`, so Unity drives `_EMISSION`
(and the colour) at night, the same pattern as the street set's `VC_Lamp_Emissive`.
"""
import json
import os
import sys

import bmesh
import bpy
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
sys.path.insert(0, r"E:\VeyraCity\Tools\Blender")
sys.path.insert(0, HERE)
import vc_tools                      # noqa: E402
import vc_bld_lib as L               # noqa: E402

EXPORT = os.path.join(ROOT, "export")
BRICK_SLOTS = ["VC_Nm_Brick", "VC_Nm_Glass", "VC_Nm_Metal", "VC_Nm_Concrete", "VC_Nm_Shutter",
               "VC_Neon_Emissive"]
PANEL_SLOTS = ["VC_Nm_Panel", "VC_Nm_Glass", "VC_Nm_Metal", "VC_Nm_Concrete", "VC_Nm_Shutter",
               "VC_Neon_Emissive"]
WALL, GLASS, METAL, CONC, SHUT, NEON = 0, 1, 2, 3, 4, 5


def grid_facade(bm, w, l, z0, z1, floor_h, pitch, m_wall, m_glass, lod=0):
    """Masonry commercial facade: glazing recessed behind full-height piers and per-floor spandrel bands.
    Continuous piers and bands instead of per-window boxes - a 57 m wide, 11 storey block would otherwise
    cost thousands of triangles for the same read."""
    L.box(bm, (w - 0.8, l - 0.8, z1 - z0), base_at=(0, 0, z0), m=m_glass)
    if lod >= 2:
        L.box(bm, (w, l, z1 - z0), base_at=(0, 0, z0), m=m_wall)
        return
    step = 1 if lod == 0 else 2
    nx, ny = max(2, int(w / pitch)), max(2, int(l / pitch))
    for i in range(0, nx + 1, step):
        x = -w / 2.0 + w * i / nx
        for sy in (-1, 1):
            L.box(bm, (0.9 * step, 0.55, z1 - z0), base_at=(x, sy * (l / 2.0 - 0.2), z0), m=m_wall)
    for j in range(0, ny + 1, step):
        y = -l / 2.0 + l * j / ny
        for sx in (-1, 1):
            L.box(bm, (0.55, 0.9 * step, z1 - z0), base_at=(sx * (w / 2.0 - 0.2), y, z0), m=m_wall)
    n = max(1, int((z1 - z0) / floor_h))
    for k in range(0, n + 1, step):
        z = z0 + (z1 - z0) * k / n
        if z >= z1 - 0.05:
            continue
        L.box(bm, (w + 0.06, l + 0.06, 0.55), base_at=(0, 0, z), m=m_wall)


def street_front(bm, w, l, h, m_wall, lod=0, bays=None):
    """Ground floor on the street side (-Y): a run of bays - glazed shopfront, roll-up shutter, recessed
    entry - under a continuous awning, with blank blade signs between them. This is the part a driver sees."""
    bays = bays or ("glass", "entry", "glass", "shutter", "glass")
    n = len(bays)
    pier = 0.9
    span = w / n
    L.box(bm, (w, 0.5, h), base_at=(0, -l / 2.0 + 0.25, 0.0), m=m_wall)           # the wall plane behind
    for i, kind in enumerate(bays):
        cx = -w / 2.0 + span * (i + 0.5)
        bw = span - pier
        L.box(bm, (pier, 0.7, h), base_at=(-w / 2.0 + span * i, -l / 2.0 + 0.35, 0.0), m=m_wall)
        if kind == "shutter":
            L.box(bm, (bw, 0.12, h - 1.0), base_at=(cx, -l / 2.0 + 0.30, 0.0), m=SHUT)
        elif kind == "entry":
            L.box(bm, (bw, 0.10, h - 1.4), base_at=(cx, -l / 2.0 + 0.95, 0.0), m=GLASS)
            L.box(bm, (bw + 0.3, 1.3, 0.22), base_at=(cx, -l / 2.0 + 0.65, h - 1.4), m=CONC)
        else:
            L.box(bm, (bw, 0.10, h - 1.3), base_at=(cx, -l / 2.0 + 0.45, 0.35), m=GLASS)
            L.box(bm, (bw, 0.55, 0.35), base_at=(cx, -l / 2.0 + 0.30, 0.0), m=CONC)   # stallriser
        if lod == 0:
            L.box(bm, (bw + 0.2, 1.25, 0.14), loc=(cx, -l / 2.0 - 0.55, h - 0.75), m=METAL)   # awning
    L.box(bm, (w + 0.2, 0.9, 0.9), base_at=(0, -l / 2.0 - 0.2, h), m=m_wall)       # fascia over the bays
    L.box(bm, (w - 1.0, 0.16, 0.62), loc=(0, -l / 2.0 - 0.68, h + 0.45), m=NEON)   # BLANK fascia sign band
    if lod < 2:                                                                     # projecting blade signs
        for sx in (-1, 1):
            L.box(bm, (0.22, 1.5, 0.22), loc=(sx * w * 0.27, -l / 2.0 - 0.85, h - 1.9), m=METAL)
            L.box(bm, (0.14, 1.15, 1.75), loc=(sx * w * 0.27, -l / 2.0 - 1.25, h - 2.9), m=NEON)


def roof_kit(bm, w, l, z, m_wall, sign=True, lod=0):
    """Parapet, plant and a BLANK rooftop sign board - the Neon Mile silhouette."""
    L.parapet(bm, w, l, z, m_wall, h=1.1, t=0.45, m_cap=CONC)
    if lod < 2:
        L.box(bm, (w * 0.22, l * 0.26, 2.2), base_at=(-w * 0.2, l * 0.14, z), m=METAL)
        L.box(bm, (w * 0.16, l * 0.16, 1.4), base_at=(w * 0.22, -l * 0.12, z), m=CONC)
    if sign:
        for sx in (-1, 1):
            L.box(bm, (0.26, 0.26, 4.6), base_at=(sx * w * 0.30, -l / 2.0 + 1.2, z + 1.1), m=METAL)
        L.box(bm, (w * 0.70, 0.18, 3.4), loc=(0, -l / 2.0 + 1.2, z + 3.6), m=NEON)   # BLANK sign board


def brick_block(w, l, h, lod=0):
    bm = bmesh.new()
    gh = 4.6
    top = h - 5.7          # the rooftop sign board is part of the record's height, not extra on top
    street_front(bm, w, l, gh, WALL, lod=lod)
    L.box(bm, (w, l - 1.0, gh), base_at=(0, 0.5, 0.0), m=WALL)
    grid_facade(bm, w, l, gh, top, 3.6, 4.2, WALL, GLASS, lod=lod)
    roof_kit(bm, w, l, top, WALL, sign=True, lod=lod)
    return bm


def modern_block(w, l, h, lod=0):
    bm = bmesh.new()
    gh = 4.8
    top = h - 5.7          # the rooftop sign board is part of the record's height, not extra on top
    street_front(bm, w, l, gh, WALL, lod=lod,
                 bays=("glass", "glass", "entry", "glass", "shutter", "glass"))
    L.box(bm, (w, l - 1.0, gh), base_at=(0, 0.5, 0.0), m=WALL)
    grid_facade(bm, w, l, gh, top, 3.9, 6.0, WALL, GLASS, lod=lod)
    if lod < 2:                                            # horizontal brise-soleil over the upper band
        for k in range(1, max(2, int((top - gh) / 3.9))):
            L.box(bm, (w + 0.5, 0.35, 0.16), loc=(0, -l / 2.0 - 0.2, gh + 3.9 * k - 0.4), m=METAL)
    roof_kit(bm, w, l, top, WALL, sign=True, lod=lod)
    return bm


SPECS = [
    ("brick_30",  brick_block,  57.0,  57.0, 30.0, BRICK_SLOTS),
    ("brick_42",  brick_block,  57.0,  57.0, 42.0, BRICK_SLOTS),
    ("brick_62",  brick_block,  57.0,  57.0, 62.0, BRICK_SLOTS),
    ("brick_33L", brick_block,  56.0, 105.0, 33.0, BRICK_SLOTS),
    ("low_16",    modern_block, 58.0,  58.0, 16.0, PANEL_SLOTS),
    ("low_21",    modern_block, 58.0,  58.0, 21.0, PANEL_SLOTS),
    ("low_18L",   modern_block, 57.0, 112.0, 18.0, PANEL_SLOTS),
    # height-band gap fills from build/analyse_misfits.py
    ("brick_62L", brick_block,  56.0, 105.0, 62.0, BRICK_SLOTS),
    ("low_14L",   modern_block, 57.0, 112.0, 14.0, PANEL_SLOTS),
]
NAMES = {k: "VC_Neon_" + "".join(p.capitalize() for p in k.split("_")) for k, _, _, _, _, _ in SPECS}
STYLE = {"brick_block": ["BrickIndustrial"], "modern_block": ["ModernLowrise"]}


def reset():
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    for m in list(bpy.data.materials):
        bpy.data.materials.remove(m, do_unlink=True)
    for me in list(bpy.data.meshes):
        if me.users == 0:
            bpy.data.meshes.remove(me)
    vc_tools.vc_setup_scene()


def build(key):
    _, fn, w, l, h, slots = next(s for s in SPECS if s[0] == key)
    reset()
    lods = []
    uv = True
    for lod in (0, 1, 2):
        L.set_detail(lod)
        o = L.new_object("%s_LOD%d" % (NAMES[key], lod), fn(w, l, h, lod), slots)
        uv = L.unwrap(o, margin=0.006, cube=2.0) and uv
        lods.append(o)
    L.set_detail(0)
    obj = lods[0]
    pts = [obj.matrix_world @ Vector(c) for c in obj.bound_box]
    lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    rep0 = L.mesh_report(obj)
    parent = bpy.data.objects.new(NAMES[key], None)
    bpy.context.scene.collection.objects.link(parent)
    for o in lods:
        o.parent = parent
    col = L.collider_box("UBX_" + parent.name, (w, l, hi.z - lo.z), base_at=(0.0, 0.0, 0.0))
    col.parent = parent
    col.matrix_parent_inverse = parent.matrix_world.inverted()

    os.makedirs(EXPORT, exist_ok=True)
    fbx = os.path.join(EXPORT, parent.name + ".fbx")
    vc_tools.vc_export_fbx([parent], fbx, mode="prop", pivot=parent, path_mode="STRIP")
    blend = os.path.join(ROOT, parent.name + ".blend")
    bpy.ops.wm.save_as_mainfile(filepath=blend)
    return {
        "key": key, "name": parent.name, "set": "D6Neon", "family": fn.__name__,
        "style_kit": STYLE[fn.__name__], "districts": ["D6"],
        "blend": os.path.relpath(blend, r"E:\VeyraCity").replace("\\", "/"),
        "fbx": os.path.relpath(fbx, r"E:\VeyraCity").replace("\\", "/"),
        "footprint_m": [w, l], "height_m": round(hi.z - lo.z, 2), "design_height_m": h,
        "bbox_m": [round(hi.x - lo.x, 2), round(hi.y - lo.y, 2), round(hi.z - lo.z, 2)],
        "base_z": round(lo.z, 5), "uv": uv, "lod_mode": "authored", "lod_ratios": None,
        "lods": [L.mesh_report(o) for o in lods],
        "collision": [col.name], "slots": rep0["slots"],
        "emissive_slot": "VC_Neon_Emissive",
    }


def main():
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else ["all"]
    keys = [s[0] for s in SPECS] if args[0] == "all" else args
    out = [build(k) for k in keys]
    path = os.path.join(HERE, "report_neon.json")
    old = []
    if os.path.exists(path) and args[0] != "all":
        old = [r for r in json.load(open(path)) if r["key"] not in keys]
    with open(path, "w", encoding="utf-8") as fh:
        json.dump(old + out, fh, indent=1)
    print("VC_NEON_OK:" + path)


main()

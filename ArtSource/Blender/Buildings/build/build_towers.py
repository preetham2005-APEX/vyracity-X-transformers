"""VEYRA CITY - D1 Crown Downtown tower set (original designs).

    blender --factory-startup -b -noaudio -P build_towers.py -- <key|all>

Sized from the data, not invented. Grouping of the 232 D1 `GlassModern/ArtDeco` records in
Assets/_Veyra/Data/world_building_placements.csv:
  * footprints are continuous, not discrete: 181 are near-square (short side 52-57 m, median 54 x 56),
    51 are slabs (short side ~54, long side clustered at 115 m (25) and 80 m (16));
    a handful are superblocks (~117 x 117) or thin towers (~30 m short side).
  * heights run 23-260 m: 83 records under 50 m, 76 between 50 and 100, 73 above 100.
  * shape is `box` for 207 and `stepped` for 25 - the stepped ones are the tall end (123-228 m).
  * typology bands the heights: T7 ~27 m, T6 ~37, T5 ~56, T4 ~75, T2 ~73, T1 ~104, T3 ~184.

The 15 models below were chosen by simulating BuildingAssembler's own fit rule (footprint +-22 % after a
uniform 0.85-1.2 scale, height 0.65-1.5x, either orientation) over all 232 records: they fit 220 of them.
The 12 that still miss are extreme outliers (52 x 168, 116 x 118 x 62, 115 x 54 x 227 ...) and keep their
procedural shell.

Towers are seen from far across the city and BUG-170 is open on the CPU, so the shaft is cheap by design:
depth comes from full-height mullion fins, corner piers and setbacks - not from per-floor geometry - which
also makes the triangle count independent of the building's height.
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
SLOTS = ["VC_Twr_Glass", "VC_Twr_Spandrel", "VC_Twr_Stone", "VC_Twr_Concrete", "VC_Twr_Metal"]
GLASS, SPAN, STONE, CONC, METAL = 0, 1, 2, 3, 4


def fins(bm, w, l, z0, z1, m, pitch=4.5, depth=0.30, wide=0.18, lod=0):
    if lod >= 2:
        return                      # LOD2 is massing only: the fins are what a collapse decimate eats first
    if lod == 1:
        pitch, wide, depth = pitch * 2.0, wide * 1.6, depth * 1.1
    """Full-height vertical mullion fins on all four faces - the tower's vertical grain, and the only
    facade relief that costs the same whether the tower is 28 m or 240 m tall."""
    nx = max(2, int(w / pitch))
    ny = max(2, int(l / pitch))
    for i in range(nx + 1):
        x = -w / 2.0 + w * i / nx
        for sy in (-1, 1):
            L.box(bm, (wide, depth, z1 - z0), base_at=(x, sy * (l / 2.0 + depth / 2.0 - 0.02), z0), m=m)
    for j in range(ny + 1):
        y = -l / 2.0 + l * j / ny
        for sx in (-1, 1):
            L.box(bm, (depth, wide, z1 - z0), base_at=(sx * (w / 2.0 + depth / 2.0 - 0.02), y, z0), m=m)


def shaft(bm, w, l, z0, z1, pier=1.4):
    """Glazed shaft: recessed curtain wall between four solid corner piers."""
    L.box(bm, (w - 0.5, l - 0.5, z1 - z0), base_at=(0, 0, z0), m=GLASS)
    for sx in (-1, 1):
        for sy in (-1, 1):
            L.box(bm, (pier, pier, z1 - z0),
                  base_at=(sx * (w - pier) / 2.0, sy * (l - pier) / 2.0, z0), m=STONE)


def band(bm, w, l, z, h, m, out=0.22):
    L.box(bm, (w + out, l + out, h), base_at=(0, 0, z), m=m)


def podium(bm, w, l, h=8.0, m_pier=STONE, lod=0):
    """Street level: a colonnade of piers with the glazing set well back, plus an entrance canopy.
    This is the part a driver actually sees, so it gets real depth."""
    if lod >= 2:
        L.box(bm, (w, l, h - 0.6), base_at=(0, 0, 0.0), m=STONE)
        L.box(bm, (w + 0.5, l + 0.5, 0.6), base_at=(0, 0, h - 0.6), m=CONC)
        return
    L.box(bm, (w - 2.2, l - 2.2, h - 0.6), base_at=(0, 0, 0.0), m=GLASS)      # recessed shopfront line
    step = 1 if lod == 0 else 2
    nx = max(3, int(w / 6.0))
    ny = max(3, int(l / 6.0))
    for i in range(0, nx + 1, step):
        x = -w / 2.0 + w * i / nx
        for sy in (-1, 1):
            L.box(bm, (1.1 * step, 1.1, h - 0.6), base_at=(x, sy * (l - 1.1) / 2.0, 0.0), m=m_pier)
    for j in range(step, ny, step):
        y = -l / 2.0 + l * j / ny
        for sx in (-1, 1):
            L.box(bm, (1.1, 1.1 * step, h - 0.6), base_at=(sx * (w - 1.1) / 2.0, y, 0.0), m=m_pier)
    L.box(bm, (w + 0.5, l + 0.5, 0.6), base_at=(0, 0, h - 0.6), m=CONC)      # podium cornice
    if lod == 0:
        L.box(bm, (10.0, 2.6, 0.45), base_at=(0, -l / 2.0 - 1.1, 4.6), m=METAL)  # entrance canopy (front, -Y)


def crown(bm, w, l, z, plant=True, mast=0.0, cap=True, lod=0):
    if lod >= 2:
        plant = False
    """Parapet, mechanical penthouse and mast - the silhouette that carries the LOD2 at city distance."""
    L.parapet(bm, w, l, z, STONE, h=1.6, t=0.55, m_cap=CONC)
    top = z + 1.6
    if cap:
        L.box(bm, (w * 0.62, l * 0.62, 4.5), base_at=(0, 0, z), m=CONC)
        L.box(bm, (w * 0.66, l * 0.66, 0.5), base_at=(0, 0, z + 4.5), m=METAL)
        top = z + 5.0
    if plant:
        L.box(bm, (w * 0.20, l * 0.24, 2.6), base_at=(-w * 0.16, l * 0.12, top), m=METAL)
        L.box(bm, (w * 0.14, l * 0.14, 1.8), base_at=(w * 0.18, -l * 0.10, top), m=CONC)
        top += 2.6
    if mast > 0.0:
        L.cyl(bm, 0.45, 0.16, mast, 8, METAL, base_at=(0, 0, top))
        top += mast
    return top


# ------------------------------------------------------------------ families

def tower_glass(w, l, h, lod=0, pitch=4.5):
    """Modern curtain-wall tower: podium, glazed shaft with mullion fins, two mechanical bands, crown."""
    bm = bmesh.new()
    pod = 8.0
    mast = 6.0 if h > 100 else 0.0
    cr = 6.5 if h > 60 else 3.5
    top = h - cr
    podium(bm, w, l, pod, lod=lod)
    shaft(bm, w, l, pod, top)
    fins(bm, w, l, pod + 0.4, top - 0.4, METAL, pitch=pitch, lod=lod)
    band(bm, w, l, pod + (top - pod) * 0.45, 1.1, SPAN)
    if h > 80:
        band(bm, w, l, pod + (top - pod) * 0.78, 1.1, SPAN)
    crown(bm, w, l, top, plant=True, mast=mast, cap=h > 60, lod=lod)
    return bm


def tower_deco(w, l, h, lod=0):
    """Art-deco massing: three stacked volumes with setbacks, stone piers, stepped crown.
    These serve the `stepped` records, which are the tall end of the district."""
    bm = bmesh.new()
    pod = 9.0
    z1 = pod + (h - pod) * 0.46
    z2 = pod + (h - pod) * 0.76
    top = h - 9.0
    w1, l1 = w, l
    w2, l2 = w * 0.80, l * 0.80
    w3, l3 = w * 0.60, l * 0.60
    podium(bm, w1, l1, pod, lod=lod)
    shaft(bm, w1, l1, pod, z1, pier=1.8)
    fins(bm, w1, l1, pod + 0.4, z1, STONE, pitch=4.0, depth=0.42, wide=0.80, lod=lod)
    band(bm, w1, l1, z1, 1.4, CONC)
    shaft(bm, w2, l2, z1 + 1.4, z2, pier=1.6)
    fins(bm, w2, l2, z1 + 1.8, z2, STONE, pitch=4.0, depth=0.42, wide=0.72, lod=lod)
    band(bm, w2, l2, z2, 1.3, CONC)
    shaft(bm, w3, l3, z2 + 1.3, top, pier=1.4)
    fins(bm, w3, l3, z2 + 1.7, top, STONE, pitch=4.0, depth=0.38, wide=0.64, lod=lod)
    t = crown(bm, w3, l3, top, plant=False, cap=True, lod=lod)
    L.box(bm, (w3 * 0.34, l3 * 0.34, 3.2), base_at=(0, 0, t), m=STONE)
    L.cyl(bm, 0.5, 0.12, 5.5, 8, METAL, base_at=(0, 0, t + 3.2))
    return bm


def block_slab(w, l, h, lod=0):
    """Slab block / superblock: a long banded facade, podium at street level, roof plant.
    Serves the 51 slab records (long side ~80 m or ~115 m) and the ~117 x 117 superblocks."""
    bm = bmesh.new()
    pod = 7.5
    top = h - 3.0
    podium(bm, w, l, pod, lod=lod)
    shaft(bm, w, l, pod, top, pier=1.6)
    fins(bm, w, l, pod + 0.4, top - 0.4, SPAN, pitch=5.0, depth=0.26, wide=0.55, lod=lod)
    for f in (0.34, 0.68):
        band(bm, w, l, pod + (top - pod) * f, 1.0, CONC)
    crown(bm, w, l, top, plant=True, mast=0.0, cap=False, lod=lod)
    return bm


# ------------------------------------------------------------------ the 15 models

SPECS = [
    # key                family        w      l      h
    ("glass_28",         tower_glass,  55.0,  56.0,  28.0),
    ("glass_42",         tower_glass,  55.0,  56.0,  42.0),
    ("glass_62",         tower_glass,  55.0,  56.0,  62.0),
    ("glass_90",         tower_glass,  55.0,  56.0,  90.0),
    ("glass_130",        tower_glass,  55.0,  56.0, 130.0),
    ("glass_190",        tower_glass,  55.0,  56.0, 190.0),
    ("deco_120",         tower_deco,   54.0,  54.0, 120.0),
    ("deco_175",         tower_deco,   54.0,  54.0, 175.0),
    ("deco_240",         tower_deco,   54.0,  54.0, 240.0),
    ("slab115_50",       block_slab,   54.0, 115.0,  50.0),
    ("slab115_110",      block_slab,   54.0, 115.0, 110.0),
    ("slab80_70",        block_slab,   54.0,  80.0,  70.0),
    ("block117_100",     block_slab,  117.0, 117.0, 100.0),
    ("thin31_70",        tower_glass,  31.0,  60.0,  70.0),
    ("thin31_150",       tower_glass,  31.0, 117.0, 150.0),
    # height-band gap fills, from the misfit analysis (build/analyse_misfits.py): each of these closes
    # records whose FOOTPRINT already fitted an existing model but whose height fell outside 0.65-1.5x.
    ("thin31_34",        tower_glass,  31.0,  60.0,  34.0),
    ("thin31_130",       tower_glass,  31.0,  60.0, 130.0),
    ("thin31_80l",       tower_glass,  31.0, 117.0,  80.0),
    ("slab115_200",      block_slab,   54.0, 115.0, 200.0),
    ("slab80_170",       block_slab,   54.0,  80.0, 170.0),
    ("block117_62",      block_slab,  117.0, 117.0,  62.0),
]
NAMES = {k: "VC_Tower_" + "".join(p.capitalize() for p in k.split("_")) for k, _, _, _, _ in
         [(s[0], None, None, None, None) for s in SPECS]}


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
    spec = next(s for s in SPECS if s[0] == key)
    _, fn, w, l, h = spec
    reset()
    # LODs are AUTHORED, not decimated. A collapse decimate on a tower eats the massing first and leaves
    # the mullion fins behind - rendered backface-culled, the LOD2 was a see-through shell of slivers
    # (preview/lod2_backface_check_towers.png, before this change). Re-running the generator at a lower
    # detail level gives a LOD2 that is the silhouette, which is what carries most frames at city distance.
    lods = []
    uv = True
    for lod in (0, 1, 2):
        o = L.new_object("%s_LOD%d" % (NAMES[key], lod), fn(w, l, h, lod), SLOTS)
        uv = L.unwrap(o, margin=0.004, cube=4.0) and uv   # one texture tile = 4 m = one storey
        lods.append(o)
    obj = lods[0]
    pts = [obj.matrix_world @ Vector(c) for c in obj.bound_box]
    lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    rep0 = L.mesh_report(obj)
    r1 = r2 = None
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
        "key": key, "name": parent.name, "set": "D1Tower",
        "style_kit": ["GlassModern", "ArtDeco"], "districts": ["D1"],
        "family": fn.__name__,
        "blend": os.path.relpath(blend, r"E:\VeyraCity").replace("\\", "/"),
        "fbx": os.path.relpath(fbx, r"E:\VeyraCity").replace("\\", "/"),
        "footprint_m": [w, l], "height_m": round(hi.z - lo.z, 2), "design_height_m": h,
        "bbox_m": [round(hi.x - lo.x, 2), round(hi.y - lo.y, 2), round(hi.z - lo.z, 2)],
        "base_z": round(lo.z, 5), "uv": uv,
        "lod_mode": "authored",
        "lod_ratios": None,
        "lods": [L.mesh_report(o) for o in lods],
        "collision": [col.name], "slots": rep0["slots"],
    }


def main():
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else ["all"]
    keys = [s[0] for s in SPECS] if args[0] == "all" else args
    out = [build(k) for k in keys]
    path = os.path.join(HERE, "report_towers.json")
    old = []
    if os.path.exists(path) and args[0] != "all":
        old = [r for r in json.load(open(path)) if r["key"] not in keys]
    with open(path, "w", encoding="utf-8") as fh:
        json.dump(old + out, fh, indent=1)
    print("VC_TOWERS_OK:" + path)


main()

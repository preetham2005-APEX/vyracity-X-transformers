"""Build the whole terrain kit: rock assets + far-tree impostors, .blend per group, FBX per LOD set.

Run: blender -b --python build_terrain.py
"""
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import bpy
from mathutils import Vector

import impostors as I
import rocks as R
import terrain_lib as T

# name, group, builder(lod) -> Builder, materials key, collision spec, declared sink (m)
#   collision: ("ucx", verts_faces) | ("ucx_multi", [vf, ...]) | ("col", verts_faces, col_name) | None
ROCKS = [
    ("VC_Rock_Boulder_S_Angular", "Boulder", lambda l: R.boulder("S", "Angular", l),
     ("ucx", lambda: R.boulder_collider("S", "Angular"))),
    ("VC_Rock_Boulder_S_Rounded", "Boulder", lambda l: R.boulder("S", "Rounded", l),
     ("ucx", lambda: R.boulder_collider("S", "Rounded"))),
    ("VC_Rock_Boulder_M_Angular", "Boulder", lambda l: R.boulder("M", "Angular", l),
     ("ucx", lambda: R.boulder_collider("M", "Angular"))),
    ("VC_Rock_Boulder_M_Rounded", "Boulder", lambda l: R.boulder("M", "Rounded", l),
     ("ucx", lambda: R.boulder_collider("M", "Rounded"))),
    ("VC_Rock_Boulder_L_Angular", "Boulder", lambda l: R.boulder("L", "Angular", l),
     ("ucx", lambda: R.boulder_collider("L", "Angular"))),
    ("VC_Rock_Boulder_L_Rounded", "Boulder", lambda l: R.boulder("L", "Rounded", l),
     ("ucx", lambda: R.boulder_collider("L", "Rounded"))),
    ("VC_Rock_Outcrop_A", "Outcrop", lambda l: R.outcrop("A", l),
     ("ucx_multi", lambda: R.outcrop_colliders("A"))),
    ("VC_Rock_Outcrop_B", "Outcrop", lambda l: R.outcrop("B", l),
     ("ucx_multi", lambda: R.outcrop_colliders("B"))),
    ("VC_Rock_CutFace_8m", "CutFace", R.cut_face, ("col", R.cut_face_collider, "COL_concrete_")),
    ("VC_Rock_ScreePatch", "Scree", R.scree, ("col", R.scree_collider, "COL_gravel_")),
    ("VC_Rock_ShoreGroup", "Shore", lambda l: R.shore_group(l),
     ("ucx_multi", lambda: R.shore_colliders())),
    ("VC_Log_Fallen_A", "Log", lambda l: R.fallen_log("A", l), ("ucx", lambda: R.log_collider("A"))),
    ("VC_Log_Fallen_B", "Log", lambda l: R.fallen_log("B", l), ("ucx", lambda: R.log_collider("B"))),
]

BARK_ONLY = {"VC_Log_Fallen_A", "VC_Log_Fallen_B"}


def bbox(objs):
    lo = Vector((1e9, 1e9, 1e9))
    hi = Vector((-1e9, -1e9, -1e9))
    for ob in objs:
        if ob.type != "MESH":
            continue
        for c in ob.bound_box:
            w = ob.matrix_world @ Vector(c)
            lo = Vector((min(lo.x, w.x), min(lo.y, w.y), min(lo.z, w.z)))
            hi = Vector((max(hi.x, w.x), max(hi.y, w.y), max(hi.z, w.z)))
    return lo, hi


def add_collision(name, spec):
    if spec is None:
        return []
    kind = spec[0]
    if kind == "ucx":
        v, f = spec[1]()
        return [T.collider_object("UCX_" + name, v, f)]
    if kind == "ucx_multi":
        out = []
        for i, (v, f) in enumerate(spec[1](), start=1):
            out.append(T.collider_object("UCX_%s_%02d" % (name, i), v, f))
        return out
    if kind == "col":
        v, f = spec[1]()
        return [T.collider_object(spec[2] + name, v, f)]
    raise ValueError(kind)


def build_group(items, blend_name, materials_for, stats):
    T.fresh_scene()
    offset = 0.0
    for name, fn, coll in items:
        mats = materials_for(name)
        lods = [fn(l).to_object("%s_LOD%d" % (name, l), mats) for l in range(3)]
        colliders = add_collision(name, coll)
        parent = T.assemble(name, lods, None)
        for c in colliders:
            c.parent = parent
        rep = T.apply_and_check(lods + colliders)
        lo, hi = bbox(lods)
        stats[name] = {
            "group": blend_name,
            "lods": {r["object"][-4:]: r for r in rep if "_LOD" in r["object"]},
            "collision": [c.name for c in colliders],
            "collision_tris": sum(len(p.vertices) - 2 for c in colliders for p in c.data.polygons),
            "size_m": [round(hi.x - lo.x, 3), round(hi.y - lo.y, 3), round(hi.z - lo.z, 3)],
            "min_z": round(lo.z, 4),
            "xy_centre": [round((lo.x + hi.x) / 2, 3), round((lo.y + hi.y) / 2, 3)],
            "materials": [m.name for m in mats],
            "fbx": name + ".fbx",
        }
        T.export(parent, name + ".fbx")
        parent.location = (offset, 0, 0)
        offset += max(3.0, (hi.x - lo.x) + 2.0)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(T.ROOT, "VC_Terrain_%s.blend" % blend_name))
    print("saved blend:", blend_name)


def main():
    stats = {}

    # ---- rocks and logs
    groups = {}
    for name, grp, fn, coll in ROCKS:
        groups.setdefault(grp, []).append((name, fn, coll))

    def rock_mats(name):
        rock, bark = T.rock_materials()
        return [bark] if name in BARK_ONLY else [rock]

    for grp, items in groups.items():
        build_group(items, grp, rock_mats, stats)

    # ---- impostors
    imp_items = [("VC_Impostor_" + s, (lambda s: (lambda l: I.impostor(s, l)))(s), None)
                 for s in I.SPECIES]
    build_group(imp_items, "Impostor", lambda n: T.impostor_material(), stats)
    for s in I.SPECIES:
        stats["VC_Impostor_" + s]["impostor"] = I.recommend(s)

    with open(os.path.join(T.ROOT, "scripts", "build_stats.json"), "w") as f:
        json.dump(stats, f, indent=1)
    print("BUILD TERRAIN OK", len(stats), "assets")


main()

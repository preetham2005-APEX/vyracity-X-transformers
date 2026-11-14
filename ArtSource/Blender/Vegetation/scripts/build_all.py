"""Build every Veyra City vegetation asset: .blend per species, FBX per LOD set, stats json.

Run:  blender -b --python build_all.py
"""
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import bpy
from mathutils import Vector

import species as S
import veg_lib as V

BLENDS = os.path.join(V.ROOT)

# name, species-blend, builder(lod)->Builder, collider spec
ASSETS = [
    ("VC_Palm_Royal_8m", "Palm_Royal", lambda l: S.royal_palm(8.0, l), ("ucx", S.royal_palm_collider(8.0))),
    ("VC_Palm_Royal_12m", "Palm_Royal", lambda l: S.royal_palm(12.0, l), ("ucx", S.royal_palm_collider(12.0))),
    ("VC_Palm_Royal_16m", "Palm_Royal", lambda l: S.royal_palm(16.0, l), ("ucx", S.royal_palm_collider(16.0))),
    ("VC_Palm_Fan_Short", "Palm_Fan", lambda l: S.fan_palm(4.5, l), ("ucx", S.fan_palm_collider(4.5))),
    ("VC_Palm_Fan_Tall", "Palm_Fan", lambda l: S.fan_palm(6.5, l), ("ucx", S.fan_palm_collider(6.5))),
    ("VC_Tree_Broadleaf_Spreading", "Broadleaf", lambda l: S.broadleaf("Spreading", l),
     ("ucx", S.broadleaf_collider("Spreading"))),
    ("VC_Tree_Broadleaf_Upright", "Broadleaf", lambda l: S.broadleaf("Upright", l),
     ("ucx", S.broadleaf_collider("Upright"))),
    ("VC_Tree_Cypress_6m", "Cypress", lambda l: S.cypress(6.0, l), ("ucx", S.cypress_collider(6.0))),
    ("VC_Tree_Cypress_9m", "Cypress", lambda l: S.cypress(9.0, l), ("ucx", S.cypress_collider(9.0))),
    ("VC_Tree_Pine_Tall", "Pine", lambda l: S.pine("Tall", l), ("ucx", S.pine_collider("Tall"))),
    ("VC_Tree_Pine_Umbrella", "Pine", lambda l: S.pine("Umbrella", l), ("ucx", S.pine_collider("Umbrella"))),
    ("VC_Hedge_Box_2m", "Hedge", lambda l: S.hedge("Straight_2m", l), ("ubx", ((2.0, 0.8, 1.2), (0, 0, 0.6)))),
    ("VC_Hedge_Box_Corner", "Hedge", lambda l: S.hedge("Corner", l), ("ubx", ((1.2, 1.2, 1.2), (0, 0, 0.6)))),
    ("VC_Hedge_Box_2m_Tall", "Hedge", lambda l: S.hedge("Straight_2m_Tall", l),
     ("ubx", ((2.0, 0.9, 2.5), (0, 0, 1.25)))),
    ("VC_Shrub_DryScrub", "Shrub", lambda l: S.shrub("Dry_Scrub", l), None),
    ("VC_Shrub_GreenBush", "Shrub", lambda l: S.shrub("Green_Bush", l), None),
    ("VC_Shrub_VergeTuft", "Shrub", lambda l: S.shrub("Verge_Tuft", l), None),
]


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


def main():
    stats = {}
    by_blend = {}
    for name, blend, fn, coll in ASSETS:
        by_blend.setdefault(blend, []).append((name, fn, coll))

    for blend, items in by_blend.items():
        mats = V.fresh_scene()
        offset = 0.0
        for name, fn, coll in items:
            lods = []
            for lod in range(3):
                b = fn(lod)
                lods.append(b.to_object("%s_LOD%d" % (name, lod), mats))
            collider = None
            if coll:
                if coll[0] == "ucx":
                    collider = V.ucx_trunk(name, coll[1][0], coll[1][1])
                else:
                    collider = V.ubx_box(name, coll[1][0], coll[1][1])
            parent = V.assemble(name, lods, collider)
            rep = V.apply_and_check(lods + ([collider] if collider else []))
            lo, hi = bbox(lods)
            stats[name] = {
                "blend": blend,
                "lods": {r["object"][-4:]: r for r in rep if "_LOD" in r["object"]},
                "collision": collider.name if collider else None,
                "collision_tris": (sum(len(p.vertices) - 2 for p in collider.data.polygons)
                                   if collider else 0),
                "size_m": [round(hi.x - lo.x, 3), round(hi.y - lo.y, 3), round(hi.z - lo.z, 3)],
                "base_centre_offset": [round((hi.x + lo.x) / 2, 4), round((hi.y + lo.y) / 2, 4),
                                       round(lo.z, 4)],
                "fbx": name + ".fbx",
            }
            V.export(parent, name + ".fbx")
            parent.location = (offset, 0, 0)
            offset += max(4.0, (hi.x - lo.x) + 2.5)
        bpy.ops.wm.save_as_mainfile(filepath=os.path.join(BLENDS, "VC_Veg_%s.blend" % blend))
        print("saved blend:", blend)

    with open(os.path.join(V.ROOT, "scripts", "build_stats.json"), "w") as f:
        json.dump(stats, f, indent=1)
    print("BUILD OK", len(stats), "assets")


main()

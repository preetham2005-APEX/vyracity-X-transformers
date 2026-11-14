"""Build every Vegetation2 asset: .blend per species group, FBX per LOD set, stats json."""
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import bpy
from mathutils import Vector

import species2 as S
import veg_lib2 as V

ASSETS = [
    # D4 Blackridge Mountains
    ("VC_Tree_Spruce_Alpine", "Alpine", lambda l: S.alpine_conifer("Spruce", l),
     ("ucx", S.conifer_collider("Spruce"))),
    ("VC_Tree_Fir_Alpine", "Alpine", lambda l: S.alpine_conifer("Fir", l),
     ("ucx", S.conifer_collider("Fir"))),
    ("VC_Tree_Snag_Weathered", "Alpine", S.snag, ("ucx", S.snag_collider())),
    ("VC_Shrub_MountainScrub", "Alpine", lambda l: S.scrub("MountainScrub", l), None),
    ("VC_Shrub_RockLedgeTuft", "Alpine", lambda l: S.scrub("RockLedgeTuft", l), None),
    # D8 Redwood Valley
    ("VC_Tree_Redwood_22m", "Redwood", lambda l: S.redwood(22.0, l), ("ucx", S.redwood_collider(22.0))),
    ("VC_Tree_Redwood_34m", "Redwood", lambda l: S.redwood(34.0, l), ("ucx", S.redwood_collider(34.0))),
    ("VC_Tree_Redwood_46m", "Redwood", lambda l: S.redwood(46.0, l), ("ucx", S.redwood_collider(46.0))),
    # D3 Ironport industrial neglect
    ("VC_Weed_ConcreteCrack", "Industrial", lambda l: S.scrub("ConcreteCrackWeed", l), None),
    ("VC_Weed_FenceLine", "Industrial", lambda l: S.scrub("FenceLineWeeds", l), None),
    ("VC_Tree_VolunteerSapling", "Industrial", S.sapling, ("ucx", S.sapling_collider())),
    ("VC_Grass_DeadClump", "Industrial", lambda l: S.scrub("DeadGrassClump", l), None),
    # Ground cover, all districts
    ("VC_Ground_GrassTuft_S", "Ground", lambda l: S.ground("GrassTuft_S", l), None),
    ("VC_Ground_GrassPatch_M", "Ground", lambda l: S.ground("GrassPatch_M", l), None),
    ("VC_Ground_WeedPatch_M", "Ground", lambda l: S.ground("WeedPatch_M", l), None),
    ("VC_Ground_DryPatch_L", "Ground", lambda l: S.ground("DryPatch_L", l), None),
]

# the snag never uses the foliage atlas, so it ships with one material slot
BARK_ONLY = {"VC_Tree_Snag_Weathered"}


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
            use = [mats[0]] if name in BARK_ONLY else mats
            lods = []
            for lod in range(3):
                b = fn(lod)
                lods.append(b.to_object("%s_LOD%d" % (name, lod), use))
            collider = None
            if coll:
                collider = (V.ucx_trunk(name, coll[1][0], coll[1][1]) if coll[0] == "ucx"
                            else V.ubx_box(name, coll[1][0], coll[1][1]))
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
            offset += max(4.0, (hi.x - lo.x) + 3.0)
        bpy.ops.wm.save_as_mainfile(filepath=os.path.join(V.ROOT, "VC_Veg2_%s.blend" % blend))
        print("saved blend:", blend)

    with open(os.path.join(V.ROOT, "scripts", "build_stats.json"), "w") as f:
        json.dump(stats, f, indent=1)
    print("BUILD2 OK", len(stats), "assets")


main()

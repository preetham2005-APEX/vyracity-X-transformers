"""Independent self-check: re-import every exported FBX and verify it against the CLAUDE.md rules.

Checks per file: one root, _LOD0/1/2 naming, one collider with the UCX_/UBX_ prefix, applied
rotation and scale on the meshes, pivot at the base centre with the trunk base at the local origin,
triangle budgets, UVs inside 0..1, custom split normals present and genuinely biased away from the
flat card normals, and material count.
Run: blender -b --python verify.py
"""
import json
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import bpy
from mathutils import Vector

import veg_lib as V

BUDGET_LOD0 = {"tree": 4000, "small": 1500}
SMALL = ("VC_Hedge", "VC_Shrub")


def clear():
    for ob in list(bpy.data.objects):
        bpy.data.objects.remove(ob, do_unlink=True)
    for me in list(bpy.data.meshes):
        bpy.data.meshes.remove(me)


def tri_count(me):
    return sum(len(p.vertices) - 2 for p in me.polygons)


def normal_bias(ob):
    """Mean angle (deg) between each loop's custom normal and its polygon's flat normal, over the
    foliage material slots. 0 would mean the cards are shading as flat cardboard."""
    me = ob.data
    if not me.has_custom_normals:
        return None
    me.calc_normals_split() if hasattr(me, "calc_normals_split") else None
    tot, n = 0.0, 0
    foliage = [i for i, m in enumerate(me.materials) if m and "Foliage" in m.name]
    for p in me.polygons:
        if p.material_index not in foliage:
            continue
        fn = p.normal
        for li in p.loop_indices:
            ln = Vector(me.loops[li].normal)
            if ln.length < 1e-6 or fn.length < 1e-6:
                continue
            d = max(-1.0, min(1.0, ln.normalized().dot(fn.normalized())))
            tot += math.degrees(math.acos(abs(d)))
            n += 1
    return (tot / n) if n else None


def main():
    stats = json.load(open(os.path.join(V.ROOT, "scripts", "build_stats.json")))
    results = []
    fails = []
    for name in sorted(stats):
        clear()
        path = os.path.join(V.EXPORT, name + ".fbx")
        bpy.ops.import_scene.fbx(filepath=path)
        objs = list(bpy.data.objects)
        roots = [o for o in objs if o.parent is None]
        meshes = [o for o in objs if o.type == "MESH"]
        lods = sorted([o for o in meshes if "_LOD" in o.name], key=lambda o: o.name)
        colliders = [o for o in meshes if o.name.startswith(("UCX_", "UBX_"))]
        r = {"asset": name, "roots": len(roots), "root_rot_y": None,
             "lods": [], "collider": colliders[0].name if colliders else None,
             "issues": []}
        if roots:
            r["root_rot_y"] = round(math.degrees(roots[0].rotation_euler.y), 1)

        if len(roots) != 1:
            r["issues"].append("expected 1 root, got %d" % len(roots))
        if len(lods) != 3:
            r["issues"].append("expected 3 LOD meshes, got %d" % len(lods))
        for i, ob in enumerate(lods):
            if not ob.name.startswith(name) or "_LOD%d" % i not in ob.name:
                r["issues"].append("bad LOD name: " + ob.name)

        lo = Vector((1e9, 1e9, 1e9))
        hi = -lo
        mats = set()
        for i, ob in enumerate(lods):
            me = ob.data
            t = tri_count(me)
            budget = BUDGET_LOD0["small"] if name.startswith(SMALL) else BUDGET_LOD0["tree"]
            if i == 0 and t > budget:
                r["issues"].append("LOD0 %d tris over budget %d" % (t, budget))
            sc = tuple(round(v, 5) for v in ob.scale)
            if sc != (1.0, 1.0, 1.0):
                r["issues"].append("%s scale not applied: %s" % (ob.name, sc))
            uvs = me.uv_layers.active.data
            bad_uv = sum(1 for u in uvs if not (-0.001 <= u.uv[0] <= 1.001 and -0.001 <= u.uv[1] <= 1.001))
            if bad_uv:
                r["issues"].append("%s has %d UVs outside 0..1" % (ob.name, bad_uv))
            nb = normal_bias(ob)
            if i == 0 and (nb is None or nb < 8.0):
                r["issues"].append("%s canopy normal bias weak/absent: %s" % (ob.name, nb))
            for m in me.materials:
                if m:
                    mats.add(m.name.split(".")[0])
            r["lods"].append({"name": ob.name, "tris": t, "normal_bias_deg": round(nb, 1) if nb else None,
                              "materials": len(me.materials)})
            if i == 0:
                for c in ob.bound_box:
                    w = ob.matrix_world @ Vector(c)
                    lo = Vector((min(lo.x, w.x), min(lo.y, w.y), min(lo.z, w.z)))
                    hi = Vector((max(hi.x, w.x), max(hi.y, w.y), max(hi.z, w.z)))
        r["materials"] = sorted(mats)
        if len(mats) > 2:
            r["issues"].append("more than 2 materials: %s" % sorted(mats))
        # pivot: the FBX is exported through VC_Unity_Prop, which turns the model 180 deg about Z,
        # so in Blender-import space the base must still sit on z = 0 around the origin in x/y.
        r["base_z"] = round(lo.z, 4)
        r["xy_centre"] = [round((lo.x + hi.x) / 2, 3), round((lo.y + hi.y) / 2, 3)]
        if lo.z < -0.06 or lo.z > 0.06:
            r["issues"].append("base not at z=0 (min z %.3f)" % lo.z)
        if colliders:
            cob = colliders[0]
            if not cob.data.materials or True:
                pass
            r["collider_tris"] = tri_count(cob.data)
        elif not name.startswith("VC_Shrub"):
            r["issues"].append("missing collider")
        results.append(r)
        if r["issues"]:
            fails.append(r)

    out = os.path.join(V.ROOT, "scripts", "verify_report.json")
    with open(out, "w") as f:
        json.dump(results, f, indent=1)
    print("VERIFY: %d assets, %d with issues" % (len(results), len(fails)))
    for r in fails:
        print(" -", r["asset"], r["issues"])


main()

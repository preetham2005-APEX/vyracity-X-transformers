"""Scripted self-check for the terrain kit: re-imports every exported FBX and verifies it.

Checks: one root, LOD0/1/2 naming, applied rotation and scale, material count, collision prefix and
kind, UV sanity (rocks tile freely, impostors must stay inside their atlas rect), the CLAUDE.md
physics rule that a drive-over COL_ surface meets the ground with no step over 0.05 m, and that the
8 m road-cut module is periodic so two modules butt without a seam.

Run: blender -b --python verify_terrain.py
"""
import json
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import bpy
from mathutils import Vector

import terrain_lib as T

# asset name prefix -> (max LOD0 tris, expected collision kind, max sink below z=0 in metres)
# prefix -> (max LOD0 tris, collision kind, max sink below z=0 m, pivot tolerance m, pivot axes)
EXPECT = [
    ("VC_Rock_Boulder_S", 160, "ucx", 0.12, 0.10, "xy", None),
    ("VC_Rock_Boulder_M", 300, "ucx", 0.22, 0.22, "xy", None),
    ("VC_Rock_Boulder_L", 520, "ucx", 0.40, 0.40, "xy", None),
    ("VC_Rock_Outcrop", 2400, "ucx", 1.40, 0.90, "xy", None),
    # the cut face is a tiling wall: its pivot is the module x-centre on the toe line, so only x
    # is constrained - y deliberately sits at the front face, not the bounding-box centre
    ("VC_Rock_CutFace", 1200, "col", 0.02, 0.10, "x", "wall"),
    ("VC_Rock_ScreePatch", 1400, "col", 0.06, 0.20, "xy", "ground"),
    ("VC_Rock_ShoreGroup", 1400, "ucx", 0.70, 0.70, "xy", None),
    ("VC_Log_Fallen", 600, "ucx", 0.02, 0.35, "xy", None),
    # an impostor card carries the bake's 1.02 framing pad so the tree inside registers at its
    # true size, which means the card legitimately overhangs ~1 % of its height below z=0.
    # Those pixels are transparent; the tree's own base is still exactly on the ground.
    ("VC_Impostor_", 8, "none", 0.60, 0.05, "xy", None),
]
STEP_LIMIT = 0.05        # CLAUDE.md: no vertical step over 0.05 m between adjacent collider surfaces


def expect_for(name):
    for e in EXPECT:
        if name.startswith(e[0]):
            return e[1:]
    return None


def clear():
    for ob in list(bpy.data.objects):
        bpy.data.objects.remove(ob, do_unlink=True)
    for me in list(bpy.data.meshes):
        bpy.data.meshes.remove(me)


def tri_count(me):
    return sum(len(p.vertices) - 2 for p in me.polygons)


def boundary_z(ob, mode="ground"):
    """Height of the collider's open boundary against the terrain.

    mode "ground": max |z| over every boundary vertex - a drive-over surface must meet the ground
    all the way round, so any boundary vertex off z=0 is a step a wheel can hit.
    mode "wall": min |z| over boundary vertices - a wall's boundary legitimately includes its top
    edge; what matters is that its FOOT reaches the ground."""
    me = ob.data
    mw = ob.matrix_world
    count = {}
    for p in me.polygons:
        vs = list(p.vertices)
        for i in range(len(vs)):
            e = tuple(sorted((vs[i], vs[(i + 1) % len(vs)])))
            count[e] = count.get(e, 0) + 1
    zs = [abs((mw @ me.vertices[vi].co).z)
          for e, c in count.items() if c == 1 for vi in e]
    if not zs:
        return 0.0
    return max(zs) if mode == "ground" else min(zs)


def periodic_x(ob, length, tol=1e-3):
    """For a tiling module: the vertex cross-sections at x = -L/2 and x = +L/2 must match, or two
    modules butted together show a seam and a collision step."""
    me = ob.data
    mw = ob.matrix_world

    def key(c):
        return (round(c.y, 3), round(c.z, 3))      # 1 mm: immune to the float32 round-trip

    pts = [mw @ v.co for v in me.vertices]
    lo = {key(c) for c in pts if abs(c.x + length * 0.5) < tol}
    hi = {key(c) for c in pts if abs(c.x - length * 0.5) < tol}
    if not lo or not hi:
        return None
    return lo == hi, len(lo), len(hi)


def main():
    stats = json.load(open(os.path.join(T.ROOT, "scripts", "build_stats.json")))
    results, fails = [], []
    for name in sorted(stats):
        clear()
        bpy.ops.import_scene.fbx(filepath=os.path.join(T.EXPORT, name + ".fbx"))
        objs = list(bpy.data.objects)
        roots = [o for o in objs if o.parent is None]
        meshes = [o for o in objs if o.type == "MESH"]
        lods = sorted([o for o in meshes if "_LOD" in o.name], key=lambda o: o.name)
        ucx = [o for o in meshes if o.name.startswith("UCX_")]
        col = [o for o in meshes if o.name.startswith("COL_")]
        exp = expect_for(name)
        r = {"asset": name, "lods": [], "ucx": [o.name for o in ucx],
             "col": [o.name for o in col], "issues": []}
        if exp is None:
            r["issues"].append("no expectation entry for this asset")
            results.append(r)
            fails.append(r)
            continue
        max_tris, want_coll, max_sink, piv_tol, piv_axes, col_mode = exp

        if len(roots) != 1:
            r["issues"].append("expected 1 root, got %d" % len(roots))
        r["root_rot_z"] = round(math.degrees(roots[0].rotation_euler.z), 1) if roots else None
        if len(lods) != 3:
            r["issues"].append("expected 3 LOD meshes, got %d" % len(lods))
        for i, ob in enumerate(lods):
            if "_LOD%d" % i not in ob.name:
                r["issues"].append("bad LOD name: " + ob.name)

        mats = set()
        lo = Vector((1e9, 1e9, 1e9))
        hi = -lo
        for i, ob in enumerate(lods):
            me = ob.data
            t = tri_count(me)
            if i == 0 and t > max_tris:
                r["issues"].append("LOD0 %d tris over the kit budget %d" % (t, max_tris))
            if tuple(round(x, 5) for x in ob.scale) != (1.0, 1.0, 1.0):
                r["issues"].append("%s scale not applied" % ob.name)
            if tuple(round(math.degrees(a), 3) for a in ob.rotation_euler) != (0.0, 0.0, 0.0):
                r["issues"].append("%s rotation not applied" % ob.name)
            uvl = me.uv_layers.active
            if uvl is None:
                r["issues"].append("%s has no UV layer" % ob.name)
            else:
                umax = max(max(abs(u.uv[0]), abs(u.uv[1])) for u in uvl.data)
                if name.startswith("VC_Impostor_"):
                    if umax > 1.001:
                        r["issues"].append("%s impostor UVs outside the atlas (max %.3f)"
                                           % (ob.name, umax))
                elif umax > 200.0:
                    r["issues"].append("%s UV projection ran away (max %.1f)" % (ob.name, umax))
            for m in me.materials:
                if m:
                    mats.add(m.name.split(".")[0])
            r["lods"].append({"name": ob.name, "tris": t, "materials": len(me.materials)})
            if i == 0:
                for c in ob.bound_box:
                    w = ob.matrix_world @ Vector(c)
                    lo = Vector((min(lo.x, w.x), min(lo.y, w.y), min(lo.z, w.z)))
                    hi = Vector((max(hi.x, w.x), max(hi.y, w.y), max(hi.z, w.z)))
        r["materials"] = sorted(mats)
        if len(mats) != 1:
            r["issues"].append("expected exactly 1 material, got %s" % sorted(mats))
        r["min_z"] = round(lo.z, 4)
        if lo.z < -max_sink - 1e-3:
            r["issues"].append("sinks %.3f m below z=0, declared max %.2f" % (-lo.z, max_sink))
        if lo.z > 0.06:
            r["issues"].append("floats %.3f m above z=0" % lo.z)
        cxy = ((lo.x + hi.x) / 2, (lo.y + hi.y) / 2)
        r["pivot_offset"] = [round(cxy[0], 3), round(cxy[1], 3)]
        off = max(abs(cxy[0]), abs(cxy[1])) if piv_axes == "xy" else abs(cxy[0])
        if off > piv_tol:
            r["issues"].append("pivot off the base centre by %.2f m (tolerance %.2f on '%s')"
                               % (off, piv_tol, piv_axes))

        # ---- collision
        if want_coll == "ucx":
            if not ucx:
                r["issues"].append("missing UCX_ collider")
            if col:
                r["issues"].append("unexpected COL_ collider on a hit-able asset")
        elif want_coll == "col":
            if not col:
                r["issues"].append("missing COL_ collider")
            if ucx:
                r["issues"].append("unexpected UCX_ convex hull on a drive-over asset")
            for c in col:
                bz = boundary_z(c, col_mode or "ground")
                r["col_boundary_m"] = round(bz, 4)
                r["col_mode"] = col_mode
                if bz > STEP_LIMIT:
                    r["issues"].append(
                        "COL_ %s sits %.3f m off the ground (limit %.2f)"
                        % ("rim" if col_mode != "wall" else "foot", bz, STEP_LIMIT))
        else:
            if ucx or col:
                r["issues"].append("impostors should carry no collision (the real tree keeps its own)")
        r["collision_tris"] = sum(tri_count(o.data) for o in ucx + col)

        # ---- tiling module periodicity
        if name.startswith("VC_Rock_CutFace"):
            for ob in lods + col:
                res = periodic_x(ob, 8.0)
                if res is None:
                    r["issues"].append("%s has no vertices on the module boundary" % ob.name)
                elif not res[0]:
                    r["issues"].append("%s is not periodic across the 8 m module boundary" % ob.name)

        results.append(r)
        if r["issues"]:
            fails.append(r)

    with open(os.path.join(T.ROOT, "scripts", "verify_report.json"), "w") as f:
        json.dump(results, f, indent=1)
    print("VERIFY TERRAIN: %d assets, %d with issues" % (len(results), len(fails)))
    for r in fails:
        print(" -", r["asset"], r["issues"])


main()

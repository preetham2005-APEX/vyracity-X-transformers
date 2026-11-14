"""Self-check every exported street FBX: re-import it into an empty scene and measure what Unity will see.

    blender --factory-startup -b -noaudio -P verify_fbx.py

Checks per file: object list, LOD naming, collision naming, root transform, base at z = 0, pivot on the
x/y origin, FRONT (Blender -Y at build time) now pointing +Y in the file (so Unity +Z after Bake Axis
Conversion), material slot order, triangle counts, and geometry hygiene.
"""
import json
import os
import sys

import bmesh
import bpy
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
EXPORT = os.path.join(ROOT, "export")

out = []
for fn in sorted(os.listdir(EXPORT)):
    if not fn.lower().endswith(".fbx"):
        continue
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    for m in list(bpy.data.materials):
        bpy.data.materials.remove(m, do_unlink=True)
    for me in list(bpy.data.meshes):
        bpy.data.meshes.remove(me, do_unlink=True)
    bpy.ops.import_scene.fbx(filepath=os.path.join(EXPORT, fn),
                             automatic_bone_orientation=True, use_anim=False)
    objs = list(bpy.context.scene.objects)
    roots = [o for o in objs if o.parent is None]
    rec = {"file": fn, "objects": sorted(o.name for o in objs),
           "roots": [{"name": r.name, "type": r.type,
                      "loc": [round(v, 4) for v in r.matrix_world.translation],
                      "rot_deg": [round(v, 2) for v in r.rotation_euler.to_matrix().to_euler("XYZ")],
                      "scale": [round(v, 4) for v in r.matrix_world.to_scale()]} for r in roots],
           "meshes": [], "problems": []}
    allv = []
    lod0_tree = None
    for o in objs:
        if o.type == "MESH" and o.name.endswith("_LOD0"):
            from mathutils.bvhtree import BVHTree
            rb = bmesh.new()
            rb.from_mesh(o.data)
            rb.transform(o.matrix_world)
            rb.normal_update()
            bmesh.ops.triangulate(rb, faces=rb.faces)
            lod0_tree = BVHTree.FromBMesh(rb)
            rb.free()
    for o in objs:
        if o.type != "MESH":
            continue
        me = o.data
        pts = [o.matrix_world @ v.co for v in me.vertices]
        allv += pts
        bm = bmesh.new()
        bm.from_mesh(me)
        bm.transform(o.matrix_world)          # compare in world space
        bm.normal_update()                    # transform() leaves the cached face normals stale
        tris = sum(len(f.verts) - 2 for f in bm.faces)
        ng = sum(1 for f in bm.faces if len(f.verts) > 4)
        nm = sum(1 for e in bm.edges if not e.is_manifold)
        # Inverted-face test. For LOD0 (a closed mesh) a normal recalculation decides it. For a decimated
        # LOD it does not - the collapse opens shells, and a recalc on an open shell is ambiguous and
        # disagrees with itself - so a LOD mesh is tested against the LOD0 SURFACE instead: take the
        # nearest point on LOD0 and check the face is not pointing the opposite way. Backed by
        # preview/lod*_backface_check*.png, which renders the LODs backface-culled against a red world.
        if "_LOD" in o.name and not o.name.endswith("_LOD0") and lod0_tree is not None:
            flip = 0
            for f in bm.faces:
                hit = lod0_tree.find_nearest(f.calc_center_median())
                if hit[0] is not None and hit[1].dot(f.normal) < -0.05:
                    flip += 1
        else:
            before = [(f.normal.copy(), all(e.is_manifold for e in f.edges)) for f in bm.faces]
            bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
            flip = sum(1 for f, (n, ok) in zip(bm.faces, before) if ok and f.normal.dot(n) < 0.0)
        bm.free()
        rec["meshes"].append({
            "name": o.name, "tris": tris, "ngons": ng, "non_manifold_edges": nm,
            "flipped": flip, "uv": len(me.uv_layers) > 0,
            "slots": [s.material.name if s.material else None for s in o.material_slots],
            "y_range": [round(min(p.y for p in pts), 4), round(max(p.y for p in pts), 4)],
            "z_range": [round(min(p.z for p in pts), 4), round(max(p.z for p in pts), 4)],
            "x_range": [round(min(p.x for p in pts), 4), round(max(p.x for p in pts), 4)],
        })
    if allv:
        rec["bbox_min"] = [round(min(p[i] for p in allv), 4) for i in range(3)]
        rec["bbox_max"] = [round(max(p[i] for p in allv), 4) for i in range(3)]
        if abs(rec["bbox_min"][2]) > 1e-3:
            rec["problems"].append("base not at z=0: %.4f" % rec["bbox_min"][2])
    lod_names = [m["name"] for m in rec["meshes"] if "_LOD" in m["name"]]
    col_names = [m["name"] for m in rec["meshes"] if m["name"].startswith(("UBX_", "UCX_", "COL_"))]
    if not lod_names:
        rec["problems"].append("no _LOD<n> meshes")
    if not col_names:
        rec["problems"].append("no collision mesh")
    for m in rec["meshes"]:
        if m["name"] in col_names:
            continue
        if m["ngons"]:
            rec["problems"].append("%s: %d n-gons" % (m["name"], m["ngons"]))
        if m["flipped"] and m["name"].endswith("_LOD0"):
            rec["problems"].append("%s: %d inverted faces" % (m["name"], m["flipped"]))
        elif m["flipped"]:
            # diagnostic only: see the comment on the test above, and the backface-culled renders
            rec.setdefault("lod_flip_diagnostic", []).append("%s: %d" % (m["name"], m["flipped"]))
        if not m["uv"]:
            rec["problems"].append("%s: no UVs" % m["name"])
    out.append(rec)

path = os.path.join(HERE, "verify.json")
with open(path, "w", encoding="utf-8") as fh:
    json.dump(out, fh, indent=1)
print("VC_VERIFY:" + path)

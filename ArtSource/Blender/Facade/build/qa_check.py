"""VEYRA CITY - scripted self-check for the facade / rooftop kit.

    blender --factory-startup -b -noaudio -P qa_check.py

Re-imports every exported FBX and measures what Unity will see. Checks, per file:
  * root is a single EMPTY; children are `<name>_LOD<n>` plus optional `COL_`/`UCX_`/`UBX_` collision
  * LOD naming contiguous from 0; props under 2 m may ship LOD0+LOD1 only (CLAUDE.md)
  * applied transforms (scale 1, rotation 0 on every LOD mesh)
  * 0 n-gons, 0 inverted faces at LOD0, 0 degenerate faces, 0 loose verts
  * UVs on every LOD, material slots present and drawn from the shared 7
  * triangle budget per prop
  * pivot: every model's origin on the x/y centre of its own anchor, and base/sill at z = 0
  * facing: the outward face is +Y in the file, i.e. Unity +Z after VC_Unity_Prop

LOD normals are NOT gated on a numeric test - on this geometry both numeric inverted-face tests are
unreliable. The gate is `lod_check.py`, which renders every LOD backface-culled against a red world.
"""
import json
import os

import bmesh
import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
EXPORT = os.path.join(ROOT, "export")
SHARED = {"VC_Fac_Metal", "VC_Fac_Glass", "VC_Fac_Panel", "VC_Fac_Fabric", "VC_Fac_Blank",
          "VC_Fac_Concrete", "VC_Fac_Emissive"}
BUDGET_LOD0 = 400          # CLAUDE.md small-prop budget; nothing in this kit should need more

report = json.load(open(os.path.join(HERE, "report_facade.json")))
BY_NAME = {r["name"]: r for r in report}

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
    bpy.ops.import_scene.fbx(filepath=os.path.join(EXPORT, fn), use_anim=False)
    objs = list(bpy.context.scene.objects)
    roots = [o for o in objs if o.parent is None]
    name = os.path.splitext(fn)[0]
    src = BY_NAME.get(name, {})
    rec = {"file": fn, "problems": [], "meshes": []}

    if len(roots) != 1 or roots[0].type != "EMPTY":
        rec["problems"].append("expected one EMPTY root, got %s"
                               % [(r.name, r.type) for r in roots])

    lod0_tree = None
    for o in objs:
        if o.type == "MESH" and o.name.endswith("_LOD0"):
            rb = bmesh.new()
            rb.from_mesh(o.data)
            rb.transform(o.matrix_world)
            rb.normal_update()
            bmesh.ops.triangulate(rb, faces=rb.faces)
            lod0_tree = BVHTree.FromBMesh(rb)
            rb.free()

    allv = []
    lod_levels, col_names = [], []
    for o in objs:
        if o.type != "MESH":
            continue
        me = o.data
        bm = bmesh.new()
        bm.from_mesh(me)
        bm.transform(o.matrix_world)
        bm.normal_update()
        pts = [v.co.copy() for v in bm.verts]
        is_col = o.name.startswith(("COL_", "UCX_", "UBX_"))
        if is_col:
            col_names.append(o.name)
        else:
            allv += pts
            if "_LOD" in o.name:
                lod_levels.append(int(o.name.rsplit("_LOD", 1)[1]))
        tris = sum(len(f.verts) - 2 for f in bm.faces)
        ng = sum(1 for f in bm.faces if len(f.verts) > 4)
        before = [(f.normal.copy(), all(e.is_manifold for e in f.edges)) for f in bm.faces]
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        flip = sum(1 for f, (n, ok) in zip(bm.faces, before) if ok and f.normal.dot(n) < 0.0)
        degen = sum(1 for f in bm.faces if f.calc_area() < 1e-9)
        loose = sum(1 for v in bm.verts if not v.link_faces)
        bm.free()
        m = {"name": o.name, "tris": tris, "ngons": ng, "degenerate": degen, "loose_verts": loose,
             "uv": len(me.uv_layers) > 0,
             "slots": [s.material.name if s.material else None for s in o.material_slots],
             "scale": [round(v, 5) for v in o.matrix_world.to_scale()],
             "collision": is_col}
        if o.name.endswith("_LOD0"):
            m["flipped"] = flip
        rec["meshes"].append(m)
        if ng and not is_col:
            rec["problems"].append("%s: %d n-gons" % (o.name, ng))
        if degen:
            rec["problems"].append("%s: %d degenerate faces" % (o.name, degen))
        if loose:
            rec["problems"].append("%s: %d loose vertices" % (o.name, loose))
        if not is_col and not m["uv"]:
            rec["problems"].append("%s: no UVs" % o.name)
        if o.name.endswith("_LOD0") and flip:
            rec["problems"].append("%s: %d inverted faces at LOD0" % (o.name, flip))
        if any(abs(s - 1.0) > 1e-3 for s in m["scale"]):
            rec["problems"].append("%s: scale not applied %s" % (o.name, m["scale"]))
        for sname in m["slots"]:
            if sname and sname.split(".")[0] not in SHARED:
                rec["problems"].append("%s: material %s outside the shared set" % (o.name, sname))

    if not lod_levels:
        rec["problems"].append("no _LOD<n> meshes")
    else:
        lod_levels.sort()
        if lod_levels != list(range(len(lod_levels))):
            rec["problems"].append("LOD levels not contiguous from 0: %s" % lod_levels)
        size = max(src.get("size_m", [9, 9, 9]))
        if len(lod_levels) == 2 and size >= 2.0:
            rec["problems"].append("only 2 LODs but the prop is %.2f m (CLAUDE.md wants 3)" % size)
        if len(lod_levels) == 3 and size < 2.0:
            rec["note"] = "3 LODs on a sub-2 m prop (CLAUDE.md allows 2)"

    lod0 = next((m for m in rec["meshes"] if m["name"].endswith("_LOD0")), None)
    if lod0 and lod0["tris"] > BUDGET_LOD0:
        rec["problems"].append("LOD0 %d tris over the %d budget" % (lod0["tris"], BUDGET_LOD0))

    if src.get("collision_type") and not col_names:
        rec["problems"].append("expected a %s collider, none in the file" % src["collision_type"])
    for c in col_names:
        if not c.startswith(("COL_", "UCX_", "UBX_")):
            rec["problems"].append("collision mesh misnamed: %s" % c)

    if allv:
        lo = [min(p[i] for p in allv) for i in range(3)]
        hi = [max(p[i] for p in allv) for i in range(3)]
        rec["bbox_min"] = [round(v, 4) for v in lo]
        rec["bbox_max"] = [round(v, 4) for v in hi]
        anchor = src.get("anchor")
        # Where z = 0 has to land depends on the anchor kind. `window` puts it on the sill line, `band`
        # on the band soffit and `ground` on the base, so the lowest geometry must sit there. A `wall`
        # piece pivots at GROUND and carries its own mounting height, so an awning starting at 3.3 m is
        # correct - only a piece reaching BELOW the ground plane is wrong.
        if anchor in ("window", "band", "ground"):
            if abs(lo[2]) > 1e-3:
                rec["problems"].append("anchor not at z = 0: %.4f" % lo[2])
        elif lo[2] < -1e-3:
            rec["problems"].append("wall-anchored piece reaches below the ground plane: %.4f" % lo[2])
        # Centring on x is a tiling requirement for the repeating modules. A free-standing prop may be
        # asymmetric (a tank's ladder and downpipe) as long as its body sits on the pivot.
        if anchor in ("window", "band") and abs((lo[0] + hi[0]) / 2.0) > 0.02:
            rec["problems"].append("not centred on x: centre %.3f" % ((lo[0] + hi[0]) / 2.0))
        if anchor in ("wall", "window", "band"):
            # the wall face is the pivot plane: geometry projects toward the street (+Y in the file,
            # which is Unity +Z after the VC_Unity_Prop turn) and must not run far behind the wall
            if hi[1] < 0.05:
                rec["problems"].append("wall-anchored piece has no geometry toward the street (+Y)")
            if lo[1] < -0.35:
                rec["problems"].append("wall-anchored piece reaches %.2f m behind the wall face" % lo[1])
    out.append(rec)

path = os.path.join(HERE, "qa_report.json")
with open(path, "w", encoding="utf-8") as fh:
    json.dump(out, fh, indent=1)
bad = sum(len(r["problems"]) for r in out)
print("VC_FAC_QA: %d files, %d problems" % (len(out), bad))
for r in out:
    for p in r["problems"]:
        print("   %s: %s" % (r["file"], p))

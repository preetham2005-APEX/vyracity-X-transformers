"""Review sheets for the terrain kit (reuses the vegetation review renderer helpers).

Run: blender -b --python make_review_terrain.py
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import bpy
import numpy as np
from mathutils import Vector

import terrain_lib as T
from atlas_paint import write_png
from make_review import H, W, backdrop, make_camera, read_png, render, set_engine, setup_world


REVIEW_PROPS = ("VC_Ground", "VC_Backdrop")


def visible(lod):
    """ALLOW-LIST, not deny-list: render only the LOD meshes we asked for, plus the review's own
    ground plane.

    The vegetation version hides a tuple of collision prefixes and renders everything else, so a
    prefix the list does not know about renders silently - which is how an untextured COL_ collider
    ended up z-fighting the cut face and reading as blown-out specular. Filtering the other way
    round, the next helper prefix someone invents cannot appear by accident.
    (Approach suggested by the street-furniture session, which builds its sheets this way.)"""
    for ob in bpy.data.objects:
        if ob.type != "MESH":
            continue
        ob.hide_render = not (ob.name.endswith("_LOD%d" % lod)
                              or ob.name.startswith(REVIEW_PROPS))

GROUPS = ["Boulder", "Outcrop", "CutFace", "Scree", "Shore", "Log", "Impostor"]


def main():
    os.makedirs(T.REVIEW, exist_ok=True)
    tmp = os.path.join(T.REVIEW, "_tmp")
    os.makedirs(tmp, exist_ok=True)
    for grp in GROUPS:
        path = os.path.join(T.ROOT, "VC_Terrain_%s.blend" % grp)
        if not os.path.exists(path):
            print("skip (no blend):", grp)
            continue
        bpy.ops.wm.open_mainfile(filepath=path)
        set_engine()
        setup_world()
        lo = Vector((1e9, 1e9, 1e9))
        hi = Vector((-1e9, -1e9, -1e9))
        for ob in bpy.data.objects:
            if ob.type == "MESH" and ob.name.endswith("_LOD0"):
                for c in ob.bound_box:
                    w = ob.matrix_world @ Vector(c)
                    lo = Vector((min(lo.x, w.x), min(lo.y, w.y), min(lo.z, w.z)))
                    hi = Vector((max(hi.x, w.x), max(hi.y, w.y), max(hi.z, w.z)))
        cx = (lo.x + hi.x) / 2
        span = max(hi.x - lo.x, (hi.z - lo.z) * (W / float(H))) * 1.08
        cz = (hi.z + lo.z) / 2
        backdrop(cx, hi.z, span)
        rows = []
        for tag, lod, tq in (("LOD0_front", 0, False), ("LOD0_threequarter", 0, True),
                             ("LOD1_front", 1, False), ("LOD2_front", 2, False)):
            for ob in list(bpy.data.objects):
                if ob.name == "VC_Cam":
                    bpy.data.objects.remove(ob, do_unlink=True)
            make_camera(cx, cz, span, tq)
            visible(lod)
            p = os.path.join(tmp, "%s_%s.png" % (grp, tag))
            render(p)
            rows.append(read_png(p))
        sheet = np.concatenate(rows, 0)
        rgb, a = sheet[..., :3], sheet[..., 3:4]
        bgc = np.array([0.62, 0.66, 0.70], np.float32)
        comp = np.clip(rgb * a + bgc * (1 - a), 0, 1)
        for i in range(1, 4):
            comp[i * H - 1:i * H + 1] = 0.55
        out = np.concatenate([comp, np.ones(comp.shape[:2] + (1,), np.float32)], -1)
        write_png(os.path.join(T.REVIEW, "REVIEW_%s.png" % grp), (out * 255 + 0.5).astype(np.uint8))
        print("review sheet:", grp)


main()

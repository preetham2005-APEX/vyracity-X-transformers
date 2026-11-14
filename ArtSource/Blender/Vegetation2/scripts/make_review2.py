"""Review sheets for Vegetation2 (reuses set 1's review renderer helpers)."""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import bpy
import numpy as np
from mathutils import Vector

import veg_lib2 as V
from atlas_paint import write_png
from make_review import H, W, backdrop, make_camera, read_png, render, set_engine, setup_world, visible

SPECIES = ["Alpine", "Redwood", "Industrial", "Ground"]


def main():
    os.makedirs(V.REVIEW, exist_ok=True)
    tmp = os.path.join(V.REVIEW, "_tmp")
    os.makedirs(tmp, exist_ok=True)
    for sp in SPECIES:
        bpy.ops.wm.open_mainfile(filepath=os.path.join(V.ROOT, "VC_Veg2_%s.blend" % sp))
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
        span = max(hi.x - lo.x, (hi.z - lo.z) * (W / float(H))) * 1.06
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
            p = os.path.join(tmp, "%s_%s.png" % (sp, tag))
            render(p)
            rows.append(read_png(p))
        sheet = np.concatenate(rows, 0)
        rgb, a = sheet[..., :3], sheet[..., 3:4]
        bgc = np.array([0.62, 0.66, 0.70], np.float32)
        comp = np.clip(rgb * a + bgc * (1 - a), 0, 1)
        for i in range(1, 4):
            comp[i * H - 1:i * H + 1] = 0.55
        out = np.concatenate([comp, np.ones(comp.shape[:2] + (1,), np.float32)], -1)
        write_png(os.path.join(V.REVIEW, "REVIEW_%s.png" % sp), (out * 255 + 0.5).astype(np.uint8))
        print("review sheet:", sp)


main()

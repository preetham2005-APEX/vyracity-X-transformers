"""Render a multi-angle review sheet per species from the saved .blend files.

Each sheet: LOD0 front elevation, LOD0 three-quarter, LOD1 front, LOD2 front - same framing, so
silhouette loss across the LOD chain is directly comparable.
Run: blender -b --python make_review.py
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import bpy
import numpy as np
from mathutils import Vector

from atlas_paint import write_png
import veg_lib as V

SPECIES = ["Palm_Royal", "Palm_Fan", "Broadleaf", "Cypress", "Pine", "Hedge", "Shrub"]
W, H = 1600, 620


def set_engine():
    sc = bpy.context.scene
    prop = bpy.types.RenderSettings.bl_rna.properties["engine"]
    ids = [i.identifier for i in prop.enum_items]
    for want in ("BLENDER_EEVEE_NEXT", "BLENDER_EEVEE", "EEVEE"):
        if want in ids:
            try:
                sc.render.engine = want
                return want
            except TypeError:
                pass
    return sc.render.engine


def setup_world():
    w = bpy.data.worlds.get("VC_Review") or bpy.data.worlds.new("VC_Review")
    w.use_nodes = True
    bg = next(n for n in w.node_tree.nodes if n.type == "BACKGROUND")
    bg.inputs[0].default_value = (0.22, 0.25, 0.30, 1.0)
    bg.inputs[1].default_value = 1.6
    bpy.context.scene.world = w

    sun = bpy.data.objects.new("VC_Sun", bpy.data.lights.new("VC_Sun", "SUN"))
    sun.data.energy = 4.0
    sun.data.angle = math.radians(2.0)
    sun.rotation_euler = (math.radians(52), 0, math.radians(38))
    bpy.context.scene.collection.objects.link(sun)

    fill = bpy.data.objects.new("VC_Fill", bpy.data.lights.new("VC_Fill", "SUN"))
    fill.data.energy = 1.0
    fill.rotation_euler = (math.radians(66), 0, math.radians(-135))
    bpy.context.scene.collection.objects.link(fill)


def backdrop(cx, cz, span):
    s = span * 1.6
    gm = bpy.data.meshes.new("VC_Ground")
    gm.from_pydata([(cx - s, -s, 0), (cx + s, -s, 0), (cx + s, 40, 0), (cx - s, 40, 0)], [],
                   [(0, 1, 2, 3)])
    gm.update()
    g = bpy.data.objects.new("VC_Ground", gm)
    gmat = bpy.data.materials.new("VC_Ground")
    gmat.use_nodes = True
    gb = next(n for n in gmat.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    gb.inputs["Base Color"].default_value = (0.19, 0.19, 0.19, 1)
    gb.inputs["Roughness"].default_value = 0.95
    gm.materials.append(gmat)
    bpy.context.scene.collection.objects.link(g)


def make_camera(cx, cz, span, three_quarter=False):
    cam = bpy.data.objects.new("VC_Cam", bpy.data.cameras.new("VC_Cam"))
    cam.data.type = "ORTHO"
    cam.data.ortho_scale = span * 1.08
    if three_quarter:
        d = span * 1.4
        a = math.radians(38)
        cam.location = (cx - math.sin(a) * d, -math.cos(a) * d, cz + span * 0.18)
        cam.rotation_euler = (math.radians(80), 0, math.radians(-38))
    else:
        cam.location = (cx, -span * 2.0, cz)
        cam.rotation_euler = (math.radians(90), 0, 0)
    bpy.context.scene.collection.objects.link(cam)
    bpy.context.scene.camera = cam
    return cam


def visible(lod):
    for ob in bpy.data.objects:
        if "_LOD" in ob.name:
            ob.hide_render = not ob.name.endswith("_LOD%d" % lod)
        elif ob.name.startswith(("UCX_", "UBX_")):
            ob.hide_render = True


def render(path):
    sc = bpy.context.scene
    sc.render.resolution_x, sc.render.resolution_y = W, H
    sc.render.resolution_percentage = 100
    sc.render.filepath = path
    sc.render.image_settings.file_format = "PNG"
    sc.render.image_settings.color_mode = "RGBA"
    try:
        sc.eevee.taa_render_samples = 96
    except AttributeError:
        pass
    try:
        sc.view_settings.view_transform = "Standard"
        sc.view_settings.look = "None"
    except TypeError:
        pass
    bpy.ops.render.render(write_still=True)


def read_png(path):
    im = bpy.data.images.load(path)
    px = np.array(im.pixels[:], np.float32).reshape(im.size[1], im.size[0], 4)
    bpy.data.images.remove(im)
    return px[::-1]  # Blender images are bottom-up


def main():
    os.makedirs(V.REVIEW, exist_ok=True)
    tmp = os.path.join(V.REVIEW, "_tmp")
    os.makedirs(tmp, exist_ok=True)
    for sp in SPECIES:
        bpy.ops.wm.open_mainfile(filepath=os.path.join(V.ROOT, "VC_Veg_%s.blend" % sp))
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
        backdrop(cx, hi.z, span)  # ground only

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
        rgb = sheet[..., :3]
        a = sheet[..., 3:4]
        bgc = np.array([0.62, 0.66, 0.70], np.float32)
        comp = np.clip(rgb * a + bgc * (1 - a), 0, 1)
        for i in range(1, 4):  # separator lines between rows
            comp[i * H - 1:i * H + 1] = 0.55
        out = np.concatenate([comp, np.ones(comp.shape[:2] + (1,), np.float32)], -1)
        write_png(os.path.join(V.REVIEW, "REVIEW_%s.png" % sp), (out * 255 + 0.5).astype(np.uint8))
        print("review sheet:", sp)


if __name__ == "__main__":
    main()

"""Contact-sheet renders of the street furniture set (evidence; appended straight from the .blend files).

    blender --factory-startup -b -noaudio -P preview.py
"""
import math
import os
import sys

import bpy
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
OUT = os.path.join(ROOT, "preview")
os.makedirs(OUT, exist_ok=True)


def clear():
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)


def append_lod0(blend, name):
    before = set(bpy.data.objects)
    bpy.ops.wm.append(filepath=os.path.join(blend, "Object", name),
                      directory=os.path.join(blend, "Object") + os.sep, filename=name)
    new = [o for o in bpy.data.objects if o not in before]
    return new[0] if new else None


def bbox(o):
    objs = [x for x in [o] + list(o.children_recursive) if x.type == "MESH"] or [o]
    pts = [x.matrix_world @ Vector(c) for x in objs for c in x.bound_box]
    lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    return lo, hi


def set_engine():
    sc = bpy.context.scene
    for e in ("BLENDER_EEVEE_NEXT", "BLENDER_EEVEE", "CYCLES"):
        try:
            sc.render.engine = e
            return e
        except TypeError:
            continue
    return sc.render.engine


def world_grey():
    w = bpy.data.worlds.new("VC_Preview")
    w.use_nodes = True
    bg = next(n for n in w.node_tree.nodes if n.type == "BACKGROUND")
    bg.inputs[0].default_value = (0.055, 0.060, 0.070, 1.0)
    bg.inputs[1].default_value = 1.2
    bpy.context.scene.world = w


def sheet(items, out_name, title_gap=0.9):
    clear()
    world_grey()
    placed, x = [], 0.0
    for blend, name in items:
        o = append_lod0(blend, name)
        if o is None:
            print("MISSING", name)
            continue
        lo, hi = bbox(o)
        w = hi.x - lo.x
        o.location.x = x + w / 2.0
        bpy.context.view_layer.update()
        placed.append((o, x, x + w))
        x += w + title_gap
    total = x - title_gap
    heights = []
    for o, _, _ in placed:
        lo, hi = bbox(o)
        heights.append(hi.z)
    top = max(heights) if heights else 2.0

    # 1.80 m human-scale rod at each end
    for px in (-title_gap, total + title_gap * 0.3):
        bpy.ops.mesh.primitive_cube_add(size=1.0, location=(px, 0, 0.90))
        rod = bpy.context.active_object
        rod.scale = (0.18, 0.18, 1.80)
        rod.name = "ScaleRod_1m80"

    bpy.ops.mesh.primitive_plane_add(size=max(total * 3, 40), location=(total / 2, 0, -0.002))

    cam_data = bpy.data.cameras.new("Cam")
    cam = bpy.data.objects.new("Cam", cam_data)
    bpy.context.scene.collection.objects.link(cam)
    cam_data.type = "ORTHO"
    cam_data.ortho_scale = max(total + 3.0, top * 1.9)
    cam.location = (total / 2.0, -30.0, top * 0.52)
    cam.rotation_euler = (math.radians(90), 0, 0)
    bpy.context.scene.camera = cam

    sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN"))
    bpy.context.scene.collection.objects.link(sun)
    sun.data.energy = 3.2
    sun.rotation_euler = (math.radians(52), math.radians(8), math.radians(-35))

    fill = bpy.data.objects.new("Fill", bpy.data.lights.new("Fill", "AREA"))
    bpy.context.scene.collection.objects.link(fill)
    fill.data.energy = 900
    fill.data.size = 12
    fill.location = (total / 2.0, -14.0, top * 1.4)
    fill.rotation_euler = (math.radians(70), 0, 0)

    sc = bpy.context.scene
    sc.render.resolution_x, sc.render.resolution_y = 2048, 1024
    sc.render.film_transparent = False
    sc.render.image_settings.file_format = "PNG"
    sc.render.filepath = os.path.join(OUT, out_name)
    bpy.ops.render.render(write_still=True)
    print("WROTE", sc.render.filepath + ".png", "items:", [o.name for o, _, _ in placed])

    # second pass: three-quarter perspective, so arms and depth read
    cam_data.type = "PERSP"
    cam_data.lens = 50
    d = max(total * 1.05, top * 2.6)
    cam.location = (total * 0.52, -d, top * 0.80)
    cam.rotation_euler = (math.radians(82), 0, math.radians(-8))
    sc.render.filepath = os.path.join(OUT, out_name + "_34")
    bpy.ops.render.render(write_still=True)
    print("WROTE", sc.render.filepath + ".png")


def main():
    names = []
    for fn in sorted(os.listdir(ROOT)):
        if fn.lower().endswith(".blend"):
            names.append((os.path.join(ROOT, fn), os.path.splitext(fn)[0] + "_LOD0"))
    small = [n for n in names if not any(k in n[1] for k in ("Lamp", "Shelter", "Sign"))]
    tall = [n for n in names if any(k in n[1] for k in ("Lamp", "Shelter", "Sign"))]
    set_engine()
    sheet(small, "sheet_small_props")
    sheet(tall, "sheet_tall_props", title_gap=1.3)


main()

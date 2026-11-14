"""Render every house's LOD1 and LOD2 with BACKFACE CULLING ON.

A flipped triangle is invisible from outside when culling is on, so it shows up as a hole. This is the
check that decides whether a LOD is really broken - a normal recalculation on a decimated (partly open)
shell is ambiguous and disagrees with itself, so it cannot be the gate.

    blender --factory-startup -b -noaudio -P lod_check.py
"""
import math
import os

import bpy
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
OUT = os.path.join(ROOT, "preview")
os.makedirs(OUT, exist_ok=True)


def clear():
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)


def sheet(level, only=None, tag=""):
    clear()
    w = bpy.data.worlds.new("W")
    w.use_nodes = True
    bg = next(n for n in w.node_tree.nodes if n.type == "BACKGROUND")
    bg.inputs[0].default_value = (0.9, 0.25, 0.25, 1.0)      # red background: any hole shows up red
    bg.inputs[1].default_value = 1.0
    bpy.context.scene.world = w

    x = 0.0
    top = 0.0
    for fn in sorted(f for f in os.listdir(ROOT) if f.endswith(".blend") and (only is None or any(k in f for k in only))):
        name = os.path.splitext(fn)[0] + "_LOD%d" % level
        blend = os.path.join(ROOT, fn)
        before = set(bpy.data.objects)
        bpy.ops.wm.append(filepath=os.path.join(blend, "Object", name),
                          directory=os.path.join(blend, "Object") + os.sep, filename=name)
        new = [o for o in bpy.data.objects if o not in before and o.type == "MESH"]
        if not new:
            print("MISSING", name)
            continue
        o = new[0]
        o.parent = None
        pts = [o.matrix_world @ Vector(c) for c in o.bound_box]
        lo = min(p.x for p in pts)
        hi = max(p.x for p in pts)
        top = max(top, max(p.z for p in pts))
        o.location.x += x - lo
        x += (hi - lo) + 2.0
        bpy.context.view_layer.update()

    for m in bpy.data.materials:
        m.use_backface_culling = True                        # the point of this render

    total = x - 2.0
    cam_data = bpy.data.cameras.new("C")
    cam = bpy.data.objects.new("C", cam_data)
    bpy.context.scene.collection.objects.link(cam)
    cam_data.type = "ORTHO"
    cam_data.ortho_scale = total + 4.0
    cam.location = (total / 2.0, -60.0, top * 0.5)
    d = Vector((0.0, 60.0, 0.0))
    cam.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()
    bpy.context.scene.camera = cam

    sun = bpy.data.objects.new("S", bpy.data.lights.new("S", "SUN"))
    bpy.context.scene.collection.objects.link(sun)
    sun.data.energy = 3.0
    sun.rotation_euler = (math.radians(55), 0, math.radians(-30))

    sc = bpy.context.scene
    try:
        sc.render.engine = "BLENDER_EEVEE_NEXT"
    except TypeError:
        pass
    sc.render.resolution_x, sc.render.resolution_y = (2048, 620) if only is None else (2048, 1100)
    sc.render.filepath = os.path.join(OUT, "lod%d_backface_check%s" % (level, tag))
    sc.render.image_settings.file_format = "PNG"
    bpy.ops.render.render(write_still=True)
    print("WROTE", sc.render.filepath + ".png")


sheet(1, only=("House",), tag="_houses")
sheet(2, only=("House",), tag="_houses")
sheet(1, only=("Tower",), tag="_towers")
sheet(2, only=("Tower",), tag="_towers")
sheet(1, only=("Neon",), tag="_neon")
sheet(2, only=("Neon",), tag="_neon")
sheet(2, only=("Coast","Town"), tag="_coasttown")
sheet(1, only=("Coast","Town"), tag="_coasttown")
sheet(2, only=("Mid","Ind"), tag="_midind")
sheet(1, only=("Mid","Ind"), tag="_midind")
sheet(2, only=("Alp","Rur","Avi","Ret"), tag="_ruralair")
sheet(1, only=("Alp","Rur","Avi","Ret"), tag="_ruralair")

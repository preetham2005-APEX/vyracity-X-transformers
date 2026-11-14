"""Render a 3/4 review image of each prop's LOD0 into ArtSource/Blender/Infra/review/."""
import glob
import math
import os

import bpy
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, ".."))
REVIEW = os.path.join(ROOT, "review")


def set_engine():
    sc = bpy.context.scene
    for name in ("BLENDER_EEVEE_NEXT", "BLENDER_EEVEE", "BLENDER_WORKBENCH"):
        try:
            sc.render.engine = name
            return name
        except TypeError:
            continue
    return sc.render.engine


def render(path):
    bpy.ops.wm.open_mainfile(filepath=path)
    name = os.path.splitext(os.path.basename(path))[0]
    keep = bpy.data.objects.get("%s_LOD0" % name)
    for ob in list(bpy.data.objects):
        if ob.type == "MESH" and ob is not keep:
            bpy.data.objects.remove(ob, do_unlink=True)
    lo = Vector((1e9, 1e9, 1e9))
    hi = Vector((-1e9, -1e9, -1e9))
    for c in keep.bound_box:
        p = keep.matrix_world @ Vector(c)
        lo = Vector((min(lo[i], p[i]) for i in range(3)))
        hi = Vector((max(hi[i], p[i]) for i in range(3)))
    centre = (lo + hi) * 0.5
    size = max((hi - lo).length, 1.0)

    cam_data = bpy.data.cameras.new("REVIEW_CAM")
    cam_data.type = "ORTHO"
    cam_data.ortho_scale = size * 1.15
    cam = bpy.data.objects.new("REVIEW_CAM", cam_data)
    bpy.context.scene.collection.objects.link(cam)
    d = Vector((math.cos(math.radians(-55)), math.sin(math.radians(-55)), 0.62)).normalized()
    cam.location = centre + d * size * 2.0
    direction = (centre - cam.location).normalized()
    cam.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
    bpy.context.scene.camera = cam

    sun_data = bpy.data.lights.new("SUN", "SUN")
    sun_data.energy = 3.0
    sun = bpy.data.objects.new("SUN", sun_data)
    bpy.context.scene.collection.objects.link(sun)
    sun.rotation_euler = (math.radians(52), 0.0, math.radians(-125))
    world = bpy.data.worlds.new("W")
    world.use_nodes = True
    bg = next(n for n in world.node_tree.nodes if n.type == "BACKGROUND")
    bg.inputs[0].default_value = (0.42, 0.52, 0.68, 1.0)
    bg.inputs[1].default_value = 1.1
    bpy.context.scene.world = world

    sc = bpy.context.scene
    set_engine()
    sc.render.resolution_x, sc.render.resolution_y = 640, 480
    sc.render.film_transparent = False
    sc.render.filepath = os.path.join(REVIEW, "%s.png" % name)
    sc.render.image_settings.file_format = "PNG"
    bpy.ops.render.render(write_still=True)
    print("[REVIEW] %s" % name)


def main():
    os.makedirs(REVIEW, exist_ok=True)
    for p in sorted(glob.glob(os.path.join(ROOT, "*.blend"))):
        render(p)


main()

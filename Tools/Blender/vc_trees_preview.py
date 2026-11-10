"""Preview render of the vc_trees.py output (LOD0 of each tree, textured, EEVEE).
"D:/New folder/blender.exe" -b --factory-startup -P Tools/Blender/vc_trees_preview.py -- --root <folder with Assets/> --png <out.png>
"""
import math
import os
import sys

import bpy
from mathutils import Vector


def arg(name, default):
    a = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    return a[a.index(name) + 1] if name in a and a.index(name) + 1 < len(a) else default


root = arg("--root", "E:/VeyraCity")
LODSHOW = "_LOD" + arg("--lod", "0")
png = arg("--png", "E:/VeyraCity/Screenshots/trees_preview.png")
for o in list(bpy.data.objects):
    bpy.data.objects.remove(o, do_unlink=True)
tex_dir = os.path.join(root, "Assets/_Veyra/Art/Textures/Vegetation")


def material(name, tex, clip):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    img = nt.nodes.new("ShaderNodeTexImage")
    img.image = bpy.data.images.load(os.path.join(tex_dir, tex))
    nt.links.new(img.outputs[0], bsdf.inputs["Base Color"])
    if clip:
        nt.links.new(img.outputs[1], bsdf.inputs["Alpha"])
        try:
            m.surface_render_method = "DITHERED"
        except Exception:
            pass
    bsdf.inputs["Roughness"].default_value = 0.8
    return m


bark = material("bark", "T_Bark.png", False)
leaf = material("leaf", "T_Foliage_Atlas.png", True)
x = 0.0
for name in ("VC_Tree_Broadleaf", "VC_Tree_Pine", "VC_Tree_Palm", "VC_Tree_Bush"):
    bpy.ops.import_scene.fbx(filepath=os.path.join(root, "Assets/_Veyra/Art/Models/Vegetation", name + ".fbx"))
    for o in bpy.context.selected_objects:
        if o.type == "MESH" and not o.name.endswith(LODSHOW):
            o.hide_render = True
        if o.type == "MESH":
            for i, slot in enumerate(o.material_slots):
                slot.material = (bark, leaf)[min(i, 1)]
        if o.parent is None:
            o.location.x = x
    x += 11.0
bpy.ops.mesh.primitive_plane_add(size=200, location=(15, 0, 0))
cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
bpy.context.scene.collection.objects.link(cam)
cam.location = Vector((16, -30, 7))
cam.rotation_euler = (math.radians(84), 0, 0)
cam.data.lens = 30
bpy.context.scene.camera = cam
sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
sun.data.energy = 4
sun.rotation_euler = (math.radians(50), math.radians(20), math.radians(30))
bpy.context.scene.collection.objects.link(sun)
w = bpy.context.scene.world or bpy.data.worlds.new("w")
bpy.context.scene.world = w
w.use_nodes = True
bg = next(n for n in w.node_tree.nodes if n.type == "BACKGROUND")
bg.inputs[0].default_value = (0.55, 0.68, 0.85, 1)
bg.inputs[1].default_value = 1.0
sc = bpy.context.scene
for eng in ("BLENDER_EEVEE_NEXT", "BLENDER_EEVEE", "CYCLES"):
    try:
        sc.render.engine = eng
        break
    except TypeError:
        continue
sc.render.resolution_x, sc.render.resolution_y = 1280, 540
sc.render.filepath = png
bpy.ops.render.render(write_still=True)
print("preview", png, sc.render.engine)

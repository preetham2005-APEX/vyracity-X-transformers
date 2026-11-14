"""Contact-sheet renders of the building kit (evidence; appended straight from the .blend files).

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


TEX = os.path.join(ROOT, "textures")


def wire_textures():
    """Hook the generated kit textures into every material, exactly as Unity's KitbashMaterials does
    (baseColor -> Base Color, metallicRoughness G -> Roughness / B -> Metallic, normal -> Normal)."""
    import bpy
    for m in bpy.data.materials:
        if not m.use_nodes or not m.name.startswith("VC_"):
            continue
        base = m.name.split(".")[0]
        nt = m.node_tree
        bsdf = next((n for n in nt.nodes if n.type == "BSDF_PRINCIPLED"), None)
        if bsdf is None:
            continue
        def img(suffix, data):
            path = os.path.join(TEX, base + "_" + suffix + ".png")
            if not os.path.exists(path):
                return None
            n = nt.nodes.new("ShaderNodeTexImage")
            n.image = bpy.data.images.load(path, check_existing=True)
            n.image.colorspace_settings.name = "Non-Color" if data else "sRGB"
            return n
        bc = img("baseColor", False)
        if bc is not None:
            nt.links.new(bc.outputs["Color"], bsdf.inputs["Base Color"])
        mr = img("metallicRoughness", True)
        if mr is not None:
            sep = nt.nodes.new("ShaderNodeSeparateColor")
            nt.links.new(mr.outputs["Color"], sep.inputs[0])
            nt.links.new(sep.outputs[1], bsdf.inputs["Roughness"])
            nt.links.new(sep.outputs[2], bsdf.inputs["Metallic"])
        nm = img("normal", True)
        if nm is not None:
            nmap = nt.nodes.new("ShaderNodeNormalMap")
            nt.links.new(nm.outputs["Color"], nmap.inputs["Color"])
            nt.links.new(nmap.outputs["Normal"], bsdf.inputs["Normal"])


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
    def aim(loc, target):
        d = Vector(target) - Vector(loc)
        cam.location = loc
        cam.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()

    cam_data.type = "ORTHO"
    cam_data.ortho_scale = max(total + 4.0, top * 2.2)
    aim((total / 2.0, -60.0, top * 0.5), (total / 2.0, 0.0, top * 0.5))
    bpy.context.scene.camera = cam

    sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN"))
    bpy.context.scene.collection.objects.link(sun)
    sun.data.energy = 3.2
    sun.rotation_euler = (math.radians(52), math.radians(8), math.radians(-35))

    fill = bpy.data.objects.new("Fill", bpy.data.lights.new("Fill", "AREA"))
    bpy.context.scene.collection.objects.link(fill)
    fill.data.energy = 300
    fill.data.size = 12
    fill.location = (total / 2.0, -14.0, top * 1.4)
    fill.rotation_euler = (math.radians(70), 0, 0)

    wire_textures()
    sc = bpy.context.scene
    sc.render.resolution_x, sc.render.resolution_y = 2048, 900
    sc.render.film_transparent = False
    sc.render.image_settings.file_format = "PNG"
    sc.render.filepath = os.path.join(OUT, out_name)
    bpy.ops.render.render(write_still=True)
    print("WROTE", sc.render.filepath + ".png", "items:", [o.name for o, _, _ in placed])

    # second pass: three-quarter perspective, so arms and depth read
    cam_data.type = "PERSP"
    cam_data.lens = 40
    d = max(total * 0.60, top * 2.4)
    aim((total * 0.52 - total * 0.22, -d, top * 1.15), (total * 0.52, 0.0, top * 0.42))
    sc.render.filepath = os.path.join(OUT, out_name + "_34")
    bpy.ops.render.render(write_still=True)
    print("WROTE", sc.render.filepath + ".png")


def main():
    names = []
    for fn in sorted(os.listdir(ROOT)):
        if fn.lower().endswith(".blend"):
            names.append((os.path.join(ROOT, fn), os.path.splitext(fn)[0] + "_LOD0"))
    set_engine()
    houses = [n for n in names if "_House_" in n[1]]
    towers = [n for n in names if "_Tower_" in n[1]]
    if houses:
        half = (len(houses) + 1) // 2
        sheet(houses[:half], "sheet_a", title_gap=3.0)
        sheet(houses[half:], "sheet_b", title_gap=3.0)
    for i in range(0, len(towers), 8):
        sheet(towers[i:i + 8], "sheet_towers_%d" % (i // 8 + 1), title_gap=12.0)
    neon = [n for n in names if "_Neon_" in n[1]]
    if neon:
        sheet(neon, "sheet_neon", title_gap=10.0)
    coast = [n for n in names if "_Coast_" in n[1]]
    if coast:
        sheet(coast, "sheet_coast", title_gap=5.0)
    town = [n for n in names if "_Town_" in n[1]]
    if town:
        sheet(town, "sheet_town", title_gap=4.0)
    mid = [n for n in names if "_Mid_" in n[1]]
    if mid:
        sheet(mid, "sheet_midrise", title_gap=8.0)
    sheds = [n for n in names if "_Ind_Shed" in n[1]]
    if sheds:
        sheet(sheds, "sheet_ironport_sheds", title_gap=14.0)
    tanks = [n for n in names if "_Ind_Tank" in n[1] or "_Ind_Process" in n[1]]
    if tanks:
        sheet(tanks, "sheet_ironport_tanks", title_gap=8.0)
    for tag, key in (("alpine", "_Alp_"), ("rural", "_Rur_"), ("aviation", "_Avi_"), ("retail", "_Ret_")):
        grp = [n for n in names if key in n[1]]
        if grp:
            sheet(grp, "sheet_" + tag, title_gap=6.0)


main()

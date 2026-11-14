"""VEYRA CITY - review renders for the facade / rooftop kit.

    blender --factory-startup -b -noaudio -P review.py

Two things:
  1. contact sheets per group, with the kit textures wired, front ortho + three-quarter -> review/sheet_*.png
  2. the LOD gate: every LOD rendered BACKFACE-CULLED against a red world, so a flipped or missing
     triangle shows as a hole -> review/lod<n>_backface_check.png
The backface render is the gate for LOD normals, not a numeric test: on box-assembled geometry both
numeric inverted-face tests are unreliable (proved on the building kit - a decimated LOD2 that every
metric called clean rendered as a see-through shell).
"""
import math
import os

import bpy
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
OUT = os.path.join(ROOT, "review")
TEX = os.path.join(ROOT, "textures")
os.makedirs(OUT, exist_ok=True)

GROUPS = [("reveal_band_crown", ("WindowReveal", "SlabBand", "CorniceBand", "SillBand", "Crown")),
          ("shopfront", ("EntryRecess", "Shopfront", "Grille", "Awning", "Sign", "AFrame",
                         "DisplayPlinth")),
          ("rooftop", ("Hvac", "FanCowl", "DuctRun", "WaterTank", "StairOverrun", "ParapetCap",
                       "Antenna", "RoofHatch"))]


def clear():
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)


def set_engine():
    for e in ("BLENDER_EEVEE_NEXT", "BLENDER_EEVEE", "CYCLES"):
        try:
            bpy.context.scene.render.engine = e
            return
        except TypeError:
            continue


def wire_textures():
    """Hook the kit textures up the way URP will: _BC to base colour, _MS alpha to smoothness
    (inverted into Blender's roughness), R to metallic, _N through a normal map."""
    for m in bpy.data.materials:
        if not m.use_nodes or not m.name.startswith("VC_Fac_"):
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
            n.image.alpha_mode = "CHANNEL_PACKED"
            return n

        bc = img("BC", False)
        if bc is not None:
            nt.links.new(bc.outputs["Color"], bsdf.inputs["Base Color"])
        ms = img("MS", True)
        if ms is not None:
            sep = nt.nodes.new("ShaderNodeSeparateColor")
            nt.links.new(ms.outputs["Color"], sep.inputs[0])
            nt.links.new(sep.outputs[0], bsdf.inputs["Metallic"])
            inv = nt.nodes.new("ShaderNodeInvert")        # smoothness (alpha) -> roughness
            nt.links.new(ms.outputs["Alpha"], inv.inputs["Color"])
            nt.links.new(inv.outputs["Color"], bsdf.inputs["Roughness"])
        nm = img("N", True)
        if nm is not None:
            nmap = nt.nodes.new("ShaderNodeNormalMap")
            nt.links.new(nm.outputs["Color"], nmap.inputs["Color"])
            nt.links.new(nmap.outputs["Normal"], bsdf.inputs["Normal"])


def append_lod(blend, name):
    before = set(bpy.data.objects)
    try:
        bpy.ops.wm.append(filepath=os.path.join(blend, "Object", name),
                          directory=os.path.join(blend, "Object") + os.sep, filename=name)
    except Exception:
        return None
    new = [o for o in bpy.data.objects if o not in before and o.type == "MESH"]
    return new[0] if new else None


def lay_out(names, level, gap):
    x, top = 0.0, 0.0
    placed = []
    for blend, stem in names:
        o = append_lod(blend, "%s_LOD%d" % (stem, level))
        if o is None:
            continue
        o.parent = None
        pts = [o.matrix_world @ Vector(c) for c in o.bound_box]
        lo, hi = min(p.x for p in pts), max(p.x for p in pts)
        top = max(top, max(p.z for p in pts))
        o.location.x += x - lo
        x += (hi - lo) + gap
        bpy.context.view_layer.update()
        placed.append(o)
    return placed, max(0.001, x - gap), max(top, 0.5)


def camera(total, top, ortho):
    cam_data = bpy.data.cameras.new("C")
    cam = bpy.data.objects.new("C", cam_data)
    bpy.context.scene.collection.objects.link(cam)
    bpy.context.scene.camera = cam

    def aim(loc, target):
        d = Vector(target) - Vector(loc)
        cam.location = loc
        cam.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()

    if ortho:
        cam_data.type = "ORTHO"
        cam_data.ortho_scale = max(total + 2.0, top * 2.2)
        aim((total / 2.0, -60.0, top * 0.5), (total / 2.0, 0.0, top * 0.5))
    else:
        cam_data.type = "PERSP"
        cam_data.lens = 42
        d = max(total * 0.75, top * 2.6)
        aim((total * 0.30, -d, top * 1.25), (total * 0.52, 0.0, top * 0.45))
    return cam


def lights(total, top):
    sun = bpy.data.objects.new("S", bpy.data.lights.new("S", "SUN"))
    bpy.context.scene.collection.objects.link(sun)
    sun.data.energy = 3.0
    sun.rotation_euler = (math.radians(54), math.radians(6), math.radians(-34))
    fill = bpy.data.objects.new("F", bpy.data.lights.new("F", "AREA"))
    bpy.context.scene.collection.objects.link(fill)
    fill.data.energy = 220
    fill.data.size = 10
    fill.location = (total / 2.0, -9.0, top * 1.5)
    fill.rotation_euler = (math.radians(68), 0, 0)


def world(rgb, strength=1.0):
    w = bpy.data.worlds.new("W")
    w.use_nodes = True
    bg = next(n for n in w.node_tree.nodes if n.type == "BACKGROUND")
    bg.inputs[0].default_value = (rgb[0], rgb[1], rgb[2], 1.0)
    bg.inputs[1].default_value = strength
    bpy.context.scene.world = w


def render(path, w=2048, h=900):
    sc = bpy.context.scene
    sc.render.resolution_x, sc.render.resolution_y = w, h
    sc.render.image_settings.file_format = "PNG"
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True)
    print("WROTE", path + ".png")


def blends():
    return [(os.path.join(ROOT, f), os.path.splitext(f)[0])
            for f in sorted(os.listdir(ROOT)) if f.endswith(".blend")]


def main():
    set_engine()
    allb = blends()
    for tag, keys in GROUPS:
        names = [b for b in allb if any(k in b[1] for k in keys)]
        if not names:
            continue
        for ortho in (True, False):
            clear()
            world((0.055, 0.060, 0.070), 1.2)
            placed, total, top = lay_out(names, 0, 1.2)
            if not placed:
                continue
            bpy.ops.mesh.primitive_plane_add(size=max(total * 3, 30), location=(total / 2, 0, -0.002))
            wire_textures()
            camera(total, top, ortho)
            lights(total, top)
            render(os.path.join(OUT, "sheet_%s%s" % (tag, "" if ortho else "_34")))

    for level in (0, 1, 2):
        clear()
        world((0.9, 0.25, 0.25), 1.0)          # red: any hole shows as background
        placed, total, top = lay_out(allb, level, 1.0)
        if not placed:
            continue
        for m in bpy.data.materials:
            m.use_backface_culling = True
        camera(total, top, True)
        lights(total, top)
        render(os.path.join(OUT, "lod%d_backface_check" % level), 2048, 620)


main()

"""Card B2 evidence: render one exported chunk / kitbash FBX with categories colour-coded and collision in wireframe.

    blender -b --factory-startup -P Tools/Blender/b2_chunk_preview.py -- --fbx <file.fbx> --out <image.png> [--chunk 250]

Writes <image>.png (orthographic three-quarter view) and <image>_top.png (orthographic top view). Render meshes use
LOD0 only; COL_ (non-convex) collision is drawn as magenta wire, UBX_/UCX_ (box / convex) as cyan wire. With --chunk N a
black N x N m outline marks the chunk footprint (chunk pivot = south-west corner at the origin).
"""
import math
import os
import sys

import bpy
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
FBX = argv[argv.index("--fbx") + 1]
OUT = argv[argv.index("--out") + 1]
CHUNK = float(argv[argv.index("--chunk") + 1]) if "--chunk" in argv else 0.0
COLOURS = {"Road_Surface": (0.22, 0.22, 0.26, 1), "Sidewalk": (0.78, 0.78, 0.74, 1), "Building": (0.9, 0.55, 0.25, 1),
           "Prop": (0.2, 0.45, 0.95, 1), "Terrain": (0.35, 0.6, 0.25, 1), "Vegetation": (0.1, 0.75, 0.2, 1)}
COL_NONCONVEX = (1.0, 0.1, 0.8, 1)
COL_CONVEX = (0.0, 0.85, 0.95, 1)


def colour_for(name):
    if "markingwhite" in name.lower():
        return (0.97, 0.97, 0.97, 1)
    if "markingyellow" in name.lower():
        return (0.95, 0.8, 0.1, 1)
    return next((c for k, c in COLOURS.items() if k in name), (0.9, 0.55, 0.25, 1) if "VC_KB_" in name else (0.6, 0.6, 0.6, 1))


bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=FBX)
bpy.context.view_layer.update()  # evaluate the FBX root rotation (Y-up file) before reading world bounds
lo = Vector((math.inf,) * 3)
hi = Vector((-math.inf,) * 3)
counts = {}
for o in list(bpy.data.objects):
    if o.type != "MESH":
        continue
    n = o.name
    if "_LOD1" in n or "_LOD2" in n:
        o.hide_render = True
        o.hide_set(True)
        continue
    for v in o.data.vertices:
        w = o.matrix_world @ v.co
        lo = Vector(map(min, lo, w))
        hi = Vector(map(max, hi, w))
    if n.startswith(("COL_", "UCX_", "UBX_")):
        # Workbench draws display_type WIRE as solid, so collision becomes real wire geometry.
        wf = o.modifiers.new("wire", "WIREFRAME")
        wf.thickness = 0.3
        wf.use_replace = True
        o.color = COL_NONCONVEX if n.startswith("COL_") else COL_CONVEX
        key = n[:4]
    else:
        o.color = colour_for(n)
        key = next((k for k in COLOURS if k in n), "other")
    counts[key] = counts.get(key, 0) + 1

scene = bpy.context.scene
if CHUNK > 0:
    # Chunk footprint outline at ground level (thin wire square, black).
    me = bpy.data.meshes.new("chunk_outline")
    me.from_pydata([(0, 0, 0), (CHUNK, 0, 0), (CHUNK, CHUNK, 0), (0, CHUNK, 0)], [], [(0, 1, 2, 3)])
    ob = bpy.data.objects.new("ChunkOutline", me)
    scene.collection.objects.link(ob)
    wf = ob.modifiers.new("wire", "WIREFRAME")
    wf.thickness = 0.8
    wf.use_replace = True
    ob.color = (0.05, 0.05, 0.05, 1)
    lo = Vector(map(min, lo, (0, 0, 0)))
    hi = Vector(map(max, hi, (CHUNK, CHUNK, 0)))

scene.world = bpy.data.worlds.new("bg")
scene.world.color = (0.86, 0.87, 0.9)
scene.render.engine = "BLENDER_WORKBENCH"
scene.display.shading.color_type = "OBJECT"
scene.display.shading.light = "STUDIO"
scene.display.shading.show_cavity = True
scene.display.shading.show_object_outline = True
scene.render.resolution_x, scene.render.resolution_y = 1600, 900
centre = (lo + hi) / 2
diag = (hi - lo).length
print("[B2] bounds", tuple(round(x, 1) for x in lo), tuple(round(x, 1) for x in hi), "diag", round(diag, 1), "counts", counts)

cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
scene.collection.objects.link(cam)
cam.data.type = "ORTHO"
cam.data.clip_start = 0.1
cam.data.clip_end = diag * 10
scene.camera = cam


def legend(scale):
    """Text rows parented to the orthographic camera, top-left of the frame."""
    for o in [o for o in bpy.data.objects if o.name.startswith("legend")]:
        bpy.data.objects.remove(o)
    rows = [("Road_Surface", COLOURS["Road_Surface"]), ("Road marking", (0.97, 0.97, 0.97, 1)),
            ("Sidewalk", COLOURS["Sidewalk"]), ("Building", COLOURS["Building"]), ("Prop", COLOURS["Prop"]),
            ("COL_ collision (non-convex)", COL_NONCONVEX), ("UBX_/UCX_ collision (box/convex)", COL_CONVEX)]
    half_w, half_h = scale / 2, scale / 2 * 9 / 16
    size = scale * 0.022
    for i, (label, col) in enumerate(rows):
        cu = bpy.data.curves.new(f"legend{i}", "FONT")
        cu.body = "■ " + label
        cu.size = size
        t = bpy.data.objects.new(f"legend{i}", cu)
        scene.collection.objects.link(t)
        t.parent = cam
        t.location = (-half_w + size * 0.8, half_h - size * (1.6 + 1.35 * i), -1.0)
        t.color = col if label != "Road marking" else (0.45, 0.45, 0.45, 1)


def shoot(direction, scale, path):
    d = Vector(direction).normalized()
    cam.location = centre + d * diag * 2
    # Straight down: fix the roll so north (+Y, = Unity +Z) is up in the image; track_quat picks an arbitrary roll there.
    cam.rotation_euler = (0.0, 0.0, 0.0) if abs(d.z) > 0.999 else (-d).to_track_quat("-Z", "Y").to_euler()
    cam.data.ortho_scale = scale
    # Shift the subject right so the legend (top-left) never covers geometry.
    right = cam.rotation_euler.to_matrix() @ Vector((1.0, 0.0, 0.0))
    cam.location -= right * scale * 0.14
    legend(scale)
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    print("[B2] preview", path)


os.makedirs(os.path.dirname(os.path.abspath(OUT)), exist_ok=True)
foot = max(hi.x - lo.x, hi.y - lo.y)
# Three-quarter view from the south-west, 40 degrees above the horizon.
shoot((-0.55, -0.83, 0.85), foot * 1.75, OUT)
root, ext = os.path.splitext(OUT)
shoot((0.0, 0.0, 1.0), foot * 1.9, root + "_top" + ext)

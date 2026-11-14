"""Cross-billboard impostors for the Veyra trees (foliage/peds session, 2026-11-14).

Headless:
  set TEMP=E:\\Temp & "D:/New folder/blender.exe" -b --factory-startup -P Tools/Blender/vc_tree_impostors.py -- --out E:/VeyraCity

Rebuilds each tree's LOD0 with vc_trees.py (same seeds, so the same tree as VC_Tree_*.fbx), textures it with the atlas
Unity uses (Assets/_Veyra/Art/Textures/Vegetation/T_Foliage_Atlas.png, T_Bark.png) and renders it orthographically from
two sides (the front, Blender -Y, and the left side, Blender +X):
  - albedo (emission = texture colour x the Unity material tint, sRGB PNG, alpha cut at 0.45 like the material),
  - normals in camera space (Raw PNG, tangent space of the billboard quad: x right, y up, z towards the viewer).
Writes
  Assets/_Veyra/Art/Textures/Vegetation/T_Tree_Impostor_Albedo.png  (2048 x 2048, 4 x 4 tiles of 512, colour bled into
                                                                     the transparent texels so mips do not halo)
  Assets/_Veyra/Art/Textures/Vegetation/T_Tree_Impostor_Normal.png
  Assets/_Veyra/Data/Derived/tree_impostors.json   per tree: ortho size, height, tile UV rects (read by Editor/FoliageUpgrade.cs)
  Logs/Foliage/impostors.txt
Everything is generated: no downloaded content.
"""
import json
import math
import os
import sys

import bpy
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)


def arg(name, default):
    a = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    return a[a.index(name) + 1] if name in a and a.index(name) + 1 < len(a) else default


ROOT = arg("--out", "E:/VeyraCity")
TEX_DIR = os.path.join(ROOT, "Assets/_Veyra/Art/Textures/Vegetation")
DATA = os.path.join(ROOT, "Assets/_Veyra/Data/Derived/tree_impostors.json")
LOG = os.path.join(ROOT, "Logs/Foliage/impostors.txt")
TILE = 512
GRID = 4

# vc_trees.py without its main() call: the tree builders, atlas quadrants and Builder
_src = open(os.path.join(HERE, "vc_trees.py"), encoding="utf-8").read().rsplit("\nmain()", 1)[0]
T = {"__name__": "vc_trees_lib", "__file__": os.path.join(HERE, "vc_trees.py")}
exec(compile(_src, "vc_trees.py", "exec"), T)

# (model, builder, linear foliage tint: M_Tree_Foliage white; M_Tree_Foliage_B _Tint = (1.12, 1.08, 0.72) gamma -> linear)
TREES = [
    ("VC_Tree_Broadleaf", lambda: T["broadleaf"](0), (1.0, 1.0, 1.0)),
    ("VC_Tree_Broadleaf2", lambda: T["broadleaf"](0, seed=29, spread=0.72, zsq=0.8, crownz=7.0), (1.283, 1.185, 0.486)),
    ("VC_Tree_Pine", lambda: T["pine"](0), (1.0, 1.0, 1.0)),
    ("VC_Tree_Palm", lambda: T["palm"](0), (1.0, 1.0, 1.0)),
    ("VC_Tree_Bush", lambda: T["bush"](0), (1.0, 1.0, 1.0)),
]


def material(name, image, tint, mode):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nt = mat.node_tree
    for n in list(nt.nodes):
        nt.nodes.remove(n)
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = image
    tex.interpolation = "Closest" if mode == "normal" else "Linear"
    emit = nt.nodes.new("ShaderNodeEmission")
    if mode == "albedo":
        mul = nt.nodes.new("ShaderNodeMix")
        mul.data_type = "RGBA"
        mul.blend_type = "MULTIPLY"
        mul.inputs[0].default_value = 1.0
        nt.links.new(tex.outputs["Color"], mul.inputs[6])
        mul.inputs[7].default_value = (tint[0], tint[1], tint[2], 1.0)
        nt.links.new(mul.outputs[2], emit.inputs["Color"])
    else:
        geo = nt.nodes.new("ShaderNodeNewGeometry")
        vt = nt.nodes.new("ShaderNodeVectorTransform")
        vt.vector_type = "NORMAL"
        vt.convert_from = "WORLD"
        vt.convert_to = "CAMERA"
        nt.links.new(geo.outputs["Normal"], vt.inputs["Vector"])
        # Blender camera space looks down -Z: flip z so +z points at the viewer; flip x/y? camera x right, y up already
        sep = nt.nodes.new("ShaderNodeSeparateXYZ")
        nt.links.new(vt.outputs["Vector"], sep.inputs[0])
        comb = nt.nodes.new("ShaderNodeCombineXYZ")
        nt.links.new(sep.outputs[0], comb.inputs[0])
        nt.links.new(sep.outputs[1], comb.inputs[1])
        neg = nt.nodes.new("ShaderNodeMath")
        neg.operation = "MULTIPLY"
        neg.inputs[1].default_value = -1.0
        nt.links.new(sep.outputs[2], neg.inputs[0])
        nt.links.new(neg.outputs[0], comb.inputs[2])
        # two-sided cards: a back face shows the flipped normal (as the Unity shader does)
        flip = nt.nodes.new("ShaderNodeMix")
        flip.data_type = "VECTOR"
        nt.links.new(geo.outputs["Backfacing"], flip.inputs[0])
        nt.links.new(comb.outputs[0], flip.inputs[4])
        negv = nt.nodes.new("ShaderNodeVectorMath")
        negv.operation = "SCALE"
        negv.inputs["Scale"].default_value = -1.0
        nt.links.new(comb.outputs[0], negv.inputs[0])
        nt.links.new(negv.outputs[0], flip.inputs[5])
        enc = nt.nodes.new("ShaderNodeVectorMath")
        enc.operation = "MULTIPLY_ADD"
        nt.links.new(flip.outputs[1], enc.inputs[0])
        enc.inputs[1].default_value = (0.5, 0.5, 0.5)
        enc.inputs[2].default_value = (0.5, 0.5, 0.5)
        nt.links.new(enc.outputs[0], emit.inputs["Color"])
    # alpha clip at 0.45 (the Unity cutoff)
    step = nt.nodes.new("ShaderNodeMath")
    step.operation = "GREATER_THAN"
    step.inputs[1].default_value = 0.45
    nt.links.new(tex.outputs["Alpha"], step.inputs[0])
    transp = nt.nodes.new("ShaderNodeBsdfTransparent")
    mix = nt.nodes.new("ShaderNodeMixShader")
    nt.links.new(step.outputs[0], mix.inputs[0])
    nt.links.new(transp.outputs[0], mix.inputs[1])
    nt.links.new(emit.outputs[0], mix.inputs[2])
    nt.links.new(mix.outputs[0], out.inputs["Surface"])
    return mat


def setup_render(scene, mode):
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = 24 if mode == "albedo" else 8
    scene.cycles.use_denoising = False
    scene.cycles.max_bounces = 0
    scene.cycles.transparent_max_bounces = 16
    scene.cycles.filter_width = 1.0 if mode == "albedo" else 0.01
    scene.render.film_transparent = True
    scene.render.resolution_x = scene.render.resolution_y = TILE
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"
    scene.render.image_settings.color_depth = "8"
    vs = scene.view_settings
    vs.view_transform = "Standard" if mode == "albedo" else "Raw"
    vs.look = "None"
    vs.exposure = 0.0
    vs.gamma = 1.0
    scene.world = scene.world or bpy.data.worlds.new("W")
    scene.world.use_nodes = True
    bg = next(n for n in scene.world.node_tree.nodes if n.type == "BACKGROUND")
    bg.inputs["Strength"].default_value = 0.0


def render_view(scene, cam, path):
    scene.camera = cam
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    img = bpy.data.images.load(path, check_existing=False)
    arr = np.array(img.pixels[:], dtype=np.float32).reshape(TILE, TILE, 4)
    bpy.data.images.remove(img)
    return arr


def bleed(rgba, iterations=10):
    """Spreads colour from opaque texels into transparent ones (alpha unchanged) so mip levels do not darken edges."""
    rgb = rgba[..., :3].copy()
    known = rgba[..., 3] > 0.01
    for _ in range(iterations):
        acc = np.zeros_like(rgb)
        cnt = np.zeros(known.shape, np.float32)
        for dy in (-1, 0, 1):
            for dx in (-1, 0, 1):
                if dx == 0 and dy == 0:
                    continue
                sk = np.roll(np.roll(known, dy, 0), dx, 1)
                sr = np.roll(np.roll(rgb, dy, 0), dx, 1)
                acc += sr * sk[..., None]
                cnt += sk
        grow = (~known) & (cnt > 0)
        rgb[grow] = acc[grow] / cnt[grow][:, None]
        known = known | grow
    out = rgba.copy()
    out[..., :3] = rgb
    return out


def save_png(arr, path, colorspace):
    h, w, _ = arr.shape
    img = bpy.data.images.new(os.path.basename(path), w, h, alpha=True, float_buffer=False)
    img.colorspace_settings.name = colorspace
    img.pixels = arr.reshape(-1).tolist()
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()
    bpy.data.images.remove(img)


def main():
    os.makedirs(os.path.dirname(DATA), exist_ok=True)
    os.makedirs(os.path.dirname(LOG), exist_ok=True)
    tmp = os.path.join(ROOT, "Logs/Foliage/impostor_tiles")
    os.makedirs(tmp, exist_ok=True)
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    scene = bpy.context.scene
    foliage_img = bpy.data.images.load(os.path.join(TEX_DIR, "T_Foliage_Atlas.png"))
    bark_img = bpy.data.images.load(os.path.join(TEX_DIR, "T_Bark.png"))
    albedo = np.zeros((TILE * GRID, TILE * GRID, 4), np.float32)
    normal = np.zeros((TILE * GRID, TILE * GRID, 4), np.float32)
    normal[..., 0:3] = (0.5, 0.5, 1.0)
    cam_data = bpy.data.cameras.new("ImpCam")
    cam_data.type = "ORTHO"
    cam = bpy.data.objects.new("ImpCam", cam_data)
    scene.collection.objects.link(cam)
    meta = dict(tile=TILE, grid=GRID, views=["front: Blender -Y = Unity +Z side, image right = Unity -X",
                                              "side: Blender +X = Unity -X side, image right = Unity -Z"], trees=[])
    lines = []
    for i, (model, build, tint) in enumerate(TREES):
        obj = build().to_object(model)   # slots 0 MI_Bark, 1 MI_Foliage (faces keep their indices)
        bounds = [obj.matrix_world @ v.co for v in obj.data.vertices]
        xs = [b.x for b in bounds]
        ys = [b.y for b in bounds]
        zs = [b.z for b in bounds]
        height = max(zs)
        half_w = max(max(abs(x) for x in xs), max(abs(y) for y in ys))
        size = max(height, 2 * half_w) * 1.03
        cam_data.ortho_scale = size
        cam_data.clip_start, cam_data.clip_end = 0.1, 200
        tiles = []
        for mode in ("albedo", "normal"):
            setup_render(scene, mode)
            obj.data.materials[0] = material(f"Bark_{mode}", bark_img, (1, 1, 1), mode)
            obj.data.materials[1] = material(f"Leaf_{mode}", foliage_img, tint, mode)
            for view, (loc, rot) in enumerate((((0, -60, size / 2), (90, 0, 0)), ((60, 0, size / 2), (90, 0, 90)))):
                cam.location = loc
                cam.rotation_euler = [math.radians(a) for a in rot]
                arr = render_view(scene, cam, os.path.join(tmp, f"{model}_{view}_{mode}.png"))
                t = 2 * i + view
                col, row = t % GRID, t // GRID
                y0, x0 = row * TILE, col * TILE
                if mode == "albedo":
                    albedo[y0:y0 + TILE, x0:x0 + TILE] = bleed(arr)
                    cover = float((arr[..., 3] > 0.5).mean())
                    lum = arr[..., :3][arr[..., 3] > 0.5]
                    lin = np.where(lum <= 0.04045, lum / 12.92, ((lum + 0.055) / 1.055) ** 2.4)
                    y = lin @ np.array([0.2126, 0.7152, 0.0722], np.float32)
                    tiles.append(dict(view=view, u0=col / GRID, v0=row / GRID, u1=(col + 1) / GRID, v1=(row + 1) / GRID))
                    lines.append(f"{model} view {view}: coverage {cover:.2f}, mean linear albedo {float(y.mean()) if len(y) else 0:.3f}")
                else:
                    a = arr.copy()
                    a[..., 3] = 1.0
                    mask = arr[..., 3] > 0.5
                    a[~mask, 0:3] = (0.5, 0.5, 1.0)
                    normal[y0:y0 + TILE, x0:x0 + TILE] = a
        meta["trees"].append(dict(model=model, size=round(size, 4), height=round(height, 4), halfWidth=round(half_w, 4), tiles=tiles))
        bpy.data.objects.remove(obj, do_unlink=True)
    save_png(albedo, os.path.join(TEX_DIR, "T_Tree_Impostor_Albedo.png"), "sRGB")
    save_png(normal, os.path.join(TEX_DIR, "T_Tree_Impostor_Normal.png"), "Non-Color")
    with open(DATA, "w", encoding="utf-8") as f:
        json.dump(meta, f, indent=1)
    with open(LOG, "w", encoding="utf-8") as f:
        f.write("\n".join(lines) + "\n")
    print("\n".join(lines))


main()

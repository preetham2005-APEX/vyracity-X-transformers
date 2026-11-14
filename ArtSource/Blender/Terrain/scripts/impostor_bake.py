"""Bake the far-tree impostor atlas (BUG-117) from the existing LOD0 vegetation meshes.

For each species, two orthographic views (0 deg and 90 deg azimuth) are rendered with the materials
temporarily swapped for Emission, so the render IS the data rather than a lit picture:

  * albedo pass - Emission(base colour texture), mixed against a Transparent BSDF by the texture's
    alpha, on a transparent film. The EXR therefore holds linear albedo + coverage.
  * normal pass - Emission(encoded camera-space normal), same alpha mix. For a camera-facing card,
    camera space IS the card's tangent space (right, up, toward-camera), so the baked map drops
    straight into a URP Lit normal slot with no conversion. OpenGL +Y up.

Everything is read back from 32-bit EXR and written out by the project's own PNG writer, so no view
transform or colour management ever touches the data.

Run: blender -b --python impostor_bake.py
"""
import json
import math
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
VEG1 = os.path.join(os.path.dirname(ROOT), "Vegetation")
VEG2 = os.path.join(os.path.dirname(ROOT), "Vegetation2")
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(VEG1, "scripts"))

import bpy  # noqa: E402
import numpy as np  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402

from atlas_paint import srgb, write_png  # noqa: E402

OUT = os.path.join(ROOT, "textures")
TMP = os.path.join(ROOT, "scripts", "_bake_tmp")
ATLAS = 2048
VIEWS = (0.0, 90.0)
TOP_SCALE = 0.5          # the top view is seen from further off; half the side resolution

# species -> (blend file, LOD0 object name, atlas tile height in px)
SPECIES = [
    ("Redwood_46m", os.path.join(VEG2, "VC_Veg2_Redwood.blend"), "VC_Tree_Redwood_46m_LOD0", 1024),
    ("Broadleaf_Spreading", os.path.join(VEG1, "VC_Veg_Broadleaf.blend"),
     "VC_Tree_Broadleaf_Spreading_LOD0", 512),
    ("Pine_Tall", os.path.join(VEG1, "VC_Veg_Pine.blend"), "VC_Tree_Pine_Tall_LOD0", 512),
    ("Palm_Royal_16m", os.path.join(VEG1, "VC_Veg_Palm_Royal.blend"),
     "VC_Palm_Royal_16m_LOD0", 512),
    ("Spruce_Alpine", os.path.join(VEG2, "VC_Veg2_Alpine.blend"), "VC_Tree_Spruce_Alpine_LOD0", 512),
    # small, and seen from a moving car on a boulevard, so it gets a nearer switch and a tile
    # sized to match rather than the forest-species 512
    ("Palm_Fan_Tall", os.path.join(VEG1, "VC_Veg_Palm_Fan.blend"), "VC_Palm_Fan_Tall_LOD0", 256),
]


# ---------------------------------------------------------------- bake materials
def _base_image(mat):
    """The image feeding Base Color, found by node type (never by localised node name)."""
    if not mat or not mat.use_nodes:
        return None
    nt = mat.node_tree
    bsdf = next((n for n in nt.nodes if n.type == "BSDF_PRINCIPLED"), None)
    if bsdf is None:
        return None
    inp = bsdf.inputs["Base Color"]
    if inp.is_linked:
        src = inp.links[0].from_node
        if src.type == "TEX_IMAGE":
            return src.image
    return next((n.image for n in nt.nodes if n.type == "TEX_IMAGE" and n.image), None)


def _make_bake_mat(src, mode):
    name = "_BAKE_%s_%s" % (mode, src.name)
    m = bpy.data.materials.get(name)
    if m is not None:
        return m
    img = _base_image(src)
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    for n in list(nt.nodes):
        nt.nodes.remove(n)
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    mix = nt.nodes.new("ShaderNodeMixShader")
    trans = nt.nodes.new("ShaderNodeBsdfTransparent")
    emis = nt.nodes.new("ShaderNodeEmission")
    emis.inputs["Strength"].default_value = 1.0
    nt.links.new(trans.outputs[0], mix.inputs[1])
    nt.links.new(emis.outputs[0], mix.inputs[2])
    nt.links.new(mix.outputs[0], out.inputs["Surface"])

    tex = None
    if img is not None:
        tex = nt.nodes.new("ShaderNodeTexImage")
        tex.image = img
        tex.interpolation = "Linear"
        nt.links.new(tex.outputs["Alpha"], mix.inputs["Fac"])
    else:
        mix.inputs["Fac"].default_value = 1.0

    if mode == "albedo":
        if tex is not None:
            nt.links.new(tex.outputs["Color"], emis.inputs["Color"])
        else:
            emis.inputs["Color"].default_value = (0.5, 0.5, 0.5, 1.0)
    else:
        geo = nt.nodes.new("ShaderNodeNewGeometry")
        vt = nt.nodes.new("ShaderNodeVectorTransform")
        vt.vector_type = "NORMAL"
        vt.convert_from = "WORLD"
        vt.convert_to = "CAMERA"
        enc = nt.nodes.new("ShaderNodeVectorMath")
        enc.operation = "MULTIPLY_ADD"
        enc.inputs[1].default_value = (0.5, 0.5, 0.5)
        enc.inputs[2].default_value = (0.5, 0.5, 0.5)
        nt.links.new(geo.outputs["Normal"], vt.inputs["Vector"])
        nt.links.new(vt.outputs["Vector"], enc.inputs[0])
        nt.links.new(enc.outputs["Vector"], emis.inputs["Color"])
    return m


def swap_materials(ob, mode):
    original = [s.material for s in ob.material_slots]
    for s in ob.material_slots:
        if s.material:
            s.material = _make_bake_mat(s.material, mode)
    return original


def restore_materials(ob, original):
    for s, m in zip(ob.material_slots, original):
        s.material = m


# ---------------------------------------------------------------- render
def set_cycles():
    sc = bpy.context.scene
    ids = [i.identifier for i in bpy.types.RenderSettings.bl_rna.properties["engine"].enum_items]
    if "CYCLES" in ids:
        try:
            sc.render.engine = "CYCLES"
        except TypeError:
            pass
    try:
        sc.cycles.samples = 16
        sc.cycles.use_denoising = False
        sc.cycles.max_bounces = 0
        sc.cycles.transparent_max_bounces = 64
    except AttributeError:
        pass
    sc.render.film_transparent = True
    sc.render.image_settings.file_format = "OPEN_EXR"
    sc.render.image_settings.color_mode = "RGBA"
    sc.render.image_settings.color_depth = "32"
    try:
        sc.render.image_settings.exr_codec = "ZIP"
    except (AttributeError, TypeError):
        pass


def isolate(obj_name):
    """Put the target LOD0 at the origin with an identity transform and hide everything else."""
    ob = bpy.data.objects.get(obj_name)
    if ob is None:
        raise RuntimeError("object not found: " + obj_name)
    ob.parent = None
    ob.matrix_world = Matrix.Identity(4)
    for o in bpy.data.objects:
        o.hide_render = (o is not ob)
    return ob


def world_bbox(ob):
    """From the evaluated mesh's vertices. ob.bound_box is cached and is stale immediately after a
    reparent, which silently shrank the first bake by up to 0.9 m."""
    bpy.context.view_layer.update()
    deps = bpy.context.evaluated_depsgraph_get()
    ev = ob.evaluated_get(deps)
    me = ev.to_mesh()
    mw = ob.matrix_world
    lo = Vector((1e9, 1e9, 1e9))
    hi = Vector((-1e9, -1e9, -1e9))
    for v in me.vertices:
        w = mw @ v.co
        lo = Vector((min(lo.x, w.x), min(lo.y, w.y), min(lo.z, w.z)))
        hi = Vector((max(hi.x, w.x), max(hi.y, w.y), max(hi.z, w.z)))
    ev.to_mesh_clear()
    return lo, hi


def place_camera(lo, hi, az_deg, pad=1.02):
    w = max(hi.x - lo.x, hi.y - lo.y)
    h = hi.z - lo.z
    cz = (hi.z + lo.z) * 0.5
    ortho = max(w, h) * pad
    cam = bpy.data.objects.get("_BAKE_CAM")
    if cam is None:
        cam = bpy.data.objects.new("_BAKE_CAM", bpy.data.cameras.new("_BAKE_CAM"))
        bpy.context.scene.collection.objects.link(cam)
    cam.data.type = "ORTHO"
    cam.data.ortho_scale = ortho
    a = math.radians(az_deg)
    d = max(w, h) * 4.0 + 10.0
    # a camera at rotation (90, 0, a) looks along (-sin a, cos a, 0), so it must stand at
    # -look * d for that ray to pass through the object
    cam.location = (math.sin(a) * d, -math.cos(a) * d, cz)
    cam.rotation_euler = (math.radians(90), 0.0, a)
    bpy.context.scene.camera = cam
    return w, h, cz, ortho


def place_camera_top(lo, hi, pad=1.02):
    """Straight down, centred on the ORIGIN so the top card shares the pivot with the side cards.
    Square frame, sized to the largest signed extent."""
    half = max(abs(lo.x), abs(hi.x), abs(lo.y), abs(hi.y))
    ortho = 2.0 * half * pad
    cam = bpy.data.objects.get("_BAKE_CAM")
    if cam is None:
        cam = bpy.data.objects.new("_BAKE_CAM", bpy.data.cameras.new("_BAKE_CAM"))
        bpy.context.scene.collection.objects.link(cam)
    cam.data.type = "ORTHO"
    cam.data.ortho_scale = ortho
    cam.location = (0.0, 0.0, hi.z + ortho * 2.0 + 10.0)
    cam.rotation_euler = (0.0, 0.0, 0.0)          # looks along -Z
    bpy.context.scene.camera = cam
    return ortho


def render_exr(path, res_x, res_y):
    sc = bpy.context.scene
    # filepath is given complete, extension included: let Blender not append a second one
    sc.render.use_file_extension = False
    sc.render.resolution_x = int(res_x)
    sc.render.resolution_y = int(res_y)
    sc.render.resolution_percentage = 100
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True)


def read_exr(path):
    im = bpy.data.images.load(path)
    px = np.array(im.pixels[:], np.float32).reshape(im.size[1], im.size[0], 4)
    bpy.data.images.remove(im)
    return px[::-1]          # Blender images are bottom-up


# ---------------------------------------------------------------- shelf packer
def pack(tiles, width=ATLAS):
    """tiles: [(key, w, h)] -> {key: (x, y)}, total height. Tallest shelf first."""
    order = sorted(tiles, key=lambda t: -t[2])
    placed, shelves = {}, []
    for key, w, h in order:
        for sh in shelves:
            if sh["h"] >= h and sh["x"] + w <= width:
                placed[key] = (sh["x"], sh["y"])
                sh["x"] += w
                break
        else:
            y = sum(s["h"] for s in shelves)
            shelves.append({"y": y, "x": w, "h": h})
            placed[key] = (0, y)
    return placed, sum(s["h"] for s in shelves)


# ---------------------------------------------------------------- main
def main():
    os.makedirs(OUT, exist_ok=True)
    os.makedirs(TMP, exist_ok=True)
    baked = {}

    for name, blend, obj_name, tile_h in SPECIES:
        bpy.ops.wm.open_mainfile(filepath=blend)
        set_cycles()
        ob = isolate(obj_name)
        lo, hi = world_bbox(ob)
        for vi, az in enumerate(VIEWS):
            w_m, h_m, cz, ortho = place_camera(lo, hi, az)
            tile_w = max(4, int(round(tile_h * w_m / max(h_m, 1e-6) / 4.0)) * 4)
            # ortho_scale governs the LARGER render dimension, so work out which
            if tile_w >= tile_h:
                frame_w, frame_h = ortho, ortho * tile_h / float(tile_w)
            else:
                frame_h, frame_w = ortho, ortho * tile_w / float(tile_h)
            key = "%s_v%d" % (name, vi)
            for mode in ("albedo", "normal"):
                orig = swap_materials(ob, mode)
                try:
                    render_exr(os.path.join(TMP, "%s_%s.exr" % (key, mode)), tile_w, tile_h)
                finally:
                    restore_materials(ob, orig)
            baked[key] = dict(species=name, view=vi, w=tile_w, h=tile_h,
                              size_m=[round(w_m, 3), round(h_m, 3)],
                              frame_m=[round(frame_w, 4), round(frame_h, 4)],
                              centre_z=round(cz, 4))
            if vi == 0:
                side = dict(cz=cz, frame_h=frame_h, tile_h=tile_h,
                            exr=os.path.join(TMP, "%s_albedo.exr" % key))

        # --- third card: a top view, for looking down on a forest from a clifftop road
        ortho = place_camera_top(lo, hi)
        top_h = max(64, int(tile_h * TOP_SCALE))
        for mode in ("albedo", "normal"):
            orig = swap_materials(ob, mode)
            try:
                render_exr(os.path.join(TMP, "%s_top_%s.exr" % (name, mode)), top_h, top_h)
            finally:
                restore_materials(ob, orig)

        # canopy height = alpha-weighted centroid of the SIDE view, so the flat card sits in the
        # crown rather than at half height (a bare-trunked conifer's mass is far above centre)
        a0 = read_exr(side["exr"])
        # weight by coverage SQUARED: a crown row is metres wide and a trunk row centimetres, so
        # plain alpha weighting drags the centroid down the trunk - it put the royal palm's card at
        # 11.7 m, below a crown that starts at 14 m. Squaring makes wide rows dominate.
        rows = np.clip(a0[..., 3], 0, 1).sum(1) ** 2
        if rows.sum() > 1e-6:
            mean_row = float((np.arange(len(rows)) * rows).sum() / rows.sum())
            canopy_z = side["cz"] + side["frame_h"] * (0.5 - (mean_row + 0.5) / side["tile_h"])
        else:
            canopy_z = side["cz"]
        baked["%s_top" % name] = dict(species=name, view="top", w=top_h, h=top_h,
                                      size_m=[round(ortho, 3), round(ortho, 3)],
                                      frame_m=[round(ortho, 4), round(ortho, 4)],
                                      centre_z=round(float(canopy_z), 4),
                                      canopy_z=round(float(canopy_z), 4))
        print("baked:", name, "(+ top view, canopy at %.2f m)" % canopy_z)

    # ---- pack and compose
    placed, total_h = pack([(k, v["w"], v["h"]) for k, v in baked.items()])
    if total_h > ATLAS:
        raise RuntimeError("impostor atlas overflow: needs %d px of height" % total_h)

    base = np.zeros((ATLAS, ATLAS, 4), np.float32)
    nrm = np.zeros((ATLAS, ATLAS, 4), np.float32)
    nrm[..., :3] = 0.5
    nrm[..., 2] = 1.0
    uvs = {}
    for key, info in baked.items():
        x, y = placed[key]
        w, h = info["w"], info["h"]
        a = read_exr(os.path.join(TMP, "%s_albedo.exr" % key))
        n = read_exr(os.path.join(TMP, "%s_normal.exr" % key))
        alpha = np.clip(a[..., 3:4], 0, 1)
        # un-premultiply: the film composites emission against a transparent background
        rgb = np.where(alpha > 1e-4, a[..., :3] / np.maximum(alpha, 1e-4), 0.0)
        nn = np.where(alpha > 1e-4, n[..., :3] / np.maximum(alpha, 1e-4), 0.5)
        hard = np.where(alpha > 0.5, 1.0, np.where(alpha > 0.22, alpha, 0.0))
        base[y:y + h, x:x + w, :3] = rgb
        base[y:y + h, x:x + w, 3:4] = hard
        nrm[y:y + h, x:x + w, :3] = np.clip(nn, 0, 1)
        nrm[y:y + h, x:x + w, 3] = 1.0
        uvs[key] = dict(uv=[x / ATLAS, 1.0 - (y + h) / ATLAS, (x + w) / ATLAS, 1.0 - y / ATLAS],
                        px=[x, y, x + w, y + h], **info)

    # colour bleed into the transparent margin, same as the foliage atlases
    cov = base[..., 3] > 0.02
    rgbd = base[..., :3].copy()
    for _ in range(10):
        miss = ~cov
        if not miss.any():
            break
        acc = np.zeros_like(rgbd)
        cnt = np.zeros(cov.shape, np.float32)
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            acc += np.roll(rgbd, (dy, dx), (0, 1)) * np.roll(cov, (dy, dx), (0, 1))[..., None]
            cnt += np.roll(cov, (dy, dx), (0, 1)).astype(np.float32)
        fill = miss & (cnt > 0)
        rgbd[fill] = acc[fill] / cnt[fill][..., None]
        cov = cov | fill
    base[..., :3] = rgbd

    write_png(os.path.join(OUT, "T_Veyra_Impostor_Atlas_BaseColor.png"),
              (np.concatenate([srgb(base[..., :3]), base[..., 3:4]], -1) * 255 + 0.5).astype(np.uint8))
    write_png(os.path.join(OUT, "T_Veyra_Impostor_Atlas_Normal.png"),
              (np.clip(nrm, 0, 1) * 255 + 0.5).astype(np.uint8))
    sm = np.full((ATLAS, ATLAS), 0.22, np.float32)     # foliage is matte; one value for the set
    z = np.zeros_like(sm)
    write_png(os.path.join(OUT, "T_Veyra_Impostor_Atlas_MetallicSmoothness.png"),
              (np.stack([z, z, z, sm], -1) * 255 + 0.5).astype(np.uint8))
    with open(os.path.join(OUT, "impostor_uvs.json"), "w") as f:
        json.dump(uvs, f, indent=1)
    print("impostor atlas done: %d tiles, %d px of %d used" % (len(uvs), total_h, ATLAS))


if __name__ == "__main__":
    main()

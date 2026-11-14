"""Vegetation2 bindings: reuse the Vegetation (set 1) geometry library, but point at the set 2
folders, at foliage atlas B, and at the SHARED bark atlas from set 1.

Set 1 is not modified by anything in this folder - it is read only.
"""
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)                                   # ArtSource/Blender/Vegetation2
SET1 = os.path.join(os.path.dirname(ROOT), "Vegetation")       # ArtSource/Blender/Vegetation
sys.path.insert(0, os.path.join(SET1, "scripts"))

import bpy  # noqa: E402

import veg_lib as V1  # noqa: E402
from veg_lib import (MAT_BARK, MAT_FOLIAGE, Builder, apply_and_check, assemble,  # noqa: F401,E402
                     card, crossed_billboard, mix_normal, rng, strip, strip_folded, tube,
                     ubx_box, ucx_trunk)

TEX = os.path.join(ROOT, "textures")            # atlas B lives here
TEX1 = os.path.join(SET1, "textures")           # shared bark atlas lives here
EXPORT = os.path.join(ROOT, "export")
REVIEW = os.path.join(ROOT, "review")

ATLAS_BARK = json.load(open(os.path.join(TEX1, "atlas_uvs.json")))["bark"]
with open(os.path.join(TEX, "atlas_uvs.json")) as _f:
    ATLAS_FOLIAGE = json.load(_f)["foliage"]

FOLIAGE_PREFIX = "T_Veyra_Foliage_Atlas_B"
BARK_PREFIX = "T_Veyra_Bark_Atlas"


def _table(group):
    return ATLAS_BARK if group == "bark" else ATLAS_FOLIAGE


def uv_rect(group, name, inset_texels=0.0, size=None):
    if size is None:
        size = 1024.0 if group == "bark" else 2048.0
    u0, v0, u1, v1 = _table(group)[name]["uv"]
    if inset_texels:
        d = inset_texels / size
        u0, v0, u1, v1 = u0 + d, v0 + d, u1 - d, v1 - d
    return u0, v0, u1, v1


def aspect(group, name):
    return _table(group)[name]["aspect"]


def make_materials():
    """[M_Veyra_Bark (shared with set 1), M_Veyra_Foliage_B]. Idempotent."""
    mats = []
    for name, prefix, folder, clip in (("M_Veyra_Bark", BARK_PREFIX, TEX1, False),
                                       ("M_Veyra_Foliage_B", FOLIAGE_PREFIX, TEX, True)):
        mat = bpy.data.materials.get(name)
        if mat is None:
            mat = bpy.data.materials.new(name)
            mat.use_nodes = True
            b = V1._tex_set(
                mat,
                V1._img(prefix + "_BaseColor", os.path.join(folder, prefix + "_BaseColor.png")),
                V1._img(prefix + "_Normal", os.path.join(folder, prefix + "_Normal.png"), True),
                V1._img(prefix + "_MetSm", os.path.join(folder, prefix + "_MetallicSmoothness.png"), True),
            )
            if clip:
                for attr, val in (("surface_render_method", "DITHERED"),
                                  ("blend_method", "CLIP"), ("shadow_method", "CLIP"),
                                  ("alpha_threshold", 0.5)):
                    try:
                        setattr(mat, attr, val)
                    except (AttributeError, TypeError):
                        pass
                mat.use_backface_culling = False
                b.inputs["Specular IOR Level"].default_value = 0.35
        mats.append(mat)
    return mats


def fresh_scene():
    for ob in list(bpy.data.objects):
        bpy.data.objects.remove(ob, do_unlink=True)
    for me in list(bpy.data.meshes):
        if me.users == 0:
            bpy.data.meshes.remove(me)
    sc = bpy.context.scene
    sc.unit_settings.system = "METRIC"
    sc.unit_settings.scale_length = 1.0
    sc.unit_settings.length_unit = "METERS"
    return make_materials()


def export(parent, filename):
    sys.path.insert(0, r"E:\VeyraCity\Tools\Blender")
    import vc_tools
    os.makedirs(EXPORT, exist_ok=True)
    path = os.path.join(EXPORT, filename)
    vc_tools.vc_export_fbx([parent], path, mode="prop", pivot=parent, path_mode="STRIP")
    return path

"""Terrain kit bindings: reuse the Vegetation geometry library, point at the Terrain folders, and
add the rock and impostor materials.

Vegetation/ and Vegetation2/ are read only from here.
"""
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)                                     # ArtSource/Blender/Terrain
VEG1 = os.path.join(os.path.dirname(ROOT), "Vegetation")
VEG2 = os.path.join(os.path.dirname(ROOT), "Vegetation2")
sys.path.insert(0, os.path.join(VEG1, "scripts"))

import bpy  # noqa: E402

import veg_lib as V1  # noqa: E402
from veg_lib import (MAT_BARK, Builder, apply_and_check, assemble, card, mix_normal,  # noqa: F401,E402
                     rng, tube, ubx_box)

TEX = os.path.join(ROOT, "textures")
TEX_VEG1 = os.path.join(VEG1, "textures")          # shared bark atlas
EXPORT = os.path.join(ROOT, "export")
REVIEW = os.path.join(ROOT, "review")

ROCK_PREFIX = "T_Veyra_Rock"
IMPOSTOR_PREFIX = "T_Veyra_Impostor_Atlas"
BARK_PREFIX = "T_Veyra_Bark_Atlas"

ATLAS_BARK = json.load(open(os.path.join(TEX_VEG1, "atlas_uvs.json")))["bark"]

# material slot indices used by this kit
MAT_ROCK = 0
MAT_BARK_SLOT = 1          # only on the fallen logs
MAT_IMPOSTOR = 0           # impostors carry one material and nothing else


def bark_rect(name, inset_texels=2.0, size=1024.0):
    u0, v0, u1, v1 = ATLAS_BARK[name]["uv"]
    d = inset_texels / size
    return u0 + d, v0 + d, u1 - d, v1 - d


def impostor_rect(table, name):
    return tuple(table[name]["uv"])


def _mat(name, prefix, folder, clip=False, tiling=False):
    mat = bpy.data.materials.get(name)
    if mat is not None:
        return mat
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    b = V1._tex_set(
        mat,
        V1._img(prefix + "_BaseColor", os.path.join(folder, prefix + "_BaseColor.png")),
        V1._img(prefix + "_Normal", os.path.join(folder, prefix + "_Normal.png"), True),
        V1._img(prefix + "_MetSm", os.path.join(folder, prefix + "_MetallicSmoothness.png"), True),
    )
    if tiling:
        for n in mat.node_tree.nodes:
            if n.type == "TEX_IMAGE":
                n.extension = "REPEAT"
    if clip:
        for attr, val in (("surface_render_method", "DITHERED"), ("blend_method", "CLIP"),
                          ("shadow_method", "CLIP"), ("alpha_threshold", 0.5)):
            try:
                setattr(mat, attr, val)
            except (AttributeError, TypeError):
                pass
        mat.use_backface_culling = False
        b.inputs["Specular IOR Level"].default_value = 0.35
    return mat


def rock_materials():
    """[M_Veyra_Rock, M_Veyra_Bark] - slot 0 rock (tiling), slot 1 the SHARED bark atlas."""
    return [_mat("M_Veyra_Rock", ROCK_PREFIX, TEX, tiling=True),
            _mat("M_Veyra_Bark", BARK_PREFIX, TEX_VEG1)]


def impostor_material():
    return [_mat("M_Veyra_Impostor", IMPOSTOR_PREFIX, TEX, clip=True)]


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


def collider_object(name, verts, faces):
    """A collision proxy mesh: no material, never renders (the Veyra model postprocessor strips the
    renderer from COL_/UCX_/UBX_ nodes)."""
    me = bpy.data.meshes.new(name)
    me.from_pydata([tuple(v) for v in verts], [], [list(f) for f in faces])
    me.update()
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    return ob


def export(parent, filename):
    sys.path.insert(0, r"E:\VeyraCity\Tools\Blender")
    import vc_tools
    os.makedirs(EXPORT, exist_ok=True)
    path = os.path.join(EXPORT, filename)
    vc_tools.vc_export_fbx([parent], path, mode="prop", pivot=parent, path_mode="STRIP")
    return path

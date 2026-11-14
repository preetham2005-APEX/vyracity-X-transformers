"""VEYRA CITY vegetation geometry library (Blender 5.2, headless).

Everything is built from explicit quads so triangle counts are exact and predictable, and so each
LOD is AUTHORED rather than decimated (a Decimate collapse on leaf cards shreds the silhouette).

Conventions (CLAUDE.md):
  1 Blender unit = 1 m. Pivot at base centre, trunk base at the local origin, model front = Blender -Y
  (vc_export_fbx mode "prop" turns it so the front faces Unity +Z).
  Materials: slot 0 = M_Veyra_Bark (opaque), slot 1 = M_Veyra_Foliage (alpha clip). Two materials for
  the entire vegetation set.
  Leaf-card normals are biased toward the canopy normal so foliage shades as a volume.
"""
import json
import math
import os
import random

import bpy
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
TEX = os.path.join(ROOT, "textures")
EXPORT = os.path.join(ROOT, "export")
REVIEW = os.path.join(ROOT, "review")

with open(os.path.join(TEX, "atlas_uvs.json")) as _f:
    ATLAS = json.load(_f)

MAT_BARK, MAT_FOLIAGE = 0, 1


def uv_rect(group, name, inset_texels=0.0, size=2048.0):
    u0, v0, u1, v1 = ATLAS[group][name]["uv"]
    if inset_texels:
        d = inset_texels / size
        u0, v0, u1, v1 = u0 + d, v0 + d, u1 - d, v1 - d
    return u0, v0, u1, v1


def aspect(group, name):
    return ATLAS[group][name]["aspect"]


# ---------------------------------------------------------------- materials
def _img(name, path, non_color=False):
    im = bpy.data.images.get(name)
    if im is None:
        im = bpy.data.images.load(path, check_existing=True)
        im.name = name
    im.colorspace_settings.name = "Non-Color" if non_color else "sRGB"
    return im


def _tex_set(mat, base, normal, metsm):
    nt = mat.node_tree
    bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    out = next(n for n in nt.nodes if n.type == "OUTPUT_MATERIAL")
    bsdf.location = (-200, 0)
    out.location = (120, 0)

    tb = nt.nodes.new("ShaderNodeTexImage")
    tb.image = base
    tb.location = (-800, 220)
    tb.interpolation = "Linear"
    nt.links.new(tb.outputs["Color"], bsdf.inputs["Base Color"])
    nt.links.new(tb.outputs["Alpha"], bsdf.inputs["Alpha"])

    tn = nt.nodes.new("ShaderNodeTexImage")
    tn.image = normal
    tn.location = (-800, -80)
    nm = nt.nodes.new("ShaderNodeNormalMap")
    nm.location = (-480, -80)
    nt.links.new(tn.outputs["Color"], nm.inputs["Color"])
    nt.links.new(nm.outputs["Normal"], bsdf.inputs["Normal"])

    tm = nt.nodes.new("ShaderNodeTexImage")
    tm.image = metsm
    tm.location = (-800, -400)
    inv = nt.nodes.new("ShaderNodeMath")
    inv.operation = "SUBTRACT"
    inv.inputs[0].default_value = 1.0
    inv.location = (-480, -400)
    nt.links.new(tm.outputs["Alpha"], inv.inputs[1])        # smoothness -> roughness for preview
    nt.links.new(inv.outputs[0], bsdf.inputs["Roughness"])
    nt.links.new(tm.outputs["Color"], bsdf.inputs["Metallic"])
    return bsdf


def make_materials():
    """M_Veyra_Bark (opaque) and M_Veyra_Foliage (alpha clip). Idempotent."""
    mats = []
    for name, prefix, clip in (("M_Veyra_Bark", "T_Veyra_Bark_Atlas", False),
                               ("M_Veyra_Foliage", "T_Veyra_Foliage_Atlas", True)):
        mat = bpy.data.materials.get(name)
        if mat is None:
            mat = bpy.data.materials.new(name)
            mat.use_nodes = True
            b = _tex_set(
                mat,
                _img(prefix + "_BaseColor", os.path.join(TEX, prefix + "_BaseColor.png")),
                _img(prefix + "_Normal", os.path.join(TEX, prefix + "_Normal.png"), True),
                _img(prefix + "_MetSm", os.path.join(TEX, prefix + "_MetallicSmoothness.png"), True),
            )
            if clip:
                # Blender 5.x EEVEE Next: dithered (clip-like) rendering, no alpha blending.
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


# ---------------------------------------------------------------- mesh builder
class Builder:
    """Accumulates quads with per-corner UVs, material index and explicit custom normals."""

    def __init__(self):
        self.v = []
        self.faces = []
        self.uv = []        # per loop
        self.nrm = []       # per loop
        self.mat = []       # per face

    def quad(self, p0, p1, p2, p3, uvs, mat, normals):
        i = len(self.v)
        self.v += [Vector(p0), Vector(p1), Vector(p2), Vector(p3)]
        self.faces.append((i, i + 1, i + 2, i + 3))
        self.uv += list(uvs)
        self.nrm += [Vector(n).normalized() for n in normals]
        self.mat.append(mat)

    def tri(self, p0, p1, p2, uvs, mat, normals):
        i = len(self.v)
        self.v += [Vector(p0), Vector(p1), Vector(p2)]
        self.faces.append((i, i + 1, i + 2))
        self.uv += list(uvs)
        self.nrm += [Vector(n).normalized() for n in normals]
        self.mat.append(mat)

    @property
    def tris(self):
        return sum(len(f) - 2 for f in self.faces)

    def to_object(self, name, materials, collection=None):
        me = bpy.data.meshes.new(name)
        me.from_pydata([tuple(p) for p in self.v], [], [list(f) for f in self.faces])
        me.update()
        for m in materials:
            me.materials.append(m)
        uvl = me.uv_layers.new(name="UVMap")
        for i, uv in enumerate(self.uv):
            uvl.data[i].uv = uv
        for p, mi in zip(me.polygons, self.mat):
            p.material_index = mi
            p.use_smooth = True
        me.normals_split_custom_set([tuple(n) for n in self.nrm])
        ob = bpy.data.objects.new(name, me)
        (collection or bpy.context.scene.collection).objects.link(ob)
        return ob


# ---------------------------------------------------------------- primitives
def _frame(direction):
    d = Vector(direction).normalized()
    up = Vector((0, 0, 1))
    if abs(d.dot(up)) > 0.97:
        up = Vector((0, 1, 0))
    r = d.cross(up).normalized()
    u = r.cross(d).normalized()
    return r, u, d


def card(b, center, right, up, w, h, rect, normal, mat=MAT_FOLIAGE, flip_u=False):
    """One leaf/frond quad. `normal` is the already-biased canopy normal shared by all 4 corners."""
    r = Vector(right).normalized() * (w * 0.5)
    u = Vector(up).normalized() * (h * 0.5)
    c = Vector(center)
    u0, v0, u1, v1 = rect
    if flip_u:
        u0, u1 = u1, u0
    b.quad(c - r - u, c + r - u, c + r + u, c - r + u,
           [(u0, v0), (u1, v0), (u1, v1), (u0, v1)], mat, [normal] * 4)


def strip(b, pts, rights, widths, rect, normals, mat=MAT_FOLIAGE):
    """A card strip following a path: pts[i] is the centre of cross-section i."""
    u0, v0, u1, v1 = rect
    n = len(pts) - 1
    for i in range(n):
        t0, t1 = i / n, (i + 1) / n
        a0 = Vector(pts[i]) - Vector(rights[i]) * widths[i] * 0.5
        a1 = Vector(pts[i]) + Vector(rights[i]) * widths[i] * 0.5
        b0 = Vector(pts[i + 1]) - Vector(rights[i + 1]) * widths[i + 1] * 0.5
        b1 = Vector(pts[i + 1]) + Vector(rights[i + 1]) * widths[i + 1] * 0.5
        va, vb = v0 + (v1 - v0) * t0, v0 + (v1 - v0) * t1
        b.quad(a0, a1, b1, b0,
               [(u0, va), (u1, va), (u1, vb), (u0, vb)], mat,
               [normals[i], normals[i], normals[i + 1], normals[i + 1]])


def strip_folded(b, pts, rights, tangents, widths, rect, normals, fold, mat=MAT_FOLIAGE):
    """A V-section card strip: two half-vanes meeting at the rachis ridge, each rolled `fold`
    radians downward. A real palm frond is V-shaped in section; two flat halves cost the same
    triangles as one flat strip of the same resolution but stop the frond reading as a ribbon.
    The tile's centre line maps to the ridge, so the texture stays correct."""
    u0, v0, u1, v1 = rect
    um = (u0 + u1) * 0.5
    n = len(pts) - 1
    for side in (-1, 1):
        ua, ub = (um, u0) if side < 0 else (um, u1)
        halves = []
        hn = []
        for i in range(len(pts)):
            r = Matrix.Rotation(-fold * side, 4, Vector(tangents[i]).normalized()) @ Vector(rights[i])
            r = r.normalized() * side
            halves.append(Vector(pts[i]) + r * (widths[i] * 0.5))
            cn = r.cross(Vector(tangents[i])).normalized()
            if cn.z < 0:
                cn = -cn
            hn.append(mix_normal(cn, normals[i], 0.55))
        for i in range(n):
            t0, t1 = i / n, (i + 1) / n
            va, vb = v0 + (v1 - v0) * t0, v0 + (v1 - v0) * t1
            if side < 0:
                b.quad(halves[i], pts[i], pts[i + 1], halves[i + 1],
                       [(ub, va), (ua, va), (ua, vb), (ub, vb)], mat,
                       [hn[i], normals[i], normals[i + 1], hn[i + 1]])
            else:
                b.quad(pts[i], halves[i], halves[i + 1], pts[i + 1],
                       [(ua, va), (ub, va), (ub, vb), (ua, vb)], mat,
                       [normals[i], hn[i], hn[i + 1], normals[i + 1]])


def tube(b, pts, radii, sides, rect, tile_m=2.0, mat=MAT_BARK, close_top=False):
    """Tapered tube along a polyline, bark UVs: circumference -> tile U (one wrap),
    height -> tile V restarted every segment (the bark tiles are seamless, so this reads as tiling
    without ever leaving the atlas rect)."""
    u0, v0, u1, v1 = rect
    rings = []
    for i, (p, rad) in enumerate(zip(pts, radii)):
        if i < len(pts) - 1:
            d = Vector(pts[i + 1]) - Vector(p)
        else:
            d = Vector(p) - Vector(pts[i - 1])
        r, u, _ = _frame(d)
        ring = []
        for k in range(sides):
            a = 2 * math.pi * k / sides
            off = (r * math.cos(a) + u * math.sin(a))
            ring.append((Vector(p) + off * rad, off.normalized()))
        rings.append(ring)
    for i in range(len(rings) - 1):
        seg_len = (Vector(pts[i + 1]) - Vector(pts[i])).length
        frac = min(1.0, seg_len / tile_m)
        for k in range(sides):
            k2 = (k + 1) % sides
            p0, n0 = rings[i][k]
            p1, n1 = rings[i][k2]
            p2, n2 = rings[i + 1][k2]
            p3, n3 = rings[i + 1][k]
            ua = u0 + (u1 - u0) * (k / sides)
            ub = u0 + (u1 - u0) * ((k + 1) / sides)
            va, vb = v0, v0 + (v1 - v0) * frac
            b.quad(p0, p1, p2, p3, [(ua, va), (ub, va), (ub, vb), (ua, vb)], mat, [n0, n1, n2, n3])
    if close_top:
        ring = rings[-1]
        c = Vector(pts[-1])
        nz = (Vector(pts[-1]) - Vector(pts[-2])).normalized()
        um, vm = (u0 + u1) * 0.5, (v0 + v1) * 0.5
        for k in range(sides):
            k2 = (k + 1) % sides
            b.tri(c, ring[k][0], ring[k2][0], [(um, vm), (u0, v0), (u1, v0)], mat, [nz, nz, nz])


def crossed_billboard(b, height, width, rect, planes=3, pivot_frac=0.5, lean=0.0):
    """LOD2: a few crossed cards carrying the species silhouette. Normals splay outward so the
    cluster still shades with some roundness at distance."""
    for i in range(planes):
        a = math.pi * i / planes
        right = Vector((math.cos(a), math.sin(a), 0))
        n = Vector((math.cos(a + math.pi / 2), math.sin(a + math.pi / 2), lean)).normalized()
        card(b, (0, 0, height * pivot_frac), right, (0, 0, 1), width, height, rect, n)


def canopy_normal(p, center, blend, up_bias=0.0, flatten_z=1.0):
    """Normal biased toward the canopy volume normal: this is what stops leaf cards reading as flat
    cardboard catching the sun wrong."""
    d = Vector(p) - Vector(center)
    d.z *= flatten_z
    if d.length < 1e-5:
        d = Vector((0, 0, 1))
    d.normalize()
    if up_bias:
        d = d.lerp(Vector((0, 0, 1)), up_bias).normalized()
    return d


def mix_normal(card_n, volume_n, bias):
    return Vector(card_n).normalized().lerp(Vector(volume_n).normalized(), bias).normalized()


# ---------------------------------------------------------------- collision
def ucx_trunk(name, pts, radii, sides=6):
    """UCX_<name>: convex trunk collider. Never renders, no material."""
    b = Builder()
    rect = (0, 0, 1, 1)
    tube(b, pts, radii, sides, rect, tile_m=1e6, mat=0, close_top=True)
    # bottom cap
    c = Vector(pts[0])
    r, u, _ = _frame(Vector(pts[1]) - Vector(pts[0]))
    ring = [c + (r * math.cos(2 * math.pi * k / sides) + u * math.sin(2 * math.pi * k / sides)) * radii[0]
            for k in range(sides)]
    for k in range(sides):
        b.tri(c, ring[(k + 1) % sides], ring[k], [(0, 0)] * 3, 0, [(0, 0, -1)] * 3)
    me = bpy.data.meshes.new("UCX_" + name)
    me.from_pydata([tuple(p) for p in b.v], [], [list(f) for f in b.faces])
    me.update()
    ob = bpy.data.objects.new("UCX_" + name, me)
    bpy.context.scene.collection.objects.link(ob)
    return ob


def ubx_box(name, size, center):
    me = bpy.data.meshes.new("UBX_" + name)
    sx, sy, sz = size[0] / 2, size[1] / 2, size[2] / 2
    cx, cy, cz = center
    vs = [(cx - sx, cy - sy, cz - sz), (cx + sx, cy - sy, cz - sz), (cx + sx, cy + sy, cz - sz),
          (cx - sx, cy + sy, cz - sz), (cx - sx, cy - sy, cz + sz), (cx + sx, cy - sy, cz + sz),
          (cx + sx, cy + sy, cz + sz), (cx - sx, cy + sy, cz + sz)]
    fs = [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]
    me.from_pydata(vs, [], fs)
    me.update()
    ob = bpy.data.objects.new("UBX_" + name, me)
    bpy.context.scene.collection.objects.link(ob)
    return ob


# ---------------------------------------------------------------- scene / export plumbing
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


def assemble(base_name, lod_objects, collider=None):
    """Parent LOD0/1/2 (+ collider) to an empty named <base>, pivot at the world origin = base centre."""
    parent = bpy.data.objects.new(base_name, None)
    bpy.context.scene.collection.objects.link(parent)
    parent.matrix_world = Matrix.Identity(4)
    for i, ob in enumerate(lod_objects):
        ob.name = "%s_LOD%d" % (base_name, i)
        ob.data.name = ob.name
        ob.parent = parent
    if collider is not None:
        collider.parent = parent
    return parent


def apply_and_check(objs):
    """Apply rotation and scale on every mesh (geometry is authored in world space, so the transforms
    are already identity) and report any degenerate geometry."""
    rep = []
    for ob in objs:
        if ob.type != "MESH":
            continue
        with bpy.context.temp_override(object=ob, active_object=ob, selected_objects=[ob],
                                       selected_editable_objects=[ob]):
            bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
        zero = sum(1 for p in ob.data.polygons if p.area < 1e-9)
        loose = sum(1 for v in ob.data.vertices if not any(v.index in p.vertices for p in ob.data.polygons))
        rep.append({
            "object": ob.name,
            "tris": sum(len(p.vertices) - 2 for p in ob.data.polygons),
            "verts": len(ob.data.vertices),
            "zero_area_faces": zero,
            "loose_verts": loose,
            "scale": tuple(round(s, 6) for s in ob.scale),
            "rot": tuple(round(math.degrees(a), 4) for a in ob.rotation_euler),
            "has_custom_normals": ob.data.has_custom_normals,
            "materials": [m.name for m in ob.data.materials],
        })
    return rep


def export(parent, filename):
    import sys
    sys.path.insert(0, r"E:\VeyraCity\Tools\Blender")
    import vc_tools
    os.makedirs(EXPORT, exist_ok=True)
    path = os.path.join(EXPORT, filename)
    vc_tools.vc_export_fbx([parent], path, mode="prop", pivot=parent, path_mode="STRIP")
    return path


def rng(seed):
    return random.Random(seed)

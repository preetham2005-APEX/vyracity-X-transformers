"""VEYRA CITY - helpers for the original road-infrastructure prop set (ArtSource/Blender/Infra).

All geometry is authored directly from vertex/face lists so that every part is a closed,
manifold shell with outward normals and no n-gons. Units are metres (1 Blender unit = 1 m).

Authoring convention (CLAUDE.md "Blender -> Unity", preset VC_Unity_Prop):
  * the model's FRONT is Blender -Y; after export it faces Unity +Z,
  * Blender +X becomes Unity -X, Blender +Z becomes Unity +Y,
  * geometry is built with the intended PIVOT already at the world origin.
"""
import math
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector

# --------------------------------------------------------------------------------------- scene


def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    u = bpy.context.scene.unit_settings
    u.system = "METRIC"
    u.scale_length = 1.0
    u.length_unit = "METERS"


# ----------------------------------------------------------------------------------- materials
# PBR metal/roughness only; no image textures - the Unity side remaps these names onto the
# VeyraMaterialLibrary assets.
MATERIALS = {
    "VC_Concrete_Kerb":     dict(base=(0.620, 0.610, 0.580), metallic=0.0, roughness=0.82),
    "VC_Concrete_Barrier":  dict(base=(0.665, 0.655, 0.625), metallic=0.0, roughness=0.76),
    "VC_Concrete_Struct":   dict(base=(0.580, 0.575, 0.560), metallic=0.0, roughness=0.80),
    "VC_Asphalt_Gutter":    dict(base=(0.085, 0.085, 0.088), metallic=0.0, roughness=0.90),
    "VC_Steel_Galv":        dict(base=(0.560, 0.570, 0.580), metallic=1.0, roughness=0.44),
    # PAINTED steel only (agreed cross-set definition). Bare dark steel must NOT use this slot:
    # a metallic-0 paint material on bare metal reads dead in night reflections.
    "VC_Steel_Dark":        dict(base=(0.060, 0.062, 0.065), metallic=0.0, roughness=0.45),
    # BARE cast iron: manhole covers, utility covers, storm grates - worn smooth, genuinely metallic.
    "VC_Iron_Cast":         dict(base=(0.440, 0.430, 0.420), metallic=1.0, roughness=0.72),
    # Canonical name agreed across sets (was VC_SignPanel_Blank; the street set's blank sign
    # plates use the same material, so both sets share one library slot).
    "VC_Sign_BlankFace":    dict(base=(0.720, 0.720, 0.700), metallic=0.0, roughness=0.36),
    "VC_Timber_Pole":       dict(base=(0.260, 0.205, 0.155), metallic=0.0, roughness=0.88),
    "VC_Porcelain":         dict(base=(0.230, 0.205, 0.175), metallic=0.0, roughness=0.22),
    "VC_Signal_Lens":       dict(base=(0.050, 0.050, 0.055), metallic=0.0, roughness=0.14),
    "VC_Collision":         dict(base=(0.900, 0.100, 0.600), metallic=0.0, roughness=1.00),
}


def _bsdf_input(bsdf, name, index):
    try:
        return bsdf.inputs[name]
    except (KeyError, TypeError):
        return bsdf.inputs[index]


def get_material(name):
    m = bpy.data.materials.get(name)
    if m is not None:
        return m
    spec = MATERIALS[name]
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    _bsdf_input(bsdf, "Base Color", 0).default_value = (spec["base"][0], spec["base"][1], spec["base"][2], 1.0)
    _bsdf_input(bsdf, "Metallic", 1).default_value = spec["metallic"]
    _bsdf_input(bsdf, "Roughness", 2).default_value = spec["roughness"]
    m.diffuse_color = (spec["base"][0], spec["base"][1], spec["base"][2], 1.0)
    m.metallic = spec["metallic"]
    m.roughness = spec["roughness"]
    return m


# ------------------------------------------------------------------------------- primitive gen
# Every generator returns (verts, faces, smooth_flags). Shells are closed; winding is
# normalised by a final recalc_face_normals pass in MeshBuilder.build().


def box(size, center=(0.0, 0.0, 0.0)):
    sx, sy, sz = (s * 0.5 for s in size)
    cx, cy, cz = center
    v = [(cx - sx, cy - sy, cz - sz), (cx + sx, cy - sy, cz - sz),
         (cx + sx, cy + sy, cz - sz), (cx - sx, cy + sy, cz - sz),
         (cx - sx, cy - sy, cz + sz), (cx + sx, cy - sy, cz + sz),
         (cx + sx, cy + sy, cz + sz), (cx - sx, cy + sy, cz + sz)]
    f = [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]
    return v, f, [False] * 6


def _frame(axis):
    axis = Vector(axis).normalized()
    up = Vector((0.0, 0.0, 1.0))
    if abs(axis.dot(up)) > 0.97:
        up = Vector((1.0, 0.0, 0.0))
    u = axis.cross(up).normalized()
    w = axis.cross(u).normalized()
    return u, w


def tube(p0, p1, r0, r1=None, sides=8, phase=0.0, smooth=None):
    """Closed capped cylinder / cone between two points."""
    p0, p1 = Vector(p0), Vector(p1)
    r1 = r0 if r1 is None else r1
    u, w = _frame(Vector(p1) - Vector(p0))
    if smooth is None:
        smooth = sides >= 8
    verts = []
    for p, r in ((p0, r0), (p1, r1)):
        for k in range(sides):
            a = phase + 2.0 * math.pi * k / sides
            verts.append(p + u * (math.cos(a) * r) + w * (math.sin(a) * r))
    faces, sm = [], []
    for k in range(sides):
        k2 = (k + 1) % sides
        faces.append((k, k2, sides + k2, sides + k))
        sm.append(smooth)
    faces.append(tuple(range(sides)))
    faces.append(tuple(range(2 * sides - 1, sides - 1, -1)))
    sm += [False, False]
    return verts, faces, sm


def prism(p0, p1, r0, r1=None, sides=4, phase=None):
    """Flat-shaded tube; phase defaults to a flat-face-forward orientation."""
    if phase is None:
        phase = math.pi / sides
    return tube(p0, p1, r0, r1, sides=sides, phase=phase, smooth=False)


def sweep(profile, mats, cap_start=True, cap_end=True, smooth=False):
    """Sweep a closed 2D profile (list of (a, b)) through a list of 4x4 section matrices.

    Profile point (a, b) sits at the section's local (a, 0, b)."""
    n = len(profile)
    verts = []
    for M in mats:
        for (a, b) in profile:
            verts.append(M @ Vector((a, 0.0, b)))
    faces, sm = [], []
    for s in range(len(mats) - 1):
        o0, o1 = s * n, (s + 1) * n
        for j in range(n):
            k = (j + 1) % n
            faces.append((o0 + j, o0 + k, o1 + k, o1 + j))
            sm.append(smooth)
    if cap_start:
        faces.append(tuple(range(n)))
        sm.append(False)
    if cap_end:
        o = (len(mats) - 1) * n
        faces.append(tuple(range(o + n - 1, o - 1, -1)))
        sm.append(False)
    return verts, faces, sm


def sections_along_y(ys, x=0.0, z=0.0):
    """Section matrices for a straight sweep running along Y (a module's length axis)."""
    return [Matrix.Translation(Vector((x, y, z))) for y in ys]


def arc_sections(centre, radius, a0, a1, steps, z=0.0):
    """Section matrices for a horizontal arc; the section plane stays radial."""
    out = []
    cx, cy = centre
    for i in range(steps + 1):
        a = a0 + (a1 - a0) * i / steps
        pos = Vector((cx + math.cos(a) * radius, cy + math.sin(a) * radius, z))
        radial = Vector((math.cos(a), math.sin(a), 0.0))
        tangent = Vector((-math.sin(a), math.cos(a), 0.0))
        M = Matrix(((radial.x, tangent.x, 0.0, pos.x),
                    (radial.y, tangent.y, 0.0, pos.y),
                    (0.0, 0.0, 1.0, pos.z),
                    (0.0, 0.0, 0.0, 1.0)))
        out.append(M)
    return out


# ---------------------------------------------------------------------------------- mesh build


class MeshBuilder:
    def __init__(self):
        self.verts, self.faces, self.fmat, self.fsmooth = [], [], [], []
        self.mats = []

    def _mat_index(self, name):
        if name not in self.mats:
            self.mats.append(name)
        return self.mats.index(name)

    def add(self, part, mat, xform=None):
        verts, faces, smooth = part
        o = len(self.verts)
        mi = self._mat_index(mat)
        if xform is None:
            self.verts.extend(Vector(v) for v in verts)
        else:
            self.verts.extend(xform @ Vector(v) for v in verts)
        if isinstance(smooth, bool):
            smooth = [smooth] * len(faces)
        for f, s in zip(faces, smooth):
            self.faces.append([i + o for i in f])
            self.fmat.append(mi)
            self.fsmooth.append(s)
        return self

    def build(self, name, uv_scale=1.0):
        me = bpy.data.meshes.new(name)
        me.from_pydata(self.verts, [], self.faces)
        me.update()
        for mname in self.mats:
            me.materials.append(get_material(mname))
        for i, poly in enumerate(me.polygons):
            poly.material_index = self.fmat[i]
            poly.use_smooth = self.fsmooth[i]

        bm = bmesh.new()
        bm.from_mesh(me)
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        ngons = [f for f in bm.faces if len(f.verts) > 4]
        if ngons:
            bmesh.ops.triangulate(bm, faces=ngons, quad_method="BEAUTY", ngon_method="BEAUTY")
        uv = bm.loops.layers.uv.verify()
        for f in bm.faces:
            n = f.normal
            ax = max(range(3), key=lambda i: abs(n[i]))
            for loop in f.loops:
                co = loop.vert.co
                if ax == 0:
                    a, b = (co.y, co.z) if n.x > 0 else (-co.y, co.z)
                elif ax == 1:
                    a, b = (-co.x, co.z) if n.y > 0 else (co.x, co.z)
                else:
                    a, b = (co.x, co.y) if n.z > 0 else (co.x, -co.y)
                loop[uv].uv = (a * uv_scale, b * uv_scale)
        bm.to_mesh(me)
        bm.free()
        me.update()

        ob = bpy.data.objects.new(name, me)
        bpy.context.scene.collection.objects.link(ob)
        return ob


def tri_count(obj):
    me = obj.data
    me.calc_loop_triangles()
    return len(me.loop_triangles)


def make_root(name, children):
    root = bpy.data.objects.new(name, None)
    root.empty_display_size = 0.5
    bpy.context.scene.collection.objects.link(root)
    for c in children:
        c.parent = root
    return root


def save_and_export(name, root, children, blend_dir, export_dir):
    sys.path.insert(0, r"E:\VeyraCity\Tools\Blender")
    import vc_tools
    bpy.ops.wm.save_as_mainfile(filepath="%s/%s.blend" % (blend_dir, name))
    vc_tools.vc_export_fbx([root] + list(children), "%s/%s.fbx" % (export_dir, name),
                           mode="prop", pivot=root, path_mode="STRIP")

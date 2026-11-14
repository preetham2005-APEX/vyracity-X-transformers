"""VEYRA CITY - street furniture geometry helpers (original content, card: street props).

All props are authored with the base at z = 0, centred on x/y, FRONT FACING BLENDER -Y so that the
VC_Unity_Prop export (180 deg turn about Z, see Tools/Blender/vc_tools.py) makes the front face Unity +Z.

Everything is built into a single bmesh per LOD0 object so each prop is one mesh with a small, fixed
set of material slots (slot order is the Unity material order). Caps are triangulated, so no n-gons.
"""
import math

import bmesh
import bpy
from mathutils import Matrix, Vector

# ---------------------------------------------------------------- materials

#: name -> (base_color rgb, metallic, roughness, emission rgb or None, emission strength)
MATERIALS = {
    "VC_Hydrant_Red":        ((0.42, 0.045, 0.035), 0.0, 0.42, None, 0.0),
    "VC_Hydrant_Yellow":     ((0.52, 0.34, 0.02), 0.0, 0.42, None, 0.0),
    "VC_Steel_Galv":         ((0.560, 0.570, 0.580), 1.0, 0.44, None, 0.0),
    "VC_Steel_Dark":         ((0.060, 0.062, 0.065), 0.0, 0.45, None, 0.0),
    "VC_Iron_Painted":       ((0.040, 0.040, 0.042), 0.0, 0.45, None, 0.0),
    "VC_Paint_Civic_Green":  ((0.035, 0.085, 0.062), 0.0, 0.45, None, 0.0),
    "VC_Paint_Civic_Blue":   ((0.030, 0.075, 0.150), 0.0, 0.45, None, 0.0),
    "VC_Paint_Slate":        ((0.085, 0.090, 0.095), 0.0, 0.50, None, 0.0),
    "VC_Paint_Orange":       ((0.62, 0.18, 0.015), 0.0, 0.52, None, 0.0),
    "VC_Paint_White":        ((0.68, 0.68, 0.66), 0.0, 0.45, None, 0.0),
    "VC_Paint_Alu_Grey":     ((0.27, 0.275, 0.28), 0.35, 0.40, None, 0.0),
    "VC_Retro_White":        ((0.78, 0.78, 0.76), 0.0, 0.22, None, 0.0),
    "VC_Glass_Clear":        ((0.78, 0.80, 0.80), 0.0, 0.07, None, 0.0),
    "VC_Sign_BlankFace":     ((0.72, 0.72, 0.70), 0.0, 0.33, None, 0.0),
    "VC_Poster_Blank":       ((0.60, 0.60, 0.60), 0.0, 0.38, None, 0.0),
    "VC_Concrete_Plinth":    ((0.30, 0.295, 0.285), 0.0, 0.72, None, 0.0),
    "VC_Wood_Slat":          ((0.16, 0.105, 0.062), 0.0, 0.58, None, 0.0),
    # Lamp lenses. Emission is driven by Unity at night (_EMISSION); HDR colour here.
    "VC_Lamp_Emissive":      ((0.80, 0.78, 0.70), 0.0, 0.18, (1.00, 0.92, 0.74), 8.0),
}


def mat(name):
    """Fetch or create one of the MATERIALS entries as a Principled BSDF material."""
    m = bpy.data.materials.get(name)
    if m is not None:
        return m
    base, metallic, rough, emis, estr = MATERIALS[name]
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    bsdf.inputs["Base Color"].default_value = (base[0], base[1], base[2], 1.0)
    bsdf.inputs["Metallic"].default_value = metallic
    bsdf.inputs["Roughness"].default_value = rough
    if emis is not None:
        bsdf.inputs["Emission Color"].default_value = (emis[0], emis[1], emis[2], 1.0)
        bsdf.inputs["Emission Strength"].default_value = estr
    m.use_fake_user = True
    return m


# ---------------------------------------------------------------- bmesh primitives

def _cone_kwargs(r1, r2, depth, seg, cap_ends):
    kw = dict(cap_ends=cap_ends, cap_tris=False, segments=seg, depth=depth, calc_uvs=False)
    try:
        bmesh.ops.create_cone.__doc__  # presence check only
    except Exception:
        pass
    kw["radius1"] = r1
    kw["radius2"] = r2
    return kw


def _finish(bm, new_faces, m, smooth_sides, xform):
    for f in new_faces:
        f.material_index = m
        if smooth_sides:
            f.smooth = abs(f.normal.z) < 0.85
    if xform is not None:
        verts = {v for f in new_faces for v in f.verts}
        bmesh.ops.transform(bm, matrix=xform, verts=list(verts))


def _new_faces(bm, before):
    return [f for f in bm.faces if f not in before]


def cyl(bm, r1, r2, h, seg=12, m=0, loc=(0, 0, 0), rot=None, caps=True, smooth=True, base_at=None):
    """Cone/cylinder with its axis on +Z. `loc` is the CENTRE unless base_at is given (then the base)."""
    before = set(bm.faces)
    kw = _cone_kwargs(r1, r2, h, seg, caps)
    try:
        bmesh.ops.create_cone(bm, matrix=Matrix.Identity(4), **kw)
    except TypeError:
        kw.pop("radius1"), kw.pop("radius2")
        bmesh.ops.create_cone(bm, matrix=Matrix.Identity(4), diameter1=r1 * 2, diameter2=r2 * 2, **kw)
    faces = _new_faces(bm, before)
    if base_at is not None:
        loc = (base_at[0], base_at[1], base_at[2] + h / 2.0)
    x = Matrix.Translation(Vector(loc))
    if rot is not None:
        x = x @ rot
    _finish(bm, faces, m, smooth, x)
    return faces


def box(bm, size, loc=(0, 0, 0), rot=None, m=0, base_at=None):
    """Axis-aligned box. `loc` is the CENTRE unless base_at is given (then the bottom-centre)."""
    before = set(bm.faces)
    bmesh.ops.create_cube(bm, size=1.0, matrix=Matrix.Identity(4), calc_uvs=False)
    faces = _new_faces(bm, before)
    if base_at is not None:
        loc = (base_at[0], base_at[1], base_at[2] + size[2] / 2.0)
    x = Matrix.Translation(Vector(loc))
    if rot is not None:
        x = x @ rot
    x = x @ Matrix.Diagonal(Vector((size[0], size[1], size[2], 1.0)))
    _finish(bm, faces, m, False, x)
    return faces


def wedge_box(bm, size, loc, m=0, top_shift_y=0.0, top_scale_x=1.0, top_scale_y=1.0, base_at=None):
    """Box whose TOP face is shifted/scaled - used for tapered heads, sloped lids and luminaire shells."""
    faces = box(bm, size, loc=loc, m=m, base_at=base_at)
    verts = {v for f in faces for v in f.verts}
    zs = [v.co.z for v in verts]
    ztop = max(zs)
    cx = sum(v.co.x for v in verts) / len(verts)
    cy = sum(v.co.y for v in verts) / len(verts)
    for v in verts:
        if abs(v.co.z - ztop) < 1e-6:
            v.co.x = cx + (v.co.x - cx) * top_scale_x
            v.co.y = cy + (v.co.y - cy) * top_scale_y + top_shift_y
    return faces


def tube_along(bm, points, radii, seg=8, m=0, cap_start=True, cap_end=True, smooth=True):
    """Swept tube through a polyline of world points (>=2), one radius per point. Rings are oriented
    perpendicular to the local direction; good enough for lamp arms and scroll brackets."""
    pts = [Vector(p) for p in points]
    if len(radii) == 1:
        radii = list(radii) * len(pts)
    rings = []
    up = Vector((0.0, 0.0, 1.0))
    for i, p in enumerate(pts):
        if i == 0:
            d = (pts[1] - pts[0])
        elif i == len(pts) - 1:
            d = (pts[-1] - pts[-2])
        else:
            d = (pts[i + 1] - pts[i - 1])
        d.normalize()
        ref = up if abs(d.dot(up)) < 0.95 else Vector((0.0, 1.0, 0.0))
        side = d.cross(ref).normalized()
        other = side.cross(d).normalized()
        ring = []
        for s in range(seg):
            a = 2.0 * math.pi * s / seg
            off = side * (math.cos(a) * radii[i]) + other * (math.sin(a) * radii[i])
            ring.append(bm.verts.new(p + off))
        rings.append(ring)
    faces = []
    for i in range(len(rings) - 1):
        a, b = rings[i], rings[i + 1]
        for s in range(seg):
            t = (s + 1) % seg
            f = bm.faces.new((a[s], a[t], b[t], b[s]))
            f.smooth = smooth
            faces.append(f)
    if cap_start:
        faces.append(bm.faces.new(list(reversed(rings[0]))))
    if cap_end:
        faces.append(bm.faces.new(rings[-1]))
    for f in faces:
        f.material_index = m
    bm.verts.index_update()
    bm.faces.index_update()
    return faces


def arc_points(center, radius, a0, a1, steps, plane="YZ"):
    """Points along a circular arc; angles in degrees. plane YZ sweeps in the side view (y, z)."""
    out = []
    for i in range(steps + 1):
        a = math.radians(a0 + (a1 - a0) * i / steps)
        if plane == "YZ":
            out.append((center[0], center[1] + radius * math.cos(a), center[2] + radius * math.sin(a)))
        else:  # XZ
            out.append((center[0] + radius * math.cos(a), center[1], center[2] + radius * math.sin(a)))
    return out


# ---------------------------------------------------------------- object assembly

def new_object(name, bm, material_names, collection="LOD0"):
    """Triangulate n-gons, write the bmesh to a fresh object, attach the material slots in order."""
    ngons = [f for f in bm.faces if len(f.verts) > 4]
    if ngons:
        bmesh.ops.triangulate(bm, faces=ngons, quad_method="BEAUTY", ngon_method="BEAUTY")
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    obj = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(obj)
    for n in material_names:
        obj.data.materials.append(mat(n))
    try:
        import vc_tools
        vc_tools.vc_move_to(obj, collection)
    except Exception:
        pass
    return obj


def collider(name, kind, size=None, loc=(0, 0, 0), base_at=None, hull_of=None, seg=8, radius=0.3, h=1.0):
    """UBX_ box or UCX_ convex prism collider object (no material, never rendered)."""
    bm = bmesh.new()
    if kind == "UBX":
        box(bm, size, loc=loc, base_at=base_at)
    else:  # UCX convex prism
        cyl(bm, radius, radius * 0.98, h, seg=seg, loc=loc, base_at=base_at, smooth=False)
    ngons = [f for f in bm.faces if len(f.verts) > 4]
    if ngons:
        bmesh.ops.triangulate(bm, faces=ngons, quad_method="BEAUTY", ngon_method="BEAUTY")
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    obj = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(obj)
    try:
        import vc_tools
        vc_tools.vc_move_to(obj, "COLLISION")
    except Exception:
        pass
    return obj


def unwrap(obj, margin=0.02, angle=66.0):
    """Smart UV project; returns True on success."""
    vl = bpy.context.view_layer
    try:
        for o in list(vl.objects):
            if o is not None and o is not obj:
                o.select_set(False)
        obj.select_set(True)
        vl.objects.active = obj
        bpy.ops.object.mode_set(mode="EDIT")
        bpy.ops.mesh.select_all(action="SELECT")
        bpy.ops.uv.smart_project(angle_limit=math.radians(angle), island_margin=margin)
        bpy.ops.object.mode_set(mode="OBJECT")
        return len(obj.data.uv_layers) > 0
    except Exception:
        import traceback
        traceback.print_exc()
        try:
            bpy.ops.object.mode_set(mode="OBJECT")
        except Exception:
            pass
        return False


def clean_lod(obj, merge=1e-4):
    """Tidy a decimated LOD: weld near-duplicate verts, drop degenerate faces, recalc normals outward."""
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=merge)
    bmesh.ops.dissolve_degenerate(bm, dist=merge, edges=bm.edges)
    loose = [v for v in bm.verts if not v.link_faces]
    if loose:
        bmesh.ops.delete(bm, geom=loose, context="VERTS")
    # NB: no recalc here - the decimate inherits LOD0's (correct) winding, and a recalc on the
    # partly non-manifold decimated shell would be ambiguous and could flip faces.
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()


def mesh_report(obj):
    """Geometry self-check for one mesh object."""
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    tris = sum(len(f.verts) - 2 for f in bm.faces)
    rep = {
        "object": obj.name,
        "tris": tris,
        "ngons": sum(1 for f in bm.faces if len(f.verts) > 4),
        "non_manifold_edges": sum(1 for e in bm.edges if not e.is_manifold),
        "degenerate_faces": sum(1 for f in bm.faces if f.calc_area() < 1e-9),
        "loose_verts": sum(1 for v in bm.verts if not v.link_faces),
        "flipped_faces": 0,
        "slots": [s.material.name if s.material else None for s in obj.material_slots],
        "has_uv": len(obj.data.uv_layers) > 0,
    }
    # inverted-normal check: compare each face normal with the one a fresh outward recalc gives
    before = [(f.normal.copy(), all(e.is_manifold for e in f.edges)) for f in bm.faces]
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    rep["flipped_faces"] = sum(1 for f, (n, ok) in zip(bm.faces, before) if ok and f.normal.dot(n) < 0.0)
    bm.free()
    rep["scale"] = [round(v, 6) for v in obj.scale]
    rep["rotation"] = [round(math.degrees(v), 4) for v in obj.rotation_euler]
    return rep

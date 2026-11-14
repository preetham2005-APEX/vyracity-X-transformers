"""VEYRA CITY - modular building kit: geometry helpers (original content).

Buildings are authored with the base at z = 0, centred on x/y, FRONT (street side) FACING BLENDER -Y so the
VC_Unity_Prop export (180 deg turn about Z, Tools/Blender/vc_tools.py) puts the front at Unity +Z.
Footprint is x = width, y = depth, matching `footprint_w_m` / `footprint_l_m` of world_building_placements.csv.

Everything is built into one bmesh per building, with a small fixed set of material slots. Walls are made of
solid boxes around the window openings (piers / sill bands / head bands), so every window is a real recess
with a real reveal - that is the depth the generated box shells never had (BUG-004).
"""
import math

import bmesh
import bpy
from mathutils import Matrix, Vector

#: Detail level the helpers below obey, so one building description can be evaluated three times to give
#: AUTHORED LOD meshes. A collapse decimate cannot do this job: on geometry assembled from boxes it eats
#: the massing and leaves slivers (see preview/lod2_backface_check_*.png before the change).
#:   0 = everything, 1 = no sills, glazing panes or steps, 2 = massing only (solid walls, no openings)
DETAIL = 0


def set_detail(level):
    global DETAIL
    DETAIL = level


# ---------------------------------------------------------------- materials
# name -> (base rgb, metallic, roughness, emission rgb or None, strength)
# The name is also the texture prefix Unity looks for:
#   Art/Textures/Imported/<source>/<name>_baseColor.png (+ _metallicRoughness, _normal)
MATERIALS = {
    "VC_Wall_Stucco":    ((0.600, 0.570, 0.505), 0.0, 0.70, None, 0.0),
    "VC_Wall_StuccoWarm":((0.620, 0.520, 0.405), 0.0, 0.70, None, 0.0),
    "VC_Wall_Brick":     ((0.330, 0.165, 0.120), 0.0, 0.78, None, 0.0),
    "VC_Wall_Board":     ((0.470, 0.470, 0.445), 0.0, 0.66, None, 0.0),
    "VC_Wall_Concrete":  ((0.400, 0.398, 0.385), 0.0, 0.72, None, 0.0),
    "VC_Roof_Tile":      ((0.185, 0.115, 0.090), 0.0, 0.68, None, 0.0),
    "VC_Roof_Shingle":   ((0.105, 0.102, 0.098), 0.0, 0.76, None, 0.0),
    "VC_Roof_Metal":     ((0.225, 0.235, 0.240), 0.8, 0.42, None, 0.0),
    "VC_Trim_Paint":     ((0.740, 0.730, 0.700), 0.0, 0.45, None, 0.0),
    "VC_Trim_Dark":      ((0.095, 0.095, 0.092), 0.0, 0.50, None, 0.0),
    "VC_Glass_Window":   ((0.105, 0.130, 0.150), 0.0, 0.10, None, 0.0),
    "VC_Door_Wood":      ((0.175, 0.105, 0.065), 0.0, 0.48, None, 0.0),
    "VC_Metal_Garage":   ((0.400, 0.405, 0.405), 0.6, 0.45, None, 0.0),
    # D1 Crown Downtown towers
    "VC_Twr_Glass":      ((0.075, 0.098, 0.125), 0.0, 0.09, None, 0.0),
    "VC_Twr_Spandrel":   ((0.135, 0.145, 0.155), 0.7, 0.34, None, 0.0),
    "VC_Twr_Stone":      ((0.400, 0.390, 0.365), 0.0, 0.55, None, 0.0),
    "VC_Twr_Concrete":   ((0.330, 0.328, 0.318), 0.0, 0.68, None, 0.0),
    "VC_Twr_Metal":      ((0.300, 0.305, 0.310), 0.9, 0.30, None, 0.0),
    # D6 Neon Mile low-rise / commercial
    "VC_Nm_Brick":       ((0.255, 0.140, 0.110), 0.0, 0.76, None, 0.0),
    "VC_Nm_Panel":       ((0.330, 0.325, 0.310), 0.0, 0.58, None, 0.0),
    "VC_Nm_Glass":       ((0.090, 0.110, 0.128), 0.0, 0.10, None, 0.0),
    "VC_Nm_Metal":       ((0.260, 0.262, 0.265), 0.85, 0.34, None, 0.0),
    "VC_Nm_Concrete":    ((0.330, 0.325, 0.315), 0.0, 0.68, None, 0.0),
    "VC_Nm_Shutter":     ((0.215, 0.218, 0.222), 0.7, 0.44, None, 0.0),
    # BLANK emissive signage. No lettering, no logo: Unity drives _EMISSION and the colour at night.
    "VC_Neon_Emissive":  ((0.620, 0.300, 0.520), 0.0, 0.22, (1.00, 0.42, 0.78), 6.0),
    # D2 Azure Coast stucco
    "VC_Cs_Stucco":      ((0.690, 0.665, 0.605), 0.0, 0.66, None, 0.0),
    "VC_Cs_RoofTile":    ((0.370, 0.165, 0.095), 0.0, 0.68, None, 0.0),
    "VC_Cs_Trim":        ((0.720, 0.710, 0.690), 0.0, 0.42, None, 0.0),
    "VC_Cs_Glass":       ((0.095, 0.120, 0.140), 0.0, 0.09, None, 0.0),
    "VC_Cs_Wood":        ((0.300, 0.205, 0.130), 0.0, 0.56, None, 0.0),
    "VC_Cs_Concrete":    ((0.520, 0.505, 0.475), 0.0, 0.70, None, 0.0),
    # D8 Redwood Valley market town
    "VC_Mt_Brick":       ((0.300, 0.150, 0.110), 0.0, 0.76, None, 0.0),
    "VC_Mt_Render":      ((0.480, 0.455, 0.410), 0.0, 0.62, None, 0.0),
    "VC_Mt_Roof":        ((0.088, 0.092, 0.098), 0.0, 0.62, None, 0.0),
    "VC_Mt_Trim":        ((0.120, 0.135, 0.120), 0.0, 0.44, None, 0.0),
    "VC_Mt_Glass":       ((0.090, 0.108, 0.125), 0.0, 0.10, None, 0.0),
    "VC_Mt_Concrete":    ((0.470, 0.458, 0.435), 0.0, 0.66, None, 0.0),
    # ResidentialMidrise (D1 + D5)
    "VC_Mr_Panel":       ((0.400, 0.392, 0.372), 0.0, 0.58, None, 0.0),
    "VC_Mr_Brick":       ((0.285, 0.165, 0.125), 0.0, 0.74, None, 0.0),
    "VC_Mr_Glass":       ((0.092, 0.112, 0.130), 0.0, 0.10, None, 0.0),
    "VC_Mr_Metal":       ((0.270, 0.274, 0.278), 0.85, 0.34, None, 0.0),
    "VC_Mr_Concrete":    ((0.420, 0.412, 0.396), 0.0, 0.66, None, 0.0),
    # D3 Ironport industrial
    "VC_In_Clad":        ((0.330, 0.340, 0.345), 0.55, 0.46, None, 0.0),
    "VC_In_Roof":        ((0.225, 0.232, 0.236), 0.70, 0.44, None, 0.0),
    "VC_In_Glass":       ((0.140, 0.160, 0.175), 0.0, 0.14, None, 0.0),
    "VC_In_Metal":       ((0.260, 0.262, 0.265), 0.88, 0.36, None, 0.0),
    "VC_In_Concrete":    ((0.360, 0.355, 0.342), 0.0, 0.70, None, 0.0),
    "VC_In_TankShell":   ((0.430, 0.438, 0.440), 0.60, 0.40, None, 0.0),
    # D4 Alpine
    "VC_Al_Timber":      ((0.265, 0.165, 0.098), 0.0, 0.62, None, 0.0),
    "VC_Al_Stone":       ((0.360, 0.348, 0.330), 0.0, 0.74, None, 0.0),
    "VC_Al_Roof":        ((0.120, 0.112, 0.105), 0.0, 0.64, None, 0.0),
    "VC_Al_Glass":       ((0.095, 0.115, 0.132), 0.0, 0.10, None, 0.0),
    "VC_Al_Trim":        ((0.400, 0.300, 0.190), 0.0, 0.52, None, 0.0),
    "VC_Al_Concrete":    ((0.420, 0.410, 0.392), 0.0, 0.68, None, 0.0),
    # D8 Rural
    "VC_Ru_Board":       ((0.300, 0.145, 0.105), 0.0, 0.72, None, 0.0),
    "VC_Ru_Roof":        ((0.215, 0.205, 0.195), 0.35, 0.56, None, 0.0),
    "VC_Ru_Metal":       ((0.330, 0.335, 0.338), 0.75, 0.42, None, 0.0),
    "VC_Ru_Glass":       ((0.095, 0.112, 0.128), 0.0, 0.11, None, 0.0),
    "VC_Ru_Trim":        ((0.600, 0.585, 0.550), 0.0, 0.50, None, 0.0),
    "VC_Ru_Concrete":    ((0.430, 0.420, 0.400), 0.0, 0.70, None, 0.0),
    # D7 Aviation
    "VC_Av_Clad":        ((0.420, 0.428, 0.432), 0.50, 0.42, None, 0.0),
    "VC_Av_Roof":        ((0.300, 0.308, 0.312), 0.70, 0.40, None, 0.0),
    "VC_Av_Glass":       ((0.110, 0.132, 0.150), 0.0, 0.11, None, 0.0),
    "VC_Av_Metal":       ((0.280, 0.284, 0.288), 0.88, 0.34, None, 0.0),
    "VC_Av_Concrete":    ((0.400, 0.394, 0.380), 0.0, 0.70, None, 0.0),
    # D5 SuburbanRetail + Roadside
    "VC_Rt_Panel":       ((0.470, 0.462, 0.445), 0.0, 0.56, None, 0.0),
    "VC_Rt_Glass":       ((0.092, 0.112, 0.130), 0.0, 0.10, None, 0.0),
    "VC_Rt_Metal":       ((0.290, 0.294, 0.298), 0.85, 0.34, None, 0.0),
    "VC_Rt_Concrete":    ((0.430, 0.422, 0.405), 0.0, 0.68, None, 0.0),
    "VC_Rt_Brick":       ((0.310, 0.175, 0.130), 0.0, 0.74, None, 0.0),
}


def mat(name):
    m = bpy.data.materials.get(name)
    if m is not None:
        return m
    base, metallic, rough, emis, estr = MATERIALS[name]
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    b.inputs["Base Color"].default_value = (base[0], base[1], base[2], 1.0)
    b.inputs["Metallic"].default_value = metallic
    b.inputs["Roughness"].default_value = rough
    if emis is not None:
        b.inputs["Emission Color"].default_value = (emis[0], emis[1], emis[2], 1.0)
        b.inputs["Emission Strength"].default_value = estr
    m.use_fake_user = True
    return m


# ---------------------------------------------------------------- primitives

def _finish(bm, faces, m, xform, smooth=False):
    for f in faces:
        f.material_index = m
        f.smooth = smooth
    if xform is not None:
        bmesh.ops.transform(bm, matrix=xform, verts=list({v for f in faces for v in f.verts}))
    return faces


def box(bm, size, loc=None, base_at=None, rot=None, m=0):
    """Axis-aligned box; `loc` is the centre, `base_at` the bottom-centre."""
    before = set(bm.faces)
    bmesh.ops.create_cube(bm, size=1.0, matrix=Matrix.Identity(4), calc_uvs=False)
    faces = [f for f in bm.faces if f not in before]
    if base_at is not None:
        loc = (base_at[0], base_at[1], base_at[2] + size[2] / 2.0)
    x = Matrix.Translation(Vector(loc))
    if rot is not None:
        x = x @ rot
    x = x @ Matrix.Diagonal(Vector((size[0], size[1], size[2], 1.0)))
    return _finish(bm, faces, m, x)


def cyl(bm, r1, r2, h, seg=8, m=0, loc=None, base_at=None, rot=None, smooth=True):
    before = set(bm.faces)
    bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=seg,
                          radius1=r1, radius2=r2, depth=h, matrix=Matrix.Identity(4), calc_uvs=False)
    faces = [f for f in bm.faces if f not in before]
    for f in faces:
        f.smooth = smooth and abs(f.normal.z) < 0.85
    if base_at is not None:
        loc = (base_at[0], base_at[1], base_at[2] + h / 2.0)
    x = Matrix.Translation(Vector(loc))
    if rot is not None:
        x = x @ rot
    for f in faces:
        f.material_index = m
    bmesh.ops.transform(bm, matrix=x, verts=list({v for f in faces for v in f.verts}))
    return faces


def poly(bm, pts, faces_idx, m=0, smooth=False):
    """Hand-built solid from a vertex list and index tuples (outward winding; recalculated later anyway)."""
    vs = [bm.verts.new(Vector(p)) for p in pts]
    out = []
    for idx in faces_idx:
        try:
            f = bm.faces.new([vs[i] for i in idx])
        except ValueError:
            continue
        f.material_index = m
        f.smooth = smooth
        out.append(f)
    bm.verts.index_update()
    bm.faces.index_update()
    return out


def profile_solid(bm, pts_yz, x0, x1, m=0, smooth=False):
    """Extrude a CLOSED 2D profile (list of (y, z)) along X from x0 to x1 as a closed solid.

    This is how the industrial roof forms are built - sawtooth, monitor, barrel, gable - because the whole
    roof is then one solid of 4n-4 triangles whatever the span, which is what the D3 sheds need: they read
    as silhouette and roof line from the road, so the budget belongs in the profile, not the facade.
    """
    a = [bm.verts.new((x0, y, z)) for (y, z) in pts_yz]
    b = [bm.verts.new((x1, y, z)) for (y, z) in pts_yz]
    n = len(pts_yz)
    out = []
    for i in range(n):
        j = (i + 1) % n
        f = bm.faces.new((a[i], a[j], b[j], b[i]))
        f.smooth = smooth
        out.append(f)
    out.append(bm.faces.new(list(reversed(a))))
    out.append(bm.faces.new(b))
    for f in out:
        f.material_index = m
    bm.verts.index_update()
    bm.faces.index_update()
    return out


def sawtooth_profile(y0, y1, z_eave, bays, rise, glaze=0.62):
    """North-light sawtooth: each bay slopes up to a vertical clerestory face. Returns (profile, glass_faces)
    where glass_faces are (y, z_bottom, z_top) of the vertical faces the clerestory glazing sits in."""
    pts = [(y0, 0.0)]
    glass = []
    step = (y1 - y0) / bays
    for i in range(bays):
        ya = y0 + step * i
        yb = ya + step
        pts.append((ya, z_eave))
        pts.append((yb, z_eave + rise))
        glass.append((yb, z_eave + rise * (1.0 - glaze), z_eave + rise - 0.12))
        if i < bays - 1:
            pts.append((yb, z_eave))
    pts.append((y1, 0.0))
    return pts, glass


# ---------------------------------------------------------------- walls and openings

def wall(bm, axis, fixed, start, end, z0, z1, thick, m_wall, windows=(), m_glass=None,
         reveal=0.14, sill=None, m_sill=None, sill_depth=0.07, inward=None):
    """A solid wall from `start` to `end` along `axis` ('X' or 'Y'), its OUTER face at `fixed` on the other
    axis, from z0 to z1 and `thick` metres thick toward the interior.

    `windows` is a list of (centre_along_axis, z_bottom, width, height); an opening whose z_bottom is 0 is a
    door (no glass, no sill - use `door` for the leaf). Each opening is a real hole with a `reveal` deep
    recess, which is the depth the generated box shells never had (BUG-004).

    The wall is emitted storey by storey: overlapping opening z-ranges are merged into bands, a full-width
    solid band fills each gap between them, and inside a band the openings are cut by a horizontal sweep.
    Openings inside one band must not overlap horizontally (different storeys are different bands, so a
    ground-floor window and the window above it are fine).
    """
    wins = sorted(windows, key=lambda w: w[0])
    if DETAIL >= 2:
        wins, m_glass = [], None          # massing only: a solid wall
    elif DETAIL == 1:
        sill = False                      # keep the openings (they carry the facade), drop the trim
    sgn = float(inward) if inward is not None else (-1.0 if fixed > 0 else 1.0)
    cen = fixed + sgn * thick / 2.0

    def seg(a, b, zz0, zz1):
        if b - a < 1e-4 or zz1 - zz0 < 1e-4:
            return
        if axis == "X":
            box(bm, (b - a, thick, zz1 - zz0), loc=((a + b) / 2.0, cen, (zz0 + zz1) / 2.0), m=m_wall)
        else:
            box(bm, (thick, b - a, zz1 - zz0), loc=(cen, (a + b) / 2.0, (zz0 + zz1) / 2.0), m=m_wall)

    # merge the openings into storey bands
    bands = []
    for w in sorted(wins, key=lambda w: w[1]):
        a, b = max(z0, w[1]), min(z1, w[1] + w[3])
        if b <= a:
            continue
        if bands and a <= bands[-1][1] + 1e-6:
            bands[-1] = (bands[-1][0], max(bands[-1][1], b), bands[-1][2] + [w])
        else:
            bands.append((a, b, [w]))

    z = z0
    for (bz0, bz1, ws) in bands:
        seg(start, end, z, bz0)                       # solid band under this storey
        cursor = start
        for (c, wz0, ww, wh) in sorted(ws, key=lambda w: w[0]):
            a, b = c - ww / 2.0, c + ww / 2.0
            seg(cursor, a, bz0, bz1)                  # pier
            seg(a, b, bz0, max(bz0, min(wz0, bz1)))   # under the opening, inside the band
            seg(a, b, max(bz0, min(wz0 + wh, bz1)), bz1)   # over the opening, inside the band
            cursor = max(cursor, b)
        seg(cursor, end, bz0, bz1)
        z = bz1
    seg(start, end, z, z1)                            # solid band above the top storey

    if m_glass is None or DETAIL >= 1:
        return
    gcen = fixed + sgn * (reveal + 0.03)
    for (c, wz0, ww, wh) in wins:
        if wz0 < 0.05:            # a door opening: the leaf is placed by `door`, and it never gets a sill
            continue
        if axis == "X":
            box(bm, (ww - 0.06, 0.05, wh - 0.06), loc=(c, gcen, wz0 + wh / 2.0), m=m_glass)
            if sill:
                box(bm, (ww + 0.18, sill_depth * 2.0, 0.07),
                    loc=(c, fixed - sgn * sill_depth * 0.2, wz0 - 0.035), m=m_sill)
        else:
            box(bm, (0.05, ww - 0.06, wh - 0.06), loc=(gcen, c, wz0 + wh / 2.0), m=m_glass)
            if sill:
                box(bm, (sill_depth * 2.0, ww + 0.18, 0.07),
                    loc=(fixed - sgn * sill_depth * 0.2, c, wz0 - 0.035), m=m_sill)


def win_row(start, end, n, width, z, height, margin=0.9):
    """Evenly spaced window spec list for `wall`."""
    a, b = start + margin, end - margin
    if n <= 0 or b <= a:
        return []
    if n == 1:
        return [((a + b) / 2.0, z, width, height)]
    step = (b - a - width) / (n - 1)
    return [(a + width / 2.0 + i * step, z, width, height) for i in range(n)]


def door_op(at, w, h, z0=0.0):
    """The opening spec to pass to `wall` so the door leaf sits in a real hole."""
    return (at, z0, w, h)


def door(bm, axis, fixed, at, z0, w, h, thick, m_door, m_trim, recess=0.12, inward=None):
    """Recessed door leaf plus a surrounding architrave. Needs a matching `door_op` in the wall."""
    if DETAIL >= 2:
        return
    sgn = float(inward) if inward is not None else (-1.0 if fixed > 0 else 1.0)
    dc = fixed + sgn * (recess + 0.03)
    if axis == "X":
        box(bm, (w, 0.06, h), loc=(at, dc, z0 + h / 2.0), m=m_door)
        box(bm, (w + 0.24, thick * 0.35, 0.10), loc=(at, fixed + sgn * thick * 0.17, z0 + h + 0.05), m=m_trim)
    else:
        box(bm, (0.06, w, h), loc=(dc, at, z0 + h / 2.0), m=m_door)
        box(bm, (thick * 0.35, w + 0.24, 0.10), loc=(fixed + sgn * thick * 0.17, at, z0 + h + 0.05), m=m_trim)


# ---------------------------------------------------------------- roofs

def gable_roof(bm, w, d, z_eave, z_ridge, m, ridge_axis="X", ov=0.45):
    """Closed triangular-prism roof solid. Ridge along X (gable ends face ±X) or along Y."""
    W, D = w / 2.0 + ov, d / 2.0 + ov
    if ridge_axis == "X":
        pts = [(-W, -D, z_eave), (W, -D, z_eave), (W, D, z_eave), (-W, D, z_eave),
               (-W, 0.0, z_ridge), (W, 0.0, z_ridge)]
        faces = [(0, 1, 5, 4), (2, 3, 4, 5), (0, 4, 3), (1, 2, 5), (0, 3, 2, 1)]
    else:
        pts = [(-W, -D, z_eave), (W, -D, z_eave), (W, D, z_eave), (-W, D, z_eave),
               (0.0, -D, z_ridge), (0.0, D, z_ridge)]
        faces = [(0, 4, 5, 3), (1, 2, 5, 4), (0, 1, 4), (3, 5, 2), (0, 3, 2, 1)]
    return poly(bm, pts, faces, m)


def hip_roof(bm, w, d, z_eave, z_ridge, m, ov=0.45, ridge_frac=0.42):
    """Closed hipped roof solid; the ridge runs along X and is `ridge_frac` of the width."""
    W, D = w / 2.0 + ov, d / 2.0 + ov
    R = W * ridge_frac
    pts = [(-W, -D, z_eave), (W, -D, z_eave), (W, D, z_eave), (-W, D, z_eave),
           (-R, 0.0, z_ridge), (R, 0.0, z_ridge)]
    faces = [(0, 1, 5, 4), (2, 3, 4, 5), (0, 4, 3), (1, 2, 5), (0, 3, 2, 1)]
    return poly(bm, pts, faces, m)


def shed_roof(bm, w, d, z_lo, z_hi, m, ov=0.35, low_side="-Y"):
    """Closed mono-pitch roof solid, low edge on `low_side`."""
    W, D = w / 2.0 + ov, d / 2.0 + ov
    if low_side == "-Y":
        pts = [(-W, -D, z_lo), (W, -D, z_lo), (W, D, z_hi), (-W, D, z_hi)]
    else:
        pts = [(-W, -D, z_hi), (W, -D, z_hi), (W, D, z_lo), (-W, D, z_lo)]
    lo = [(p[0], p[1], min(z_lo, z_hi) - 0.22) for p in pts]
    all_pts = list(pts) + lo
    faces = [(0, 1, 2, 3), (4, 7, 6, 5), (0, 4, 5, 1), (1, 5, 6, 2), (2, 6, 7, 3), (3, 7, 4, 0)]
    return poly(bm, all_pts, faces, m)


def fascia(bm, w, d, z_eave, m, ov=0.45, h=0.20, t=0.10):
    """Fascia board ring at the eaves - the shadow line that reads at distance."""
    W, D = w / 2.0 + ov, d / 2.0 + ov
    box(bm, (2 * W + 2 * t, t, h), loc=(0, -D - t / 2.0, z_eave - h / 2.0), m=m)
    box(bm, (2 * W + 2 * t, t, h), loc=(0, D + t / 2.0, z_eave - h / 2.0), m=m)
    box(bm, (t, 2 * D, h), loc=(-W - t / 2.0, 0, z_eave - h / 2.0), m=m)
    box(bm, (t, 2 * D, h), loc=(W + t / 2.0, 0, z_eave - h / 2.0), m=m)


def parapet(bm, w, d, z_top, m, h=0.75, t=0.26, m_cap=None):
    """Parapet wall ring around a flat roof, with an optional coping cap in another material."""
    W, D = w / 2.0, d / 2.0
    box(bm, (w, t, h), base_at=(0, -D + t / 2.0, z_top), m=m)
    box(bm, (w, t, h), base_at=(0, D - t / 2.0, z_top), m=m)
    box(bm, (t, d - 2 * t, h), base_at=(-W + t / 2.0, 0, z_top), m=m)
    box(bm, (t, d - 2 * t, h), base_at=(W - t / 2.0, 0, z_top), m=m)
    if m_cap is not None:
        box(bm, (w + 0.12, t + 0.12, 0.08), base_at=(0, -D + t / 2.0, z_top + h), m=m_cap)
        box(bm, (w + 0.12, t + 0.12, 0.08), base_at=(0, D - t / 2.0, z_top + h), m=m_cap)
        box(bm, (t + 0.12, d - 2 * t, 0.08), base_at=(-W + t / 2.0, 0, z_top + h), m=m_cap)
        box(bm, (t + 0.12, d - 2 * t, 0.08), base_at=(W - t / 2.0, 0, z_top + h), m=m_cap)


def chimney(bm, x, y, z_base, z_top, m, w=0.70, d=0.60, m_cap=None):
    box(bm, (w, d, z_top - z_base), base_at=(x, y, z_base), m=m)
    if m_cap is not None:
        box(bm, (w + 0.16, d + 0.16, 0.12), base_at=(x, y, z_top), m=m_cap)


def dormer(bm, x, y_front, z_sill, w, h, depth, m_wall, m_roof, m_glass, m_trim):
    """Gabled dormer sitting on a front roof slope (front faces -Y)."""
    if DETAIL >= 2:
        return
    box(bm, (w, depth, h), base_at=(x, y_front + depth / 2.0, z_sill), m=m_wall)
    box(bm, (w - 0.5, 0.05, h - 0.5), loc=(x, y_front + 0.10, z_sill + h / 2.0), m=m_glass)
    pts = [(x - w / 2 - 0.12, y_front - 0.12, z_sill + h), (x + w / 2 + 0.12, y_front - 0.12, z_sill + h),
           (x + w / 2 + 0.12, y_front + depth + 0.12, z_sill + h), (x - w / 2 - 0.12, y_front + depth + 0.12, z_sill + h),
           (x, y_front - 0.12, z_sill + h + 0.42), (x, y_front + depth + 0.12, z_sill + h + 0.42)]
    poly(bm, pts, [(0, 4, 5, 3), (1, 2, 5, 4), (0, 1, 4), (3, 5, 2), (0, 3, 2, 1)], m_roof)


def porch(bm, x0, x1, y_front, depth, z_floor, z_head, m_post, m_roof, m_deck, posts=3, post=0.14):
    """Covered porch: deck slab, posts, flat canopy."""
    if DETAIL >= 2:
        return
    box(bm, (x1 - x0, depth, 0.18), base_at=((x0 + x1) / 2.0, y_front - depth / 2.0, z_floor - 0.18), m=m_deck)
    for i in range(posts):
        px = x0 + post + (x1 - x0 - 2 * post) * i / max(1, posts - 1)
        box(bm, (post, post, z_head - z_floor), base_at=(px, y_front - depth + post, z_floor), m=m_post)
    box(bm, (x1 - x0 + 0.30, depth + 0.30, 0.16), base_at=((x0 + x1) / 2.0, y_front - depth / 2.0, z_head), m=m_roof)


def steps(bm, x, y_front, w, z_floor, m, n=3, run=0.20):
    """Entrance steps rising from z = 0 to z_floor. Nothing goes below the ground plane, and the bottom
    tread projects at most n * run in front of y_front."""
    if DETAIL >= 1:
        return
    h = z_floor / float(n)
    for i in range(n):
        out = run * (n - i)
        box(bm, (w, out, h), base_at=(x, y_front - out / 2.0, i * h), m=m)


def entry_recess(bm, x0, x1, y_front, depth, z0, z_head, m_wall, m_soffit, m_floor, inward=1.0):
    """A recessed entry bay carved into the front wall (the caller leaves a full-height opening there).
    Builds the two side returns, the back wall is the caller's job (it carries the door), plus the soffit
    over the recess and the recess floor. Front faces -Y, so the recess goes toward +Y."""
    if DETAIL >= 2:
        return
    t = 0.18
    box(bm, (t, depth, z_head - z0), base_at=(x0 + t / 2.0, y_front + depth / 2.0, z0), m=m_wall)
    box(bm, (t, depth, z_head - z0), base_at=(x1 - t / 2.0, y_front + depth / 2.0, z0), m=m_wall)
    box(bm, (x1 - x0, depth, 0.14), base_at=((x0 + x1) / 2.0, y_front + depth / 2.0, z_head), m=m_soffit)
    box(bm, (x1 - x0, depth, 0.06), base_at=((x0 + x1) / 2.0, y_front + depth / 2.0, z0), m=m_floor)


# ---------------------------------------------------------------- object assembly

def new_object(name, bm, material_names):
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
    return obj


def collider_box(name, size, base_at):
    bm = bmesh.new()
    box(bm, size, base_at=base_at)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    obj = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(obj)
    return obj


def unwrap(obj, margin=0.01, angle=66.0, cube=2.0):
    vl = bpy.context.view_layer
    try:
        for o in list(vl.objects):
            if o is not None and o is not obj:
                o.select_set(False)
        obj.select_set(True)
        vl.objects.active = obj
        bpy.ops.object.mode_set(mode="EDIT")
        bpy.ops.mesh.select_all(action="SELECT")
        bpy.ops.uv.cube_project(cube_size=cube, correct_aspect=True, scale_to_bounds=False)
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


def clean_lod(obj, merge=1e-4, ref=None):
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=merge)
    bmesh.ops.dissolve_degenerate(bm, dist=merge, edges=bm.edges)
    loose = [v for v in bm.verts if not v.link_faces]
    if loose:
        bmesh.ops.delete(bm, geom=loose, context="VERTS")
    # A collapse decimate DOES flip triangles here (proved: preview/lod1_backface_check_zoom.png rendered
    # with this pass removed shows the background through the walls). A normal recalculation cannot fix it,
    # because the collapse opens shells and a recalc on an open shell is ambiguous. So orient every LOD face
    # against the LOD0 SURFACE: take the nearest point on LOD0 and flip the face if it faces the opposite
    # way. With this pass the same render is solid everywhere.
    if ref is not None:
        from mathutils.bvhtree import BVHTree
        rb = bmesh.new()
        rb.from_mesh(ref.data)
        bmesh.ops.triangulate(rb, faces=rb.faces)
        tree = BVHTree.FromBMesh(rb)
        flip = []
        for f in bm.faces:
            hit = tree.find_nearest(f.calc_center_median())
            if hit[0] is not None and hit[1].dot(f.normal) < 0.0:
                flip.append(f)
        rb.free()
        if flip:
            bmesh.ops.reverse_faces(bm, faces=flip)
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()


def mesh_report(obj):
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    tris = sum(len(f.verts) - 2 for f in bm.faces)
    rep = {
        "object": obj.name, "tris": tris,
        "ngons": sum(1 for f in bm.faces if len(f.verts) > 4),
        "non_manifold_edges": sum(1 for e in bm.edges if not e.is_manifold),
        "degenerate_faces": sum(1 for f in bm.faces if f.calc_area() < 1e-9),
        "loose_verts": sum(1 for v in bm.verts if not v.link_faces),
        "slots": [s.material.name if s.material else None for s in obj.material_slots],
        "has_uv": len(obj.data.uv_layers) > 0,
    }
    before = [(f.normal.copy(), all(e.is_manifold for e in f.edges)) for f in bm.faces]
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    rep["flipped_faces"] = sum(1 for f, (n, ok) in zip(bm.faces, before) if ok and f.normal.dot(n) < 0.0)
    bm.free()
    rep["scale"] = [round(v, 6) for v in obj.scale]
    rep["rotation"] = [round(math.degrees(v), 4) for v in obj.rotation_euler]
    return rep

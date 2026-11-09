"""
VEYRA CITY - Arden Kite (stock) generator for Blender 5.2 LTS.

Fictional 2004 compact rear-drive 2+2 coupe. The car is rebuilt from scratch by
this script, so it can be regenerated and tweaked deterministically:

    blender -b --factory-startup --python build_arden_kite.py -- \
        --out E:/VeyraCity/ArtSource/Vehicles/ArdenKite [--render] [--export] [--verify]

Conventions (CLAUDE.md section 4, Docs/VEHICLE_ASSET_PIPELINE.md):
  * 1 Blender unit = 1 m. Front of car faces Blender -Y, up is +Z, the car's left is +X.
  * Root empty "ArdenKite_Stock" at ground level, centred between the axles.
  * Wheel_* and Caliper_* origins sit at the hub centres (location never applied).
  * FBX: forward -Z, up Y, apply unit, bake space transform -> front faces Unity +Z.
"""
import bpy, bmesh, math, os, sys, json
from mathutils import Vector, Matrix

# ----------------------------------------------------------------------------
# Spec (stock)
# ----------------------------------------------------------------------------
LENGTH, WIDTH, HEIGHT = 4.30, 1.78, 1.30
WHEELBASE, TRACK_F, TRACK_R = 2.55, 1.50, 1.50
Y_FRONT, Y_REAR = -2.175, 2.125          # 0.90 m front overhang, 0.85 m rear
Y_AXLE_F, Y_AXLE_R = -WHEELBASE / 2, WHEELBASE / 2
TYRE_R, TYRE_W, RIM_R = 0.316, 0.205, 0.2032
ARCH_R, ARCH_Z = 0.366, 0.322
SHELL = 0.02

HUBS = {
    "FL": Vector((+TRACK_F / 2, Y_AXLE_F, TYRE_R)),
    "FR": Vector((-TRACK_F / 2, Y_AXLE_F, TYRE_R)),
    "RL": Vector((+TRACK_R / 2, Y_AXLE_R, TYRE_R)),
    "RR": Vector((-TRACK_R / 2, Y_AXLE_R, TYRE_R)),
}

def args():
    a = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    o = {"out": "E:/VeyraCity/ArtSource/Vehicles/ArdenKite", "render": False, "export": False,
         "verify": False, "renders": "E:/VeyraCity/Logs/evidence/vehicle", "tag": ""}
    i = 0
    while i < len(a):
        k = a[i].lstrip("-")
        if k in ("render", "export", "verify"):
            o[k] = True
        elif k in o:
            o[k] = a[i + 1]; i += 1
        i += 1
    return o

# ----------------------------------------------------------------------------
# Small math helpers
# ----------------------------------------------------------------------------
def lerp(a, b, t): return a + (b - a) * t
def clamp(x, a, b): return max(a, min(b, x))
def smooth(t):
    t = clamp(t, 0.0, 1.0); return t * t * (3 - 2 * t)

def interp(points, y):
    """Monotone cubic (Fritsch-Carlson PCHIP) interpolation through (y, v) control points.
    Smooth (C1) without overshoot, so long panels have no ripples."""
    xs = [p[0] for p in points]; vs = [p[1] for p in points]
    n = len(xs)
    if y <= xs[0]: return vs[0]
    if y >= xs[-1]: return vs[-1]
    h = [xs[i + 1] - xs[i] for i in range(n - 1)]
    d = [(vs[i + 1] - vs[i]) / h[i] for i in range(n - 1)]
    m = [0.0] * n
    m[0], m[-1] = d[0], d[-1]
    for i in range(1, n - 1):
        if d[i - 1] * d[i] <= 0:
            m[i] = 0.0
        else:
            w1, w2 = 2 * h[i] + h[i - 1], h[i] + 2 * h[i - 1]
            m[i] = (w1 + w2) / (w1 / d[i - 1] + w2 / d[i])
    for i in range(n - 1):
        if xs[i] <= y <= xs[i + 1]:
            t = (y - xs[i]) / h[i]
            t2, t3 = t * t, t * t * t
            return ((2 * t3 - 3 * t2 + 1) * vs[i] + (t3 - 2 * t2 + t) * h[i] * m[i]
                    + (-2 * t3 + 3 * t2) * vs[i + 1] + (t3 - t2) * h[i] * m[i + 1])
    return vs[-1]

def gauss(y, c, s): return math.exp(-((y - c) / s) ** 2)

# ----------------------------------------------------------------------------
# Body profile (all heights in m above ground, y along the car, front = -Y)
# ----------------------------------------------------------------------------
Z_SHOULDER = [(-2.175, 0.560), (-2.10, 0.600), (-1.85, 0.652), (-1.275, 0.712), (-0.62, 0.832),
              (0.0, 0.866), (0.95, 0.890), (1.70, 0.902), (2.05, 0.900), (2.125, 0.868)]
Z_CENTRE = [(-2.175, 0.590), (-2.10, 0.624), (-1.85, 0.666), (-1.275, 0.718), (-0.66, 0.836),
            (-0.30, 1.065), (0.10, 1.268), (0.40, 1.300), (0.90, 1.262), (1.30, 1.112),
            (1.68, 0.938), (2.05, 0.926), (2.125, 0.896)]
Z_BOTTOM = [(-2.175, 0.235), (-1.95, 0.178), (-1.60, 0.150), (1.60, 0.150), (1.95, 0.215), (2.125, 0.300)]
W_SHOULDER = [(-2.175, 0.815), (-1.90, 0.842), (-1.275, 0.852), (-0.80, 0.840), (0.0, 0.836),
              (0.60, 0.842), (1.275, 0.860), (1.85, 0.842), (2.125, 0.800)]
CABIN = [(-2.175, 0.0), (-0.75, 0.0), (-0.45, 1.0), (1.45, 1.0), (1.85, 0.0), (2.125, 0.0)]

ROUND_F, ROUND_R = 0.42, 0.32     # plan-view corner rounding depth at nose / tail
P_F, P_R = 2.4, 2.8

def end_fade(y):
    """0 at the nose/tail closing lines, 1 along the body: fades creases out at the ends."""
    return smooth((min(y - Y_FRONT, Y_REAR - y) - 0.06) / 0.30)

def plan_scale(y):
    """Superellipse plan rounding of the nose and tail (1 = full width, 0 = closing line)."""
    if y < Y_FRONT + ROUND_F:
        d = clamp((Y_FRONT + ROUND_F - y) / ROUND_F, 0.0, 1.0)
        return (1.0 - d ** P_F) ** (1.0 / P_F)
    if y > Y_REAR - ROUND_R:
        d = clamp((y - (Y_REAR - ROUND_R)) / ROUND_R, 0.0, 1.0)
        return (1.0 - d ** P_R) ** (1.0 / P_R)
    return 1.0

def section(y):
    """Half cross-section (left side, +X) from open bottom edge to roof centreline."""
    zs = interp(Z_SHOULDER, y)
    zc = interp(Z_CENTRE, y)
    zb = interp(Z_BOTTOM, y)
    ws = interp(W_SHOULDER, y)
    flare = 0.016 * gauss(y, Y_AXLE_F, 0.42) + 0.024 * gauss(y, Y_AXLE_R, 0.48)
    wmax = ws + 0.010 + flare
    g = interp(CABIN, y)
    sx = plan_scale(y)

    pts = []
    # lower body: bottom inner -> rocker -> side bulge -> under shoulder
    pts.append((wmax - 0.17, zb - 0.004))
    pts.append((wmax - 0.075, zb))
    pts.append((wmax - 0.032, zb + 0.055))
    pts.append((wmax - 0.008, zb + 0.16))
    pts.append((wmax, lerp(zb, zs, 0.52)))
    pts.append((wmax - 0.004 - 0.4 * flare, zs - 0.075))
    i_shoulder = len(pts)
    pts.append((ws + 0.012, zs))                       # shoulder crease
    # belt ledge / fender inner edge
    wb = ws - lerp(0.030, 0.050, g)
    zbelt = zs + lerp(0.006, 0.022, g)
    pts.append((wb, zbelt))
    # top curve: hood/deck (g=0) blended with superellipse greenhouse (g=1)
    h = max(zc - zbelt, 0.0)
    n = 3.2
    for t in (0.14, 0.30, 0.46, 0.62, 0.77, 0.90, 1.0):
        # hood: almost flat with a small crown and a gentle valley to the fender line
        xh = wb * (1.0 - t)
        zh = zbelt + (zc - zbelt) * t + 0.012 * math.sin(math.pi * t) * (1 - g) * end_fade(y)
        phi = t * math.pi / 2
        c, s = math.cos(phi), math.sin(phi)
        fz = s ** (2.0 / n)
        xg = wb * (c ** (2.0 / n)) * (1.0 - 0.10 * fz)
        zg = zbelt + h * fz
        pts.append((lerp(xh, xg, g), lerp(zh, zg, g)))
    pts[-1] = (0.0, pts[-1][1])
    # At the nose/tail every ring point collapses onto the centreline (x -> 0). The closing
    # line must then rise monotonically in z, or it folds back on itself (cusp, shell spike).
    tip = 1.0 - sx
    if tip > 0:
        for i in range(1, len(pts)):
            x, z = pts[i]
            pts[i] = (x, max(z, pts[i - 1][1] + 0.0015 * tip))
    out = [(x * sx, z) for (x, z) in pts]
    return out, i_shoulder

def stations():
    """Loft stations: dense in the nose/tail rounding zones, even along the body."""
    ys = []
    nf = 9
    for i in range(nf + 1):                       # nose tip -> start of body
        ys.append(Y_FRONT + ROUND_F * (1 - math.cos(i / nf * math.pi / 2)))
    y0, y1 = Y_FRONT + ROUND_F, Y_REAR - ROUND_R
    nb = 46
    for i in range(1, nb):
        ys.append(lerp(y0, y1, i / nb))
    nr = 8
    for i in range(nr + 1):                       # end of body -> tail tip
        ys.append(y1 + ROUND_R * math.sin(i / nr * math.pi / 2))
    return ys

# ----------------------------------------------------------------------------
# Scene / object helpers
# ----------------------------------------------------------------------------
def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    sc.unit_settings.system = 'METRIC'
    sc.unit_settings.scale_length = 1.0
    return sc

def link(ob, parent=None):
    bpy.context.scene.collection.objects.link(ob)
    if parent is not None:
        ob.parent = parent
        ob.matrix_parent_inverse = Matrix.Identity(4)
    return ob

def obj_from_bm(name, bm, mats=None, parent=None, location=(0, 0, 0)):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me); bm.free()
    for m in (mats or []):
        me.materials.append(m)
    ob = bpy.data.objects.new(name, me)
    ob.location = location
    return link(ob, parent)

def apply_modifiers(ob):
    dg = bpy.context.evaluated_depsgraph_get()
    ev = ob.evaluated_get(dg)
    me = bpy.data.meshes.new_from_object(ev, preserve_all_data_layers=True, depsgraph=dg)
    old = ob.data
    ob.modifiers.clear()
    ob.data = me
    me.name = ob.name
    if old.users == 0:
        bpy.data.meshes.remove(old)

def shade(ob, angle=35.0):
    me = ob.data
    me.shade_smooth()
    me.set_sharp_from_angle(angle=math.radians(angle))

def tri_count(ob):
    return sum(len(p.vertices) - 2 for p in ob.data.polygons)

# ----------------------------------------------------------------------------
# Materials
# ----------------------------------------------------------------------------
def principled(mat):
    return next(n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED")

def make_mat(name, color, metallic=0.0, rough=0.5, coat=0.0, coat_rough=0.03, emission=None,
             emission_strength=0.0, transmission=0.0, alpha=1.0, ior=1.45):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    b = principled(m)
    b.inputs["Base Color"].default_value = (*color, 1.0)
    b.inputs["Metallic"].default_value = metallic
    b.inputs["Roughness"].default_value = rough
    b.inputs["Coat Weight"].default_value = coat
    b.inputs["Coat Roughness"].default_value = coat_rough
    b.inputs["IOR"].default_value = ior
    if emission is not None:
        b.inputs["Emission Color"].default_value = (*emission, 1.0)
        b.inputs["Emission Strength"].default_value = emission_strength
    if transmission > 0:
        b.inputs["Transmission Weight"].default_value = transmission
    if alpha < 1.0:
        b.inputs["Alpha"].default_value = alpha
        m.surface_render_method = 'BLENDED'
    m.diffuse_color = (*color, alpha)
    return m

def materials():
    M = {}
    M["M_Paint"] = make_mat("M_Paint", (0.56, 0.58, 0.60), metallic=0.85, rough=0.32, coat=1.0, coat_rough=0.03)
    M["M_Glass"] = make_mat("M_Glass", (0.015, 0.02, 0.025), metallic=0.0, rough=0.0, transmission=0.0, alpha=0.62, ior=1.5)
    M["M_Rubber"] = make_mat("M_Rubber", (0.025, 0.025, 0.027), rough=0.82)
    M["M_Rim"] = make_mat("M_Rim", (0.70, 0.71, 0.72), metallic=1.0, rough=0.24)
    M["M_Chrome"] = make_mat("M_Chrome", (0.92, 0.92, 0.93), metallic=1.0, rough=0.06)
    M["M_PlasticBlack"] = make_mat("M_PlasticBlack", (0.018, 0.018, 0.02), rough=0.55)
    M["M_Interior"] = make_mat("M_Interior", (0.035, 0.035, 0.038), rough=0.75)
    M["M_Disc"] = make_mat("M_Disc", (0.42, 0.42, 0.42), metallic=1.0, rough=0.42)
    M["M_Caliper"] = make_mat("M_Caliper", (0.55, 0.05, 0.04), metallic=0.2, rough=0.4, coat=0.5)
    M["M_LightHead"] = make_mat("M_LightHead", (0.85, 0.88, 0.92), metallic=0.0, rough=0.02, alpha=0.22,
                                 emission=(1.0, 0.97, 0.9), emission_strength=0.0)
    M["M_LightTail"] = make_mat("M_LightTail", (0.30, 0.008, 0.008), rough=0.08,
                                 emission=(1.0, 0.04, 0.02), emission_strength=0.35)
    M["M_LightIndicator"] = make_mat("M_LightIndicator", (0.75, 0.38, 0.03), rough=0.12,
                                      emission=(1.0, 0.45, 0.02), emission_strength=0.0)
    M["M_LightReverse"] = make_mat("M_LightReverse", (0.85, 0.85, 0.86), rough=0.10,
                                    emission=(1.0, 1.0, 1.0), emission_strength=0.0)
    return M

# ----------------------------------------------------------------------------
# Body surface (loft cage + subdivision)
# ----------------------------------------------------------------------------
def body_cage():
    ys = stations()
    rings = []
    i_sh = 0
    for y in ys:
        sec, i_sh = section(y)
        rings.append((y, sec))
    m = len(rings[0][1])            # points per half ring (last one on centreline)
    bm = bmesh.new()
    grid = []
    for (y, sec) in rings:
        row_l = [bm.verts.new((x, y, z)) for (x, z) in sec]
        row_r = [bm.verts.new((-x, y, z)) for (x, z) in sec[:-1]]
        # full ring: left bottom -> ... -> centre -> ... -> right bottom
        grid.append(row_l + list(reversed(row_r)))
    n = len(grid[0])
    for j in range(len(grid) - 1):
        a, b = grid[j], grid[j + 1]
        for i in range(n - 1):
            bm.faces.new((a[i], a[i + 1], b[i + 1], b[i]))
    # the first and last rings are closing lines on the centreline: merge them
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
    bm.normal_update()
    # outward normals: consistent winding, then flip if they point towards the car's axis
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    score = 0.0
    for f in bm.faces:
        c = f.calc_center_median()
        score += f.normal.dot(Vector((c.x, 0.0, c.z - 0.62))) * f.calc_area()
    if score < 0:
        bmesh.ops.reverse_faces(bm, faces=bm.faces)
    # crease along the shoulder line (both sides) and the rocker
    crease = bm.edges.layers.float.get("crease_edge") or bm.edges.layers.float.new("crease_edge")
    sh_l, sh_r = i_sh, n - 1 - i_sh
    for j in range(len(grid) - 1):
        f = min(end_fade(rings[j][0]), end_fade(rings[j + 1][0]))
        # shoulder and rocker creases fade out at the ends; the mid-height character line
        # keeps going and wraps across the nose and tail as a bumper line
        for col, c in ((sh_l, 0.85 * f), (sh_r, 0.85 * f), (2, 0.35 * f), (n - 3, 0.35 * f),
                       (4, 0.40), (n - 5, 0.40)):
            va, vb = grid[j][col], grid[j + 1][col]
            if va.is_valid and vb.is_valid:
                e = bm.edges.get((va, vb))
                if e is not None:
                    e[crease] = c
    return bm

def body_surface(M):
    bm = body_cage()
    ob = obj_from_bm("BodySurface", bm)
    sub = ob.modifiers.new("Subsurf", 'SUBSURF')
    sub.levels = 2; sub.render_levels = 2; sub.quality = 3
    apply_modifiers(ob)
    return ob

# ----------------------------------------------------------------------------
# Cutters (closed prisms), 2D outlines
# ----------------------------------------------------------------------------
def rounded_poly(pts, r=0.03, seg=4):
    """Round the corners of a closed 2D polygon."""
    out = []
    n = len(pts)
    for i in range(n):
        p0, p1, p2 = Vector(pts[i - 1]), Vector(pts[i]), Vector(pts[(i + 1) % n])
        d0, d1 = (p0 - p1), (p2 - p1)
        l0, l1 = d0.length, d1.length
        rr = min(r, 0.45 * l0, 0.45 * l1)
        a = p1 + d0.normalized() * rr
        b = p1 + d1.normalized() * rr
        for k in range(seg + 1):
            t = k / seg
            q = (1 - t) ** 2 * a + 2 * (1 - t) * t * p1 + t * t * b
            out.append((q.x, q.y))
    return out

def prism_bm(bm, outline, axis, lo, hi):
    """Extrude a closed 2D outline along an axis ('x','y','z') between lo and hi."""
    def P(u, v, w):
        if axis == 'x': return (w, u, v)      # outline in (y, z)
        if axis == 'y': return (u, w, v)      # outline in (x, z)
        return (u, v, w)                      # outline in (x, y)
    a = [bm.verts.new(P(u, v, lo)) for (u, v) in outline]
    b = [bm.verts.new(P(u, v, hi)) for (u, v) in outline]
    n = len(outline)
    for i in range(n):
        bm.faces.new((a[i], a[(i + 1) % n], b[(i + 1) % n], b[i]))
    bm.faces.new(list(reversed(a)))
    bm.faces.new(b)

def cylinder_bm(bm, centre, radius, axis, lo, hi, seg=64):
    outline = [(centre[0] + radius * math.cos(2 * math.pi * k / seg),
                centre[1] + radius * math.sin(2 * math.pi * k / seg)) for k in range(seg)]
    prism_bm(bm, outline, axis, lo, hi)

def box_bm(bm, mn, mx):
    v = [bm.verts.new((x, y, z)) for x in (mn[0], mx[0]) for y in (mn[1], mx[1]) for z in (mn[2], mx[2])]
    for f in ((0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)):
        bm.faces.new([v[i] for i in f])

def finish_closed(bm):
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return bm

# Window / light outlines ----------------------------------------------------
def side_window_outline():
    # (y, z) in side view; front follows the A-pillar rake, rear the C-pillar
    return rounded_poly([(-0.47, 0.915), (0.80, 0.945), (1.06, 0.975), (0.70, 1.205),
                         (0.10, 1.228), (-0.12, 1.140)], r=0.06, seg=5)

def windshield_outline():
    # (x, y) plan view
    return rounded_poly([(-0.69, -0.585), (0.69, -0.585), (0.565, 0.080), (-0.565, 0.080)], r=0.08, seg=5)

def rear_window_outline():
    return rounded_poly([(-0.535, 1.000), (0.535, 1.000), (0.640, 1.655), (-0.640, 1.655)], r=0.08, seg=5)

HEAD_Z = (0.470, 0.720)      # z band of the front light cluster cut
HEAD_SPLIT = 0.530           # below: indicator, above: headlight
TAIL_Z = (0.735, 0.875)
TAIL_SPLIT_Z, TAIL_SPLIT_X = 0.790, 0.500

def headlight_plan(side):
    """Plan-view (x, y) outline of the swept front light cluster; it wraps the corner."""
    pts = [(0.30, -2.32), (0.66, -2.24), (0.92, -2.02), (0.95, -1.83),
           (0.84, -1.80), (0.74, -1.86), (0.60, -1.97), (0.44, -2.06), (0.30, -2.11)]
    pts = rounded_poly(pts, r=0.03, seg=4)
    return [(side * x, y) for (x, y) in pts]

def taillight_plan(side):
    """Plan-view (x, y) outline of the rear light band; it wraps onto the quarter panel."""
    pts = [(0.25, 2.30), (0.95, 2.10), (0.98, 1.90), (0.80, 1.89), (0.74, 1.97), (0.60, 2.04), (0.25, 2.07)]
    pts = rounded_poly(pts, r=0.03, seg=4)
    return [(side * x, y) for (x, y) in pts]

def intake_outlines():
    main = rounded_poly([(-0.46, 0.255), (0.46, 0.255), (0.40, 0.400), (-0.40, 0.400)], r=0.05, seg=5)
    side = []
    for s in (1, -1):
        side.append(rounded_poly([(s * 0.56, 0.250), (s * 0.73, 0.262), (s * 0.70, 0.335), (s * 0.57, 0.335)],
                                 r=0.02, seg=3))
    return [main] + side

def plate_outline():
    return rounded_poly([(-0.265, 0.470), (0.265, 0.470), (0.265, 0.630), (-0.265, 0.630)], r=0.02, seg=3)

def point_in_poly(p, poly):
    x, y = p; inside = False
    n = len(poly)
    for i in range(n):
        x1, y1 = poly[i]; x2, y2 = poly[(i + 1) % n]
        if (y1 > y) != (y2 > y):
            xi = x1 + (y - y1) * (x2 - x1) / (y2 - y1)
            if x < xi: inside = not inside
    return inside

def dist_to_poly(p, poly):
    best = 1e9
    P = Vector(p)
    n = len(poly)
    for i in range(n):
        a, b = Vector(poly[i]), Vector(poly[(i + 1) % n])
        ab = b - a
        t = clamp((P - a).dot(ab) / max(ab.length_squared, 1e-12), 0.0, 1.0)
        best = min(best, (a + ab * t - P).length)
    return best

def inside_with_margin(p, poly, margin):
    return point_in_poly(p, poly) or dist_to_poly(p, poly) <= margin

# ----------------------------------------------------------------------------
# Build the body shell with cut-outs
# ----------------------------------------------------------------------------
def build_cutters(M):
    cut = {}
    # wheel arches (cylinders along X on each side)
    bm = bmesh.new()
    for y in (Y_AXLE_F, Y_AXLE_R):
        for s in (1, -1):
            lo, hi = (0.54, 1.30) if s > 0 else (-1.30, -0.54)
            cylinder_bm(bm, (y, ARCH_Z), ARCH_R, 'x', lo, hi, seg=72)
    cut["arches"] = bm
    # windows
    bm = bmesh.new()
    sw = side_window_outline()
    prism_bm(bm, sw, 'x', 0.30, 1.30)
    prism_bm(bm, sw, 'x', -1.30, -0.30)
    cut["side_windows"] = bm
    bm = bmesh.new()
    prism_bm(bm, windshield_outline(), 'z', 0.80, 1.50)
    prism_bm(bm, rear_window_outline(), 'z', 0.85, 1.50)
    cut["front_rear_glass"] = bm
    # lights + intakes (holes through the shell; lenses / grilles fill them)
    bm = bmesh.new()
    for s in (1, -1):
        prism_bm(bm, headlight_plan(s), 'z', *HEAD_Z)
        prism_bm(bm, taillight_plan(s), 'z', *TAIL_Z)
    for o in intake_outlines():
        prism_bm(bm, o, 'y', -2.40, -1.85)
    prism_bm(bm, plate_outline(), 'y', 1.98, 2.40)      # rear licence plate recess
    cut["lights"] = bm
    # panel gaps (thin slabs through the shell)
    g = 0.0045
    bm = bmesh.new()
    for s in (1, -1):
        xs = (0.60, 1.20) if s > 0 else (-1.20, -0.60)
        box_bm(bm, (xs[0], -0.83 - g / 2, 0.235), (xs[1], -0.83 + g / 2, 0.875))   # door front
        box_bm(bm, (xs[0], 0.62 - g / 2, 0.235), (xs[1], 0.62 + g / 2, 0.905))    # door rear
        box_bm(bm, (xs[0], -0.83, 0.262 - g / 2), (xs[1], 0.62, 0.262 + g / 2))   # door sill line
        xh = 0.625 * s
        box_bm(bm, (xh - g / 2, -2.07, 0.55), (xh + g / 2, -0.69, 0.95))           # hood shut lines
        xt = 0.520 * s
        # trunk lid: shut lines along the deck, then down the tail face to the lid bottom
        box_bm(bm, (xt - g / 2, 1.70, 0.86), (xt + g / 2, 2.30, 1.05))
        box_bm(bm, (xt - g / 2, 1.96, 0.690), (xt + g / 2, 2.30, 0.95))
    box_bm(bm, (-0.625, -0.69 - g / 2, 0.70), (0.625, -0.69 + g / 2, 0.95))      # cowl
    box_bm(bm, (-0.625, -2.07 - g / 2, 0.55), (0.625, -2.07 + g / 2, 0.80))      # hood front
    box_bm(bm, (-0.520, 1.70 - g / 2, 0.85), (0.520, 1.70 + g / 2, 1.05))        # trunk front
    box_bm(bm, (-0.520, 1.96, 0.690 - g / 2), (0.520, 2.30, 0.690 + g / 2))      # trunk lid bottom
    cut["gaps"] = bm
    obs = []
    for k, bm in cut.items():
        finish_closed(bm)
        ob = obj_from_bm("CUT_" + k, bm, mats=[M["M_PlasticBlack"]])
        ob.hide_render = True
        obs.append(ob)
    return obs

def build_body(M, surface, root):
    # shell = copy of surface + solidify inward + cut-outs
    me = surface.data.copy()
    ob = bpy.data.objects.new("Body_LOD0", me)
    link(ob, root)
    me.materials.clear()
    me.materials.append(M["M_Paint"])
    me.materials.append(M["M_Interior"])
    me.materials.append(M["M_PlasticBlack"])
    sol = ob.modifiers.new("Shell", 'SOLIDIFY')
    sol.thickness = SHELL; sol.offset = -1.0
    sol.use_even_offset = False; sol.use_quality_normals = True
    sol.material_offset = 1; sol.material_offset_rim = 2
    apply_modifiers(ob)
    cutters = build_cutters(M)
    for c in cutters:
        md = ob.modifiers.new(c.name, 'BOOLEAN')
        md.operation = 'DIFFERENCE'; md.object = c
        md.solver = 'MANIFOLD'
        md.material_mode = 'TRANSFER'
        apply_modifiers(ob)
    for c in cutters:
        bpy.data.objects.remove(c, do_unlink=True)
    shade(ob, 32.0)
    return ob

# ----------------------------------------------------------------------------
# Glass and light lenses: patches of the outer surface, inset into the shell wall
# ----------------------------------------------------------------------------
def patch_from_surface(surface, name, select_fn, inset, mats, root, mat_fn=None):
    bm = bmesh.new()
    bm.from_mesh(surface.data)
    bm.normal_update()
    keep = set()
    for f in bm.faces:
        c = f.calc_center_median()
        if select_fn(c):
            keep.add(f)
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if f not in keep], context='FACES')
    bm.normal_update()
    for v in bm.verts:
        v.co = v.co - v.normal * inset
    if mat_fn is not None:
        for f in bm.faces:
            f.material_index = mat_fn(f.calc_center_median())
    ob = obj_from_bm(name, bm, mats=mats, parent=root)
    shade(ob, 60.0)
    return ob

def build_glass(M, surface, root):
    sw, ws, rw = side_window_outline(), windshield_outline(), rear_window_outline()
    mg = 0.012
    def sel(c):
        if abs(c.x) > 0.30 and inside_with_margin((c.y, c.z), sw, mg) and c.z > 0.85:
            return True
        if c.z > 0.80 and inside_with_margin((c.x, c.y), ws, mg) and -0.62 < c.y < 0.12:
            return True
        if c.z > 0.85 and inside_with_margin((c.x, c.y), rw, mg) and 0.96 < c.y < 1.70:
            return True
        return False
    return patch_from_surface(surface, "Glass", sel, SHELL * 0.45, [M["M_Glass"]], root)

IND_X = 0.680               # front indicators sit on the outer corner of the cluster

def surface_patch_bm(surface, select_fn, inset):
    """Faces of the outer surface whose centre passes select_fn, pushed inward by `inset`."""
    bm = bmesh.new()
    bm.from_mesh(surface.data)
    bm.normal_update()
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if not select_fn(f.calc_center_median())], context='FACES')
    bm.normal_update()
    for v in bm.verts:
        v.co = v.co - v.normal * inset
    return bm

def bisect(bm, planes):
    """Cut the patch along exact planes so material/object splits have straight, clean edges."""
    for co, no in planes:
        geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
        bmesh.ops.bisect_plane(bm, geom=geom, dist=1e-6, plane_co=co, plane_no=no)
    return bm

def subset_bm(bm, keep_fn):
    out = bm.copy()
    bmesh.ops.delete(out, geom=[f for f in out.faces if not keep_fn(f.calc_center_median())], context='FACES')
    return out

def build_lights(M, surface, root):
    mg = 0.012
    hl = {1: headlight_plan(1), -1: headlight_plan(-1)}
    tl = {1: taillight_plan(1), -1: taillight_plan(-1)}
    def in_head(c):
        return (c.y < -1.70 and HEAD_Z[0] - mg <= c.z <= HEAD_Z[1] + mg
                and any(inside_with_margin((c.x, c.y), hl[k], mg) for k in (1, -1)))
    def in_tail(c):
        return (c.y > 1.80 and TAIL_Z[0] - mg <= c.z <= TAIL_Z[1] + mg
                and any(inside_with_margin((c.x, c.y), tl[k], mg) for k in (1, -1)))
    head = surface_patch_bm(surface, in_head, SHELL * 0.5)
    bisect(head, [((0, 0, HEAD_SPLIT), (0, 0, 1)), ((IND_X, 0, 0), (1, 0, 0)), ((-IND_X, 0, 0), (1, 0, 0))])
    tail = surface_patch_bm(surface, in_tail, SHELL * 0.5)
    bisect(tail, [((0, 0, TAIL_SPLIT_Z), (0, 0, 1)), ((TAIL_SPLIT_X, 0, 0), (1, 0, 0)),
                  ((-TAIL_SPLIT_X, 0, 0), (1, 0, 0))])
    is_ind = lambda c: c.z < HEAD_SPLIT and abs(c.x) > IND_X
    is_rear_ind = lambda c: abs(c.x) > TAIL_SPLIT_X and c.z < TAIL_SPLIT_Z

    obs = []
    # --- headlights: clear lens + chrome reflector bowl + two projectors per side
    lens = subset_bm(head, lambda c: not is_ind(c))
    refl = lens.copy()
    refl.normal_update()
    for v in refl.verts:
        v.co = v.co - v.normal * 0.030
    for f in refl.faces:
        f.material_index = 1
    lens_ob = obj_from_bm("Lights_Head", lens, mats=[M["M_LightHead"], M["M_Chrome"], M["M_PlasticBlack"]], parent=root)
    refl_ob = obj_from_bm("tmp_reflector", refl, mats=[M["M_LightHead"], M["M_Chrome"], M["M_PlasticBlack"]])
    pbm = bmesh.new()
    lens_pts = [v.co.copy() for v in lens_ob.data.vertices]
    for s in (1, -1):
        for (px, pz) in ((0.47, 0.598), (0.615, 0.606)):
            near = min(lens_pts, key=lambda p: (p.x - s * px) ** 2 + (p.z - pz) ** 2)
            c = Vector((s * px, near.y + 0.024, pz))
            add_ellipsoid(pbm, c, (0.030, 0.012, 0.030), mat=1, seg=20, rings=10)
            add_ellipsoid(pbm, c + Vector((0, -0.010, 0)), (0.018, 0.006, 0.018), mat=2, seg=16, rings=8)
    proj_ob = obj_from_bm("tmp_projectors", pbm, mats=[M["M_LightHead"], M["M_Chrome"], M["M_PlasticBlack"]])
    join_into(lens_ob, [refl_ob, proj_ob])
    shade(lens_ob, 60.0)
    obs.append(lens_ob)
    # --- indicators (front corner + rear outer-lower segment), one object per side
    for name, s in (("Indicator_L", 1), ("Indicator_R", -1)):
        f_bm = subset_bm(head, lambda c, s=s: is_ind(c) and c.x * s > 0)
        r_bm = subset_bm(tail, lambda c, s=s: is_rear_ind(c) and c.x * s > 0)
        a = obj_from_bm(name, f_bm, mats=[M["M_LightIndicator"]], parent=root)
        b = obj_from_bm("tmp_" + name, r_bm, mats=[M["M_LightIndicator"]])
        join_into(a, [b]); shade(a, 60.0)
        obs.append(a)
    # --- rear lights
    for name, fn, mat in (("Lights_Tail", lambda c: abs(c.x) > TAIL_SPLIT_X and c.z >= TAIL_SPLIT_Z, "M_LightTail"),
                          ("Lights_Brake", lambda c: abs(c.x) <= TAIL_SPLIT_X and c.z >= TAIL_SPLIT_Z, "M_LightTail"),
                          ("Lights_Reverse", lambda c: abs(c.x) <= TAIL_SPLIT_X and c.z < TAIL_SPLIT_Z, "M_LightReverse")):
        ob = obj_from_bm(name, subset_bm(tail, fn), mats=[M[mat]], parent=root)
        shade(ob, 60.0)
        obs.append(ob)
    head.free(); tail.free()
    return obs

# ----------------------------------------------------------------------------
# Generic mesh builders
# ----------------------------------------------------------------------------
def lathe(bm, profile, seg, mat=0, axis_sign=1.0, centre=(0, 0, 0), angle0=0.0, closed=True):
    """Revolve (r, a) profile points around the X axis. Returns the created faces."""
    cx, cy, cz = centre
    rings = []
    for k in range(seg):
        th = angle0 + 2 * math.pi * k / seg
        c, s = math.cos(th), math.sin(th)
        rings.append([bm.verts.new((cx + axis_sign * a, cy + r * c, cz + r * s)) for (r, a) in profile])
    faces = []
    n = len(profile)
    for k in range(seg):
        r0, r1 = rings[k], rings[(k + 1) % seg]
        for i in range(n - (0 if closed else 1)):
            i2 = (i + 1) % n
            f = bm.faces.new((r0[i], r0[i2], r1[i2], r1[i]))
            f.material_index = mat; f.smooth = True
            faces.append(f)
    return faces

def add_box(bm, mn, mx, mat=0, M4=None):
    v = [bm.verts.new((x, y, z)) for x in (mn[0], mx[0]) for y in (mn[1], mx[1]) for z in (mn[2], mx[2])]
    fs = []
    for f in ((0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)):
        face = bm.faces.new([v[i] for i in f]); face.material_index = mat; fs.append(face)
    if M4 is not None:
        for vv in v: vv.co = M4 @ vv.co
    return v

def add_torus(bm, R, r, seg=48, rseg=12, mat=0, M4=None, arc=2 * math.pi):
    rings = []
    full = abs(arc - 2 * math.pi) < 1e-6
    nseg = seg if full else seg + 1
    for k in range(nseg):
        u = arc * k / seg
        ring = []
        for j in range(rseg):
            v = 2 * math.pi * j / rseg
            p = Vector(((R + r * math.cos(v)) * math.cos(u), (R + r * math.cos(v)) * math.sin(u), r * math.sin(v)))
            if M4 is not None: p = M4 @ p
            ring.append(bm.verts.new(p))
        rings.append(ring)
    for k in range(seg):
        a, b = rings[k], rings[(k + 1) % nseg]
        for j in range(rseg):
            f = bm.faces.new((a[j], a[(j + 1) % rseg], b[(j + 1) % rseg], b[j]))
            f.material_index = mat; f.smooth = True

def add_ellipsoid(bm, centre, radii, mat=0, seg=24, rings=12):
    res = bmesh.ops.create_uvsphere(bm, u_segments=seg, v_segments=rings, radius=1.0)
    for v in res["verts"]:
        v.co = Vector((centre[0] + v.co.x * radii[0], centre[1] + v.co.y * radii[1], centre[2] + v.co.z * radii[2]))
    for f in {f for v in res["verts"] for f in v.link_faces}:
        f.material_index = mat; f.smooth = True
    return res["verts"]

# ----------------------------------------------------------------------------
# Wheels and calipers (built around the hub at the origin, outer face = side * +X)
# ----------------------------------------------------------------------------
def tyre_profile():
    hw = TYRE_W / 2
    p = []
    # outer bead -> sidewall -> shoulder -> tread (with 3 grooves) -> shoulder -> sidewall -> inner bead
    side = [(0.205, 0.088), (0.228, 0.097), (0.255, 0.1015), (0.280, 0.1010), (0.298, 0.0975), (0.309, 0.0905)]
    tread = []
    for a in (0.082, 0.064, 0.046, 0.043, 0.040, 0.037, 0.020, 0.004, 0.0015, -0.0015, -0.004, -0.020,
              -0.037, -0.040, -0.043, -0.046, -0.064, -0.082):
        groove = any(abs(a - g) < 0.0035 for g in (0.0415, 0.0, -0.0415))
        tread.append((TYRE_R - (0.006 if groove else 0.0), a))
    p = side + [(0.3145, 0.086)] + tread + [(0.3145, -0.086)] + [(r, -a) for (r, a) in reversed(side)]
    p += [(0.200, -0.088), (0.200, 0.088)]
    return p

def build_wheel(name, side, M, root, hub):
    bm = bmesh.new()
    s = side  # +1 left (outer face +X), -1 right
    # tyre
    lathe(bm, tyre_profile(), 60, mat=0, axis_sign=s)
    # rim barrel + outer lip (profile r, a)
    barrel = [(0.214, 0.090), (0.205, 0.094), (0.193, 0.088), (0.188, 0.070), (0.185, -0.075),
              (0.192, -0.090), (0.202, -0.092), (0.202, -0.082), (0.195, -0.074), (0.198, 0.065), (0.206, 0.082)]
    lathe(bm, barrel, 60, mat=1, axis_sign=s)
    # brake disc + hat
    disc = [(0.145, 0.004), (0.145, -0.022), (0.080, -0.022), (0.080, 0.004)]
    lathe(bm, disc, 64, mat=2, axis_sign=s)
    hat = [(0.082, 0.004), (0.082, 0.030), (0.060, 0.050), (0.040, 0.050), (0.040, 0.004)]
    lathe(bm, hat, 32, mat=2, axis_sign=s)
    # hub face + centre cap
    hubp = [(0.000, 0.066), (0.052, 0.066), (0.072, 0.060), (0.074, 0.040), (0.050, 0.035), (0.000, 0.035)]
    lathe(bm, hubp, 40, mat=1, axis_sign=s)
    cap = [(0.000, 0.078), (0.024, 0.077), (0.030, 0.070), (0.030, 0.064), (0.000, 0.064)]
    lathe(bm, cap, 24, mat=3, axis_sign=s)
    # twin 5-spoke design (10 spokes)
    for i in range(5):
        for off in (-7.5, 7.5):
            th = math.radians(i * 72 + off + 18)
            Rm = Matrix.Rotation(th, 4, 'X')
            # spoke as a tapered, dished box from r=0.062 to r=0.192
            r0, r1 = 0.062, 0.193
            w0, w1 = 0.016, 0.011
            a_in0, a_out0 = 0.040, 0.066   # axial extent near the hub
            a_in1, a_out1 = 0.060, 0.084   # near the lip (concave face)
            def P(r, w, a): return Rm @ Vector((s * a, r, w))
            vs = [P(r0, -w0, a_in0), P(r0, w0, a_in0), P(r0, w0, a_out0), P(r0, -w0, a_out0),
                  P(r1, -w1, a_in1), P(r1, w1, a_in1), P(r1, w1, a_out1), P(r1, -w1, a_out1)]
            v = [bm.verts.new(p) for p in vs]
            for f in ((0, 1, 2, 3), (4, 7, 6, 5), (0, 4, 5, 1), (1, 5, 6, 2), (2, 6, 7, 3), (3, 7, 4, 0)):
                face = bm.faces.new([v[k] for k in f]); face.material_index = 1
    # lug nuts
    for i in range(5):
        th = math.radians(i * 72)
        c = Vector((0.0, 0.050 * math.cos(th), 0.050 * math.sin(th)))
        prof = [(0.000, 0.084), (0.009, 0.084), (0.011, 0.079), (0.011, 0.064), (0.000, 0.064)]
        lathe(bm, prof, 12, mat=3, axis_sign=s, centre=(0.0, c.y, c.z))
    clean_bm(bm)
    ob = obj_from_bm(name, bm, mats=[M["M_Rubber"], M["M_Rim"], M["M_Disc"], M["M_Chrome"]],
                     parent=root, location=hub)
    bev = ob.modifiers.new("Bevel", 'BEVEL')
    bev.width = 0.0025; bev.segments = 1; bev.limit_method = 'ANGLE'; bev.angle_limit = math.radians(50)
    apply_modifiers(ob)
    shade(ob, 40.0)
    return ob

def build_caliper(name, side, M, root, hub, angle_deg=12.0):
    bm = bmesh.new()
    s = side
    span = math.radians(58)
    th0 = math.radians(angle_deg) - span / 2
    segs = 10
    # torus-sector block: radial 0.118..0.172, axial -0.038..+0.026 (straddles the disc)
    prof = [(0.118, -0.038), (0.172, -0.038), (0.176, -0.006), (0.172, 0.026), (0.118, 0.026), (0.114, -0.006)]
    rings = []
    for k in range(segs + 1):
        th = th0 + span * k / segs
        c, sn = math.cos(th), math.sin(th)
        rings.append([bm.verts.new((s * a, r * c, r * sn)) for (r, a) in prof])
    n = len(prof)
    for k in range(segs):
        for i in range(n):
            f = bm.faces.new((rings[k][i], rings[k][(i + 1) % n], rings[k + 1][(i + 1) % n], rings[k + 1][i]))
            f.smooth = True
    bm.faces.new(list(reversed(rings[0])))
    bm.faces.new(rings[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = obj_from_bm(name, bm, mats=[M["M_Caliper"]], parent=root, location=hub)
    bev = ob.modifiers.new("Bevel", 'BEVEL')
    bev.width = 0.004; bev.segments = 2; bev.limit_method = 'ANGLE'
    apply_modifiers(ob)
    shade(ob, 40.0)
    return ob

# ----------------------------------------------------------------------------
# Other parts
# ----------------------------------------------------------------------------
def build_underbody(M, root):
    bm = bmesh.new()
    # floor pan between the wheel wells, slightly above ground clearance
    add_box(bm, (-0.62, -1.95, 0.130), (0.62, 1.95, 0.150), mat=0)
    # wheel well liners (half cylinders, open below), inner walls
    for y in (Y_AXLE_F, Y_AXLE_R):
        for s in (1, -1):
            x0, x1 = (0.53, 0.86) if s > 0 else (-0.86, -0.53)
            seg = 40
            r = ARCH_R - 0.004
            ring0, ring1 = [], []
            for k in range(seg + 1):
                th = math.radians(-12) + (math.radians(204)) * k / seg
                yy, zz = y + r * math.cos(th), ARCH_Z + r * math.sin(th)
                ring0.append(bm.verts.new((x0, yy, zz)))
                ring1.append(bm.verts.new((x1, yy, zz)))
            for k in range(seg):
                f = bm.faces.new((ring0[k], ring1[k], ring1[k + 1], ring0[k + 1])); f.material_index = 0
            # inner wall (fan)
            xc = x0 if s > 0 else x1
            c = bm.verts.new((xc, y, ARCH_Z - 0.05))
            for k in range(seg):
                a = ring0[k] if s > 0 else ring1[k]
                b = ring0[k + 1] if s > 0 else ring1[k + 1]
                bm.faces.new((c, b, a) if s > 0 else (c, a, b))
    # rear diffuser with fins
    add_box(bm, (-0.44, 1.90, 0.200), (0.44, 2.04, 0.214), mat=0)
    for x in (-0.30, -0.10, 0.10, 0.30):
        add_box(bm, (x - 0.006, 1.92, 0.152), (x + 0.006, 2.04, 0.205), mat=0)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = obj_from_bm("Underbody", bm, mats=[M["M_PlasticBlack"]], parent=root)
    shade(ob, 30.0)
    return ob

def build_grilles(M, root):
    """Dark recessed panels with slats behind the bumper intakes (part of Underbody material set)."""
    bm = bmesh.new()
    # rear plate recess: back panel + blank plate
    add_box(bm, (-0.275, 2.075, 0.46), (0.275, 2.085, 0.64), mat=0)
    add_box(bm, (-0.245, 2.085, 0.485), (0.245, 2.092, 0.615), mat=1)
    for k, o in enumerate(intake_outlines()):
        xs = [p[0] for p in o]; zs = [p[1] for p in o]
        x0, x1, z0, z1 = min(xs), max(xs), min(zs), max(zs)
        y = -2.03 if k == 0 else -1.95          # recessed behind the bumper face
        add_box(bm, (x0 - 0.02, y, z0 - 0.02), (x1 + 0.02, y + 0.01, z1 + 0.02), mat=0)
        n = max(2, int((z1 - z0) / 0.028))
        for i in range(1, n):
            z = lerp(z0, z1, i / n)
            add_box(bm, (x0 - 0.01, y - 0.045, z - 0.004), (x1 + 0.01, y, z + 0.004), mat=0)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = obj_from_bm("Grille", bm, mats=[M["M_PlasticBlack"], M["M_Rim"]], parent=root)
    shade(ob, 30.0)
    return ob

def build_mirror(name, side, M, root):
    bm = bmesh.new()
    s = side
    # aerodynamic housing (elongated ellipsoid, painted), short arm to the door
    add_ellipsoid(bm, (s * 0.080, 0.0, 0.030), (0.052, 0.095, 0.042), mat=0, seg=28, rings=14)
    add_box(bm, (min(0, s * 0.045), -0.035, -0.012), (max(0, s * 0.045), 0.030, 0.022), mat=2)
    # mirror glass on the rear (+Y) face
    add_ellipsoid(bm, (s * 0.083, 0.070, 0.030), (0.046, 0.010, 0.036), mat=1, seg=24, rings=10)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    loc = (s * 0.795, -0.44, 0.955)
    ob = obj_from_bm(name, bm, mats=[M["M_Paint"], M["M_Chrome"], M["M_PlasticBlack"]], parent=root, location=loc)
    shade(ob, 40.0)
    return ob

def build_exhaust(M, root):
    bm = bmesh.new()
    prof = [(0.040, -0.10), (0.046, -0.10), (0.048, 0.03), (0.044, 0.035), (0.036, 0.03), (0.036, -0.10)]
    # lathe around X then rotate to point along +Y (rearward)
    lathe(bm, prof, 32, mat=0)
    R = Matrix.Rotation(math.radians(-90), 4, 'Z')
    for v in bm.verts: v.co = R @ v.co
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    loc = (-0.46, 2.10, 0.255)
    ob = obj_from_bm("Exhaust_Tip", bm, mats=[M["M_Chrome"]], parent=root, location=loc)
    shade(ob, 40.0)
    return ob

def build_interior(M, root):
    bm = bmesh.new()
    I = 0
    # floor and tunnel
    add_box(bm, (-0.74, -0.70, 0.18), (0.74, 1.40, 0.22), mat=I)
    add_box(bm, (-0.10, -0.75, 0.22), (0.10, 0.65, 0.40), mat=I)
    # dashboard (kept behind the cowl and under the windshield) and instrument hood
    add_box(bm, (-0.76, -0.60, 0.58), (0.76, -0.38, 0.80), mat=I)
    add_ellipsoid(bm, (0.0, -0.46, 0.80), (0.74, 0.13, 0.045), mat=I, seg=32, rings=10)
    add_ellipsoid(bm, (0.37, -0.40, 0.84), (0.16, 0.07, 0.05), mat=I, seg=24, rings=10)
    # centre console
    add_box(bm, (-0.12, -0.55, 0.40), (0.12, -0.20, 0.72), mat=I)
    # front seats (cushion, backrest, headrest)
    for x in (0.37, -0.37):
        add_box(bm, (x - 0.24, -0.10, 0.25), (x + 0.24, 0.40, 0.38), mat=I)
        R = Matrix.Translation((x, 0.43, 0.38)) @ Matrix.Rotation(math.radians(-14), 4, 'X')
        add_box(bm, (-0.24, -0.06, 0.0), (0.24, 0.06, 0.62), mat=I, M4=R)
        add_ellipsoid(bm, (x, 0.52, 1.06), (0.12, 0.06, 0.08), mat=I, seg=16, rings=8)
    # rear bench
    add_box(bm, (-0.62, 0.70, 0.24), (0.62, 1.10, 0.38), mat=I)
    R = Matrix.Translation((0.0, 1.14, 0.38)) @ Matrix.Rotation(math.radians(-24), 4, 'X')
    add_box(bm, (-0.62, -0.06, 0.0), (0.62, 0.06, 0.50), mat=I, M4=R)
    # parcel shelf and door cards
    add_box(bm, (-0.72, 1.20, 0.86), (0.72, 1.62, 0.89), mat=I)
    for s in (1, -1):
        x0, x1 = (0.76, 0.80) if s > 0 else (-0.80, -0.76)
        add_box(bm, (x0, -0.70, 0.22), (x1, 0.60, 0.88), mat=I)
    # steering wheel (left-hand drive) on a column
    Rw = Matrix.Translation((0.37, -0.25, 0.82)) @ Matrix.Rotation(math.radians(90 - 22), 4, 'X')
    add_torus(bm, 0.180, 0.016, seg=40, rseg=10, mat=I, M4=Rw)
    add_box(bm, (-0.012, -0.17, -0.012), (0.012, 0.17, 0.012), mat=I, M4=Rw)
    add_box(bm, (-0.035, -0.035, -0.06), (0.035, 0.035, 0.0), mat=I, M4=Rw)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = obj_from_bm("Interior", bm, mats=[M["M_Interior"]], parent=root)
    shade(ob, 35.0)
    return ob

def build_arch_lips(M, root, body):
    """Thin rolled lips around the wheel arch openings (joined into the body)."""
    bm = bmesh.new()
    for y in (Y_AXLE_F, Y_AXLE_R):
        for s in (1, -1):
            # sample the body side x at the arch top for placement
            x = interp(W_SHOULDER, y) + 0.010 + (0.016 * gauss(y, Y_AXLE_F, 0.42) + 0.024 * gauss(y, Y_AXLE_R, 0.48)) - 0.012
            M4 = Matrix.Translation((s * x, y, ARCH_Z)) @ Matrix.Rotation(math.radians(90), 4, 'Y')
            arc = math.radians(196)
            # local -X maps to world +Z after the Y rotation: centre the arc on the top (u = 180 deg)
            M4 = M4 @ Matrix.Rotation(math.radians(180 - 98), 4, 'Z')
            add_torus(bm, ARCH_R + 0.004, 0.009, seg=48, rseg=8, mat=0, M4=M4, arc=arc)
    # door handles: slim black pull bars just proud of the door skin
    for s in (1, -1):
        y, z, x = 0.40, 0.790, 0.838     # door skin is at about x = 0.84 there
        add_box(bm, (min(s * (x - 0.004), s * (x + 0.010)), y - 0.065, z - 0.011),
                (max(s * (x - 0.004), s * (x + 0.010)), y + 0.065, z + 0.011), mat=1)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    lips = obj_from_bm("ArchLips", bm, mats=[M["M_Paint"], M["M_PlasticBlack"]])
    return lips

def join_into(target, others):
    bpy.ops.object.select_all(action='DESELECT')
    for o in others: o.select_set(True)
    target.select_set(True)
    bpy.context.view_layer.objects.active = target
    bpy.ops.object.join()

# ----------------------------------------------------------------------------
# LODs, collision, sockets
# ----------------------------------------------------------------------------
def build_lod(src, name, ratio, root):
    ob = bpy.data.objects.new(name, src.data.copy())
    link(ob, root)
    d = ob.modifiers.new("Decimate", 'DECIMATE')
    d.ratio = ratio; d.use_collapse_triangulate = True
    apply_modifiers(ob)
    shade(ob, 35.0)
    return ob

def hull_from_points(name, pts, root):
    bm = bmesh.new()
    vs = [bm.verts.new(p) for p in pts]
    res = bmesh.ops.convex_hull(bm, input=vs)
    drop = {g for g in res.get("geom_interior", []) + res.get("geom_unused", []) if isinstance(g, bmesh.types.BMVert)}
    if drop:
        bmesh.ops.delete(bm, geom=list(drop), context='VERTS')
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = obj_from_bm(name, bm, parent=root)
    return ob

def build_collision(surface, root):
    vs = [v.co.copy() for v in surface.data.vertices]
    # subsample for small hulls
    vs = vs[::7]
    lower_front = [v for v in vs if v.z <= 0.90 and v.y <= -0.55]
    lower_rear = [v for v in vs if v.z <= 0.98 and v.y >= -0.65]
    cabin = [v for v in vs if v.z >= 0.84 and -0.70 <= v.y <= 1.75]
    obs = [hull_from_points("UCX_Body_01", lower_front, root),
           hull_from_points("UCX_Body_02", lower_rear, root),
           hull_from_points("UCX_Body_03", cabin, root)]
    bm = bmesh.new()
    box_bm(bm, (-0.80, -1.95, 0.16), (0.80, 1.95, 0.62))
    finish_closed(bm)
    obs.append(obj_from_bm("UBX_Chassis", bm, parent=root))
    for o in obs:
        o.display_type = 'WIRE'
        o.hide_render = True
    return obs

def build_sockets(root):
    socks = {
        "Socket_Spoiler": (0.0, 1.98, 0.985),
        "Socket_FrontBumper": (0.0, -2.17, 0.33),
        "Socket_RearBumper": (0.0, 2.12, 0.42),
        "Socket_SideSkirt_L": (0.86, 0.0, 0.20),
        "Socket_SideSkirt_R": (-0.86, 0.0, 0.20),
        "Socket_Exhaust": (-0.46, 2.13, 0.255),
    }
    out = []
    for n, p in socks.items():
        e = bpy.data.objects.new(n, None)
        e.empty_display_type = 'ARROWS'; e.empty_display_size = 0.15
        e.location = p
        out.append(link(e, root))
    return out

def smart_uv(objs):
    for ob in objs:
        if ob.type != 'MESH' or ob.name.startswith(("UCX_", "UBX_")):
            continue
        bpy.ops.object.select_all(action='DESELECT')
        ob.select_set(True)
        bpy.context.view_layer.objects.active = ob
        bpy.ops.object.mode_set(mode='EDIT')
        bpy.ops.mesh.select_all(action='SELECT')
        bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.01)
        bpy.ops.object.mode_set(mode='OBJECT')
        # safety net: any UV the projector could not solve (degenerate face) becomes 0,0
        for layer in ob.data.uv_layers:
            for d in layer.data:
                if d.uv.x != d.uv.x or d.uv.y != d.uv.y:
                    d.uv = (0.0, 0.0)

def clean_bm(bm):
    """Merge coincident vertices (lathe points on the axis) and drop zero-area faces."""
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6)
    bmesh.ops.dissolve_degenerate(bm, dist=1e-6, edges=bm.edges)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)

# ----------------------------------------------------------------------------
# Build everything
# ----------------------------------------------------------------------------
def build():
    reset_scene()
    M = materials()
    root = bpy.data.objects.new("ArdenKite_Stock", None)
    root.empty_display_type = 'PLAIN_AXES'
    link(root)
    surface = body_surface(M)
    body = build_body(M, surface, root)
    lips = build_arch_lips(M, root, body)
    join_into(body, [lips])
    shade(body, 32.0)
    glass = build_glass(M, surface, root)
    lights = build_lights(M, surface, root)
    under = build_underbody(M, root)
    grille = build_grilles(M, root)
    join_into(under, [grille])
    under.name = "Underbody"; under.data.name = "Underbody"
    wheels = [build_wheel("Wheel_" + k, 1 if k[1] == "L" else -1, M, root, HUBS[k]) for k in ("FL", "FR", "RL", "RR")]
    calipers = [build_caliper("Caliper_" + k, 1 if k[1] == "L" else -1, M, root, HUBS[k]) for k in ("FL", "FR", "RL", "RR")]
    mirrors = [build_mirror("Mirror_L", 1, M, root), build_mirror("Mirror_R", -1, M, root)]
    exhaust = build_exhaust(M, root)
    interior = build_interior(M, root)
    col = build_collision(surface, root)
    lod1 = build_lod(body, "Body_LOD1", 0.5, root)
    lod2 = build_lod(body, "Body_LOD2", 0.15, root)
    socks = build_sockets(root)
    bpy.data.objects.remove(surface, do_unlink=True)
    meshes = [body, lod1, lod2, glass, under, interior, exhaust] + lights + wheels + calipers + mirrors
    for o in meshes:
        o.data.name = o.name
    smart_uv(meshes)
    lod1.hide_render = True; lod2.hide_render = True
    lod1.hide_set(True); lod2.hide_set(True)
    return root

# ----------------------------------------------------------------------------
# Measurement / report
# ----------------------------------------------------------------------------
def world_bbox(objs):
    mn = Vector((1e9, 1e9, 1e9)); mx = Vector((-1e9, -1e9, -1e9))
    for o in objs:
        for c in o.bound_box:
            w = o.matrix_world @ Vector(c)
            mn = Vector(map(min, mn, w)); mx = Vector(map(max, mx, w))
    return mn, mx

def report(root):
    kids = [o for o in root.children_recursive]
    meshes = [o for o in kids if o.type == 'MESH']
    body = bpy.data.objects["Body_LOD0"]
    bmn, bmx = world_bbox([body])
    vis = [o for o in meshes if not o.name.startswith(("UCX_", "UBX_", "Body_LOD1", "Body_LOD2"))]
    amn, amx = world_bbox(vis)
    tri = {o.name: tri_count(o) for o in meshes}
    lod0_total = sum(v for k, v in tri.items() if not k.startswith(("UCX_", "UBX_", "Body_LOD1", "Body_LOD2")))
    r = {
        "body_bbox_min": [round(v, 4) for v in bmn], "body_bbox_max": [round(v, 4) for v in bmx],
        "body_size": [round(v, 4) for v in (bmx - bmn)],
        "all_visible_size": [round(v, 4) for v in (amx - amn)],
        "all_visible_min": [round(v, 4) for v in amn],
        "tris": tri, "lod0_visible_total_tris": lod0_total,
        "hubs": {o.name: [round(v, 4) for v in o.matrix_world.translation] for o in kids if o.name.startswith(("Wheel_", "Caliper_"))},
    }
    return r

# ----------------------------------------------------------------------------
# Preview renders
# ----------------------------------------------------------------------------
def setup_studio():
    sc = bpy.context.scene
    sc.render.engine = 'BLENDER_EEVEE'
    sc.render.resolution_x, sc.render.resolution_y = 1280, 720
    sc.render.film_transparent = False
    try:
        sc.eevee.taa_render_samples = 48
        sc.eevee.use_raytracing = True
    except Exception:
        pass
    try:
        sc.view_settings.view_transform = 'AgX'
    except Exception:
        pass
    w = bpy.data.worlds.new("Studio"); sc.world = w
    bg = next(n for n in w.node_tree.nodes if n.type == "BACKGROUND")
    bg.inputs[0].default_value = (0.30, 0.32, 0.35, 1.0); bg.inputs[1].default_value = 0.55
    # ground
    bm = bmesh.new(); bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=30)
    gm = make_mat("M_StudioFloor", (0.16, 0.16, 0.17), rough=0.45)
    floor = obj_from_bm("StudioFloor", bm, mats=[gm])
    # lights: soft key, cool fill, long top strip for paint reflections
    def area(name, loc, energy, size, rot, shape='SQUARE', size_y=None):
        ld = bpy.data.lights.new(name, 'AREA'); ld.energy = energy; ld.size = size
        if shape != 'SQUARE':
            ld.shape = shape; ld.size_y = size_y
        o = bpy.data.objects.new(name, ld); o.location = loc; o.rotation_euler = rot
        link(o)
    area("Key", (3.5, -4.0, 5.0), 900, 4.0, (math.radians(40), 0, math.radians(40)))
    area("Fill", (-5.0, 2.0, 3.5), 350, 5.0, (math.radians(55), 0, math.radians(-110)))
    area("Top", (0.0, 0.0, 5.0), 260, 1.2, (0, 0, 0), shape='RECTANGLE', size_y=8.0)
    sun = bpy.data.lights.new("Sun", 'SUN'); sun.energy = 1.2; sun.angle = math.radians(8)
    so = bpy.data.objects.new("Sun", sun); so.rotation_euler = (math.radians(50), 0, math.radians(30)); link(so)
    target = bpy.data.objects.new("CamTarget", None); link(target)
    cd = bpy.data.cameras.new("Cam"); cam = bpy.data.objects.new("Cam", cd); link(cam)
    sc.camera = cam
    con = cam.constraints.new('TRACK_TO'); con.target = target
    con.track_axis = 'TRACK_NEGATIVE_Z'; con.up_axis = 'UP_Y'
    return cam, target

def render_views(folder, tag=""):
    os.makedirs(folder, exist_ok=True)
    cam, target = setup_studio()
    views = {
        "front34": ((4.6, -6.2, 1.55), (0.0, -0.2, 0.55), 50),
        "rear34": ((-4.8, 6.0, 1.75), (0.0, 0.2, 0.60), 50),
        "side": ((8.5, 0.0, 0.85), (0.0, 0.0, 0.62), 50),
        "front": ((0.0, -8.0, 0.95), (0.0, 0.0, 0.62), 50),
        "top": ((0.0, 0.01, 11.0), (0.0, 0.0, 0.0), 40),
        "wheel": ((2.2, -2.45, 0.55), (0.75, -1.275, 0.33), 60),
    }
    out = []
    for k, (loc, tgt, lens) in views.items():
        cam.location = loc; target.location = tgt; cam.data.lens = lens
        p = os.path.join(folder, f"arden_kite_stock_{k}{tag}.png")
        bpy.context.scene.render.filepath = p
        bpy.ops.render.render(write_still=True)
        out.append(p)
    return out

# ----------------------------------------------------------------------------
# Export + verify
# ----------------------------------------------------------------------------
def export_fbx(root, path):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.object.select_all(action='DESELECT')
    for o in [root] + list(root.children_recursive):
        o.hide_viewport = False
        o.hide_set(False)
        o.select_set(True)
    bpy.context.view_layer.objects.active = root
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={'EMPTY', 'MESH'},
        apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS', global_scale=1.0,
        axis_forward='-Z', axis_up='Y', bake_space_transform=True,
        mesh_smooth_type='FACE', use_mesh_modifiers=True, add_leaf_bones=False,
        use_tspace=False, path_mode='AUTO', bake_anim=False)
    return path

def verify_fbx(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=path)
    objs = list(bpy.data.objects)
    info = {"objects": {}, "materials": sorted(m.name for m in bpy.data.materials)}
    for o in objs:
        info["objects"][o.name] = {
            "type": o.type, "parent": o.parent.name if o.parent else None,
            "world_loc": [round(v, 4) for v in o.matrix_world.translation],
            "rot": [round(math.degrees(v), 2) for v in o.matrix_world.to_euler()],
            "scale": [round(v, 4) for v in o.matrix_world.to_scale()],
            "tris": tri_count(o) if o.type == 'MESH' else 0,
        }
    meshes = [o for o in objs if o.type == 'MESH' and not o.name.startswith(("UCX_", "UBX_", "Body_LOD1", "Body_LOD2"))]
    mn, mx = world_bbox(meshes)
    info["bbox_min"] = [round(v, 4) for v in mn]; info["bbox_max"] = [round(v, 4) for v in mx]
    info["size"] = [round(v, 4) for v in (mx - mn)]
    b = bpy.data.objects.get("Body_LOD0")
    if b:
        bmn, bmx = world_bbox([b]); info["body_size"] = [round(v, 4) for v in (bmx - bmn)]
    return info

def main():
    o = args()
    root = build()
    rep = report(root)
    os.makedirs(o["out"], exist_ok=True)
    blend = os.path.join(o["out"], "ArdenKite_Stock.blend")
    bpy.context.preferences.filepaths.save_version = 0      # no .blend1 backups
    bpy.ops.wm.save_as_mainfile(filepath=blend, compress=True)
    rep["blend"] = blend
    if o["render"]:
        rep["renders"] = render_views(o["renders"], o["tag"])
        bpy.ops.wm.open_mainfile(filepath=blend)
        root = bpy.data.objects["ArdenKite_Stock"]
    if o["export"]:
        fbx = export_fbx(root, os.path.join(o["out"], "export", "ArdenKite_Stock.fbx"))
        rep["fbx"] = fbx
        if o["verify"]:
            rep["verify"] = verify_fbx(fbx)
    with open(os.path.join(o["out"], "build_report.json"), "w") as f:
        json.dump(rep, f, indent=1)
    print("KITE_REPORT " + json.dumps({k: v for k, v in rep.items() if k not in ("tris", "verify")}))

if __name__ == "__main__":
    main()

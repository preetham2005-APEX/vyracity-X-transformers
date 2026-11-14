"""Veyra City terrain rock kit: boulders, outcrops, a tiling road-cut panel, scree, a shoreline
group and fallen logs.

Every LOD is authored by regenerating the rock at a lower sphere resolution with the SAME noise
seed, so the silhouette stays put while the triangle count drops. Decimating a faceted rock rounds
exactly the fracture edges that make it read as rock.
"""
import math

from mathutils import Vector

from rock_lib import TILE_M, add_rock, fbm3, mesh_bounds, rock_mesh, uv_sphere
from terrain_lib import MAT_ROCK, Builder, bark_rect, rng, tube

MAT_LOG_BARK = 0   # the logs are exported with [M_Veyra_Bark] alone

# (segments, rings) per LOD - the rock is rebuilt at each, not decimated
RES = {
    "S": [(10, 6), (7, 4), (5, 3)],
    "M": [(12, 8), (8, 5), (5, 3)],
    "L": [(16, 10), (10, 6), (6, 4)],
    "XS": [(7, 4), (5, 3), (4, 3)],
}

SHAPE = {
    # planes = number of fracture half-spaces; more planes + less noise = angular,
    # fewer planes + more noise = weathered and rounded
    "Angular": dict(planes=9, noise_amp=0.16, noise_freq=1.5, smooth=0.0, plane_bias=0.18),
    "Rounded": dict(planes=3, noise_amp=0.30, noise_freq=1.15, smooth=0.35, plane_bias=0.25),
}

BOULDER = {
    "S": dict(size=0.72, scale=(1.0, 0.86, 0.62)),
    "M": dict(size=1.65, scale=(1.0, 0.92, 0.66)),
    "L": dict(size=3.20, scale=(1.0, 0.88, 0.60)),
}


def _rock_lod(res_key, lod, shape, size, scale, seed, sit=True):
    seg, ring = RES[res_key][lod]
    p = SHAPE[shape]
    sc = (size * 0.5 * scale[0], size * 0.5 * scale[1], size * 0.5 * scale[2])
    verts, faces = rock_mesh(seg, ring, sc, seed, noise_amp=p["noise_amp"],
                             noise_freq=p["noise_freq"], planes=p["planes"],
                             plane_bias=p["plane_bias"])
    if sit:
        # drop so the rock sits on z = 0 and bed it 8 % of its height into the ground, which is what
        # stops a boulder reading as a prop balanced on the terrain
        lo, hi = mesh_bounds(verts)
        h = hi.z - lo.z
        dz = -lo.z - 0.08 * h
        for v in verts:
            v.z += dz
    return verts, faces, p["smooth"]


# ====================================================================== BOULDERS
def boulder(res_key, shape, lod, seed=101):
    b = Builder()
    cfg = BOULDER[res_key]
    verts, faces, smooth = _rock_lod(res_key, lod, shape, cfg["size"], cfg["scale"],
                                     seed + hash(res_key + shape) % 701)
    add_rock(b, verts, faces, MAT_ROCK, smooth=smooth)
    return b


def boulder_collider(res_key, shape, seed=101):
    """UCX_: the LOD2 shell. PhysX builds the convex hull from the vertices, so the topology does
    not matter - only that the cloud matches the silhouette."""
    verts, faces, _ = _rock_lod(res_key, 2, shape, BOULDER[res_key]["size"],
                                BOULDER[res_key]["scale"], seed + hash(res_key + shape) % 701)
    return verts, faces


# ====================================================================== OUTCROPS
OUTCROP = {
    # a cluster of part-buried blocks breaking out of a slope (VC_25: low, wide, shallow-angled)
    "A": dict(span=5.6, blocks=5, hmax=1.5, seed=211),
    "B": dict(span=8.4, blocks=7, hmax=2.3, seed=307),
}


def _outcrop_blocks(kind):
    p = OUTCROP[kind]
    r = rng(p["seed"])
    out = []
    for i in range(p["blocks"]):
        a = 2 * math.pi * i / p["blocks"] + r.uniform(-0.5, 0.5)
        rad = p["span"] * 0.5 * (0.18 + 0.82 * r.random() ** 0.6)
        size = p["hmax"] * r.uniform(0.75, 1.55)
        pos = Vector((math.cos(a) * rad, math.sin(a) * rad * 0.72, 0.0))
        # buried fraction: the outer blocks barely break the surface
        bury = 0.22 + 0.45 * (rad / (p["span"] * 0.5))
        out.append((pos, size, bury, r.uniform(0, 6.28), 400 + i * 37 + p["seed"]))
    return out


def outcrop(kind, lod, seed=0):
    p = OUTCROP[kind]
    b = Builder()
    res = "L" if p["span"] > 7 else "M"
    for pos, size, bury, yaw, sd in _outcrop_blocks(kind):
        verts, faces, smooth = _rock_lod(res, lod, "Angular", size,
                                         (1.0, 0.80, 0.52), sd, sit=False)
        c, s = math.cos(yaw), math.sin(yaw)
        for v in verts:
            v.x, v.y = v.x * c - v.y * s, v.x * s + v.y * c
        lo, hi = mesh_bounds(verts)
        h = hi.z - lo.z
        for v in verts:
            v.z += -lo.z - bury * h
        add_rock(b, verts, faces, MAT_ROCK, origin=pos, smooth=smooth,
                 uv_offset=(pos.x / TILE_M, pos.y / TILE_M))
    return b


def outcrop_colliders(kind):
    """One convex hull per block: a single hull over the whole cluster would fill the gaps between
    blocks that you can see straight through."""
    p = OUTCROP[kind]
    res = "L" if p["span"] > 7 else "M"
    out = []
    for i, (pos, size, bury, yaw, sd) in enumerate(_outcrop_blocks(kind)):
        verts, faces, _ = _rock_lod(res, 2, "Angular", size, (1.0, 0.80, 0.52), sd, sit=False)
        c, s = math.cos(yaw), math.sin(yaw)
        for v in verts:
            v.x, v.y = v.x * c - v.y * s, v.x * s + v.y * c
        lo, hi = mesh_bounds(verts)
        h = hi.z - lo.z
        for v in verts:
            v.z += -lo.z - bury * h
            v += pos
        out.append((verts, faces))
    return out


# ====================================================================== ROAD CUT PANEL
CUT_L = 8.0        # module length: modules butt at exactly 8.00 m
CUT_H = 4.2
CUT_D = 1.5
CUT_Y0 = 0.45       # shift so y = 0 lands on the toe of the slope, not inside the rock
CUT_BEDS = 4        # bedding planes up the face


def _cut_surf(u, w, seed=511, rough=True):
    """The cut face as a strict height field: y = f(u, w), monotone enough that no quad folds.

    u runs 0..1 along the module, w runs 0..1 up the face. Everything that varies with u is an
    integer harmonic of the module length or a circular noise coordinate, so surf(0, w) == surf(1, w)
    exactly and two modules butt with no seam and no collision step.
    """
    x = (u - 0.5) * CUT_L
    z = w * CUT_H
    wob = (0.30 * math.sin(2 * math.pi * u + seed * 0.7)
           + 0.16 * math.sin(4 * math.pi * u + 1.9)
           + 0.09 * math.sin(8 * math.pi * u + 3.1))
    # bedding: a gentle sawtooth. Shallow on purpose - the first pass used a near-vertical riser,
    # which alternated the quad normals and read as stripes.
    bw = (w * CUT_BEDS) % 1.0
    step = -0.075 * bw
    lean = -0.62 * w                       # the face leans back as it rises
    r = 0.0
    if rough:
        r = 0.10 * fbm3((math.cos(2 * math.pi * u) * 1.5,
                         math.sin(2 * math.pi * u) * 1.5, w * 5.0), seed, 3)
    depth = (CUT_D * 0.30) - wob * 0.35 - step - lean - r
    # CLAUDE.md: a model's front is Blender -Y (so it faces Unity +Z). The road side of the cut is
    # its front, so the rock recedes into +Y and y = 0 is the toe line facing the carriageway.
    return Vector((x, depth - CUT_Y0, z))


def _cut_quad(b, p00, p01, p11, p10, mat=MAT_ROCK):
    """One face quad, wound and normalled the same way every time: no conditional flipping, so a
    face whose normal disagrees with its winding is not expressible."""
    n = (p01 - p00).cross(p10 - p00)   # = (p1 - p0) x (p3 - p0), consistent with the winding
    if n.length < 1e-9:
        return
    n.normalize()
    uvs = [((q.x + CUT_L * 0.5) / TILE_M, q.z / TILE_M) for q in (p00, p01, p11, p10)]
    b.quad(p00, p01, p11, p10, uvs, mat, [n] * 4)


def cut_face(lod, seed=511):
    """A tiling rock-cut panel for a road cut (VC_26 / VC_07): leaning stratified face with a talus
    wedge at the foot. Verified periodic across the 8 m module boundary by verify_terrain.py."""
    b = Builder()
    nx = (24, 14, 6)[lod]
    nz = (14, 8, 4)[lod]

    for i in range(nx):
        for k in range(nz):
            u0, u1 = i / nx, (i + 1) / nx
            w0, w1 = k / nz, (k + 1) / nz
            # order matters: _cut_quad takes the normal as (p1-p0) x (p3-p0), so p1 must step
            # along +x and p3 along +z for the normal to come out -y, the road side
            _cut_quad(b, _cut_surf(u0, w0, seed), _cut_surf(u1, w0, seed),
                      _cut_surf(u1, w1, seed), _cut_surf(u0, w1, seed))

    # talus wedge: a quad strip from the toe line out in front of the face, same winding rule
    if lod < 2:
        tn = (16, 8)[lod]
        for i in range(tn):
            u0, u1 = i / tn, (i + 1) / tn
            foot0, foot1 = _cut_surf(u0, 0.0, seed), _cut_surf(u1, 0.0, seed)
            d0 = 0.70 + 0.26 * math.sin(2 * math.pi * u0 + 0.8)
            d1 = 0.70 + 0.26 * math.sin(2 * math.pi * u1 + 0.8)
            h0 = 0.42 + 0.16 * math.sin(4 * math.pi * u0 + 2.2)
            h1 = 0.42 + 0.16 * math.sin(4 * math.pi * u1 + 2.2)
            crest0 = Vector((foot0.x, foot0.y - d0 * 0.42, h0))
            crest1 = Vector((foot1.x, foot1.y - d1 * 0.42, h1))
            toe0 = Vector((foot0.x, foot0.y - d0, 0.0))
            toe1 = Vector((foot1.x, foot1.y - d1, 0.0))
            back0 = Vector((foot0.x, foot0.y, 0.0))
            back1 = Vector((foot1.x, foot1.y, 0.0))
            _cut_quad(b, back0, crest0, crest1, back1)   # inner slope, against the face
            _cut_quad(b, crest0, toe0, toe1, crest1)     # outer slope, down to the road
    return b


def cut_face_collider():
    """COL_concrete_: non-convex, matching the face at low resolution. A convex hull would bulge
    out over the carriageway, which is why this one is not UCX_."""
    nx, nz = 8, 5
    verts, faces = [], []
    for i in range(nx + 1):
        for k in range(nz + 1):
            verts.append(tuple(_cut_surf(i / nx, k / nz, rough=False)))
    for i in range(nx):
        for k in range(nz):
            a = i * (nz + 1) + k
            faces.append((a, a + 1, a + nz + 2, a + nz + 1))
    return verts, faces


# ====================================================================== SCREE / GRAVEL PATCH
SCREE_R = 2.25
SCREE_H = 0.10        # centre height; the rim is exactly 0, so there is no step at the ground


def scree(lod, seed=601):
    b = Builder()
    r = rng(seed)
    seg = (16, 10, 6)[lod]
    rings = (3, 2, 1)[lod]

    def hz(rr):
        return SCREE_H * max(0.0, 1.0 - (rr / SCREE_R) ** 2) ** 1.3

    # shallow mound base
    centre = Vector((0, 0, hz(0.0)))
    prev = None
    for ring in range(1, rings + 1):
        rr = SCREE_R * ring / rings
        pts = []
        for k in range(seg):
            a = 2 * math.pi * k / seg
            wob = 1.0 + 0.10 * math.sin(3 * a + seed) + 0.06 * math.sin(7 * a + 1.3)
            pts.append(Vector((math.cos(a) * rr * wob, math.sin(a) * rr * wob,
                               hz(rr) if ring < rings else 0.0)))
        if prev is None:
            for k in range(seg):
                p0, p1 = pts[k], pts[(k + 1) % seg]
                n = (p0 - centre).cross(p1 - centre)
                if n.length < 1e-9:
                    continue
                n.normalize()
                if n.z < 0:
                    n = -n
                uvs = [(p.x / TILE_M * 2.4, p.y / TILE_M * 2.4) for p in (centre, p0, p1)]
                b.tri(centre, p0, p1, uvs, MAT_ROCK, [n] * 3)
        else:
            for k in range(seg):
                k2 = (k + 1) % seg
                q = (prev[k], prev[k2], pts[k2], pts[k])
                n = (q[1] - q[0]).cross(q[3] - q[0])
                if n.length < 1e-9:
                    continue
                n.normalize()
                if n.z < 0:
                    n = -n
                uvs = [(p.x / TILE_M * 2.4, p.y / TILE_M * 2.4) for p in q]
                b.quad(q[0], q[1], q[2], q[3], uvs, MAT_ROCK, [n] * 4)
        prev = pts

    # loose stones on top - these do NOT collide, they are all under 0.14 m
    n_st = (26, 10, 0)[lod]
    for i in range(n_st):
        a = r.uniform(0, 2 * math.pi)
        rr = SCREE_R * math.sqrt(r.random()) * 0.95
        sz = r.uniform(0.09, 0.26)
        verts, faces, smooth = _rock_lod("XS", min(lod, 2), "Angular", sz,
                                         (1.0, 0.85, 0.55), 900 + i * 13)
        pos = Vector((math.cos(a) * rr, math.sin(a) * rr, hz(rr) - sz * 0.10))
        add_rock(b, verts, faces, MAT_ROCK, origin=pos, tile=TILE_M * 0.4,
                 uv_offset=(pos.x, pos.y), smooth=smooth)
    return b


def scree_collider():
    """COL_Ground_Scree: the mound surface only, rim exactly at z = 0 so there is no vertical step
    where it meets the terrain (CLAUDE.md physics rule). The loose stones are decorative."""
    seg, rings = 12, 2
    verts = [(0.0, 0.0, SCREE_H)]
    faces = []

    def hz(rr):
        return SCREE_H * max(0.0, 1.0 - (rr / SCREE_R) ** 2) ** 1.3

    for ring in range(1, rings + 1):
        rr = SCREE_R * ring / rings
        for k in range(seg):
            a = 2 * math.pi * k / seg
            wob = 1.0 + 0.10 * math.sin(3 * a + 601) + 0.06 * math.sin(7 * a + 1.3)
            verts.append((math.cos(a) * rr * wob, math.sin(a) * rr * wob,
                          hz(rr) if ring < rings else 0.0))
    for k in range(seg):
        faces.append((0, 1 + (k + 1) % seg, 1 + k))
    for ring in range(rings - 1):
        a0 = 1 + ring * seg
        a1 = 1 + (ring + 1) * seg
        for k in range(seg):
            k2 = (k + 1) % seg
            faces.append((a0 + k, a0 + k2, a1 + k2, a1 + k))
    return verts, faces


# ====================================================================== SHORELINE GROUP
def shore_group(lod, seed=701):
    """D2 shoreline: flat water-worn rocks, low and wide, sitting part-submerged."""
    b = Builder()
    r = rng(seed)
    for i in range(6):
        a = 2 * math.pi * i / 6 + r.uniform(-0.4, 0.4)
        rad = 1.9 * (0.20 + 0.80 * r.random() ** 0.5)
        size = r.uniform(0.75, 1.9)
        verts, faces, smooth = _rock_lod("M", lod, "Rounded", size, (1.0, 0.82, 0.34),
                                         720 + i * 29, sit=False)
        lo, hi = mesh_bounds(verts)
        h = hi.z - lo.z
        for v in verts:
            v.z += -lo.z - 0.30 * h
        pos = Vector((math.cos(a) * rad, math.sin(a) * rad * 0.8, 0.0))
        add_rock(b, verts, faces, MAT_ROCK, origin=pos, smooth=max(smooth, 0.45),
                 uv_offset=(pos.x / TILE_M, pos.y / TILE_M))
    return b


def shore_colliders(seed=701):
    r = rng(seed)
    out = []
    for i in range(6):
        a = 2 * math.pi * i / 6 + r.uniform(-0.4, 0.4)
        rad = 1.9 * (0.20 + 0.80 * r.random() ** 0.5)
        size = r.uniform(0.75, 1.9)
        verts, faces, _ = _rock_lod("M", 2, "Rounded", size, (1.0, 0.82, 0.34), 720 + i * 29,
                                    sit=False)
        lo, hi = mesh_bounds(verts)
        h = hi.z - lo.z
        pos = Vector((math.cos(a) * rad, math.sin(a) * rad * 0.8, 0.0))
        for v in verts:
            v.z += -lo.z - 0.30 * h
            v += pos
        out.append((verts, faces))
    return out


# ====================================================================== FALLEN LOGS
LOG = {
    "A": dict(L=6.2, r0=0.34, r1=0.21, bend=0.30, stubs=4, seed=801),
    "B": dict(L=3.9, r0=0.26, r1=0.17, bend=0.16, stubs=3, seed=839),
}


def fallen_log(kind, lod, seed=0):
    """D8 deadfall. Uses the SHARED bark atlas (bark_conifer), so it adds no material of its own."""
    p = LOG[kind]
    r = rng(p["seed"] + lod)
    b = Builder()
    sides = (10, 6, 4)[lod]
    nseg = (6, 4, 2)[lod]
    rect = bark_rect("bark_conifer")
    pts, radii = [], []
    for i in range(nseg + 1):
        t = i / nseg
        x = (t - 0.5) * p["L"]
        y = p["bend"] * math.sin(math.pi * t) * 1.0
        z = p["r0"] * 0.92 + 0.06 * math.sin(3.1 * t + 1.0)
        pts.append((x, y, z))
        radii.append(p["r0"] * (1 - t) + p["r1"] * t)
    tube(b, pts, radii, sides, rect, tile_m=1.8, mat=MAT_LOG_BARK, close_top=True)
    # broken root end
    if lod < 2:
        for i in range(p["stubs"]):
            t = 0.18 + 0.64 * i / max(1, p["stubs"] - 1.0)
            idx = min(nseg, int(t * nseg))
            base = Vector(pts[idx])
            a = r.uniform(0, 2 * math.pi)
            el = math.radians(r.uniform(18, 68))
            d = Vector((math.cos(a) * math.cos(el), math.sin(a) * math.cos(el), math.sin(el)))
            ln = r.uniform(0.35, 0.95)
            rad = radii[idx] * r.uniform(0.22, 0.38)
            tube(b, [base + d * (radii[idx] * 0.6), base + d * ln], [rad, rad * 0.4],
                 max(4, sides - 4), rect, tile_m=1.0, mat=MAT_LOG_BARK, close_top=True)
    return b


def log_collider(kind):
    """UCX_: a low box-ish hull around the trunk. PhysX hulls the vertex cloud."""
    p = LOG[kind]
    verts, faces = [], []
    nseg, sides = 3, 6
    for i in range(nseg + 1):
        t = i / nseg
        x = (t - 0.5) * p["L"]
        y = p["bend"] * math.sin(math.pi * t)
        z = p["r0"] * 0.92
        rad = (p["r0"] * (1 - t) + p["r1"] * t) * 1.06
        for k in range(sides):
            a = 2 * math.pi * k / sides
            verts.append((x, y + math.cos(a) * rad, max(0.0, z + math.sin(a) * rad)))
    for i in range(nseg):
        a0 = i * sides
        a1 = (i + 1) * sides
        for k in range(sides):
            k2 = (k + 1) % sides
            faces.append((a0 + k, a0 + k2, a1 + k2, a1 + k))
    return verts, faces

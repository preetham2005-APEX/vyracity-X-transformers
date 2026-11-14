"""VEYRA CITY - original road-level infrastructure props (kerbs, drainage, barriers).

Every builder returns a metadata dict and leaves its objects in the current scene:
    name, lods (list of objects, LOD0 first), collision (object or None),
    collision_type, pivot, notes.

Axis convention (see vc_infra_lib): a module's length runs along Blender -Y, so after the
VC_Unity_Prop export it runs along Unity +Z; the kerb rises on Blender -X, i.e. Unity +X -
the right-hand kerb of a carriageway travelling +Z.
"""
import math

from mathutils import Matrix, Vector

from vc_infra_lib import (MeshBuilder, arc_sections, box, prism, sections_along_y, sweep)

CONC_KERB = "VC_Concrete_Kerb"
CONC_BARRIER = "VC_Concrete_Barrier"
STEEL_GALV = "VC_Steel_Galv"
STEEL_DARK = "VC_Steel_Dark"
IRON = "VC_Iron_Cast"
COLMAT = "VC_Collision"


def sweep_profiles(profiles, ys):
    """Sweep a list of same-length profiles, one per section, along Y. Returns (v, f, smooth)."""
    n = len(profiles[0])
    verts, faces, sm = [], [], []
    for y, prof in zip(ys, profiles):
        for (a, b) in prof:
            verts.append(Vector((a, y, b)))
    for s in range(len(ys) - 1):
        o0, o1 = s * n, (s + 1) * n
        for j in range(n):
            k = (j + 1) % n
            faces.append((o0 + j, o0 + k, o1 + k, o1 + j))
            sm.append(False)
    faces.append(tuple(range(n)))
    faces.append(tuple(range(len(verts) - 1, len(verts) - n - 1, -1)))
    sm += [False, False]
    return verts, faces, sm


# --------------------------------------------------------------------------------- kerb profile
# Veyra standard kerb: 150 mm upstand, 25 mm top chamfer, 450 mm integral gutter pan with a
# 1:20 cross-fall, 280 mm footway lip, buried to -320 mm. t = 0 full kerb, t = 1 dropped
# (driveway apron / dropped crossing).

def kerb_profile(detail=0, t=0.0):
    def L(a, b):
        return a + (b - a) * t

    if detail == 0:
        pts = [(0.000, 0.000), (0.450, 0.022),
               (0.472, L(0.145, 0.030)), (0.497, L(0.170, 0.045)),
               (0.600, L(0.172, 0.047)), (0.618, L(0.154, 0.034)),
               (0.900, L(0.160, 0.070)), (0.900, -0.320), (0.000, -0.320)]
    elif detail == 1:
        pts = [(0.000, 0.000), (0.490, L(0.168, 0.043)),
               (0.900, L(0.160, 0.070)), (0.900, -0.320), (0.000, -0.320)]
    else:  # collision: keeps the gutter pan and the kerb face, drops the chamfers
        pts = [(0.000, 0.000), (0.450, 0.022), (0.472, L(0.145, 0.030)),
               (0.600, L(0.172, 0.047)), (0.900, L(0.160, 0.070)),
               (0.900, -0.320), (0.000, -0.320)]
    return [(-s, z) for s, z in pts]


# Collision is split by SURFACE, not by shape: the gutter pan is asphalt and the kerb face, top and
# footway lip are concrete, and one MeshCollider carries one SurfaceDefinition. The two solids butt
# on the s = 0.450 plane and share the toe height, so there is no vertical step between them.
# Naming follows the project convention COL_<surface>_<name> (VeyraModelPostprocessor).

def kerb_collision_pan():
    return [(-s, z) for s, z in [(0.000, 0.000), (0.450, 0.022),
                                 (0.450, -0.320), (0.000, -0.320)]]


def kerb_collision_kerb(t=0.0):
    def L(a, b):
        return a + (b - a) * t
    return [(-s, z) for s, z in [(0.450, 0.022), (0.472, L(0.145, 0.030)),
                                 (0.600, L(0.172, 0.047)), (0.900, L(0.160, 0.070)),
                                 (0.900, -0.320), (0.450, -0.320)]]


# ------------------------------------------------------------------------------ 1a kerb straight

def build_kerb_straight_4m():
    name, length = "VC_Kerb_Straight_4m", 4.0
    lods = []
    for d in (0, 1):
        mb = MeshBuilder()
        mb.add(sweep(kerb_profile(d), sections_along_y([0.0, -length])), CONC_KERB)
        lods.append(mb.build("%s_LOD%d" % (name, d)))
    secs = sections_along_y([0.0, -length])
    cols = [MeshBuilder().add(sweep(kerb_collision_pan(), secs), COLMAT)
            .build("COL_dry_asphalt_%s_Gutter" % name),
            MeshBuilder().add(sweep(kerb_collision_kerb(), secs), COLMAT)
            .build("COL_kerb_%s_Kerb" % name)]
    return dict(name=name, lods=lods, collision=cols,
                collision_type="COL (non-convex MeshCollider) x2: dry_asphalt gutter + kerb face/top",
                pivot="start of the piece, at the road edge of the gutter pan, z = 0 = road surface",
                notes="4.00 m module; 150 mm upstand + 25 mm chamfer + 450 mm gutter pan. Runs toward "
                      "Unity +Z, kerb rises toward Unity +X. Surfaces: Kerb (face/top), Asphalt (pan).")


# -------------------------------------------------------------------------------- 1b kerb corner

def build_kerb_corner_90():
    name = "VC_Kerb_Corner_90"
    face_r = 3.00                     # kerb-face radius of the return
    r = face_r + 0.45                 # the profile datum sits 450 mm road-side of the face
    lods = []
    for d, steps in ((0, 8), (1, 4)):
        mb = MeshBuilder()
        mb.add(sweep(kerb_profile(d), arc_sections((-r, 0.0), r, 0.0, -math.pi / 2.0, steps)),
               CONC_KERB)
        lods.append(mb.build("%s_LOD%d" % (name, d)))
    secs = arc_sections((-r, 0.0), r, 0.0, -math.pi / 2.0, 6)
    cols = [MeshBuilder().add(sweep(kerb_collision_pan(), secs), COLMAT)
            .build("COL_dry_asphalt_%s_Gutter" % name),
            MeshBuilder().add(sweep(kerb_collision_kerb(), secs), COLMAT)
            .build("COL_kerb_%s_Kerb" % name)]
    return dict(name=name, lods=lods, collision=cols,
                collision_type="COL (non-convex MeshCollider) x2: dry_asphalt gutter + kerb face/top",
                pivot="start of the piece (same datum as VC_Kerb_Straight_4m)",
                notes="90 deg kerb return, 3.00 m face radius, turning toward Unity +X (footway on the "
                      "inside). The left-hand return is this piece mirrored on X.")


# --------------------------------------------------------------------------------- 1c drive apron

def build_kerb_drive_apron():
    name = "VC_Kerb_DriveApron_4m"
    ys = [0.0, -0.80, -3.20, -4.00]
    ts = [0.0, 1.0, 1.0, 0.0]
    lods = []
    for d in (0, 1):
        mb = MeshBuilder()
        mb.add(sweep_profiles([kerb_profile(d, t) for t in ts], ys), CONC_KERB)
        lods.append(mb.build("%s_LOD%d" % (name, d)))
    cols = [MeshBuilder().add(sweep_profiles([kerb_collision_pan() for _ in ts], ys), COLMAT)
            .build("COL_dry_asphalt_%s_Gutter" % name),
            MeshBuilder().add(sweep_profiles([kerb_collision_kerb(t) for t in ts], ys), COLMAT)
            .build("COL_kerb_%s_Kerb" % name)]
    return dict(name=name, lods=lods, collision=cols,
                collision_type="COL (non-convex MeshCollider) x2: dry_asphalt gutter + kerb face/top",
                pivot="start of the piece (same datum as VC_Kerb_Straight_4m)",
                notes="4.00 m dropped-kerb driveway apron: 0.80 m flare down, 2.40 m dropped crossing "
                      "(30 mm upstand), 0.80 m flare up. Tiles between straight kerb modules.")


# -------------------------------------------------------------------------- 2 stormwater inlet

def _inlet_throat_profile(detail=0):
    """Combination inlet: open gutter sump + kerb throat under a cast lintel."""
    if detail == 0:
        pts = [(0.000, 0.000), (0.030, 0.000), (0.030, -0.140), (0.800, -0.140),
               (0.800, 0.158), (0.4745, 0.158), (0.497, 0.170), (0.600, 0.172),
               (0.618, 0.154), (0.900, 0.160), (0.900, -0.320), (0.000, -0.320)]
    else:
        pts = [(0.000, 0.000), (0.030, 0.000), (0.030, -0.140), (0.800, -0.140),
               (0.800, 0.158), (0.4745, 0.158), (0.600, 0.172),
               (0.900, 0.160), (0.900, -0.320), (0.000, -0.320)]
    return [(-s, z) for s, z in pts]


def _grate(mb, bars, mat, frame=True):
    mb.add(box((0.030, 0.900, 0.045), (-0.0455, -0.600, -0.0205)), mat)   # road-side frame rail
    mb.add(box((0.030, 0.900, 0.045), (-0.4450, -0.600, -0.0205)), mat)   # kerb-side frame rail
    if frame:
        mb.add(box((0.430, 0.030, 0.045), (-0.2450, -0.1650, -0.0205)), mat)  # end frame
        mb.add(box((0.430, 0.030, 0.045), (-0.2450, -1.0350, -0.0205)), mat)  # end frame
    span = 0.84
    for i in range(bars):
        y = -0.180 - (i + 0.5) * span / bars
        mb.add(box((0.370, 0.026, 0.040), (-0.2450, y, -0.0200)), mat)


def build_drain_inlet():
    name = "VC_Drain_Inlet_Kerb"
    lods = []
    for d, bars in ((0, 7), (1, 3)):
        mb = MeshBuilder()
        mb.add(sweep(kerb_profile(d), sections_along_y([0.0, -0.15])), CONC_KERB)
        mb.add(sweep(kerb_profile(d), sections_along_y([-1.05, -1.20])), CONC_KERB)
        mb.add(sweep(_inlet_throat_profile(d), sections_along_y([-0.15, -1.05])), CONC_KERB)
        _grate(mb, bars, IRON, frame=(d == 0))
        lods.append(mb.build("%s_LOD%d" % (name, d)))
    secs = sections_along_y([0.0, -1.20])
    cols = [MeshBuilder().add(sweep(kerb_collision_pan(), secs), COLMAT)
            .build("COL_dry_asphalt_%s_Gutter" % name),
            MeshBuilder().add(sweep(kerb_collision_kerb(), secs), COLMAT)
            .build("COL_kerb_%s_Kerb" % name)]
    return dict(name=name, lods=lods, collision=cols,
                collision_type="COL (non-convex MeshCollider) x2: dry_asphalt gutter + kerb face/top "
                                "(the gutter solid closes the sump)",
                pivot="start of the piece (same datum as VC_Kerb_Straight_4m)",
                notes="1.20 m combination inlet: 0.90 m kerb throat (140 mm clear under a cast lintel) "
                      "over a 0.90 x 0.42 m sump with a 7-bar cast grate. Collision closes the sump so "
                      "wheels cannot drop in.")


# --------------------------------------------------------------------------- 3 W-beam guardrail
# Original Veyra W-beam: 312 mm deep, 83 mm corrugation, modelled 12 mm thick for silhouette.

def _wbeam_profile(detail):
    if detail == 0:
        out = [(-0.156, 0.000), (-0.120, 0.075), (-0.078, 0.083), (-0.030, 0.040),
               (0.000, 0.000), (0.030, 0.040), (0.078, 0.083), (0.120, 0.075), (0.156, 0.000)]
    else:
        out = [(-0.156, 0.000), (-0.078, 0.083), (0.000, 0.000), (0.078, 0.083), (0.156, 0.000)]
    inner = [(z, x - 0.012) for (z, x) in reversed(out)]
    return [(x, z) for (z, x) in out] + [(x, z) for (z, x) in inner]


def _wbeam_sections(samples):
    """samples: list of (y, z_centre, x_offset)."""
    return [Matrix.Translation(Vector((x, y, z))) for (y, z, x) in samples]


def _guard_post(mb, y, detail, top=0.75, bottom=-1.10):
    if detail >= 1:
        mb.add(box((0.075, 0.150, top - bottom), (-0.2375, y, (top + bottom) * 0.5)), STEEL_GALV)
        return
    p = [(-0.075, -0.075), (-0.075, 0.075), (-0.040, 0.075), (-0.040, 0.040),
         (0.000, 0.040), (0.000, -0.040), (-0.040, -0.040), (-0.040, -0.075)]
    prof = [(-0.200 + a, b) for (a, b) in p]
    n = len(prof)
    verts, faces, sm = [], [], []
    for z in (bottom, top):
        for (a, b) in prof:
            verts.append(Vector((a, y + b, z)))
    for j in range(n):
        k = (j + 1) % n
        faces.append((j, k, n + k, n + j))
        sm.append(False)
    faces.append(tuple(range(n)))
    faces.append(tuple(range(2 * n - 1, n - 1, -1)))
    sm += [False, False]
    mb.add((verts, faces, sm), STEEL_GALV)


def build_guardrail_4m():
    name, length = "VC_Guardrail_WBeam_4m", 4.0
    lods = []
    for d in (0, 1, 2):
        mb = MeshBuilder()
        if d == 2:
            mb.add(box((0.095, length, 0.312), (0.038, -length * 0.5, 0.55)), STEEL_GALV)
            mb.add(box((0.075, 0.150, 1.85), (-0.2375, 0.0, -0.175)), STEEL_GALV)
        else:
            ys = [0.0, -length * 0.5, -length] if d == 0 else [0.0, -length]
            mb.add(sweep(_wbeam_profile(d), _wbeam_sections([(y, 0.55, 0.0) for y in ys])),
                   STEEL_GALV)
            _guard_post(mb, 0.0, d)
            mb.add(box((0.200, 0.200, 0.360), (-0.100, 0.0, 0.55)), STEEL_GALV)
        if d == 0:
            # ONE splice bolt, at the module START only. The module pitch is exactly 4.00 m, so a
            # bolt at the far end would land in the same place as the next module's start bolt and
            # z-fight with it at every joint in a run. One bolt per module = one bolt per joint.
            mb.add(prism((0.030, 0.0, 0.55), (-0.012, 0.0, 0.55), 0.021, sides=6), STEEL_GALV)
        lods.append(mb.build("%s_LOD%d" % (name, d)))
    mb = MeshBuilder()
    mb.add(box((0.130, length, 0.620), (0.030, -length * 0.5, 0.430)), COLMAT)
    col = mb.build("UBX_%s" % name)
    return dict(name=name, lods=lods, collision=col,
                collision_type="UBX (BoxCollider)",
                pivot="start of the piece, post centreline, ground level (z = 0), rail back plane (x = 0)",
                notes="4.00 m post-and-rail module, rail centre 550 mm, 1.85 m C-section post at the "
                      "module start. The rail face looks toward Unity -X (the carriageway); the module "
                      "runs toward Unity +Z. PITCH IS EXACTLY 4.00 m: the rail spans y = 0 to -4.00. "
                      "The post, blockout and splice bolt straddle the start plane and reach 0.10 m "
                      "BACK past it, which is deliberate - in a run that puts the splice post over the "
                      "joint between two rails, as a real W-beam does. So the bounding length is "
                      "4.12 m but the placement pitch is 4.00 m; do not space them by the bbox.")


def build_guardrail_end():
    name = "VC_Guardrail_WBeam_End"
    samples = {
        0: [(0.0, 0.55, 0.0), (-1.0, 0.52, -0.05), (-2.0, 0.45, -0.18),
            (-3.0, 0.33, -0.38), (-4.0, 0.20, -0.60)],
        1: [(0.0, 0.55, 0.0), (-2.0, 0.45, -0.18), (-4.0, 0.20, -0.60)],
    }
    posts = {0: (0.0, -2.0, -4.0), 1: (0.0, -4.0)}
    lods = []
    for d in (0, 1, 2):
        mb = MeshBuilder()
        if d == 2:
            mb.add(sweep([(-0.08, -0.156), (0.08, -0.156), (0.08, 0.156), (-0.08, 0.156)],
                         _wbeam_sections([(0.0, 0.55, 0.0), (-4.0, 0.20, -0.60)])), STEEL_GALV)
            mb.add(box((0.075, 0.150, 1.65), (-0.2375, 0.0, -0.075)), STEEL_GALV)
            mb.add(box((0.260, 0.120, 0.420), (-0.660, -4.06, 0.200)), STEEL_GALV)
        else:
            mb.add(sweep(_wbeam_profile(d), _wbeam_sections(samples[d])), STEEL_GALV)
            for y in posts[d]:
                top = 0.75 if y == 0.0 else 0.40
                _guard_post(mb, y, d if y == 0.0 else 1, top=top, bottom=-0.90)
            mb.add(box((0.260, 0.120, 0.420), (-0.660, -4.06, 0.200)), STEEL_GALV)
            mb.add(box((0.180, 0.300, 0.140), (-0.660, -3.90, -0.060)), STEEL_GALV)
        lods.append(mb.build("%s_LOD%d" % (name, d)))
    mb = MeshBuilder()
    verts = [(0.08, 0.0, 0.0), (0.08, 0.0, 0.75), (-0.20, 0.0, 0.75), (-0.20, 0.0, 0.0),
             (-0.52, -4.1, 0.0), (-0.52, -4.1, 0.40), (-0.80, -4.1, 0.40), (-0.80, -4.1, 0.0)]
    faces = [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]
    mb.add((verts, faces, [False] * 6), COLMAT)
    col = mb.build("UCX_%s" % name)
    return dict(name=name, lods=lods, collision=col,
                collision_type="UCX (convex MeshCollider)",
                pivot="start of the piece (mates with VC_Guardrail_WBeam_4m), ground level",
                notes="4.00 m flared end terminal: the rail drops 550 -> 200 mm and flares 0.60 m away "
                      "from the carriageway into an impact head and ground anchor.")


# ----------------------------------------------------------------------- 4 concrete Jersey barrier
# Veyra NJ-type profile: 815 mm high, 610 mm base, 75 mm vertical toe, 55 deg lower batter to
# 330 mm, 84 deg upper face, 156 mm top with a 20 mm chamfer. Tapered ends truncate the same
# profile, so every section keeps the same point count.

def jersey_profile(detail=0, height=0.815, shrink=0.0):
    h = max(height, 0.360)
    xt = 0.1264 - 0.1005 * (h - 0.330)
    right = [(0.305, 0.000), (0.305, 0.075), (0.1264, 0.330)]
    if detail == 0:
        right += [(xt, h - 0.020), (xt - 0.020, h)]
    else:
        right += [(xt, h)]
    prof = right + [(-x, z) for (x, z) in reversed(right)]
    if shrink:
        sx = (0.305 - shrink) / 0.305
        sz = (h - shrink) / h
        prof = [(x * sx, z * sz) for (x, z) in prof]
    return prof


def build_jersey_3m():
    name = "VC_Barrier_Jersey_3m"
    lods = []
    for d, ys, shr in ((0, [1.50, 1.45, -1.45, -1.50], [0.015, 0.0, 0.0, 0.015]),
                       (1, [1.50, -1.50], [0.0, 0.0])):
        mb = MeshBuilder()
        mb.add(sweep_profiles([jersey_profile(d, 0.815, s) for s in shr], ys), CONC_BARRIER)
        lods.append(mb.build("%s_LOD%d" % (name, d)))
    mb = MeshBuilder()
    mb.add(sweep_profiles([jersey_profile(1) for _ in range(2)], [1.50, -1.50]), COLMAT)
    col = mb.build("UCX_%s" % name)
    return dict(name=name, lods=lods, collision=col,
                collision_type="UCX (convex MeshCollider)",
                pivot="base centre (centre of the 3.00 m length, cross-section centre, z = 0)",
                notes="3.00 m NJ-type module, 815 mm high, 610 mm base, 156 mm top; the end 50 mm is "
                      "inset 15 mm for the lap joint, so modules read as separate castings.")


def build_jersey_end():
    name = "VC_Barrier_Jersey_EndTaper"
    lods = []
    for d, ys, hs in ((0, [1.50, 0.50, -0.50, -1.50], [0.815, 0.650, 0.490, 0.360]),
                      (1, [1.50, -1.50], [0.815, 0.360])):
        mb = MeshBuilder()
        mb.add(sweep_profiles([jersey_profile(d, h) for h in hs], ys), CONC_BARRIER)
        lods.append(mb.build("%s_LOD%d" % (name, d)))
    mb = MeshBuilder()
    mb.add(sweep_profiles([jersey_profile(1, h) for h in (0.815, 0.360)], [1.50, -1.50]), COLMAT)
    col = mb.build("UCX_%s" % name)
    return dict(name=name, lods=lods, collision=col,
                collision_type="UCX (convex MeshCollider)",
                pivot="base centre (centre of the 3.00 m length, cross-section centre, z = 0)",
                notes="3.00 m tapered end: 815 -> 360 mm over the run, same NJ profile truncated. The "
                      "full-height end (Blender +Y, Unity -Z) mates with VC_Barrier_Jersey_3m.")


BUILDERS = [build_kerb_straight_4m, build_kerb_corner_90, build_kerb_drive_apron,
            build_drain_inlet, build_guardrail_4m, build_guardrail_end,
            build_jersey_3m, build_jersey_end]

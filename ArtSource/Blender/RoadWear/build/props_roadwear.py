"""VEYRA CITY - road-surface detail meshes (covers, grates, expansion joint).

Same conventions as the Infra prop set: VC_Unity_Prop export, `<name>_LOD<n>` children under a
root empty, `COL_`/`UCX_`/`UBX_` collision. z = 0 is the ROAD SURFACE for every prop here.

Physics rule (CLAUDE.md): no vertical step over 0.05 m between adjacent collider surfaces.
Every collision mesh in this set tops out 10 mm or less above the road plane, and the QA script
fails the build if any of them exceeds 0.05 m.
"""
import math
import os
import sys

from mathutils import Vector

INFRA_BUILD = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)),
                                           "..", "..", "Infra", "build"))
if not os.path.isdir(INFRA_BUILD):
    raise RuntimeError("expected the Infra prop library at %s" % INFRA_BUILD)
if INFRA_BUILD not in sys.path:
    sys.path.insert(0, INFRA_BUILD)

import vc_infra_lib                                                         # noqa: E402
from vc_infra_lib import MeshBuilder, box, prism, sections_along_y, sweep   # noqa: E402

# Joint sealant is specific to this set; registered at runtime so Infra/ is left untouched.
vc_infra_lib.MATERIALS.setdefault(
    "VC_Joint_Sealant", dict(base=(0.022, 0.021, 0.021), metallic=0.0, roughness=0.42))

# Surface token for cast-iron castings. These are MEASURED to stand 6-10 mm proud of the road with
# ZERO coplanar area (build/check_coplanar.py), so they are not in a raycast tie with the road
# collider underneath: the nearest hit wins and the tyre genuinely is on metal.
# Docs/SURFACE_CONVENTION.md's coplanar hazard applies to FLUSH colliders, which these are not.
# CONDITIONAL: this holds only while they are placed with the pivot at road level, as documented.
# If they are ever sunk so their tops finish flush with the asphalt, they become coplanar and must
# go back to dry_asphalt.
CASTING_SURFACE = "metal"

IRON = "VC_Iron_Cast"
STEEL_DARK = "VC_Steel_Dark"
CONC = "VC_Concrete_Struct"
SEALANT = "VC_Joint_Sealant"
COLMAT = "VC_Collision"


# ------------------------------------------------------------------------------ revolve helpers

def revolve_solid(profile, segs, smooth=True):
    """Solid of revolution about Z. profile = [(r, z), ...] starting and ending at r = 0."""
    verts, idx = [], []
    for (r, z) in profile:
        if r <= 1e-9:
            idx.append(("pole", len(verts)))
            verts.append(Vector((0.0, 0.0, z)))
        else:
            idx.append(("ring", len(verts)))
            for k in range(segs):
                a = 2.0 * math.pi * k / segs
                verts.append(Vector((math.cos(a) * r, math.sin(a) * r, z)))
    faces, sm = [], []
    for i in range(len(profile) - 1):
        (t0, o0), (t1, o1) = idx[i], idx[i + 1]
        if t0 == "ring" and t1 == "ring":
            for k in range(segs):
                k2 = (k + 1) % segs
                faces.append((o0 + k, o0 + k2, o1 + k2, o1 + k))
                sm.append(smooth)
        elif t0 == "pole":
            for k in range(segs):
                faces.append((o0, o1 + ((k + 1) % segs), o1 + k))
                sm.append(False)
        else:
            for k in range(segs):
                faces.append((o1, o0 + k, o0 + ((k + 1) % segs)))
                sm.append(False)
    return verts, faces, sm


def revolve_ring(profile, segs, smooth=False):
    """Closed-loop profile [(r, z), ...] (never touching the axis) revolved about Z."""
    n = len(profile)
    verts = []
    for k in range(segs):
        a = 2.0 * math.pi * k / segs
        for (r, z) in profile:
            verts.append(Vector((math.cos(a) * r, math.sin(a) * r, z)))
    faces, sm = [], []
    for k in range(segs):
        o0, o1 = k * n, ((k + 1) % segs) * n
        for j in range(n):
            j2 = (j + 1) % n
            faces.append((o0 + j, o0 + j2, o1 + j2, o1 + j))
            sm.append(smooth)
    return verts, faces, sm


def frustum_box(wx0, wy0, wx1, wy1, z0, z1, cx=0.0, cy=0.0):
    """Box with a different footprint top and bottom (for flush, chamfered covers)."""
    a, b = wx0 * 0.5, wy0 * 0.5
    c, d = wx1 * 0.5, wy1 * 0.5
    v = [(cx - a, cy - b, z0), (cx + a, cy - b, z0), (cx + a, cy + b, z0), (cx - a, cy + b, z0),
         (cx - c, cy - d, z1), (cx + c, cy - d, z1), (cx + c, cy + d, z1), (cx - c, cy + d, z1)]
    f = [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]
    return v, f, [False] * 6


# ------------------------------------------------------------------- round manhole covers
# Cover 0.62 m, frame to 0.80 m. Cover top +10 mm, frame top +6 mm, rim tapers to the road.

COVER_TOP = 0.010
FRAME_TOP = 0.006


def _cover_disc(segs):
    return revolve_solid([(0.000, COVER_TOP), (0.296, COVER_TOP), (0.310, 0.003),
                          (0.310, -0.050), (0.000, -0.050)], segs)


def _cover_frame(segs):
    return revolve_ring([(0.316, FRAME_TOP), (0.336, FRAME_TOP), (0.400, 0.000),
                         (0.400, -0.062), (0.316, -0.062)], segs)


def _cover_collision(name):
    mb = MeshBuilder()
    mb.add(revolve_solid([(0.000, COVER_TOP), (0.336, COVER_TOP), (0.400, 0.000),
                          (0.400, -0.030), (0.000, -0.030)], 12, smooth=False), COLMAT)
    return mb.build("COL_%s_%s" % (CASTING_SURFACE, name))


def _manhole(name, pattern):
    lods = []
    for d in (0, 1):
        segs = 20 if d == 0 else 12
        mb = MeshBuilder()
        mb.add(_cover_disc(segs), IRON)
        mb.add(_cover_frame(segs), IRON)
        pattern(mb, d)
        lods.append(mb.build("%s_LOD%d" % (name, d)))
    return dict(name=name, lods=lods, collision=_cover_collision(name),
                collision_type="COL (non-convex MeshCollider)",
                pivot="centre of the cover at the ROAD SURFACE (z = 0); the casting sits -0.062 m deep",
                notes="")


def _pattern_rings(mb, d):
    rings = ((0.252, 0.028), (0.165, 0.026)) if d == 0 else ((0.200, 0.055),)
    segs = 16 if d == 0 else 10
    for (r, w) in rings:
        mb.add(revolve_ring([(r - w * 0.5, COVER_TOP), (r + w * 0.5, COVER_TOP),
                             (r + w * 0.5 - 0.004, COVER_TOP + 0.005),
                             (r - w * 0.5 + 0.004, COVER_TOP + 0.005)], segs), IRON)
    if d == 0:
        mb.add(revolve_solid([(0.000, COVER_TOP + 0.006), (0.052, COVER_TOP + 0.006),
                              (0.060, COVER_TOP), (0.000, COVER_TOP)], 12), IRON)
        for sx in (-1.0, 1.0):
            mb.add(box((0.030, 0.090, 0.012), (sx * 0.215, 0.0, COVER_TOP - 0.004)), IRON)


def _pattern_radial(mb, d):
    segs = 20 if d == 0 else 10
    ribs = 16 if d == 0 else 6
    mb.add(revolve_ring([(0.252, COVER_TOP), (0.280, COVER_TOP),
                         (0.276, COVER_TOP + 0.005), (0.256, COVER_TOP + 0.005)], segs), IRON)
    for i in range(ribs):
        a = 2.0 * math.pi * i / ribs
        r0, r1 = 0.070, 0.240
        mb.add(prism((math.cos(a) * r0, math.sin(a) * r0, COVER_TOP + 0.0025),
                     (math.cos(a) * r1, math.sin(a) * r1, COVER_TOP + 0.0025),
                     0.016, sides=4), IRON)
    if d == 0:
        mb.add(revolve_solid([(0.000, COVER_TOP + 0.006), (0.055, COVER_TOP + 0.006),
                              (0.064, COVER_TOP), (0.000, COVER_TOP)], 12), IRON)
        mb.add(box((0.026, 0.085, 0.012), (0.0, 0.215, COVER_TOP - 0.004)), IRON)


def build_manhole_round_a():
    m = _manhole("VC_Manhole_Round_A", _pattern_rings)
    m["notes"] = ("0.62 m round cast cover in a 0.80 m frame, concentric-ring pattern with a "
                  "centre boss and two lift slots. Cover top +10 mm, frame top +6 mm, rim tapers "
                  "to the road plane.")
    return m


def build_manhole_round_b():
    m = _manhole("VC_Manhole_Round_B", _pattern_radial)
    m["notes"] = ("0.62 m round cast cover in a 0.80 m frame, 16 radial ribs inside a perimeter "
                  "ring with a centre boss and one lift slot. Same flush profile as pattern A, so "
                  "the two read as different castings in the same frame.")
    return m


# --------------------------------------------------------------------- square utility cover

def build_utility_cover_square():
    name = "VC_UtilityCover_Square"
    lods = []
    for d in (0, 1):
        mb = MeshBuilder()
        mb.add(frustum_box(0.600, 0.600, 0.578, 0.578, -0.048, COVER_TOP), IRON)
        for (sx, sy, lx, ly) in ((0.0, -0.345, 0.720, 0.070), (0.0, 0.345, 0.720, 0.070),
                                 (-0.345, 0.0, 0.070, 0.620), (0.345, 0.0, 0.070, 0.620)):
            mb.add(frustum_box(lx, ly, lx - 0.016, ly - 0.016, -0.060, FRAME_TOP, sx, sy), IRON)
        studs = 4 if d == 0 else 2
        for i in range(studs):
            for j in range(studs):
                x = -0.21 + i * 0.42 / max(1, studs - 1)
                y = -0.21 + j * 0.42 / max(1, studs - 1)
                mb.add(frustum_box(0.070, 0.070, 0.056, 0.056, COVER_TOP - 0.002,
                                   COVER_TOP + 0.005, x, y), IRON)
        if d == 0:
            for sy in (-1.0, 1.0):
                mb.add(box((0.090, 0.028, 0.012), (0.0, sy * 0.235, COVER_TOP - 0.004)), IRON)
        lods.append(mb.build("%s_LOD%d" % (name, d)))
    mb = MeshBuilder()
    mb.add(frustum_box(0.760, 0.760, 0.700, 0.700, -0.030, COVER_TOP), COLMAT)
    col = mb.build("COL_%s_%s" % (CASTING_SURFACE, name))
    return dict(name=name, lods=lods, collision=col,
                collision_type="COL (non-convex MeshCollider)",
                pivot="centre of the cover at the ROAD SURFACE (z = 0)",
                notes="0.60 m square utility access cover in a 0.72 m frame, chequer studs and two "
                      "lift slots. Cover top +10 mm, frame top +6 mm.")


# ------------------------------------------------------------------------- storm drain grate

def build_storm_grate_road():
    name = "VC_StormDrain_Grate_Road"
    bar_z = -0.004
    lods = []
    for d in (0, 1):
        mb = MeshBuilder()
        for (sx, sy, lx, ly) in ((0.0, -0.325, 0.700, 0.070), (0.0, 0.325, 0.700, 0.070),
                                 (-0.325, 0.0, 0.070, 0.580), (0.325, 0.0, 0.070, 0.580)):
            mb.add(frustum_box(lx, ly, lx - 0.016, ly - 0.016, -0.075, FRAME_TOP, sx, sy), IRON)
        mb.add(box((0.580, 0.580, 0.012), (0.0, 0.0, -0.090)), CONC)          # sump floor
        bars = 7 if d == 0 else 3
        for i in range(bars):
            x = -0.240 + i * 0.480 / (bars - 1)
            mb.add(box((0.028, 0.660, 0.055), (x, 0.0, bar_z - 0.027)), IRON)
        if d == 0:
            for sy in (-0.150, 0.150):
                mb.add(box((0.660, 0.022, 0.030), (0.0, sy, bar_z - 0.040)), IRON)
        lods.append(mb.build("%s_LOD%d" % (name, d)))
    mb = MeshBuilder()
    mb.add(frustum_box(0.700, 0.700, 0.660, 0.660, -0.030, FRAME_TOP), COLMAT)
    col = mb.build("COL_%s_%s" % (CASTING_SURFACE, name))
    return dict(name=name, lods=lods, collision=col,
                collision_type="COL (non-convex MeshCollider)",
                pivot="centre of the grate at the ROAD SURFACE (z = 0)",
                notes="0.58 m in-carriageway storm grate in a 0.70 m frame: 7 bars recessed 10 mm "
                      "under the frame top, two cross ribs, a sump floor 90 mm down. The collider is "
                      "a single flush plate so wheels roll over the bars instead of dropping in.")


# ----------------------------------------------------------------------- expansion joint strip

def build_expansion_joint():
    name = "VC_ExpansionJoint_Strip_4m"
    length = 4.0
    conc = [(-0.150, 0.000), (-0.026, 0.000), (-0.026, -0.020), (0.026, -0.020),
            (0.026, 0.000), (0.150, 0.000), (0.150, -0.050), (-0.150, -0.050)]
    flat = [(-0.150, 0.000), (0.150, 0.000), (0.150, -0.050), (-0.150, -0.050)]
    lods = []
    for d in (0, 1):
        mb = MeshBuilder()
        mb.add(sweep(conc if d == 0 else flat, sections_along_y([0.0, -length])), CONC)
        if d == 0:
            mb.add(box((0.050, length, 0.016), (0.0, -length * 0.5, -0.010)), SEALANT)
        lods.append(mb.build("%s_LOD%d" % (name, d)))
    # NO COLLIDER, deliberately. Its top face is 1.200 m2 lying at exactly z = 0, which is
    # exactly coplanar with the road collider beneath it, so a wheel raycast would tie between
    # the two (Docs/SURFACE_CONVENTION.md). The road already carries both the surface and the
    # height, and a 52 mm sealant groove changes no grip, so a second collider buys nothing and
    # costs an ambiguity. Render geometry only.
    return dict(name=name, lods=lods, collision=None,
                collision_type="none - deliberately; see notes",
                pivot="start of the piece, joint centreline, z = 0 = road surface",
                notes="4.00 m transverse/longitudinal joint strip, 0.30 m wide: a 52 mm groove with "
                      "a sealant bead whose top sits 2 mm below the road. Ships with NO collider: "
                      "its top face would be exactly coplanar with the road collider and put the "
                      "wheel raycast in a tie. The road beneath carries surface and height.")


BUILDERS = [build_manhole_round_a, build_manhole_round_b, build_utility_cover_square,
            build_storm_grate_road, build_expansion_joint]

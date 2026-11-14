"""VEYRA CITY - D2 Azure Coast stucco set and D8 Redwood Valley market-town set (original designs).

    blender --factory-startup -b -noaudio -P build_coast_town.py -- <key|all>

From the data:
  * D2 `CoastalStucco`, 81 records: 65 `villa` at exactly 20 x 15 m (heights 5-12, median 9.1), 12
    `beach_club` at exactly 24 x 12 m, and 4 one-offs (42 x 26 and 30 x 18 marina buildings, 36 x 20,
    22 x 14). Shape is `house` for 65 and `box` for 16.
  * D8 `MarketTownBrick`, 65 records: every one exactly 12 x 12 m, shape `box`, kind `town`,
    heights 6.4-9.8 (median 7.6).

Two footprints carry 77 of the 81 D2 records and one footprint carries all 65 D8 records, so - as with the
D5 houses - the job is VARIETY at a fixed size, not a spread of sizes: the assembler never repeats a model
within two records of the same chunk, so several designs per footprint are what stop a street reading as
one building copied sixty times.
"""
import json
import os
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
sys.path.insert(0, r"E:\VeyraCity\Tools\Blender")
sys.path.insert(0, HERE)
import vc_tools                      # noqa: E402
import vc_bld_lib as L               # noqa: E402

EXPORT = os.path.join(ROOT, "export")
RAD = __import__("math").radians

COAST = ["VC_Cs_Stucco", "VC_Cs_RoofTile", "VC_Cs_Trim", "VC_Cs_Glass", "VC_Cs_Wood", "VC_Cs_Concrete"]
TOWN = ["VC_Mt_Brick", "VC_Mt_Roof", "VC_Mt_Trim", "VC_Mt_Glass", "VC_Mt_Render", "VC_Mt_Concrete"]
TOWN_RENDERED = ["VC_Mt_Render", "VC_Mt_Roof", "VC_Mt_Trim", "VC_Mt_Glass", "VC_Mt_Brick", "VC_Mt_Concrete"]
WALL, ROOF, TRIM, GLASS, WOOD, CONC = 0, 1, 2, 3, 4, 5

G_SILL, G_H = 0.95, 1.55
U_SILL, U_H = 4.15, 1.45


def pergola(bm, x0, x1, y0, y1, z, m_post, m_beam, posts=3, rafters=5):
    """Posts, beams and rafters - the shade structure that makes a coastal frontage read."""
    if L.DETAIL >= 2:
        return
    for i in range(posts):
        x = x0 + (x1 - x0) * i / max(1, posts - 1)
        L.box(bm, (0.16, 0.16, z), base_at=(x, y0 + 0.08, 0.0), m=m_post)
        L.box(bm, (0.16, 0.16, z), base_at=(x, y1 - 0.08, 0.0), m=m_post)
    L.box(bm, (x1 - x0 + 0.3, 0.14, 0.22), base_at=((x0 + x1) / 2.0, y0 + 0.08, z), m=m_beam)
    L.box(bm, (x1 - x0 + 0.3, 0.14, 0.22), base_at=((x0 + x1) / 2.0, y1 - 0.08, z), m=m_beam)
    if L.DETAIL == 0:
        for j in range(rafters):
            x = x0 + (x1 - x0) * j / max(1, rafters - 1)
            L.box(bm, (0.10, y1 - y0 + 0.3, 0.14), base_at=(x, (y0 + y1) / 2.0, z + 0.22), m=m_beam)


def railing(bm, axis, fixed, a, b, z, m, h=1.05):
    """Balcony / terrace railing: two rails and a few posts (never a full baluster run - too expensive)."""
    if L.DETAIL >= 2:
        return
    n = max(2, int((b - a) / 1.6))
    for i in range(n + 1):
        t = a + (b - a) * i / n
        if axis == "X":
            L.box(bm, (0.08, 0.08, h), base_at=(t, fixed, z), m=m)
        else:
            L.box(bm, (0.08, 0.08, h), base_at=(fixed, t, z), m=m)
    for zz in (h - 0.06, h * 0.45):
        if axis == "X":
            L.box(bm, (b - a, 0.07, 0.07), base_at=((a + b) / 2.0, fixed, z + zz), m=m)
        else:
            L.box(bm, (0.07, b - a, 0.07), base_at=(fixed, (a + b) / 2.0, z + zz), m=m)


def shopfront(bm, w, y_front, h, m_trim, bays=2, stall=0.45):
    """Small-town shopfront: glazed bays on a stallriser, a recessed door, a fascia board over."""
    if L.DETAIL >= 2:
        return
    span = w / bays
    for i in range(bays):
        cx = -w / 2.0 + span * (i + 0.5)
        bw = span - 0.55
        L.box(bm, (bw, 0.10, h - stall - 0.55), base_at=(cx, y_front + 0.30, stall), m=GLASS)
        L.box(bm, (bw + 0.1, 0.22, stall), base_at=(cx, y_front + 0.22, 0.0), m=CONC)
        L.box(bm, (0.55, 0.26, h - 0.4), base_at=(-w / 2.0 + span * i, y_front + 0.20, 0.0), m=m_trim)
    L.box(bm, (0.55, 0.26, h - 0.4), base_at=(w / 2.0, y_front + 0.20, 0.0), m=m_trim)
    L.box(bm, (w + 0.2, 0.30, 0.62), base_at=(0, y_front - 0.02, h - 0.4), m=m_trim)   # fascia


# ------------------------------------------------------------------ D2 Azure Coast, villas 20 x 15

VW, VD = 20.0, 15.0
VFY, VBY, VLX, VRX = -VD / 2.0, VD / 2.0, -VW / 2.0, VW / 2.0


def _villa_shell(bm, z0, z1, front, back, left, right, sill=True):
    ms = TRIM if sill else None
    L.wall(bm, "X", VFY, VLX, VRX, z0, z1, 0.32, WALL, front, GLASS, sill=sill, m_sill=ms, inward=1)
    L.wall(bm, "X", VBY, VLX, VRX, z0, z1, 0.32, WALL, back, GLASS, sill=sill, m_sill=ms, inward=-1)
    L.wall(bm, "Y", VLX, VFY, VBY, z0, z1, 0.32, WALL, left, GLASS, sill=sill, m_sill=ms, inward=1)
    L.wall(bm, "Y", VRX, VFY, VBY, z0, z1, 0.32, WALL, right, GLASS, sill=sill, m_sill=ms, inward=-1)


def villa_a(lod=0):
    """Two storey, hipped terracotta roof, first-floor balcony across the frontage, ground pergola."""
    bm = bmesh.new()
    eave, ridge = 6.60, 9.60
    _villa_shell(bm, 0.0, eave,
                 [(-6.0, G_SILL, 2.60, G_H), (-1.2, 0.0, 2.40, 2.45), (4.6, G_SILL, 2.60, G_H)]
                 + L.win_row(VLX, VRX, 4, 1.20, U_SILL, U_H),
                 L.win_row(VLX, VRX, 3, 1.40, G_SILL, G_H) + L.win_row(VLX, VRX, 3, 1.20, U_SILL, U_H),
                 L.win_row(VFY, VBY, 2, 1.30, G_SILL, G_H) + L.win_row(VFY, VBY, 2, 1.20, U_SILL, U_H),
                 L.win_row(VFY, VBY, 2, 1.30, G_SILL, G_H) + L.win_row(VFY, VBY, 2, 1.20, U_SILL, U_H))
    L.box(bm, (2.40, 0.10, 2.45), loc=(-1.2, VFY + 0.20, 1.225), m=GLASS)          # sliding doors
    L.box(bm, (12.0, 2.20, 0.28), base_at=(0, VFY - 1.10, 3.30), m=CONC)           # balcony slab
    railing(bm, "X", VFY - 2.15, -6.0, 6.0, 3.58, TRIM)
    L.box(bm, (14.0, 2.60, 0.14), base_at=(0, VFY - 1.30, 0.0), m=CONC)            # terrace
    pergola(bm, -5.0, 5.0, VFY - 2.5, VFY - 0.3, 2.70, WOOD, WOOD)
    L.hip_roof(bm, VW, VD, eave, ridge, ROOF, ov=0.65, ridge_frac=0.45)
    L.fascia(bm, VW, VD, eave, TRIM, ov=0.65)
    return bm


def villa_b(lod=0):
    """Flat roof behind a parapet, projecting upper volume, full-height glazing, external stair."""
    bm = bmesh.new()
    top = 9.60 - 0.55
    _villa_shell(bm, 0.0, top,
                 [(-5.2, 0.0, 4.20, 2.60), (3.0, G_SILL, 3.20, G_H),
                  (-4.4, U_SILL, 4.60, 1.70), (4.2, U_SILL, 2.20, 1.70)],
                 [(-3.0, G_SILL, 3.00, G_H), (3.6, G_SILL, 2.00, G_H),
                  (-3.0, U_SILL, 3.00, 1.70), (3.6, U_SILL, 2.00, 1.70)],
                 [(0.0, G_SILL, 2.20, G_H), (0.0, U_SILL, 2.20, 1.70)],
                 [(-2.0, G_SILL, 1.80, G_H), (2.4, U_SILL, 1.80, 1.70)], sill=False)
    L.box(bm, (4.20, 0.10, 2.60), loc=(-5.2, VFY + 0.20, 1.30), m=GLASS)
    L.box(bm, (9.0, 2.40, 0.30), base_at=(-2.0, VFY - 1.20, 3.45), m=WALL)         # projecting volume
    L.box(bm, (9.0, 2.40, 3.10), base_at=(-2.0, VFY - 1.20, 3.75), m=WALL)
    L.box(bm, (7.6, 0.10, 1.80), loc=(-2.0, VFY - 2.35, 5.45), m=GLASS)
    railing(bm, "X", VFY - 1.25, 3.2, 9.2, 3.75, TRIM)                             # roof-terrace rail
    L.box(bm, (16.0, 3.00, 0.14), base_at=(0, VFY - 1.50, 0.0), m=CONC)
    for i in range(5):                                                             # external stair
        L.box(bm, (1.6, 0.34, 0.18), base_at=(8.2, VFY - 0.4 - i * 0.34, i * 0.18), m=CONC)
    L.box(bm, (VW, VD, 0.16), base_at=(0, 0, top), m=CONC)
    L.parapet(bm, VW, VD, top + 0.16, WALL, h=0.40, t=0.26, m_cap=CONC)
    return bm


def villa_c(lod=0):
    """Split gable with terracotta tile, side loggia of square piers, shuttered windows."""
    bm = bmesh.new()
    eave, ridge = 6.40, 9.60
    _villa_shell(bm, 0.0, eave,
                 [(-5.6, G_SILL, 1.70, G_H), (-1.0, 0.0, 1.20, 2.40), (3.4, G_SILL, 1.70, G_H),
                  (6.8, G_SILL, 1.70, G_H)] + L.win_row(VLX, VRX, 4, 1.10, U_SILL, U_H),
                 L.win_row(VLX, VRX, 3, 1.40, G_SILL, G_H) + L.win_row(VLX, VRX, 3, 1.10, U_SILL, U_H),
                 L.win_row(VFY, VBY, 2, 1.20, U_SILL, U_H),
                 L.win_row(VFY, VBY, 2, 1.20, G_SILL, G_H) + L.win_row(VFY, VBY, 2, 1.20, U_SILL, U_H))
    L.door(bm, "X", VFY, -1.0, 0.0, 1.20, 2.40, 0.32, WOOD, TRIM, inward=1)
    if L.DETAIL < 2:                                                               # window shutters
        for (x, z) in ((-5.6, G_SILL), (3.4, G_SILL), (6.8, G_SILL)):
            for sx in (-1, 1):
                L.box(bm, (0.42, 0.07, G_H), base_at=(x + sx * 1.14, VFY - 0.05, z), m=TRIM)
    for i in range(4):                                                             # loggia piers (left)
        L.box(bm, (0.52, 0.52, 3.20), base_at=(VLX - 1.90, VFY + 1.8 + i * 3.4, 0.0), m=WALL)
    L.box(bm, (2.60, 12.0, 0.34), base_at=(VLX - 1.30, 0.6, 3.20), m=WALL)
    L.box(bm, (2.60, 12.0, 0.14), base_at=(VLX - 1.30, 0.6, 0.0), m=CONC)
    L.gable_roof(bm, VW, VD, eave, ridge, ROOF, "X", ov=0.70)
    L.fascia(bm, VW, VD, eave, TRIM, ov=0.70)
    L.chimney(bm, 7.2, 2.4, 6.8, 9.50, WALL, m_cap=CONC)
    return bm


def villa_d(lod=0):
    """Single-storey bungalow, low-pitch tile roof, deep shaded porch."""
    bm = bmesh.new()
    eave, ridge = 4.50, 6.40
    _villa_shell(bm, 0.0, eave,
                 [(-6.2, G_SILL, 2.00, G_H), (-1.6, 0.0, 2.60, 2.45), (4.8, G_SILL, 2.60, G_H)],
                 L.win_row(VLX, VRX, 3, 1.60, G_SILL, G_H),
                 L.win_row(VFY, VBY, 2, 1.40, G_SILL, G_H),
                 L.win_row(VFY, VBY, 2, 1.40, G_SILL, G_H))
    L.box(bm, (2.60, 0.10, 2.45), loc=(-1.6, VFY + 0.20, 1.225), m=GLASS)
    L.box(bm, (16.0, 3.20, 0.14), base_at=(0, VFY - 1.60, 0.0), m=CONC)
    pergola(bm, -6.5, 6.5, VFY - 3.0, VFY - 0.4, 2.80, WOOD, WOOD, posts=4, rafters=7)
    L.hip_roof(bm, VW, VD, eave, ridge, ROOF, ov=0.85, ridge_frac=0.50)
    L.fascia(bm, VW, VD, eave, TRIM, ov=0.85)
    return bm


def villa_e(lod=0):
    """One and a half storey: a pitched wing beside a flat-roofed terrace block."""
    bm = bmesh.new()
    eave, ridge = 5.00, 7.80
    flat = 7.80 - 0.50
    mid = 2.0
    L.wall(bm, "X", VFY, VLX, mid, 0.0, eave, 0.32, WALL,
           [(-6.4, G_SILL, 1.80, G_H), (-2.2, 0.0, 2.40, 2.45), (0.4, U_SILL, 1.20, 1.30)],
           GLASS, sill=True, m_sill=TRIM, inward=1)
    L.wall(bm, "X", VFY, mid, VRX, 0.0, flat, 0.32, WALL,
           [(5.2, G_SILL, 3.40, G_H), (5.2, U_SILL, 3.40, 1.60)], GLASS, sill=False, inward=1)
    L.wall(bm, "X", VBY, VLX, VRX, 0.0, eave, 0.32, WALL,
           L.win_row(VLX, VRX, 3, 1.40, G_SILL, G_H), GLASS, sill=True, m_sill=TRIM, inward=-1)
    L.wall(bm, "Y", VLX, VFY, VBY, 0.0, eave, 0.32, WALL,
           L.win_row(VFY, VBY, 2, 1.30, G_SILL, G_H), GLASS, sill=True, m_sill=TRIM, inward=1)
    L.wall(bm, "Y", VRX, VFY, VBY, 0.0, flat, 0.32, WALL,
           L.win_row(VFY, VBY, 2, 1.30, G_SILL, G_H), GLASS, sill=True, m_sill=TRIM, inward=-1)
    L.box(bm, (2.40, 0.10, 2.45), loc=(-2.2, VFY + 0.20, 1.225), m=GLASS)
    before = set(bm.faces)
    L.gable_roof(bm, mid - VLX, VD, eave, ridge, ROOF, "X", ov=0.55)
    bmesh.ops.transform(bm, matrix=Matrix.Translation(Vector(((VLX + mid) / 2.0, 0.0, 0.0))),
                        verts=list({v for f in bm.faces if f not in before for v in f.verts}))
    L.box(bm, (VRX - mid, VD, 0.16), base_at=((mid + VRX) / 2.0, 0, flat), m=CONC)
    L.parapet(bm, VRX - mid, VD, flat + 0.16, WALL, h=0.34, t=0.24, m_cap=CONC)
    L.box(bm, (14.0, 2.40, 0.14), base_at=(0, VFY - 1.20, 0.0), m=CONC)
    pergola(bm, 2.4, 8.4, VFY - 2.2, VFY - 0.3, 2.70, WOOD, WOOD)
    return bm


# ------------------------------------------------------------------ D2 beach clubs 24 x 12

CW, CD = 24.0, 12.0
CFY, CBY, CLX, CRX = -CD / 2.0, CD / 2.0, -CW / 2.0, CW / 2.0


def club_a(lod=0):
    """Single-storey beach pavilion: glazed frontage under a deep flat roof, timber deck."""
    bm = bmesh.new()
    top = 7.00 - 0.65
    L.wall(bm, "X", CFY, CLX, CRX, 0.0, top, 0.30, WALL,
           [(-8.0, 0.0, 4.40, 3.10), (-1.0, 0.0, 5.20, 3.10), (6.6, 0.0, 4.40, 3.10)], None, inward=1)
    L.wall(bm, "X", CBY, CLX, CRX, 0.0, top, 0.30, WALL,
           L.win_row(CLX, CRX, 4, 1.40, G_SILL, G_H), GLASS, sill=True, m_sill=TRIM, inward=-1)
    L.wall(bm, "Y", CLX, CFY, CBY, 0.0, top, 0.30, WALL,
           L.win_row(CFY, CBY, 2, 1.40, G_SILL, G_H), GLASS, sill=True, m_sill=TRIM, inward=1)
    L.wall(bm, "Y", CRX, CFY, CBY, 0.0, top, 0.30, WALL,
           L.win_row(CFY, CBY, 2, 1.40, G_SILL, G_H), GLASS, sill=True, m_sill=TRIM, inward=-1)
    for (cx, bw) in ((-8.0, 4.40), (-1.0, 5.20), (6.6, 4.40)):
        L.box(bm, (bw, 0.10, 3.10), loc=(cx, CFY + 0.22, 1.55), m=GLASS)
    L.box(bm, (CW + 2.4, CD + 2.0, 0.42), base_at=(0, -0.4, top), m=CONC)          # deep flat roof
    L.box(bm, (CW + 2.4, 0.26, 0.55), base_at=(0, CFY - 1.0, top - 0.10), m=TRIM)  # fascia
    L.box(bm, (CW + 3.0, 4.20, 0.16), base_at=(0, CFY - 2.1, 0.0), m=WOOD)         # deck
    railing(bm, "X", CFY - 4.1, -11.0, 11.0, 0.16, TRIM, h=0.95)
    for sx in (-1, 1):                                                             # roof columns
        L.box(bm, (0.22, 0.22, top), base_at=(sx * 10.6, CFY - 0.9, 0.16), m=TRIM)
    return bm


def club_b(lod=0):
    """Two storey club: balcony over a glazed ground floor, roof terrace behind a parapet."""
    bm = bmesh.new()
    top = 10.20 - 0.55
    L.wall(bm, "X", CFY, CLX, CRX, 0.0, 3.60, 0.30, WALL,
           [(-7.6, 0.0, 5.00, 3.00), (1.0, 0.0, 4.20, 3.00), (8.2, 0.0, 3.20, 3.00)], None, inward=1)
    for (cx, bw) in ((-7.6, 5.00), (1.0, 4.20), (8.2, 3.20)):
        L.box(bm, (bw, 0.10, 3.00), loc=(cx, CFY + 0.22, 1.50), m=GLASS)
    L.wall(bm, "X", CFY, CLX, CRX, 3.60, top, 0.30, WALL,
           [(-7.0, 4.95, 4.00, 1.80), (2.0, 4.95, 3.20, 1.80), (8.6, 4.95, 2.20, 1.80)],
           GLASS, sill=False, inward=1)
    L.wall(bm, "X", CBY, CLX, CRX, 0.0, top, 0.30, WALL,
           L.win_row(CLX, CRX, 4, 1.40, G_SILL, G_H) + L.win_row(CLX, CRX, 4, 1.30, 4.95, 1.80),
           GLASS, sill=True, m_sill=TRIM, inward=-1)
    L.wall(bm, "Y", CLX, CFY, CBY, 0.0, top, 0.30, WALL,
           L.win_row(CFY, CBY, 2, 1.30, G_SILL, G_H) + L.win_row(CFY, CBY, 2, 1.30, 4.95, 1.80),
           GLASS, sill=True, m_sill=TRIM, inward=1)
    L.wall(bm, "Y", CRX, CFY, CBY, 0.0, top, 0.30, WALL,
           L.win_row(CFY, CBY, 2, 1.30, G_SILL, G_H) + L.win_row(CFY, CBY, 2, 1.30, 4.95, 1.80),
           GLASS, sill=True, m_sill=TRIM, inward=-1)
    L.box(bm, (CW + 1.6, 2.60, 0.26), base_at=(0, CFY - 1.30, 3.34), m=CONC)       # balcony
    railing(bm, "X", CFY - 2.55, -12.0, 12.0, 3.60, TRIM)
    L.box(bm, (CW, CD, 0.16), base_at=(0, 0, top), m=CONC)
    L.parapet(bm, CW, CD, top + 0.16, WALL, h=0.40, t=0.24, m_cap=CONC)
    return bm


MW, MD = 33.0, 20.0


def marina(lod=0):
    """Marina / clubhouse: a long low hall with a shallow metal roof and an open glazed frontage."""
    bm = bmesh.new()
    eave, ridge = 5.20, 7.60
    fy, by, lx, rx = -MD / 2.0, MD / 2.0, -MW / 2.0, MW / 2.0
    L.wall(bm, "X", fy, lx, rx, 0.0, eave, 0.30, WALL,
           [(-11.0, 0.0, 5.60, 3.40), (-2.0, 0.0, 6.40, 3.40), (8.0, 0.0, 5.60, 3.40),
            (-11.0, 4.00, 5.00, 0.90), (-2.0, 4.00, 5.60, 0.90), (8.0, 4.00, 5.00, 0.90)],
           GLASS, sill=False, inward=1)
    for (cx, bw) in ((-11.0, 5.60), (-2.0, 6.40), (8.0, 5.60)):
        L.box(bm, (bw, 0.10, 3.40), loc=(cx, fy + 0.22, 1.70), m=GLASS)
    L.wall(bm, "X", by, lx, rx, 0.0, eave, 0.30, WALL,
           L.win_row(lx, rx, 5, 1.40, G_SILL, G_H), GLASS, sill=True, m_sill=TRIM, inward=-1)
    L.wall(bm, "Y", lx, fy, by, 0.0, eave, 0.30, WALL,
           L.win_row(fy, by, 3, 1.40, G_SILL, G_H), GLASS, sill=True, m_sill=TRIM, inward=1)
    L.wall(bm, "Y", rx, fy, by, 0.0, eave, 0.30, WALL,
           L.win_row(fy, by, 3, 1.40, G_SILL, G_H), GLASS, sill=True, m_sill=TRIM, inward=-1)
    L.box(bm, (MW + 4.0, 5.00, 0.16), base_at=(0, fy - 2.5, 0.0), m=WOOD)
    railing(bm, "X", fy - 4.9, -15.0, 15.0, 0.16, TRIM, h=0.95)
    L.gable_roof(bm, MW, MD, eave, ridge, ROOF, "X", ov=0.90)
    L.fascia(bm, MW, MD, eave, TRIM, ov=0.90)
    return bm


# ------------------------------------------------------------------ D8 market town 12 x 12

TW, TD = 12.0, 12.0
TFY, TBY, TLX, TRX = -TD / 2.0, TD / 2.0, -TW / 2.0, TW / 2.0


def _town_shell(bm, z0, z1, front, back=None, left=None, right=None, sill=True):
    ms = TRIM if sill else None
    L.wall(bm, "X", TFY, TLX, TRX, z0, z1, 0.30, WALL, front, GLASS, sill=sill, m_sill=ms, inward=1)
    L.wall(bm, "X", TBY, TLX, TRX, z0, z1, 0.30, WALL, back or [], GLASS, sill=sill, m_sill=ms, inward=-1)
    L.wall(bm, "Y", TLX, TFY, TBY, z0, z1, 0.30, WALL, left or [], GLASS, sill=sill, m_sill=ms, inward=1)
    L.wall(bm, "Y", TRX, TFY, TBY, z0, z1, 0.30, WALL, right or [], GLASS, sill=sill, m_sill=ms, inward=-1)


def terrace_a(lod=0):
    """Two storey over a shopfront, brick, parapet with a stone coping."""
    bm = bmesh.new()
    gh, top = 3.90, 7.60 - 0.45
    _town_shell(bm, gh, top, L.win_row(TLX, TRX, 3, 1.05, 4.60, 1.55),
                L.win_row(TLX, TRX, 2, 1.05, 4.60, 1.55),
                L.win_row(TFY, TBY, 2, 1.00, 4.60, 1.55),
                L.win_row(TFY, TBY, 2, 1.00, 4.60, 1.55))
    L.box(bm, (TW, TD, gh), base_at=(0, 0.2, 0.0), m=WALL)
    shopfront(bm, TW, TFY, gh, TRIM, bays=2)
    L.box(bm, (TW + 0.3, TD + 0.3, 0.22), base_at=(0, 0, top), m=CONC)           # cornice
    L.parapet(bm, TW, TD, top + 0.22, WALL, h=0.45, t=0.24, m_cap=CONC)
    return bm


def terrace_b(lod=0):
    """Three storey, tall narrow windows, slate roof hidden behind a parapet."""
    bm = bmesh.new()
    gh, top = 3.70, 8.60 - 0.50
    _town_shell(bm, gh, top,
                L.win_row(TLX, TRX, 3, 0.95, 4.35, 1.75) + L.win_row(TLX, TRX, 3, 0.95, 6.60, 1.45),
                L.win_row(TLX, TRX, 2, 0.95, 4.35, 1.75) + L.win_row(TLX, TRX, 2, 0.95, 6.60, 1.45),
                L.win_row(TFY, TBY, 2, 0.95, 4.35, 1.75),
                L.win_row(TFY, TBY, 2, 0.95, 4.35, 1.75))
    L.box(bm, (TW, TD, gh), base_at=(0, 0.2, 0.0), m=WALL)
    shopfront(bm, TW, TFY, gh, TRIM, bays=2)
    if L.DETAIL < 2:
        L.box(bm, (TW + 0.26, TD + 0.26, 0.16), base_at=(0, 0, 6.30), m=CONC)    # string course
    L.box(bm, (TW + 0.3, TD + 0.3, 0.20), base_at=(0, 0, top), m=CONC)
    L.parapet(bm, TW, TD, top + 0.20, WALL, h=0.50, t=0.24, m_cap=CONC)
    return bm


def terrace_c(lod=0):
    """Three storey with the gable facing the street and a slate roof."""
    bm = bmesh.new()
    gh, eave, ridge = 3.80, 7.20, 9.60
    _town_shell(bm, gh, eave,
                L.win_row(TLX, TRX, 2, 1.10, 4.45, 1.70),
                L.win_row(TLX, TRX, 2, 1.10, 4.45, 1.70),
                L.win_row(TFY, TBY, 2, 1.00, 4.45, 1.70),
                L.win_row(TFY, TBY, 2, 1.00, 4.45, 1.70))
    L.box(bm, (TW, TD, gh), base_at=(0, 0.2, 0.0), m=WALL)
    shopfront(bm, TW, TFY, gh, TRIM, bays=1)
    L.gable_roof(bm, TW, TD, eave, ridge, ROOF, "Y", ov=0.35)                    # gable to the street
    L.box(bm, (1.20, 0.22, 1.00), loc=(0.0, TFY - 0.1, 7.90), m=GLASS)           # loft window
    L.chimney(bm, 4.2, 3.0, 7.4, 9.50, WALL, m_cap=CONC)
    return bm


def terrace_d(lod=0):
    """Two storey town house, pitched slate roof, no shopfront."""
    bm = bmesh.new()
    eave, ridge = 5.20, 6.80
    _town_shell(bm, 0.0, eave,
                [(-3.6, G_SILL, 1.20, 1.50), (0.6, 0.0, 1.05, 2.15), (3.9, G_SILL, 1.20, 1.50)]
                + L.win_row(TLX, TRX, 3, 1.00, 3.55, 1.35),
                L.win_row(TLX, TRX, 2, 1.10, G_SILL, 1.50) + L.win_row(TLX, TRX, 2, 1.00, 3.55, 1.35),
                L.win_row(TFY, TBY, 2, 1.00, 3.55, 1.35),
                L.win_row(TFY, TBY, 2, 1.00, 3.55, 1.35))
    L.door(bm, "X", TFY, 0.6, 0.0, 1.05, 2.15, 0.30, WOOD, TRIM, inward=1)
    L.steps(bm, 0.6, TFY, 1.40, 0.16, CONC, n=2)
    L.gable_roof(bm, TW, TD, eave, ridge, ROOF, "X", ov=0.30)
    L.fascia(bm, TW, TD, eave, TRIM, ov=0.30)
    L.chimney(bm, -4.4, 2.0, 5.4, 6.70, WALL, m_cap=CONC)
    return bm


def terrace_e(lod=0):
    """Painted render over a shopfront with an awning, moulded cornice."""
    bm = bmesh.new()
    gh, top = 4.00, 7.80 - 0.45
    _town_shell(bm, gh, top,
                L.win_row(TLX, TRX, 3, 1.00, 4.70, 1.60),
                L.win_row(TLX, TRX, 2, 1.00, 4.70, 1.60),
                L.win_row(TFY, TBY, 2, 1.00, 4.70, 1.60),
                L.win_row(TFY, TBY, 2, 1.00, 4.70, 1.60))
    L.box(bm, (TW, TD, gh), base_at=(0, 0.2, 0.0), m=WALL)
    shopfront(bm, TW, TFY, gh, TRIM, bays=2)
    if L.DETAIL == 0:
        L.box(bm, (TW - 0.6, 1.30, 0.12), loc=(0, TFY - 0.75, gh - 0.75),
              rot=Matrix.Rotation(RAD(-12), 4, "X"), m=TRIM)                     # awning
    L.box(bm, (TW + 0.4, TD + 0.4, 0.26), base_at=(0, 0, top), m=CONC)
    L.box(bm, (TW + 0.22, TD + 0.22, 0.18), base_at=(0, 0, top - 0.18), m=CONC)
    L.parapet(bm, TW, TD, top + 0.26, WALL, h=0.42, t=0.24, m_cap=CONC)
    return bm


SPECS = [
    ("villa_a",  villa_a,  20.0, 15.0,  9.60, COAST, "coast", ["CoastalStucco"], ["D2"]),
    ("villa_b",  villa_b,  20.0, 15.0,  9.60, COAST, "coast", ["CoastalStucco"], ["D2"]),
    ("villa_c",  villa_c,  20.0, 15.0,  9.60, COAST, "coast", ["CoastalStucco"], ["D2"]),
    ("villa_d",  villa_d,  20.0, 15.0,  6.40, COAST, "coast", ["CoastalStucco"], ["D2"]),
    ("villa_e",  villa_e,  20.0, 15.0,  7.80, COAST, "coast", ["CoastalStucco"], ["D2"]),
    ("club_a",   club_a,   24.0, 12.0,  7.00, COAST, "coast", ["CoastalStucco"], ["D2"]),
    ("club_b",   club_b,   24.0, 12.0, 10.20, COAST, "coast", ["CoastalStucco"], ["D2"]),
    ("marina",   marina,   33.0, 20.0,  7.60, COAST, "coast", ["CoastalStucco"], ["D2"]),
    ("terrace_a", terrace_a, 12.0, 12.0, 7.60, TOWN, "town", ["MarketTownBrick"], ["D8"]),
    ("terrace_b", terrace_b, 12.0, 12.0, 8.60, TOWN, "town", ["MarketTownBrick"], ["D8"]),
    ("terrace_c", terrace_c, 12.0, 12.0, 9.60, TOWN, "town", ["MarketTownBrick"], ["D8"]),
    ("terrace_d", terrace_d, 12.0, 12.0, 6.80, TOWN, "town", ["MarketTownBrick"], ["D8"]),
    ("terrace_e", terrace_e, 12.0, 12.0, 7.80, TOWN_RENDERED, "town", ["MarketTownBrick"], ["D8"]),
]
NAMES = {s[0]: ("VC_Coast_" if s[6] == "coast" else "VC_Town_")
         + "".join(p.capitalize() for p in s[0].split("_")) for s in SPECS}


def reset():
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    for m in list(bpy.data.materials):
        bpy.data.materials.remove(m, do_unlink=True)
    for me in list(bpy.data.meshes):
        if me.users == 0:
            bpy.data.meshes.remove(me)
    vc_tools.vc_setup_scene()


def build(key):
    _, fn, w, l, h, slots, group, styles, districts = next(s for s in SPECS if s[0] == key)
    reset()
    lods, uv = [], True
    for lod in (0, 1, 2):
        L.set_detail(lod)
        o = L.new_object("%s_LOD%d" % (NAMES[key], lod), fn(lod), slots)
        uv = L.unwrap(o, margin=0.008, cube=2.0) and uv
        lods.append(o)
    L.set_detail(0)
    obj = lods[0]
    pts = [obj.matrix_world @ Vector(c) for c in obj.bound_box]
    lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    rep0 = L.mesh_report(obj)
    parent = bpy.data.objects.new(NAMES[key], None)
    bpy.context.scene.collection.objects.link(parent)
    for o in lods:
        o.parent = parent
    col = L.collider_box("UBX_" + parent.name, (w, l, hi.z - lo.z), base_at=(0.0, 0.0, 0.0))
    col.parent = parent
    col.matrix_parent_inverse = parent.matrix_world.inverted()

    os.makedirs(EXPORT, exist_ok=True)
    fbx = os.path.join(EXPORT, parent.name + ".fbx")
    vc_tools.vc_export_fbx([parent], fbx, mode="prop", pivot=parent, path_mode="STRIP")
    blend = os.path.join(ROOT, parent.name + ".blend")
    bpy.ops.wm.save_as_mainfile(filepath=blend)
    return {
        "key": key, "name": parent.name, "set": group, "family": fn.__name__,
        "style_kit": styles, "districts": districts,
        "blend": os.path.relpath(blend, r"E:\VeyraCity").replace("\\", "/"),
        "fbx": os.path.relpath(fbx, r"E:\VeyraCity").replace("\\", "/"),
        "footprint_m": [w, l], "height_m": round(hi.z - lo.z, 2), "design_height_m": h,
        "bbox_m": [round(hi.x - lo.x, 2), round(hi.y - lo.y, 2), round(hi.z - lo.z, 2)],
        "base_z": round(lo.z, 5), "uv": uv, "lod_mode": "authored", "lod_ratios": None,
        "lods": [L.mesh_report(o) for o in lods],
        "collision": [col.name], "slots": rep0["slots"],
    }


def main():
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else ["all"]
    keys = [s[0] for s in SPECS] if args[0] == "all" else args
    out = [build(k) for k in keys]
    for group, path in (("coast", "report_coast.json"), ("town", "report_town.json")):
        rows = [r for r in out if r["set"] == group]
        full = os.path.join(HERE, path)
        old = []
        if os.path.exists(full) and args[0] != "all":
            old = [r for r in json.load(open(full)) if r["key"] not in keys]
        if rows or old:
            with open(full, "w", encoding="utf-8") as fh:
                json.dump(old + rows, fh, indent=1)
    print("VC_COASTTOWN_OK")


main()

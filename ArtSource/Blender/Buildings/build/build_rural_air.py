"""VEYRA CITY - the last five record groups: D4 Alpine, D8 Rural, D7 Aviation, D5 SuburbanRetail, Roadside.

    blender --factory-startup -b -noaudio -P build_rural_air.py -- <key|all>

Grouping (world_building_placements.csv):
  * `Alpine` 25, all D4: 24 `chalet` at exactly 12 x 10 m (heights 7.1-9.0) and 1 `workshop` at 42 x 24.
  * `Rural` 25, all D8: `barn` 9 at 26 x 14 (h 9), `silo` 8 at 7 x 7 (h 16, cylinder),
    `farmhouse` 7 at 12 x 10 (h 7), plus one 64 x 30 barn and one 32 x 18 `mill`.
  * `Aviation` 8, all D7: 6 `hangar` at 62 x 74 (h 17), 1 `terminal` 130 x 36 (h 12),
    1 `grandstand` 18 x 150 (h 14).
  * `SuburbanRetail` 3, all D5: 40 x 30 (h 7), 120 x 60 (h 10), 24 x 14 (h 5).
  * `Roadside` 1, D4: a 26 x 16 `gas_station` at 6 m.

Each style kit gets its own catalogue source so a chalet cannot be placed on a Redwood Valley farmhouse
record (both are 12 x 10 m - only the district and style lists keep them apart).
"""
import json
import math
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

ALP = ["VC_Al_Timber", "VC_Al_Stone", "VC_Al_Roof", "VC_Al_Glass", "VC_Al_Trim", "VC_Al_Concrete"]
RUR = ["VC_Ru_Board", "VC_Ru_Roof", "VC_Ru_Metal", "VC_Ru_Glass", "VC_Ru_Trim", "VC_Ru_Concrete"]
AVI = ["VC_Av_Clad", "VC_Av_Roof", "VC_Av_Glass", "VC_Av_Metal", "VC_Av_Concrete"]
RET = ["VC_Rt_Panel", "VC_Rt_Glass", "VC_Rt_Metal", "VC_Rt_Concrete", "VC_Rt_Brick"]
# slot indices per family
A_TIM, A_STO, A_ROOF, A_GLA, A_TRIM, A_CON = 0, 1, 2, 3, 4, 5
R_BRD, R_ROOF, R_MET, R_GLA, R_TRIM, R_CON = 0, 1, 2, 3, 4, 5
V_CLAD, V_ROOF, V_GLA, V_MET, V_CON = 0, 1, 2, 3, 4
T_PAN, T_GLA, T_MET, T_CON, T_BRK = 0, 1, 2, 3, 4

GS, GH = 0.95, 1.35


def box_shell(bm, w, d, z0, z1, m, t=0.28, front=(), back=(), left=(), right=(), glass=None,
              sill=None, m_sill=None):
    fy, by, lx, rx = -d / 2.0, d / 2.0, -w / 2.0, w / 2.0
    L.wall(bm, "X", fy, lx, rx, z0, z1, t, m, front, glass, sill=sill, m_sill=m_sill, inward=1)
    L.wall(bm, "X", by, lx, rx, z0, z1, t, m, back, glass, sill=sill, m_sill=m_sill, inward=-1)
    L.wall(bm, "Y", lx, fy, by, z0, z1, t, m, left, glass, sill=sill, m_sill=m_sill, inward=1)
    L.wall(bm, "Y", rx, fy, by, z0, z1, t, m, right, glass, sill=sill, m_sill=m_sill, inward=-1)


# ------------------------------------------------------------------ D4 Alpine, chalets 12 x 10

def chalet_a(lod=0):
    """Broad gable to the street under deep eaves, stone ground floor, timber upper, full-width balcony."""
    bm = bmesh.new()
    w, d = 12.0, 10.0
    eave, ridge = 5.10, 8.00
    box_shell(bm, w, d, 0.0, 2.70, A_STO, front=[(-3.4, GS, 1.20, GH), (1.2, 0.0, 1.10, 2.15)],
              back=L.win_row(-6, 6, 2, 1.20, GS, GH), left=L.win_row(-5, 5, 1, 1.20, GS, GH),
              right=L.win_row(-5, 5, 1, 1.20, GS, GH), glass=A_GLA, sill=True, m_sill=A_TRIM)
    L.door(bm, "X", -d / 2.0, 1.2, 0.0, 1.10, 2.15, 0.28, A_TIM, A_TRIM, inward=1)
    box_shell(bm, w, d, 2.70, eave, A_TIM, front=L.win_row(-6, 6, 3, 1.00, 3.55, 1.25),
              back=L.win_row(-6, 6, 2, 1.00, 3.55, 1.25), left=L.win_row(-5, 5, 1, 1.00, 3.55, 1.25),
              right=L.win_row(-5, 5, 1, 1.00, 3.55, 1.25), glass=A_GLA, sill=True, m_sill=A_TRIM)
    if L.DETAIL < 2:
        L.box(bm, (w + 1.4, 1.50, 0.18), base_at=(0, -d / 2.0 - 0.75, 2.70), m=A_TIM)      # balcony deck
        for i in range(7):
            L.box(bm, (0.10, 0.10, 0.95), base_at=(-w / 2.0 - 0.55 + (w + 1.1) * i / 6.0,
                                                   -d / 2.0 - 1.45, 2.88), m=A_TIM)
        L.box(bm, (w + 1.4, 0.09, 0.09), base_at=(0, -d / 2.0 - 1.45, 3.78), m=A_TIM)
    L.gable_roof(bm, w, d, eave, ridge, A_ROOF, "Y", ov=1.05)
    L.fascia(bm, w, d, eave, A_TRIM, ov=1.05, h=0.26, t=0.12)
    L.chimney(bm, 4.4, 2.2, 5.4, 7.90, A_STO, m_cap=A_CON)
    return bm


def chalet_b(lod=0):
    """Ridge along the street, external timber stair to the upper floor, stone plinth."""
    bm = bmesh.new()
    w, d = 12.0, 10.0
    eave, ridge = 5.40, 8.00
    box_shell(bm, w, d, 0.0, 1.30, A_STO)
    box_shell(bm, w, d, 1.30, eave, A_TIM,
              front=[(-3.8, 2.0, 1.20, 1.25), (0.2, 1.30, 1.10, 2.10), (3.8, 2.0, 1.20, 1.25),
                     (-2.6, 3.9, 1.00, 1.15), (2.6, 3.9, 1.00, 1.15)],
              back=L.win_row(-6, 6, 2, 1.10, 2.0, 1.25) + L.win_row(-6, 6, 2, 1.00, 3.9, 1.15),
              left=L.win_row(-5, 5, 1, 1.10, 2.0, 1.25), right=L.win_row(-5, 5, 1, 1.10, 2.0, 1.25),
              glass=A_GLA, sill=True, m_sill=A_TRIM)
    L.door(bm, "X", -d / 2.0, 0.2, 1.30, 1.10, 2.10, 0.28, A_TIM, A_TRIM, inward=1)
    if L.DETAIL < 2:
        for i in range(6):                                                   # external stair
            L.box(bm, (1.30, 0.32, 0.22), base_at=(-w / 2.0 - 0.75, -d / 2.0 + 1.0 + i * 0.32,
                                                   i * 0.22), m=A_TIM)
        L.box(bm, (0.12, 2.20, 1.00), base_at=(-w / 2.0 - 1.30, -d / 2.0 + 1.9, 1.32), m=A_TIM)
    L.gable_roof(bm, w, d, eave, ridge, A_ROOF, "X", ov=1.10)
    L.fascia(bm, w, d, eave, A_TRIM, ov=1.10, h=0.26, t=0.12)
    return bm


def chalet_c(lod=0):
    """Lower eave with a roof dormer and a big stone chimney breast on the gable end."""
    bm = bmesh.new()
    w, d = 12.0, 10.0
    eave, ridge = 4.30, 8.00
    box_shell(bm, w, d, 0.0, eave, A_TIM,
              front=[(-3.6, GS, 1.30, GH), (0.4, 0.0, 1.10, 2.15), (4.0, GS, 1.30, GH)],
              back=L.win_row(-6, 6, 2, 1.20, GS, GH), left=L.win_row(-5, 5, 1, 1.20, GS, GH),
              right=L.win_row(-5, 5, 1, 1.20, GS, GH), glass=A_GLA, sill=True, m_sill=A_TRIM)
    L.door(bm, "X", -d / 2.0, 0.4, 0.0, 1.10, 2.15, 0.28, A_TIM, A_TRIM, inward=1)
    L.box(bm, (1.90, d + 0.6, eave + 3.0), base_at=(-w / 2.0 + 0.5, 0.2, 0.0), m=A_STO)   # chimney breast
    L.gable_roof(bm, w, d, eave, ridge, A_ROOF, "X", ov=1.00)
    L.fascia(bm, w, d, eave, A_TRIM, ov=1.00, h=0.26, t=0.12)
    L.dormer(bm, 2.4, -3.9, 4.45, 1.80, 1.45, 1.20, A_TIM, A_ROOF, A_GLA, A_TRIM)
    return bm


def workshop_alpine(lod=0):
    """The one D4 `workshop` record, 42 x 24: a long timber shed with big doors and a steep roof."""
    bm = bmesh.new()
    w, d = 42.0, 24.0
    eave, ridge = 5.00, 8.50
    box_shell(bm, w, d, 0.0, 1.00, A_STO)
    box_shell(bm, w, d, 1.00, eave, A_TIM,
              front=[(-12.0, 1.0, 5.00, 3.40), (0.0, 1.0, 5.00, 3.40), (12.0, GS + 1.0, 1.40, GH)],
              back=L.win_row(-20, 20, 4, 1.40, GS + 1.0, GH),
              left=L.win_row(-11, 11, 2, 1.30, GS + 1.0, GH),
              right=L.win_row(-11, 11, 2, 1.30, GS + 1.0, GH), glass=A_GLA, sill=True, m_sill=A_TRIM)
    if L.DETAIL < 2:
        for cx in (-12.0, 0.0):
            L.box(bm, (5.00, 0.14, 3.40), loc=(cx, -d / 2.0 + 0.18, 2.70), m=A_TIM)
    L.gable_roof(bm, w, d, eave, ridge, A_ROOF, "X", ov=0.95)
    L.fascia(bm, w, d, eave, A_TRIM, ov=0.95, h=0.26, t=0.12)
    return bm


# ------------------------------------------------------------------ D8 Rural

def barn_gambrel(lod=0):
    """26 x 14 barn with a gambrel roof - the strongest rural silhouette - and a big sliding door."""
    bm = bmesh.new()
    w, d, eave = 26.0, 14.0, 5.20
    box_shell(bm, w, d, 0.0, eave, R_BRD,
              front=[(0.0, 0.0, 5.20, 4.20), (-9.5, GS, 1.10, GH), (9.5, GS, 1.10, GH)],
              back=L.win_row(-13, 13, 3, 1.10, GS, GH), left=[], right=[], glass=R_GLA,
              sill=True, m_sill=R_TRIM)
    hd = d / 2.0
    pts = [(-hd - 0.5, eave - 0.2), (-hd - 0.5, eave), (-hd * 0.55, eave + 2.3),
           (0.0, eave + 3.8), (hd * 0.55, eave + 2.3), (hd + 0.5, eave), (hd + 0.5, eave - 0.2)]
    L.profile_solid(bm, pts, -w / 2.0 - 0.4, w / 2.0 + 0.4, R_ROOF)
    if L.DETAIL < 2:
        L.box(bm, (5.20, 0.16, 4.20), loc=(0.0, -hd + 0.20, 2.10), m=R_MET)       # sliding door
        L.box(bm, (5.60, 0.22, 0.20), loc=(0.0, -hd + 0.06, 4.40), m=R_MET)       # door track
        L.box(bm, (1.40, 1.40, 0.90), base_at=(0.0, 0.0, eave + 3.6), m=R_MET)    # ridge vent
    return bm


def barn_leanto(lod=0):
    """26 x 14 barn: gable with a lean-to along one side and a clerestory strip."""
    bm = bmesh.new()
    w, d, eave, ridge = 26.0, 14.0, 5.60, 8.40
    box_shell(bm, w, d, 0.0, eave, R_BRD,
              front=[(-5.0, 0.0, 4.60, 4.00), (6.4, GS, 1.20, GH)],
              back=L.win_row(-13, 13, 3, 1.20, GS, GH), left=[], right=[], glass=R_GLA,
              sill=True, m_sill=R_TRIM)
    L.gable_roof(bm, w, d, eave, ridge, R_ROOF, "X", ov=0.60)
    if L.DETAIL < 2:
        L.box(bm, (4.60, 0.16, 4.00), loc=(-5.0, -d / 2.0 + 0.18, 2.00), m=R_MET)
        L.box(bm, (w, 3.60, 0.18), base_at=(0, d / 2.0 + 1.70, 3.60), m=R_ROOF)   # lean-to roof
        for i in range(5):
            L.box(bm, (0.22, 0.22, 3.60), base_at=(-w / 2.0 + 1.0 + i * 6.0, d / 2.0 + 3.3, 0.0), m=R_BRD)
        L.box(bm, (w - 2.0, 0.14, 0.70), loc=(0, -d / 2.0 + 0.10, eave - 0.60), m=R_GLA)
    return bm


def farmhouse_a(lod=0):
    bm = bmesh.new()
    w, d, eave, ridge = 12.0, 10.0, 4.60, 7.00
    box_shell(bm, w, d, 0.0, eave, R_BRD,
              front=[(-3.4, GS, 1.20, GH), (0.6, 0.0, 1.05, 2.10), (4.0, GS, 1.20, GH),
                     (-2.6, 3.2, 1.00, 1.05), (2.6, 3.2, 1.00, 1.05)],
              back=L.win_row(-6, 6, 2, 1.20, GS, GH), left=L.win_row(-5, 5, 1, 1.10, GS, GH),
              right=L.win_row(-5, 5, 1, 1.10, GS, GH), glass=R_GLA, sill=True, m_sill=R_TRIM)
    L.door(bm, "X", -d / 2.0, 0.6, 0.0, 1.05, 2.10, 0.28, R_TRIM, R_TRIM, inward=1)
    L.porch(bm, -1.4, 2.6, -d / 2.0, 1.10, 0.18, 2.55, R_TRIM, R_ROOF, R_CON, posts=2)
    L.gable_roof(bm, w, d, eave, ridge, R_ROOF, "X", ov=0.50)
    L.fascia(bm, w, d, eave, R_TRIM, ov=0.50)
    L.chimney(bm, -4.2, 1.6, 4.8, 6.90, R_BRD, m_cap=R_CON)
    return bm


def farmhouse_b(lod=0):
    bm = bmesh.new()
    w, d, eave, ridge = 12.0, 10.0, 4.90, 7.00
    box_shell(bm, w, d, 0.0, eave, R_BRD,
              front=[(-3.2, GS, 1.30, GH), (1.0, 0.0, 1.05, 2.10), (4.2, GS, 1.10, GH),
                     (0.0, 3.4, 1.10, 1.05)],
              back=L.win_row(-6, 6, 2, 1.20, GS, GH), left=L.win_row(-5, 5, 1, 1.10, GS, GH),
              right=L.win_row(-5, 5, 1, 1.10, GS, GH), glass=R_GLA, sill=True, m_sill=R_TRIM)
    L.door(bm, "X", -d / 2.0, 1.0, 0.0, 1.05, 2.10, 0.28, R_TRIM, R_TRIM, inward=1)
    L.box(bm, (2.10, 1.00, 0.16), base_at=(1.0, -d / 2.0 - 0.50, 2.45), m=R_ROOF)
    L.hip_roof(bm, w, d, eave, ridge, R_ROOF, ov=0.55, ridge_frac=0.42)
    L.fascia(bm, w, d, eave, R_TRIM, ov=0.55)
    L.chimney(bm, 3.8, 1.2, 5.1, 6.90, R_BRD, m_cap=R_CON)
    return bm


def silo(lod=0):
    """7 x 7 m, 16 m tall: a corrugated grain silo with a conical roof and a ladder."""
    bm = bmesh.new()
    r, h = 3.5, 16.0
    seg = 16 if lod == 0 else (12 if lod == 1 else 8)
    L.cyl(bm, r + 0.35, r + 0.35, 0.45, seg, R_CON, base_at=(0, 0, 0.0))
    L.cyl(bm, r, r, h - 3.4, seg, R_MET, base_at=(0, 0, 0.45))
    L.cyl(bm, r * 1.03, 0.45, 2.6, seg, R_MET, base_at=(0, 0, h - 2.95))
    L.cyl(bm, 0.50, 0.50, 0.9, 8, R_MET, base_at=(0, 0, h - 0.35))
    if lod >= 2:
        return bm
    for k in (0.3, 0.55, 0.8):                                            # banding rings
        L.cyl(bm, r + 0.09, r + 0.09, 0.16, seg, R_MET, base_at=(0, 0, 0.45 + (h - 3.9) * k))
    L.box(bm, (0.10, 0.52, h - 2.0), base_at=(r + 0.30, 0, 0.45), m=R_TRIM)   # ladder stringers
    if lod == 0:
        for i in range(12):
            L.box(bm, (0.42, 0.06, 0.06), loc=(r + 0.30, 0, 1.0 + i * 1.1), m=R_TRIM)
    L.box(bm, (1.20, 1.20, 1.60), base_at=(0, -r - 0.4, 0.45), m=R_MET)       # discharge hopper
    return bm


def barn_big(lod=0):
    """The one 64 x 30 rural record: a long machinery barn, open bays on the front."""
    bm = bmesh.new()
    w, d, eave, ridge = 64.0, 30.0, 7.20, 10.50
    L.box(bm, (w, d, 0.60), base_at=(0, 0, 0.0), m=R_CON)
    L.wall(bm, "X", d / 2.0, -w / 2.0, w / 2.0, 0.60, eave, 0.32, R_BRD,
           L.win_row(-32, 32, 5, 1.30, 4.6, 1.20), R_GLA, sill=True, m_sill=R_TRIM, inward=-1)
    for sx in (-1, 1):
        L.wall(bm, "Y", sx * w / 2.0, -d / 2.0, d / 2.0, 0.60, eave, 0.32, R_BRD, [], None,
               inward=-sx)
    bays = 5 if lod < 2 else 2
    for i in range(bays + 1):                                               # front posts, bays open
        L.box(bm, (0.70, 0.70, eave - 0.60), base_at=(-w / 2.0 + w * i / bays, -d / 2.0 + 0.5, 0.60),
              m=R_BRD)
    L.box(bm, (w, 0.80, 0.90), base_at=(0, -d / 2.0 + 0.5, eave - 0.90), m=R_BRD)
    L.gable_roof(bm, w, d, eave, ridge, R_ROOF, "X", ov=0.80)
    if L.DETAIL < 2:
        for i in range(3):
            L.box(bm, (2.2, 1.6, 0.8), base_at=(-18.0 + i * 18.0, 0.0, ridge - 0.45), m=R_MET)
    return bm


def mill(lod=0):
    """The one 32 x 18 `mill` record: a working block with a taller head house."""
    bm = bmesh.new()
    w, d, eave, ridge = 32.0, 18.0, 7.40, 11.00
    box_shell(bm, w, d, 0.0, eave, R_BRD,
              front=[(-10.0, 0.0, 3.60, 3.60), (-2.0, GS, 1.20, GH), (2.0, GS, 1.20, GH),
                     (8.0, GS, 1.20, GH), (-6.0, 4.4, 1.10, 1.15), (0.0, 4.4, 1.10, 1.15),
                     (6.0, 4.4, 1.10, 1.15)],
              back=L.win_row(-16, 16, 4, 1.20, GS, GH) + L.win_row(-16, 16, 4, 1.10, 4.4, 1.15),
              left=L.win_row(-9, 9, 2, 1.10, GS, GH), right=L.win_row(-9, 9, 2, 1.10, GS, GH),
              glass=R_GLA, sill=True, m_sill=R_TRIM)
    if L.DETAIL < 2:
        L.box(bm, (3.60, 0.16, 3.60), loc=(-10.0, -d / 2.0 + 0.18, 1.80), m=R_MET)
    L.box(bm, (7.0, 7.0, 10.4), base_at=(10.0, 1.0, 0.0), m=R_BRD)             # head house
    L.gable_roof(bm, w, d, eave, ridge, R_ROOF, "X", ov=0.55)
    before = set(bm.faces)
    L.gable_roof(bm, 7.6, 7.6, 10.4, 11.0, R_ROOF, "Y", ov=0.30)
    bmesh.ops.transform(bm, matrix=Matrix.Translation(Vector((10.0, 1.0, 0.0))),
                        verts=list({v for f in bm.faces if f not in before for v in f.verts}))
    return bm


# ------------------------------------------------------------------ D7 Aviation

def hangar_arch(lod=0):
    """62 x 74 hangar: a clear-span arched roof over a full-width door front."""
    bm = bmesh.new()
    w, d, h = 62.0, 74.0, 13.0
    L.box(bm, (w, d, 1.00), base_at=(0, 0, 0.0), m=V_CON)
    for sy in (-1, 1):
        if sy == 1:
            L.wall(bm, "X", d / 2.0, -w / 2.0, w / 2.0, 1.0, h, 0.45, V_CLAD,
                   L.win_row(-30, 30, 5, 2.20, 7.0, 1.60), V_GLA, inward=-1)
    for sx in (-1, 1):
        L.wall(bm, "Y", sx * w / 2.0, -d / 2.0, d / 2.0, 1.0, h, 0.45, V_CLAD, [], None, inward=-sx)
    L.box(bm, (w - 3.0, 0.50, h - 1.6), loc=(0, -d / 2.0 + 0.30, 1.0 + (h - 1.6) / 2.0), m=V_MET)
    if L.DETAIL < 2:
        for i in range(1, 6):                                               # door leaf joints
            L.box(bm, (0.25, 0.70, h - 2.0), base_at=(-w / 2.0 + w * i / 6.0, -d / 2.0 + 0.30, 1.0),
                  m=V_MET)
    hd = d / 2.0
    rise = 5.5
    seg = 8 if lod == 0 else (6 if lod == 1 else 3)
    pts = [(-hd, h - 0.3)]
    for i in range(seg + 1):
        t = i / seg
        pts.append((-hd + d * t, h + rise * math.sin(math.pi * t)))
    pts.append((hd, h - 0.3))
    L.profile_solid(bm, pts, -w / 2.0, w / 2.0, V_ROOF, smooth=lod == 0)
    return bm


def hangar_monitor(lod=0):
    """62 x 74 hangar: low-pitch gable with a raised monitor and rooftop plant."""
    bm = bmesh.new()
    w, d, h = 62.0, 74.0, 13.5
    L.box(bm, (w, d, 1.00), base_at=(0, 0, 0.0), m=V_CON)
    L.wall(bm, "X", d / 2.0, -w / 2.0, w / 2.0, 1.0, h, 0.45, V_CLAD,
           L.win_row(-30, 30, 5, 2.20, 7.5, 1.60), V_GLA, inward=-1)
    for sx in (-1, 1):
        L.wall(bm, "Y", sx * w / 2.0, -d / 2.0, d / 2.0, 1.0, h, 0.45, V_CLAD,
               L.win_row(-36, 36, 4, 2.00, 7.5, 1.60), V_GLA, inward=-sx)
    L.box(bm, (w - 3.0, 0.50, h - 1.8), loc=(0, -d / 2.0 + 0.30, 1.0 + (h - 1.8) / 2.0), m=V_MET)
    hd = d / 2.0
    mon = d * 0.18
    pts = [(-hd, h - 0.3), (-hd, h), (-mon, h + 2.6), (-mon, h + 4.2), (mon, h + 4.2),
           (mon, h + 2.6), (hd, h), (hd, h - 0.3)]
    L.profile_solid(bm, pts, -w / 2.0, w / 2.0, V_ROOF)
    if L.DETAIL < 2:
        for sy in (-1, 1):
            L.box(bm, (w - 2.0, 0.16, 1.30), loc=(0, sy * mon, h + 3.4), m=V_GLA)
        for i in range(3):
            L.box(bm, (5.0, 4.0, 1.60), base_at=(-16.0 + i * 16.0, -d * 0.26, h + 1.3), m=V_MET)
    return bm


def terminal(lod=0):
    """The 130 x 36 `terminal` record: a glazed frontage under a flat roof, with a control block."""
    bm = bmesh.new()
    w, d, top = 130.0, 36.0, 9.60
    L.box(bm, (w, d, top), base_at=(0, 0.6, 0.0), m=V_CLAD)
    if L.DETAIL < 2:
        L.box(bm, (w - 6.0, 0.30, top - 2.2), loc=(0, -d / 2.0 + 0.25, 0.9 + (top - 2.2) / 2.0),
              m=V_GLA)
        for i in range(13):
            L.box(bm, (0.70, 0.90, top - 1.2), base_at=(-w / 2.0 + 4.0 + i * 9.0, -d / 2.0 + 0.35, 0.0),
                  m=V_MET)
        L.box(bm, (w + 2.0, 5.0, 0.55), base_at=(0, -d / 2.0 - 2.0, top - 1.6), m=V_MET)   # canopy
    L.box(bm, (w, d, 0.30), base_at=(0, 0.6, top), m=V_ROOF)
    L.parapet(bm, w, d, top + 0.30, V_CLAD, h=0.70, t=0.30, m_cap=V_CON)
    L.box(bm, (9.0, 9.0, 2.4), base_at=(w * 0.32, 4.0, top + 0.30), m=V_CLAD)              # control block
    L.box(bm, (10.4, 10.4, 1.5), base_at=(w * 0.32, 4.0, top + 2.70), m=V_GLA)
    L.box(bm, (11.0, 11.0, 0.35), base_at=(w * 0.32, 4.0, top + 4.20), m=V_ROOF)
    if L.DETAIL < 2:
        for i in range(4):
            L.box(bm, (4.0, 3.2, 1.40), base_at=(-w * 0.30 + i * 14.0, 6.0, top + 0.30), m=V_MET)
    if L.DETAIL == 0:
        for i in range(6):
            L.box(bm, (1.4, 1.2, 0.8), base_at=(-w * 0.40 + i * 11.0, -8.0, top + 0.30), m=V_MET)
    return bm


def grandstand(lod=0):
    """The 18 x 150 `grandstand` record: raked seating under a cantilever canopy."""
    bm = bmesh.new()
    w, d = 150.0, 18.0
    hd = d / 2.0
    pts = [(-hd, 0.0), (-hd, 1.2), (hd * 0.1, 7.2), (hd, 8.6), (hd, 0.0)]
    L.profile_solid(bm, pts, -w / 2.0, w / 2.0, V_CON)                        # raked bowl
    if L.DETAIL < 2:
        rows = 8 if L.DETAIL == 0 else 4
        for i in range(rows):
            t = i / float(rows)
            y = -hd * 0.9 + (hd * 1.8) * t
            z = 1.4 + 6.4 * t
            L.box(bm, (w - 2.0, 1.60 * (d / 18.0), 0.22), base_at=(0, y, z), m=V_MET)   # seat decks
    L.box(bm, (w, 1.2, 9.6), base_at=(0, hd - 0.6, 0.0), m=V_CLAD)            # rear wall
    if L.DETAIL < 2:
        for i in range(9):                                                     # canopy columns
            L.box(bm, (0.55, 0.55, 14.0), base_at=(-w / 2.0 + 3.0 + i * 18.0, hd - 1.6, 0.0), m=V_MET)
    L.box(bm, (w + 2.0, d * 0.85, 0.55), base_at=(0, hd - d * 0.45, 13.4), m=V_ROOF)
    L.box(bm, (w + 2.0, 0.50, 0.90), base_at=(0, hd - d * 0.85, 13.1), m=V_MET)
    return bm


# ------------------------------------------------------------------ D5 SuburbanRetail + Roadside

def retail_strip(lod=0):
    """40 x 30 retail: a glazed shopfront run under a deep canopy, parapet over."""
    bm = bmesh.new()
    w, d, top = 40.0, 30.0, 6.30
    L.box(bm, (w, d, top), base_at=(0, 0.5, 0.0), m=T_PAN)
    if L.DETAIL < 2:
        bays = 5
        for i in range(bays):
            cx = -w / 2.0 + w * (i + 0.5) / bays
            L.box(bm, (w / bays - 1.1, 0.26, 3.30), loc=(cx, -d / 2.0 + 0.20, 2.05), m=T_GLA)
            L.box(bm, (1.1, 0.50, top - 0.4), base_at=(-w / 2.0 + w * i / bays, -d / 2.0 + 0.25, 0.0),
                  m=T_BRK)
        L.box(bm, (1.1, 0.50, top - 0.4), base_at=(w / 2.0, -d / 2.0 + 0.25, 0.0), m=T_BRK)
        L.box(bm, (w + 1.0, 2.60, 0.40), base_at=(0, -d / 2.0 - 1.10, 3.90), m=T_MET)      # canopy
        L.box(bm, (w, 0.55, 1.10), base_at=(0, -d / 2.0 + 0.1, top - 1.10), m=T_BRK)        # fascia
    L.box(bm, (w, d, 0.22), base_at=(0, 0.5, top), m=T_CON)
    L.parapet(bm, w, d, top + 0.22, T_PAN, h=0.48, t=0.26, m_cap=T_CON)
    if L.DETAIL < 2:
        for i in range(3):
            L.box(bm, (3.2, 2.6, 1.20), base_at=(-10.0 + i * 10.0, 6.0, top + 0.22), m=T_MET)
    if L.DETAIL == 0:
        for i in range(4):
            L.box(bm, (1.1, 0.9, 0.7), base_at=(-14.0 + i * 9.0, -6.0, top + 0.22), m=T_MET)
    return bm


def retail_bigbox(lod=0):
    """120 x 60 big box: blank clad flanks, a glazed entrance bay, a big rooftop plant array."""
    bm = bmesh.new()
    w, d, top = 120.0, 60.0, 9.40
    L.box(bm, (w, d, top), base_at=(0, 0.5, 0.0), m=T_PAN)
    if L.DETAIL < 2:
        L.box(bm, (26.0, 0.40, 5.60), loc=(-8.0, -d / 2.0 + 0.25, 3.00), m=T_GLA)
        L.box(bm, (30.0, 3.40, 0.55), base_at=(-8.0, -d / 2.0 - 1.50, 5.90), m=T_MET)
        L.box(bm, (w, 0.60, 1.60), base_at=(0, -d / 2.0 + 0.1, top - 1.60), m=T_BRK)
        for i in range(6):                                                     # service doors
            L.box(bm, (3.6, 0.30, 4.20), base_at=(-w / 2.0 + 14.0 + i * 18.0, d / 2.0 - 0.15, 0.0),
                  m=T_MET)
    L.box(bm, (w, d, 0.26), base_at=(0, 0.5, top), m=T_CON)
    L.parapet(bm, w, d, top + 0.26, T_PAN, h=1.05, t=0.30, m_cap=T_CON)
    if L.DETAIL < 2:
        for i in range(4):
            for j in range(2):
                L.box(bm, (8.0, 7.0, 1.80), base_at=(-36.0 + i * 24.0, -8.0 + j * 18.0, top + 0.26),
                      m=T_MET)
    if L.DETAIL == 0:
        for i in range(8):
            L.cyl(bm, 0.70, 0.70, 1.10, 8, T_MET, base_at=(-46.0 + i * 13.0, 22.0, top + 0.26))
    return bm


def retail_small(lod=0):
    """24 x 14 unit: a single shopfront with a flat roof and a small plant screen."""
    bm = bmesh.new()
    # 6.2 m overall, not the record's 5.0: under 6 m the catalogue drops the entry as a "fragment"
    w, d, top = 24.0, 14.0, 5.00
    L.box(bm, (w, d, top), base_at=(0, 0.4, 0.0), m=T_BRK)
    if L.DETAIL < 2:
        L.box(bm, (w - 6.0, 0.24, 2.70), loc=(0, -d / 2.0 + 0.18, 1.75), m=T_GLA)
        L.box(bm, (w - 4.0, 1.40, 0.30), base_at=(0, -d / 2.0 - 0.55, 3.10), m=T_MET)
        L.box(bm, (w, 0.45, 0.90), base_at=(0, -d / 2.0 + 0.1, top - 0.90), m=T_PAN)
    L.box(bm, (w, d, 0.20), base_at=(0, 0.4, top), m=T_CON)
    L.parapet(bm, w, d, top + 0.20, T_PAN, h=0.40, t=0.24, m_cap=T_CON)
    if L.DETAIL < 2:
        L.box(bm, (3.0, 2.4, 1.00), base_at=(5.0, 3.0, top + 0.20), m=T_MET)
    if L.DETAIL == 0:
        L.box(bm, (1.2, 1.0, 0.8), base_at=(-6.0, 2.0, top + 0.20), m=T_MET)
        L.box(bm, (w - 8.0, 0.14, 0.42), loc=(0, -d / 2.0 - 0.02, top - 1.60), m=T_MET)
    return bm


def gas_station(lod=0):
    """The one Roadside record, 26 x 16: a kiosk with a forecourt canopy on columns."""
    bm = bmesh.new()
    w, d = 26.0, 16.0
    kw, kd, ktop = 11.0, 7.0, 4.20
    L.box(bm, (w, d, 0.14), base_at=(0, 0, 0.0), m=T_CON)                      # forecourt slab
    L.box(bm, (kw, kd, ktop), base_at=(w / 2.0 - kw / 2.0 - 0.5, d / 2.0 - kd / 2.0 - 0.5, 0.14),
          m=T_PAN)
    if L.DETAIL < 2:
        L.box(bm, (kw - 2.4, 0.22, 2.60), loc=(w / 2.0 - kw / 2.0 - 0.5, d / 2.0 - kd - 0.38, 1.74),
              m=T_GLA)
        L.box(bm, (kw + 0.4, kd + 0.4, 0.55), base_at=(w / 2.0 - kw / 2.0 - 0.5, d / 2.0 - kd / 2.0 - 0.5,
                                                       ktop + 0.14), m=T_MET)
    for sx in (-1, 1):                                                          # canopy columns
        for sy in (-1, 1):
            L.box(bm, (0.70, 0.70, 5.20), base_at=(-w / 2.0 + 5.0 + sx * 3.5 + 1.5,
                                                   -d / 2.0 + 4.2 + sy * 2.6, 0.14), m=T_MET)
    L.box(bm, (17.0, 11.0, 0.95), base_at=(-w / 2.0 + 8.0, -d / 2.0 + 4.2, 5.34), m=T_MET)
    L.box(bm, (17.4, 11.4, 0.35), base_at=(-w / 2.0 + 8.0, -d / 2.0 + 4.2, 6.29), m=T_PAN)
    if L.DETAIL < 2:
        for i in range(2):                                                      # pump islands
            L.box(bm, (3.6, 1.10, 0.30), base_at=(-w / 2.0 + 4.0 + i * 8.0, -d / 2.0 + 4.2, 0.14),
                  m=T_CON)
            L.box(bm, (0.80, 0.60, 1.60), base_at=(-w / 2.0 + 4.0 + i * 8.0, -d / 2.0 + 4.2, 0.44),
                  m=T_MET)
    if L.DETAIL == 0:
        for i in range(2):                                                      # pump hose columns
            L.cyl(bm, 0.16, 0.16, 1.10, 6, T_MET,
                  base_at=(-w / 2.0 + 4.0 + i * 8.0 - 1.3, -d / 2.0 + 4.2, 0.44))
    return bm


SPECS = [
    ("chalet_a", chalet_a, 12.0, 10.0, 8.0, ALP, "alpine", ["Alpine"], ["D4"]),
    ("chalet_b", chalet_b, 12.0, 10.0, 8.0, ALP, "alpine", ["Alpine"], ["D4"]),
    ("chalet_c", chalet_c, 12.0, 10.0, 8.0, ALP, "alpine", ["Alpine"], ["D4"]),
    ("workshop", workshop_alpine, 42.0, 24.0, 8.5, ALP, "alpine", ["Alpine"], ["D4"]),
    ("barn_gambrel", barn_gambrel, 26.0, 14.0, 9.0, RUR, "rural", ["Rural"], ["D8"]),
    ("barn_leanto", barn_leanto, 26.0, 14.0, 8.4, RUR, "rural", ["Rural"], ["D8"]),
    ("farmhouse_a", farmhouse_a, 12.0, 10.0, 7.0, RUR, "rural", ["Rural"], ["D8"]),
    ("farmhouse_b", farmhouse_b, 12.0, 10.0, 7.0, RUR, "rural", ["Rural"], ["D8"]),
    ("silo", silo, 7.0, 7.0, 16.0, RUR, "rural", ["Rural"], ["D8"]),
    ("barn_big", barn_big, 64.0, 30.0, 10.5, RUR, "rural", ["Rural"], ["D8"]),
    ("mill", mill, 32.0, 18.0, 11.0, RUR, "rural", ["Rural"], ["D8"]),
    ("hangar_arch", hangar_arch, 62.0, 74.0, 18.5, AVI, "aviation", ["Aviation"], ["D7"]),
    ("hangar_monitor", hangar_monitor, 62.0, 74.0, 17.7, AVI, "aviation", ["Aviation"], ["D7"]),
    ("terminal", terminal, 130.0, 36.0, 14.0, AVI, "aviation", ["Aviation"], ["D7"]),
    ("grandstand", grandstand, 18.0, 150.0, 14.0, AVI, "aviation", ["Aviation"], ["D7"]),
    ("retail_strip", retail_strip, 40.0, 30.0, 7.0, RET, "retail", ["SuburbanRetail"], ["D5"]),
    ("retail_bigbox", retail_bigbox, 120.0, 60.0, 10.7, RET, "retail", ["SuburbanRetail"], ["D5"]),
    ("retail_small", retail_small, 24.0, 14.0, 6.2, RET, "retail", ["SuburbanRetail"], ["D5"]),
    ("gas_station", gas_station, 26.0, 16.0, 6.6, RET, "roadside", ["Roadside"], ["D4"]),
]
PREFIX = {"alpine": "VC_Alp_", "rural": "VC_Rur_", "aviation": "VC_Avi_", "retail": "VC_Ret_",
          "roadside": "VC_Ret_"}
NAMES = {s[0]: PREFIX[s[6]] + "".join(p.capitalize() for p in s[0].split("_")) for s in SPECS}


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
        uv = L.unwrap(o, margin=0.006, cube=2.0) and uv
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
        "key": key, "name": parent.name, "set": group, "family": key,
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
    for group in ("alpine", "rural", "aviation", "retail", "roadside"):
        rows = [r for r in out if r["set"] == group]
        full = os.path.join(HERE, "report_%s.json" % group)
        old = []
        if os.path.exists(full) and args[0] != "all":
            old = [r for r in json.load(open(full)) if r["key"] not in keys]
        if rows or old:
            with open(full, "w", encoding="utf-8") as fh:
                json.dump(old + rows, fh, indent=1)
    print("VC_RURALAIR_OK")


main()

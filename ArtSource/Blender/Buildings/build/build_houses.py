"""VEYRA CITY - D5 suburban house set (original designs), 14 x 11 m footprint, ~7.2 m tall.

    blender --factory-startup -b -noaudio -P build_houses.py -- <key|all>

Why this footprint: 1,923 of the 2,708 records in Assets/_Veyra/Data/world_building_placements.csv are D5
style_kit "Suburban", every one of them 14.0 x 11.0 m with a height between 6.2 and 8.2 m. They are the
"identical box houses in rows" of BUG-004 and the biggest single block of buildings in the city.

Eight distinct designs so BuildingAssembler (which never repeats a model within two records of the same
chunk) can build a street that does not read as one house copied 1,923 times.
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
W, D = 14.0, 11.0
FY, BY = -D / 2.0, D / 2.0           # front (-Y) and back (+Y) outer faces
LX, RX = -W / 2.0, W / 2.0
T = 0.30                             # wall thickness

# material slots, same order in every house
SLOTS_STUCCO = ["VC_Wall_Stucco", "VC_Roof_Tile", "VC_Trim_Paint", "VC_Glass_Window",
                "VC_Door_Wood", "VC_Wall_Concrete"]
SLOTS_WARM = ["VC_Wall_StuccoWarm", "VC_Roof_Tile", "VC_Trim_Paint", "VC_Glass_Window",
              "VC_Door_Wood", "VC_Wall_Concrete"]
SLOTS_BRICK = ["VC_Wall_Brick", "VC_Roof_Shingle", "VC_Trim_Paint", "VC_Glass_Window",
               "VC_Door_Wood", "VC_Wall_Concrete"]
SLOTS_BOARD = ["VC_Wall_Board", "VC_Roof_Shingle", "VC_Trim_Paint", "VC_Glass_Window",
               "VC_Door_Wood", "VC_Wall_Concrete"]
WALL, ROOF, TRIM, GLASS, DOOR, CONC = 0, 1, 2, 3, 4, 5
GAR = 6

G_SILL, G_H = 0.95, 1.40             # ground-floor window sill / height
U_SILL, U_H = 3.70, 1.30             # upper-floor window sill / height


def shell(bm, z0, z1, front_wins=(), back_wins=(), left_wins=(), right_wins=(), sill=True,
          x0=LX, x1=RX, y0=FY, y1=BY):
    """The four outer walls of a rectangular block."""
    ms = TRIM if sill else None
    L.wall(bm, "X", y0, x0, x1, z0, z1, T, WALL, front_wins, GLASS, sill=sill, m_sill=ms, inward=1)
    L.wall(bm, "X", y1, x0, x1, z0, z1, T, WALL, back_wins, GLASS, sill=sill, m_sill=ms, inward=-1)
    L.wall(bm, "Y", x0, y0, y1, z0, z1, T, WALL, left_wins, GLASS, sill=sill, m_sill=ms, inward=1)
    L.wall(bm, "Y", x1, y0, y1, z0, z1, T, WALL, right_wins, GLASS, sill=sill, m_sill=ms, inward=-1)


# ------------------------------------------------------------------ the eight designs

def h1_gable_porch():
    """Two storey, gable ridge across the frontage, deep covered porch over the entry."""
    bm = bmesh.new()
    eave, ridge = 5.40, 7.20
    front = [(1.2, G_SILL, 1.20, G_H), (4.2, G_SILL, 1.20, G_H)]
    shell(bm, 0.0, eave,
          front_wins=front + [(-3.40, 0.0, 2.80, 2.75)] + L.win_row(-6.2, 6.2, 3, 1.10, U_SILL, U_H),
          back_wins=L.win_row(LX, RX, 3, 1.10, G_SILL, G_H) + L.win_row(LX, RX, 3, 1.10, U_SILL, U_H),
          left_wins=L.win_row(FY, BY, 2, 1.00, G_SILL, G_H) + L.win_row(FY, BY, 2, 1.00, U_SILL, U_H),
          right_wins=L.win_row(FY, BY, 2, 1.00, G_SILL, G_H) + L.win_row(FY, BY, 2, 1.00, U_SILL, U_H))
    L.entry_recess(bm, -4.80, -2.00, FY, 1.50, 0.0, 2.75, WALL, TRIM, CONC)
    L.wall(bm, "X", FY + 1.50, -4.80, -2.00, 0.0, 2.75, T, WALL,
           [L.door_op(-3.40, 1.05, 2.15)], None, inward=1)
    L.door(bm, "X", FY + 1.50, -3.40, 0.0, 1.05, 2.15, T, DOOR, TRIM, inward=1)
    L.steps(bm, -3.40, FY, 1.60, 0.18, CONC, n=2)
    L.gable_roof(bm, W, D, eave, ridge, ROOF, "X", ov=0.50)
    L.fascia(bm, W, D, eave, TRIM, ov=0.50)
    L.chimney(bm, 5.20, 2.00, 4.60, 7.15, WALL, m_cap=CONC)
    return L.new_object("VC_House_GablePorch", bm, SLOTS_STUCCO), 7.20


def h2_hip_bay():
    """Hipped roof, brick, projecting bay window on the frontage."""
    bm = bmesh.new()
    eave, ridge = 5.50, 7.20
    shell(bm, 0.0, eave,
          front_wins=[(3.60, G_SILL, 1.30, G_H), L.door_op(0.90, 1.05, 2.15)]
          + L.win_row(-6.2, 6.2, 3, 1.10, U_SILL, U_H),
          back_wins=L.win_row(LX, RX, 3, 1.20, G_SILL, G_H) + L.win_row(LX, RX, 3, 1.10, U_SILL, U_H),
          left_wins=L.win_row(FY, BY, 2, 1.00, G_SILL, G_H) + L.win_row(FY, BY, 2, 1.00, U_SILL, U_H),
          right_wins=L.win_row(FY, BY, 2, 1.00, G_SILL, G_H) + L.win_row(FY, BY, 2, 1.00, U_SILL, U_H))
    # projecting bay: a shallow box with glass on three sides
    bx, bw, bd = -3.00, 3.00, 0.72
    L.box(bm, (bw, bd, 2.60), base_at=(bx, FY - bd / 2.0, 0.0), m=WALL)
    L.box(bm, (bw - 0.50, 0.06, 1.45), loc=(bx, FY - bd + 0.05, 1.70), m=GLASS)
    L.box(bm, (0.06, bd - 0.30, 1.45), loc=(bx - bw / 2.0 + 0.06, FY - bd / 2.0, 1.70), m=GLASS)
    L.box(bm, (0.06, bd - 0.30, 1.45), loc=(bx + bw / 2.0 - 0.06, FY - bd / 2.0, 1.70), m=GLASS)
    L.box(bm, (bw + 0.26, bd + 0.26, 0.16), base_at=(bx, FY - bd / 2.0, 2.60), m=TRIM)
    L.door(bm, "X", FY, 0.90, 0.0, 1.05, 2.15, T, DOOR, TRIM)
    L.box(bm, (2.00, 1.00, 0.14), base_at=(0.90, FY - 0.50, 2.45), m=TRIM)     # entry canopy
    L.steps(bm, 0.90, FY, 1.60, 0.18, CONC, n=2)
    L.hip_roof(bm, W, D, eave, ridge, ROOF, ov=0.45, ridge_frac=0.40)
    L.fascia(bm, W, D, eave, TRIM, ov=0.45)
    L.chimney(bm, -5.40, 1.20, 4.70, 7.15, WALL, m_cap=CONC)
    return L.new_object("VC_House_HipBay", bm, SLOTS_BRICK), 7.20


def h3_garage_front():
    """Cross-gable: living block on the left, forward garage wing on the right."""
    bm = bmesh.new()
    eave, ridge = 5.20, 7.20
    gx0, gy0 = 1.40, FY                 # garage wing occupies x 1.4..7.0, full depth
    # main block
    shell(bm, 0.0, eave, x0=LX, x1=gx0,
          front_wins=[(-5.10, G_SILL, 1.20, G_H), L.door_op(-2.40, 1.10, 2.20)]
          + L.win_row(LX, gx0, 2, 1.10, U_SILL, U_H),
          back_wins=L.win_row(LX, gx0, 2, 1.20, G_SILL, G_H) + L.win_row(LX, gx0, 2, 1.10, U_SILL, U_H),
          left_wins=L.win_row(FY, BY, 2, 1.00, G_SILL, G_H) + L.win_row(FY, BY, 2, 1.00, U_SILL, U_H),
          right_wins=[])
    L.door(bm, "X", FY, -2.40, 0.0, 1.10, 2.20, T, DOOR, TRIM)
    L.box(bm, (2.30, 1.20, 0.16), base_at=(-2.40, FY - 0.60, 2.55), m=TRIM)
    L.steps(bm, -2.40, FY, 1.70, 0.18, CONC, n=2)
    # garage wing, gable facing the street
    L.wall(bm, "X", FY, gx0, RX, 0.0, 4.40, T, WALL, [(4.20, 0.0, 3.40, 2.45)], None, inward=1)
    L.wall(bm, "X", BY, gx0, RX, 0.0, 4.40, T, WALL, [], None, inward=-1)
    L.wall(bm, "Y", RX, FY, BY, 0.0, 4.40, T, WALL,
           L.win_row(FY, BY, 2, 1.00, U_SILL, U_H), GLASS, sill=True, m_sill=TRIM, inward=-1)
    L.box(bm, (3.40, 0.14, 2.45), loc=(4.20, FY + 0.10, 1.26), m=GAR)           # garage door
    L.box(bm, (3.70, 0.22, 0.16), loc=(4.20, FY + 0.02, 2.58), m=TRIM)
    for gz in (0.72, 1.26, 1.80):
        L.box(bm, (3.30, 0.06, 0.05), loc=(4.20, FY + 0.04, gz), m=TRIM)   # garage door panel lines
    L.box(bm, (3.90, 0.45, 0.10), base_at=(4.20, FY - 0.22, 0.0), m=CONC)     # threshold only
    gw = RX - gx0
    pts_off = Matrix.Translation(Vector(((gx0 + RX) / 2.0, 0.0, 0.0)))
    before = set(bm.faces)
    L.gable_roof(bm, gw, D, 4.40, 6.10, ROOF, "Y", ov=0.45)
    bmesh.ops.transform(bm, matrix=pts_off,
                        verts=list({v for f in bm.faces if f not in before for v in f.verts}))
    # main roof
    before = set(bm.faces)
    L.gable_roof(bm, gx0 - LX, D, eave, ridge, ROOF, "X", ov=0.50)
    bmesh.ops.transform(bm, matrix=Matrix.Translation(Vector(((LX + gx0) / 2.0, 0.0, 0.0))),
                        verts=list({v for f in bm.faces if f not in before for v in f.verts}))
    return L.new_object("VC_House_GarageFront", bm, SLOTS_WARM + ["VC_Metal_Garage"]), 7.20


def h4_split_level():
    """Split level: a lower left block and a taller right block, both mono-pitch."""
    bm = bmesh.new()
    mid = -1.00
    lo_eave, lo_hi = 3.90, 4.80
    hi_eave, hi_hi = 5.60, 7.20
    shell(bm, 0.0, lo_eave, x0=LX, x1=mid,
          front_wins=[(-5.40, G_SILL, 1.60, G_H), (-2.60, G_SILL, 1.10, G_H)],
          back_wins=L.win_row(LX, mid, 2, 1.20, G_SILL, G_H),
          left_wins=L.win_row(FY, BY, 2, 1.10, G_SILL, G_H), right_wins=[])
    shell(bm, 0.0, hi_eave, x0=mid, x1=RX,
          front_wins=[(2.40, G_SILL, 1.20, G_H), L.door_op(5.20, 1.05, 2.15)]
          + L.win_row(mid, RX, 2, 1.10, U_SILL, U_H),
          back_wins=L.win_row(mid, RX, 2, 1.20, G_SILL, G_H) + L.win_row(mid, RX, 2, 1.10, U_SILL, U_H),
          left_wins=[], right_wins=L.win_row(FY, BY, 2, 1.00, G_SILL, G_H) + L.win_row(FY, BY, 2, 1.00, U_SILL, U_H))
    L.door(bm, "X", FY, 5.20, 0.0, 1.05, 2.15, T, DOOR, TRIM)
    L.box(bm, (2.10, 1.10, 0.15), base_at=(5.20, FY - 0.55, 2.50), m=TRIM)
    L.steps(bm, 5.20, FY, 1.60, 0.18, CONC, n=2)
    for (x0, x1, lo, hi) in ((LX, mid, lo_eave, lo_hi), (mid, RX, hi_eave, hi_hi)):
        before = set(bm.faces)
        L.shed_roof(bm, x1 - x0, D, lo, hi, ROOF, ov=0.40, low_side="-Y")
        bmesh.ops.transform(bm, matrix=Matrix.Translation(Vector(((x0 + x1) / 2.0, 0.0, 0.0))),
                            verts=list({v for f in bm.faces if f not in before for v in f.verts}))
    return L.new_object("VC_House_SplitLevel", bm, SLOTS_BOARD), 7.20


def h5_flat_parapet():
    """Flat roof behind a parapet, deep entry canopy, banded windows - the modern Veyra suburb."""
    bm = bmesh.new()
    top = 6.45
    shell(bm, 0.0, top,
          front_wins=[(-4.60, G_SILL, 2.40, G_H), (2.60, G_SILL, 1.10, G_H), L.door_op(0.20, 1.10, 2.25),
                      (-3.80, U_SILL, 3.20, U_H), (3.20, U_SILL, 1.90, U_H)],
          back_wins=[(-3.00, G_SILL, 2.60, G_H), (3.40, G_SILL, 1.60, G_H),
                     (-3.00, U_SILL, 2.60, U_H), (3.40, U_SILL, 1.60, U_H)],
          left_wins=[(-1.20, G_SILL, 1.40, G_H), (-1.20, U_SILL, 1.40, U_H)],
          right_wins=[(0.60, G_SILL, 1.40, G_H), (0.60, U_SILL, 1.40, U_H)], sill=False)
    L.door(bm, "X", FY, 0.20, 0.0, 1.10, 2.25, T, DOOR, TRIM)
    L.box(bm, (4.40, 1.60, 0.22), base_at=(0.70, FY - 0.80, 2.60), m=CONC)      # cantilever canopy
    L.box(bm, (0.16, 0.55, 2.60), base_at=(2.70, FY - 0.28, 0.0), m=TRIM)      # canopy fin
    L.steps(bm, 0.20, FY, 1.80, 0.18, CONC, n=2)
    L.box(bm, (W, D, 0.14), base_at=(0, 0, top), m=CONC)                        # roof deck
    L.parapet(bm, W, D, top + 0.14, WALL, h=0.46, t=0.26, m_cap=CONC)
    L.box(bm, (1.60, 1.20, 0.55), base_at=(-3.60, 2.40, top + 0.14), m=TRIM)    # roof plant
    return L.new_object("VC_House_FlatParapet", bm, SLOTS_STUCCO), 7.20


def h6_dormer_cottage():
    """Steep gable with two front dormers - the older end of the suburb."""
    bm = bmesh.new()
    eave, ridge = 4.10, 7.20
    shell(bm, 0.0, eave,
          front_wins=[(-4.60, G_SILL, 1.30, G_H), (-1.60, G_SILL, 1.30, G_H), (4.40, G_SILL, 1.30, G_H),
                      (1.50, 0.0, 2.60, 2.60)],
          back_wins=L.win_row(LX, RX, 3, 1.20, G_SILL, G_H),
          left_wins=L.win_row(FY, BY, 2, 1.10, G_SILL, G_H),
          right_wins=L.win_row(FY, BY, 2, 1.10, G_SILL, G_H))
    L.entry_recess(bm, 0.20, 2.80, FY, 1.30, 0.0, 2.60, WALL, TRIM, CONC)
    L.wall(bm, "X", FY + 1.30, 0.20, 2.80, 0.0, 2.60, T, WALL,
           [L.door_op(1.50, 1.10, 2.20)], None, inward=1)
    L.door(bm, "X", FY + 1.30, 1.50, 0.0, 1.10, 2.20, T, DOOR, TRIM, inward=1)
    L.steps(bm, 1.50, FY, 1.50, 0.18, CONC, n=2)
    L.gable_roof(bm, W, D, eave, ridge, ROOF, "X", ov=0.55)
    L.fascia(bm, W, D, eave, TRIM, ov=0.55)
    L.dormer(bm, -3.40, -4.60, 4.20, 1.90, 1.60, 1.25, WALL, ROOF, GLASS, TRIM)
    L.dormer(bm, 3.40, -4.60, 4.20, 1.90, 1.60, 1.25, WALL, ROOF, GLASS, TRIM)
    L.chimney(bm, -6.00, 1.60, 5.20, 7.15, WALL, m_cap=CONC)
    return L.new_object("VC_House_DormerCottage", bm, SLOTS_WARM), 7.20


def h7_duplex():
    """Two mirrored halves, each with its own entry and gable facing the street."""
    bm = bmesh.new()
    eave, ridge = 5.00, 7.20
    shell(bm, 0.0, eave,
          front_wins=[(-5.60, G_SILL, 1.20, G_H), (-2.10, G_SILL, 1.20, G_H),
                      (2.10, G_SILL, 1.20, G_H), (5.60, G_SILL, 1.20, G_H),
                      L.door_op(-3.85, 1.05, 2.15), L.door_op(3.85, 1.05, 2.15),
                      (-5.20, U_SILL, 1.10, U_H), (-2.40, U_SILL, 1.10, U_H),
                      (2.40, U_SILL, 1.10, U_H), (5.20, U_SILL, 1.10, U_H)],
          back_wins=L.win_row(LX, RX, 4, 1.10, G_SILL, G_H) + L.win_row(LX, RX, 4, 1.10, U_SILL, U_H),
          left_wins=L.win_row(FY, BY, 2, 1.00, U_SILL, U_H),
          right_wins=L.win_row(FY, BY, 2, 1.00, U_SILL, U_H))
    for sx in (-1.0, 1.0):
        L.door(bm, "X", FY, sx * 3.85, 0.0, 1.05, 2.15, T, DOOR, TRIM)
        L.box(bm, (1.90, 0.95, 0.15), base_at=(sx * 3.85, FY - 0.48, 2.45), m=TRIM)
        L.steps(bm, sx * 3.85, FY, 1.50, 0.18, CONC, n=2)
        before = set(bm.faces)
        L.gable_roof(bm, W / 2.0, D, eave, ridge, ROOF, "Y", ov=0.40)
        bmesh.ops.transform(bm, matrix=Matrix.Translation(Vector((sx * W / 4.0, 0.0, 0.0))),
                            verts=list({v for f in bm.faces if f not in before for v in f.verts}))
    # party wall between the two halves: it also closes the junction of the two gable solids
    L.box(bm, (0.50, D + 0.9, ridge + 0.10 - (eave - 0.60)), base_at=(0.0, 0.0, eave - 0.60), m=WALL)
    return L.new_object("VC_House_Duplex", bm, SLOTS_BRICK), 7.20


def h8_ranch_carport():
    """Single storey, shallow wide gable, open carport bay on the left."""
    bm = bmesh.new()
    eave, ridge = 4.35, 7.05
    cx = -2.60                         # carport occupies x LX..cx
    shell(bm, 0.0, eave, x0=cx, x1=RX,
          front_wins=[(0.40, G_SILL, 2.80, G_H), (5.10, G_SILL, 1.40, G_H),
                      L.door_op(-1.70, 1.10, 2.20)],
          back_wins=L.win_row(cx, RX, 3, 1.30, G_SILL, G_H),
          left_wins=[], right_wins=L.win_row(FY, BY, 2, 1.10, G_SILL, G_H))
    L.wall(bm, "Y", cx, FY, BY, 0.0, eave, T, WALL, [], None, inward=1)
    L.door(bm, "X", FY, -1.70, 0.0, 1.10, 2.20, T, DOOR, TRIM)
    L.steps(bm, -1.70, FY, 1.60, 0.18, CONC, n=2)
    L.box(bm, (cx - LX, D, 0.12), base_at=((LX + cx) / 2.0, 0.0, 0.0), m=CONC)   # carport slab
    for (px, py) in ((LX + 0.35, FY + 0.40), (LX + 0.35, BY - 0.40)):
        L.box(bm, (0.22, 0.22, eave), base_at=(px, py, 0.12), m=TRIM)
    L.gable_roof(bm, W, D, eave, ridge, ROOF, "X", ov=0.70)
    L.fascia(bm, W, D, eave, TRIM, ov=0.70)
    # the ranch is low; a rear utility block brings it to the kit height
    L.dormer(bm, 2.60, -4.70, 4.45, 2.10, 1.55, 1.25, WALL, ROOF, GLASS, TRIM)
    L.chimney(bm, 5.60, 1.80, 5.00, 7.00, WALL, m_cap=CONC)
    return L.new_object("VC_House_RanchCarport", bm, SLOTS_BOARD), 7.05


def h9_garage_small():
    """D5 `garage_small`, 8 x 7 m: a single-bay detached garage with a roller door and a low-pitch roof."""
    bm = bmesh.new()
    # 8.6 x 7.5 x 6.10, not the record's 8 x 7 x 4.0: KitbashCatalogBuilder excludes any entry under 6 m
    # as a "fragment", so a 4 m garage would never be placed. The larger plan keeps the fit scale below 1
    # (s = 0.93), which brings the height ratio to 1.42 - inside the assembler's 0.65-1.5 window.
    w, d = 8.6, 7.5
    fy, by, lx, rx = -d / 2.0, d / 2.0, -w / 2.0, w / 2.0
    eave, ridge = 3.95, 6.10
    L.wall(bm, "X", fy, lx, rx, 0.0, eave, 0.26, WALL, [(0.0, 0.0, 4.20, 2.35)], None, inward=1)
    L.wall(bm, "X", by, lx, rx, 0.0, eave, 0.26, WALL, [(1.6, G_SILL, 0.90, 0.90)], GLASS,
           sill=True, m_sill=TRIM, inward=-1)
    L.wall(bm, "Y", lx, fy, by, 0.0, eave, 0.26, WALL, [], None, inward=1)
    L.wall(bm, "Y", rx, fy, by, 0.0, eave, 0.26, WALL, [(0.0, G_SILL, 0.90, 0.90)], GLASS,
           sill=True, m_sill=TRIM, inward=-1)
    L.box(bm, (4.20, 0.14, 2.35), loc=(0.0, fy + 0.12, 1.175), m=GAR)
    L.box(bm, (4.50, 0.22, 0.16), loc=(0.0, fy + 0.02, 2.46), m=TRIM)
    if L.DETAIL == 0:
        for gz in (0.62, 1.14, 1.66):
            L.box(bm, (4.00, 0.06, 0.05), loc=(0.0, fy + 0.04, gz), m=TRIM)
    L.wall(bm, "X", by, lx, rx, eave - 1.1, eave, 0.26, WALL, [], None, inward=-1)
    L.gable_roof(bm, w, d, eave, ridge, ROOF, "X", ov=0.35)
    L.fascia(bm, w, d, eave, TRIM, ov=0.35)
    return L.new_object("VC_House_GarageSmall", bm, SLOTS_BOARD + ["VC_Metal_Garage"]), 6.10


def h10_garage_community():
    """D5 `garage_community`, 40 x 22 m: a long block of lock-up bays behind a flat roof and parapet."""
    bm = bmesh.new()
    w, d = 40.0, 22.0
    fy, by, lx, rx = -d / 2.0, d / 2.0, -w / 2.0, w / 2.0
    top = 7.00 - 0.55
    bays = 8
    span = w / bays
    L.box(bm, (w, d, top), base_at=(0, 0.0, 0.0), m=WALL)
    for i in range(bays):                                  # roller-door bays along the street side
        cx = lx + span * (i + 0.5)
        L.box(bm, (span - 0.75, 0.16, 2.60), loc=(cx, fy + 0.10, 1.30), m=GAR)
        L.box(bm, (span - 0.45, 0.26, 0.20), loc=(cx, fy + 0.02, 2.72), m=TRIM)
        if L.DETAIL == 0:
            for gz in (0.70, 1.30, 1.90):
                L.box(bm, (span - 1.15, 0.06, 0.05), loc=(cx, fy + 0.02, gz), m=TRIM)
        L.box(bm, (0.75, 0.30, top), base_at=(lx + span * i, fy + 0.15, 0.0), m=WALL)
    L.box(bm, (0.75, 0.30, top), base_at=(rx, fy + 0.15, 0.0), m=WALL)
    L.wall(bm, "X", by, lx, rx, 0.0, top, 0.30, WALL,
           L.win_row(lx, rx, 6, 1.10, 4.20, 1.20), GLASS, sill=True, m_sill=TRIM, inward=-1)
    L.box(bm, (w, d, 0.16), base_at=(0, 0, top), m=CONC)
    L.parapet(bm, w, d, top + 0.16, WALL, h=0.39, t=0.26, m_cap=CONC)
    return L.new_object("VC_House_GarageCommunity", bm, SLOTS_BOARD + ["VC_Metal_Garage"]), 7.00


#: footprint the manifest reports per model (the records these are matched against)
FOOTPRINTS = {"garage_small": (8.6, 7.5), "garage_community": (40.0, 22.0)}

HOUSES = {
    "gable_porch": h1_gable_porch,
    "hip_bay": h2_hip_bay,
    "garage_front": h3_garage_front,
    "split_level": h4_split_level,
    "flat_parapet": h5_flat_parapet,
    "dormer_cottage": h6_dormer_cottage,
    "duplex": h7_duplex,
    "ranch_carport": h8_ranch_carport,
    "garage_small": h9_garage_small,
    "garage_community": h10_garage_community,
}


# ------------------------------------------------------------------ pipeline

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
    reset()
    # LODs are AUTHORED, not decimated: the same house description is evaluated at three detail levels
    # (vc_bld_lib.set_detail). A collapse decimate shreds this kind of box-assembled geometry - the LOD2
    # was a cloud of slivers with no silhouette left (preview/lod2_backface_check_houses.png before this).
    lods = []
    uv = True
    name = None
    for lod in (0, 1, 2):
        L.set_detail(lod)
        obj, want_h = HOUSES[key]()
        name = name or obj.name
        obj.name = "%s_LOD%d" % (name, lod)
        uv = L.unwrap(obj) and uv
        lods.append(obj)
    L.set_detail(0)
    obj = lods[0]
    pts = [obj.matrix_world @ Vector(c) for c in obj.bound_box]
    lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    rep0 = L.mesh_report(obj)
    parent = bpy.data.objects.new(name, None)
    bpy.context.scene.collection.objects.link(parent)
    for o in lods:
        o.parent = parent
    col = L.collider_box("UBX_" + parent.name, (hi.x - lo.x, hi.y - lo.y, hi.z - lo.z),
                         base_at=((lo.x + hi.x) / 2.0, (lo.y + hi.y) / 2.0, 0.0))
    col.parent = parent
    col.matrix_parent_inverse = parent.matrix_world.inverted()

    os.makedirs(EXPORT, exist_ok=True)
    fbx = os.path.join(EXPORT, parent.name + ".fbx")
    vc_tools.vc_export_fbx([parent], fbx, mode="prop", pivot=parent, path_mode="STRIP")
    blend = os.path.join(ROOT, parent.name + ".blend")
    bpy.ops.wm.save_as_mainfile(filepath=blend)
    return {
        "key": key, "name": parent.name, "set": "Suburban",
        "style_kit": ["Suburban"], "districts": ["D5"],
        "blend": os.path.relpath(blend, r"E:\VeyraCity").replace("\\", "/"),
        "fbx": os.path.relpath(fbx, r"E:\VeyraCity").replace("\\", "/"),
        "footprint_m": list(FOOTPRINTS.get(key, (W, D))),
        "bbox_m": [round(hi.x - lo.x, 2), round(hi.y - lo.y, 2), round(hi.z - lo.z, 2)],
        "height_m": round(hi.z - lo.z, 2), "target_height_m": want_h,
        "base_z": round(lo.z, 5), "uv": uv,
        "lod_mode": "authored",
        "lod_ratios": None,
        "lods": [L.mesh_report(o) for o in lods],
        "collision": [col.name],
        "slots": rep0["slots"],
    }


def main():
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else ["all"]
    keys = list(HOUSES) if args[0] == "all" else args
    out = [build(k) for k in keys]
    path = os.path.join(HERE, "report_houses.json")
    old = []
    if os.path.exists(path) and args[0] != "all":
        old = [r for r in json.load(open(path)) if r["key"] not in keys]
    with open(path, "w", encoding="utf-8") as fh:
        json.dump(old + out, fh, indent=1)
    print("VC_HOUSES_OK:" + path)


main()

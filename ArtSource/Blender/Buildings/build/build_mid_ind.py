"""VEYRA CITY - ResidentialMidrise set and D3 Ironport industrial set (original designs).

    blender --factory-startup -b -noaudio -P build_mid_ind.py -- <key|all>

Grouping of the records (world_building_placements.csv):

  ResidentialMidrise, 87 records, all `apartment`, all `box`, on exactly TWO footprints:
    * 34 x 18 m - 47 records, all D5, heights 12-18 m (a five-storey suburban block)
    * 38 x 24 m - 40 records, all D1, heights 28-75 m (a downtown residential tower)

  D3 Ironport, three style kits:
    * `Industrial` 41 (39 D3, 2 D7): `warehouse` 35, `rail_shed` 5, `warehouse_hq` 1; discrete footprints
      90 x 50 (18), 64 x 40 (12), 120 x 24 (5), 72 x 42 (4), 80 x 45 (2); heights 8.7-15.5 m.
    * `IndustrialTank` 80, all D3, all `cylinder`/`tank`: diameter 28.5-42.8 m (34.0 for 20 of them),
      height 12.5-19 m.
    * `IndustrialRefinery` 3, all D3, `process_column`: 8 x 8 m at 47.6-66 m tall.

Sheds and tanks read as silhouette and roof line from the road, so the triangles go into varied roof forms
(sawtooth, monitor, barrel, gable), rooftop plant and clerestory bands rather than facade detail - the roof
is one extruded profile solid, so a 120 m shed costs the same as a 64 m one.
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
RAD = math.radians

MID = ["VC_Mr_Panel", "VC_Mr_Glass", "VC_Mr_Metal", "VC_Mr_Concrete", "VC_Mr_Brick"]
MID_BRICK = ["VC_Mr_Brick", "VC_Mr_Glass", "VC_Mr_Metal", "VC_Mr_Concrete", "VC_Mr_Panel"]
IND = ["VC_In_Clad", "VC_In_Roof", "VC_In_Glass", "VC_In_Metal", "VC_In_Concrete"]
TANK = ["VC_In_TankShell", "VC_In_Metal", "VC_In_Concrete"]
WALL, GLASS, METAL, CONC, ALT = 0, 1, 2, 3, 4
I_CLAD, I_ROOF, I_GLASS, I_METAL, I_CONC = 0, 1, 2, 3, 4
T_SHELL, T_METAL, T_CONC = 0, 1, 2


# ------------------------------------------------------------------ ResidentialMidrise

def _mid_facade(bm, w, l, z0, z1, floor_h, pitch, m_wall, m_glass, lod=0):
    """Glazing recessed behind full-height piers and per-floor spandrel bands."""
    L.box(bm, (w - 0.7, l - 0.7, z1 - z0), base_at=(0, 0, z0), m=m_glass)
    if lod >= 2:
        L.box(bm, (w, l, z1 - z0), base_at=(0, 0, z0), m=m_wall)
        return
    step = 1 if lod == 0 else 2
    nx, ny = max(2, int(w / pitch)), max(2, int(l / pitch))
    for i in range(0, nx + 1, step):
        x = -w / 2.0 + w * i / nx
        for sy in (-1, 1):
            L.box(bm, (0.8 * step, 0.5, z1 - z0), base_at=(x, sy * (l / 2.0 - 0.18), z0), m=m_wall)
    for j in range(0, ny + 1, step):
        y = -l / 2.0 + l * j / ny
        for sx in (-1, 1):
            L.box(bm, (0.5, 0.8 * step, z1 - z0), base_at=(sx * (w / 2.0 - 0.18), y, z0), m=m_wall)
    n = max(1, int((z1 - z0) / floor_h))
    for k in range(0, n + 1, step):
        z = z0 + (z1 - z0) * k / n
        if z < z1 - 0.05:
            L.box(bm, (w + 0.05, l + 0.05, 0.5), base_at=(0, 0, z), m=m_wall)


def _balconies(bm, w, l, z0, z1, floor_h, m_slab, m_rail, lod=0, inset=0.0):
    """Projecting balcony slabs with a rail, on the street side only."""
    if lod >= 2:
        return
    n = max(1, int((z1 - z0) / floor_h))
    step = 1 if lod == 0 else 2
    for k in range(1, n, step):
        z = z0 + floor_h * k
        if z > z1 - 0.6:
            break
        L.box(bm, (w - 2.0 - inset, 1.60, 0.22), base_at=(0, -l / 2.0 - 0.80, z), m=m_slab)
        if lod == 0:
            L.box(bm, (w - 2.0 - inset, 0.08, 1.02), base_at=(0, -l / 2.0 - 1.58, z + 0.22), m=m_rail)


def midrise_slab(w, l, h, lod=0, floor=3.1, brick=False, loggia=False):
    """Five-storey-and-up apartment block: ground floor with an entrance, banded facade, balconies."""
    bm = bmesh.new()
    gh = 3.5
    top = h - 0.9
    L.box(bm, (w, l, gh), base_at=(0, 0.2, 0.0), m=WALL)
    if lod < 2:                                                     # entrance and ground-floor glazing
        L.box(bm, (w - 5.0, 0.12, gh - 1.1), loc=(0, -l / 2.0 + 0.30, 0.55 + (gh - 1.1) / 2.0), m=GLASS)
        L.box(bm, (3.0, 1.40, 0.24), base_at=(0, -l / 2.0 - 0.70, gh - 0.9), m=CONC)
        L.box(bm, (w + 0.3, 0.45, 0.45), base_at=(0, -l / 2.0 + 0.1, gh - 0.45), m=CONC)
    _mid_facade(bm, w, l, gh, top, floor, 4.4, WALL, GLASS, lod=lod)
    if loggia:                                                      # recessed loggias instead of balconies
        if lod < 2:
            nb = max(1, int((top - gh) / floor))
            for k in range(1, nb, 1 if lod == 0 else 2):
                z = gh + floor * k
                if z > top - 0.6:
                    break
                L.box(bm, (w * 0.42, 0.9, floor - 0.7), base_at=(0, -l / 2.0 + 0.45, z + 0.25), m=ALT)
    else:
        _balconies(bm, w, l, gh, top, floor, CONC, METAL, lod=lod)
    L.box(bm, (w, l, 0.18), base_at=(0, 0, top), m=CONC)
    L.parapet(bm, w, l, top + 0.18, WALL, h=0.52, t=0.26, m_cap=CONC)
    if lod < 2:                                                     # lift overrun / roof plant
        L.box(bm, (w * 0.22, l * 0.30, 2.40), base_at=(w * 0.18, 0, top + 0.18), m=WALL)
        L.box(bm, (w * 0.16, l * 0.18, 1.20), base_at=(-w * 0.22, l * 0.12, top + 0.18), m=METAL)
    return bm


# ------------------------------------------------------------------ D3 Ironport sheds

def _shed_box(bm, w, l, h, lod=0, dock=True):
    """Clad walls on a concrete plinth, with loading docks and a personnel door on the street side."""
    L.box(bm, (w, l, 1.10), base_at=(0, 0, 0.0), m=I_CONC)
    L.box(bm, (w, l, h - 1.10), base_at=(0, 0, 1.10), m=I_CLAD)
    if lod >= 2:
        return
    L.box(bm, (w + 0.10, l + 0.10, 0.30), base_at=(0, 0, 1.10), m=I_METAL)       # plinth flashing
    if dock:
        bays = max(2, int(w / 14.0))
        for i in range(bays):
            cx = -w / 2.0 + w * (i + 0.5) / bays
            L.box(bm, (4.20, 0.20, 4.40), base_at=(cx, -l / 2.0 - 0.05, 1.10), m=I_METAL)
            L.box(bm, (4.80, 0.60, 0.35), base_at=(cx, -l / 2.0 - 0.35, 5.50), m=I_METAL)   # dock canopy
    L.box(bm, (1.30, 0.18, 2.30), base_at=(w / 2.0 - 3.0, -l / 2.0 - 0.04, 1.10), m=I_METAL)
    if lod == 0:                                                                  # wall rib lines
        for i in range(1, max(2, int(w / 6.0))):
            x = -w / 2.0 + w * i / max(2, int(w / 6.0))
            for sy in (-1, 1):
                L.box(bm, (0.16, 0.12, h - 1.4), base_at=(x, sy * (l / 2.0 + 0.04), 1.10), m=I_METAL)


def shed_sawtooth(w, l, h, lod=0):
    """North-light sawtooth roof - the strongest silhouette in the set."""
    bm = bmesh.new()
    _shed_box(bm, w, l, h, lod=lod)
    bays = 5 if lod < 2 else 3
    pts, glass = L.sawtooth_profile(-l / 2.0, l / 2.0, 0.0, bays, 3.4)
    pts = [(y, z + h) if z > 0 else (y, h - 0.2) for (y, z) in pts]
    L.profile_solid(bm, pts, -w / 2.0, w / 2.0, I_ROOF)
    if lod < 2:
        for (y, z0, z1) in glass:
            L.box(bm, (w - 1.2, 0.14, (z1 - z0)), loc=(0, y - 0.1, h + (z0 + z1) / 2.0), m=I_GLASS)
    if lod == 0:
        L.box(bm, (w * 0.16, l * 0.10, 1.80), base_at=(-w * 0.26, l * 0.30, h + 3.2), m=I_METAL)
    return bm


def shed_monitor(w, l, h, lod=0):
    """Low-pitch gable with a continuous raised monitor (clerestory ridge) and rooftop plant."""
    bm = bmesh.new()
    _shed_box(bm, w, l, h, lod=lod)
    hl = l / 2.0
    mon = l * 0.22
    pts = [(-hl, h - 0.2), (-hl, h), (-mon, h + 1.9), (-mon, h + 3.4), (mon, h + 3.4),
           (mon, h + 1.9), (hl, h), (hl, h - 0.2)]
    L.profile_solid(bm, pts, -w / 2.0, w / 2.0, I_ROOF)
    if lod < 2:
        for sy in (-1, 1):
            L.box(bm, (w - 1.6, 0.14, 1.20), loc=(0, sy * mon, h + 2.6), m=I_GLASS)
        for i, sx in enumerate((-1, 0, 1)):
            L.box(bm, (w * 0.14, l * 0.16, 1.60), base_at=(sx * w * 0.28, -l * 0.26, h + 1.2), m=I_METAL)
    if lod == 0:
        for i in range(4):
            L.box(bm, (0.9, 0.9, 1.1), base_at=(-w * 0.3 + w * 0.2 * i, l * 0.30, h + 1.0), m=I_METAL)
    return bm


def shed_barrel(w, l, h, lod=0):
    """Arched (barrel-vault) roof, faceted - a different silhouette at the same footprint."""
    bm = bmesh.new()
    _shed_box(bm, w, l, h, lod=lod)
    hl = l / 2.0
    rise = 4.6
    seg = 7 if lod == 0 else (5 if lod == 1 else 3)
    pts = [(-hl, h - 0.2)]
    for i in range(seg + 1):
        t = i / seg
        pts.append((-hl + l * t, h + rise * math.sin(math.pi * t)))
    pts.append((hl, h - 0.2))
    L.profile_solid(bm, pts, -w / 2.0, w / 2.0, I_ROOF, smooth=lod == 0)
    if lod < 2:
        L.box(bm, (w - 2.0, 1.10, 0.35), loc=(0, 0, h + rise + 0.05), m=I_GLASS)   # ridge roof-light
    return bm


def shed_flat(w, l, h, lod=0):
    """Flat roof behind a parapet, carrying a big rooftop plant array."""
    bm = bmesh.new()
    _shed_box(bm, w, l, h, lod=lod)
    L.box(bm, (w, l, 0.22), base_at=(0, 0, h), m=I_ROOF)
    L.parapet(bm, w, l, h + 0.22, I_CLAD, h=0.95, t=0.30, m_cap=I_METAL)
    if lod < 2:
        for i in range(3):
            for j in range(2):
                L.box(bm, (w * 0.17, l * 0.20, 1.70),
                      base_at=(-w * 0.27 + w * 0.27 * i, -l * 0.16 + l * 0.32 * j, h + 0.22), m=I_METAL)
    if lod == 0:
        for i in range(5):
            L.cyl(bm, 0.42, 0.42, 1.30, 8, I_METAL, base_at=(-w * 0.34 + w * 0.17 * i, l * 0.34, h + 0.22))
    return bm


def shed_rail(w, l, h, lod=0):
    """Rail shed: a long gable with a continuous ridge vent and open gable ends."""
    bm = bmesh.new()
    L.box(bm, (w, l, 0.90), base_at=(0, 0, 0.0), m=I_CONC)
    for sy in (-1, 1):                                        # side walls only; the ends stay open
        L.box(bm, (w, 0.45, h - 0.90), base_at=(0, sy * (l / 2.0 - 0.22), 0.90), m=I_CLAD)
    if lod < 2:
        for i in range(max(3, int(w / 10.0))):
            x = -w / 2.0 + w * (i + 0.5) / max(3, int(w / 10.0))
            for sy in (-1, 1):
                L.box(bm, (0.35, 0.60, h - 1.0), base_at=(x, sy * (l / 2.0 - 0.22), 0.90), m=I_METAL)
    hl = l / 2.0
    pts = [(-hl, h - 0.3), (-hl, h), (-l * 0.09, h + 2.6), (-l * 0.09, h + 3.3), (l * 0.09, h + 3.3),
           (l * 0.09, h + 2.6), (hl, h), (hl, h - 0.3)]
    L.profile_solid(bm, pts, -w / 2.0, w / 2.0, I_ROOF)
    if lod < 2:
        for sy in (-1, 1):
            L.box(bm, (w - 1.0, 0.12, 0.55), loc=(0, sy * l * 0.09, h + 2.95), m=I_GLASS)
    return bm


# ------------------------------------------------------------------ D3 tanks and process column

def tank(d, h, lod=0, dome=False):
    """Storage tank: shell with wind-girder rings, a cone or dome roof, external stair and top railing."""
    bm = bmesh.new()
    r = d / 2.0
    seg = 20 if lod == 0 else (14 if lod == 1 else 10)
    L.cyl(bm, r + 0.9, r + 0.9, 0.55, seg, T_CONC, base_at=(0, 0, 0.0))          # ring foundation
    L.cyl(bm, r, r, h - 0.55, seg, T_SHELL, base_at=(0, 0, 0.55))
    if dome:
        L.cyl(bm, r * 1.01, r * 0.55, 1.5, seg, T_SHELL, base_at=(0, 0, h - 0.55))
        L.cyl(bm, r * 0.55, 0.0, 1.1, seg, T_SHELL, base_at=(0, 0, h + 0.95))
    else:
        L.cyl(bm, r * 1.02, 0.6, 2.6, seg, T_SHELL, base_at=(0, 0, h - 0.55))
    if lod >= 2:
        return bm
    for k in (0.35, 0.68):                                                        # wind-girder rings
        L.cyl(bm, r + 0.14, r + 0.14, 0.22, seg, T_METAL, base_at=(0, 0, 0.55 + (h - 1.1) * k))
    steps = 10 if lod == 0 else 5                                                 # external spiral stair
    for i in range(steps):
        a0 = 2.0 * math.pi * i / steps * 0.75
        z = 0.55 + (h - 1.4) * i / steps
        L.box(bm, (1.10, 0.22, 0.16), loc=(math.cos(a0) * (r + 0.55), math.sin(a0) * (r + 0.55), z),
              rot=Matrix.Rotation(a0 + math.pi / 2.0, 4, "Z"), m=T_METAL)
        L.box(bm, (0.10, 0.10, 1.05), loc=(math.cos(a0) * (r + 1.0), math.sin(a0) * (r + 1.0), z + 0.6),
              m=T_METAL)
    # roof railing: a ring of posts and a top rail. A solid disc here reads as a hole in the roof.
    ztop = h + (2.3 if not dome else 0.9)
    posts = 12 if lod == 0 else 8
    for i in range(posts):
        a0 = 2.0 * math.pi * i / posts
        L.box(bm, (0.10, 0.10, 1.05),
              base_at=(math.cos(a0) * r * 0.60, math.sin(a0) * r * 0.60, ztop - 0.25), m=T_METAL)
        a1 = 2.0 * math.pi * (i + 0.5) / posts
        seglen = 2.0 * math.pi * r * 0.60 / posts
        L.box(bm, (seglen, 0.07, 0.07),
              loc=(math.cos(a1) * r * 0.60, math.sin(a1) * r * 0.60, ztop + 0.75),
              rot=Matrix.Rotation(a1 + math.pi / 2.0, 4, "Z"), m=T_METAL)
    L.cyl(bm, 1.10, 1.10, 0.55, 8, T_METAL, base_at=(0, 0, ztop - 0.25))      # centre hatch / vent
    if lod == 0:
        for i in range(3):                                                        # nozzles / pipework
            a0 = 2.0 * math.pi * i / 3.0 + 0.4
            L.cyl(bm, 0.30, 0.30, 1.4, 8, T_METAL,
                  loc=(math.cos(a0) * (r + 0.6), math.sin(a0) * (r + 0.6), 2.2),
                  rot=Matrix.Rotation(math.pi / 2.0, 4, "Y") @ Matrix.Rotation(a0, 4, "X"))
    return bm


def process_column(d, h, lod=0):
    """Refinery process column: a tall vessel with platforms, a ladder cage and a vapour line."""
    bm = bmesh.new()
    r = d / 2.0
    seg = 14 if lod == 0 else (10 if lod == 1 else 8)
    L.cyl(bm, r + 0.8, r + 0.8, 1.2, seg, T_CONC, base_at=(0, 0, 0.0))
    L.cyl(bm, r, r * 0.92, h - 3.0, seg, T_SHELL, base_at=(0, 0, 1.2))
    L.cyl(bm, r * 0.92, 0.0, 1.9, seg, T_SHELL, base_at=(0, 0, h - 1.8))
    if lod >= 2:
        return bm
    plats = 5 if lod == 0 else 3
    for i in range(1, plats + 1):
        z = 1.2 + (h - 5.0) * i / (plats + 1)
        L.cyl(bm, r + 1.5, r + 1.5, 0.14, seg, T_METAL, base_at=(0, 0, z))
        if lod == 0:
            L.cyl(bm, r + 1.5, r + 1.5, 1.05, max(8, seg // 2), T_METAL, base_at=(0, 0, z + 0.14))
    L.cyl(bm, 0.22, 0.22, h - 2.0, 6, T_METAL, base_at=(r + 1.1, 0, 1.2))          # ladder stringer
    if lod == 0:
        L.cyl(bm, 0.38, 0.38, 6.0, 8, T_METAL, loc=(0, r + 2.6, h - 4.0),
              rot=Matrix.Rotation(math.pi / 2.0, 4, "X"))
    return bm


# ------------------------------------------------------------------ specs

SPECS = [
    # key            fn                 w      l      h     slots   set       styles                 districts
    ("mid34_a", lambda lod: midrise_slab(34, 18, 17.0, lod), 34.0, 18.0, 17.0, MID, "midrise", ["ResidentialMidrise"], ["D1", "D5"]),
    ("mid34_b", lambda lod: midrise_slab(34, 18, 17.0, lod, brick=True, loggia=True), 34.0, 18.0, 17.0, MID_BRICK, "midrise", ["ResidentialMidrise"], ["D1", "D5"]),
    ("mid34_c", lambda lod: midrise_slab(34, 18, 14.0, lod), 34.0, 18.0, 14.0, MID_BRICK, "midrise", ["ResidentialMidrise"], ["D1", "D5"]),
    ("mid38_30", lambda lod: midrise_slab(38, 24, 30.0, lod), 38.0, 24.0, 30.0, MID, "midrise", ["ResidentialMidrise"], ["D1", "D5"]),
    ("mid38_45", lambda lod: midrise_slab(38, 24, 45.0, lod, loggia=True), 38.0, 24.0, 45.0, MID, "midrise", ["ResidentialMidrise"], ["D1", "D5"]),
    ("mid38_60", lambda lod: midrise_slab(38, 24, 60.0, lod), 38.0, 24.0, 60.0, MID_BRICK, "midrise", ["ResidentialMidrise"], ["D1", "D5"]),
    ("mid38_75", lambda lod: midrise_slab(38, 24, 75.0, lod, loggia=True), 38.0, 24.0, 75.0, MID, "midrise", ["ResidentialMidrise"], ["D1", "D5"]),
    ("shed_sawtooth", lambda lod: shed_sawtooth(90, 50, 12.5, lod), 90.0, 50.0, 12.5, IND, "ironport", ["Industrial"], ["D3", "D7"]),
    ("shed_monitor", lambda lod: shed_monitor(90, 50, 12.5, lod), 90.0, 50.0, 12.5, IND, "ironport", ["Industrial"], ["D3", "D7"]),
    ("shed_barrel", lambda lod: shed_barrel(64, 40, 12.0, lod), 64.0, 40.0, 12.0, IND, "ironport", ["Industrial"], ["D3", "D7"]),
    ("shed_flat", lambda lod: shed_flat(64, 40, 9.0, lod), 64.0, 40.0, 9.0, IND, "ironport", ["Industrial"], ["D3", "D7"]),
    ("shed_rail", lambda lod: shed_rail(120, 24, 13.0, lod), 120.0, 24.0, 13.0, IND, "ironport", ["Industrial"], ["D3", "D7"]),
    ("shed_rail_low", lambda lod: shed_rail(120, 24, 7.5, lod), 120.0, 24.0, 7.5, IND, "ironport", ["Industrial"], ["D3", "D7"]),
    ("tank_cone", lambda lod: tank(34.0, 16.0, lod), 34.0, 34.0, 16.0, TANK, "ironporttanks", ["IndustrialTank"], ["D3"]),
    ("tank_dome", lambda lod: tank(34.0, 12.5, lod, dome=True), 34.0, 34.0, 12.5, TANK, "ironporttanks", ["IndustrialTank"], ["D3"]),
    ("process_column", lambda lod: process_column(8.0, 52.0, lod), 8.0, 8.0, 52.0, TANK, "ironporttanks", ["IndustrialRefinery"], ["D3"]),
]
PREFIX = {"midrise": "VC_Mid_", "ironport": "VC_Ind_", "ironporttanks": "VC_Ind_"}
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
        uv = L.unwrap(o, margin=0.006, cube=2.0 if group == "midrise" else 4.0) and uv
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
    for group, path in (("midrise", "report_midrise.json"), ("ironport", "report_ironport.json"),
                        ("ironporttanks", "report_ironporttanks.json")):
        rows = [r for r in out if r["set"] == group]
        full = os.path.join(HERE, path)
        old = []
        if os.path.exists(full) and args[0] != "all":
            old = [r for r in json.load(open(full)) if r["key"] not in keys]
        if rows or old:
            with open(full, "w", encoding="utf-8") as fh:
                json.dump(old + rows, fh, indent=1)
    print("VC_MIDIND_OK")


main()

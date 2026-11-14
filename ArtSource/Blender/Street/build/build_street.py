"""VEYRA CITY - street furniture builder (original geometry).

    blender --factory-startup -b -noaudio -P build_street.py -- <prop_key|all>

Every prop is built with the base at z = 0, the mounting point on the x/y origin and the front facing
Blender -Y, then exported with the VC_Unity_Prop preset (Tools/Blender/vc_tools.py).
"""
import json
import math
import os
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)                      # ArtSource/Blender/Street
sys.path.insert(0, r"E:\VeyraCity\Tools\Blender")
sys.path.insert(0, HERE)
import vc_tools                                    # noqa: E402
import vc_street_lib as L                          # noqa: E402

EXPORT_DIR = os.path.join(ROOT, "export")
RAD = math.radians
ROT_X90 = Matrix.Rotation(RAD(90), 4, "X")   # cylinder axis -> +Y
ROT_Y90 = Matrix.Rotation(RAD(90), 4, "Y")   # cylinder axis -> +X


# ----------------------------------------------------------------- props

def p_hydrant():
    """Veyra municipal fire hydrant, 0.78 m. Two colour variants share this mesh (slot 0 swap)."""
    bm = bmesh.new()
    L.cyl(bm, 0.175, 0.160, 0.06, 10, 0, base_at=(0, 0, 0.0))        # ground flange
    L.cyl(bm, 0.120, 0.108, 0.46, 10, 0, base_at=(0, 0, 0.06))       # barrel
    L.cyl(bm, 0.108, 0.140, 0.06, 10, 0, base_at=(0, 0, 0.52))       # shoulder
    L.cyl(bm, 0.140, 0.115, 0.08, 10, 0, base_at=(0, 0, 0.58))       # bonnet
    L.cyl(bm, 0.115, 0.045, 0.07, 10, 0, base_at=(0, 0, 0.66))       # dome
    L.box(bm, (0.052, 0.052, 0.045), base_at=(0, 0, 0.73), m=0)      # operating nut
    L.cyl(bm, 0.075, 0.070, 0.13, 8, 0, loc=(0.0, -0.115, 0.40), rot=ROT_X90)   # pumper nozzle (front)
    L.cyl(bm, 0.058, 0.054, 0.12, 6, 0, loc=(0.115, 0.0, 0.42), rot=ROT_Y90)    # side nozzles
    L.cyl(bm, 0.058, 0.054, 0.12, 6, 0, loc=(-0.115, 0.0, 0.42), rot=ROT_Y90)
    L.mat("VC_Hydrant_Yellow")   # second colour variant, same mesh
    obj = L.new_object("VC_Hydrant", bm, ["VC_Hydrant_Red"])
    cols = [L.collider("UBX_VC_Hydrant", "UBX", size=(0.30, 0.30, 0.78), base_at=(0, 0, 0))]
    return obj, cols, 2, "0.78 m. Second colour variant: material VC_Hydrant_Yellow (same mesh)."


def p_parkingmeter():
    bm = bmesh.new()
    L.cyl(bm, 0.095, 0.088, 0.03, 8, 0, base_at=(0, 0, 0.0))
    L.cyl(bm, 0.046, 0.042, 0.92, 8, 0, base_at=(0, 0, 0.03))
    L.wedge_box(bm, (0.175, 0.135, 0.34), None, m=0, base_at=(0, 0, 0.95), top_scale_x=0.92, top_scale_y=0.92)
    L.box(bm, (0.125, 0.022, 0.15), loc=(0, -0.070, 1.15), m=1)      # display bezel
    L.box(bm, (0.055, 0.020, 0.016), loc=(0, -0.070, 1.25), m=0)     # coin slot
    L.wedge_box(bm, (0.178, 0.138, 0.045), None, m=0, base_at=(0, 0, 1.29),
                top_scale_x=0.62, top_scale_y=0.62)
    obj = L.new_object("VC_ParkingMeter", bm, ["VC_Paint_Slate", "VC_Glass_Clear"])
    cols = [L.collider("UBX_VC_ParkingMeter", "UBX", size=(0.19, 0.16, 1.335), base_at=(0, 0, 0))]
    return obj, cols, 2, "1.34 m, single head on post. Display glass = slot 1."


def p_trashcan_civic():
    bm = bmesh.new()
    L.cyl(bm, 0.300, 0.290, 0.05, 12, 0, base_at=(0, 0, 0.0))        # plinth ring
    L.cyl(bm, 0.272, 0.300, 0.72, 12, 0, base_at=(0, 0, 0.05))       # body (flares out)
    L.cyl(bm, 0.315, 0.315, 0.07, 12, 0, base_at=(0, 0, 0.77))       # rim band
    L.cyl(bm, 0.300, 0.330, 0.03, 12, 0, base_at=(0, 0, 0.84))       # lid lip
    L.cyl(bm, 0.330, 0.150, 0.11, 12, 0, base_at=(0, 0, 0.87))       # rain hood
    L.cyl(bm, 0.045, 0.030, 0.05, 6, 0, base_at=(0, 0, 0.98))        # knob
    obj = L.new_object("VC_TrashCan_Civic", bm, ["VC_Paint_Civic_Green"])
    cols = [L.collider("UCX_VC_TrashCan_Civic", "UCX", radius=0.325, h=1.03, seg=8, base_at=(0, 0, 0))]
    return obj, cols, 2, "1.03 m civic/park litter bin with rain hood."


def p_dumpster():
    bm = bmesh.new()
    L.wedge_box(bm, (1.78, 1.16, 0.95), None, m=0, base_at=(0, 0, 0.14),
                top_scale_x=1.035, top_scale_y=1.06)                  # body, flared to the top
    rot = Matrix.Rotation(RAD(7), 4, "X")
    L.box(bm, (1.84, 0.63, 0.05), loc=(0, -0.315, 1.125), rot=rot, m=0)       # lid, front leaf
    L.box(bm, (1.84, 0.63, 0.05), loc=(0, 0.315, 1.125), rot=Matrix.Rotation(RAD(-7), 4, "X"), m=0)
    L.cyl(bm, 0.022, 0.022, 1.86, 6, 0, loc=(0, 0, 1.15), rot=ROT_Y90)        # hinge bar
    for sx in (-1, 1):
        for sy in (-1, 1):
            L.box(bm, (0.44, 0.10, 0.13), loc=(sx * 0.44, sy * 0.615, 0.62), m=0)   # fork pockets
            L.box(bm, (0.07, 0.11, 0.13), loc=(sx * 0.74, sy * 0.46, 0.145), m=0)   # caster forks
            L.cyl(bm, 0.075, 0.075, 0.055, 8, 0, loc=(sx * 0.74, sy * 0.46, 0.075), rot=ROT_Y90)
    obj = L.new_object("VC_Dumpster", bm, ["VC_Paint_Civic_Blue"])
    cols = [L.collider("UBX_VC_Dumpster", "UBX", size=(1.86, 1.28, 1.15), base_at=(0, 0, 0))]
    return obj, cols, 2, "1.84 x 1.28 x 1.15 m commercial wheeled dumpster, sloped twin-leaf lid."


def p_postbox():
    bm = bmesh.new()
    L.box(bm, (0.56, 0.46, 0.10), base_at=(0, 0, 0.0), m=0)
    L.box(bm, (0.50, 0.40, 1.00), base_at=(0, 0, 0.10), m=0)
    L.wedge_box(bm, (0.52, 0.42, 0.15), None, m=0, base_at=(0, 0, 1.10),
                top_scale_x=0.72, top_scale_y=0.50)                   # hipped cap
    L.box(bm, (0.32, 0.030, 0.055), loc=(0, -0.205, 0.98), m=0)       # letter slot surround
    L.wedge_box(bm, (0.36, 0.075, 0.045), None, m=0, base_at=(0, -0.222, 1.025),
                top_scale_y=0.35, top_shift_y=-0.012)                 # slot hood
    L.box(bm, (0.34, 0.022, 0.46), loc=(0, -0.206, 0.55), m=0)        # collection door
    L.cyl(bm, 0.030, 0.030, 0.045, 6, 0, loc=(0.11, -0.222, 0.55), rot=ROT_X90)   # door lock
    obj = L.new_object("VC_PostBox", bm, ["VC_Paint_Civic_Green"])
    cols = [L.collider("UBX_VC_PostBox", "UBX", size=(0.56, 0.46, 1.25), base_at=(0, 0, 0))]
    return obj, cols, 2, "1.25 m pillar post box, slot and collection door on the front (-Y)."


def p_newsbox():
    bm = bmesh.new()
    for sx in (-1, 1):
        for sy in (-1, 1):
            L.cyl(bm, 0.026, 0.024, 0.28, 6, 0, base_at=(sx * 0.17, sy * 0.145, 0.0))
    L.box(bm, (0.42, 0.38, 0.68), base_at=(0, 0, 0.28), m=0)
    L.box(bm, (0.345, 0.022, 0.355), loc=(0, -0.182, 0.80),
          rot=Matrix.Rotation(RAD(14), 4, "X"), m=1)                  # sloped vend window
    L.wedge_box(bm, (0.44, 0.40, 0.14), None, m=0, base_at=(0, 0, 0.96),
                top_scale_y=0.72, top_shift_y=-0.030)                 # hood
    L.box(bm, (0.085, 0.070, 0.115), loc=(0.145, -0.185, 0.90), m=0)  # coin head
    L.cyl(bm, 0.016, 0.016, 0.30, 6, 0, loc=(0, -0.205, 0.62), rot=ROT_Y90)   # pull handle
    obj = L.new_object("VC_NewsBox", bm, ["VC_Paint_Orange", "VC_Glass_Clear"])
    cols = [L.collider("UBX_VC_NewsBox", "UBX", size=(0.44, 0.40, 1.10), base_at=(0, 0, 0))]
    return obj, cols, 2, "1.10 m newspaper vending box. Window glass = slot 1."


def p_busshelter():
    bm = bmesh.new()
    posts = [(-1.90, -0.70), (-1.90, 0.70), (1.90, -0.70), (1.90, 0.70)]
    for px, py in posts:
        L.cyl(bm, 0.058, 0.052, 2.40, 8, 0, base_at=(px, py, 0.0))
    L.box(bm, (3.72, 0.028, 1.74), loc=(0, 0.700, 1.33), m=1)         # rear glazing
    L.box(bm, (0.028, 1.30, 1.74), loc=(-1.900, 0.0, 1.33), m=1)      # left glazing
    L.box(bm, (3.72, 0.060, 0.09), loc=(0, 0.700, 0.420), m=0)        # glazing sill rails
    L.box(bm, (0.060, 1.30, 0.09), loc=(-1.900, 0.0, 0.420), m=0)
    L.box(bm, (4.16, 1.76, 0.095), base_at=(0, 0, 2.400), m=0)        # roof deck
    L.box(bm, (4.16, 0.070, 0.150), loc=(0, -0.845, 2.420), m=0)      # front fascia
    L.box(bm, (2.56, 0.44, 0.060), base_at=(0, 0.420, 0.460), m=2)    # bench seat
    L.box(bm, (2.56, 0.055, 0.130), loc=(0, 0.640, 0.740), m=2)       # back rail
    for sx in (-1, 1):
        L.box(bm, (0.065, 0.42, 0.46), base_at=(sx * 1.18, 0.420, 0.0), m=0)
    # right-hand bay: blank poster frame (Unity applies Veyra's own artwork)
    L.box(bm, (0.075, 1.22, 1.72), loc=(1.900, 0.0, 1.33), m=0)       # frame body
    L.box(bm, (0.030, 1.06, 1.56), loc=(1.880, 0.0, 1.33), m=3)       # blank poster face (outward, +X)
    L.box(bm, (0.030, 1.06, 1.56), loc=(1.920, 0.0, 1.33), m=3)       # blank poster face (inward)
    obj = L.new_object("VC_BusShelter", bm,
                       ["VC_Paint_Alu_Grey", "VC_Glass_Clear", "VC_Wood_Slat", "VC_Poster_Blank"])
    cols = []
    for i, (px, py) in enumerate(posts, start=1):
        cols.append(L.collider("UBX_VC_BusShelter_Post%d" % i, "UBX", size=(0.13, 0.13, 2.40),
                               base_at=(px, py, 0.0)))
    cols.append(L.collider("UBX_VC_BusShelter_Rear", "UBX", size=(3.72, 0.09, 1.95), base_at=(0, 0.70, 0.40)))
    cols.append(L.collider("UBX_VC_BusShelter_Left", "UBX", size=(0.09, 1.30, 1.95), base_at=(-1.90, 0.0, 0.40)))
    cols.append(L.collider("UBX_VC_BusShelter_Poster", "UBX", size=(0.10, 1.22, 1.95), base_at=(1.90, 0.0, 0.40)))
    cols.append(L.collider("UBX_VC_BusShelter_Bench", "UBX", size=(2.56, 0.46, 0.52), base_at=(0, 0.42, 0.0)))
    cols.append(L.collider("UBX_VC_BusShelter_Roof", "UBX", size=(4.16, 1.76, 0.25), base_at=(0, 0, 2.40)))
    return obj, cols, 3, ("4.16 x 1.76 x 2.50 m. Open side faces -Y (Unity +Z). Poster frame is BLANK "
                          "(slot 3), both faces.")


def _cobra_arm(bm, sign, m_body, m_lens):
    """One cobra arm + luminaire reaching `sign` * Y (sign -1 = over the carriageway, the prop front)."""
    pts = [(0.0, sign * 0.00, 8.52), (0.0, sign * 0.30, 8.93), (0.0, sign * 0.80, 9.20),
           (0.0, sign * 1.45, 9.32), (0.0, sign * 2.10, 9.31)]
    L.tube_along(bm, pts, [0.056, 0.053, 0.050, 0.047, 0.045], seg=8, m=m_body)
    L.wedge_box(bm, (0.30, 0.80, 0.175), loc=(0.0, sign * 2.46, 9.235), m=m_body,
                top_scale_x=0.72, top_scale_y=0.86)                   # cobra head shell
    L.box(bm, (0.235, 0.615, 0.022), loc=(0.0, sign * 2.46, 9.140), m=m_lens)   # lens (emissive slot)


def _lamp_pole_cobra(bm):
    L.cyl(bm, 0.225, 0.205, 0.055, 10, 0, base_at=(0, 0, 0.0))        # base plate
    L.cyl(bm, 0.145, 0.125, 0.30, 10, 0, base_at=(0, 0, 0.055))       # anchor collar
    L.cyl(bm, 0.098, 0.058, 8.17, 10, 0, base_at=(0, 0, 0.355))       # tapered shaft
    L.box(bm, (0.100, 0.024, 0.30), loc=(0.0, -0.095, 0.95), m=0)     # access door


def p_lamp_cobra_single():
    bm = bmesh.new()
    _lamp_pole_cobra(bm)
    _cobra_arm(bm, -1, 0, 1)
    obj = L.new_object("VC_Lamp_Cobra_Single", bm, ["VC_Paint_Alu_Grey", "VC_Lamp_Emissive"])
    cols = [L.collider("UCX_VC_Lamp_Cobra_Single", "UCX", radius=0.145, h=1.20, seg=8, base_at=(0, 0, 0)),
            L.collider("UBX_VC_Lamp_Cobra_Single_Shaft", "UBX", size=(0.20, 0.20, 8.52), base_at=(0, 0, 0))]
    return obj, cols, 3, "8.52 m pole, arm reaches 2.46 m toward -Y. Lens = slot 1 (VC_Lamp_Emissive)."


def p_lamp_cobra_twin():
    bm = bmesh.new()
    _lamp_pole_cobra(bm)
    _cobra_arm(bm, -1, 0, 1)
    _cobra_arm(bm, 1, 0, 1)
    obj = L.new_object("VC_Lamp_Cobra_Twin", bm, ["VC_Paint_Alu_Grey", "VC_Lamp_Emissive"])
    cols = [L.collider("UCX_VC_Lamp_Cobra_Twin", "UCX", radius=0.145, h=1.20, seg=8, base_at=(0, 0, 0)),
            L.collider("UBX_VC_Lamp_Cobra_Twin_Shaft", "UBX", size=(0.20, 0.20, 8.52), base_at=(0, 0, 0))]
    return obj, cols, 3, "8.52 m pole, two opposed arms (±Y). Lenses = slot 1 (VC_Lamp_Emissive)."


def p_lamp_boulevard_twin():
    bm = bmesh.new()
    L.cyl(bm, 0.190, 0.175, 0.12, 10, 2, base_at=(0, 0, 0.0))         # concrete plinth
    L.cyl(bm, 0.130, 0.115, 0.20, 10, 0, base_at=(0, 0, 0.12))        # collar
    L.cyl(bm, 0.088, 0.062, 5.86, 10, 0, base_at=(0, 0, 0.32))        # shaft
    L.cyl(bm, 0.072, 0.058, 0.10, 10, 0, base_at=(0, 0, 6.18))        # cap
    L.box(bm, (0.090, 0.028, 0.26), loc=(0.0, -0.088, 0.95), m=0)     # access door
    for sy in (-1, 1):
        rot = Matrix.Rotation(RAD(sy * -17), 4, "X")
        L.box(bm, (0.105, 1.72, 0.105), loc=(0.0, sy * 0.85, 6.38), rot=rot, m=0)     # straight arm
        L.wedge_box(bm, (0.345, 0.74, 0.095), loc=(0.0, sy * 1.78, 6.650), m=0,
                    top_scale_x=0.80, top_scale_y=0.90)               # flat luminaire shell
        L.box(bm, (0.285, 0.620, 0.018), loc=(0.0, sy * 1.78, 6.595), m=1)   # lens (emissive slot)
    obj = L.new_object("VC_Lamp_Boulevard_Twin", bm,
                       ["VC_Paint_Alu_Grey", "VC_Lamp_Emissive", "VC_Concrete_Plinth"])
    cols = [L.collider("UCX_VC_Lamp_Boulevard_Twin", "UCX", radius=0.19, h=0.32, seg=8, base_at=(0, 0, 0)),
            L.collider("UBX_VC_Lamp_Boulevard_Twin_Shaft", "UBX", size=(0.18, 0.18, 6.28), base_at=(0, 0, 0))]
    return obj, cols, 3, "6.28 m modern twin-arm boulevard lamp. Lenses = slot 1 (VC_Lamp_Emissive)."


def _ornate_lantern(bm, x, z, m_body, m_lens):
    """Tapered octagonal lantern: shoe, glass cage, mid ring, roof, finial. Base at z."""
    L.cyl(bm, 0.160, 0.140, 0.09, 8, m_body, base_at=(x, 0, z))
    L.cyl(bm, 0.148, 0.112, 0.52, 8, m_lens, base_at=(x, 0, z + 0.09))        # glass cage
    L.cyl(bm, 0.120, 0.120, 0.035, 8, m_body, base_at=(x, 0, z + 0.61))
    L.cyl(bm, 0.215, 0.055, 0.145, 8, m_body, base_at=(x, 0, z + 0.645))     # shallow pyramidal roof
    L.cyl(bm, 0.034, 0.010, 0.095, 6, m_body, base_at=(x, 0, z + 0.790))      # finial


def _ornate_column(bm, top_z, m_body):
    """Stepped plinth + ogee base + fluted-look tapered column, from z = 0 to top_z."""
    L.cyl(bm, 0.300, 0.282, 0.10, 8, m_body, base_at=(0, 0, 0.0))
    L.cyl(bm, 0.262, 0.222, 0.14, 8, m_body, base_at=(0, 0, 0.10))
    L.cyl(bm, 0.205, 0.145, 0.22, 10, m_body, base_at=(0, 0, 0.24))
    L.cyl(bm, 0.145, 0.108, 0.33, 10, m_body, base_at=(0, 0, 0.46))
    L.cyl(bm, 0.098, 0.072, top_z - 0.85 - 0.10, 10, m_body, base_at=(0, 0, 0.79))
    L.cyl(bm, 0.112, 0.112, 0.06, 10, m_body, base_at=(0, 0, top_z - 0.16))   # collar ring
    L.cyl(bm, 0.074, 0.062, 0.10, 10, m_body, base_at=(0, 0, top_z - 0.10))


def p_lamp_ornate_single():
    bm = bmesh.new()
    _ornate_column(bm, 4.12, 0)
    _ornate_lantern(bm, 0.0, 4.12, 0, 1)
    obj = L.new_object("VC_Lamp_Ornate_Single", bm, ["VC_Iron_Painted", "VC_Lamp_Emissive"])
    cols = [L.collider("UCX_VC_Lamp_Ornate_Single", "UCX", radius=0.30, h=0.46, seg=8, base_at=(0, 0, 0)),
            L.collider("UBX_VC_Lamp_Ornate_Single_Shaft", "UBX", size=(0.22, 0.22, 4.12), base_at=(0, 0, 0))]
    return obj, cols, 3, "5.01 m post-top cast-iron lantern standard. Glass cage = slot 1 (VC_Lamp_Emissive)."


def p_lamp_ornate_twin():
    bm = bmesh.new()
    _ornate_column(bm, 3.92, 0)
    for sx in (-1, 1):
        pts = [(sx * 0.055, 0.0, 3.78), (sx * 0.34, 0.0, 3.92), (sx * 0.62, 0.0, 4.08),
               (sx * 0.80, 0.0, 4.20)]
        L.tube_along(bm, pts, [0.052, 0.046, 0.042, 0.040], seg=6, m=0)       # scroll bracket
        _ornate_lantern(bm, sx * 0.80, 4.20, 0, 1)
    L.cyl(bm, 0.090, 0.026, 0.17, 8, 0, base_at=(0, 0, 3.92))                 # central finial
    obj = L.new_object("VC_Lamp_Ornate_Twin", bm, ["VC_Iron_Painted", "VC_Lamp_Emissive"])
    cols = [L.collider("UCX_VC_Lamp_Ornate_Twin", "UCX", radius=0.30, h=0.46, seg=8, base_at=(0, 0, 0)),
            L.collider("UBX_VC_Lamp_Ornate_Twin_Shaft", "UBX", size=(0.22, 0.22, 3.92), base_at=(0, 0, 0))]
    return obj, cols, 3, ("5.09 m twin-lantern cast-iron standard, lanterns at x = ±0.80 m. "
                          "Glass cages = slot 1 (VC_Lamp_Emissive).")


def p_signblade():
    bm = bmesh.new()
    L.cyl(bm, 0.095, 0.088, 0.06, 8, 0, base_at=(0, 0, 0.0))
    L.cyl(bm, 0.046, 0.040, 2.56, 8, 0, base_at=(0, 0, 0.06))
    L.box(bm, (1.10, 0.022, 0.225), loc=(0, 0, 2.735), m=1)           # blade facing ±Y
    L.box(bm, (0.022, 1.10, 0.225), loc=(0, 0, 2.470), m=1)           # blade facing ±X
    L.box(bm, (0.075, 0.075, 0.075), loc=(0, 0, 2.610), m=0)          # bracket block
    L.cyl(bm, 0.055, 0.020, 0.09, 8, 0, base_at=(0, 0, 2.855))        # finial
    obj = L.new_object("VC_SignBlade", bm, ["VC_Steel_Dark", "VC_Sign_BlankFace"])
    cols = [L.collider("UBX_VC_SignBlade", "UBX", size=(0.14, 0.14, 2.62), base_at=(0, 0, 0))]
    return obj, cols, 3, "2.95 m street-name blade sign, two crossed BLANK plates (slot 1)."


def p_signspeed():
    bm = bmesh.new()
    L.cyl(bm, 0.085, 0.078, 0.05, 8, 0, base_at=(0, 0, 0.0))
    L.cyl(bm, 0.036, 0.032, 2.05, 8, 0, base_at=(0, 0, 0.05))
    L.cyl(bm, 0.330, 0.330, 0.022, 12, 1, loc=(0, 0.014, 2.03), rot=ROT_X90)  # blank plate
    L.box(bm, (0.075, 0.055, 0.22), loc=(0, 0.042, 2.03), m=0)        # backing bracket
    obj = L.new_object("VC_SignSpeedLimit", bm, ["VC_Steel_Dark", "VC_Sign_BlankFace"])
    cols = [L.collider("UBX_VC_SignSpeedLimit", "UBX", size=(0.12, 0.12, 2.10), base_at=(0, 0, 0))]
    return obj, cols, 3, "2.36 m slim post, 0.66 m BLANK circular plate (slot 1) facing -Y."


def p_bollard():
    bm = bmesh.new()
    L.cyl(bm, 0.135, 0.122, 0.05, 10, 0, base_at=(0, 0, 0.0))
    L.cyl(bm, 0.098, 0.086, 0.55, 10, 0, base_at=(0, 0, 0.05))
    L.cyl(bm, 0.092, 0.090, 0.10, 10, 1, base_at=(0, 0, 0.60))        # retroreflective band
    L.cyl(bm, 0.086, 0.082, 0.23, 10, 0, base_at=(0, 0, 0.70))
    L.cyl(bm, 0.082, 0.030, 0.09, 10, 0, base_at=(0, 0, 0.93))        # dome
    obj = L.new_object("VC_Bollard", bm, ["VC_Paint_Slate", "VC_Retro_White"])
    cols = [L.collider("UCX_VC_Bollard", "UCX", radius=0.135, h=1.02, seg=8, base_at=(0, 0, 0))]
    return obj, cols, 2, "1.02 m traffic bollard, reflective band = slot 1."


def p_barrel():
    bm = bmesh.new()
    L.box(bm, (0.56, 0.56, 0.045), base_at=(0, 0, 0.0), m=0)          # square ballast base
    L.cyl(bm, 0.272, 0.252, 0.81, 10, 0, base_at=(0, 0, 0.045))       # drum (near-cylindrical)
    L.cyl(bm, 0.279, 0.274, 0.105, 10, 1, base_at=(0, 0, 0.23))       # reflective band low
    L.cyl(bm, 0.266, 0.261, 0.105, 10, 1, base_at=(0, 0, 0.53))       # reflective band high
    L.cyl(bm, 0.258, 0.258, 0.045, 10, 0, base_at=(0, 0, 0.855))      # top rim
    obj = L.new_object("VC_Barrel_Construction", bm, ["VC_Paint_Orange", "VC_Retro_White"])
    cols = [L.collider("UCX_VC_Barrel_Construction", "UCX", radius=0.29, h=0.90, seg=8, base_at=(0, 0, 0))]
    return obj, cols, 2, "0.90 m construction barrel, two reflective bands = slot 1."


PROPS = {
    "hydrant": p_hydrant,
    "parkingmeter": p_parkingmeter,
    "trashcan_civic": p_trashcan_civic,
    "dumpster": p_dumpster,
    "postbox": p_postbox,
    "newsbox": p_newsbox,
    "busshelter": p_busshelter,
    "lamp_cobra_single": p_lamp_cobra_single,
    "lamp_cobra_twin": p_lamp_cobra_twin,
    "lamp_boulevard_twin": p_lamp_boulevard_twin,
    "lamp_ornate_single": p_lamp_ornate_single,
    "lamp_ornate_twin": p_lamp_ornate_twin,
    "signblade": p_signblade,
    "signspeed": p_signspeed,
    "bollard": p_bollard,
    "barrel": p_barrel,
}


# ----------------------------------------------------------------- pipeline

def reset_scene():
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    for b in list(bpy.data.materials):
        bpy.data.materials.remove(b, do_unlink=True)
    for block in (bpy.data.meshes, bpy.data.collections):
        for b in list(block):
            if b.users == 0:
                block.remove(b)
    vc_tools.vc_setup_scene()


def lod_ratios(tris0, levels):
    """Spec ratios 1.0 / 0.40 / 0.10, with a floor so tiny meshes are not destroyed."""
    r1 = min(1.0, max(0.40, 72.0 / max(tris0, 1)))
    r2 = min(1.0, max(0.10, 56.0 / max(tris0, 1)))
    return (1.0, r1) if levels == 2 else (1.0, r1, r2)


def build(key):
    reset_scene()
    obj, cols, levels, note = PROPS[key]()

    bbox = [obj.matrix_world @ Vector(c) for c in obj.bound_box]
    lo = Vector((min(c.x for c in bbox), min(c.y for c in bbox), min(c.z for c in bbox)))
    hi = Vector((max(c.x for c in bbox), max(c.y for c in bbox), max(c.z for c in bbox)))

    uv_ok = L.unwrap(obj)
    rep0 = L.mesh_report(obj)
    ratios = lod_ratios(rep0["tris"], levels)
    parent, lods = vc_tools.vc_make_lods(obj, ratios=ratios)
    for i, lod in enumerate(lods):
        if i:
            L.clean_lod(lod)
            vc_tools.vc_move_to(lod, "LOD%d" % i)
    for c in cols:
        c.parent = parent
        c.matrix_parent_inverse = parent.matrix_world.inverted()

    reports = [L.mesh_report(o) for o in lods]
    name = parent.name
    os.makedirs(EXPORT_DIR, exist_ok=True)
    fbx = os.path.join(EXPORT_DIR, name + ".fbx")
    vc_tools.vc_export_fbx([parent], fbx, mode="prop", pivot=parent, path_mode="STRIP")
    blend = os.path.join(ROOT, name + ".blend")
    bpy.ops.wm.save_as_mainfile(filepath=blend)

    return {
        "key": key,
        "name": name,
        "note": note,
        "blend": os.path.relpath(blend, r"E:\VeyraCity").replace("\\", "/"),
        "fbx": os.path.relpath(fbx, r"E:\VeyraCity").replace("\\", "/"),
        "size_m": [round(hi.x - lo.x, 3), round(hi.y - lo.y, 3), round(hi.z - lo.z, 3)],
        "base_z": round(lo.z, 5),
        "pivot_world": [round(v, 5) for v in parent.matrix_world.translation],
        "xy_center_offset": [round((lo.x + hi.x) / 2, 4), round((lo.y + hi.y) / 2, 4)],
        "uv": uv_ok,
        "lod_ratios": [round(r, 3) for r in ratios],
        "lods": reports,
        "collision": [{"name": c.name, "tris": L.mesh_report(c)["tris"]} for c in cols],
        "slots": reports[0]["slots"],
    }


def main():
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else ["all"]
    keys = list(PROPS) if args[0] == "all" else args
    out = []
    for k in keys:
        out.append(build(k))
    path = os.path.join(HERE, "report.json")
    with open(path, "w", encoding="utf-8") as fh:
        json.dump(out, fh, indent=1)
    print("VC_REPORT_PATH:" + path)


main()

"""VEYRA CITY - facade articulation + rooftop clutter kit (visual gap #5).

    blender --factory-startup -b -noaudio -P build_facade.py -- <key|all>

Gap #5 (Docs/VISUAL_GAP_ANALYSIS.md): "VC towers are flat curtain-wall boxes with no recess. The
reference has projecting signage, awnings, recessed entrances and varied parapet heights." The 88-model
building kit fixed the massing; this kit is the ground-floor articulation that makes a street readable at
driving speed, plus roof silhouettes that are not flat lids.

MODULE GRID - measured from the building kit, not invented. The ground-floor rhythm there is
`int(w / 6.0)` colonnade piers on the tower podiums and 6.0 m shopfront bays on the market-town terraces
(`build_towers.podium`, `build_coast_town.shopfront`). So the facade kit is built on a **6.0 m bay with a
3.0 m half-bay**; upper-facade pier pitches in the kit (4.0 / 4.5 / 5.0 m) are irrelevant here because the
ground-floor kit sits below the first spandrel band.

PRIORITY - reordered by the main session (which is doing night ambient, per-window reveal shading and
floor/column coherence at the same time, so its shading and this geometry should meet in the middle
rather than both faking depth):
  1. window reveal depth - a real recess with head and jamb
  2. floor-level slab and cornice banding
  3. crown / setback pieces for tower tops
  then the ground-floor shopfront kit, then the rooftop clutter.
"Night facades read as flat boxes with lit rectangles painted on" is the problem these three fix: the
tower set carries its floor lines and window grid in the curtain-wall TEXTURE (that is what makes a 240 m
tower cost the same as a 28 m one), so at night there is no geometry for a lit pane to sit behind. These
modules put the recess back where it matters, at a cost that is paid per module placed rather than per
tower.

PIVOT - every wall-mounted piece pivots at the **wall face, centred horizontally, at ground level
(z = 0)**, and carries its own mounting height in its geometry. One anchor for the whole kit: put the
pivot on the building's front face at ground, and an awning lands at 3.3 m and a fascia sign at 4.1 m by
themselves. Free-standing pavement props (A-frame, display plinth) and all rooftop props use the normal
base-centre pivot. The window and banding modules are the one deviation: they repeat UP a facade, so
they anchor at the wall face, centred, at the BOTTOM OF THEIR OWN OPENING / BAND (z = 0 is the sill line
or the band soffit, not the ground). Outward is Blender -Y, so after VC_Unity_Prop the pieces face Unity +Z - the same way
the kit buildings' fronts face.
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
sys.path.insert(0, r"E:\VeyraCity\ArtSource\Blender\Buildings\build")   # vc_bld_lib (same author/session)
import vc_tools                      # noqa: E402
import vc_bld_lib as L               # noqa: E402

EXPORT = os.path.join(ROOT, "export")
BAY, HALF = 6.0, 3.0

#: How far the glazing sits behind the wall face on every window reveal module. ONE constant, used by all
#: three reveals, so the main session's shading work can set it: change this line and re-run
#: `build_facade.py -- window_reveal_single window_reveal_band window_reveal_storey`. 0.26 m is the
#: midpoint of the 0.24-0.30 m range offered; the Unity session has it to confirm.
REVEAL_DEPTH = 0.26

#: The 3 m ribbon module may later go shallower (0.18-0.20 m), because ribbon glazing really is shallower
#: than a punched masonry opening. Optional polish, not a blocker: set this and re-run that one model.
REVEAL_DEPTH_BAND = REVEAL_DEPTH

# One shared material set for all 20 props: few slots, shared textures (BUG-170).
SLOTS = ["VC_Fac_Metal", "VC_Fac_Glass", "VC_Fac_Panel", "VC_Fac_Fabric", "VC_Fac_Blank",
         "VC_Fac_Concrete", "VC_Fac_Emissive"]
MET, GLA, PAN, FAB, BLK, CON, EMI = 0, 1, 2, 3, 4, 5, 6

FAC_MATERIALS = {
    "VC_Fac_Metal":    ((0.265, 0.270, 0.275), 0.90, 0.35, None, 0.0),
    "VC_Fac_Glass":    ((0.085, 0.105, 0.122), 0.00, 0.08, None, 0.0),
    "VC_Fac_Panel":    ((0.330, 0.300, 0.265), 0.00, 0.52, None, 0.0),
    "VC_Fac_Fabric":   ((0.250, 0.135, 0.115), 0.00, 0.78, None, 0.0),
    "VC_Fac_Blank":    ((0.620, 0.615, 0.600), 0.00, 0.40, None, 0.0),
    "VC_Fac_Concrete": ((0.400, 0.394, 0.380), 0.00, 0.68, None, 0.0),
    # BLANK emissive: crown light coves and lit sign faces. No artwork, no lettering - Unity drives
    # _EMISSION and the colour, same pattern as VC_Lamp_Emissive and VC_Neon_Emissive.
    "VC_Fac_Emissive": ((0.600, 0.580, 0.520), 0.00, 0.22, (1.00, 0.92, 0.76), 5.0),
}
L.MATERIALS.update(FAC_MATERIALS)


def wedge_box(bm, size, loc, m=0, top_shift_y=0.0, top_scale_x=1.0, top_scale_y=1.0):
    """Box whose TOP face is shifted/scaled - a tapered awning or hood. Local to this kit: vc_bld_lib
    has no wedge_box (that one lives in the street set's library, which this kit does not import)."""
    faces = L.box(bm, size, loc=loc, m=m)
    verts = {v for f in faces for v in f.verts}
    ztop = max(v.co.z for v in verts)
    cx = sum(v.co.x for v in verts) / len(verts)
    cy = sum(v.co.y for v in verts) / len(verts)
    for v in verts:
        if abs(v.co.z - ztop) < 1e-6:
            v.co.x = cx + (v.co.x - cx) * top_scale_x
            v.co.y = cy + (v.co.y - cy) * top_scale_y + top_shift_y
    return faces


# ------------------------------------------------------------------ PART 0 (priority): reveal + banding
# Wall face at y = 0, street at -Y. These modules anchor at the bottom of their own opening or band.

def _reveal(bm, w, hh, lod, depth=REVEAL_DEPTH, mullions=0, transom=0.0):
    """A real window recess: head, two jambs and a sill, with the glass set `depth` behind the wall face.
    This is the geometry the night shading needs - a lit pane set back, with a head that casts onto it."""
    jam, hd, sl = 0.13, 0.16, 0.10
    L.box(bm, (w, depth, hd), base_at=(0, -depth / 2.0, hh - hd), m=PAN)              # head / lintel
    L.box(bm, (w, depth + 0.07, sl), base_at=(0, -depth / 2.0 - 0.035, 0.0), m=CON)   # sill, with a drip
    for sx in (-1, 1):                                                                 # jambs
        L.box(bm, (jam, depth, hh - hd - sl), base_at=(sx * (w / 2.0 - jam / 2.0), -depth / 2.0, sl),
              m=PAN)
    gw, gh = w - 2 * jam, hh - hd - sl
    L.box(bm, (gw, 0.05, gh), loc=(0, -depth + 0.03, sl + gh / 2.0), m=GLA)           # recessed glazing
    L.box(bm, (gw, 0.07, 0.07), loc=(0, -depth + 0.05, sl + 0.04), m=MET)             # frame bottom
    L.box(bm, (gw, 0.07, 0.07), loc=(0, -depth + 0.05, sl + gh - 0.04), m=MET)
    if transom > 0.0 and lod < 2:
        L.box(bm, (gw, 0.08, 0.08), loc=(0, -depth + 0.05, sl + transom), m=MET)
    if lod < 2:
        for i in range(1, mullions + 1):
            x = -w / 2.0 + w * i / (mullions + 1)
            L.box(bm, (0.07, 0.08, gh), base_at=(x, -depth + 0.05, sl), m=MET)
    if lod == 0:
        L.box(bm, (w + 0.10, 0.06, 0.05), loc=(0, -depth - 0.05, 0.025), m=CON)       # sill nosing
    return hh


def window_reveal_single(lod=0):
    """1.5 x 1.6 m punched window, reveal = REVEAL_DEPTH - the repeating unit for a punched facade."""
    bm = bmesh.new()
    hh = _reveal(bm, 1.50, 1.60, lod, mullions=1)
    return bm, (1.60, REVEAL_DEPTH + 0.07, hh), "window"


def window_reveal_band(lod=0):
    """3.0 m ribbon window, four lights, reveal = REVEAL_DEPTH - the half-bay horizontal module."""
    bm = bmesh.new()
    hh = _reveal(bm, HALF, 1.45, lod, depth=REVEAL_DEPTH_BAND, mullions=3)
    return bm, (HALF + 0.10, REVEAL_DEPTH_BAND + 0.05, hh), "window"


def window_reveal_storey(lod=0):
    """1.5 x 3.3 m storey-height bay with a transom, reveal = REVEAL_DEPTH - curtain-wall frontages."""
    bm = bmesh.new()
    hh = _reveal(bm, 1.50, 3.30, lod, mullions=1, transom=2.35)
    return bm, (1.60, REVEAL_DEPTH + 0.07, hh), "window"


def slab_band(lod=0):
    """Floor-line slab edge: the horizontal that makes a facade read as storeys rather than a box."""
    bm = bmesh.new()
    L.box(bm, (BAY, 0.20, 0.42), base_at=(0, -0.10, 0.0), m=CON)
    if lod < 2:
        L.box(bm, (BAY, 0.26, 0.09), base_at=(0, -0.13, 0.33), m=CON)                 # projecting lip
        L.box(bm, (BAY, 0.05, 0.05), loc=(0, -0.245, 0.30), m=CON)                    # drip groove edge
    return bm, (BAY, 0.28, 0.42), "band"


def cornice_band(lod=0):
    """A profiled cornice module - a deeper, moulded horizontal for the top of a base or a parapet line."""
    bm = bmesh.new()
    pts = [(0.0, 0.0), (-0.16, 0.10), (-0.20, 0.26), (-0.34, 0.40), (-0.34, 0.52),
           (-0.10, 0.66), (0.0, 0.66)]
    L.profile_solid(bm, pts, -BAY / 2.0, BAY / 2.0, CON)
    if lod < 2:
        L.box(bm, (BAY, 0.09, 0.07), loc=(0, -0.30, 0.46), m=CON)                     # bed mould
    return bm, (BAY, 0.36, 0.66), "band"


def sill_band(lod=0):
    """A thin string course / continuous sill - the cheapest horizontal, for mid-facade coherence."""
    bm = bmesh.new()
    L.box(bm, (BAY, 0.14, 0.16), base_at=(0, -0.07, 0.0), m=CON)
    if lod < 2:
        L.box(bm, (BAY, 0.19, 0.05), base_at=(0, -0.095, 0.11), m=CON)
    return bm, (BAY, 0.20, 0.16), "band"


def crown_setback(lod=0):
    """Tower crown: a parapet, a setback step and a coping, as a 6 m module along a roof edge.
    Stops a tower top reading as a flat lid, which is the other half of gap #5."""
    bm = bmesh.new()
    L.box(bm, (BAY, 0.52, 1.30), base_at=(0, -0.26, 0.0), m=PAN)                      # parapet upstand
    L.box(bm, (BAY, 0.66, 0.16), base_at=(0, -0.33, 1.30), m=CON)                     # coping
    L.box(bm, (BAY, 0.80, 0.70), base_at=(0, -0.10, 1.46), m=PAN)                     # setback step
    L.box(bm, (BAY, 0.92, 0.14), base_at=(0, -0.16, 2.16), m=CON)
    if lod < 2:
        L.box(bm, (BAY, 0.10, 0.26), loc=(0, -0.56, 1.00), m=EMI)                     # BLANK light cove
        L.box(bm, (BAY, 0.14, 0.10), loc=(0, -0.58, 1.17), m=MET)                     # cove shield
    return bm, (BAY, 0.92, 2.30), "band"


def crown_fin(lod=0):
    """A vertical crown fin / pilaster rising past the parapet - varies a roof line at 60 triangles."""
    bm = bmesh.new()
    L.box(bm, (0.70, 0.46, 3.40), base_at=(0, -0.23, 0.0), m=PAN)
    L.box(bm, (0.86, 0.58, 0.18), base_at=(0, -0.29, 3.40), m=CON)                    # cap
    if lod < 2:
        L.box(bm, (0.26, 0.10, 2.60), loc=(0, -0.50, 1.70), m=EMI)                    # BLANK lit reveal
        L.box(bm, (0.44, 0.12, 0.12), loc=(0, -0.50, 3.16), m=MET)
    return bm, (0.86, 0.58, 3.58), "band"


# ------------------------------------------------------------------ PART 1: ground floor
# Wall face at y = 0; the street is -Y, the building interior +Y.

def entry_recess(lod=0):
    """A recessed entry read from the street: side returns and a soffit project toward the street, so the
    door plane sits ~1.0 m deep. Needs no opening in the host wall, which is solid at ground level."""
    bm = bmesh.new()
    w, dep, hh = HALF, 1.05, 3.20
    for sx in (-1, 1):                                                   # side returns
        L.box(bm, (0.22, dep, hh), base_at=(sx * (w / 2.0 - 0.11), -dep / 2.0, 0.0), m=PAN)
    L.box(bm, (w, dep + 0.10, 0.26), base_at=(0, -dep / 2.0, hh), m=PAN)          # soffit / head
    L.box(bm, (w - 0.44, dep, 0.04), base_at=(0, -dep / 2.0, 0.0), m=CON)         # recess floor
    L.box(bm, (1.90, 0.10, 2.35), loc=(0, -0.06, 1.175), m=GLA)                   # glazed door pair
    L.box(bm, (0.10, 0.12, 2.35), loc=(0, -0.08, 1.175), m=MET)                   # meeting stile
    L.box(bm, (2.20, 0.14, 0.55), loc=(0, -0.07, 2.70), m=GLA)                    # transom
    L.box(bm, (2.34, 0.16, 0.12), loc=(0, -0.08, 2.40), m=MET)                    # transom rail
    if lod < 2:
        for sx in (-1, 1):
            L.box(bm, (0.14, 0.16, 2.90), loc=(sx * 1.17, -0.08, 1.45), m=MET)    # jambs
        L.box(bm, (0.06, 0.06, 1.05), loc=(0.78, -0.16, 1.05), m=MET)             # pull handle
    return bm, (w, dep, hh + 0.26), "wall"


def _shopfront(bm, w, lod, mullions):
    """Stall riser, glazing, transom and head - the parts that give a shopfront its depth."""
    hh, riser, trans = 3.20, 0.62, 2.45
    L.box(bm, (w, 0.16, riser), base_at=(0, -0.08, 0.0), m=PAN)                   # stall riser
    L.box(bm, (w - 0.24, 0.07, trans - riser - 0.10), loc=(0, -0.10, (riser + trans) / 2.0), m=GLA)
    L.box(bm, (w - 0.24, 0.07, hh - trans - 0.26), loc=(0, -0.10, (trans + hh - 0.26) / 2.0 + 0.04),
          m=GLA)                                                                   # transom light
    L.box(bm, (w, 0.18, 0.12), loc=(0, -0.09, trans + 0.06), m=MET)               # transom rail
    L.box(bm, (w, 0.22, 0.26), base_at=(0, -0.11, hh), m=PAN)                     # head beam
    for sx in (-1, 1):
        L.box(bm, (0.12, 0.20, hh), base_at=(sx * (w / 2.0 - 0.06), -0.10, 0.0), m=MET)
    if lod < 2:
        for i in range(1, mullions + 1):
            x = -w / 2.0 + w * i / (mullions + 1)
            L.box(bm, (0.09, 0.19, hh - riser), base_at=(x, -0.10, riser), m=MET)
    if lod == 0:
        L.box(bm, (w - 0.3, 0.05, 0.06), loc=(0, -0.145, riser + 0.03), m=MET)    # riser capping
    return hh


def shopfront_3m(lod=0):
    bm = bmesh.new()
    hh = _shopfront(bm, HALF, lod, 1)
    return bm, (HALF, 0.26, hh + 0.26), "wall"


def shopfront_6m(lod=0):
    bm = bmesh.new()
    hh = _shopfront(bm, BAY, lod, 3)
    return bm, (BAY, 0.26, hh + 0.26), "wall"


def grille_closed(lod=0):
    """Roll-up security grille, shut: slatted curtain in its guide rails (reference VC_22)."""
    bm = bmesh.new()
    w, hh = HALF, 3.05
    L.box(bm, (w - 0.18, 0.09, hh - 0.10), base_at=(0, -0.055, 0.0), m=MET)
    if lod < 2:
        n = 10 if lod == 0 else 5
        for i in range(n):
            L.box(bm, (w - 0.22, 0.05, (hh - 0.3) / n * 0.46),
                  base_at=(0, -0.115, 0.12 + (hh - 0.3) * i / n), m=MET)          # slat relief
    for sx in (-1, 1):
        L.box(bm, (0.12, 0.17, hh), base_at=(sx * (w / 2.0 - 0.06), -0.085, 0.0), m=MET)
    L.box(bm, (w, 0.30, 0.34), base_at=(0, -0.15, hh), m=PAN)                      # drum housing
    return bm, (w, 0.30, hh + 0.34), "wall"


def grille_open(lod=0):
    """The same grille rolled up: the drum is visible under the housing, the opening is clear."""
    bm = bmesh.new()
    w, hh = HALF, 3.05
    for sx in (-1, 1):
        L.box(bm, (0.12, 0.17, hh), base_at=(sx * (w / 2.0 - 0.06), -0.085, 0.0), m=MET)
    L.cyl(bm, 0.20, 0.20, w - 0.26, 10 if lod == 0 else 8, MET,
          loc=(0, -0.16, hh - 0.08), rot=Matrix.Rotation(math.radians(90), 4, "Y"))
    L.box(bm, (w, 0.34, 0.14), base_at=(0, -0.17, hh + 0.12), m=PAN)               # hood over the drum
    if lod < 2:
        L.box(bm, (w - 0.30, 0.06, 0.10), loc=(0, -0.16, hh - 0.30), m=MET)        # bottom bar, raised
    return bm, (w, 0.34, hh + 0.26), "wall"


def awning_fabric(lod=0):
    """Fabric awning on folding arms, with a valance - the strongest ground-floor shadow in VC_17."""
    bm = bmesh.new()
    w, proj, z0 = HALF, 1.45, 3.30
    seg = 4 if lod == 0 else (3 if lod == 1 else 2)
    pts = [(0.0, z0 + 0.42)]
    for i in range(1, seg + 1):               # from 1: i = 0 repeats the point above -> zero-area faces
        t = i / seg
        pts.append((-proj * t, z0 + 0.42 - 0.52 * t - 0.10 * math.sin(math.pi * t)))
    pts.append((-proj, z0 - 0.42))
    pts.append((0.0, z0 + 0.30))
    L.profile_solid(bm, pts, -w / 2.0, w / 2.0, FAB)
    L.box(bm, (w, 0.05, 0.34), loc=(0, -proj + 0.02, z0 - 0.26), m=FAB)            # valance
    L.box(bm, (w, 0.12, 0.14), loc=(0, -0.05, z0 + 0.46), m=MET)                   # wall rail
    if lod < 2:
        for sx in (-1, 1):
            L.box(bm, (0.07, proj, 0.07), loc=(sx * (w / 2.0 - 0.12), -proj / 2.0, z0 + 0.14), m=MET)
    return bm, (w, proj, z0 + 0.60), "wall"


def awning_box(lod=0):
    """Rigid box awning - the flat-soffit alternative, reads as a different shop."""
    bm = bmesh.new()
    w, proj, z0 = HALF, 1.15, 3.30
    wedge_box(bm, (w, proj, 0.70), loc=(0, -proj / 2.0, z0 + 0.35), m=PAN,
              top_shift_y=0.16, top_scale_y=0.72)
    L.box(bm, (w, 0.10, 0.80), loc=(0, -0.05, z0 + 0.40), m=MET)                   # wall plate
    if lod < 2:
        L.box(bm, (w + 0.08, 0.10, 0.22), loc=(0, -proj + 0.03, z0 + 0.14), m=MET)  # front edge
        for sx in (-1, 1):
            L.box(bm, (0.08, proj * 0.8, 0.10), loc=(sx * (w / 2.0 - 0.1), -proj * 0.45, z0 + 0.06),
                  m=MET)
    return bm, (w, proj, z0 + 0.70), "wall"


def sign_projecting(lod=0):
    """Wall-mounted blank sign box, perpendicular to the facade (reference VC_12 layering).
    Both faces are BLANK on their own material slot - Veyra's own artwork is applied in Unity."""
    bm = bmesh.new()
    z0 = 3.85
    L.box(bm, (0.16, 0.16, 0.90), loc=(0, -0.10, z0 + 0.10), m=MET)                # wall plate
    L.box(bm, (0.10, 0.52, 0.10), loc=(0, -0.36, z0 + 0.42), m=MET)                # bracket arm
    L.box(bm, (0.10, 0.52, 0.10), loc=(0, -0.36, z0 - 0.26), m=MET)
    L.box(bm, (0.14, 1.05, 0.95), loc=(0, -0.90, z0 + 0.08), m=MET)                # sign body
    for sx in (-1, 1):
        L.box(bm, (0.03, 0.92, 0.82), loc=(sx * 0.085, -0.90, z0 + 0.08), m=BLK)   # BLANK faces
    if lod < 2:
        L.box(bm, (0.06, 0.06, 0.62), loc=(0, -0.42, z0 + 0.08), m=MET)            # diagonal stay
    return bm, (0.18, 1.45, z0 + 0.56), "wall"


def sign_fascia(lod=0):
    """Flush fascia sign panel over a shopfront head. BLANK."""
    bm = bmesh.new()
    w, z0 = HALF, 3.56
    L.box(bm, (w, 0.14, 0.62), loc=(0, -0.07, z0 + 0.31), m=PAN)                   # backing box
    L.box(bm, (w - 0.24, 0.06, 0.46), loc=(0, -0.155, z0 + 0.31), m=BLK)           # BLANK face
    if lod < 2:
        L.box(bm, (w, 0.20, 0.08), loc=(0, -0.09, z0 + 0.66), m=MET)               # drip / top trim
        L.box(bm, (w, 0.18, 0.07), loc=(0, -0.09, z0 - 0.01), m=MET)
    return bm, (w, 0.22, z0 + 0.70), "wall"


def a_frame(lod=0):
    """Pavement A-frame board. Free-standing: base-centre pivot, not the wall anchor. BLANK faces."""
    bm = bmesh.new()
    for sy in (-1, 1):
        rot = Matrix.Rotation(math.radians(sy * 8.0), 4, "X")
        L.box(bm, (0.70, 0.05, 1.00), loc=(0, sy * 0.17, 0.55), rot=rot, m=PAN)
        L.box(bm, (0.60, 0.03, 0.84), loc=(0, sy * 0.205, 0.55), rot=rot, m=BLK)   # BLANK face
    L.box(bm, (0.74, 0.46, 0.05), base_at=(0, 0, 0.0), m=MET)                       # foot
    if lod < 2:
        L.box(bm, (0.68, 0.06, 0.06), loc=(0, 0, 1.06), m=MET)                      # hinge bar
    return bm, (0.74, 0.46, 1.10), "ground"


def display_plinth(lod=0):
    """Stall / display plinth for a shop frontage."""
    bm = bmesh.new()
    L.box(bm, (1.60, 0.80, 0.78), base_at=(0, 0, 0.0), m=PAN)
    L.box(bm, (1.72, 0.92, 0.07), base_at=(0, 0, 0.78), m=CON)
    if lod < 2:
        L.box(bm, (1.48, 0.68, 0.06), base_at=(0, 0, 0.40), m=MET)                  # mid shelf
        L.box(bm, (1.66, 0.86, 0.05), base_at=(0, 0, 0.07), m=CON)                  # plinth kick
    return bm, (1.72, 0.92, 0.85), "ground"


# ------------------------------------------------------------------ PART 2: rooftop clutter

def hvac_small(lod=0):
    bm = bmesh.new()
    L.box(bm, (1.25, 0.95, 0.72), base_at=(0, 0, 0.12), m=PAN)
    L.box(bm, (1.35, 1.05, 0.12), base_at=(0, 0, 0.0), m=MET)                       # sleeper rails
    L.cyl(bm, 0.34, 0.34, 0.10, 10 if lod == 0 else 8, MET, base_at=(0, 0, 0.84))   # fan guard
    if lod < 2:
        L.box(bm, (0.95, 0.05, 0.42), loc=(0, -0.50, 0.46), m=MET)                  # louvre face
        L.box(bm, (0.18, 0.12, 0.26), loc=(0.48, 0.52, 0.40), m=MET)                # pipe stub
    return bm, (1.35, 1.05, 0.94), "ground"


def hvac_large(lod=0):
    bm = bmesh.new()
    L.box(bm, (2.60, 1.60, 1.35), base_at=(0, 0, 0.16), m=PAN)
    L.box(bm, (2.76, 1.76, 0.16), base_at=(0, 0, 0.0), m=MET)
    for sx in (-1, 1):
        L.cyl(bm, 0.42, 0.42, 0.12, 10 if lod == 0 else 8, MET, base_at=(sx * 0.62, 0, 1.51))
    if lod < 2:
        L.box(bm, (2.20, 0.06, 0.78), loc=(0, -0.83, 0.80), m=MET)                  # louvre bank
        L.box(bm, (0.70, 0.08, 0.95), loc=(0.80, 0.83, 0.70), m=MET)                # access panel
        L.box(bm, (0.26, 0.26, 0.55), base_at=(-1.05, 0.55, 1.51), m=MET)           # control box
    return bm, (2.76, 1.76, 2.06), "ground"


def fan_cowl(lod=0):
    """Extract fan cowl on a kerb - the small round silhouette that breaks a flat roof."""
    bm = bmesh.new()
    seg = 12 if lod == 0 else (10 if lod == 1 else 8)
    L.box(bm, (0.80, 0.80, 0.26), base_at=(0, 0, 0.0), m=CON)                       # upstand kerb
    L.cyl(bm, 0.34, 0.34, 0.42, seg, MET, base_at=(0, 0, 0.26))
    L.cyl(bm, 0.46, 0.30, 0.30, seg, MET, base_at=(0, 0, 0.68))                     # cowl
    L.cyl(bm, 0.30, 0.30, 0.06, seg, MET, base_at=(0, 0, 0.98))
    if lod < 2:
        L.cyl(bm, 0.50, 0.50, 0.05, seg, MET, base_at=(0, 0, 0.60))                 # drip ring
    return bm, (0.92, 0.92, 1.04), "ground"


def duct_run(lod=0):
    """A 4 m insulated duct run on sleeper supports, with flanged joints."""
    bm = bmesh.new()
    length = 4.0
    L.box(bm, (0.62, length, 0.52), loc=(0, 0, 0.72), m=MET)
    for t in (-0.34, 0.0, 0.34):
        L.box(bm, (0.70, 0.07, 0.60), loc=(0, length * t, 0.72), m=MET)             # flanges
    for t in (-0.36, 0.36):
        L.box(bm, (0.72, 0.22, 0.46), base_at=(0, length * t, 0.0), m=CON)          # sleepers
    if lod < 2:
        L.box(bm, (0.26, 0.26, 0.40), base_at=(0, length * 0.46, 0.98), m=MET)      # riser stub
    return bm, (0.72, length + 0.3, 0.98), "ground"


def water_tank(lod=0):
    """Cylindrical water tank on a four-leg stand with a ladder - a classic roof silhouette."""
    bm = bmesh.new()
    seg = 12 if lod == 0 else (10 if lod == 1 else 8)
    r, legh = 1.05, 1.60
    for sx in (-1, 1):
        for sy in (-1, 1):
            L.box(bm, (0.13, 0.13, legh), base_at=(sx * 0.72, sy * 0.72, 0.0), m=MET)
    L.box(bm, (1.90, 1.90, 0.12), base_at=(0, 0, legh), m=MET)
    L.cyl(bm, r, r, 1.70, seg, PAN, base_at=(0, 0, legh + 0.12))
    L.cyl(bm, r * 1.02, r * 0.35, 0.34, seg, PAN, base_at=(0, 0, legh + 1.82))      # domed top
    if lod < 2:
        for k in (0.30, 0.70):
            L.cyl(bm, r + 0.05, r + 0.05, 0.08, seg, MET, base_at=(0, 0, legh + 0.12 + 1.70 * k))
        L.box(bm, (0.06, 0.40, legh + 1.6), base_at=(r + 0.16, 0, 0.0), m=MET)      # ladder stringers
        L.cyl(bm, 0.09, 0.09, 1.30, 6, MET, base_at=(-r + 0.2, -r + 0.2, 0.0))      # downpipe
    if lod == 0:
        for i in range(7):
            L.box(bm, (0.34, 0.05, 0.05), loc=(r + 0.16, 0, 0.30 + i * 0.26), m=MET)
    return bm, (2.20, 2.20, legh + 2.16), "ground"


def stair_overrun(lod=0):
    """Lift / stair overrun box - the thing that makes a roof read as inhabited."""
    bm = bmesh.new()
    w, d, hh = 3.20, 2.60, 2.55
    L.box(bm, (w, d, hh), base_at=(0, 0, 0.0), m=PAN)
    L.box(bm, (w + 0.16, d + 0.16, 0.14), base_at=(0, 0, hh), m=CON)                # coping
    if lod < 2:
        L.box(bm, (0.95, 0.10, 2.05), loc=(0, -d / 2.0 + 0.04, 1.03), m=MET)        # door
        L.box(bm, (0.70, 0.08, 0.30), loc=(0.95, -d / 2.0 + 0.04, 2.15), m=MET)     # louvre
    return bm, (w + 0.16, d + 0.16, hh + 0.14), "ground"


def parapet_cap(lod=0):
    """3 m parapet coping module, so a roof edge can be varied instead of a flat lid."""
    bm = bmesh.new()
    pts = [(-0.26, 0.0), (-0.26, 0.30), (-0.30, 0.34), (-0.30, 0.40), (0.30, 0.40),
           (0.30, 0.34), (0.26, 0.30), (0.26, 0.0)]
    L.profile_solid(bm, pts, -HALF / 2.0, HALF / 2.0, CON)
    if lod < 2:
        L.box(bm, (HALF, 0.56, 0.04), loc=(0, 0, 0.42), m=CON)                       # flush flashing
    return bm, (HALF, 0.60, 0.46), "band"


def antenna_cluster(lod=0):
    """Mast with cross arms, whips and a small drum - roof-line interest at almost no cost."""
    bm = bmesh.new()
    L.box(bm, (0.52, 0.52, 0.16), base_at=(0, 0, 0.0), m=CON)                        # ballast base
    L.cyl(bm, 0.07, 0.055, 3.40, 6, MET, base_at=(0, 0, 0.16))
    for z in (2.05, 2.65):
        L.box(bm, (1.10, 0.06, 0.06), loc=(0, 0, z), m=MET)
    if lod < 2:
        for sx in (-1, 1):
            L.cyl(bm, 0.022, 0.012, 0.95, 4, MET, base_at=(sx * 0.50, 0, 2.65))      # whips
        L.cyl(bm, 0.16, 0.16, 0.30, 8, MET, loc=(0.0, 0.22, 3.05),
              rot=Matrix.Rotation(math.radians(90), 4, "X"))                          # drum
    if lod == 0:
        for sx in (-1, 1):
            L.box(bm, (0.05, 0.05, 0.70), loc=(sx * 0.18, 0, 0.60),
                  rot=Matrix.Rotation(math.radians(sx * 12), 4, "Y"), m=MET)          # stays
    return bm, (1.10, 0.52, 3.56), "ground"


def roof_hatch(lod=0):
    """Roof access hatch, lid propped open."""
    bm = bmesh.new()
    L.box(bm, (1.10, 0.90, 0.26), base_at=(0, 0, 0.0), m=CON)                        # upstand
    L.box(bm, (1.16, 0.96, 0.06), base_at=(0, 0, 0.26), m=MET)                       # frame
    rot = Matrix.Rotation(math.radians(-58), 4, "X")
    L.box(bm, (1.12, 0.92, 0.05), loc=(0, 0.30, 0.65), rot=rot, m=MET)               # open lid
    if lod < 2:
        L.box(bm, (0.06, 0.34, 0.06), loc=(0.40, 0.14, 0.44), m=MET)                 # stay
    return bm, (1.16, 1.30, 0.95), "ground"


SPECS = [
    # key, fn, part, collider (None = none).  Part 0 first: the main session's priority order.
    ("window_reveal_single", window_reveal_single, 0, None),
    ("window_reveal_band",   window_reveal_band,   0, None),
    ("window_reveal_storey", window_reveal_storey, 0, None),
    ("slab_band",            slab_band,            0, None),
    ("cornice_band",         cornice_band,         0, None),
    ("sill_band",            sill_band,            0, None),
    ("crown_setback",        crown_setback,        0, None),
    ("crown_fin",            crown_fin,            0, None),
    ("entry_recess",   entry_recess,   1, "UBX"),
    ("shopfront_3m",   shopfront_3m,   1, "UBX"),
    ("shopfront_6m",   shopfront_6m,   1, "UBX"),
    ("grille_closed",  grille_closed,  1, "UBX"),
    ("grille_open",    grille_open,    1, "UBX"),
    ("awning_fabric",  awning_fabric,  1, None),
    ("awning_box",     awning_box,     1, None),
    ("sign_projecting", sign_projecting, 1, None),
    ("sign_fascia",    sign_fascia,    1, None),
    ("a_frame",        a_frame,        1, "UBX"),
    ("display_plinth", display_plinth, 1, "UBX"),
    ("hvac_small",     hvac_small,     2, "UBX"),
    ("hvac_large",     hvac_large,     2, "UBX"),
    ("fan_cowl",       fan_cowl,       2, "UBX"),
    ("duct_run",       duct_run,       2, "UBX"),
    ("water_tank",     water_tank,     2, "UCX"),
    ("stair_overrun",  stair_overrun,  2, "UBX"),
    ("parapet_cap",    parapet_cap,    2, None),
    ("antenna_cluster", antenna_cluster, 2, None),
    ("roof_hatch",     roof_hatch,     2, None),
]
#: part 0 window/band pieces and part 1 shopfront pieces live on a facade; crowns and rooftop on a roof.
PREFIX = {0: "VC_Fac_", 1: "VC_Fac_", 2: "VC_Roof_"}
for _k in ("crown_setback", "crown_fin"):
    pass   # crowns keep the VC_Fac_ prefix: they are a facade/parapet module, not roof clutter
NAMES = {k: PREFIX[p] + "".join(x.capitalize() for x in k.split("_")) for k, _, p, _ in SPECS}


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
    _, fn, part, coltype = next(s for s in SPECS if s[0] == key)
    reset()
    # CLAUDE.md: props under 2 m ship LOD0 + LOD1; larger meshes get three levels.
    probe_bm, size, anchor = fn(0)
    probe_bm.free()
    levels = (0, 1) if max(size) < 2.0 else (0, 1, 2)
    lods, uv = [], True
    for lod in levels:
        L.set_detail(lod)
        bm, size, anchor = fn(lod)
        o = L.new_object("%s_LOD%d" % (NAMES[key], lod), bm, SLOTS)
        uv = L.unwrap(o, margin=0.01, cube=1.0) and uv       # 1 m per UV tile, like the Infra set
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
    cols = []
    if coltype:
        cx, cy = (lo.x + hi.x) / 2.0, (lo.y + hi.y) / 2.0
        if coltype == "UBX":
            c = L.collider_box("UBX_" + parent.name,
                               (hi.x - lo.x, hi.y - lo.y, hi.z - lo.z), base_at=(cx, cy, lo.z))
        else:
            bmc = bmesh.new()
            L.cyl(bmc, (hi.x - lo.x) / 2.0 * 0.95, (hi.x - lo.x) / 2.0 * 0.95, hi.z - lo.z, 8,
                  base_at=(cx, cy, lo.z))
            bmesh.ops.recalc_face_normals(bmc, faces=bmc.faces)
            me = bpy.data.meshes.new("UCX_" + parent.name)
            bmc.to_mesh(me)
            bmc.free()
            c = bpy.data.objects.new("UCX_" + parent.name, me)
            bpy.context.scene.collection.objects.link(c)
        c.parent = parent
        c.matrix_parent_inverse = parent.matrix_world.inverted()
        cols.append(c)

    os.makedirs(EXPORT, exist_ok=True)
    fbx = os.path.join(EXPORT, parent.name + ".fbx")
    vc_tools.vc_export_fbx([parent], fbx, mode="prop", pivot=parent, path_mode="STRIP")
    blend = os.path.join(ROOT, parent.name + ".blend")
    bpy.ops.wm.save_as_mainfile(filepath=blend)
    return {
        "key": key, "name": parent.name, "part": part,
        "pivot": {"wall": "wall face, centred, z = 0 at ground",
                  "window": "wall face, centred, z = 0 at the opening sill",
                  "band": "wall face, centred, z = 0 at the band soffit",
                  "ground": "base centre, z = 0"}[anchor],
        "anchor": anchor,
        "blend": os.path.relpath(blend, r"E:\VeyraCity").replace("\\", "/"),
        "fbx": os.path.relpath(fbx, r"E:\VeyraCity").replace("\\", "/"),
        "size_m": [round(hi.x - lo.x, 3), round(hi.y - lo.y, 3), round(hi.z - lo.z, 3)],
        "mount_z_m": round(lo.z, 3),
        "levels": len(levels), "uv": uv,
        "lods": [L.mesh_report(o) for o in lods],
        "collision": [c.name for c in cols], "collision_type": coltype,
        "slots": rep0["slots"],
    }


def main():
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else ["all"]
    keys = [s[0] for s in SPECS] if args[0] == "all" else args
    out = [build(k) for k in keys]
    path = os.path.join(HERE, "report_facade.json")
    old = []
    if os.path.exists(path) and args[0] != "all":
        old = [r for r in json.load(open(path)) if r["key"] not in keys]
    with open(path, "w", encoding="utf-8") as fh:
        json.dump(old + out, fh, indent=1)
    print("VC_FACADE_OK:" + path)


main()

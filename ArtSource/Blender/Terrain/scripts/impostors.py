"""Far-tree impostor meshes (BUG-117).

CHOICE: a 2-card cross billboard with per-card baked views and a real baked normal map, NOT an
octahedral impostor. Reasons, in order:
  1. An octahedral impostor needs a custom Unity shader to pick and blend the three nearest views
     from the hemi-octahedron grid. That is a Unity-side dependency this session cannot write,
     test or even compile (no editor access), and shipping an atlas nobody can sample is worth
     nothing.
  2. The cross billboard drops into the EXISTING URP Lit alpha-clip setup with no new shader at
     all, so it can be used the day it lands.
  3. The thing BUG-117 is actually about is alpha-tested overdraw from distant trees. The win comes
     from replacing a ~600-triangle card cloud with 4 triangles of compact silhouette; it does not
     require view-dependent parallax.
What is given up is honest and is written into the MANIFEST: no parallax, and a visible narrowing
at 45 degrees of azimuth between the two baked views.

Each card carries its own baked view (0 deg and 90 deg), so two of the four cardinal directions are
exactly right rather than approximated.
"""
import json
import os

from mathutils import Vector

from terrain_lib import MAT_IMPOSTOR, Builder, TEX, card

_TABLE_PATH = os.path.join(TEX, "impostor_uvs.json")
if not os.path.exists(_TABLE_PATH):
    raise RuntimeError(
        "impostor atlas table missing: %s\n"
        "Run scripts/impostor_bake.py before scripts/build_terrain.py - the impostor meshes are "
        "sized and UV'd from the baked atlas." % _TABLE_PATH)
with open(_TABLE_PATH) as _f:
    TABLE = json.load(_f)

SPECIES = ["Redwood_46m", "Broadleaf_Spreading", "Pine_Tall", "Palm_Royal_16m", "Spruce_Alpine",
           "Palm_Fan_Tall"]

# Screen-relative height at which the impostor takes over. A LOWER h swaps FURTHER out, i.e. keeps
# the real mesh for longer. Forest species are seen en masse at distance and can swap early;
# street-side species are driven past at close range by a moving camera, where a 2-card cross reads
# flat and visibly turns, so they keep their mesh roughly twice as far out.
SWITCH_H = {
    "Redwood_46m": 0.25,
    "Pine_Tall": 0.25,
    "Spruce_Alpine": 0.25,
    "Broadleaf_Spreading": 0.11,
    "Palm_Royal_16m": 0.11,
    "Palm_Fan_Tall": 0.11,
}
STREET = {"Broadleaf_Spreading", "Palm_Royal_16m", "Palm_Fan_Tall"}


def tile(name, view):
    return TABLE["%s_v%d" % (name, view)]


def impostor(name, lod):
    """LOD0: two crossed cards, one per baked view (4 tris).
    LOD1/LOD2: a single card on the 0 deg view (2 tris). The impostor has two meaningful levels;
    LOD2 repeats LOD1 so the asset keeps the project's three-level convention and the LODGroup has
    a band to cull from."""
    b = Builder()
    t0 = tile(name, 0)
    t1 = tile(name, 1)
    # the card is the rendered FRAME, centred where the bake camera was aimed, so the tree inside
    # lands at exactly its real size and height
    w0, h0 = t0["frame_m"]
    w1, _ = t1["frame_m"]
    cz = t0["centre_z"]

    # card A: spans X-Z, faces -Y  (this is the view the 0 deg bake was taken from)
    card(b, Vector((0, 0, cz)), Vector((1, 0, 0)), Vector((0, 0, 1)),
         w0, h0, tuple(t0["uv"]), Vector((0, -1, 0)), mat=MAT_IMPOSTOR)
    if lod == 0:
        # card B: spans Y-Z, faces +X. The 90 deg bake camera stands at +X looking along -X,
        # so its toward-camera axis is +X and the baked tangent-space normal expects that sign.
        card(b, Vector((0, 0, cz)), Vector((0, 1, 0)), Vector((0, 0, 1)),
             w1, h0, tuple(t1["uv"]), Vector((1, 0, 0)), mat=MAT_IMPOSTOR)
        # card C: horizontal, at the canopy centroid, facing +Z. Without it the crown collapses to
        # slivers when the camera looks down - the clifftop roads in D4 and D8.
        t2 = TABLE.get("%s_top" % name)
        if t2 is not None:
            tw, th = t2["frame_m"]
            card(b, Vector((0, 0, t2["canopy_z"])), Vector((1, 0, 0)), Vector((0, 1, 0)),
                 tw, th, tuple(t2["uv"]), Vector((0, 0, 1)), mat=MAT_IMPOSTOR)
    return b


def size_of(name):
    """The CARD's extents. Once the impostor sits in the tree's LODGroup, the group's bounds are
    the card, so this is the height Unity's screen-relative maths actually uses. It is within 2 %
    of the LOD0 mesh (the 1.02 framing pad). Note the species names are design heights: the
    measured LOD0 geometry of `Redwood_46m` is 45.5 m, not 46.0."""
    t0 = tile(name, 0)
    t1 = tile(name, 1)
    return max(t0["frame_m"][0], t1["frame_m"][0]), t0["frame_m"][1]


# ---------------------------------------------------------------- switch distances
# Unity LODGroup screen-relative height h = H / (d * 2 * tan(vfov/2)).
# At 1440p with a 60 deg vertical FOV, 2*tan(30) = 1.1547, so d = H / (1.1547 * h).
TAN = 1.1547
# The texel-limited minimum: below this distance the impostor is being magnified.
# screen_px = (res_y / (2*tan(vfov/2))) * H / d = 1247 * H / d at 1440p.
PX_CONST = 1440.0 / TAN


def recommend(name):
    _, h_m = size_of(name)
    tile_h = tile(name, 0)["h"]
    d_min = PX_CONST * h_m / tile_h                 # 1 texel == 1 screen pixel
    h_switch = SWITCH_H[name]
    d_switch = h_m / (TAN * h_switch)
    floored = d_switch < d_min
    if floored:
        # never swap nearer than the texel limit, whatever the art direction wants
        d_switch = d_min
        h_switch = h_m / (TAN * d_switch)
    return dict(height_m=round(h_m, 2), tree_height_m=round(tile(name, 0)["size_m"][1], 2),
                tile_px=tile_h,
                d_texel_min_m=round(d_min, 1), d_switch_m=round(d_switch, 1),
                screen_height_switch=round(h_switch, 4),
                margin=round(d_switch / d_min, 2),
                texel_floor_binds=floored,
                role="street" if name in STREET else "forest")

"""Build T_Veyra_Foliage_Atlas_B (2048), the second and last foliage atlas.

Only tiles that set 1 genuinely cannot provide live here: alpine and redwood conifer sprays, the
industrial-neglect weed vocabulary, dry/dead grass, and the LOD2 billboards for the new silhouettes.
The BARK atlas is NOT duplicated - set 2 shares set 1's bark atlas, so the two sets together are
3 materials (foliage A, foliage B, bark).
"""
import math
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
SET1_SCRIPTS = os.path.join(os.path.dirname(ROOT), "Vegetation", "scripts")
sys.path.insert(0, SET1_SCRIPTS)

import numpy as np  # noqa: E402

from atlas_paint import Canvas, RNG, jitter, leaf, needle, write_png  # noqa: E402
from make_atlas import _cluster, _sprig, _trunk, build  # noqa: E402

OUT = os.path.join(ROOT, "textures")

# ------------------------------------------------------------------ palette (linear albedo)
C_SPRUCE = (0.034, 0.072, 0.052)       # dark blue-green
C_SPRUCE_LT = (0.058, 0.108, 0.078)
C_REDWOOD = (0.058, 0.102, 0.044)
C_REDWOOD_LT = (0.092, 0.150, 0.062)
C_DEAD = (0.215, 0.180, 0.088)         # straw
C_DEAD_DK = (0.135, 0.108, 0.052)
C_ROCK = (0.105, 0.118, 0.070)         # wiry alpine tuft
C_WEED = (0.090, 0.128, 0.044)
C_WEED_LT = (0.148, 0.172, 0.062)
C_WEED_YEL = (0.175, 0.165, 0.055)
C_SAPLING = (0.098, 0.178, 0.058)
C_MTN = (0.072, 0.095, 0.052)
C_TWIG = (0.075, 0.060, 0.042)
C_DEADTWIG = (0.095, 0.082, 0.062)


# ------------------------------------------------------------------ cluster tiles
def t_spruce_spray(cv):
    """Downswept spruce/fir shoot: short stiff needles all round the shoot, dense and dark."""
    W, H = cv.w, cv.h
    bx, by = W * 0.04, H * 0.30
    tx, ty = W * 0.97, H * 0.62      # droops as it goes out
    needle(cv, bx, by, tx, ty, 8.0, (0.090, 0.066, 0.046), rough=0.84, taper=True)
    n = 54
    for i in range(n):
        t = (i + 0.5) / n
        px = bx + (tx - bx) * t
        py = by + (ty - by) * t + H * 0.10 * (t ** 2.2)
        ln = H * 0.21 * (1.0 - 0.40 * t)
        for s in (-1, 1):
            for k in range(9):
                a = math.atan2(ty - by, tx - bx) + s * math.radians(36 + k * 10 + RNG.uniform(-7, 7))
                L = ln * RNG.uniform(0.58, 1.0)
                needle(cv, px, py, px + math.cos(a) * L, py + math.sin(a) * L,
                       4.2, jitter(C_SPRUCE if k % 2 else C_SPRUCE_LT, 0.17), rough=0.70, taper=True)
    # a few side shoots so the tile is not one straight line
    for s in (-1, 1):
        for j in (0.34, 0.66):
            sx, sy = bx + (tx - bx) * j, by + (ty - by) * j
            a = math.atan2(ty - by, tx - bx) + s * math.radians(34)
            ex, ey = sx + math.cos(a) * W * 0.22, sy + math.sin(a) * W * 0.22
            needle(cv, sx, sy, ex, ey, 5.0, (0.090, 0.066, 0.046), rough=0.84)
            for k in range(14):
                tt = (k + 0.5) / 14
                qx, qy = sx + (ex - sx) * tt, sy + (ey - sy) * tt
                for s2 in (-1, 1):
                    a2 = a + s2 * math.radians(RNG.uniform(45, 80))
                    L = H * 0.13 * RNG.uniform(0.6, 1.0)
                    needle(cv, qx, qy, qx + math.cos(a2) * L, qy + math.sin(a2) * L,
                           3.0, jitter(C_SPRUCE, 0.18), rough=0.70, taper=True)


def t_redwood_spray(cv):
    """Flat two-ranked redwood spray: awl leaves in a plane, so the frond reads flat and feathery."""
    W, H = cv.w, cv.h
    bx, by = W * 0.03, H * 0.50
    tx, ty = W * 0.98, H * 0.50
    needle(cv, bx, by, tx, ty, 6.5, (0.085, 0.062, 0.042), rough=0.84, taper=True)
    rank = 30
    for i in range(rank):
        t = (i + 0.5) / rank
        px, py = bx + (tx - bx) * t, by + (ty - by) * t
        # side branchlets, each carrying its own two ranks of awl leaves
        for s in (-1, 1):
            a = s * math.radians(72 - 18 * t)
            bl = H * 0.34 * (1.0 - 0.52 * t) * RNG.uniform(0.82, 1.0)
            ex, ey = px + math.cos(a) * bl, py + math.sin(a) * bl
            needle(cv, px, py, ex, ey, 3.2, (0.085, 0.062, 0.042), rough=0.82, taper=True)
            k = 9
            for j in range(k):
                tt = (j + 0.5) / k
                qx, qy = px + (ex - px) * tt, py + (ey - py) * tt
                for s2 in (-1, 1):
                    a2 = a + s2 * math.radians(58)
                    L = bl * 0.30 * (1.0 - 0.4 * tt)
                    leaf(cv, qx + math.cos(a2) * L * 0.5, qy + math.sin(a2) * L * 0.5,
                         L, L * 0.30, a2,
                         jitter(C_REDWOOD if j % 2 else C_REDWOOD_LT, 0.16), rough=0.66,
                         vein=0.7, soft=0.9)


def t_dead_grass(cv):
    """Dry straw clump: collapsed blades, bleached. D3 verges and D8 dry season."""
    W, H = cv.w, cv.h
    for _ in range(90):
        a = math.radians(RNG.uniform(-158, -22))
        L = H * RNG.uniform(0.35, 0.95)
        bend = RNG.uniform(-0.8, 0.8)          # dead grass folds over much more than live grass
        x0, y0 = W * RNG.uniform(0.30, 0.70), H * 0.995
        mx, my = x0 + math.cos(a) * L * 0.5, y0 + math.sin(a) * L * 0.5
        ex, ey = mx + math.cos(a + bend) * L * 0.55, my + math.sin(a + bend) * L * 0.55
        c = jitter(C_DEAD if RNG.random() < 0.7 else C_DEAD_DK, 0.26)
        needle(cv, x0, y0, mx, my, RNG.uniform(2.8, 4.6), c, rough=0.86, taper=False)
        needle(cv, mx, my, ex, ey, RNG.uniform(1.8, 3.2), c * 0.9, rough=0.86)


def t_rock_tuft(cv):
    """Short wiry alpine tuft for rock ledges: stiff, spiky, grey-green."""
    W, H = cv.w, cv.h
    for _ in range(76):
        a = math.radians(RNG.uniform(-150, -30))
        L = H * RNG.uniform(0.30, 0.68)
        x0, y0 = W * RNG.uniform(0.38, 0.62), H * 0.995
        c = jitter(C_ROCK, 0.24)
        needle(cv, x0, y0, x0 + math.cos(a) * L, y0 + math.sin(a) * L,
               RNG.uniform(2.2, 3.8), c, rough=0.84, taper=True)
    for _ in range(16):   # a few dead straws in the tuft
        a = math.radians(RNG.uniform(-140, -40))
        L = H * RNG.uniform(0.25, 0.55)
        needle(cv, W * 0.5, H * 0.995, W * 0.5 + math.cos(a) * L, H * 0.995 + math.sin(a) * L,
               RNG.uniform(1.6, 2.6), jitter(C_DEAD_DK, 0.25), rough=0.88, taper=True)


def t_weed_tall(cv):
    """Ruderal weed: woody stems, narrow leaves, seed heads. The stuff that colonises a fence line."""
    W, H = cv.w, cv.h
    for st in range(7):
        x0 = W * RNG.uniform(0.22, 0.78)
        a = math.radians(RNG.uniform(-112, -68))
        L = H * RNG.uniform(0.58, 0.96)
        ex, ey = x0 + math.cos(a) * L, H * 0.995 + math.sin(a) * L
        needle(cv, x0, H * 0.995, ex, ey, RNG.uniform(3.0, 4.6),
               jitter(C_WEED_LT, 0.2), rough=0.80, taper=True)
        k = 7
        for j in range(k):
            t = (j + 0.6) / (k + 0.4)
            qx, qy = x0 + (ex - x0) * t, H * 0.995 + (ey - H * 0.995) * t
            for s in (-1, 1):
                a2 = a + s * math.radians(RNG.uniform(38, 74))
                ln = W * 0.14 * (1.0 - 0.45 * t) * RNG.uniform(0.7, 1.1)
                leaf(cv, qx + math.cos(a2) * ln * 0.5, qy + math.sin(a2) * ln * 0.5,
                     ln, ln * 0.26, a2, jitter(C_WEED, 0.18), rough=0.74, vein=0.7)
        # seed head
        for _ in range(16):
            a3 = RNG.uniform(0, 2 * math.pi)
            rr = W * RNG.uniform(0.0, 0.045)
            needle(cv, ex, ey, ex + math.cos(a3) * rr, ey + math.sin(a3) * rr,
                   2.0, jitter(C_WEED_YEL, 0.22), rough=0.84)


def t_weed_broadleaf(cv):
    """Dock / plantain rosette: the broad-leaved weed that comes up through a concrete crack."""
    W, H = cv.w, cv.h
    cx, cy = W * 0.5, H * 0.92
    for i in range(34):
        a = math.radians(-176 + i * 172 / 33.0) + math.radians(RNG.uniform(-7, 7))
        ln = H * RNG.uniform(0.34, 0.86)
        col = jitter(C_WEED if i % 3 else C_WEED_LT, 0.20)
        leaf(cv, cx + math.cos(a) * ln * 0.5, cy + math.sin(a) * ln * 0.5,
             ln, ln * RNG.uniform(0.17, 0.27), a, col, rough=0.70, vein=0.85,
             curl=RNG.uniform(-0.14, 0.14))
    for i in range(9):   # yellowing outer leaves
        a = math.radians(-170 + i * 164 / 8.0)
        ln = H * RNG.uniform(0.26, 0.54)
        leaf(cv, cx + math.cos(a) * ln * 0.5, cy + math.sin(a) * ln * 0.5,
             ln, ln * RNG.uniform(0.16, 0.24), a, jitter(C_WEED_YEL, 0.22), rough=0.78, vein=0.8)


def t_sapling_leaf(cv):
    """Pinnate compound leaf of a volunteer sapling (tree-of-heaven character): long rachis,
    paired leaflets, thin and sparse - a self-seeded tree in a yard is never lush."""
    W, H = cv.w, cv.h
    bx, by = W * 0.5, H * 0.99
    tx, ty = W * 0.5 + W * 0.10, H * 0.04
    needle(cv, bx, by, tx, ty, 4.2, (0.115, 0.098, 0.055), rough=0.80, taper=True)
    k = 11
    for j in range(k):
        t = (j + 0.4) / (k + 0.2)
        qx, qy = bx + (tx - bx) * t, by + (ty - by) * t
        ln = W * 0.30 * math.sin(math.pi * min(1.0, 0.18 + t * 0.9)) ** 0.5
        for s in (-1, 1):
            a = math.radians(-90) + s * math.radians(74 - 16 * t)
            leaf(cv, qx + math.cos(a) * ln * 0.5, qy + math.sin(a) * ln * 0.5,
                 ln, ln * 0.30, a, jitter(C_SAPLING, 0.17), rough=0.66, vein=0.75,
                 curl=RNG.uniform(-0.1, 0.1))


def t_mountain_scrub(cv):
    """Low alpine scrub: tiny tough leaves, grey-green, dead twigs showing through."""
    W, H = cv.w, cv.h
    for _ in range(11):
        a = RNG.uniform(0, 2 * math.pi)
        needle(cv, W * 0.5, H * 0.90, W * 0.5 + math.cos(a) * W * 0.40,
               H * 0.90 + math.sin(a) * H * 0.38, RNG.uniform(2.2, 3.8),
               jitter(C_DEADTWIG, 0.2), rough=0.88, taper=True)
    _cluster(cv, W * 0.5, H * 0.56, W * 0.46, H * 0.40, 170, C_MTN, W * 0.040, W * 0.072, 0.72,
             twigs=False, wfrac=0.70)
    _cluster(cv, W * 0.5, H * 0.48, W * 0.30, H * 0.24, 55, C_ROCK, W * 0.036, W * 0.064, 0.74,
             twigs=False, wfrac=0.70)


# ------------------------------------------------------------------ LOD2 billboards
def bb_spruce(cv):
    """Narrow alpine spruce: steep cone, downswept branches, spire top, bare lower trunk."""
    W, H = cv.w, cv.h
    _trunk(cv, W * 0.50, H * 1.0, W * 0.50, H * 0.02, W * 0.055, (0.080, 0.060, 0.044))
    whorls = 15
    for i in range(whorls):
        t = i / (whorls - 1.0)
        y = H * (0.90 - 0.86 * t)
        half = W * 0.46 * (1.0 - t) ** 0.70 + W * 0.020
        for s in (-1, 1):
            for j in range(2):
                cx = W * 0.5 + s * half * RNG.uniform(0.20, 0.96)
                _cluster(cv, cx, y + H * RNG.uniform(-0.006, 0.014), half * 0.28, H * 0.016, 16,
                         C_SPRUCE, W * 0.075, W * 0.135, 0.70, twigs=False, wfrac=0.26)


def bb_fir(cv):
    """Broader fir: fuller cone, blunter top, foliage down to a third of the height."""
    W, H = cv.w, cv.h
    _trunk(cv, W * 0.50, H * 1.0, W * 0.50, H * 0.06, W * 0.070, (0.080, 0.060, 0.044))
    whorls = 12
    for i in range(whorls):
        t = i / (whorls - 1.0)
        y = H * (0.94 - 0.84 * t)
        half = W * 0.49 * (1.0 - t) ** 0.52 + W * 0.030
        for s in (-1, 1):
            for j in range(3):
                cx = W * 0.5 + s * half * RNG.uniform(0.16, 0.98)
                _cluster(cv, cx, y + H * RNG.uniform(-0.010, 0.016), half * 0.30, H * 0.020, 18,
                         C_SPRUCE if j % 2 else C_SPRUCE_LT, W * 0.080, W * 0.145, 0.70,
                         twigs=False, wfrac=0.26)


def bb_redwood(cv):
    """The D8 signature silhouette, and the one that is read from kilometres away: a massive
    buttressed trunk, no branches for the lower half, then a narrow irregular column of foliage."""
    W, H = cv.w, cv.h
    # buttressed trunk, widest at the base
    for i in range(26):
        t = i / 25.0
        y0 = H * (1.0 - t * 0.995)
        y1 = H * (1.0 - (t + 1 / 25.0) * 0.995)
        w = W * (0.165 * (1.0 - t) ** 1.25 + 0.042)
        needle(cv, W * 0.5, y0, W * 0.5, y1, w * 2.0, (0.112, 0.062, 0.044), rough=0.88,
               taper=False)
    whorls = 20
    for i in range(whorls):
        t = i / (whorls - 1.0)
        y = H * (0.52 - 0.50 * t)
        half = W * 0.40 * (1.0 - t) ** 0.55 + W * 0.055
        for s in (-1, 1):
            for j in range(2):
                cx = W * 0.5 + s * half * RNG.uniform(0.22, 1.0)
                _cluster(cv, cx, y + H * RNG.uniform(-0.008, 0.012), half * 0.34, H * 0.013, 16,
                         C_REDWOOD if j % 2 else C_REDWOOD_LT, W * 0.085, W * 0.150, 0.68,
                         twigs=False, wfrac=0.30)
    # a couple of big low limbs, which is what gives a redwood its distinctive outline
    for s in (-1, 1):
        y = H * RNG.uniform(0.50, 0.58)
        needle(cv, W * 0.5, y, W * 0.5 + s * W * 0.34, y - H * 0.055, W * 0.030,
               (0.108, 0.060, 0.042), rough=0.88, taper=True)
        _cluster(cv, W * 0.5 + s * W * 0.30, y - H * 0.055, W * 0.14, H * 0.020, 22,
                 C_REDWOOD, W * 0.085, W * 0.150, 0.68, twigs=False, wfrac=0.30)


def bb_sapling(cv):
    """Scrappy self-seeded sapling: thin leaning stem, a handful of compound leaves."""
    W, H = cv.w, cv.h
    _trunk(cv, W * 0.46, H * 1.0, W * 0.56, H * 0.22, W * 0.045, (0.105, 0.088, 0.058))
    for i in range(9):
        a = RNG.uniform(0, 2 * math.pi)
        cx = W * 0.52 + math.cos(a) * W * RNG.uniform(0.05, 0.40)
        cy = H * (0.42 + RNG.uniform(-0.22, 0.14))
        _cluster(cv, cx, cy, W * 0.20, H * 0.075, 26, C_SAPLING, W * 0.085, W * 0.150, 0.66,
                 twigs=False, wfrac=0.30)


def bb_mtn_scrub(cv):
    """Low wide alpine scrub dome, painted low in the slot so the LOD2 card is not stretched."""
    W, H = cv.w, cv.h
    _cluster(cv, W * 0.5, H * 0.90, W * 0.48, H * 0.085, 150, C_MTN, W * 0.050, W * 0.090, 0.72,
             wfrac=0.6)
    _cluster(cv, W * 0.5, H * 0.865, W * 0.32, H * 0.050, 55, C_ROCK, W * 0.045, W * 0.080, 0.74,
             twigs=False, wfrac=0.6)


def bb_weed(cv):
    """Tall ruderal weed clump."""
    W, H = cv.w, cv.h
    for _ in range(9):
        x0 = W * RNG.uniform(0.2, 0.8)
        a = math.radians(RNG.uniform(-110, -70))
        L = H * RNG.uniform(0.16, 0.30)
        needle(cv, x0, H * 0.995, x0 + math.cos(a) * L, H * 0.995 + math.sin(a) * L,
               3.0, jitter(C_WEED_LT, 0.2), rough=0.80, taper=True)
    _cluster(cv, W * 0.5, H * 0.875, W * 0.47, H * 0.105, 130, C_WEED, W * 0.055, W * 0.100, 0.72,
             twigs=False, wfrac=0.35)
    _cluster(cv, W * 0.5, H * 0.830, W * 0.30, H * 0.055, 40, C_WEED_YEL, W * 0.045, W * 0.085,
             0.78, twigs=False, wfrac=0.35)


def bb_dead_grass(cv):
    """Low straw clump."""
    W, H = cv.w, cv.h
    for _ in range(110):
        a = math.radians(RNG.uniform(-150, -30))
        L = H * RNG.uniform(0.05, 0.135)
        x0 = W * RNG.uniform(0.18, 0.82)
        bend = RNG.uniform(-0.7, 0.7)
        mx, my = x0 + math.cos(a) * L * 0.55, H * 0.995 + math.sin(a) * L * 0.55
        needle(cv, x0, H * 0.995, mx, my, 3.0, jitter(C_DEAD, 0.25), rough=0.86, taper=False)
        needle(cv, mx, my, mx + math.cos(a + bend) * L * 0.5, my + math.sin(a + bend) * L * 0.5,
               2.2, jitter(C_DEAD_DK, 0.25), rough=0.86)


def bb_ground_patch(cv):
    """Very low, very wide mixed ground patch: the LOD2 for the scatter modules."""
    W, H = cv.w, cv.h
    _cluster(cv, W * 0.5, H * 0.945, W * 0.49, H * 0.050, 150, C_WEED, W * 0.045, W * 0.085, 0.74,
             twigs=False, wfrac=0.45)
    for _ in range(70):
        a = math.radians(RNG.uniform(-150, -30))
        L = H * RNG.uniform(0.03, 0.075)
        x0 = W * RNG.uniform(0.1, 0.9)
        needle(cv, x0, H * 0.995, x0 + math.cos(a) * L, H * 0.995 + math.sin(a) * L,
               2.6, jitter(C_DEAD, 0.25), rough=0.86, taper=True)


# ------------------------------------------------------------------ layout
TILES_B = [
    ("spruce_spray", 0, 0, 512, 512, t_spruce_spray),
    ("redwood_spray", 512, 0, 1024, 512, t_redwood_spray),
    ("dead_grass", 1024, 0, 1536, 512, t_dead_grass),
    ("rock_tuft", 1536, 0, 2048, 512, t_rock_tuft),
    ("weed_tall", 0, 512, 512, 1024, t_weed_tall),
    ("weed_broadleaf", 512, 512, 1024, 1024, t_weed_broadleaf),
    ("sapling_leaf", 1024, 512, 1536, 1024, t_sapling_leaf),
    ("mountain_scrub", 1536, 512, 2048, 1024, t_mountain_scrub),
    # LOD2 billboards, 256 x 1024 slots (tall trees need the height; low shapes are painted at the
    # bottom of their slot and the tight-bbox packer crops them to the right aspect)
    ("bb_spruce", 0, 1024, 256, 2048, bb_spruce),
    ("bb_fir", 256, 1024, 512, 2048, bb_fir),
    ("bb_redwood", 512, 1024, 768, 2048, bb_redwood),
    ("bb_sapling", 768, 1024, 1024, 2048, bb_sapling),
    ("bb_mtn_scrub", 1024, 1024, 1280, 2048, bb_mtn_scrub),
    ("bb_weed", 1280, 1024, 1536, 2048, bb_weed),
    ("bb_dead_grass", 1536, 1024, 1792, 2048, bb_dead_grass),
    ("bb_ground_patch", 1792, 1024, 2048, 2048, bb_ground_patch),
]


if __name__ == "__main__":
    import json
    import make_atlas
    os.makedirs(OUT, exist_ok=True)
    make_atlas.OUT = OUT          # reuse set 1's build()/dilate()/normal_from_height() verbatim
    u = build(2048, TILES_B, "T_Veyra_Foliage_Atlas_B", True)
    with open(os.path.join(OUT, "atlas_uvs.json"), "w") as f:
        json.dump({"foliage": u}, f, indent=1)
    print("atlas B done:", len(u), "tiles")

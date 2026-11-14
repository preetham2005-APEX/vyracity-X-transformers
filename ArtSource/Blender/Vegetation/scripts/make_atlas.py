"""Build T_Veyra_Foliage_Atlas (2048) and T_Veyra_Bark_Atlas (1024).

Each atlas writes BaseColor (+alpha), Normal and MetallicSmoothness PNGs.
ONE foliage atlas for the whole vegetation set, so every tree, hedge and shrub in Veyra City
shares a single alpha-clip material; one bark atlas for every trunk.
"""
import math
import os
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from atlas_paint import OUT, Canvas, RNG, jitter, leaf, needle, srgb, write_png

# ------------------------------------------------------------------ palette (linear albedo)
C_PALM = (0.150, 0.235, 0.070)
C_PALM_LT = (0.225, 0.330, 0.105)
C_SKIRT = (0.200, 0.140, 0.062)
C_BROAD = (0.088, 0.160, 0.050)
C_BROAD_LT = (0.145, 0.235, 0.072)
C_CYP = (0.050, 0.090, 0.040)
C_PINE = (0.062, 0.115, 0.062)
C_BOX = (0.064, 0.118, 0.038)
C_SCRUB = (0.150, 0.155, 0.070)
C_SCRUB2 = (0.105, 0.145, 0.058)
C_GRASS = (0.185, 0.175, 0.080)
C_TWIG = (0.075, 0.060, 0.042)


# ------------------------------------------------------------------ tile painters
def t_palm_royal_frond(cv):
    """Pinnate royal-palm frond: arching rachis, leaflets sweeping back, drooping tips."""
    W, H = cv.w, cv.h
    n = 46
    bx, by = W * 0.5, H * 0.99
    pts = []
    for i in range(n + 1):
        t = i / n
        x = bx + (W * 0.17) * t * t
        y = by - (H * 0.95) * (t * (1.0 - 0.18 * t))
        pts.append((x, y))
    for i in range(n):
        t = i / n
        x, y = pts[i]
        x2, y2 = pts[i + 1]
        ang = math.atan2(y2 - y, x2 - x)
        ln = (W * 0.300) * math.sin(math.pi * min(1.0, 0.10 + t * 0.95)) ** 0.75 * (1.0 - 0.25 * t)
        if ln < 4:
            continue
        for side in (-1, 1):
            sweep = math.radians(58 + 26 * t) * side
            a = ang + sweep
            droop = 0.22 + 0.5 * t
            col = jitter(C_PALM if (i + (side > 0)) % 2 else C_PALM_LT, 0.14)
            ex = x + math.cos(a) * ln * 0.5
            ey = y + math.sin(a) * ln * 0.5 + droop * ln * 0.18
            leaf(cv, ex, ey, ln, max(3.2, ln * 0.085), a, col, rough=0.56,
                 curl=0.10 * side, vein=0.75, soft=0.9)
    for i in range(n):
        needle(cv, pts[i][0], pts[i][1], pts[i + 1][0], pts[i + 1][1],
               max(2.0, 9.0 * (1 - i / n)), (0.16, 0.16, 0.085), rough=0.6, taper=False)


def t_palm_fan_frond(cv):
    """Costapalmate fan: segments radiating from a hub, split tips, stout petiole."""
    W, H = cv.w, cv.h
    bx, by = W * 0.5, H * 0.985
    hub_y = H * 0.70
    needle(cv, bx, by, bx, hub_y + 10, 13.0, (0.19, 0.17, 0.09), rough=0.66, taper=False)
    n = 27
    for i in range(n):
        t = i / (n - 1)
        a = math.radians(-180 + 12 + t * 156)
        ln = (H * 0.60) * (0.74 + 0.26 * math.sin(math.pi * t)) * (1.0 - 0.10 * abs(t - 0.5) * 2)
        col = jitter(C_PALM_LT if i % 3 else C_PALM, 0.13)
        ex = bx + math.cos(a) * ln * 0.5
        ey = hub_y + math.sin(a) * ln * 0.5
        wdt = max(5.0, ln * 0.115)
        leaf(cv, ex, ey, ln, wdt, a, col, rough=0.58, vein=0.8, soft=1.0)
        tipx, tipy = bx + math.cos(a) * ln, hub_y + math.sin(a) * ln
        for s in (-1, 1):
            a2 = a + math.radians(7 * s)
            needle(cv, tipx - math.cos(a) * ln * 0.18, tipy - math.sin(a) * ln * 0.18,
                   tipx + math.cos(a2) * ln * 0.10, tipy + math.sin(a2) * ln * 0.10,
                   wdt * 0.42, col * 0.9, rough=0.6)


def t_palm_skirt(cv):
    """Dead hanging skirt frond: collapsed, brown, ragged."""
    W, H = cv.w, cv.h
    bx, by = W * 0.5, H * 0.04
    n = 22
    for i in range(n):
        t = i / (n - 1)
        a = math.radians(55 + t * 70)
        ln = (H * 0.84) * (0.62 + 0.38 * math.sin(math.pi * t)) * RNG.uniform(0.82, 1.0)
        col = jitter(C_SKIRT, 0.22)
        ex = bx + math.cos(a) * ln * 0.5
        ey = by + math.sin(a) * ln * 0.5
        leaf(cv, ex, ey, ln, max(5.0, ln * 0.10), a, col, rough=0.80, vein=0.9, soft=1.4)
    for i in range(70):
        t = RNG.uniform(0, 1)
        a = math.radians(55 + t * 70) + math.radians(RNG.uniform(-6, 6))
        ln = H * RNG.uniform(0.35, 0.92)
        needle(cv, bx, by, bx + math.cos(a) * ln, by + math.sin(a) * ln,
               RNG.uniform(1.5, 3.2), jitter(C_SKIRT, 0.3), rough=0.85)


def _cluster(cv, cx, cy, rx, ry, count, col, lmin, lmax, rough=0.60, twigs=True, wfrac=0.45):
    if twigs:
        for _ in range(max(3, count // 9)):
            a = RNG.uniform(0, 2 * math.pi)
            needle(cv, cx, cy, cx + math.cos(a) * rx * 0.9, cy + math.sin(a) * ry * 0.9,
                   RNG.uniform(2.0, 4.0), C_TWIG, rough=0.85, taper=True)
    for _ in range(count):
        a = RNG.uniform(0, 2 * math.pi)
        r = math.sqrt(RNG.uniform(0, 1))
        px, py = cx + math.cos(a) * rx * r, cy + math.sin(a) * ry * r
        ln = RNG.uniform(lmin, lmax)
        shade = 1.0 - 0.38 * (1.0 - r) + RNG.uniform(-0.08, 0.08)
        leaf(cv, px, py, ln, ln * wfrac, RNG.uniform(0, 2 * math.pi),
             np.clip(jitter(col, 0.16) * shade, 0, 1), rough=rough,
             curl=RNG.uniform(-0.12, 0.12), vein=0.6)


def t_broadleaf_a(cv):
    W, H = cv.w, cv.h
    _cluster(cv, W * 0.5, H * 0.52, W * 0.42, H * 0.40, 150, C_BROAD, W * 0.085, W * 0.145, 0.52)
    _cluster(cv, W * 0.5, H * 0.40, W * 0.30, H * 0.26, 60, C_BROAD_LT, W * 0.075, W * 0.125, 0.48)


def t_broadleaf_b(cv):
    W, H = cv.w, cv.h
    _cluster(cv, W * 0.5, H * 0.56, W * 0.44, H * 0.36, 120, C_BROAD, W * 0.095, W * 0.16, 0.52)
    _cluster(cv, W * 0.35, H * 0.35, W * 0.22, H * 0.20, 45, C_BROAD_LT, W * 0.08, W * 0.13, 0.48)


def _sprig(cv, x, y, ang, ln, depth, col, w0):
    if depth == 0 or ln < 6:
        return
    ex, ey = x + math.cos(ang) * ln, y + math.sin(ang) * ln
    needle(cv, x, y, ex, ey, w0, col, rough=0.6, taper=True)
    k = max(3, int(ln / 14))
    for i in range(1, k + 1):
        t = i / (k + 1.0)
        px, py = x + (ex - x) * t, y + (ey - y) * t
        for s in (-1, 1):
            _sprig(cv, px, py, ang + math.radians(RNG.uniform(34, 52)) * s,
                   ln * (0.46 - 0.18 * t), depth - 1, jitter(col, 0.13), max(1.2, w0 * 0.55))


def t_cypress_sprig(cv):
    """Feathery scale-leaf sprig; reads as a dense dark flake at distance."""
    W, H = cv.w, cv.h
    for i in range(3):
        _sprig(cv, W * (0.30 + 0.20 * i), H * 0.99, math.radians(-90 + RNG.uniform(-12, 12)),
               H * RNG.uniform(0.62, 0.80), 3, C_CYP, 5.2)


def t_pine_spray(cv):
    """Needle spray: a woody shoot with fascicles of long needles."""
    W, H = cv.w, cv.h
    bx, by = W * 0.03, H * 0.52
    tx, ty = W * 0.97, H * 0.46
    needle(cv, bx, by, tx, ty, 9.0, (0.105, 0.075, 0.050), rough=0.82, taper=True)
    n = 34
    for i in range(n):
        t = (i + 0.5) / n
        px, py = bx + (tx - bx) * t, by + (ty - by) * t
        ln = H * 0.40 * (1.0 - 0.52 * t)
        for s in (-1, 1):
            for k in range(6):
                a = math.atan2(ty - by, tx - bx) + s * math.radians(28 + k * 12 + RNG.uniform(-6, 6))
                L = ln * RNG.uniform(0.78, 1.0)
                needle(cv, px, py, px + math.cos(a) * L, py + math.sin(a) * L,
                       3.4, jitter(C_PINE, 0.16), rough=0.52, taper=True)


def _rect_scatter(cv, count, col, lmin, lmax, rough, wfrac, over=0.12):
    """Scatter leaves across the WHOLE tile, overflowing the edges, so the tile is full bleed and
    neighbouring cells of a hedge shell butt together with no see-through seam."""
    W, H = cv.w, cv.h
    for _ in range(count):
        px = RNG.uniform(-over * W, (1 + over) * W)
        py = RNG.uniform(-over * H, (1 + over) * H)
        ln = RNG.uniform(lmin, lmax)
        leaf(cv, px, py, ln, ln * wfrac, RNG.uniform(0, 2 * math.pi),
             np.clip(jitter(col, 0.17) * RNG.uniform(0.72, 1.12), 0, 1), rough=rough,
             curl=RNG.uniform(-0.12, 0.12), vein=0.6)


def t_boxwood(cv):
    """Dense small-leaf mat for clipped hedges. Full bleed: alpha covers the whole tile."""
    W, H = cv.w, cv.h
    cv.rgb[:] = np.asarray(C_BOX, np.float32) * 0.42
    cv.a[:] = 1.0
    cv.ht[:] = 0.3
    cv.rg[:] = 0.60
    _rect_scatter(cv, 520, C_BOX, W * 0.055, W * 0.095, 0.56, 0.62)
    _rect_scatter(cv, 200, C_BROAD_LT, W * 0.05, W * 0.085, 0.52, 0.62)


def t_scrub_a(cv):
    W, H = cv.w, cv.h
    for _ in range(9):
        a = RNG.uniform(math.radians(-150), math.radians(-30))
        L = H * RNG.uniform(0.35, 0.62)
        needle(cv, W * 0.5, H * 0.99, W * 0.5 + math.cos(a) * L, H * 0.99 + math.sin(a) * L,
               RNG.uniform(3.5, 6.0), C_TWIG, rough=0.88)
    _cluster(cv, W * 0.5, H * 0.52, W * 0.44, H * 0.40, 95, C_SCRUB, W * 0.055, W * 0.10, 0.70,
             twigs=False, wfrac=0.34)


def t_scrub_b(cv):
    W, H = cv.w, cv.h
    _cluster(cv, W * 0.5, H * 0.58, W * 0.46, H * 0.36, 140, C_SCRUB2, W * 0.05, W * 0.09, 0.64,
             wfrac=0.52)


def t_grass_tuft(cv):
    W, H = cv.w, cv.h
    for _ in range(70):
        a = math.radians(RNG.uniform(-150, -30))
        L = H * RNG.uniform(0.40, 0.92)
        bend = RNG.uniform(-0.35, 0.35)
        x0, y0 = W * RNG.uniform(0.35, 0.65), H * 0.995
        mx, my = x0 + math.cos(a) * L * 0.55, y0 + math.sin(a) * L * 0.55
        ex, ey = mx + math.cos(a + bend) * L * 0.5, my + math.sin(a + bend) * L * 0.5
        c = jitter(C_GRASS, 0.25)
        needle(cv, x0, y0, mx, my, RNG.uniform(3.0, 5.0), c, rough=0.74, taper=False)
        needle(cv, mx, my, ex, ey, RNG.uniform(2.0, 3.4), c * 0.92, rough=0.74)


def t_leaf_small(cv):
    W, H = cv.w, cv.h
    _cluster(cv, W * 0.5, H * 0.5, W * 0.45, H * 0.45, 90, C_BROAD_LT, W * 0.07, W * 0.12, 0.56)


# ------------------------------------------------------------------ billboards (LOD2 silhouettes)
def _trunk(cv, x0, y0, x1, y1, w, col=(0.085, 0.075, 0.062)):
    needle(cv, x0, y0, x1, y1, w, col, rough=0.88, taper=False)


def bb_royal(cv):
    W, H = cv.w, cv.h
    _trunk(cv, W * 0.50, H * 1.0, W * 0.52, H * 0.20, W * 0.105, (0.135, 0.120, 0.098))
    cx, cy = W * 0.52, H * 0.175
    for i in range(16):
        a = math.radians(-180 + 8 + i * 164 / 15.0)
        L = H * 0.235 * RNG.uniform(0.78, 1.0)
        ex, ey = cx + math.cos(a) * L * 0.5, cy + math.sin(a) * L * 0.5 + L * 0.26
        leaf(cv, ex, ey, L, L * 0.26, a, jitter(C_PALM, 0.15), rough=0.58, vein=0.5, soft=2.0)


def bb_fan(cv):
    W, H = cv.w, cv.h
    _trunk(cv, W * 0.50, H * 1.0, W * 0.50, H * 0.46, W * 0.20, (0.120, 0.100, 0.075))
    for i in range(16):
        a = math.radians(-180 + 6 + i * 168 / 15.0)
        L = H * 0.34 * RNG.uniform(0.78, 1.0)
        cx, cy = W * 0.5, H * 0.42
        leaf(cv, cx + math.cos(a) * L * 0.5, cy + math.sin(a) * L * 0.5, L, L * 0.34, a,
             jitter(C_PALM_LT, 0.15), rough=0.58, vein=0.5, soft=2.0)
    for i in range(10):
        a = math.radians(62 + i * 56 / 9.0)
        L = H * 0.20
        leaf(cv, W * 0.5 + math.cos(a) * L * 0.5, H * 0.44 + math.sin(a) * L * 0.5, L, L * 0.30, a,
             jitter(C_SKIRT, 0.2), rough=0.82, vein=0.5, soft=2.0)


def bb_broadleaf(cv):
    W, H = cv.w, cv.h
    _trunk(cv, W * 0.50, H * 1.0, W * 0.50, H * 0.60, W * 0.085)
    for s in (-1, 1):
        _trunk(cv, W * 0.50, H * 0.64, W * (0.5 + 0.17 * s), H * 0.47, W * 0.045)
    _cluster(cv, W * 0.50, H * 0.33, W * 0.46, H * 0.27, 230, C_BROAD, W * 0.075, W * 0.125, 0.52,
             twigs=False, wfrac=0.5)
    _cluster(cv, W * 0.44, H * 0.26, W * 0.26, H * 0.15, 80, C_BROAD_LT, W * 0.065, W * 0.11, 0.48,
             twigs=False, wfrac=0.5)


def bb_cypress(cv):
    """Narrow column, pointed top, ragged flanks: the shape has to survive at 60 m."""
    W, H = cv.w, cv.h
    _trunk(cv, W * 0.5, H * 1.0, W * 0.5, H * 0.93, W * 0.16, (0.075, 0.060, 0.045))
    for _ in range(190):
        t = RNG.random() ** 0.9
        y = H * (0.035 + 0.93 * t)
        prof = math.sin(math.pi * min(1.0, 0.055 + 0.95 * (1.0 - y / H))) ** 0.40
        half = W * 0.47 * prof
        x = W * 0.5 + RNG.uniform(-1, 1) * half
        ln = W * RNG.uniform(0.17, 0.30)
        leaf(cv, x, y, ln, ln * 0.55, RNG.uniform(0, 2 * math.pi),
             np.clip(jitter(C_CYP, 0.20) * RNG.uniform(0.70, 1.20), 0, 1), rough=0.58, vein=0.6)
    for _ in range(26):  # ragged sprigs breaking the outline
        t = RNG.random()
        y = H * (0.05 + 0.90 * t)
        prof = math.sin(math.pi * min(1.0, 0.055 + 0.95 * (1.0 - y / H))) ** 0.40
        s = 1 if RNG.random() < 0.5 else -1
        _sprig(cv, W * 0.5 + s * W * 0.45 * prof, y, math.radians(-90 + RNG.uniform(-40, 40)),
               H * RNG.uniform(0.05, 0.11), 2, C_CYP, 3.0)


def bb_pine(cv):
    """Conifer crown over a bare trunk: foliage only in the top 58%, as on the LOD0 mesh."""
    W, H = cv.w, cv.h
    _trunk(cv, W * 0.50, H * 1.0, W * 0.50, H * 0.04, W * 0.070, (0.095, 0.070, 0.050))
    whorls = 9
    for i in range(whorls):
        t = i / (whorls - 1.0)
        y = H * (0.56 - 0.50 * t)
        half = W * 0.47 * (1.0 - t) ** 0.80 + W * 0.045
        for s in (-1, 1):
            for j in range(3):
                cx = W * 0.5 + s * half * RNG.uniform(0.18, 0.95)
                _cluster(cv, cx, y + H * RNG.uniform(-0.012, 0.012), half * 0.30, H * 0.030, 22,
                         C_PINE, W * 0.11, W * 0.20, 0.52, twigs=False, wfrac=0.20)


def bb_shrub(cv):
    """Painted wide and low in the slot: a shrub is broader than it is tall, and a billboard whose
    painted aspect does not match the mesh card gets visibly smeared."""
    W, H = cv.w, cv.h
    _cluster(cv, W * 0.5, H * 0.80, W * 0.49, H * 0.165, 150, C_SCRUB, W * 0.055, W * 0.10, 0.66,
             wfrac=0.4)
    _cluster(cv, W * 0.5, H * 0.74, W * 0.34, H * 0.10, 60, C_SCRUB2, W * 0.05, W * 0.09, 0.64,
             twigs=False, wfrac=0.5)


def bb_hedge(cv):
    W, H = cv.w, cv.h
    _cluster(cv, W * 0.5, H * 0.70, W * 0.52, H * 0.28, 260, C_BOX, W * 0.055, W * 0.095, 0.56,
             twigs=False, wfrac=0.62)


# ------------------------------------------------------------------ bark tiles (opaque, tiling)
def _bark_base(cv, col, rough):
    W, H = cv.w, cv.h
    cv.rgb[:] = np.asarray(col, np.float32)
    cv.a[:] = 1.0
    cv.rg[:] = rough
    ys, xs = np.mgrid[0:H, 0:W].astype(np.float32)
    n = np.zeros((H, W), np.float32)
    for f, amp in ((4, 0.5), (9, 0.28), (19, 0.15), (37, 0.07)):
        ph = RNG.uniform(0, 6.28, 2)
        n += amp * (np.sin(xs / W * f * 6.2832 + ph[0]) * np.sin(ys / H * f * 6.2832 + ph[1]))
    cv.ht[:] = 0.5 + 0.25 * n
    return xs, ys, n


def bark_palm(cv):
    """Ringed royal-palm trunk: smooth grey-green with horizontal leaf-scar bands."""
    W, H = cv.w, cv.h
    xs, ys, n = _bark_base(cv, (0.128, 0.122, 0.100), 0.74)
    band = np.sin(ys / H * 9 * 6.2832 + 0.45 * np.sin(xs / W * 3 * 6.2832)) * 0.5 + 0.5
    band = np.clip((band - 0.40) / 0.45, 0, 1)
    cv.ht[:] = np.clip(0.48 + 0.26 * band + 0.16 * n, 0, 1)
    cv.rgb[:] *= (0.86 + 0.20 * band + 0.06 * n)[..., None]
    cv.rg[:] = 0.70 + 0.14 * (1 - band)


def bark_broadleaf(cv):
    """Mottled plane/ficus bark: smooth plates, pale patches."""
    W, H = cv.w, cv.h
    xs, ys, n = _bark_base(cv, (0.098, 0.082, 0.064), 0.76)
    p = np.zeros((H, W), np.float32)
    for f in (3, 6, 11):
        ph = RNG.uniform(0, 6.28, 2)
        p += np.sin(xs / W * f * 6.2832 + ph[0] + 1.3 * np.sin(ys / H * (f * 0.7) * 6.2832)) / f
    patch = np.clip((p * 2.4 + 0.5), 0, 1)
    pale = np.asarray((0.205, 0.190, 0.155), np.float32)
    cv.rgb[:] = cv.rgb * (1 - patch[..., None]) + pale * patch[..., None]
    cv.ht[:] = np.clip(0.5 + 0.30 * n + 0.18 * (patch - 0.5), 0, 1)
    cv.rg[:] = 0.70 + 0.14 * (1 - patch)


def bark_conifer(cv):
    """Furrowed pine/cypress bark: deep vertical fissures, plates."""
    W, H = cv.w, cv.h
    xs, ys, n = _bark_base(cv, (0.080, 0.052, 0.038), 0.84)
    fis = np.sin(xs / W * 9 * 6.2832 + 1.9 * np.sin(ys / H * 2.0 * 6.2832) + 0.9 * n)
    plate = np.clip(fis * 0.5 + 0.5, 0, 1) ** 0.6
    seg = np.clip(np.sin(ys / H * 16 * 6.2832 + 2.0 * np.sin(xs / W * 5 * 6.2832)) * 0.5 + 0.5, 0, 1)
    d = np.clip(plate * (0.55 + 0.45 * seg), 0, 1)
    cv.rgb[:] = cv.rgb * (0.52 + 0.92 * d)[..., None]
    cv.ht[:] = np.clip(0.18 + 0.78 * d, 0, 1)
    cv.rg[:] = 0.80 + 0.12 * (1 - d)


def bark_stem(cv):
    """Thin shrub / twig bark."""
    W, H = cv.w, cv.h
    xs, ys, n = _bark_base(cv, (0.085, 0.068, 0.050), 0.82)
    fis = np.clip(np.sin(xs / W * 14 * 6.2832 + 1.2 * n) * 0.5 + 0.5, 0, 1)
    cv.rgb[:] *= (0.70 + 0.55 * fis)[..., None]
    cv.ht[:] = np.clip(0.35 + 0.55 * fis, 0, 1)


# ------------------------------------------------------------------ atlas assembly
FOLIAGE_TILES = [
    ("palm_royal_frond", 0, 0, 512, 512, t_palm_royal_frond),
    ("palm_fan_frond", 512, 0, 1024, 512, t_palm_fan_frond),
    ("palm_skirt", 1024, 0, 1536, 512, t_palm_skirt),
    ("broadleaf_a", 1536, 0, 2048, 512, t_broadleaf_a),
    ("broadleaf_b", 0, 512, 512, 1024, t_broadleaf_b),
    ("cypress_sprig", 512, 512, 1024, 1024, t_cypress_sprig),
    ("pine_spray", 1024, 512, 1536, 1024, t_pine_spray),
    ("boxwood", 1536, 512, 2048, 1024, t_boxwood),
    ("scrub_a", 0, 1024, 512, 1536, t_scrub_a),
    ("scrub_b", 512, 1024, 1024, 1536, t_scrub_b),
    ("grass_tuft", 1024, 1024, 1536, 1536, t_grass_tuft),
    ("leaf_small", 1536, 1024, 2048, 1536, t_leaf_small),
    ("bb_royal", 0, 1536, 256, 2048, bb_royal),
    ("bb_fan", 256, 1536, 512, 2048, bb_fan),
    ("bb_broadleaf", 512, 1536, 1024, 2048, bb_broadleaf),
    ("bb_cypress", 1024, 1536, 1280, 2048, bb_cypress),
    ("bb_pine", 1280, 1536, 1536, 2048, bb_pine),
    ("bb_shrub", 1536, 1536, 1792, 2048, bb_shrub),
    ("bb_hedge", 1792, 1536, 2048, 2048, bb_hedge),
]

BARK_TILES = [
    ("bark_palm", 0, 0, 512, 512, bark_palm),
    ("bark_broadleaf", 512, 0, 1024, 512, bark_broadleaf),
    ("bark_conifer", 0, 512, 512, 1024, bark_conifer),
    ("bark_stem", 512, 512, 1024, 1024, bark_stem),
]


def dilate(rgb, a, px=12):
    """Bleed colour outward into transparent pixels so mips and filtering never pull black."""
    out = rgb.copy()
    cov = a > 0.02
    for _ in range(px):
        miss = ~cov
        if not miss.any():
            break
        acc = np.zeros_like(out)
        cnt = np.zeros(a.shape, np.float32)
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            sh = np.roll(out, (dy, dx), (0, 1))
            sc = np.roll(cov, (dy, dx), (0, 1)).astype(np.float32)
            acc += sh * sc[..., None]
            cnt += sc
        fill = miss & (cnt > 0)
        out[fill] = acc[fill] / cnt[fill][..., None]
        cov = cov | fill
    return out


def normal_from_height(h, strength=2.4):
    gx = np.roll(h, -1, 1) - np.roll(h, 1, 1)
    gy = np.roll(h, -1, 0) - np.roll(h, 1, 0)
    nx, ny, nz = -gx * strength, gy * strength, np.ones_like(h)  # +Y up (OpenGL)
    ln = np.sqrt(nx * nx + ny * ny + nz * nz)
    return np.stack([nx / ln, ny / ln, nz / ln], -1) * 0.5 + 0.5


def build(size, tiles, name, alpha_clip):
    rgb = np.zeros((size, size, 3), np.float32)
    a = np.zeros((size, size), np.float32)
    ht = np.zeros((size, size), np.float32)
    rg = np.full((size, size), 0.6, np.float32)
    uvs = {}
    for tn, x0, y0, x1, y1, fn in tiles:
        cv = Canvas(x1 - x0, y1 - y0)
        fn(cv)
        rgb[y0:y1, x0:x1] = cv.rgb
        a[y0:y1, x0:x1] = cv.a
        ht[y0:y1, x0:x1] = cv.ht
        rg[y0:y1, x0:x1] = cv.rg
        # UV rect with v measured from the BOTTOM (Blender / Unity convention)
        # tight bbox of painted pixels, so cards carry almost no empty alpha (fill rate)
        ta = cv.a > 0.02
        if ta.any():
            rows = np.where(ta.any(1))[0]
            cols = np.where(ta.any(0))[0]
            pad = 2
            ry0 = max(0, int(rows[0]) - pad)
            ry1 = min(y1 - y0, int(rows[-1]) + 1 + pad)
            rx0 = max(0, int(cols[0]) - pad)
            rx1 = min(x1 - x0, int(cols[-1]) + 1 + pad)
        else:
            rx0, ry0, rx1, ry1 = 0, 0, x1 - x0, y1 - y0
        ax0, ay0, ax1, ay1 = x0 + rx0, y0 + ry0, x0 + rx1, y0 + ry1
        uvs[tn] = {
            "uv": [ax0 / size, 1.0 - ay1 / size, ax1 / size, 1.0 - ay0 / size],
            "aspect": (ax1 - ax0) / float(ay1 - ay0),
            "px": [ax0, ay0, ax1, ay1],
        }
    if alpha_clip:
        # leaves are matte. Smoothness above ~0.38 gives vegetation a wet plastic sheen in URP,
        # which is what made the first pass read as grey-green rather than green.
        rg = np.clip(np.maximum(rg, 0.62), 0, 1)
        rgb = dilate(rgb, a, 14)
        a = np.where(a > 0.5, 1.0, np.where(a > 0.22, a, 0.0))
    base = np.concatenate([srgb(rgb), np.clip(a, 0, 1)[..., None]], -1)
    write_png(os.path.join(OUT, name + "_BaseColor.png"), (base * 255 + 0.5).astype(np.uint8))
    nrm = normal_from_height(ht)
    nrm4 = np.concatenate([nrm, np.ones(nrm.shape[:2] + (1,), np.float32)], -1)
    write_png(os.path.join(OUT, name + "_Normal.png"), (np.clip(nrm4, 0, 1) * 255 + 0.5).astype(np.uint8))
    # URP Lit: metallic in RGB (0 for vegetation and bark), smoothness = 1 - roughness in alpha
    sm = np.clip(1.0 - rg, 0, 1)
    z = np.zeros_like(sm)
    ms = np.stack([z, z, z, sm], -1)
    write_png(os.path.join(OUT, name + "_MetallicSmoothness.png"), (ms * 255 + 0.5).astype(np.uint8))
    return uvs


if __name__ == "__main__":
    import json
    os.makedirs(OUT, exist_ok=True)
    u1 = build(2048, FOLIAGE_TILES, "T_Veyra_Foliage_Atlas", True)
    u2 = build(1024, BARK_TILES, "T_Veyra_Bark_Atlas", False)
    with open(os.path.join(OUT, "atlas_uvs.json"), "w") as f:
        json.dump({"foliage": u1, "bark": u2}, f, indent=1)
    print("atlas done:", len(u1), "foliage tiles,", len(u2), "bark tiles")

"""Veyra City vegetation set 2 species builders: D4 alpine, D8 redwood, D3 industrial neglect,
and the cheap ground-cover scatter modules.

Same rules as set 1: every LOD authored (never decimated), leaf-card normals biased toward the
canopy volume, pivot at base centre with the trunk base at the local origin.
Ground cover is built to a much harder budget than the trees, because it is the asset class that
is actually instanced in the thousands.
"""
import math

from mathutils import Matrix, Vector

from veg_lib2 import (MAT_BARK, MAT_FOLIAGE, Builder, aspect, card, crossed_billboard,  # noqa: F401
                      mix_normal, rng, strip, tube, uv_rect)

F = "foliage"
BK = "bark"


def _rot(v, axis, ang):
    return Matrix.Rotation(ang, 4, Vector(axis).normalized()) @ Vector(v)


def _dir(az, el):
    return Vector((math.cos(az) * math.cos(el), math.sin(az) * math.cos(el), math.sin(el)))


def _tuft(b, pos, axis, size, rect, asp, vol, r, crosses=2):
    """A foliage tuft: `crosses` cards sharing a centre, rotated about the branch axis. Two crossed
    cards per tuft cost the same as two flat ones but never go edge-on to the camera."""
    for c in range(crosses):
        right = Vector(axis).normalized()
        base_up = right.cross(Vector((0, 0, 1)))
        if base_up.length < 1e-3:
            base_up = Vector((0, 1, 0))
        up = _rot(base_up.normalized(), right, c * math.pi / crosses + r.uniform(-0.4, 0.4))
        cn = right.cross(up).normalized()
        if cn.dot(vol) < 0:
            cn = -cn
        card(b, pos, right, up, size, size / asp, rect, mix_normal(cn, vol, 0.80),
             flip_u=r.random() < 0.5)


# ====================================================================== D4 ALPINE CONIFERS
CONIFER = {
    # narrow steep spruce and a fuller fir; both downswept, both with a bare lower trunk
    "Spruce": dict(H=18.0, bare=0.17, whorls=14, per=8, spread=2.10, taper=0.72, tr=0.26,
                   el0=-26.0, el1=8.0, inner=0.26, bb="bb_spruce", bbw=1.05),
    "Fir": dict(H=13.0, bare=0.22, whorls=11, per=9, spread=3.30, taper=0.40, tr=0.28,
                el0=-18.0, el1=12.0, inner=0.24, bb="bb_fir", bbw=1.15),
}


def alpine_conifer(shape, lod, seed=11):
    p = CONIFER[shape]
    r = rng(seed + hash(shape) % 971 + lod * 7)
    b = Builder()
    H = p["H"]
    rect = uv_rect(F, "spruce_spray")
    asp = aspect(F, "spruce_spray")
    rect_bk = uv_rect(BK, "bark_conifer", 2.0, 1024.0)
    if lod == 2:
        crossed_billboard(b, H, p["spread"] * 2.0 * p["bbw"], uv_rect(F, p["bb"]), planes=2,
                          pivot_frac=0.5)
        return b
    sides = (8, 6)[lod]
    nseg = max(5, int(round(H / 2.5)))
    tpts, trad = [], []
    for i in range(nseg + 1):
        t = i / nseg
        tpts.append((0.0, 0.0, H * 0.985 * t))
        trad.append(p["tr"] * (1 - t) ** 0.80 + 0.028)
    tube(b, tpts, trad, sides, rect_bk, tile_m=2.5)

    whorls = p["whorls"] if lod == 0 else max(4, p["whorls"] - 3)
    per = (p["per"], p["per"] - 2)[lod]
    tufts = (4, 3)[lod]
    for w in range(whorls):
        t = w / max(1, whorls - 1.0)
        z = H * (p["bare"] + (0.965 - p["bare"]) * t) + r.uniform(-0.3, 0.3) * (H * 0.015)
        half = p["spread"] * (1.0 - t) ** p["taper"] + p["spread"] * 0.06
        for k in range(per):
            az = 2 * math.pi * k / per + w * 0.77 + r.uniform(-0.17, 0.17)
            el = math.radians(p["el0"] + (p["el1"] - p["el0"]) * t + r.uniform(-5, 5))
            d = _dir(az, el)
            base = Vector((0, 0, z))
            ln = half * r.uniform(0.70, 1.04)
            if lod == 0 and w < max(2, whorls // 4):
                tube(b, [base + d * 0.05, base + d * (ln * 0.55)],
                     [p["tr"] * 0.24, p["tr"] * 0.10], 4, rect_bk, tile_m=1.4)
            for c in range(tufts):
                ct = p["inner"] + (1.0 - p["inner"]) * (c + 0.6) / (tufts + 0.2)
                pos = base + d * (ln * ct)
                s = ln * (0.60 if lod == 0 else 0.84) * r.uniform(0.80, 1.06)
                vol = Vector((math.cos(az) * 0.55, math.sin(az) * 0.55, 1.0)).normalized()
                _tuft(b, pos, d, s, rect, asp, vol, r)
    return b


def conifer_collider(shape):
    p = CONIFER[shape]
    return [(0, 0, 0), (0, 0, p["H"] * 0.5)], [p["tr"] * 1.2, p["tr"] * 0.7]


# ====================================================================== D4 WEATHERED SNAG
def snag(lod, seed=12):
    """Standing dead tree: broken top, stripped limb stubs, no foliage at all.
    Bark material only, so this asset is ONE material and costs one draw call."""
    r = rng(seed + lod * 5)
    H = 9.2
    b = Builder()
    rect_bk = uv_rect(BK, "bark_conifer", 2.0, 1024.0)
    sides = (8, 5, 4)[lod]
    nseg = (6, 4, 2)[lod]
    tpts, trad = [], []
    for i in range(nseg + 1):
        t = i / nseg
        # leans slightly, and the broken top is torn rather than tapered to a point
        tpts.append((0.10 * H * t * t * 0.35, 0.03 * H * t, H * t))
        trad.append(0.30 * (1 - t) ** 0.42 + 0.055)
    tube(b, tpts, trad, sides, rect_bk, tile_m=2.6, close_top=True)
    if lod == 2:
        return b
    stubs = (9, 5)[lod]
    for i in range(stubs):
        t = r.uniform(0.28, 0.94)
        z = H * t
        az = 2 * math.pi * i / stubs + r.uniform(-0.4, 0.4)
        el = math.radians(r.uniform(-34, 26))
        d = _dir(az, el)
        ln = (1.9 - 1.25 * t) * r.uniform(0.45, 1.0)
        rad = 0.085 * (1.0 - 0.5 * t)
        base = Vector((0.10 * H * t * t * 0.35, 0.03 * H * t, z))
        tube(b, [base + d * 0.04, base + d * ln], [rad, rad * 0.35],
             (5, 4)[lod], rect_bk, tile_m=1.3, close_top=True)
    return b


def snag_collider():
    return [(0, 0, 0), (0, 0, 9.2 * 0.6)], [0.36, 0.20]


# ====================================================================== D8 REDWOOD
def redwood(H, lod, seed=13):
    """Sequoia form: buttressed trunk, bare for the lower half, narrow irregular crown.
    The LOD2 billboard carries most of the work - these are read in silhouette across the valley."""
    r = rng(seed + int(H * 3) + lod * 11)
    b = Builder()
    rect = uv_rect(F, "redwood_spray")
    asp = aspect(F, "redwood_spray")
    rect_bk = uv_rect(BK, "bark_conifer", 2.0, 1024.0)
    spread = 0.105 * H + 1.0
    if lod == 2:
        # 3 planes, not 2: this is the district's signature silhouette and it is seen from every
        # angle across the valley, so an extra 2 tris is the cheapest quality in the whole set.
        crossed_billboard(b, H, spread * 2.0, uv_rect(F, "bb_redwood"), planes=3, pivot_frac=0.5)
        return b
    sides = (10, 6)[lod]
    tr = 0.030 * H + 0.28
    nseg = max(6, int(round(H / 3.0)))
    tpts, trad = [], []
    for i in range(nseg + 1):
        t = i / nseg
        # strong basal buttress flare, then a long slow taper
        flare = 1.0 + 0.55 * max(0.0, 1.0 - t * 7.0) ** 2
        tpts.append((0.0, 0.0, H * 0.99 * t))
        trad.append((tr * (1 - t) ** 0.62 + 0.05) * flare)
    tube(b, tpts, trad, sides, rect_bk, tile_m=3.0)

    bare = 0.46
    whorls = (int(10 + H * 0.16), int(7 + H * 0.11))[lod]
    per = (7, 5)[lod]
    tufts = (4, 3)[lod]
    for w in range(whorls):
        t = w / max(1, whorls - 1.0)
        z = H * (bare + (0.975 - bare) * t) + r.uniform(-0.4, 0.4) * (H * 0.012)
        half = spread * (1.0 - t) ** 0.58 + spread * 0.10
        for k in range(per):
            az = 2 * math.pi * k / per + w * 0.81 + r.uniform(-0.22, 0.22)
            el = math.radians(r.uniform(-16, 6) + 26 * t)
            d = _dir(az, el)
            base = Vector((0, 0, z))
            ln = half * r.uniform(0.62, 1.05)
            if lod == 0 and w < 3:
                tube(b, [base + d * 0.05, base + d * (ln * 0.6)],
                     [tr * 0.20, tr * 0.08], 4, rect_bk, tile_m=1.6)
            for c in range(tufts):
                ct = 0.28 + 0.72 * (c + 0.6) / (tufts + 0.2)
                pos = base + d * (ln * ct)
                s = ln * (0.68 if lod == 0 else 0.90) * r.uniform(0.80, 1.06)
                vol = Vector((math.cos(az) * 0.5, math.sin(az) * 0.5, 1.0)).normalized()
                _tuft(b, pos, d, s, rect, asp, vol, r)
    return b


def redwood_collider(H):
    tr = 0.030 * H + 0.28
    return [(0, 0, 0), (0, 0, H * 0.45)], [tr * 1.6, tr * 0.85]


# ====================================================================== D4 / D3 SHRUB-SCALE
SCRUB2 = {
    "MountainScrub": dict(W=1.40, H=0.85, tile="mountain_scrub", cards=(58, 24), size=0.50,
                          twigs=5, stem=0.022, flat=0.70, bb="bb_mtn_scrub", upright=False),
    "RockLedgeTuft": dict(W=0.70, H=0.50, tile="rock_tuft", cards=(30, 12), size=0.40,
                          twigs=0, stem=0.0, flat=0.9, bb="bb_mtn_scrub", upright=True),
    "DeadGrassClump": dict(W=0.95, H=0.62, tile="dead_grass", cards=(34, 14), size=0.50,
                           twigs=0, stem=0.0, flat=0.9, bb="bb_dead_grass", upright=True),
    "FenceLineWeeds": dict(W=1.25, H=1.05, tile="weed_tall", cards=(46, 19), size=0.72,
                           twigs=0, stem=0.0, flat=0.95, bb="bb_weed", upright=True),
    "ConcreteCrackWeed": dict(W=0.40, H=0.34, tile="weed_broadleaf", cards=(18, 8), size=0.26,
                              twigs=0, stem=0.0, flat=0.8, bb="bb_ground_patch", upright=False),
}


def scrub(kind, lod, seed=14):
    p = SCRUB2[kind]
    r = rng(seed + hash(kind) % 967 + lod * 13)
    b = Builder()
    rect = uv_rect(F, p["tile"])
    W, Hh = p["W"], p["H"]
    if lod == 2:
        crossed_billboard(b, Hh, W, uv_rect(F, p["bb"]), planes=2, pivot_frac=0.5)
        return b
    C = Vector((0, 0, Hh * 0.52))
    if p["twigs"] and lod == 0:
        for i in range(p["twigs"]):
            az = 2 * math.pi * i / p["twigs"] + r.uniform(-0.3, 0.3)
            el = math.radians(r.uniform(50, 80))
            d = _dir(az, el)
            tube(b, [(0, 0, 0), tuple(d * (Hh * 0.72))], [p["stem"] * 1.4, p["stem"] * 0.5], 3,
                 uv_rect(BK, "bark_stem", 2.0, 1024.0), tile_m=1.0)
    n = p["cards"][lod]
    size = p["size"] * (1.0 if lod == 0 else 1.45)
    upright = p["upright"]
    for _ in range(n):
        while True:
            u = Vector((r.gauss(0, 1), r.gauss(0, 1), r.gauss(0, 1)))
            if u.length > 1e-3:
                break
        u.normalize()
        sh = r.uniform(0.50, 1.0) ** 0.45
        pos = C + Vector((u.x * W * 0.5, u.y * W * 0.5, u.z * Hh * 0.5 * p["flat"])) * sh
        radial = Vector((pos.x, pos.y, (pos.z - C.z) * 1.6 + 0.25)).normalized()
        if upright:
            right = Vector((-u.y, u.x, 0))
            if right.length < 1e-3:
                right = Vector((1, 0, 0))
            right.normalize()
            up = Vector((0, 0, 1))
        else:
            right = radial.cross(Vector((0, 0, 1)))
            if right.length < 1e-3:
                right = Vector((1, 0, 0))
            right.normalize()
            up = right.cross(radial).normalized()
            right = _rot(right, up, r.uniform(-0.7, 0.7))
            up = _rot(up, right, r.uniform(-0.7, 0.7))
        cn = right.cross(up).normalized()
        if cn.dot(radial) < 0:
            cn = -cn
        s = size * r.uniform(0.78, 1.18)
        pos.z = s * 0.50 if upright else max(pos.z, s * 0.72)
        card(b, pos, right, up, s * r.uniform(0.85, 1.1), s, rect,
             mix_normal(cn, radial, 0.80), flip_u=r.random() < 0.5)
    return b


# ====================================================================== D3 VOLUNTEER SAPLING
def sapling(lod, seed=15):
    """Self-seeded tree in a yard or a gutter: thin leaning stem, a few forks, sparse compound
    leaves. Deliberately scrappy - a lush one would read as planted, not volunteer."""
    r = rng(seed + lod * 9)
    H = 3.6
    b = Builder()
    rect = uv_rect(F, "sapling_leaf")
    asp = aspect(F, "sapling_leaf")
    rect_bk = uv_rect(BK, "bark_stem", 2.0, 1024.0)
    if lod == 2:
        crossed_billboard(b, H, H * 0.52, uv_rect(F, "bb_sapling"), planes=2, pivot_frac=0.5)
        return b
    sides = (6, 4)[lod]
    lean = 0.12
    nseg = 4
    tpts, trad = [], []
    for i in range(nseg + 1):
        t = i / nseg
        tpts.append((lean * H * t * t, 0.03 * H * t, H * 0.72 * t))
        trad.append(0.065 * (1 - t) ** 0.7 + 0.012)
    tube(b, tpts, trad, sides, rect_bk, tile_m=1.8)
    forks = (4, 3)[lod]
    tips = []
    for i in range(forks):
        t = 0.45 + 0.5 * i / max(1, forks - 1.0)
        base = Vector((lean * H * t * t, 0.03 * H * t, H * 0.72 * t))
        az = 2 * math.pi * i / forks + r.uniform(-0.4, 0.4)
        el = math.radians(r.uniform(48, 72))
        d = _dir(az, el)
        ln = H * r.uniform(0.18, 0.34)
        if lod == 0:
            tube(b, [base, base + d * ln], [0.030, 0.010], 4, rect_bk, tile_m=1.2)
        tips.append((base + d * ln, az))
    n = (34, 14)[lod]
    for i in range(n):
        tip, az0 = tips[i % len(tips)]
        off = Vector((r.gauss(0, 1), r.gauss(0, 1), r.gauss(0, 1)))
        off = off.normalized() * r.uniform(0.05, 0.55)
        pos = tip + off + Vector((0, 0, 0.12))
        s = (0.52 if lod == 0 else 0.78) * r.uniform(0.80, 1.15)
        pos.z = max(pos.z, s * 0.55)
        vol = Vector((pos.x, pos.y, 1.3)).normalized()
        axis = _dir(az0 + r.uniform(-1.2, 1.2), math.radians(r.uniform(-25, 35)))
        _tuft(b, pos, axis, s, rect, asp, vol, r, crosses=2)
    return b


def sapling_collider():
    return [(0, 0, 0), (0, 0, 3.6 * 0.5)], [0.085, 0.045]


# ====================================================================== GROUND COVER
# Built deliberately cheap: these are scattered in the thousands, so every triangle here is paid
# for ~1000x more often than a triangle on a tree.
GROUND = {
    "GrassTuft_S": dict(W=0.38, H=0.30, tiles=("dead_grass",), cards=(6, 3), size=0.34,
                        bb="bb_dead_grass", spread=0.10),
    "GrassPatch_M": dict(W=1.00, H=0.42, tiles=("dead_grass", "rock_tuft"), cards=(14, 6),
                         size=0.42, bb="bb_ground_patch", spread=0.38),
    "WeedPatch_M": dict(W=0.90, H=0.50, tiles=("weed_tall", "weed_broadleaf"), cards=(14, 6),
                        size=0.46, bb="bb_ground_patch", spread=0.34),
    "DryPatch_L": dict(W=1.70, H=0.46, tiles=("dead_grass", "weed_tall"), cards=(22, 9),
                       size=0.44, bb="bb_ground_patch", spread=0.70),
}


def ground(kind, lod, seed=16):
    p = GROUND[kind]
    r = rng(seed + hash(kind) % 953 + lod * 3)
    b = Builder()
    if lod == 2:
        # a single card, 2 tris: at ground-cover LOD2 distance nothing else is justifiable
        crossed_billboard(b, p["H"], p["W"], uv_rect(F, p["bb"]), planes=1, pivot_frac=0.5)
        return b
    rects = [uv_rect(F, t) for t in p["tiles"]]
    n = p["cards"][lod]
    size = p["size"] * (1.0 if lod == 0 else 1.5)
    for i in range(n):
        a = r.uniform(0, 2 * math.pi)
        rad = p["spread"] * math.sqrt(r.random())
        pos = Vector((math.cos(a) * rad, math.sin(a) * rad, 0.0))
        right = Vector((math.cos(a + math.pi / 2 + r.uniform(-1.4, 1.4)),
                        math.sin(a + math.pi / 2 + r.uniform(-1.4, 1.4)), 0)).normalized()
        up = Vector((0, 0, 1))
        s = size * r.uniform(0.72, 1.20)
        pos.z = s * 0.50
        # ground cover shades off the ground plane, not off a canopy sphere: biasing the normals
        # up keeps a scattered field from flickering dark as the camera turns
        vol = Vector((pos.x * 0.35, pos.y * 0.35, 1.0)).normalized()
        cn = right.cross(up).normalized()
        if cn.dot(vol) < 0:
            cn = -cn
        card(b, pos, right, up, s * r.uniform(0.9, 1.25), s, rects[i % len(rects)],
             mix_normal(cn, vol, 0.88), flip_u=r.random() < 0.5)
    return b

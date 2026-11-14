"""VEYRA CITY vegetation species builders.

Every LOD is authored, not decimated. Silhouette fidelity is traded for triangles wherever the two
conflict (BUG-170: this project is CPU bound; vegetation is instanced in the thousands).
"""
import math

from mathutils import Matrix, Vector

from veg_lib import (MAT_BARK, MAT_FOLIAGE, Builder, aspect, card, crossed_billboard, mix_normal,
                     rng, strip, strip_folded, tube, uv_rect)

F = "foliage"
BK = "bark"


def _rot(v, axis, ang):
    return (Matrix.Rotation(ang, 4, axis) @ Vector(v))


def _dir(az, el):
    return Vector((math.cos(az) * math.cos(el), math.sin(az) * math.cos(el), math.sin(el)))


# ====================================================================== ROYAL PALM
def royal_palm(H, lod, seed=1):
    """Tall clean ringed trunk, crownshaft, arching pinnate fronds. H = overall height."""
    r = rng(seed + int(H * 7) + lod * 31)
    b = Builder()
    sides = (8, 6, 0)[lod]
    crown_z = H * 0.80
    rb, rt = 0.12 + 0.0055 * H, 0.085 + 0.0030 * H
    frond_n = {8: 12, 12: 14, 16: 16}.get(int(H), 13)
    L = 0.30 * H
    rect_f = uv_rect(F, "palm_royal_frond")
    rect_b = uv_rect(BK, "bark_palm", 2.0, 1024.0)

    if lod == 2:
        crossed_billboard(b, H, H * 0.52, uv_rect(F, "bb_royal"), planes=3, pivot_frac=0.5)
        return b

    # trunk: gentle S lean, slight basal flare
    nseg = max(4, int(round(crown_z / 2.0)))
    lean = r.uniform(-0.035, 0.035)
    pts, radii = [], []
    for i in range(nseg + 1):
        t = i / nseg
        z = crown_z * t
        x = lean * H * (t ** 1.8) + 0.04 * H * lean * math.sin(t * 2.2)
        pts.append((x, 0.0, z))
        radii.append(rb * (1.0 - t) ** 0.55 + rt * t + (0.035 * H * 0.01) * max(0.0, 1 - t * 6))
    tube(b, pts, radii, sides, rect_b, tile_m=2.0)
    # crownshaft
    top = Vector(pts[-1])
    csh = H * 0.075
    tube(b, [top, top + Vector((lean * H * 0.08, 0, csh))], [rt * 1.06, rt * 0.80],
         sides, uv_rect(BK, "bark_stem", 2.0, 1024.0), tile_m=1.4, close_top=True)
    crown = top + Vector((lean * H * 0.08, 0, csh))

    segs = (7, 3, 0)[lod]
    fw = L * 0.46
    for i in range(frond_n):
        az = 2 * math.pi * i / frond_n + r.uniform(-0.12, 0.12)
        e0 = math.radians(r.uniform(18, 58) if i % 3 == 0 else r.uniform(-8, 34))
        drop = math.radians(r.uniform(72, 104))
        roll = r.uniform(-0.45, 0.45)
        pts_f, rights, tangents, widths, norms = [], [], [], [], []
        for k in range(segs + 1):
            t = k / segs
            el = e0 - drop * (t ** 1.55)
            d = _dir(az, el)
            arc = Vector((0, 0, 0))
            for j in range(k):
                tj = (j + 0.5) / segs
                arc += _dir(az, e0 - drop * (tj ** 1.55)) * (L / segs)
            p = crown + arc
            right = Vector((-math.sin(az), math.cos(az), 0))
            right = _rot(right, d, roll * t)
            w = fw * (0.42 + 0.58 * math.sin(math.pi * min(1.0, 0.18 + t * 0.88)) ** 0.6)
            vol = (p - crown).normalized().lerp(Vector((0, 0, 1)), 0.45).normalized()
            cn = right.cross(d).normalized()
            if cn.z < 0:
                cn = -cn
            pts_f.append(p)
            rights.append(right)
            tangents.append(d)
            widths.append(w)
            norms.append(mix_normal(cn, vol, 0.70))
        if lod == 0:
            strip_folded(b, pts_f, rights, tangents, widths, rect_f, norms, math.radians(34))
        else:
            strip(b, pts_f, rights, widths, rect_f, norms)
    # young spear
    if lod == 0:
        sp = L * 0.75
        card(b, crown + Vector((0, 0, sp * 0.5)), Vector((1, 0, 0)), Vector((0, 0, 1)),
             fw * 0.30, sp, rect_f, Vector((0, -1, 0.6)))
    return b


def royal_palm_collider(H):
    crown_z = H * 0.80
    rb, rt = 0.12 + 0.0055 * H, 0.085 + 0.0030 * H
    return [(0, 0, 0), (0, 0, crown_z * 0.5), (0, 0, crown_z)], [rb * 1.05, (rb + rt) * 0.55, rt * 1.1]


# ====================================================================== FAN PALM
def fan_palm(H, lod, seed=2):
    """Short stout trunk, costapalmate fans, dried skirt under the crown."""
    r = rng(seed + int(H * 13) + lod * 17)
    b = Builder()
    sides = (8, 5, 0)[lod]
    crown_z = H * 0.60
    rb, rt = 0.20 + 0.012 * H, 0.17 + 0.008 * H
    rect_fan = uv_rect(F, "palm_fan_frond")
    rect_sk = uv_rect(F, "palm_skirt")

    if lod == 2:
        crossed_billboard(b, H, H * 0.62, uv_rect(F, "bb_fan"), planes=3, pivot_frac=0.5)
        return b

    nseg = max(3, int(round(crown_z / 1.6)))
    pts = [(0, 0, crown_z * i / nseg) for i in range(nseg + 1)]
    radii = [rb * (1 - i / nseg) ** 0.5 + rt * (i / nseg) for i in range(nseg + 1)]
    tube(b, pts, radii, sides, uv_rect(BK, "bark_palm", 2.0, 1024.0), tile_m=1.5, close_top=True)
    crown = Vector((0, 0, crown_z))

    fan_n = 12 if lod == 0 else 9
    FL = H * 0.34
    FW = FL * aspect(F, "palm_fan_frond")
    for i in range(fan_n):
        az = 2 * math.pi * i / fan_n + r.uniform(-0.2, 0.2)
        el = math.radians(r.uniform(2, 62))
        d = _dir(az, el)
        stem = 0.22 * FL
        base = crown + d * stem
        right = Vector((-math.sin(az), math.cos(az), 0))
        fold = math.radians(r.uniform(10, 26))
        segs = 3 if lod == 0 else 2
        pts_f, rights, tangents, widths, norms = [], [], [], [], []
        for k in range(segs + 1):
            t = k / segs
            el2 = el - fold * t * 1.6
            p = base + _dir(az, el2) * (FL * t)
            w = FW * (0.30 + 0.70 * math.sin(math.pi * (0.22 + 0.78 * t)) ** 0.5)
            vol = (p - crown).normalized().lerp(Vector((0, 0, 1)), 0.40).normalized()
            tangent = _dir(az, el2)
            cn = right.cross(tangent).normalized()
            if cn.z < 0:
                cn = -cn
            pts_f.append(p)
            rights.append(right)
            tangents.append(tangent)
            widths.append(w)
            norms.append(mix_normal(cn, vol, 0.72))
        if lod == 0:
            strip_folded(b, pts_f, rights, tangents, widths, rect_fan, norms, math.radians(26))
        else:
            strip(b, pts_f, rights, widths, rect_fan, norms)

    skirt_n = 12 if lod == 0 else 8
    SL = H * 0.26
    SW = SL * aspect(F, "palm_skirt") * 0.8
    for i in range(skirt_n):
        az = 2 * math.pi * i / skirt_n + r.uniform(-0.15, 0.15)
        el = math.radians(r.uniform(-78, -42))
        d = _dir(az, el)
        c = crown + d * (SL * 0.48) + Vector((0, 0, -0.04 * H))
        right = Vector((-math.sin(az), math.cos(az), 0))
        up = d
        vol = Vector((math.cos(az), math.sin(az), -0.25)).normalized()
        cn = right.cross(up).normalized()
        card(b, c, right, up, SW, SL, rect_sk, mix_normal(cn, vol, 0.75))
    return b


def fan_palm_collider(H):
    crown_z = H * 0.60
    rb, rt = 0.20 + 0.012 * H, 0.17 + 0.008 * H
    return [(0, 0, 0), (0, 0, crown_z)], [rb * 1.05, rt * 1.15]


# ====================================================================== BROADLEAF STREET TREE
BROADLEAF = {
    # shape: height, crown radius x/y, crown radius z, crown centre z, fork height, trunk radius
    "Spreading": dict(H=9.0, rx=4.1, rz=2.5, cz=6.2, fork=2.5, tr=0.26, cards=(640, 250), size=(1.5, 2.3)),
    "Upright": dict(H=11.5, rx=3.3, rz=3.4, cz=7.6, fork=3.4, tr=0.23, cards=(660, 258), size=(1.4, 2.1)),
}


def broadleaf(shape, lod, seed=3):
    p = BROADLEAF[shape]
    r = rng(seed + hash(shape) % 997 + lod * 7)
    b = Builder()
    rect_a = uv_rect(F, "broadleaf_a")
    rect_b2 = uv_rect(F, "broadleaf_b")
    rect_bk = uv_rect(BK, "bark_broadleaf", 2.0, 1024.0)
    C = Vector((0, 0, p["cz"]))

    if lod == 2:
        crossed_billboard(b, p["H"], p["rx"] * 2.0, uv_rect(F, "bb_broadleaf"), planes=3,
                          pivot_frac=0.5)
        return b

    sides = (8, 6)[lod]
    # trunk to the fork
    tpts = [(0, 0, 0), (0, 0, p["fork"] * 0.55), (0, 0, p["fork"])]
    trad = [p["tr"] * 1.22, p["tr"] * 0.98, p["tr"] * 0.86]
    tube(b, tpts, trad, sides, rect_bk, tile_m=2.0)
    fork = Vector((0, 0, p["fork"]))

    prim = 4 if lod == 0 else 3
    tips = []
    for i in range(prim):
        az = 2 * math.pi * i / prim + r.uniform(-0.3, 0.3)
        el = math.radians(r.uniform(44, 70))
        ln = p["rx"] * r.uniform(0.62, 0.86)
        mid = fork + _dir(az, el + math.radians(14)) * (ln * 0.5)
        end = fork + _dir(az, el) * ln
        tube(b, [fork, mid, end], [p["tr"] * 0.80, p["tr"] * 0.46, p["tr"] * 0.24],
             max(4, sides - 2), rect_bk, tile_m=1.6)
        tips.append((end, az, el))
    if lod == 0:
        for end, az, el in tips:
            for k in range(2):
                az2 = az + r.uniform(-0.8, 0.8)
                el2 = el + r.uniform(-0.35, 0.25)
                ln2 = p["rx"] * r.uniform(0.28, 0.45)
                tube(b, [end, end + _dir(az2, el2) * ln2],
                     [p["tr"] * 0.22, p["tr"] * 0.09], 4, rect_bk, tile_m=1.2)

    n_cards = p["cards"][lod]
    smin, smax = p["size"]
    if lod == 1:
        smin, smax = smin * 1.35, smax * 1.35

    # clump centres on the canopy shell: a real crown is a cluster of lobes, not a smooth dome
    n_clump = 15 if lod == 0 else 9
    clumps = []
    for _ in range(n_clump):
        while True:
            u = Vector((r.gauss(0, 1), r.gauss(0, 1), r.gauss(0, 1)))
            if u.length > 1e-3:
                break
        u.normalize()
        sh = r.uniform(0.60, 0.98)
        clumps.append(C + Vector((u.x * p["rx"], u.y * p["rx"],
                                  u.z * p["rz"] * (0.78 if u.z < 0 else 1.0))) * sh)
    rc = p["rx"] * 0.40

    for _ in range(n_cards):
        cc = clumps[r.randrange(n_clump)]
        while True:
            u = Vector((r.gauss(0, 1), r.gauss(0, 1), r.gauss(0, 1)))
            if u.length > 1e-3:
                break
        u.normalize()
        pos = cc + u * (rc * r.uniform(0.0, 1.0) ** 0.55)
        # keep it inside the overall crown envelope
        e = Vector(((pos.x - C.x) / p["rx"], (pos.y - C.y) / p["rx"], (pos.z - C.z) / p["rz"]))
        if e.length > 1.0:
            e.normalize()
            pos = C + Vector((e.x * p["rx"], e.y * p["rx"], e.z * p["rz"]))
        if pos.z < p["fork"] * 0.95:
            continue
        # shading volume: mostly the whole crown, partly the local lobe, so lobes read as lobes
        glob = Vector((pos.x / p["rx"], pos.y / p["rx"], (pos.z - C.z) / p["rz"]))
        loc = pos - cc
        radial = (glob.normalized() * 0.62 +
                  (loc.normalized() if loc.length > 1e-4 else glob.normalized()) * 0.38).normalized()
        # card plane roughly tangent to the canopy, jittered
        right = radial.cross(Vector((0, 0, 1)))
        if right.length < 1e-3:
            right = Vector((1, 0, 0))
        right.normalize()
        up = right.cross(radial).normalized()
        jx, jy = r.uniform(-0.6, 0.6), r.uniform(-0.6, 0.6)
        right = _rot(right, up, jx)
        up = _rot(up, right, jy)
        cn = right.cross(up).normalized()
        if cn.dot(radial) < 0:
            cn = -cn
        s = r.uniform(smin, smax)
        card(b, pos, right, up, s, s * r.uniform(0.80, 1.05),
             rect_a if r.random() < 0.6 else rect_b2,
             mix_normal(cn, radial, 0.82), flip_u=r.random() < 0.5)
    return b


def broadleaf_collider(shape):
    p = BROADLEAF[shape]
    return [(0, 0, 0), (0, 0, p["fork"])], [p["tr"] * 1.25, p["tr"] * 0.95]


# ====================================================================== ITALIAN CYPRESS
def cypress(H, lod, seed=4):
    r = rng(seed + int(H * 11) + lod * 5)
    b = Builder()
    W = H * 0.155
    rect = uv_rect(F, "cypress_sprig")
    if lod == 2:
        crossed_billboard(b, H, W * 1.25, uv_rect(F, "bb_cypress"), planes=2, pivot_frac=0.5)
        return b
    sides = (6, 4)[lod]
    tube(b, [(0, 0, 0), (0, 0, H * 0.35), (0, 0, H * 0.92)],
         [0.10 + 0.006 * H, 0.055, 0.025], sides, uv_rect(BK, "bark_conifer", 2.0, 1024.0),
         tile_m=2.0)

    n = (235, 92)[lod]
    size = (H * 0.105, H * 0.165)[lod]
    for _ in range(n):
        t = r.random() ** 0.86
        z = H * (0.06 + 0.93 * t)
        # spindle profile: full in the lower-middle, tapering to a point
        prof = math.sin(math.pi * min(1.0, 0.10 + 0.92 * (z / H))) ** 0.45
        rad = (W * 0.5) * prof * r.uniform(0.70, 1.0)
        az = r.uniform(0, 2 * math.pi)
        pos = Vector((math.cos(az) * rad, math.sin(az) * rad, max(z, size * 0.52)))
        radial = Vector((math.cos(az), math.sin(az), 0.12 + 0.55 * (z / H))).normalized()
        right = Vector((-math.sin(az), math.cos(az), 0))
        up = Vector((0, 0, 1))
        right = _rot(right, up, r.uniform(-0.5, 0.5))
        up = _rot(up, right, r.uniform(-0.25, 0.25))
        cn = right.cross(up).normalized()
        if cn.dot(radial) < 0:
            cn = -cn
        s = size * r.uniform(0.80, 1.15)
        card(b, pos, right, up, s * 0.9, s, rect, mix_normal(cn, radial, 0.85),
             flip_u=r.random() < 0.5)
    return b


def cypress_collider(H):
    return [(0, 0, 0), (0, 0, H * 0.7)], [0.13 + 0.006 * H, 0.07]


# ====================================================================== PINE
PINE = {
    # bare = fraction of height with no branches; spread = crown half-width at the lowest whorl
    "Tall": dict(H=13.0, bare=0.42, whorls=10, per=8, spread=2.6, taper=0.80, lean=0.02, tr=0.28,
                 el0=-12.0, el1=34.0, inner=0.28),
    "Umbrella": dict(H=9.5, bare=0.54, whorls=7, per=9, spread=3.4, taper=0.26, lean=0.05, tr=0.32,
                     el0=30.0, el1=2.0, inner=0.50),
}


def pine(shape, lod, seed=5):
    """Conifer built from crossed needle-spray tufts. Single flat sprays read as a bottle brush;
    two cards crossed about the branch axis cost the same per tuft and hold a volume from any angle."""
    p = PINE[shape]
    r = rng(seed + hash(shape) % 991 + lod * 3)
    b = Builder()
    H = p["H"]
    rect = uv_rect(F, "pine_spray")
    asp = aspect(F, "pine_spray")
    rect_bk = uv_rect(BK, "bark_conifer", 2.0, 1024.0)
    if lod == 2:
        crossed_billboard(b, H, p["spread"] * 2.1, uv_rect(F, "bb_pine"), planes=2, pivot_frac=0.5)
        return b
    sides = (8, 6)[lod]
    nseg = max(4, int(round(H / 2.5)))
    tpts, trad = [], []
    for i in range(nseg + 1):
        t = i / nseg
        tpts.append((p["lean"] * H * t * t, 0, H * 0.97 * t))
        trad.append(p["tr"] * (1 - t) ** 0.75 + 0.035)
    tube(b, tpts, trad, sides, rect_bk, tile_m=2.5)

    whorls = p["whorls"] if lod == 0 else max(3, p["whorls"] - 2)
    per = (p["per"], p["per"] - 2)[lod]
    tufts = (4, 3)[lod]
    for w in range(whorls):
        t = w / max(1, whorls - 1.0)
        z = H * (p["bare"] + (0.96 - p["bare"]) * t) + r.uniform(-0.32, 0.32) * (H * 0.022)
        axis_x = p["lean"] * H * (z / H) ** 2
        half = p["spread"] * (1.0 - t) ** p["taper"] + p["spread"] * 0.09
        for k in range(per):
            az = 2 * math.pi * k / per + w * 0.73 + r.uniform(-0.16, 0.16)
            el = math.radians(p["el0"] + (p["el1"] - p["el0"]) * t + r.uniform(-6, 6))
            d = _dir(az, el)
            base = Vector((axis_x, 0, z))
            ln = half * r.uniform(0.66, 1.06)
            if lod == 0 and w < max(2, whorls // 3):
                tube(b, [base + d * 0.06, base + d * (ln * 0.60)],
                     [p["tr"] * 0.26, p["tr"] * 0.11], 4, rect_bk, tile_m=1.4)
            for c in range(tufts):
                ct = p["inner"] + (1.0 - p["inner"]) * (c + 0.65) / (tufts + 0.25)
                pos = base + d * (ln * ct)
                s = ln * (0.50 if lod == 0 else 0.80) * r.uniform(0.78, 1.08)
                vol = Vector((math.cos(az) * 0.6, math.sin(az) * 0.6, 1.0)).normalized()
                for cross in range(2):
                    right = d
                    up = _rot(Vector((-math.sin(az), math.cos(az), 0)), right,
                              cross * math.pi * 0.5 + r.uniform(-0.45, 0.45))
                    cn = right.cross(up).normalized()
                    if cn.dot(vol) < 0:
                        cn = -cn
                    card(b, pos, right, up, s, s / asp, rect,
                         mix_normal(cn, vol, 0.80), flip_u=r.random() < 0.5)
    return b


def pine_collider(shape):
    p = PINE[shape]
    return [(0, 0, 0), (0, 0, p["H"] * 0.5)], [p["tr"] * 1.2, p["tr"] * 0.7]


# ====================================================================== BOXWOOD HEDGE
HEDGE = {
    "Straight_2m": dict(L=2.0, D=0.8, H=1.2, cell=0.50, breakup=52),
    "Corner": dict(L=1.7, D=0.8, H=1.2, cell=0.50, breakup=56, corner=True),
    "Straight_2m_Tall": dict(L=2.0, D=0.9, H=2.5, cell=0.50, breakup=78),
}


def _hedge_shell(b, x0, x1, y0, y1, z0, z1, cell, rect, r, noise, faces="all"):
    """Axis-aligned clipped-box shell in boxwood; each cell carries one full atlas tile,
    randomly flipped, so the leaf scale stays constant and nothing tiles visibly."""
    nx = max(1, int(round((x1 - x0) / cell)))
    ny = max(1, int(round((y1 - y0) / cell)))
    nz = max(1, int(round((z1 - z0) / cell)))
    u0, v0, u1, v1 = rect

    def rr():
        a, bb = (u1, u0) if r.random() < 0.5 else (u0, u1)
        c, d = (v1, v0) if r.random() < 0.5 else (v0, v1)
        return [(a, c), (bb, c), (bb, d), (a, d)]

    def disp(x, y, z):
        return Vector((x + noise * math.sin(y * 7.3 + z * 4.1) * r.uniform(0.4, 1.0),
                       y + noise * math.sin(x * 6.1 + z * 5.7) * r.uniform(0.4, 1.0),
                       z + noise * math.sin(x * 5.3 + y * 6.9) * r.uniform(0.2, 0.8)))

    def face(corners, n):
        b.quad(*corners, rr(), MAT_FOLIAGE, [n] * 4)

    # top
    for i in range(nx):
        for j in range(ny):
            xa, xb = x0 + (x1 - x0) * i / nx, x0 + (x1 - x0) * (i + 1) / nx
            ya, yb = y0 + (y1 - y0) * j / ny, y0 + (y1 - y0) * (j + 1) / ny
            face([disp(xa, ya, z1), disp(xb, ya, z1), disp(xb, yb, z1), disp(xa, yb, z1)],
                 Vector((0, 0, 1)))
    # long sides (+/- Y)
    for i in range(nx):
        for k in range(nz):
            xa, xb = x0 + (x1 - x0) * i / nx, x0 + (x1 - x0) * (i + 1) / nx
            za, zb = z0 + (z1 - z0) * k / nz, z0 + (z1 - z0) * (k + 1) / nz
            face([disp(xa, y0, za), disp(xb, y0, za), disp(xb, y0, zb), disp(xa, y0, zb)],
                 Vector((0, -1, 0.28)))
            face([disp(xb, y1, za), disp(xa, y1, za), disp(xa, y1, zb), disp(xb, y1, zb)],
                 Vector((0, 1, 0.28)))
    # ends (+/- X)
    if faces == "all":
        for j in range(ny):
            for k in range(nz):
                ya, yb = y0 + (y1 - y0) * j / ny, y0 + (y1 - y0) * (j + 1) / ny
                za, zb = z0 + (z1 - z0) * k / nz, z0 + (z1 - z0) * (k + 1) / nz
                face([disp(x0, yb, za), disp(x0, ya, za), disp(x0, ya, zb), disp(x0, yb, zb)],
                     Vector((-1, 0, 0.28)))
                face([disp(x1, ya, za), disp(x1, yb, za), disp(x1, yb, zb), disp(x1, ya, zb)],
                     Vector((1, 0, 0.28)))


def hedge(kind, lod, seed=6):
    p = HEDGE[kind]
    r = rng(seed + hash(kind) % 977 + lod * 11)
    b = Builder()
    rect = uv_rect(F, "boxwood", 2.0)
    rect_edge = uv_rect(F, "leaf_small")
    L, D, Hh = p["L"], p["D"], p["H"]
    corner = p.get("corner", False)
    cell = p["cell"] * (1.0 if lod == 0 else 1.8)
    noise = (0.045, 0.02, 0.0)[lod]

    if lod == 2:
        # a plain clipped box, one tile per face: 12 tris, no breakup cards
        if corner:
            _hedge_shell(b, -L / 2, L / 2, -L / 2, -L / 2 + D, 0, Hh, 99, rect, r, 0)
            _hedge_shell(b, -L / 2, -L / 2 + D, -L / 2 + D, L / 2, 0, Hh, 99, rect, r, 0)
        else:
            _hedge_shell(b, -L / 2, L / 2, -D / 2, D / 2, 0, Hh, 99, rect, r, 0)
        return b

    if corner:
        # L-shaped: one arm along +X, one along +Y, outer corner at (-L/2, -L/2)
        _hedge_shell(b, -L / 2, L / 2, -L / 2, -L / 2 + D, 0, Hh, cell, rect, r, noise)
        _hedge_shell(b, -L / 2, -L / 2 + D, -L / 2 + D, L / 2, 0, Hh, cell, rect, r, noise)
    else:
        _hedge_shell(b, -L / 2, L / 2, -D / 2, D / 2, 0, Hh, cell, rect, r, noise)

    # silhouette breakup along the clipped edges: this is what stops it reading as a box
    nb = p["breakup"] if lod == 0 else int(p["breakup"] * 0.40)
    for _ in range(nb):
        if corner:
            if r.random() < 0.5:
                x = r.uniform(-L / 2, L / 2)
                y = r.uniform(-L / 2, -L / 2 + D)
            else:
                x = r.uniform(-L / 2, -L / 2 + D)
                y = r.uniform(-L / 2 + D, L / 2)
            ex, ey = L / 2, L / 2
        else:
            x = r.uniform(-L / 2, L / 2)
            y = r.uniform(-D / 2, D / 2)
            ex, ey = L / 2, D / 2
        edge = r.random()
        if edge < 0.55:
            z = Hh + r.uniform(-0.03, 0.07)
            n = Vector((0, 0, 1))
        else:
            z = r.uniform(Hh * 0.25, Hh * 0.98)
            sy = 1 if r.random() < 0.5 else -1
            y = sy * (ey + r.uniform(-0.02, 0.05))
            n = Vector((0, sy, 0.4))
        s = r.uniform(0.22, 0.40) * (1.0 if Hh < 2 else 1.15)
        right = _rot(Vector((1, 0, 0)), Vector((0, 0, 1)), r.uniform(0, 6.28))
        up = _rot(Vector((0, 0, 1)), right, r.uniform(-0.6, 0.6))
        cn = right.cross(up).normalized()
        if cn.dot(n) < 0:
            cn = -cn
        card(b, Vector((x, y, z)), right, up, s, s, rect_edge,
             mix_normal(cn, n, 0.80), flip_u=r.random() < 0.5)
    return b


# ====================================================================== LOW SHRUB / SCRUB
SHRUB = {
    "Dry_Scrub": dict(W=1.15, H=0.65, tile="scrub_a", cards=(64, 26), size=0.44, twigs=4,
                      stem=0.018, flat=0.55),
    "Green_Bush": dict(W=1.45, H=0.95, tile="scrub_b", cards=(62, 31), size=0.52, twigs=5,
                       stem=0.030, flat=0.75),
    "Verge_Tuft": dict(W=0.95, H=0.72, tile="grass_tuft", cards=(40, 16), size=0.56, twigs=0,
                       stem=0.0, flat=0.9),
}


def shrub(kind, lod, seed=7):
    p = SHRUB[kind]
    r = rng(seed + hash(kind) % 983 + lod * 13)
    b = Builder()
    rect = uv_rect(F, p["tile"])
    W, Hh = p["W"], p["H"]
    if lod == 2:
        crossed_billboard(b, Hh, W, uv_rect(F, "bb_shrub"), planes=2, pivot_frac=0.5)
        return b
    C = Vector((0, 0, Hh * 0.52))
    if p["twigs"] and lod == 0:
        for i in range(p["twigs"]):
            az = 2 * math.pi * i / p["twigs"] + r.uniform(-0.3, 0.3)
            el = math.radians(r.uniform(52, 80))
            d = _dir(az, el)
            tube(b, [(0, 0, 0), tuple(d * (Hh * 0.75))], [p["stem"] * 1.4, p["stem"] * 0.5], 3,
                 uv_rect(BK, "bark_stem", 2.0, 1024.0), tile_m=1.0)
    n = p["cards"][lod]
    size = p["size"] * (1.0 if lod == 0 else 1.45)
    upright = p["tile"] == "grass_tuft"
    for _ in range(n):
        while True:
            u = Vector((r.gauss(0, 1), r.gauss(0, 1), r.gauss(0, 1)))
            if u.length > 1e-3:
                break
        u.normalize()
        sh = r.uniform(0.52, 1.0) ** 0.45
        pos = C + Vector((u.x * W * 0.5, u.y * W * 0.5, u.z * Hh * 0.5 * p["flat"])) * sh
        pos.z = max(pos.z, size * 0.46)
        radial = Vector((pos.x, pos.y, (pos.z - C.z) * 1.6 + 0.25)).normalized()
        if upright:
            right = Vector((-u.y, u.x, 0))
            if right.length < 1e-3:
                right = Vector((1, 0, 0))
            right.normalize()
            up = Vector((0, 0, 1))
            pos.z = size * 0.48
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
        # grass blades grow FROM the ground; dome shrubs sit on it
        pos.z = s * 0.50 if upright else max(pos.z, s * 0.72)
        card(b, pos, right, up, s * r.uniform(0.85, 1.1), s, rect,
             mix_normal(cn, radial, 0.80), flip_u=r.random() < 0.5)
    return b

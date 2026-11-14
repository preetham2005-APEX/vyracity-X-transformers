"""Procedural road-wear decal generation for Veyra City.

Everything here is generated from seeded numpy noise - no photographs, no traced or sampled
imagery. The reference captures were read for WHERE wear accumulates and WHAT SHAPE it takes
only; no colour, black level or contrast value is taken from them (they are compressed video).

Each generator returns a dict of float arrays for one atlas cell:
    albedo (n, n, 3)  linear-light colour the decal paints onto the road
    alpha  (n, n)     decal coverage, already faded to 0 at the cell border
    height (n, n)     relief in METRES (negative = into the road), used for the normal map
    smooth (n, n)     target smoothness (URP: 1 - roughness)
    ao     (n, n)     ambient occlusion
"""
import numpy as np

ASPHALT_SMOOTH = 0.25          # reference smoothness of dry Veyra asphalt
PAINT_T = 0.0015               # thermoplastic lane-marking thickness (m)


# --------------------------------------------------------------------------------- noise tools

def rng(seed):
    return np.random.default_rng(seed)


def _smoothstep_f(t):
    return t * t * (3.0 - 2.0 * t)


def value_noise(n, cx, cy, seed):
    """Tileable bilinear value noise, cx cells across, cy cells down."""
    g = rng(seed).random((cy + 1, cx + 1))
    g[-1, :] = g[0, :]
    g[:, -1] = g[:, 0]
    tx = np.arange(n) / n * cx
    ty = np.arange(n) / n * cy
    ix, fx = np.floor(tx).astype(int), _smoothstep_f(tx - np.floor(tx))
    iy, fy = np.floor(ty).astype(int), _smoothstep_f(ty - np.floor(ty))
    a = g[iy[:, None], ix[None, :]]
    b = g[iy[:, None], ix[None, :] + 1]
    c = g[iy[:, None] + 1, ix[None, :]]
    d = g[iy[:, None] + 1, ix[None, :] + 1]
    fxx, fyy = fx[None, :], fy[:, None]
    return (a * (1 - fxx) + b * fxx) * (1 - fyy) + (c * (1 - fxx) + d * fxx) * fyy


def fbm(n, cx, cy, seed, octaves=4, gain=0.5, lac=2):
    out = np.zeros((n, n))
    amp, norm = 1.0, 0.0
    for o in range(octaves):
        out += amp * value_noise(n, max(1, int(cx * lac ** o)), max(1, int(cy * lac ** o)),
                                 seed + 977 * o)
        norm += amp
        amp *= gain
    return out / norm


def ss(a, b, x):
    t = np.clip((x - a) / (b - a + 1e-12), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def uv(n):
    g = (np.arange(n) + 0.5) / n
    return g[None, :] * np.ones((n, 1)), g[:, None] * np.ones((1, n))


def voronoi(n, cells, seed, jitter=0.85):
    """Tileable jittered-grid Voronoi; returns (F1, F2) in cell units."""
    pts = rng(seed).random((cells, cells, 2)) * jitter + (1.0 - jitter) * 0.5
    t = (np.arange(n) + 0.5) / n * cells
    ic = np.floor(t).astype(int)
    fc = t - ic
    f1 = np.full((n, n), 1e9)
    f2 = np.full((n, n), 1e9)
    for dy in (-1, 0, 1):
        for dx in (-1, 0, 1):
            cxi = (ic[None, :] + dx) % cells
            cyi = (ic[:, None] + dy) % cells
            px = pts[cyi, cxi, 0] + dx - fc[None, :]
            py = pts[cyi, cxi, 1] + dy - fc[:, None]
            d = np.sqrt(px * px + py * py)
            lo = np.minimum(f1, d)
            f2 = np.minimum(f2, np.maximum(f1, d))
            f1 = lo
    return f1, f2


def seg_distance(u, v, p0, p1):
    dx, dy = p1[0] - p0[0], p1[1] - p0[1]
    L2 = dx * dx + dy * dy
    t = np.clip(((u - p0[0]) * dx + (v - p0[1]) * dy) / L2, 0.0, 1.0)
    return np.hypot(u - (p0[0] + t * dx), v - (p0[1] + t * dy))


def border_fade(n, margin=0.07):
    u, v = uv(n)
    return (ss(0.0, margin, u) * ss(0.0, margin, 1 - u)
            * ss(0.0, margin, v) * ss(0.0, margin, 1 - v))


def blur(a, k=3):
    out = a.astype(np.float64)
    for _ in range(k):
        out = (out + np.roll(out, 1, 0) + np.roll(out, -1, 0)
               + np.roll(out, 1, 1) + np.roll(out, -1, 1)) / 5.0
    return out


def base(n):
    return dict(albedo=np.zeros((n, n, 3)), alpha=np.zeros((n, n)),
                height=np.zeros((n, n)), smooth=np.full((n, n), ASPHALT_SMOOTH),
                ao=np.ones((n, n)))


def tint(d, rgb, mask=None):
    m = 1.0 if mask is None else mask[:, :, None]
    d["albedo"] = d["albedo"] * (1 - m) + np.array(rgb)[None, None, :] * m


def finish(d, n, margin=0.07):
    d["alpha"] = np.clip(d["alpha"], 0.0, 1.0) * border_fade(n, margin)
    d["smooth"] = np.clip(d["smooth"], 0.0, 1.0)
    d["ao"] = np.clip(d["ao"], 0.0, 1.0)
    return d


# ------------------------------------------------------------------------------- tyre and scuff
# World cell: 4.00 x 4.00 m. Wheel paths sit at roughly a quarter and three quarters of a lane.

def tyre_twin_track(n, seed=11):
    d = base(n)
    u, v = uv(n)
    streak = fbm(n, 2, 14, seed, octaves=4)
    grain = fbm(n, 26, 90, seed + 3, octaves=3)
    m = np.zeros((n, n))
    for cx, w in ((0.255, 0.085), (0.745, 0.085)):
        core = np.exp(-np.abs((u - cx) / w) ** 3.2 * 1.1)
        m = np.maximum(m, core)
    m *= 0.80 + 0.20 * streak
    m *= 0.84 + 0.16 * grain
    d["alpha"] = m * 0.84
    tint(d, (0.016, 0.015, 0.015))
    d["smooth"] = ASPHALT_SMOOTH + 0.16 * m          # polished by traffic
    d["height"] = -0.0008 * m
    d["ao"] = 1.0 - 0.10 * m
    return finish(d, n)


def tyre_braking(n, seed=23):
    d = base(n)
    u, v = uv(n)
    band = np.exp(-((u - 0.5) / 0.21) ** 2 * 1.3)
    ramp = ss(1.15, 0.05, v)                            # heaviest where the wheel locked
    stripes = 0.60 + 0.40 * fbm(n, 2, 34, seed, octaves=3)
    smear = 0.70 + 0.30 * fbm(n, 6, 24, seed + 7, octaves=3)
    m = band * ramp * stripes * smear
    d["alpha"] = np.clip(m * 0.95, 0, 1)
    tint(d, (0.012, 0.012, 0.013))
    d["smooth"] = ASPHALT_SMOOTH + 0.10 * m
    d["height"] = 0.0004 * m                           # rubber laid on top
    d["ao"] = 1.0 - 0.18 * m
    return finish(d, n)


def tyre_turn_scuff(n, seed=37):
    d = base(n)
    u, v = uv(n)
    m = np.zeros((n, n))
    for (cx, cy, r0, w) in ((-0.35, -0.10, 1.00, 0.075), (-0.35, -0.10, 1.22, 0.065)):
        r = np.hypot(u - cx, v - cy) + 0.045 * fbm(n, 5, 5, seed + int(r0 * 50))
        arc = np.exp(-((r - r0) / w) ** 2 * 1.8)
        ang = np.arctan2(v - cy, u - cx)
        arc *= ss(0.05, 0.45, ang) * ss(1.45, 1.00, ang)
        m = np.maximum(m, arc)
    m *= 0.5 + 0.5 * fbm(n, 8, 30, seed + 5, octaves=3)
    d["alpha"] = m * 0.70
    tint(d, (0.017, 0.016, 0.016))
    d["smooth"] = ASPHALT_SMOOTH + 0.13 * m
    d["ao"] = 1.0 - 0.14 * m
    return finish(d, n)


def tyre_junction_polish(n, seed=41):
    d = base(n)
    u, v = uv(n)
    blob = np.exp(-(((u - 0.5) / 0.42) ** 2 + ((v - 0.5) / 0.46) ** 2) * 1.1)
    blob *= 0.45 + 0.55 * fbm(n, 4, 4, seed, octaves=4)
    polish = ss(0.45, 0.85, fbm(n, 14, 14, seed + 2, octaves=4))
    d["alpha"] = np.clip(blob * 0.42 + polish * blob * 0.25, 0, 1)
    tint(d, (0.085, 0.084, 0.086))
    tint(d, (0.135, 0.133, 0.130), polish * blob)      # polished aggregate reads lighter
    d["smooth"] = ASPHALT_SMOOTH + 0.22 * blob
    d["ao"] = 1.0 - 0.06 * blob
    return finish(d, n)


# ------------------------------------------------------------------------------------- cracking
# World cell: 3.00 x 3.00 m.

def _crack_net(n, cells, seed, width=0.055):
    f1, f2 = voronoi(n, cells, seed)
    edge = f2 - f1
    return ss(width, 0.0, edge)


def crack_alligator_dense(n, seed=101):
    d = base(n)
    net = _crack_net(n, 22, seed, 0.060)
    cover = ss(0.34, 0.68, fbm(n, 3, 3, seed + 1, octaves=4))
    sub = _crack_net(n, 44, seed + 9, 0.040) * ss(0.55, 0.85, fbm(n, 5, 5, seed + 4))
    m = np.clip(np.maximum(net, sub * 0.8) * cover, 0, 1)
    m *= 0.7 + 0.3 * fbm(n, 30, 30, seed + 2, octaves=3)
    d["alpha"] = m
    tint(d, (0.018, 0.017, 0.017))
    d["height"] = -0.010 * m
    d["smooth"] = ASPHALT_SMOOTH - 0.14 * m
    d["ao"] = 1.0 - 0.75 * blur(m, 2)
    return finish(d, n)


def crack_alligator_sparse(n, seed=113):
    d = base(n)
    net = _crack_net(n, 11, seed, 0.050)
    cover = ss(0.46, 0.80, fbm(n, 2, 2, seed + 1, octaves=4))
    m = np.clip(net * cover, 0, 1) * (0.75 + 0.25 * fbm(n, 24, 24, seed + 3))
    d["alpha"] = m
    tint(d, (0.020, 0.019, 0.019))
    d["height"] = -0.008 * m
    d["smooth"] = ASPHALT_SMOOTH - 0.12 * m
    d["ao"] = 1.0 - 0.65 * blur(m, 2)
    return finish(d, n)


def _wander(n, seed, amp=0.055, cells=5):
    return 0.5 + amp * (fbm(n, 1, cells, seed, octaves=4) - 0.5) * 2.0


def crack_longitudinal(n, seed=127):
    d = base(n)
    u, v = uv(n)
    path = _wander(n, seed)
    w = 0.006 + 0.004 * fbm(n, 1, 9, seed + 1)
    m = ss(1.0, 0.0, np.abs(u - path) / (w + 1e-9))
    for (p0, p1, sc) in (((0.5, 0.26), (0.78, 0.10), 0.7),
                         ((0.5, 0.62), (0.22, 0.80), 0.65),
                         ((0.5, 0.84), (0.68, 0.96), 0.5)):
        br = ss(0.007, 0.0, seg_distance(u, v, p0, p1)) * sc
        m = np.maximum(m, br * ss(0.3, 0.7, fbm(n, 7, 7, seed + 11)))
    m = np.clip(m * (0.7 + 0.3 * fbm(n, 20, 60, seed + 5)), 0, 1)
    d["alpha"] = m
    tint(d, (0.016, 0.015, 0.015))
    d["height"] = -0.013 * m
    d["smooth"] = ASPHALT_SMOOTH - 0.15 * m
    d["ao"] = 1.0 - 0.80 * blur(m, 2)
    return finish(d, n)


def crack_sealed(n, seed=139):
    d = base(n)
    u, v = uv(n)
    path = _wander(n, seed, amp=0.07)
    bead = ss(0.030, 0.004, np.abs(u - path)) * (0.85 + 0.15 * fbm(n, 2, 20, seed + 2))
    core = ss(0.009, 0.001, np.abs(u - path))
    d["alpha"] = np.clip(bead, 0, 1)
    tint(d, (0.022, 0.021, 0.021))
    tint(d, (0.013, 0.013, 0.014), core)
    d["height"] = 0.0045 * bead - 0.0025 * core        # tar bead stands slightly proud
    d["smooth"] = ASPHALT_SMOOTH + 0.30 * bead         # sealant is glossier than asphalt
    d["ao"] = 1.0 - 0.25 * bead
    return finish(d, n)


# -------------------------------------------------------------------------- lane-marking wear
# World cell: 4.00 x 4.00 m, so a 0.15 m line is 0.0375 of the cell.

WHITE = (0.530, 0.528, 0.515)
YELLOW = (0.430, 0.265, 0.032)


def _paint_band(n, centre, width, seed, wear=0.5, along=True):
    u, v = uv(n)
    c = u if along else v
    band = ss(1.0, 0.82, np.abs(c - centre) / (width * 0.5))
    erosion = fbm(n, 5, 16, seed, octaves=4) if along else fbm(n, 16, 5, seed, octaves=4)
    keep = ss(wear - 0.10, wear + 0.18, erosion)
    return band, np.clip(band * keep, 0, 1)


def _scuff(d, n, seed, amount=0.35):
    u, v = uv(n)
    s = ss(0.55, 0.9, fbm(n, 4, 26, seed, octaves=3))
    d["albedo"] *= (1.0 - amount * s)[:, :, None]
    return s


def mark_edge_line_worn(n, seed=211):
    d = base(n)
    band, m = _paint_band(n, 0.5, 0.150, seed, wear=0.34)
    d["alpha"] = m
    tint(d, WHITE)
    _scuff(d, n, seed + 1, 0.30)
    d["height"] = PAINT_T * m
    d["smooth"] = ASPHALT_SMOOTH + 0.12 * m
    d["ao"] = 1.0 - 0.05 * m
    return finish(d, n)


def mark_lane_dash_scuffed(n, seed=223):
    d = base(n)
    u, v = uv(n)
    band, m = _paint_band(n, 0.5, 0.150, seed, wear=0.40)
    dash = ss(0.0, 0.04, v - 0.12) * ss(0.0, 0.04, 0.87 - v)   # 3.0 m dash in a 4 m cell
    m = m * dash
    d["alpha"] = m
    tint(d, WHITE)
    _scuff(d, n, seed + 5, 0.38)
    d["height"] = PAINT_T * m
    d["smooth"] = ASPHALT_SMOOTH + 0.10 * m
    d["ao"] = 1.0 - 0.05 * m
    return finish(d, n)


def mark_double_yellow_worn(n, seed=233):
    d = base(n)
    m = np.zeros((n, n))
    for cx, sd in ((0.5 - 0.100, seed), (0.5 + 0.100, seed + 31)):
        _, mi = _paint_band(n, cx, 0.100, sd, wear=0.38)
        m = np.maximum(m, mi)
    d["alpha"] = m
    tint(d, YELLOW)
    _scuff(d, n, seed + 3, 0.34)
    d["height"] = PAINT_T * m
    d["smooth"] = ASPHALT_SMOOTH + 0.10 * m
    d["ao"] = 1.0 - 0.05 * m
    return finish(d, n)


def mark_white_heavy_worn(n, seed=241):
    d = base(n)
    band, m = _paint_band(n, 0.5, 0.150, seed, wear=0.62)
    patches = ss(0.26, 0.58, fbm(n, 2, 6, seed + 2, octaves=4))
    m = np.clip(m * patches, 0, 1)
    d["alpha"] = m
    tint(d, WHITE)
    _scuff(d, n, seed + 4, 0.45)
    d["height"] = PAINT_T * 0.7 * m
    d["smooth"] = ASPHALT_SMOOTH + 0.06 * m
    d["ao"] = 1.0 - 0.04 * m
    return finish(d, n)


# -------------------------------------------------------------------------- oil and fluid stains
# World cell: 2.00 x 2.00 m.

def _blob(n, cx, cy, r, seed, warp=0.25):
    u, v = uv(n)
    d = np.hypot((u - cx), (v - cy)) + warp * r * (fbm(n, 4, 4, seed, octaves=4) - 0.5) * 2.0
    return ss(r, r * 0.25, d), d


def oil_pooled(n, seed=301):
    d = base(n)
    core, dist = _blob(n, 0.5, 0.5, 0.30, seed)
    rim = np.clip(ss(0.42, 0.30, dist) - core * 0.85, 0, 1)
    d["alpha"] = np.clip(core * 0.92 + rim * 0.45, 0, 1)
    tint(d, (0.082, 0.076, 0.066), rim)                 # dry, absorbed rim
    tint(d, (0.013, 0.012, 0.012), core)
    d["smooth"] = ASPHALT_SMOOTH + 0.42 * core - 0.08 * rim
    d["ao"] = 1.0 - 0.18 * core
    d["height"] = -0.0004 * core
    return finish(d, n)


def oil_drips(n, seed=311):
    d = base(n)
    r = rng(seed)
    m = np.zeros((n, n))
    for i in range(14):
        cx = 0.5 + (r.random() - 0.5) * 0.55
        cy = 0.5 + (r.random() - 0.5) * 0.55
        rad = 0.025 + r.random() * 0.055
        b, _ = _blob(n, cx, cy, rad, seed + i, warp=0.35)
        m = np.maximum(m, b)
    halo = blur(m, 6)
    d["alpha"] = np.clip(m * 0.85 + halo * 0.30, 0, 1)
    tint(d, (0.030, 0.028, 0.026), halo)
    tint(d, (0.015, 0.014, 0.014), m)
    d["smooth"] = ASPHALT_SMOOTH + 0.38 * m
    d["ao"] = 1.0 - 0.15 * m
    return finish(d, n)


def oil_smeared(n, seed=317):
    d = base(n)
    u, v = uv(n)
    core, dist = _blob(n, 0.42, 0.34, 0.20, seed)
    trail = ss(0.16, 0.0, np.abs(u - (0.42 + 0.10 * ss(0.34, 1.0, v)))) * ss(0.30, 0.55, v)
    trail *= 0.35 + 0.65 * fbm(n, 5, 30, seed + 2, octaves=3)
    m = np.clip(np.maximum(core, trail * 0.8), 0, 1)
    d["alpha"] = m * 0.85
    tint(d, (0.026, 0.024, 0.023))
    tint(d, (0.014, 0.013, 0.013), core)
    d["smooth"] = ASPHALT_SMOOTH + 0.30 * core + 0.12 * trail
    d["ao"] = 1.0 - 0.14 * m
    return finish(d, n)


def oil_fresh_spill(n, seed=331):
    d = base(n)
    core, dist = _blob(n, 0.5, 0.52, 0.26, seed, warp=0.42)
    rim = np.clip(ss(0.34, 0.26, dist) - core * 0.9, 0, 1)
    d["alpha"] = np.clip(core * 0.96 + rim * 0.5, 0, 1)
    tint(d, (0.090, 0.083, 0.070), rim)
    tint(d, (0.010, 0.010, 0.011), core)
    d["smooth"] = ASPHALT_SMOOTH + 0.58 * core          # wet, mirror-ish
    d["ao"] = 1.0 - 0.22 * core
    d["height"] = -0.0006 * core
    return finish(d, n)


# ---------------------------------------------------------------------------- patches and repairs
# World cell: 3.00 x 3.00 m.

def _noisy_rect(n, x0, x1, y0, y1, seed, amp=0.035, soft=0.012):
    u, v = uv(n)
    w = amp * (fbm(n, 6, 6, seed, octaves=4) - 0.5) * 2.0
    inside = (ss(0.0, soft, u - x0 + w) * ss(0.0, soft, x1 - u + w)
              * ss(0.0, soft, v - y0 + w) * ss(0.0, soft, y1 - v + w))
    return np.clip(inside, 0, 1)


def _seam(mask, width=2):
    e = np.abs(mask - blur(mask, width))
    return np.clip(e / (e.max() + 1e-9), 0, 1)


def patch_rectangular(n, seed=401):
    d = base(n)
    m = _noisy_rect(n, 0.18, 0.82, 0.24, 0.76, seed)
    seam = np.clip(_seam(m, 3) * 1.8, 0, 1)
    grain = fbm(n, 22, 22, seed + 2, octaves=3)
    d["alpha"] = np.clip(m * 0.92 + seam, 0, 1)
    tint(d, (0.029, 0.029, 0.030))
    d["albedo"] *= (0.85 + 0.30 * grain)[:, :, None]
    tint(d, (0.018, 0.017, 0.017), seam)
    d["height"] = 0.0035 * m + 0.0022 * seam
    d["smooth"] = ASPHALT_SMOOTH + 0.06 * m + 0.20 * seam
    d["ao"] = 1.0 - 0.45 * seam
    return finish(d, n)


def patch_trench(n, seed=409):
    d = base(n)
    m = _noisy_rect(n, 0.06, 0.94, 0.38, 0.62, seed, amp=0.025)
    seam = np.clip(_seam(m, 3) * 1.8, 0, 1)
    d["alpha"] = np.clip(m * 0.95 + seam, 0, 1)
    tint(d, (0.027, 0.027, 0.028))
    d["albedo"] *= (0.88 + 0.24 * fbm(n, 26, 10, seed + 1, octaves=3))[:, :, None]
    tint(d, (0.016, 0.016, 0.016), seam)
    d["height"] = -0.0030 * m + 0.0025 * seam           # settled trench with a proud seal
    d["smooth"] = ASPHALT_SMOOTH + 0.18 * seam
    d["ao"] = 1.0 - 0.50 * seam
    return finish(d, n)


def patch_pothole_fill(n, seed=419):
    d = base(n)
    blob, dist = _blob(n, 0.5, 0.5, 0.33, seed, warp=0.45)
    seam = np.clip(_seam(blob, 3) * 1.9, 0, 1)
    d["alpha"] = np.clip(blob * 0.95 + seam, 0, 1)
    tint(d, (0.024, 0.024, 0.025))
    d["albedo"] *= (0.80 + 0.40 * fbm(n, 30, 30, seed + 3, octaves=3))[:, :, None]
    tint(d, (0.015, 0.015, 0.015), seam)
    d["height"] = 0.0045 * blob + 0.0015 * seam
    d["smooth"] = ASPHALT_SMOOTH - 0.06 * blob + 0.16 * seam
    d["ao"] = 1.0 - 0.40 * seam
    return finish(d, n)


def patch_skin_seal(n, seed=431):
    d = base(n)
    m = _noisy_rect(n, 0.12, 0.88, 0.14, 0.86, seed, amp=0.05, soft=0.03)
    web = _crack_net(n, 17, seed + 5, 0.045) * m * ss(0.35, 0.75, fbm(n, 4, 4, seed + 8))
    d["alpha"] = np.clip(m * 0.75 + web, 0, 1)
    tint(d, (0.031, 0.031, 0.032))
    tint(d, (0.020, 0.019, 0.019), web)
    d["height"] = 0.0012 * m + 0.0035 * web
    d["smooth"] = ASPHALT_SMOOTH + 0.05 * m + 0.28 * web
    d["ao"] = 1.0 - 0.25 * web
    return finish(d, n)


# ------------------------------------------------------------------------------ kerbside grime
# World cell: 2.00 x 2.00 m. u = 0 is the kerb line, u = 1 runs out into the carriageway.

def grime_kerb_gradient(n, seed=501):
    d = base(n)
    u, v = uv(n)
    g = ss(0.62, 0.0, u + 0.16 * (fbm(n, 3, 8, seed, octaves=4) - 0.5) * 2.0)
    g *= 0.70 + 0.30 * fbm(n, 6, 18, seed + 1, octaves=3)
    d["alpha"] = np.clip(g * 0.82, 0, 1)
    tint(d, (0.026, 0.025, 0.022))
    d["smooth"] = ASPHALT_SMOOTH - 0.11 * g
    d["ao"] = 1.0 - 0.22 * g
    return finish(d, n)


def grime_gutter_silt(n, seed=509):
    d = base(n)
    u, v = uv(n)
    band = ss(0.34, 0.04, u + 0.10 * (fbm(n, 2, 12, seed, octaves=4) - 0.5) * 2.0)
    grit = ss(0.62, 0.86, fbm(n, 40, 40, seed + 2, octaves=3)) * band
    d["alpha"] = np.clip(band * 0.70 + grit * 0.6, 0, 1)
    tint(d, (0.078, 0.073, 0.060))
    tint(d, (0.102, 0.095, 0.078), grit)
    d["height"] = 0.0015 * grit
    d["smooth"] = ASPHALT_SMOOTH - 0.14 * band
    d["ao"] = 1.0 - 0.30 * band - 0.15 * grit
    return finish(d, n)


def grime_shade_blotch(n, seed=521):
    d = base(n)
    blotch = ss(0.32, 0.72, fbm(n, 3, 3, seed, octaves=5))
    fine = ss(0.45, 0.80, fbm(n, 16, 16, seed + 1, octaves=3))
    d["alpha"] = np.clip(blotch * 0.72 + fine * blotch * 0.28, 0, 1)
    tint(d, (0.025, 0.025, 0.025))
    d["smooth"] = ASPHALT_SMOOTH - 0.13 * blotch
    d["ao"] = 1.0 - 0.20 * blotch
    return finish(d, n)


def grime_debris_line(n, seed=523):
    d = base(n)
    u, v = uv(n)
    r = rng(seed)
    line = ss(0.20, 0.02, np.abs(u - (0.22 + 0.06 * (fbm(n, 1, 9, seed) - 0.5) * 2.0)))
    bits = np.zeros((n, n))
    for i in range(70):
        cx = 0.22 + (r.random() - 0.5) * 0.30
        cy = r.random()
        rad = 0.006 + r.random() * 0.016
        b, _ = _blob(n, cx, cy, rad, seed + i, warp=0.5)
        bits = np.maximum(bits, b)
    d["alpha"] = np.clip(line * 0.42 + bits * 0.9, 0, 1)
    tint(d, (0.030, 0.028, 0.024), line)
    tint(d, (0.095, 0.082, 0.058), bits)
    d["height"] = 0.0022 * bits
    d["smooth"] = ASPHALT_SMOOTH - 0.10 * line
    d["ao"] = 1.0 - 0.25 * line - 0.30 * bits
    return finish(d, n)


# ------------------------------------------------------------------------------------- atlases

ATLASES = [
    dict(name="T_RW_TyreTrack", size=2048, cell_m=4.0,
         cells=[("twin_track_straight", tyre_twin_track), ("braking_scuff", tyre_braking),
                ("turn_scuff", tyre_turn_scuff), ("junction_polish", tyre_junction_polish)]),
    dict(name="T_RW_Crack", size=2048, cell_m=3.0,
         cells=[("alligator_dense", crack_alligator_dense),
                ("alligator_sparse", crack_alligator_sparse),
                ("longitudinal_branching", crack_longitudinal), ("sealed_crack", crack_sealed)]),
    dict(name="T_RW_LaneWear", size=2048, cell_m=(1.0, 4.0),
         cells=[("white_edge_line_worn", mark_edge_line_worn),
                ("white_lane_dash_scuffed", mark_lane_dash_scuffed),
                ("yellow_double_centre_worn", mark_double_yellow_worn),
                ("white_heavy_worn", mark_white_heavy_worn)]),
    dict(name="T_RW_Oil", size=1024, cell_m=2.0,
         cells=[("pooled_stain", oil_pooled), ("drip_cluster", oil_drips),
                ("smeared_trail", oil_smeared), ("fresh_spill", oil_fresh_spill)]),
    dict(name="T_RW_Patch", size=1024, cell_m=3.0,
         cells=[("rectangular_patch", patch_rectangular), ("trench_patch", patch_trench),
                ("pothole_fill", patch_pothole_fill), ("skin_seal", patch_skin_seal)]),
    dict(name="T_RW_Grime", size=1024, cell_m=2.0,
         cells=[("kerb_gradient", grime_kerb_gradient), ("gutter_silt", grime_gutter_silt),
                ("shade_blotch", grime_shade_blotch), ("debris_line", grime_debris_line)]),
]

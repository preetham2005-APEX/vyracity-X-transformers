"""
VEYRA CITY road decal atlas (master prompt §1.4/E: road wear and grime decals). Original procedural content, no third-party art.

Run headless (numpy only, Blender just saves the PNGs):
  "/d/New folder/blender.exe" -b --factory-startup -P Tools/Blender/vc_decals.py -- --out <dir>

Writes a 2048 x 2048 atlas, 4 x 4 tiles of 512 px, for URP DBuffer decals (Albedo + Normal + MAOS):
  T_Decal_Road_BaseMap.png   RGB albedo (sRGB), A = coverage
  T_Decal_Road_Normal.png    tangent-space normal (OpenGL / Unity), linear
  T_Decal_Road_Mask.png      R metallic, G ambient occlusion, B unused (0), A smoothness (1 - roughness), linear
Tiles (row-major from the top-left), see TILES below:
  0 oil stain A, 1 oil stain B, 2 oil drip trail, 3 skid mark straight,
  4 skid mark curved, 5 round manhole cover, 6 square utility cover, 7 storm drain grate,
  8 crack network A, 9 crack network B, 10 sealed patch soft, 11 sealed patch seam,
  12 tyre scuff cluster, 13 paint scuff (white), 14 dried puddle ring, 15 gutter grime
"""
import os, sys, math
import numpy as np

T = 512
N = 4
S = T * N
rng = np.random.default_rng(2611)


def arg(name, default):
    a = sys.argv
    if "--" in a:
        a = a[a.index("--") + 1:]
        if name in a:
            return a[a.index(name) + 1]
    return default


def grid():
    y, x = np.mgrid[0:T, 0:T].astype(np.float32)
    return (x + 0.5) / T * 2 - 1, (y + 0.5) / T * 2 - 1   # -1..1


def fbm(seed, octaves=5, base=4):
    r = np.random.default_rng(seed)
    out = np.zeros((T, T), np.float32)
    amp, tot = 1.0, 0.0
    for o in range(octaves):
        n = base * (2 ** o)
        g = r.random((n + 1, n + 1)).astype(np.float32)
        xs = np.linspace(0, n, T, endpoint=False)
        xi = xs.astype(int); xf = xs - xi
        xf = xf * xf * (3 - 2 * xf)
        a = g[np.ix_(xi, xi)]; b = g[np.ix_(xi, xi + 1)]; c = g[np.ix_(xi + 1, xi)]; d = g[np.ix_(xi + 1, xi + 1)]
        fx = xf[None, :]; fy = xf[:, None]
        out += amp * ((a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy)
        tot += amp; amp *= 0.5
    return out / tot


def edge(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0, 1)
    return t * t * (3 - 2 * t)


def tile_blank():
    # albedo rgb, coverage a, height (for the normal), metallic, ao, smoothness
    return dict(rgb=np.zeros((T, T, 3), np.float32), a=np.zeros((T, T), np.float32), h=np.zeros((T, T), np.float32),
                m=np.zeros((T, T), np.float32), ao=np.ones((T, T), np.float32), sm=np.full((T, T), 0.3, np.float32))


def oil(seed, stretch=1.0):
    t = tile_blank(); x, y = grid()
    n = fbm(seed, 5, 3)
    r = np.sqrt((x * stretch) ** 2 + y ** 2) + (n - 0.5) * 0.55
    core = edge(0.75, 0.15, r)
    t["a"] = core * 0.85
    t["rgb"][:] = np.stack([0.035, 0.032, 0.03])
    t["sm"] = 0.35 + core * 0.45          # oily sheen
    t["ao"] = 1 - core * 0.15
    return t


def drip(seed):
    t = tile_blank(); x, y = grid()
    n = fbm(seed, 4, 4)
    cov = np.zeros((T, T), np.float32)
    for k in range(9):
        cy = -0.8 + k * 0.2 + (n[0, k * 40 % T] - 0.5) * 0.1
        rr = 0.08 + 0.05 * math.sin(k * 1.7) ** 2
        d = np.sqrt((x * 1.4) ** 2 + (y - cy) ** 2) + (n - 0.5) * 0.12
        cov = np.maximum(cov, edge(rr, rr * 0.3, d))
    t["a"] = cov * 0.8
    t["rgb"][:] = (0.04, 0.036, 0.033)
    t["sm"] = 0.4 + cov * 0.4
    return t


def skid(seed, curve):
    t = tile_blank(); x, y = grid()
    n = fbm(seed, 4, 6)
    yy = y - curve * (x ** 2) * 0.8
    cov = np.zeros((T, T), np.float32)
    for off in (-0.33, 0.33):                 # a tyre pair
        band = edge(0.13, 0.07, np.abs(yy - off) + (n - 0.5) * 0.05)
        ribs = 0.75 + 0.25 * np.sin(yy * 220.0) ** 2
        fade = edge(1.0, 0.55, np.abs(x)) * (0.6 + 0.4 * n)
        cov = np.maximum(cov, band * ribs * fade)
    t["a"] = cov * 0.75
    t["rgb"][:] = (0.025, 0.025, 0.026)
    t["sm"] = 0.25 + cov * 0.2
    return t


def manhole(round_):
    t = tile_blank(); x, y = grid()
    r = np.sqrt(x ** 2 + y ** 2)
    if round_:
        inside = edge(0.86, 0.84, r)
        rim = edge(0.86, 0.84, r) * edge(0.74, 0.76, r)
        pattern = (np.sin((np.arctan2(y, x) * 8)) > 0.6) * edge(0.7, 0.68, r) * edge(0.2, 0.22, r)
        rings = (np.abs(((r * 10) % 1) - 0.5) < 0.08) * edge(0.7, 0.68, r)
        relief = rim * 1.0 + pattern * 0.5 + rings * 0.4
    else:
        ax, ay = np.abs(x), np.abs(y)
        m_ = np.maximum(ax, ay)
        inside = edge(0.86, 0.84, m_)
        rim = inside * edge(0.74, 0.76, m_)
        diamond = (np.abs(((x + y) * 6) % 1 - 0.5) < 0.1) | (np.abs(((x - y) * 6) % 1 - 0.5) < 0.1)
        relief = rim + diamond * edge(0.72, 0.7, m_) * 0.5
    n = fbm(77 if round_ else 78, 4, 8)
    base = 0.11 + n * 0.05
    t["rgb"] = np.stack([base * 1.05, base, base * 0.95], -1)
    rust = edge(0.55, 0.85, n) * inside
    t["rgb"] = t["rgb"] * (1 - rust[..., None] * 0.6) + np.stack([0.22, 0.11, 0.05])[None, None, :] * rust[..., None] * 0.6
    t["a"] = inside
    t["h"] = relief * inside
    t["m"] = inside * (0.65 - rust * 0.5)
    t["sm"] = 0.35 + relief * 0.25
    t["ao"] = 1 - edge(0.86, 0.8, r if round_ else np.maximum(np.abs(x), np.abs(y))) * 0.35
    return t


def grate():
    t = tile_blank(); x, y = grid()
    frame = (np.abs(x) < 0.9) & (np.abs(y) < 0.55)
    slots = frame & ((np.abs(x) > 0.82) | (np.abs(y) > 0.47) | (np.abs((x * 7) % 1 - 0.5) < 0.18))
    t["a"] = frame.astype(np.float32)
    dark = frame & ~slots
    t["rgb"][:] = 0.12
    t["rgb"][dark] = 0.015                     # the drain below
    t["h"] = slots.astype(np.float32)
    t["m"] = slots * 0.7
    t["ao"] = np.where(dark, 0.2, 1.0)
    t["sm"] = np.where(slots, 0.45, 0.1)
    return t


def cracks(seed):
    t = tile_blank()
    r = np.random.default_rng(seed)
    cov = np.zeros((T, T), np.float32)
    for k in range(6):
        px, py = r.random() * T, r.random() * T
        ang = r.random() * math.tau
        for s in range(int(150 + r.random() * 250)):
            ang += (r.random() - 0.5) * 0.7
            px += math.cos(ang) * 1.3; py += math.sin(ang) * 1.3
            ix, iy = int(px) % T, int(py) % T
            w = 1 if r.random() < 0.7 else 2
            cov[max(0, iy - w):iy + w + 1, max(0, ix - w):ix + w + 1] = 1.0
            if r.random() < 0.015: ang += (1 if r.random() < 0.5 else -1) * 1.2
    halo = np.clip(cov + np.roll(cov, 2, 0) * 0.4 + np.roll(cov, 2, 1) * 0.4, 0, 1)
    x, y = grid()
    fade = edge(1.0, 0.7, np.maximum(np.abs(x), np.abs(y)))
    t["a"] = halo * 0.9 * fade
    t["rgb"][:] = 0.03
    t["h"] = -cov
    t["ao"] = 1 - cov * 0.6
    t["sm"][:] = 0.15
    return t


def patch(seed, seam):
    t = tile_blank(); x, y = grid()
    n = fbm(seed, 5, 5)
    box = np.maximum(np.abs(x) * 1.0, np.abs(y) * 1.35) + (n - 0.5) * (0.04 if seam else 0.18)
    inside = edge(0.86, 0.78, box)
    t["a"] = inside * 0.55
    tone = 0.19 + n * 0.04
    t["rgb"] = np.stack([tone, tone, tone * 1.02], -1)
    if seam:
        line = edge(0.035, 0.0, np.abs(box - 0.82))
        t["rgb"] = t["rgb"] * (1 - line[..., None]) + 0.03 * line[..., None]
        t["a"] = np.maximum(t["a"], line * 0.9)
        t["h"] = line * 0.6
    t["sm"] = 0.22 + inside * 0.08
    return t


def scuff(seed):
    t = tile_blank(); x, y = grid()
    r = np.random.default_rng(seed)
    cov = np.zeros((T, T), np.float32)
    for k in range(7):
        a = r.random() * math.pi; ca, sa = math.cos(a), math.sin(a)
        cx, cy = (r.random() - 0.5) * 1.2, (r.random() - 0.5) * 1.2
        u = (x - cx) * ca + (y - cy) * sa; v = -(x - cx) * sa + (y - cy) * ca
        cov = np.maximum(cov, edge(0.06, 0.02, np.abs(v)) * edge(0.6, 0.2, np.abs(u)) * (0.5 + 0.5 * r.random()))
    t["a"] = cov * 0.6
    t["rgb"][:] = 0.03
    t["sm"] = 0.2 + cov * 0.1
    return t


def paint_scuff(seed):
    t = tile_blank(); x, y = grid()
    n = fbm(seed, 5, 6)
    band = edge(0.18, 0.12, np.abs(y)) * edge(1.0, 0.6, np.abs(x))
    worn = band * edge(0.35, 0.6, n)
    t["a"] = worn * 0.85
    t["rgb"][:] = 0.78
    t["sm"] = 0.3 + worn * 0.1
    return t


def puddle(seed):
    t = tile_blank(); x, y = grid()
    n = fbm(seed, 5, 3)
    r = np.sqrt(x ** 2 + (y * 1.3) ** 2) + (n - 0.5) * 0.5
    ring = edge(0.06, 0.0, np.abs(r - 0.62))
    t["a"] = ring * 0.6 + edge(0.62, 0.3, r) * 0.12
    t["rgb"][:] = 0.07
    t["sm"] = 0.25
    return t


def gutter(seed):
    t = tile_blank(); x, y = grid()
    n = fbm(seed, 5, 6)
    band = edge(-0.2, 0.9, y)                 # grime builds towards the kerb (tile bottom)
    cov = band * (0.5 + 0.5 * n)
    t["a"] = cov * 0.7
    t["rgb"] = np.stack([0.06 + n * 0.02, 0.055 + n * 0.02, 0.045 + n * 0.015], -1)
    t["ao"] = 1 - cov * 0.25
    t["sm"] = 0.12 + cov * 0.18
    return t


TILES = [lambda: oil(1, 1.0), lambda: oil(2, 1.6), lambda: drip(3), lambda: skid(4, 0.0),
         lambda: skid(5, 1.0), lambda: manhole(True), lambda: manhole(False), grate,
         lambda: cracks(9), lambda: cracks(10), lambda: patch(11, False), lambda: patch(12, True),
         lambda: scuff(13), lambda: paint_scuff(14), lambda: puddle(15), lambda: gutter(16)]


def normal_from_height(h, strength=4.0):
    gx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * 0.5 * strength
    gy = (np.roll(h, -1, 0) - np.roll(h, 1, 0)) * 0.5 * strength
    nz = np.ones_like(h)
    l = np.sqrt(gx * gx + gy * gy + nz * nz)
    return np.stack([-gx / l * 0.5 + 0.5, gy / l * 0.5 + 0.5, nz / l * 0.5 + 0.5], -1)   # OpenGL (+Y up, image rows down)


def save(arr, path, srgb):
    import bpy
    h, w = arr.shape[:2]
    img = bpy.data.images.new(os.path.basename(path), w, h, alpha=True, float_buffer=False)
    if not srgb:
        img.colorspace_settings.name = "Non-Color"
    px = np.flipud(arr).astype(np.float32).reshape(-1)
    img.pixels.foreach_set(px)
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()
    bpy.data.images.remove(img)


def main():
    out = arg("--out", "ArtSource/Blender/Decals")
    os.makedirs(out, exist_ok=True)
    base = np.zeros((S, S, 4), np.float32)
    nrm = np.zeros((S, S, 4), np.float32); nrm[..., 0:3] = (0.5, 0.5, 1.0); nrm[..., 3] = 1
    msk = np.zeros((S, S, 4), np.float32); msk[..., 1] = 1
    for i, make in enumerate(TILES):
        t = make()
        r0, c0 = (i // N) * T, (i % N) * T
        base[r0:r0 + T, c0:c0 + T, 0:3] = np.clip(t["rgb"], 0, 1)
        base[r0:r0 + T, c0:c0 + T, 3] = np.clip(t["a"], 0, 1)
        nrm[r0:r0 + T, c0:c0 + T, 0:3] = normal_from_height(t["h"])
        msk[r0:r0 + T, c0:c0 + T, 0] = np.clip(t["m"], 0, 1)
        msk[r0:r0 + T, c0:c0 + T, 1] = np.clip(t["ao"], 0, 1)
        msk[r0:r0 + T, c0:c0 + T, 3] = np.clip(t["sm"], 0, 1)
    # dilate albedo colour into transparent texels so mips do not bleed black (coverage stays in alpha)
    # review preview (not imported): each tile over a mid-grey asphalt with a little noise, coverage applied
    bg = 0.28 + (rng.random((S, S)) - 0.5) * 0.04
    prev = np.zeros((S, S, 4), np.float32); prev[..., 3] = 1
    for c in range(3): prev[..., c] = bg * (1 - base[..., 3]) + base[..., c] * base[..., 3]
    save(prev, os.path.join(out, "Preview_Decal_Road.png"), True)
    save(base, os.path.join(out, "T_Decal_Road_BaseMap.png"), True)
    save(nrm, os.path.join(out, "T_Decal_Road_Normal.png"), False)
    save(msk, os.path.join(out, "T_Decal_Road_Mask.png"), False)
    print(f"decal atlas {S}x{S}, {len(TILES)} tiles -> {out}")


main()

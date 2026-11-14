"""VEYRA CITY - original tiling PBR textures for the building kit.

    blender --factory-startup -b -noaudio -P make_textures.py

Writes, for every kit material, to ArtSource/Blender/Buildings/textures/:
    <material>_baseColor.png          sRGB albedo
    <material>_metallicRoughness.png  glTF packing: G = roughness, B = metallic (what KitbashMaterials reads)
    <material>_normal.png             OpenGL tangent-space normal, derived from the pattern's height field

512 x 512, power of two, every pattern seamless (all filtering wraps). One texture covers 2.0 m, which is the
cube_size the kit meshes are box-projected with, so brick courses and siding boards come out at real size.
Everything is generated from noise and arithmetic here - no scanned, downloaded or third-party source.
"""
import os

import numpy as np

N = 512
TILE_M = 2.0                      # metres covered by one texture tile
HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(os.path.dirname(HERE), "textures")
rng = np.random.default_rng(20261113)

u = (np.arange(N) + 0.5) / N
U, V = np.meshgrid(u, u)          # U across, V up; both in 0..1
X, Y = U * TILE_M, V * TILE_M     # metres


def blur(a, r=2, passes=2):
    """Wrap-around box blur, so every pattern stays seamless."""
    for _ in range(passes):
        acc = np.zeros_like(a)
        for dy in range(-r, r + 1):
            for dx in range(-r, r + 1):
                acc += np.roll(np.roll(a, dy, 0), dx, 1)
        a = acc / ((2 * r + 1) ** 2)
    return a


def noise(scale=1.0, octaves=4, seed=0):
    """Periodic value noise: random field at a few resolutions, smoothed with wrap-around blur."""
    g = np.random.default_rng(20261113 + seed)
    out = np.zeros((N, N))
    amp = 1.0
    tot = 0.0
    for o in range(octaves):
        step = max(1, N // (2 ** (o + 2)))
        small = g.random((N // step + 1, N // step + 1))
        big = np.kron(small, np.ones((step, step)))[:N, :N]
        out += blur(big, r=max(1, step // 3), passes=1) * amp
        tot += amp
        amp *= 0.5
    out /= tot
    return (out - out.min()) / max(1e-6, float(np.ptp(out))) * scale


def srgb(x):
    x = np.clip(x, 0.0, 1.0)
    return np.where(x <= 0.0031308, x * 12.92, 1.055 * np.power(x, 1 / 2.4) - 0.055)


def save(name, rgb, data=False):
    import bpy
    img = bpy.data.images.new(name, N, N, alpha=False, float_buffer=False)
    img.colorspace_settings.name = "Non-Color"
    px = np.zeros((N, N, 4), dtype=np.float32)
    px[:, :, :3] = np.clip(rgb, 0.0, 1.0)
    px[:, :, 3] = 1.0
    img.pixels.foreach_set(px.reshape(-1))
    img.file_format = "PNG"
    path = os.path.join(OUT, name + ".png")
    img.filepath_raw = path
    img.save()
    bpy.data.images.remove(img)
    return path


def normal_from_height(h, strength=2.0):
    """OpenGL tangent-space normal map (+Y up) from a height field; gradients wrap."""
    dx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * 0.5 * strength * N / 64.0
    dy = (np.roll(h, -1, 0) - np.roll(h, 1, 0)) * 0.5 * strength * N / 64.0
    nz = np.ones_like(h)
    ln = np.sqrt(dx * dx + dy * dy + nz * nz)
    return np.stack([(-dx / ln) * 0.5 + 0.5, (-dy / ln) * 0.5 + 0.5, (nz / ln) * 0.5 + 0.5], axis=-1)


def write(name, albedo_lin, height, rough, metal=0.0):
    save(name + "_baseColor", srgb(albedo_lin))
    mr = np.zeros((N, N, 3))
    mr[:, :, 1] = np.clip(rough, 0.0, 1.0)
    mr[:, :, 2] = np.clip(np.broadcast_to(np.asarray(metal, dtype=float), (N, N)), 0.0, 1.0)
    save(name + "_metallicRoughness", mr)
    save(name + "_normal", normal_from_height(height))


def tint(base, var):
    """Linear RGB field from a base colour plus a per-pixel multiplier."""
    return np.stack([base[0] * var, base[1] * var, base[2] * var], axis=-1)


# ------------------------------------------------------------------ patterns

def bricks(course_h=0.075, brick_l=0.23, joint=0.012):
    """Running bond. Returns (mask of mortar, per-brick random id)."""
    row = np.floor(Y / course_h)
    offset = (row % 2) * (brick_l / 2.0)
    col = np.floor((X + offset) / brick_l)
    fy = Y / course_h - row
    fx = (X + offset) / brick_l - col
    jy = joint / course_h / 2.0
    jx = joint / brick_l / 2.0
    mortar = (fy < jy) | (fy > 1 - jy) | (fx < jx) | (fx > 1 - jx)
    ident = ((row * 73 + col * 149) % 17) / 17.0
    return mortar, ident


def lap_siding(board=0.20, groove=0.010):
    row = np.floor(Y / board)
    f = Y / board - row
    g = f < (groove / board)
    h = 1.0 - f * 0.55 - g * 0.9
    ident = ((row * 53) % 11) / 11.0
    return g, h, ident


def tiles_roof(course=0.33, width=0.26):
    """Pantile-like rows: a rounded profile across the width, a step at each course."""
    row = np.floor(Y / course)
    f = Y / course - row
    col = np.floor((X + (row % 2) * width / 2.0) / width)
    fx = (X + (row % 2) * width / 2.0) / width - col
    h = np.sin(np.pi * np.clip(fx, 0, 1)) * 0.6 + (1.0 - f) * 0.5
    edge = (f < 0.06)
    ident = ((row * 31 + col * 97) % 13) / 13.0
    return edge, h, ident


def shingles(course=0.16, width=0.30):
    row = np.floor(Y / course)
    f = Y / course - row
    col = np.floor((X + (row % 2) * width / 2.0) / width)
    fx = (X + (row % 2) * width / 2.0) / width - col
    gap = (f < 0.05) | (fx < 0.012) | (fx > 0.988)
    h = (1.0 - f) * 0.5 - gap * 0.8
    ident = ((row * 41 + col * 137) % 19) / 19.0
    return gap, h, ident


def ribs(pitch=0.25, w=0.035, vertical=True):
    a = X if vertical else Y
    f = (a / pitch) - np.floor(a / pitch)
    rib = f < (w / pitch)
    return rib, rib * 1.0


# ------------------------------------------------------------------ the kit materials

def main():
    os.makedirs(OUT, exist_ok=True)
    grain = noise(1.0, 4, 1)
    fine = noise(1.0, 5, 2)
    dirt = blur(noise(1.0, 3, 3), r=3, passes=2)

    # --- stucco (two colourways)
    for name, col in (("VC_Wall_Stucco", (0.600, 0.570, 0.505)),
                      ("VC_Wall_StuccoWarm", (0.620, 0.520, 0.405))):
        h = fine * 0.35 + grain * 0.15
        var = 0.90 + fine * 0.14 + (dirt - 0.5) * 0.10
        write(name, tint(col, var), h, 0.62 + fine * 0.16)

    # --- brick
    mortar, ident = bricks()
    base = np.stack([np.full((N, N), 0.330), np.full((N, N), 0.165), np.full((N, N), 0.120)], -1)
    base *= (0.72 + ident[..., None] * 0.55)                      # brick-to-brick colour spread
    base *= (0.92 + fine[..., None] * 0.16)
    mort_col = np.array([0.52, 0.51, 0.48])
    alb = np.where(mortar[..., None], mort_col * (0.85 + fine[..., None] * 0.25), base)
    h = np.where(mortar, 0.0, 0.85 + ident * 0.1) + fine * 0.10
    write("VC_Wall_Brick", alb, h, np.where(mortar, 0.88, 0.72 + fine * 0.14))

    # --- board / lap siding
    g, hs, ident = lap_siding()
    var = 0.88 + ident * 0.18 + fine * 0.10
    alb = tint((0.470, 0.470, 0.445), var) * np.where(g, 0.55, 1.0)[..., None]
    write("VC_Wall_Board", alb, hs + grain * 0.06, 0.58 + fine * 0.12)

    # --- concrete
    pits = (noise(1.0, 5, 7) > 0.86) * 0.5
    alb = tint((0.400, 0.398, 0.385), 0.88 + dirt * 0.22 + fine * 0.08) * (1.0 - pits * 0.3)[..., None]
    write("VC_Wall_Concrete", alb, fine * 0.25 - pits * 0.5, 0.70 + dirt * 0.16)

    # --- roof tile
    edge, h, ident = tiles_roof()
    alb = tint((0.185, 0.115, 0.090), 0.78 + ident * 0.45 + fine * 0.12) * np.where(edge, 0.55, 1.0)[..., None]
    write("VC_Roof_Tile", alb, h, 0.64 + fine * 0.14)

    # --- roof shingle
    gap, h, ident = shingles()
    alb = tint((0.105, 0.102, 0.098), 0.70 + ident * 0.55 + fine * 0.18) * np.where(gap, 0.5, 1.0)[..., None]
    write("VC_Roof_Shingle", alb, h, 0.74 + fine * 0.14)

    # --- standing-seam metal roof
    rib, h = ribs(0.28, 0.035, vertical=True)
    alb = tint((0.225, 0.235, 0.240), 0.90 + fine * 0.14) * np.where(rib, 1.18, 1.0)[..., None]
    write("VC_Roof_Metal", alb, h * 0.8 + fine * 0.05, 0.38 + fine * 0.12, metal=0.85)

    # --- painted trim
    for name, col, rough in (("VC_Trim_Paint", (0.740, 0.730, 0.700), 0.42),
                             ("VC_Trim_Dark", (0.095, 0.095, 0.092), 0.48)):
        write(name, tint(col, 0.95 + fine * 0.08), fine * 0.08, rough + fine * 0.10)

    # --- glass
    sheen = 0.85 + blur(noise(1.0, 2, 11), r=6, passes=2) * 0.30
    write("VC_Glass_Window", tint((0.105, 0.130, 0.150), sheen), fine * 0.02, 0.07 + fine * 0.05)

    # --- door: vertical grain
    gr = blur(noise(1.0, 5, 13), r=1, passes=1)
    gr = 0.80 + 0.35 * np.sin(X * 46.0 + gr * 6.0) * 0.25 + gr * 0.2
    write("VC_Door_Wood", tint((0.175, 0.105, 0.065), gr), gr * 0.10, 0.46 + fine * 0.10)

    # --- garage door: horizontal ribs
    rib, h = ribs(0.22, 0.030, vertical=False)
    alb = tint((0.400, 0.405, 0.405), 0.93 + fine * 0.10) * np.where(rib, 0.88, 1.0)[..., None]
    write("VC_Metal_Garage", alb, h * 0.7, 0.42 + fine * 0.10, metal=0.55)

    towers()
    neon()
    coast_town()
    mid_ind()
    rural_air()
    print("VC_TEXTURES_OK:" + OUT)


def rural_air():
    """D4 Alpine, D8 Rural, D7 Aviation and D5 retail materials, 2.0 m tile."""
    global X, Y
    X, Y = U * 2.0, V * 2.0
    fine = noise(1.0, 5, 107)
    dirt = blur(noise(1.0, 3, 109), r=3, passes=2)

    # --- alpine: stacked timber boards with a strong grain, rubble stone, dark slate
    bd = Y / 0.22 - np.floor(Y / 0.22)
    gap = bd < 0.05
    grain = 0.80 + 0.26 * np.sin(X * 34.0 + blur(noise(1.0, 4, 113), r=1, passes=1) * 9.0)
    write("VC_Al_Timber", tint((0.265, 0.165, 0.098), grain * (0.92 + fine * 0.14))
          * np.where(gap, 0.55, 1.0)[..., None],
          np.where(gap, -0.8, 0.0) + grain * 0.16, 0.58 + fine * 0.12)
    cell = blur(noise(1.0, 2, 127), r=2, passes=1)
    stone = (cell * 9.0) % 1.0
    joint = (stone < 0.10)
    write("VC_Al_Stone", tint((0.360, 0.348, 0.330), 0.84 + cell * 0.30 + fine * 0.10)
          * np.where(joint, 0.70, 1.0)[..., None],
          np.where(joint, -0.8, 0.0) + cell * 0.35, 0.72 + fine * 0.12)
    gap2, sh, sid = shingles(course=0.20, width=0.30)
    write("VC_Al_Roof", tint((0.120, 0.112, 0.105), 0.72 + sid * 0.55 + fine * 0.16)
          * np.where(gap2, 0.52, 1.0)[..., None], sh, 0.58 + fine * 0.14)
    sheen = 0.84 + blur(noise(1.0, 2, 131), r=6, passes=2) * 0.30
    write("VC_Al_Glass", tint((0.095, 0.115, 0.132), sheen), fine * 0.02, 0.09 + fine * 0.04)
    write("VC_Al_Trim", tint((0.400, 0.300, 0.190), grain * (0.94 + fine * 0.10)),
          grain * 0.10, 0.50 + fine * 0.10)
    write("VC_Al_Concrete", tint((0.420, 0.410, 0.392), 0.90 + dirt * 0.18), fine * 0.18, 0.66 + dirt * 0.12)

    # --- rural: painted board-and-batten, corrugated roof and metal, weathered
    bat = X / 0.30 - np.floor(X / 0.30)
    batten = bat < 0.10
    board = 0.88 + 0.18 * np.sin(Y * 26.0 + fine * 6.0)
    write("VC_Ru_Board", tint((0.300, 0.145, 0.105), board * (0.86 + dirt * 0.26))
          * np.where(batten, 1.18, 1.0)[..., None], np.where(batten, 0.7, 0.0) + fine * 0.10,
          0.70 + fine * 0.12 + dirt * 0.08)
    cor = Y / 0.12 - np.floor(Y / 0.12)
    ch = np.sin(np.pi * cor)
    write("VC_Ru_Roof", tint((0.215, 0.205, 0.195), (0.84 + ch * 0.26) * (0.86 + dirt * 0.28)),
          ch * 0.9, 0.52 + dirt * 0.16, metal=0.35)
    cor2 = X / 0.12 - np.floor(X / 0.12)
    ch2 = np.sin(np.pi * cor2)
    write("VC_Ru_Metal", tint((0.330, 0.335, 0.338), (0.86 + ch2 * 0.22) * (0.90 + dirt * 0.18)),
          ch2 * 0.85, 0.42 + dirt * 0.14, metal=0.75)
    write("VC_Ru_Glass", tint((0.095, 0.112, 0.128), sheen), fine * 0.02, 0.11 + fine * 0.04)
    write("VC_Ru_Trim", tint((0.600, 0.585, 0.550), 0.92 + fine * 0.12), fine * 0.06, 0.50 + fine * 0.10)
    write("VC_Ru_Concrete", tint((0.430, 0.420, 0.400), 0.88 + dirt * 0.22), fine * 0.20, 0.70 + dirt * 0.12)

    # --- aviation: large profiled panels, light grey
    pitch = 0.60
    f2 = X / pitch - np.floor(X / pitch)
    rib3 = np.clip((np.abs(f2 - 0.5) - 0.30) / 0.20, 0.0, 1.0)
    write("VC_Av_Clad", tint((0.420, 0.428, 0.432), (0.90 + rib3 * 0.18) * (0.93 + dirt * 0.14)),
          rib3 * 0.8, 0.42 + fine * 0.10, metal=0.50)
    f3 = Y / pitch - np.floor(Y / pitch)
    rib4 = np.clip((np.abs(f3 - 0.5) - 0.30) / 0.20, 0.0, 1.0)
    write("VC_Av_Roof", tint((0.300, 0.308, 0.312), (0.90 + rib4 * 0.16) * (0.92 + dirt * 0.16)),
          rib4 * 0.8, 0.40 + fine * 0.10, metal=0.70)
    write("VC_Av_Glass", tint((0.110, 0.132, 0.150), sheen), fine * 0.02, 0.11 + fine * 0.04)
    brush3 = 0.90 + 0.10 * np.sin(Y * 260.0 + fine * 8.0)
    write("VC_Av_Metal", tint((0.280, 0.284, 0.288), brush3), fine * 0.05, 0.34 + fine * 0.08, metal=0.88)
    write("VC_Av_Concrete", tint((0.400, 0.394, 0.380), 0.90 + dirt * 0.18), fine * 0.18, 0.70 + dirt * 0.12)

    # --- retail
    j2 = 1.2
    seam2 = ((X / j2 - np.floor(X / j2)) < 0.016) | ((Y / j2 - np.floor(Y / j2)) < 0.016)
    write("VC_Rt_Panel", tint((0.470, 0.462, 0.445), 0.92 + fine * 0.12)
          * np.where(seam2, 0.74, 1.0)[..., None], np.where(seam2, -0.5, 0.0) + fine * 0.10,
          0.52 + fine * 0.12)
    write("VC_Rt_Glass", tint((0.092, 0.112, 0.130), sheen), fine * 0.02, 0.09 + fine * 0.04)
    write("VC_Rt_Metal", tint((0.290, 0.294, 0.298), brush3), fine * 0.05, 0.34 + fine * 0.08, metal=0.85)
    write("VC_Rt_Concrete", tint((0.430, 0.422, 0.405), 0.90 + dirt * 0.18), fine * 0.18, 0.66 + dirt * 0.12)
    mortar2, ident2 = bricks(course_h=0.070, brick_l=0.220, joint=0.012)
    base2 = np.stack([np.full((N, N), 0.310), np.full((N, N), 0.175), np.full((N, N), 0.130)], -1)
    base2 *= (0.74 + ident2[..., None] * 0.54) * (0.92 + fine[..., None] * 0.14)
    alb2 = np.where(mortar2[..., None], np.array([0.46, 0.45, 0.43]) * (0.82 + fine[..., None] * 0.26), base2)
    write("VC_Rt_Brick", alb2, np.where(mortar2, 0.0, 0.85 + ident2 * 0.1) + fine * 0.10,
          np.where(mortar2, 0.86, 0.74 + fine * 0.12))


def mid_ind():
    """ResidentialMidrise (2.0 m tile) and D3 Ironport industrial (4.0 m tile) materials."""
    global X, Y
    X, Y = U * 2.0, V * 2.0
    fine = noise(1.0, 5, 83)
    dirt = blur(noise(1.0, 3, 89), r=3, passes=2)

    # --- midrise: precast panels with a joint grid, brick alternative, glass, metal, concrete
    j = 1.5
    seam = ((X / j - np.floor(X / j)) < 0.016) | ((Y / j - np.floor(Y / j)) < 0.016)
    pid = ((np.floor(X / j) * 19 + np.floor(Y / j) * 37) % 9) / 9.0
    write("VC_Mr_Panel", tint((0.400, 0.392, 0.372), 0.90 + pid * 0.16 + fine * 0.10)
          * np.where(seam, 0.70, 1.0)[..., None],
          np.where(seam, -0.6, 0.0) + fine * 0.12, 0.54 + fine * 0.12)
    mortar, ident = bricks(course_h=0.070, brick_l=0.220, joint=0.012)
    base = np.stack([np.full((N, N), 0.285), np.full((N, N), 0.165), np.full((N, N), 0.125)], -1)
    base *= (0.72 + ident[..., None] * 0.58) * (0.92 + fine[..., None] * 0.16)
    alb = np.where(mortar[..., None], np.array([0.45, 0.44, 0.42]) * (0.82 + fine[..., None] * 0.28), base)
    write("VC_Mr_Brick", alb, np.where(mortar, 0.0, 0.85 + ident * 0.1) + fine * 0.10,
          np.where(mortar, 0.86, 0.74 + fine * 0.12))
    sheen = 0.84 + blur(noise(1.0, 2, 91), r=6, passes=2) * 0.30
    write("VC_Mr_Glass", tint((0.092, 0.112, 0.130), sheen), fine * 0.02, 0.09 + fine * 0.04)
    brush = 0.90 + 0.12 * np.sin(Y * 230.0 + fine * 8.0)
    write("VC_Mr_Metal", tint((0.270, 0.274, 0.278), brush), fine * 0.05, 0.32 + fine * 0.08, metal=0.85)
    form = ((Y / 0.6 - np.floor(Y / 0.6)) < 0.04)
    write("VC_Mr_Concrete", tint((0.420, 0.412, 0.396), 0.90 + dirt * 0.18 + fine * 0.08)
          * np.where(form, 0.88, 1.0)[..., None], np.where(form, -0.4, 0.0) + fine * 0.16,
          0.64 + dirt * 0.12)

    # --- industrial, 4 m tile: trapezoidal profiled cladding and roof sheet, running vertically
    X, Y = U * 4.0, V * 4.0
    fine = noise(1.0, 5, 97)
    dirt = blur(noise(1.0, 3, 101), r=4, passes=2)
    pitch = 0.333
    f = X / pitch - np.floor(X / pitch)
    rib = np.clip((np.abs(f - 0.5) - 0.28) / 0.22, 0.0, 1.0)      # trapezoidal rib profile
    streak = 0.90 + (dirt - 0.5) * 0.30                            # rain streaking down the sheet
    write("VC_In_Clad", tint((0.330, 0.340, 0.345), (0.86 + rib * 0.26) * streak + fine * 0.06),
          rib * 0.9, 0.44 + fine * 0.10 + (1.0 - streak) * 0.10, metal=0.55)
    fr = Y / pitch - np.floor(Y / pitch)
    rib2 = np.clip((np.abs(fr - 0.5) - 0.28) / 0.22, 0.0, 1.0)
    write("VC_In_Roof", tint((0.225, 0.232, 0.236), (0.86 + rib2 * 0.24) * (0.92 + dirt * 0.18)),
          rib2 * 0.9, 0.42 + fine * 0.10, metal=0.70)
    # wired clerestory glazing: a faint mesh, nothing readable
    wire = ((X / 0.25 - np.floor(X / 0.25)) < 0.05) | ((Y / 0.25 - np.floor(Y / 0.25)) < 0.05)
    write("VC_In_Glass", tint((0.140, 0.160, 0.175), 0.90 + fine * 0.12)
          * np.where(wire, 1.25, 1.0)[..., None], wire * 0.3, 0.14 + fine * 0.06)
    brush2 = 0.90 + 0.10 * np.sin(Y * 300.0 + fine * 9.0)
    write("VC_In_Metal", tint((0.260, 0.262, 0.265), brush2 * (0.94 + dirt * 0.12)),
          fine * 0.05, 0.34 + fine * 0.10, metal=0.88)
    write("VC_In_Concrete", tint((0.360, 0.355, 0.342), 0.88 + dirt * 0.22 + fine * 0.08),
          fine * 0.20, 0.68 + dirt * 0.14)
    # tank shell: welded plate courses with visible seams and weathering
    course = 2.0
    cs = ((Y / course - np.floor(Y / course)) < 0.012)
    plate = ((np.floor(Y / course) * 29 + np.floor(X / 3.0) * 13) % 7) / 7.0
    write("VC_In_TankShell", tint((0.430, 0.438, 0.440), 0.88 + plate * 0.14 + (dirt - 0.5) * 0.18)
          * np.where(cs, 0.88, 1.0)[..., None], np.where(cs, 0.5, 0.0) + fine * 0.08,
          0.38 + fine * 0.10 + dirt * 0.10, metal=0.60)


def coast_town():
    """D2 Azure Coast stucco and D8 Redwood Valley market-town materials, 2.0 m tile."""
    global X, Y
    X, Y = U * 2.0, V * 2.0
    fine = noise(1.0, 5, 53)
    coarse = blur(noise(1.0, 3, 59), r=4, passes=2)

    # --- coastal lime stucco: trowel texture, sun-bleached, a little salt staining low down
    h = fine * 0.42 + coarse * 0.22
    write("VC_Cs_Stucco", tint((0.690, 0.665, 0.605), 0.92 + fine * 0.12 + (coarse - 0.5) * 0.12),
          h, 0.60 + fine * 0.16)
    # --- terracotta pantiles
    edge, th, ident = tiles_roof(course=0.31, width=0.24)
    write("VC_Cs_RoofTile", tint((0.370, 0.165, 0.095), 0.74 + ident * 0.52 + fine * 0.14)
          * np.where(edge, 0.58, 1.0)[..., None], th, 0.62 + fine * 0.14)
    write("VC_Cs_Trim", tint((0.720, 0.710, 0.690), 0.95 + fine * 0.08), fine * 0.06, 0.40 + fine * 0.08)
    sheen = 0.84 + blur(noise(1.0, 2, 61), r=6, passes=2) * 0.30
    write("VC_Cs_Glass", tint((0.095, 0.120, 0.140), sheen), fine * 0.02, 0.08 + fine * 0.04)
    # --- weathered decking / pergola timber: boards every 0.14 m, grain along the board
    bd = Y / 0.14 - np.floor(Y / 0.14)
    gap = bd < 0.06
    grain = 0.84 + 0.22 * np.sin(X * 52.0 + blur(noise(1.0, 4, 67), r=1, passes=1) * 7.0)
    write("VC_Cs_Wood", tint((0.300, 0.205, 0.130), grain * (0.92 + fine * 0.14))
          * np.where(gap, 0.55, 1.0)[..., None], np.where(gap, -0.7, 0.0) + grain * 0.12,
          0.52 + fine * 0.12)
    write("VC_Cs_Concrete", tint((0.520, 0.505, 0.475), 0.90 + coarse * 0.18 + fine * 0.08),
          fine * 0.20, 0.68 + coarse * 0.12)

    # --- market-town brick: smaller imperial course, lime mortar, soot in the joints
    mortar, ident = bricks(course_h=0.068, brick_l=0.215, joint=0.012)
    base = np.stack([np.full((N, N), 0.300), np.full((N, N), 0.150), np.full((N, N), 0.110)], -1)
    base *= (0.68 + ident[..., None] * 0.66) * (0.92 + fine[..., None] * 0.16)
    alb = np.where(mortar[..., None], np.array([0.46, 0.45, 0.42]) * (0.78 + fine[..., None] * 0.3), base)
    write("VC_Mt_Brick", alb, np.where(mortar, 0.0, 0.85 + ident * 0.12) + fine * 0.10,
          np.where(mortar, 0.86, 0.74 + fine * 0.12))
    write("VC_Mt_Render", tint((0.480, 0.455, 0.410), 0.90 + coarse * 0.16 + fine * 0.10),
          fine * 0.30, 0.58 + fine * 0.14)
    # --- slate: overlapping courses, slightly varied tone
    gap2, sh, sid = shingles(course=0.175, width=0.26)
    write("VC_Mt_Roof", tint((0.088, 0.092, 0.098), 0.70 + sid * 0.60 + fine * 0.16)
          * np.where(gap2, 0.52, 1.0)[..., None], sh, 0.58 + fine * 0.14)
    write("VC_Mt_Trim", tint((0.120, 0.135, 0.120), 0.94 + fine * 0.10), fine * 0.06, 0.42 + fine * 0.08)
    sheen2 = 0.82 + blur(noise(1.0, 2, 71), r=6, passes=2) * 0.32
    write("VC_Mt_Glass", tint((0.090, 0.108, 0.125), sheen2), fine * 0.02, 0.09 + fine * 0.04)
    write("VC_Mt_Concrete", tint((0.470, 0.458, 0.435), 0.90 + coarse * 0.16 + fine * 0.08),
          fine * 0.18, 0.64 + coarse * 0.12)


def neon():
    """D6 Neon Mile materials, 2.0 m tile (the low-rise meshes are box-projected at cube_size 2.0)."""
    global X, Y
    X, Y = U * 2.0, V * 2.0
    fine = noise(1.0, 5, 41)
    dirt = blur(noise(1.0, 3, 43), r=3, passes=2)

    mortar, ident = bricks(course_h=0.072, brick_l=0.225, joint=0.013)
    base = np.stack([np.full((N, N), 0.255), np.full((N, N), 0.140), np.full((N, N), 0.110)], -1)
    base *= (0.70 + ident[..., None] * 0.60) * (0.90 + fine[..., None] * 0.18)
    base *= (0.80 + dirt[..., None] * 0.35)                      # soot and weathering, back-street brick
    alb = np.where(mortar[..., None], np.array([0.40, 0.39, 0.37]) * (0.8 + fine[..., None] * 0.3), base)
    write("VC_Nm_Brick", alb, np.where(mortar, 0.0, 0.85 + ident * 0.1) + fine * 0.10,
          np.where(mortar, 0.88, 0.74 + fine * 0.12))

    j = 1.2
    seam = ((X / j - np.floor(X / j)) < 0.018) | ((Y / j - np.floor(Y / j)) < 0.018)
    pid = ((np.floor(X / j) * 23 + np.floor(Y / j) * 41) % 11) / 11.0
    write("VC_Nm_Panel", tint((0.330, 0.325, 0.310), 0.88 + pid * 0.18 + fine * 0.10)
          * np.where(seam, 0.68, 1.0)[..., None],
          np.where(seam, -0.7, 0.0) + fine * 0.10, 0.54 + fine * 0.12)

    sheen = 0.82 + blur(noise(1.0, 2, 47), r=6, passes=2) * 0.34
    write("VC_Nm_Glass", tint((0.090, 0.110, 0.128), sheen), fine * 0.02, 0.08 + fine * 0.05)

    brush = 0.90 + 0.12 * np.sin(Y * 240.0 + fine * 9.0)
    write("VC_Nm_Metal", tint((0.260, 0.262, 0.265), brush), fine * 0.05, 0.32 + fine * 0.08, metal=0.85)

    form = ((Y / 0.6 - np.floor(Y / 0.6)) < 0.04)
    write("VC_Nm_Concrete", tint((0.330, 0.325, 0.315), 0.86 + dirt * 0.22 + fine * 0.08)
          * np.where(form, 0.88, 1.0)[..., None], np.where(form, -0.4, 0.0) + fine * 0.16, 0.66 + dirt * 0.12)

    slat = Y / 0.10 - np.floor(Y / 0.10)                          # roll-up shutter: corrugated slats
    h = np.sin(np.pi * slat) * 0.9
    groove = slat < 0.07
    write("VC_Nm_Shutter", tint((0.215, 0.218, 0.222), 0.86 + h * 0.22 + fine * 0.08)
          * np.where(groove, 0.7, 1.0)[..., None], h - groove * 0.8, 0.42 + fine * 0.10, metal=0.7)

    # BLANK emissive sign panel: an even face with a slight edge falloff and a faint tube grain.
    # Deliberately no lettering, no logo, no symbol - Unity applies Veyra's own artwork and colour.
    edge = np.clip(np.minimum(np.minimum(U, 1 - U), np.minimum(V, 1 - V)) * 14.0, 0.0, 1.0)
    grain = 0.94 + 0.06 * np.sin(V * 90.0)
    face = (0.55 + 0.45 * edge) * grain
    save("VC_Neon_Emissive_baseColor", srgb(tint((0.620, 0.300, 0.520), face)))
    mr = np.zeros((N, N, 3)); mr[:, :, 1] = 0.20 + fine * 0.06
    save("VC_Neon_Emissive_metallicRoughness", mr)
    save("VC_Neon_Emissive_normal", normal_from_height(fine * 0.03))


def towers():
    """D1 tower materials. One tile covers 4.0 m - one storey - because the tower meshes are box-projected
    at cube_size 4.0. The curtain wall carries its floor lines and mullion grid in the texture, which is
    why the shaft needs no per-floor geometry (and why a 240 m tower costs the same triangles as a 28 m one)."""
    global X, Y
    X, Y = U * 4.0, V * 4.0
    fine = noise(1.0, 5, 23)
    dirt = blur(noise(1.0, 3, 29), r=3, passes=2)

    # curtain wall: a spandrel band at every storey, vertical mullions, glass that varies pane to pane
    fy = Y / 4.0
    spandrel = (fy < 0.30)
    col = np.floor(X / 1.333)
    fx = X / 1.333 - col
    mull = (fx < 0.055) | (fx > 0.945)
    pane = ((col * 37 + np.floor(Y / 4.0) * 53) % 23) / 23.0
    glass = np.stack([np.full((N, N), 0.075), np.full((N, N), 0.098), np.full((N, N), 0.125)], -1)
    glass *= (0.70 + pane[..., None] * 0.85)                 # pane-to-pane reflection spread
    span_c = np.array([0.135, 0.145, 0.155]) * (0.9 + fine * 0.2)[..., None]
    alb = np.where(spandrel[..., None], span_c, glass)
    alb = np.where(mull[..., None], np.array([0.30, 0.305, 0.31]) * 0.9, alb)
    h = np.where(spandrel, 0.55, 0.0) + np.where(mull, 0.85, 0.0)
    rough = np.where(spandrel, 0.34, 0.07 + pane * 0.05)
    metal = np.where(spandrel | mull, 0.75, 0.0)
    save("VC_Twr_Glass_baseColor", srgb(alb))
    mr = np.zeros((N, N, 3)); mr[:, :, 1] = rough; mr[:, :, 2] = metal
    save("VC_Twr_Glass_metallicRoughness", mr)
    save("VC_Twr_Glass_normal", normal_from_height(h))

    # spandrel / metal panel: a panel seam every 1.33 m
    seam = ((X / 1.333 - np.floor(X / 1.333)) < 0.03) | ((Y / 1.333 - np.floor(Y / 1.333)) < 0.03)
    write("VC_Twr_Spandrel", tint((0.135, 0.145, 0.155), 0.92 + fine * 0.16) * np.where(seam, 0.7, 1.0)[..., None],
          np.where(seam, -0.6, 0.0) + fine * 0.08, 0.32 + fine * 0.10, metal=0.75)

    # precast stone: joints every 1.33 m, slight panel-to-panel colour spread
    jx = (X / 1.333 - np.floor(X / 1.333)); jy = (Y / 1.333 - np.floor(Y / 1.333))
    joint = (jx < 0.022) | (jy < 0.022)
    pid = ((np.floor(X / 1.333) * 17 + np.floor(Y / 1.333) * 31) % 13) / 13.0
    alb = tint((0.400, 0.390, 0.365), 0.86 + pid * 0.22 + fine * 0.10 + (dirt - 0.5) * 0.10)
    write("VC_Twr_Stone", alb * np.where(joint, 0.72, 1.0)[..., None],
          np.where(joint, -0.7, 0.0) + fine * 0.10, 0.52 + fine * 0.12)

    # board-formed concrete
    form = ((Y / 0.6 - np.floor(Y / 0.6)) < 0.04)
    write("VC_Twr_Concrete", tint((0.330, 0.328, 0.318), 0.88 + dirt * 0.20 + fine * 0.08)
          * np.where(form, 0.86, 1.0)[..., None], np.where(form, -0.4, 0.0) + fine * 0.18, 0.66 + dirt * 0.14)

    # brushed metal
    brush = 0.9 + 0.12 * np.sin(Y * 180.0 + fine * 8.0)
    write("VC_Twr_Metal", tint((0.300, 0.305, 0.310), brush), fine * 0.05, 0.28 + fine * 0.08, metal=0.9)


main()

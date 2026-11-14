"""VEYRA CITY - shared textures for the facade / rooftop kit (original, generated here).

    blender --factory-startup -b -noaudio -P make_textures.py

ONE texture set for all 28 props - 7 materials, 21 files - because every prop in this kit instances and
BUG-170 is open. Nothing here is per-prop.

Packing follows CLAUDE.md's URP Lit rule, NOT the glTF packing the building kit uses:
    <mat>_BC.png   sRGB albedo (RGB), alpha unused (opaque)
    <mat>_N.png    OpenGL tangent-space normal, +Y up, Non-Color
    <mat>_MS.png   R = metallic, G = 0, B = 0, **A = smoothness (1 - roughness)**, Non-Color
Unity: Smoothness Source = Metallic Alpha, _N imported as Normal Map, _MS sRGB off.
(The building kit writes `_metallicRoughness` instead because KitbashMaterials reads glTF packing. These
props go through the VeyraMaterialLibrary, so they follow CLAUDE.md.)

512 x 512, power of two, every pattern seamless; one tile covers 1.0 m, matching the 1 m box projection
the meshes are unwrapped with (same convention as the Infra set).
"""
import os

import numpy as np

N = 512
TILE_M = 1.0
HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(os.path.dirname(HERE), "textures")

u = (np.arange(N) + 0.5) / N
U, V = np.meshgrid(u, u)
X, Y = U * TILE_M, V * TILE_M


def blur(a, r=2, passes=2):
    for _ in range(passes):
        acc = np.zeros_like(a)
        for dy in range(-r, r + 1):
            for dx in range(-r, r + 1):
                acc += np.roll(np.roll(a, dy, 0), dx, 1)
        a = acc / ((2 * r + 1) ** 2)
    return a


def noise(octaves=4, seed=0):
    g = np.random.default_rng(31337 + seed)
    out = np.zeros((N, N))
    amp, tot = 1.0, 0.0
    for o in range(octaves):
        step = max(1, N // (2 ** (o + 2)))
        small = g.random((N // step + 1, N // step + 1))
        big = np.kron(small, np.ones((step, step)))[:N, :N]
        out += blur(big, r=max(1, step // 3), passes=1) * amp
        tot += amp
        amp *= 0.5
    out /= tot
    return (out - out.min()) / max(1e-6, float(np.ptp(out)))


def srgb(x):
    x = np.clip(x, 0.0, 1.0)
    return np.where(x <= 0.0031308, x * 12.92, 1.055 * np.power(x, 1 / 2.4) - 0.055)


def save(name, rgb, alpha=None):
    import bpy
    img = bpy.data.images.new(name, N, N, alpha=alpha is not None, float_buffer=False)
    img.colorspace_settings.name = "Non-Color"
    px = np.zeros((N, N, 4), dtype=np.float32)
    px[:, :, :3] = np.clip(rgb, 0.0, 1.0)
    px[:, :, 3] = 1.0 if alpha is None else np.clip(alpha, 0.0, 1.0)
    img.pixels.foreach_set(px.reshape(-1))
    img.file_format = "PNG"
    img.filepath_raw = os.path.join(OUT, name + ".png")
    img.save()
    bpy.data.images.remove(img)


def normal_from_height(h, strength=2.0):
    dx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * 0.5 * strength * N / 64.0
    dy = (np.roll(h, -1, 0) - np.roll(h, 1, 0)) * 0.5 * strength * N / 64.0
    nz = np.ones_like(h)
    ln = np.sqrt(dx * dx + dy * dy + nz * nz)
    return np.stack([(-dx / ln) * 0.5 + 0.5, (-dy / ln) * 0.5 + 0.5, (nz / ln) * 0.5 + 0.5], axis=-1)


def write(name, albedo_lin, height, rough, metal=0.0):
    """_BC + _N + _MS, with smoothness (1 - roughness) in the metallic map's ALPHA."""
    save(name + "_BC", srgb(albedo_lin))
    save(name + "_N", normal_from_height(height))
    mr = np.zeros((N, N, 3))
    mr[:, :, 0] = np.clip(np.broadcast_to(np.asarray(metal, dtype=float), (N, N)), 0.0, 1.0)
    save(name + "_MS", mr, alpha=1.0 - np.clip(rough, 0.0, 1.0))


def tint(base, var):
    return np.stack([base[0] * var, base[1] * var, base[2] * var], axis=-1)


def main():
    os.makedirs(OUT, exist_ok=True)
    fine = noise(5, 1)
    dirt = blur(noise(3, 2), r=3, passes=2)

    # --- painted / anodised metal: faint brushing plus grime in the recesses
    brush = 0.92 + 0.09 * np.sin(Y * 430.0 + fine * 7.0)
    write("VC_Fac_Metal", tint((0.265, 0.270, 0.275), brush * (0.94 + dirt * 0.12)),
          fine * 0.05, 0.33 + fine * 0.10 + dirt * 0.06, metal=0.90)

    # --- shopfront / window glass: subtle sheet variation, no pattern that could read as artwork
    sheen = 0.84 + blur(noise(2, 3), r=6, passes=2) * 0.30
    write("VC_Fac_Glass", tint((0.085, 0.105, 0.122), sheen), fine * 0.02, 0.07 + fine * 0.04)

    # --- painted panel: a 0.5 m board joint grid, slight orange-peel in the paint
    j = 0.5
    seam = ((X / j - np.floor(X / j)) < 0.02) | ((Y / j - np.floor(Y / j)) < 0.02)
    write("VC_Fac_Panel", tint((0.330, 0.300, 0.265), 0.92 + fine * 0.14 + (dirt - 0.5) * 0.10)
          * np.where(seam, 0.78, 1.0)[..., None],
          np.where(seam, -0.55, 0.0) + fine * 0.22, 0.50 + fine * 0.12)

    # --- awning canvas: a woven weft and a faint seam every 0.25 m
    weave = 0.88 + 0.10 * (np.sin(X * 820.0) * np.sin(Y * 820.0))
    stripe = ((Y / 0.25 - np.floor(Y / 0.25)) < 0.03)
    write("VC_Fac_Fabric", tint((0.250, 0.135, 0.115), weave * (0.93 + dirt * 0.14))
          * np.where(stripe, 0.88, 1.0)[..., None],
          weave * 0.25 + np.where(stripe, -0.3, 0.0), 0.76 + fine * 0.10)

    # --- BLANK sign face: flat, even, deliberately featureless. No lettering, symbol or logo.
    #     Unity applies Veyra's own artwork over this.
    write("VC_Fac_Blank", tint((0.620, 0.615, 0.600), 0.97 + fine * 0.05), fine * 0.02,
          0.40 + fine * 0.05)

    # --- precast concrete for sills, copings, cornices and plinths
    pits = (noise(5, 7) > 0.88) * 0.5
    write("VC_Fac_Concrete", tint((0.400, 0.394, 0.380), 0.88 + dirt * 0.22 + fine * 0.08)
          * (1.0 - pits * 0.25)[..., None], fine * 0.26 - pits * 0.5, 0.66 + dirt * 0.14)

    # --- BLANK emissive: crown light coves. Even face, slight tube grain, no artwork.
    grain = 0.95 + 0.05 * np.sin(V * 120.0)
    write("VC_Fac_Emissive", tint((0.600, 0.580, 0.520), grain), fine * 0.02, 0.22 + fine * 0.04)

    print("VC_FAC_TEXTURES_OK:" + OUT)


main()

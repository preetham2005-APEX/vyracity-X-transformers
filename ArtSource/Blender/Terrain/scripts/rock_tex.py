"""Build T_Veyra_Rock_{BaseColor,Normal,MetallicSmoothness}.png - ONE seamless tiling 1024 set for
the entire rock kit.

A tiling set, not an atlas: rock UVs are box-projected and tile freely, which gives far better texel
density than an atlas rect could, and still only costs one material. 1024 px over a 2.5 m repeat is
410 px/m, which holds up at the kerbside distances a boulder is seen from.
"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
VEG1_SCRIPTS = os.path.join(os.path.dirname(ROOT), "Vegetation", "scripts")
sys.path.insert(0, VEG1_SCRIPTS)

import numpy as np  # noqa: E402

from atlas_paint import srgb, write_png  # noqa: E402

OUT = os.path.join(ROOT, "textures")
SIZE = 1024
RNG = np.random.default_rng(20261114)


def vnoise(size, freq, rng):
    """Periodic value noise: a freq x freq random lattice, smoothstep-interpolated and wrapped, so
    the result tiles exactly."""
    g = rng.random((freq, freq)).astype(np.float32)
    idx = np.arange(size, dtype=np.float32) * freq / size
    i0 = np.floor(idx).astype(np.int32) % freq
    i1 = (i0 + 1) % freq
    f = idx - np.floor(idx)
    f = (f * f * (3 - 2 * f)).astype(np.float32)
    a = g[np.ix_(i0, i0)]
    b = g[np.ix_(i1, i0)]
    c = g[np.ix_(i0, i1)]
    d = g[np.ix_(i1, i1)]
    fx = f[:, None]
    fy = f[None, :]
    return (a * (1 - fx) * (1 - fy) + b * fx * (1 - fy) + c * (1 - fx) * fy + d * fx * fy)


def fbm(size, rng, octaves=6, base=4, gain=0.5):
    out = np.zeros((size, size), np.float32)
    amp, f, norm = 1.0, base, 0.0
    for _ in range(octaves):
        out += amp * vnoise(size, f, rng)
        norm += amp
        amp *= gain
        f *= 2
    return out / norm


def ridged(size, rng, octaves=4, base=3):
    n = fbm(size, rng, octaves, base)
    return 1.0 - np.abs(n * 2.0 - 1.0)


def main():
    os.makedirs(OUT, exist_ok=True)
    S = SIZE
    grain = fbm(S, RNG, 6, 16, gain=0.62)      # mineral grain, deliberately high-frequency
    patch = fbm(S, RNG, 4, 3)                  # large tonal patches
    micro = vnoise(S, 256, RNG)                # per-crystal speckle
    micro2 = vnoise(S, 512, RNG)

    # two fracture sets at different scales: a coarse one that breaks the surface into plates and a
    # fine one that crazes it. Sharpened hard - the first pass left them as soft dark worms.
    crev_a = ridged(S, RNG, 5, 3)
    crev_a = np.clip((crev_a - 0.72) / 0.28, 0, 1) ** 2.2
    crev_b = ridged(S, RNG, 4, 11)
    crev_b = np.clip((crev_b - 0.80) / 0.20, 0, 1) ** 2.0
    crev = np.clip(crev_a + 0.55 * crev_b, 0, 1)

    spall = np.clip((fbm(S, RNG, 3, 6) - 0.54) / 0.18, 0, 1)    # freshly broken pale faces
    lich = np.clip((fbm(S, RNG, 4, 9) - 0.64) / 0.26, 0, 1)     # lichen film, sparse
    quartz = np.clip((micro2 - 0.76) / 0.24, 0, 1)              # bright crystal flecks
    mica = np.clip((0.26 - micro) / 0.26, 0, 1)                 # dark flecks

    dark = np.array((0.030, 0.028, 0.026), np.float32)
    mid = np.array((0.118, 0.112, 0.102), np.float32)
    pale = np.array((0.260, 0.250, 0.230), np.float32)
    warm = np.array((0.178, 0.146, 0.106), np.float32)
    lichen = np.array((0.082, 0.102, 0.050), np.float32)

    t = np.clip(patch * 0.80 + grain * 0.70, 0, 1)[..., None]
    col = mid * (1 - t) + pale * t
    col = col * (1 - 0.40 * patch[..., None]) + warm * (0.40 * patch[..., None])
    col = col * (1 - 0.60 * spall[..., None]) + pale * (0.60 * spall[..., None])
    col = col * (1 - 0.55 * quartz[..., None]) + pale * 1.25 * (0.55 * quartz[..., None])
    col = col * (1 - 0.60 * mica[..., None]) + dark * (0.60 * mica[..., None])
    col = col * (1 - 0.92 * crev[..., None]) + dark * (0.92 * crev[..., None])
    col = col * (1 - 0.50 * lich[..., None]) + lichen * (0.50 * lich[..., None])
    col = col * (0.74 + 0.52 * grain)[..., None]               # grain as straight tonal contrast
    col = np.clip(col, 0, 1)

    a = np.ones((S, S, 1), np.float32)
    write_png(os.path.join(OUT, "T_Veyra_Rock_BaseColor.png"),
              ((np.concatenate([srgb(col), a], -1)) * 255 + 0.5).astype(np.uint8))

    # --- height -> normal (OpenGL, +Y up). The fractures cut, the grain and flecks bump.
    height = (0.50 * patch + 0.42 * grain + 0.26 * spall
              + 0.10 * quartz - 0.08 * mica - 1.05 * crev)
    height = (height - height.min()) / max(1e-6, (height.max() - height.min()))
    gx = np.roll(height, -1, 1) - np.roll(height, 1, 1)
    gy = np.roll(height, -1, 0) - np.roll(height, 1, 0)
    strength = 5.5
    nx, ny, nz = -gx * strength, gy * strength, np.ones_like(height)
    ln = np.sqrt(nx * nx + ny * ny + nz * nz)
    nrm = np.stack([nx / ln, ny / ln, nz / ln], -1) * 0.5 + 0.5
    write_png(os.path.join(OUT, "T_Veyra_Rock_Normal.png"),
              (np.clip(np.concatenate([nrm, a], -1), 0, 1) * 255 + 0.5).astype(np.uint8))

    # --- metallic 0, smoothness = 1 - roughness in alpha (URP Lit, Metallic Alpha)
    rough = 0.92 - 0.20 * spall - 0.10 * quartz + 0.05 * crev - 0.12 * lich
    rough = np.clip(rough, 0.55, 0.99)
    sm = 1.0 - rough
    z = np.zeros_like(sm)
    write_png(os.path.join(OUT, "T_Veyra_Rock_MetallicSmoothness.png"),
              (np.stack([z, z, z, sm], -1) * 255 + 0.5).astype(np.uint8))
    print("rock texture set done: 1024, smoothness %.2f-%.2f" % (sm.min(), sm.max()))


if __name__ == "__main__":
    main()

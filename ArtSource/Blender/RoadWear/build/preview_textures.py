"""Contact sheets: every decal composited over flat asphalt and lit by its own normal map."""
import os
import sys

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)

import vc_tex                                   # noqa: E402
import vc_tex2                                  # noqa: E402
from vc_png import to_srgb, u8, write_png       # noqa: E402

ROOT = os.path.abspath(os.path.join(HERE, ".."))
REVIEW = os.path.join(ROOT, "review")
TILE = 512


def srgb_to_lin(x):
    x = np.clip(x, 0, 1)
    return np.where(x <= 0.04045, x / 12.92, ((x + 0.055) / 1.055) ** 2.4)


def downsample(a, out):
    k = a.shape[0] // out
    s = a[:out * k, :out * k]
    return s.reshape(out, k, out, k, *a.shape[2:]).mean(axis=(1, 3))


def atlas_panel(atlas, lit=True):
    n = atlas["size"] // 2
    c = atlas["cell_m"]
    mu, mv = (c, c) if np.isscalar(c) else c
    texel_u, texel_v = mu / n, mv / n
    panel = np.zeros((TILE * 2, TILE * 2, 3))
    rngl = np.random.default_rng(7)
    for i, (_, fn) in enumerate(atlas["cells"]):
        d = fn(n)
        base = 0.052 + 0.012 * vc_tex.fbm(n, 64, 64, 4242, octaves=3)
        road = np.repeat(base[:, :, None], 3, axis=2)
        a = d["alpha"][:, :, None]
        col = road * (1 - a) + d["albedo"] * a
        if lit:
            dzdr, dzdc = np.gradient(d["height"], texel_v, texel_u)
            nx, ny, nz = -dzdc, dzdr, np.ones_like(dzdc)
            inv = 1.0 / np.sqrt(nx * nx + ny * ny + nz * nz)
            L = np.array([-0.45, 0.42, 0.79])
            L = L / np.linalg.norm(L)
            ndl = np.clip((nx * L[0] + ny * L[1] + nz * L[2]) * inv, 0, 1)
            shade = 0.32 + 0.68 * ndl
            col = col * (shade * d["ao"])[:, :, None]
        cx, cy = (i % 2) * TILE, (i // 2) * TILE
        panel[cy:cy + TILE, cx:cx + TILE] = downsample(col, TILE)
    return panel


def main():
    os.makedirs(REVIEW, exist_ok=True)
    atlases = vc_tex.ATLASES + vc_tex2.ATLASES2
    cols, half = 3, TILE // 2
    rows = (len(atlases) + cols - 1) // cols
    sheet = np.zeros((rows * half * 2, cols * half * 2, 3))
    for i, atlas in enumerate(atlases):
        p = atlas_panel(atlas)
        small = downsample(p, half * 2)           # exact factor 2
        r, c = divmod(i, cols)
        sheet[r * half * 2:(r + 1) * half * 2, c * half * 2:(c + 1) * half * 2] = small
        write_png(os.path.join(REVIEW, "%s_preview.png" % atlas["name"]),
                  u8(to_srgb(downsample(p, TILE))))
        print("[PREVIEW] %s" % atlas["name"])
    write_png(os.path.join(REVIEW, "_contact_sheet.png"), u8(to_srgb(sheet)))
    for spec in vc_tex2.TILING:                       # 2 x 2 repeat, so a seam would show
        n = spec["size"]
        texel = spec["tile_m"] / n
        d = spec["fn"](n)
        dzdr, dzdc = np.gradient(d["height"], texel, texel)
        nx, ny, nz = -dzdc, dzdr, np.ones_like(dzdc)
        inv = 1.0 / np.sqrt(nx * nx + ny * ny + nz * nz)
        L = np.array([-0.45, 0.42, 0.79])
        L = L / np.linalg.norm(L)
        ndl = np.clip((nx * L[0] + ny * L[1] + nz * L[2]) * inv, 0, 1)
        col = d["albedo"] * ((0.32 + 0.68 * ndl) * d["ao"])[:, :, None]
        half_t = downsample(col, TILE)
        tiled = np.tile(half_t, (2, 2, 1))
        write_png(os.path.join(REVIEW, "%s_preview.png" % spec["name"]),
                  u8(to_srgb(downsample(tiled, TILE))))
        print("[PREVIEW] %s (2x2 tiled)" % spec["name"])
    print("[PREVIEW] contact sheet")


main()

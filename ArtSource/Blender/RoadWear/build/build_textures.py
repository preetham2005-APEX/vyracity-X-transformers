"""Bake the Veyra road-wear decal atlases to PNG.

    "D:\\New folder\\blender.exe" -b --factory-startup -P build_textures.py
(any Python 3 with numpy works too - nothing here touches bpy)

Outputs, per atlas, into ArtSource/Blender/RoadWear/textures/:
    <name>_BC.png  RGBA  sRGB albedo + decal alpha (fades to 0 at every cell border)
    <name>_N.png   RGB   tangent-space normal, OpenGL (+Y up), linear/raw
    <name>_MS.png  RGBA  R = metallic, G = AO, B = height (0.5 = road plane), A = smoothness
URP Lit packing: Smoothness = 1 - roughness in the Metallic map's alpha
(Smoothness Source = Metallic Alpha). _N must be imported as a Normal Map, _MS as raw data.
"""
import json
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
TEX = os.path.join(ROOT, "textures")

HEIGHT_RANGE = 0.02      # +/- 20 mm maps to the 0..1 range of the MS blue channel


def cell_size_m(atlas):
    """Atlas cell world size as (across u, along v) in metres."""
    c = atlas["cell_m"]
    return (float(c), float(c)) if np.isscalar(c) else (float(c[0]), float(c[1]))


def cell_normal(height, texel_u, texel_v):
    """Tangent-space normal from a height field in metres. OpenGL convention (+Y up)."""
    dzdr, dzdc = np.gradient(height, texel_v, texel_u)
    nx = -dzdc
    ny = dzdr
    nz = np.ones_like(height)
    inv = 1.0 / np.sqrt(nx * nx + ny * ny + nz * nz)
    return np.stack([nx * inv, ny * inv, nz * inv], axis=-1)


def build(atlas):
    size, n = atlas["size"], atlas["size"] // 2
    mu, mv = cell_size_m(atlas)
    texel_u, texel_v = mu / n, mv / n
    bc = np.zeros((size, size, 4))
    nm = np.zeros((size, size, 3))
    nm[:, :, 2] = 1.0
    ms = np.zeros((size, size, 4))
    ms[:, :, 1] = 1.0                      # AO
    ms[:, :, 2] = 0.5                      # height = road plane
    ms[:, :, 3] = vc_tex.ASPHALT_SMOOTH
    cells = []
    for i, (cname, fn) in enumerate(atlas["cells"]):
        cx, cy = (i % 2) * n, (i // 2) * n
        d = fn(n)
        sl = (slice(cy, cy + n), slice(cx, cx + n))
        bc[sl][:, :, :3] = to_srgb(d["albedo"])
        bc[sl][:, :, 3] = d["alpha"]
        nm[sl] = cell_normal(d["height"], texel_u, texel_v)
        ms[sl][:, :, 0] = 0.0
        ms[sl][:, :, 1] = d["ao"]
        ms[sl][:, :, 2] = np.clip(d["height"] / (2.0 * HEIGHT_RANGE) + 0.5, 0.0, 1.0)
        ms[sl][:, :, 3] = d["smooth"]
        cells.append(dict(cell=cname, index=i,
                          uv_rect=[round((i % 2) * 0.5, 3), round(1.0 - (i // 2 + 1) * 0.5, 3),
                                   0.5, 0.5],
                          px_rect=[cx, cy, n, n],
                          alpha_max=round(float(d["alpha"].max()), 3),
                          height_mm=[round(float(d["height"].min()) * 1000, 2),
                                     round(float(d["height"].max()) * 1000, 2)],
                          smoothness=[round(float(d["smooth"].min()), 3),
                                      round(float(d["smooth"].max()), 3)]))
    files = {}
    files["BC"] = write_png(os.path.join(TEX, "%s_BC.png" % atlas["name"]), u8(bc))
    files["N"] = write_png(os.path.join(TEX, "%s_N.png" % atlas["name"]), u8(nm * 0.5 + 0.5))
    files["MS"] = write_png(os.path.join(TEX, "%s_MS.png" % atlas["name"]), u8(ms))
    return dict(name=atlas["name"], size=size, cell_px=n, cell_m=[mu, mv],
                cells=cells, bytes=files)


def build_tiling(spec):
    """Seamless tiling MATERIAL texture: no cell grid, no border fade, opaque RGB albedo."""
    size = spec["size"]
    texel = spec["tile_m"] / size
    d = spec["fn"](size)
    bc = to_srgb(d["albedo"])
    nm = cell_normal(d["height"], texel, texel)
    ms = np.stack([np.zeros((size, size)), d["ao"],
                   np.clip(d["height"] / (2.0 * HEIGHT_RANGE) + 0.5, 0.0, 1.0),
                   d["smooth"]], axis=-1)
    files = {"BC": write_png(os.path.join(TEX, "%s_BC.png" % spec["name"]), u8(bc)),
             "N": write_png(os.path.join(TEX, "%s_N.png" % spec["name"]), u8(nm * 0.5 + 0.5)),
             "MS": write_png(os.path.join(TEX, "%s_MS.png" % spec["name"]), u8(ms))}
    return dict(name=spec["name"], size=size, tile_m=spec["tile_m"], bytes=files,
                height_mm=[round(float(d["height"].min()) * 1000, 2),
                           round(float(d["height"].max()) * 1000, 2)],
                smoothness=[round(float(d["smooth"].min()), 3),
                            round(float(d["smooth"].max()), 3)])


def main():
    os.makedirs(TEX, exist_ok=True)
    out, tiles = [], []
    for atlas in vc_tex.ATLASES + vc_tex2.ATLASES2:
        r = build(atlas)
        out.append(r)
        print("[TEX] %-16s %dx%d  cell %dpx = %.1f m along  %.1f MB"
              % (r["name"], r["size"], r["size"], r["cell_px"], r["cell_m"][1],
                 sum(r["bytes"].values()) / 1e6))
    for spec in vc_tex2.TILING:
        r = build_tiling(spec)
        tiles.append(r)
        print("[TEX] %-16s %dx%d  tiling %.2f m  %.1f MB"
              % (r["name"], r["size"], r["size"], r["tile_m"], sum(r["bytes"].values()) / 1e6))
    with open(os.path.join(HERE, "texture_stats.json"), "w") as fh:
        json.dump(dict(atlases=out, tiling=tiles), fh, indent=1)
    print("[TEX] %d atlases + %d tiling, %d files"
          % (len(out), len(tiles), (len(out) + len(tiles)) * 3))


main()

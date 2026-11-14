"""VEYRA CITY vegetation atlas painter.

Generates ONE foliage atlas (2048 sq, alpha-clip) and ONE bark atlas (1024 sq) for the whole
vegetation set, so every tree, hedge and shrub in the city shares 2 materials.
Pure numpy + a minimal PNG writer: no colour management in the loop, the bytes are the bytes.
Run with Blender's bundled python (numpy) or any python3 with numpy.
"""
import math
import os
import struct
import zlib

import numpy as np

OUT = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "textures")
RNG = np.random.default_rng(20261112)

# ---------------------------------------------------------------- png writer
def write_png(path, rgba8):
    h, w, c = rgba8.shape
    assert c == 4
    raw = b"".join(b"\x00" + rgba8[y].tobytes() for y in range(h))

    def chunk(tag, data):
        return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)

    png = b"\x89PNG\r\n\x1a\n"
    png += chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0))
    png += chunk(b"IDAT", zlib.compress(raw, 9))
    png += chunk(b"IEND", b"")
    with open(path, "wb") as f:
        f.write(png)


def srgb(lin):
    lin = np.clip(lin, 0.0, 1.0)
    return np.where(lin <= 0.0031308, lin * 12.92, 1.055 * np.power(lin, 1 / 2.4) - 0.055)


# ---------------------------------------------------------------- canvas
class Canvas:
    """RGB (linear) + A + H (height proxy) + R (roughness)."""

    def __init__(self, w, h):
        self.w, self.h = w, h
        self.rgb = np.zeros((h, w, 3), np.float32)
        self.a = np.zeros((h, w), np.float32)
        self.ht = np.zeros((h, w), np.float32)
        self.rg = np.zeros((h, w), np.float32)

    def blend(self, x0, y0, mask, rgb, height, rough):
        """Paint a sub-rect; mask is coverage 0..1 (painter's algorithm, newest on top)."""
        mh, mw = mask.shape
        x1, y1 = x0 + mw, y0 + mh
        sx0, sy0 = max(0, x0), max(0, y0)
        sx1, sy1 = min(self.w, x1), min(self.h, y1)
        if sx1 <= sx0 or sy1 <= sy0:
            return
        m = mask[sy0 - y0:sy1 - y0, sx0 - x0:sx1 - x0]
        if m.max() <= 0:
            return
        m3 = m[..., None]
        sl = (slice(sy0, sy1), slice(sx0, sx1))
        src = rgb[sy0 - y0:sy1 - y0, sx0 - x0:sx1 - x0] if rgb.ndim == 3 else np.broadcast_to(rgb, m.shape + (3,))
        self.rgb[sl] = self.rgb[sl] * (1 - m3) + src * m3
        self.a[sl] = np.maximum(self.a[sl], m)
        hh = height[sy0 - y0:sy1 - y0, sx0 - x0:sx1 - x0] if np.ndim(height) else np.full(m.shape, float(height), np.float32)
        self.ht[sl] = self.ht[sl] * (1 - m) + hh * m
        self.rg[sl] = self.rg[sl] * (1 - m) + float(rough) * m


def _rot_grid(w, h, cx, cy, ang):
    ys, xs = np.mgrid[0:h, 0:w].astype(np.float32)
    dx, dy = xs - cx, ys - cy
    c, s = math.cos(-ang), math.sin(-ang)
    return dx * c - dy * s, dx * s + dy * c


def leaf(cv, cx, cy, length, width, ang, col, rough=0.62, curl=0.0, vein=0.55, soft=1.2):
    """One leaf: pointed ellipse with a midrib and a dome height profile."""
    r = int(length * 0.75) + 6
    x0, y0 = int(cx - r), int(cy - r)
    w = h = 2 * r
    u, v = _rot_grid(w, h, cx - x0, cy - y0, ang)
    t = u / (length * 0.5)
    v = v - curl * (length * 0.5) * (1.0 - t * t)
    prof = np.clip(1.0 - t * t, 0, 1) ** 0.55 * np.clip(1.0 - 0.30 * t, 0, 2)
    hw = np.maximum(width * 0.5 * prof, 1e-4)
    d = np.abs(v) / hw
    mask = np.clip((1.0 - d) * hw / soft, 0, 1) * (np.abs(t) < 1.0)
    rib = np.clip(1.0 - d / 0.22, 0, 1)
    shade = 1.0 - vein * 0.35 * rib - 0.18 * np.clip(d, 0, 1) ** 2
    rgb = np.asarray(col, np.float32)[None, None, :] * shade[..., None]
    ht = np.clip(np.sqrt(np.clip(1 - d * d, 0, 1)), 0, 1) * 0.6 + np.clip(1 - np.abs(t), 0, 1) * 0.4
    cv.blend(x0, y0, mask.astype(np.float32), rgb.astype(np.float32), ht.astype(np.float32), rough)


def needle(cv, x0p, y0p, x1p, y1p, width, col, rough=0.58, taper=True):
    """A straight needle / blade / twig between two points."""
    pad = int(width) + 4
    xa, xb = int(min(x0p, x1p)) - pad, int(max(x0p, x1p)) + pad
    ya, yb = int(min(y0p, y1p)) - pad, int(max(y0p, y1p)) + pad
    w, h = max(xb - xa, 1), max(yb - ya, 1)
    ys, xs = np.mgrid[0:h, 0:w].astype(np.float32)
    px, py = xs + xa, ys + ya
    dx, dy = x1p - x0p, y1p - y0p
    L2 = dx * dx + dy * dy + 1e-6
    tt = np.clip(((px - x0p) * dx + (py - y0p) * dy) / L2, 0, 1)
    cxp, cyp = x0p + tt * dx, y0p + tt * dy
    dist = np.hypot(px - cxp, py - cyp)
    hw = width * 0.5 * ((1.0 - 0.85 * tt) if taper else 1.0)
    hw = np.maximum(hw, 0.35)
    mask = np.clip((hw - dist) / 0.9 + 0.5, 0, 1)
    ht = np.clip(1 - (dist / np.maximum(hw, 1e-3)) ** 2, 0, 1) * 0.8 + 0.2
    rgb = np.asarray(col, np.float32)[None, None, :] * (0.82 + 0.18 * ht)[..., None]
    cv.blend(xa, ya, mask.astype(np.float32), rgb.astype(np.float32), ht.astype(np.float32), rough)


def jitter(col, amt=0.10, rng=RNG):
    c = np.asarray(col, np.float32) * (1.0 + rng.uniform(-amt, amt))
    c = c * (1.0 + rng.uniform(-amt, amt, 3) * 0.6)
    return np.clip(c, 0, 1)

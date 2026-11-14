"""Minimal deterministic PNG writer (8-bit RGB / RGBA / grey), no external dependencies."""
import struct
import zlib

import numpy as np


def _chunk(tag, data):
    return (struct.pack(">I", len(data)) + tag + data
            + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF))


def write_png(path, arr):
    """arr: uint8 array (h, w), (h, w, 3) or (h, w, 4). Written top row first."""
    a = np.ascontiguousarray(arr, dtype=np.uint8)
    if a.ndim == 2:
        a = a[:, :, None]
    h, w, c = a.shape
    ctype = {1: 0, 3: 2, 4: 6}[c]
    raw = np.concatenate([np.zeros((h, 1), np.uint8), a.reshape(h, w * c)], axis=1).tobytes()
    png = b"\x89PNG\r\n\x1a\n"
    png += _chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, ctype, 0, 0, 0))
    png += _chunk(b"IDAT", zlib.compress(raw, 9))
    png += _chunk(b"IEND", b"")
    with open(path, "wb") as fh:
        fh.write(png)
    return len(png)


def to_srgb(lin):
    """Linear float -> sRGB-encoded float (written straight into the 8-bit file)."""
    lin = np.clip(lin, 0.0, 1.0)
    return np.where(lin <= 0.0031308, lin * 12.92, 1.055 * np.power(lin, 1.0 / 2.4) - 0.055)


def u8(x):
    return np.clip(np.rint(np.asarray(x, dtype=np.float64) * 255.0), 0, 255).astype(np.uint8)


def read_png(path):
    """Read a PNG written by write_png (8-bit, non-interlaced, filter type 0)."""
    with open(path, "rb") as fh:
        data = fh.read()
    sig = bytes([137, 80, 78, 71, 13, 10, 26, 10])
    assert data[:8] == sig, "not a PNG: %s" % path
    pos, idat, hdr = 8, b"", None
    while pos < len(data):
        ln = struct.unpack(">I", data[pos:pos + 4])[0]
        tag = data[pos + 4:pos + 8]
        body = data[pos + 8:pos + 8 + ln]
        if tag == b"IHDR":
            hdr = struct.unpack(">IIBBBBB", body)
        elif tag == b"IDAT":
            idat += body
        pos += 12 + ln
    w, h, depth, ctype, _, _, interlace = hdr
    assert depth == 8 and interlace == 0, "unsupported PNG: %s" % path
    c = {0: 1, 2: 3, 6: 4}[ctype]
    raw = np.frombuffer(zlib.decompress(idat), dtype=np.uint8).reshape(h, w * c + 1)
    assert not raw[:, 0].any(), "unexpected PNG row filters in %s" % path
    return raw[:, 1:].reshape(h, w, c)

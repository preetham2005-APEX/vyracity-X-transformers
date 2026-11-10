"""
Headless Blender: tile PNG previews into one contact sheet (row-major, in the given order).

  blender -b --factory-startup --python contact_sheet.py -- <out.png> <cols> <scale_div> <img1> <img2> ...
"""
import bpy
import sys
import numpy as np

a = sys.argv[sys.argv.index("--") + 1:]
out, cols, div, files = a[0], int(a[1]), a[2], a[3:]
# <scale_div> is an integer downscale, or "WxH" to resample every tile to a fixed size (mixed-size inputs)
fixed = tuple(int(v) for v in div.split("x")) if "x" in div else None
tiles = []
for f in files:
    im = bpy.data.images.load(f)
    w, h = im.size
    px = np.empty(w * h * 4, dtype=np.float32)
    im.pixels.foreach_get(px)
    px = px.reshape(h, w, 4)
    if fixed:
        ys = (np.arange(fixed[1]) * h / fixed[1]).astype(int)
        xs = (np.arange(fixed[0]) * w / fixed[0]).astype(int)
        px = px[ys][:, xs]
        px[..., 3] = 1.0
    else:
        px = px[::int(div), ::int(div)]
    tiles.append(px)
    bpy.data.images.remove(im)
th, tw = tiles[0].shape[:2]
rows = (len(tiles) + cols - 1) // cols
sheet = np.ones((rows * th, cols * tw, 4), dtype=np.float32) * 0.2
sheet[..., 3] = 1
for i, t in enumerate(tiles):
    r, c = divmod(i, cols)
    y0 = (rows - 1 - r) * th          # Blender rows start at the bottom
    sheet[y0:y0 + t.shape[0], c * tw:c * tw + t.shape[1]] = t[:th, :tw]
    # thin border so tiles read apart
    sheet[y0:y0 + th, c * tw] = (0, 0, 0, 1)
    sheet[y0, c * tw:c * tw + tw] = (0, 0, 0, 1)
o = bpy.data.images.new("sheet", sheet.shape[1], sheet.shape[0], alpha=True)
o.pixels.foreach_set(sheet.reshape(-1))
o.filepath_raw = out
o.file_format = "PNG"
o.save()
print("SHEET", out, sheet.shape)

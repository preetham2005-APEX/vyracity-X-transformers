"""
Headless Blender: convert PNGs to (optionally downscaled) JPEGs for reports.

  blender -b --factory-startup --python to_jpeg.py -- <quality 1-100> <max_width> <in.png> <out.jpg> [<in2.png> <out2.jpg> ...]
"""
import bpy
import sys
import numpy as np

a = sys.argv[sys.argv.index("--") + 1:]
quality, max_w, pairs = int(a[0]), int(a[1]), a[2:]
scene = bpy.context.scene
scene.render.image_settings.file_format = "JPEG"
scene.render.image_settings.quality = quality
scene.render.image_settings.color_mode = "RGB"
for i in range(0, len(pairs), 2):
    src, dst = pairs[i], pairs[i + 1]
    im = bpy.data.images.load(src)
    w, h = im.size
    if w > max_w:
        px = np.empty(w * h * 4, dtype=np.float32)
        im.pixels.foreach_get(px)
        px = px.reshape(h, w, 4)
        nw, nh = max_w, int(round(h * max_w / w))
        ys = (np.arange(nh) * h / nh).astype(int)
        xs = (np.arange(nw) * w / nw).astype(int)
        # box-filter 2x2 neighbourhood for a smoother downscale
        px = (px[ys][:, xs] + px[np.minimum(ys + 1, h - 1)][:, xs] + px[ys][:, np.minimum(xs + 1, w - 1)]
              + px[np.minimum(ys + 1, h - 1)][:, np.minimum(xs + 1, w - 1)]) * 0.25
        out = bpy.data.images.new("o", nw, nh, alpha=False)
        out.pixels.foreach_set(px.reshape(-1))
        out.save_render(dst, scene=scene)
        bpy.data.images.remove(out)
    else:
        im.save_render(dst, scene=scene)
    bpy.data.images.remove(im)
    print("JPEG", dst)

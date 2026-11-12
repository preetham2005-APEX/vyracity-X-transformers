"""Phase B (A7): original procedural trees for Veyra City, authored in Blender, replacing the blob/cone PropMeshes.

Headless:
  "D:/New folder/blender.exe" -b --factory-startup -P Tools/Blender/vc_trees.py -- --out E:/VeyraCity

Writes
  Assets/_Veyra/Art/Models/Vegetation/VC_Tree_{Broadleaf,Pine,Palm,Bush}.fbx   (VC_Unity_Prop preset, base-centre pivot,
      <name>_LOD0/_LOD1/_LOD2 children so Unity builds one LODGroup; material slots MI_Bark / MI_Foliage)
  Assets/_Veyra/Art/Textures/Vegetation/T_Foliage_Atlas.png  (2048 x 1024 RGBA: broadleaf, needles, palm frond, bush leaves, dense conifer strip)
  Assets/_Veyra/Art/Textures/Vegetation/T_Bark.png           (256 x 512 tiling bark)
  Logs/B1/trees.txt                                          (triangles per LOD)
Heights match the prototypes they replace (deciduous 10 m, pine 14 m, palm 12 m, bush 2 m), so the placement scales in
InstancedPropRenderer stay valid. Everything is generated from seeds: no downloaded content, nothing to license.
Foliage cards get normals bent away from the crown centre (soft, volumetric shading instead of flat cards).
"""
import math
import os
import random
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import vc_tools  # noqa: E402


def arg(name, default):
    a = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    return a[a.index(name) + 1] if name in a and a.index(name) + 1 < len(a) else default


ROOT = arg("--out", "E:/VeyraCity")
MODEL_DIR = os.path.join(ROOT, "Assets/_Veyra/Art/Models/Vegetation")
TEX_DIR = os.path.join(ROOT, "Assets/_Veyra/Art/Textures/Vegetation")
LOG = os.path.join(ROOT, "Logs/B1/trees.txt")

# atlas quadrants (u0, v0) of a 0.5 x 0.5 tile
# The atlas is 2048 x 1024: the left half holds four 512 px card tiles (AU scales their u), the third column (u 0.5-0.75)
# an opaque dense-conifer strip for the cone masses of the pine (far LODs and its core).
AU = 0.5
Q_BROAD, Q_NEEDLE, Q_FROND, Q_BUSH = (0.0, 0.5), (0.25, 0.5), (0.0, 0.0), (0.25, 0.0)
CONIFER_U = (0.505, 0.745)


# ---------------------------------------------------------------------------------------------------------- textures

def ellipse(img, cx, cy, rx, ry, ang, col, soft=1.5):
    h, w, _ = img.shape
    r = int(max(rx, ry)) + 3
    x0, x1, y0, y1 = max(0, int(cx) - r), min(w, int(cx) + r), max(0, int(cy) - r), min(h, int(cy) + r)
    if x0 >= x1 or y0 >= y1:
        return
    ys, xs = np.mgrid[y0:y1, x0:x1].astype(np.float32)
    dx, dy = xs - cx, ys - cy
    ca, sa = math.cos(ang), math.sin(ang)
    u, v = (dx * ca + dy * sa) / rx, (-dx * sa + dy * ca) / ry
    d = np.sqrt(u * u + v * v)
    a = np.clip((1.0 - d) * max(rx, ry) / soft, 0.0, 1.0)
    # vein darkening along the long axis, lighter tip side
    shade = 0.85 + 0.15 * np.clip(u, -1, 1) - 0.12 * np.exp(-(v * v) * 60.0)
    for c in range(3):
        img[y0:y1, x0:x1, c] = img[y0:y1, x0:x1, c] * (1 - a) + (col[c] * shade) * a
    img[y0:y1, x0:x1, 3] = np.maximum(img[y0:y1, x0:x1, 3], a)


def line(img, xa, ya, xb, yb, width, col):
    n = int(max(abs(xb - xa), abs(yb - ya))) + 1
    for i in range(n):
        t = i / max(1, n - 1)
        ellipse(img, xa + (xb - xa) * t, ya + (yb - ya) * t, width, width, 0.0, col, soft=1.0)


def foliage_atlas(size=1024, seed=11):
    rng = random.Random(seed)
    img = np.zeros((size, size * 2, 4), np.float32)
    half = size // 2
    twig = (0.22, 0.16, 0.10)
    # broadleaf cluster (top-left): twigs and ~170 leaves
    ox, oy = 0, half
    for _ in range(9):
        a = rng.uniform(0, math.tau)
        line(img, ox + half * 0.5, oy + half * 0.5, ox + half * (0.5 + 0.42 * math.cos(a)), oy + half * (0.5 + 0.42 * math.sin(a)), 2.2, twig)
    for _ in range(170):
        r = half * 0.42 * math.sqrt(rng.random())
        a = rng.uniform(0, math.tau)
        g = rng.uniform(0.75, 1.1)
        col = (0.16 * g, 0.30 * g + rng.uniform(-0.03, 0.04), 0.09 * g)
        ellipse(img, ox + half * 0.5 + r * math.cos(a), oy + half * 0.5 + r * math.sin(a), rng.uniform(10, 17), rng.uniform(5, 8), rng.uniform(0, math.pi), col)
    # needle branch (top-right): one spray filling the tile - main twig, side twigs, dense needles
    ox, oy = half, half
    cy = oy + half * 0.5
    line(img, ox + 10, cy, ox + half - 12, cy, 3.0, twig)
    for i in range(14):
        x0 = ox + 30 + i * (half - 60) / 14.0
        for s in (-1, 1):
            span = half * 0.42 * (1.0 - 0.55 * i / 14.0)
            x1, y1 = x0 + span * 0.6, cy + s * span
            # iteration 21: a soft foliage body under every side twig, so the alpha-tested spray reads as a needle
            # mass and not as dark hairs (CAM_10 fringe)
            mx, my = (x0 + x1) * 0.5 + 4, (cy + y1) * 0.5
            ellipse(img, mx, my, span * 0.62, max(7.0, span * 0.16), math.atan2(y1 - cy, x1 - x0), (0.2, 0.44, 0.16), soft=6.0)   # iteration 35: linear albedo ~0.04 -> ~0.065 (D1: pines black at night)
            line(img, x0, cy, x1, y1, 1.6, twig)
            for k in range(26):
                t = k / 26.0
                px_, py_ = x0 + (x1 - x0) * t, cy + (y1 - cy) * t
                for s2 in (-1, 1):
                    ln = rng.uniform(10, 18) * (1.0 - 0.5 * t)
                    g = rng.uniform(0.85, 1.15) * (1.0 + 0.25 * t)   # iteration 21: lighter new growth towards the tips
                    line(img, px_, py_, px_ + ln * 0.8, py_ + s2 * ln * 0.35 + s * ln * 0.3, 2.4, (0.22 * g, 0.5 * g, 0.18 * g))   # iteration 10: fuller, lighter needles (CAM_10 read black and hairy)
    # palm frond (bottom-left): rib along the tile diagonal with leaflets
    ox, oy = 0, 0
    line(img, ox + 16, oy + half * 0.5, ox + half - 10, oy + half * 0.5, 2.5, (0.36, 0.34, 0.16))
    for i in range(46):
        x = ox + 24 + i * (half - 40) / 46.0
        ln = half * 0.44 * math.sin(math.pi * (0.15 + 0.85 * i / 46.0))
        for s in (-1, 1):
            g = rng.uniform(0.85, 1.1)
            ang = s * 1.15
            ellipse(img, x + ln * 0.25, oy + half * 0.5 + s * ln * 0.5, ln * 0.52, 3.6, ang, (0.20 * g, 0.34 * g, 0.10 * g), soft=1.2)
    # bush leaves (bottom-right): small dense leaves
    ox, oy = half, 0
    for _ in range(520):
        r = half * 0.46 * math.sqrt(rng.random())
        a = rng.uniform(0, math.tau)
        g = rng.uniform(0.7, 1.1)
        ellipse(img, ox + half * 0.5 + r * math.cos(a), oy + half * 0.5 + r * math.sin(a), rng.uniform(9, 15), rng.uniform(5, 8), rng.uniform(0, math.pi), (0.14 * g, 0.28 * g, 0.10 * g))
    # dense conifer strip (x 1024-1536, full height): opaque dark-green base and thousands of short needle strokes
    x0, x1 = size, size + half
    base = np.array((0.13, 0.34, 0.12), np.float32)   # iteration 8: greener (read grey in CAM_10)
    img[:, x0:x1, :3] = base
    img[:, x0:x1, 3] = 1.0
    for _ in range(5200):
        x = rng.uniform(x0 + 2, x1 - 2)
        y = rng.uniform(2, size - 2)
        ln = rng.uniform(6, 14)
        g = rng.uniform(0.7, 1.35)
        ang = rng.uniform(-0.9, 0.9)
        line(img, x, y, x + math.sin(ang) * ln, y - math.cos(ang) * ln, 1.2, (0.16 * g, 0.45 * g, 0.16 * g))
    img[:, x0:x1, 3] = 1.0
    # dilate colour into transparent texels so mips do not bleed black
    rgb, a = img[..., :3], img[..., 3:4]
    fill = (0.14, 0.24, 0.09)
    img[..., :3] = rgb * np.clip(a * 4, 0, 1) + np.array(fill, np.float32) * (1 - np.clip(a * 4, 0, 1))
    return img


def bark_texture(w=256, h=512, seed=5):
    rng = np.random.default_rng(seed)
    base = rng.random((h // 8, w // 16)).astype(np.float32)
    big = np.kron(base, np.ones((8, 16), np.float32))
    noise = rng.random((h, w)).astype(np.float32) * 0.25
    # vertical furrows
    x = np.arange(w, dtype=np.float32)
    furrow = 0.5 + 0.5 * np.sin(x / w * math.tau * 9 + np.sin(np.arange(h)[:, None] / 37.0) * 1.3)
    v = np.clip(0.35 + 0.35 * big + noise + 0.25 * furrow, 0, 1)
    img = np.zeros((h, w, 4), np.float32)
    img[..., 0] = 0.20 * v + 0.05
    img[..., 1] = 0.15 * v + 0.04
    img[..., 2] = 0.11 * v + 0.03
    img[..., 3] = 1.0
    return img


def save_png(arr, path):
    h, w, _ = arr.shape
    img = bpy.data.images.new(os.path.basename(path), w, h, alpha=True)
    img.pixels = arr.reshape(-1).tolist()
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()
    bpy.data.images.remove(img)


# ---------------------------------------------------------------------------------------------------------- geometry

class Builder:
    def __init__(self):
        self.bm = bmesh.new()
        self.uv = self.bm.loops.layers.uv.new("UVMap")
        self.normals = []  # per loop custom normal, in face loop order

    def face(self, verts, uvs, mat, normals=None):
        vs = [self.bm.verts.new(v) for v in verts]
        f = self.bm.faces.new(vs)
        f.material_index = mat
        for loop, uv in zip(f.loops, uvs):
            loop[self.uv].uv = uv
        n = normals or [None] * len(verts)
        self.normals.append(n)
        return f

    def tube(self, path, radii, sides, mat, v_scale=1.0):
        """A tapered tube along points, bark UVs (u around, v along in metres / 2)."""
        rings = []
        for i, p in enumerate(path):
            t = (path[min(i + 1, len(path) - 1)] - path[max(i - 1, 0)]).normalized()
            a = t.orthogonal().normalized()
            b = t.cross(a).normalized()
            rings.append([p + (a * math.cos(k / sides * math.tau) + b * math.sin(k / sides * math.tau)) * radii[i] for k in range(sides + 1)])
        vlen = 0.0
        for i in range(len(path) - 1):
            seg = (path[i + 1] - path[i]).length
            for k in range(sides):
                q = [rings[i][k], rings[i][k + 1], rings[i + 1][k + 1], rings[i + 1][k]]
                uv = [(k / sides, vlen / 2 * v_scale), ((k + 1) / sides, vlen / 2 * v_scale),
                      ((k + 1) / sides, (vlen + seg) / 2 * v_scale), (k / sides, (vlen + seg) / 2 * v_scale)]
                self.face(q, uv, mat)
            vlen += seg

    def card(self, centre, right, up, quad, mat, crown_centre=None, bend=0.6):
        """A two-sided leaf card. Normals lean away from the crown centre (soft volumetric shading)."""
        u0, v0 = quad
        corners = [centre - right - up, centre + right - up, centre + right + up, centre - right + up]
        uvs = [(u0 + 0.01 * AU, v0 + 0.01), (u0 + 0.49 * AU, v0 + 0.01), (u0 + 0.49 * AU, v0 + 0.49), (u0 + 0.01 * AU, v0 + 0.49)]
        fn = right.cross(up).normalized()
        # iteration 9: one face per card (the foliage material is two-sided; a reversed twin with inward normals
        # z-fought it and rendered the trees near black)
        for side in (1,):
            n = fn * side
            nrm = []
            for c in corners:
                if crown_centre is not None:
                    out = (c - crown_centre).normalized()
                    nrm.append((n * (1 - bend) + out * bend).normalized())
                else:
                    nrm.append(n)
            if side == 1:
                self.face(corners, uvs, mat, nrm)
            else:
                self.face(list(reversed(corners)), list(reversed(uvs)), mat, list(reversed(nrm)))

    def to_object(self, name):
        me = bpy.data.meshes.new(name)
        self.bm.to_mesh(me)
        self.bm.free()
        # custom split normals: faces were created in order; loops follow
        loop_normals = []
        for poly, n in zip(me.polygons, self.normals):
            for i, li in enumerate(poly.loop_indices):
                loop_normals.append(n[i] if n[i] is not None else None)
        if hasattr(me, "use_auto_smooth"):
            me.use_auto_smooth = True
        default = [tuple(me.polygons[p].normal) for p in range(len(me.polygons))]
        flat = []
        li = 0
        for p, poly in enumerate(me.polygons):
            for _ in poly.loop_indices:
                n = loop_normals[li]
                flat.append(tuple(n) if n is not None else default[p])
                li += 1
        me.normals_split_custom_set(flat)
        for mname in ("MI_Bark", "MI_Foliage"):
            mat = bpy.data.materials.get(mname) or bpy.data.materials.new(mname)
            me.materials.append(mat)
        obj = bpy.data.objects.new(name, me)
        bpy.context.scene.collection.objects.link(obj)
        return obj


def broadleaf(detail, seed=3, spread=1.0, zsq=1.25, crownz=6.6):
    """spread scales the branch reach, zsq flattens (>1) or elongates (<1) the crown ellipsoid (world iteration 7:
    a second, taller and narrower broadleaf for species variety)."""
    rng = random.Random(seed)
    b = Builder()
    sides = [8, 6, 4][detail]
    crown = Vector((0, 0, crownz))
    trunk = [Vector((0, 0, 0)), Vector((0.05, 0, 1.5)), Vector((0.1, 0.05, 3.0)), Vector((0.05, 0.1, 4.2))]
    b.tube(trunk, [0.32, 0.26, 0.22, 0.18], sides, 0)
    n_branch = [6, 5, 4][detail]
    tips = []
    for i in range(n_branch):
        a = i / n_branch * math.tau + rng.uniform(-0.3, 0.3)
        out = Vector((math.cos(a), math.sin(a), 0))
        start = trunk[-1] + Vector((0, 0, rng.uniform(-0.6, 0.3)))
        mid = start + out * rng.uniform(1.0, 1.6) * spread + Vector((0, 0, rng.uniform(1.2, 1.8)))
        end = mid + out * rng.uniform(1.0, 1.8) * spread + Vector((0, 0, rng.uniform(0.8, 1.8) / max(0.6, spread)))
        if detail < 2:
            b.tube([start, mid, end], [0.13, 0.08, 0.035], max(3, sides - 3), 0)
        tips += [mid, end, (mid + end) * 0.5 + Vector((0, 0, 0.5))]
    tips.append(Vector((0, 0, 8.8)))
    cards = [140, 56, 22][detail]
    size = [1.25, 1.75, 2.6][detail]
    for _ in range(cards):
        t = rng.choice(tips)
        c = t + Vector((rng.gauss(0, 1.1 * spread), rng.gauss(0, 1.1 * spread), rng.gauss(0, 0.8)))
        # keep the crown roughly ellipsoidal (9.9 m top)
        d = c - crown
        d.z *= zsq
        d.x /= spread
        d.y /= spread
        if d.length > 3.4:
            c = crown + (c - crown) * (3.4 / d.length)
        r = Vector((rng.uniform(-1, 1), rng.uniform(-1, 1), rng.uniform(-0.3, 0.3))).normalized()
        up = r.cross(Vector((rng.uniform(-1, 1), rng.uniform(-1, 1), rng.uniform(-1, 1)))).normalized()
        b.card(c, r * size, up * size, Q_BROAD, 1, crown)
    return b


def cone_mass(b, tiers, sides, z0=2.6, top=14.0, r0=3.0, rng=None):
    """Stacked, slightly drooping cones textured with the dense leaf quadrant: the conifer's body, opaque from afar
    (B1 review: needle cards alone alpha-tested away beyond ~150 m and far pines read as bare trunks)."""
    span = (top - z0) / tiers
    for t in range(tiers):
        zb = z0 + t * span * 0.82
        zt = min(top, zb + span * 1.55)
        rb = r0 * (1.0 - t / (tiers + 0.6))
        rot = (rng.uniform(0, 1) if rng else 0.0) * math.tau / sides
        for k in range(sides):
            a0, a1 = rot + k / sides * math.tau, rot + (k + 1) / sides * math.tau
            p0 = Vector((math.cos(a0) * rb, math.sin(a0) * rb, zb - 0.35))
            p1 = Vector((math.cos(a1) * rb, math.sin(a1) * rb, zb - 0.35))
            apex = Vector((0, 0, zt))
            mid = (p0 + p1) * 0.5
            n0 = Vector((p0.x, p0.y, rb * 0.6)).normalized()
            n1 = Vector((p1.x, p1.y, rb * 0.6)).normalized()
            na = Vector((mid.x, mid.y, rb * 1.4)).normalized()
            ua, ub = CONIFER_U[0] + (CONIFER_U[1] - CONIFER_U[0]) * k / sides, CONIFER_U[0] + (CONIFER_U[1] - CONIFER_U[0]) * (k + 1) / sides
            uvs = [(ua, 0.02), (ub, 0.02), ((ua + ub) * 0.5, 0.98)]
            b.face([p0, p1, apex], uvs, 1, [n0, n1, na])


def pine(detail, seed=7):
    rng = random.Random(seed)
    b = Builder()
    sides = [7, 5, 4][detail]
    b.tube([Vector((0, 0, 0)), Vector((0, 0, 5)), Vector((0, 0, 10)), Vector((0, 0, 14))], [0.28, 0.2, 0.11, 0.02], sides, 0)
    cone_mass(b, [5, 4, 3][detail], [9, 7, 6][detail], r0=[1.9, 2.7, 3.1][detail], rng=rng)   # iteration 8: smaller core up close
    whorls = [12, 5, 0][detail]
    per = [8, 5, 4][detail]
    for w in range(whorls):
        t = w / max(1, whorls - 1)
        z = 3.0 + t * 10.6
        ln = 3.1 * (1 - t) + 0.6
        for i in range(per):
            a = i / per * math.tau + w * 0.7 + rng.uniform(-0.2, 0.2)
            out = Vector((math.cos(a), math.sin(a), 0))
            base = Vector((0, 0, z))
            tip = base + out * ln + Vector((0, 0, -0.45 * ln))
            centre = (base + tip) * 0.5
            right = (tip - base) * 0.5
            up = out.cross(Vector((0, 0, 1))).normalized() * (0.9 + 0.6 * (1 - t))
            # needle cards lie close to horizontal: tilt them a little up so they read from below
            up = (up + Vector((0, 0, 0.25))).normalized() * up.length
            b.card(centre, right, up, Q_NEEDLE, 1, Vector((0, 0, z + 1.0)), bend=0.45)
            if detail == 0:   # a crossing card per branch: volume from the side
                b.card(centre, right, Vector((0, 0, 1)) * (0.6 + 0.4 * (1 - t)), Q_NEEDLE, 1, Vector((0, 0, z + 1.0)), bend=0.45)
    return b


def palm(detail, seed=9):
    rng = random.Random(seed)
    b = Builder()
    sides = [8, 6, 4][detail]
    pts, rad = [], []
    segs = [10, 6, 4][detail]
    for i in range(segs + 1):
        t = i / segs
        pts.append(Vector((0.9 * t * t, 0.0, t * 11.2)))
        rad.append(0.26 - 0.08 * t)
    b.tube(pts, rad, sides, 0, v_scale=2.0)
    top = pts[-1]
    fronds = [11, 8, 6][detail]
    fsegs = [5, 3, 2][detail]
    for i in range(fronds):
        a = i / fronds * math.tau + rng.uniform(-0.15, 0.15)
        out = Vector((math.cos(a), math.sin(a), 0))
        side = out.cross(Vector((0, 0, 1))).normalized()
        L = rng.uniform(3.6, 4.4)
        prev = None
        for s in range(fsegs + 1):
            t = s / fsegs
            p = top + out * (L * t) + Vector((0, 0, 0.9 * math.sin(t * math.pi * 0.6) - 1.9 * t * t))
            w = 0.95 * math.sin(math.pi * (0.1 + 0.9 * t)) + 0.1
            cur = (p - side * w, p + side * w, t)
            if prev:
                u0, v0 = Q_FROND
                quad = [prev[0], prev[1], cur[1], cur[0]]
                uvs = [(u0 + (0.01 + 0.48 * prev[2]) * AU, v0 + 0.01), (u0 + (0.01 + 0.48 * prev[2]) * AU, v0 + 0.49),
                       (u0 + (0.01 + 0.48 * cur[2]) * AU, v0 + 0.49), (u0 + (0.01 + 0.48 * cur[2]) * AU, v0 + 0.01)]
                n = (quad[1] - quad[0]).cross(quad[3] - quad[0]).normalized()
                if n.z < 0:
                    n = -n
                b.face(quad, uvs, 1, [n] * 4)
            prev = cur
    return b


def bush(detail, seed=13):
    rng = random.Random(seed)
    b = Builder()
    centre = Vector((0, 0, 0.9))
    cards = [40, 16, 7][detail]
    size = [0.7, 0.95, 1.2][detail]
    for _ in range(cards):
        c = centre + Vector((rng.gauss(0, 0.45), rng.gauss(0, 0.45), rng.gauss(0, 0.3)))
        c.z = max(c.z, 0.35)
        r = Vector((rng.uniform(-1, 1), rng.uniform(-1, 1), rng.uniform(-0.4, 0.4))).normalized()
        up = r.cross(Vector((rng.uniform(-1, 1), rng.uniform(-1, 1), rng.uniform(-1, 1)))).normalized()
        b.card(c, r * size, up * size, Q_BUSH, 1, centre, bend=0.7)
    # a stub so the bark slot exists (hidden inside the bush)
    b.tube([Vector((0, 0, 0)), Vector((0, 0, 0.5))], [0.05, 0.03], 3, 0)
    return b


def main():
    os.makedirs(MODEL_DIR, exist_ok=True)
    os.makedirs(TEX_DIR, exist_ok=True)
    os.makedirs(os.path.dirname(LOG), exist_ok=True)
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    vc_tools.vc_setup_scene()
    save_png(foliage_atlas(), os.path.join(TEX_DIR, "T_Foliage_Atlas.png"))
    save_png(bark_texture(), os.path.join(TEX_DIR, "T_Bark.png"))
    lines = []
    for name, fn in (("VC_Tree_Broadleaf", broadleaf), ("VC_Tree_Broadleaf2", lambda d: broadleaf(d, seed=29, spread=0.72, zsq=0.8, crownz=7.0)),
                     ("VC_Tree_Pine", pine), ("VC_Tree_Palm", palm), ("VC_Tree_Bush", bush)):
        parent = bpy.data.objects.new(name, None)
        bpy.context.scene.collection.objects.link(parent)
        lods = []
        for lod in range(3):
            obj = fn(lod).to_object(f"{name}_LOD{lod}")
            obj.parent = parent
            lods.append(obj)
        tris = [vc_tools.vc_tri_count([o]) for o in lods]
        path = os.path.join(MODEL_DIR, name + ".fbx")
        vc_tools.vc_export_fbx([parent], path, mode="prop", path_mode="STRIP")
        lines.append(f"{name}: tris LOD0 {tris[0]}, LOD1 {tris[1]} ({100 * tris[1] / tris[0]:.0f}%), LOD2 {tris[2]} ({100 * tris[2] / tris[0]:.0f}%) -> {path}")
        for o in lods + [parent]:
            bpy.data.objects.remove(o, do_unlink=True)
    with open(LOG, "w", encoding="utf-8") as f:
        f.write("\n".join(lines) + "\n")
    print("\n".join(lines))


main()

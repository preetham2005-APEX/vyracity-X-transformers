"""Rock geometry primitives.

A rock is a UV sphere, displaced by deterministic value noise, then CLIPPED by a set of random
half-spaces. The clipping is what makes it read as rock rather than as a potato: real boulders are
bounded by fracture planes, and plane-clipping a noisy sphere reproduces that at almost no cost.
Faces are flat-shaded (custom normals = face normal) and UV'd by dominant-axis box projection
against a single tiling rock texture, so the whole kit is one material.
"""
import math

from mathutils import Vector

TILE_M = 2.5          # metres of rock surface per texture repeat


# ---------------------------------------------------------------- deterministic noise
def _h(x, y, z, seed):
    v = math.sin(x * 127.1 + y * 311.7 + z * 74.7 + seed * 43.3) * 43758.5453
    return v - math.floor(v)


def noise3(p, freq, seed):
    """Smooth value noise on a lattice. Deterministic for a given (p, freq, seed)."""
    x, y, z = p[0] * freq, p[1] * freq, p[2] * freq
    xi, yi, zi = math.floor(x), math.floor(y), math.floor(z)
    xf, yf, zf = x - xi, y - yi, z - zi
    u = xf * xf * (3 - 2 * xf)
    v = yf * yf * (3 - 2 * yf)
    w = zf * zf * (3 - 2 * zf)
    out = 0.0
    for dz in (0, 1):
        for dy in (0, 1):
            for dx in (0, 1):
                c = _h(xi + dx, yi + dy, zi + dz, seed)
                wx = u if dx else (1 - u)
                wy = v if dy else (1 - v)
                wz = w if dz else (1 - w)
                out += c * wx * wy * wz
    return out * 2.0 - 1.0


def fbm3(p, seed, octaves=4, freq=1.0, gain=0.5, lac=2.1):
    out, amp, f = 0.0, 1.0, freq
    norm = 0.0
    for _ in range(octaves):
        out += amp * noise3(p, f, seed)
        norm += amp
        amp *= gain
        f *= lac
    return out / max(norm, 1e-6)


# ---------------------------------------------------------------- rock mesh
def uv_sphere(segments, rings):
    """Vertices and quad/tri faces of a unit UV sphere. Poles are triangle fans."""
    verts = [Vector((0, 0, 1))]
    for r in range(1, rings):
        phi = math.pi * r / rings
        z, s = math.cos(phi), math.sin(phi)
        for k in range(segments):
            th = 2 * math.pi * k / segments
            verts.append(Vector((math.cos(th) * s, math.sin(th) * s, z)))
    verts.append(Vector((0, 0, -1)))
    bot = len(verts) - 1
    faces = []
    for k in range(segments):
        faces.append((0, 1 + (k + 1) % segments, 1 + k))
    for r in range(rings - 2):
        a0 = 1 + r * segments
        a1 = 1 + (r + 1) * segments
        for k in range(segments):
            k2 = (k + 1) % segments
            faces.append((a0 + k, a0 + k2, a1 + k2, a1 + k))
    a0 = 1 + (rings - 2) * segments
    for k in range(segments):
        faces.append((bot, a0 + k, a0 + (k + 1) % segments))
    return verts, faces


def clip_planes(count, seed, bias_down=0.0):
    """Random half-spaces used as fracture planes. `bias_down` pushes planes toward the underside so
    a boulder gets a flatter base and sits convincingly on the ground."""
    r = _h
    planes = []
    for i in range(count):
        a = r(i * 1.7, 2.3, seed * 0.5, seed) * 2 * math.pi
        b = r(i * 3.1, 5.9, seed * 1.5, seed + 7) * 2 - 1
        s = math.sqrt(max(0.0, 1 - b * b))
        n = Vector((math.cos(a) * s, math.sin(a) * s, b - bias_down)).normalized()
        d = 0.62 + 0.30 * r(i * 9.3, 1.1, seed, seed + 13)
        planes.append((n, d))
    return planes


def rock_mesh(segments, rings, scale, seed, noise_amp=0.22, noise_freq=1.6, planes=0,
              plane_bias=0.0, flatten_z=None, octaves=4):
    """One rock. Returns (verts, faces) in local space, already scaled.

    flatten_z: if given, every vertex below that z is raised to it, giving a flat sitting base.
    """
    verts, faces = uv_sphere(segments, rings)
    pl = clip_planes(planes, seed, plane_bias) if planes else []
    out = []
    for v in verts:
        d = Vector(v)
        n = fbm3((d.x * noise_freq, d.y * noise_freq, d.z * noise_freq), seed, octaves)
        p = d * (1.0 + noise_amp * n)
        for nrm, dist in pl:
            dot = p.dot(nrm)
            if dot > dist:
                p = p - nrm * (dot - dist)
        p = Vector((p.x * scale[0], p.y * scale[1], p.z * scale[2]))
        out.append(p)
    if flatten_z is not None:
        for p in out:
            if p.z < flatten_z:
                p.z = flatten_z
    return out, faces


def box_uv(p, nrm, tile=TILE_M, offset=(0.0, 0.0)):
    """Dominant-axis planar projection. UVs tile freely (the rock texture is a seamless tiling set,
    not an atlas), so no rect clamping is needed."""
    ax, ay, az = abs(nrm.x), abs(nrm.y), abs(nrm.z)
    if az >= ax and az >= ay:
        u, v = p.x, p.y
    elif ax >= ay:
        u, v = p.y, p.z
    else:
        u, v = p.x, p.z
    return (u / tile + offset[0], v / tile + offset[1])


def add_rock(b, verts, faces, mat, origin=(0, 0, 0), tile=TILE_M, uv_offset=(0.0, 0.0),
             smooth=0.0):
    """Emit a rock into a Builder with flat shading and box-projected UVs.

    `smooth` blends the face normal toward the radial direction (0 = fully faceted, 1 = fully
    rounded); weathered rock wants a little, fractured rock wants none.
    """
    o = Vector(origin)
    centre = Vector((0, 0, 0))
    for v in verts:
        centre += v
    centre /= max(1, len(verts))
    for f in faces:
        ps = [Vector(verts[i]) + o for i in f]
        e1 = ps[1] - ps[0]
        e2 = ps[2] - ps[0]
        n = e1.cross(e2)
        if n.length < 1e-9:
            continue
        n.normalize()
        fc = Vector((0, 0, 0))
        for p in ps:
            fc += p
        fc /= len(ps)
        if (fc - (centre + o)).dot(n) < 0:
            ps = list(reversed(ps))
            n = -n
        if smooth > 0.0:
            rad = (fc - (centre + o))
            if rad.length > 1e-6:
                n = n.lerp(rad.normalized(), smooth).normalized()
        uvs = [box_uv(p, n, tile, uv_offset) for p in ps]
        if len(ps) == 3:
            b.tri(ps[0], ps[1], ps[2], uvs, mat, [n] * 3)
        else:
            b.quad(ps[0], ps[1], ps[2], ps[3], uvs, mat, [n] * 4)


def mesh_bounds(verts):
    lo = Vector((1e9, 1e9, 1e9))
    hi = Vector((-1e9, -1e9, -1e9))
    for v in verts:
        lo = Vector((min(lo.x, v.x), min(lo.y, v.y), min(lo.z, v.z)))
        hi = Vector((max(hi.x, v.x), max(hi.y, v.y), max(hi.z, v.z)))
    return lo, hi

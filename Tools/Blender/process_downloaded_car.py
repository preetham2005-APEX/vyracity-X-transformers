"""
Turn a downloaded car model (Sketchfab glTF zip, .glb, .gltf, .blend, .fbx, .obj) into a VEYRA CITY vehicle asset.

  blender -b --factory-startup --python process_downloaded_car.py -- \
      --src <file in ArtSource/Downloads> --id <car_id> --name "<Display Name>" [--length 4.6] [--flip auto|0|1]
      [--part "<object name prefix>"] [--strip "Carbadges,clearcoat"] [--lod0 120000] [--paint "Bodymat,body_one"]

Pipeline (CLAUDE.md "Blender -> Unity" and "Vehicles in Blender"):
  * licence gate: <download>.licence.json must exist next to the download (CC0, CC-BY-4.0, own-work or
    purchased-royalty-free, with author and url); names of other games are refused
  * import; optional --part keeps one car out of a multi-car pack (the body objects with that prefix and the
    parts inside its footprint) and turns it so its long axis is Blender Y
  * strip: fake shadow planes, faces with materials matching --strip (real-brand-like badges, clear-coat shells)
  * front detection from part names (front/rear, FL/RR, headlight/tail...), front ends at Blender -Y
    (Unity +Z after the -Z forward / Y up export); scale to --length when the model's own scale is implausible
  * wheels: candidate parts (wheel-ish names or materials) are split into loose pieces, grouped per corner;
    the hub is the centre of the tyre, every piece inside the tyre cylinder spins with it
  * Wheel_FL/FR/RL/RR empties at the hub centres (location is not applied), Caliper_* empties for brake parts
  * Body_LOD0/1/2 (LOD0 <= --lod0 tris, LOD1 <= 40k, LOD2 <= 10k), UBX_Chassis box, UCX_Body convex hull <= 250 faces
  * materials.json (glTF PBR values, texture transforms, roles) and the textures, with metallic-roughness
    repacked to Unity's metallic (R) + smoothness (A)
  * FBX (-Z forward, Y up, baked space transform, no embedded media), process_report.json, preview PNG
"""
import bpy
import bmesh
import json
import math
import os
import re
import shutil
import sys
import zipfile
from mathutils import Vector, Matrix

ROOT = "E:/VeyraCity"
ART = ROOT + "/ArtSource/Vehicles"
WORK = "E:/Temp/VeyraCity/cars"
ALLOWED_LICENCES = ("CC0", "CC-BY-4.0", "own-work", "purchased-royalty-free")
FORBIDDEN = ("gta", "grand theft", "liberty city", "rockstar", "cyberpunk", "night city", "cdpr", "forza", "need for speed",
             "nfs", "asphalt", "gran turismo", "burnout", "midnight club")
WHEEL_WORDS = ("wheel", "wheek", "tire", "tyre", "rim", "rims", "hub", "rueda", "roda", "llanta", "felge", "reifen")
BRAKE_WORDS = ("brake", "caliper", "calliper", "disc", "disk", "rotor")
TYRE_MAT_WORDS = ("tire", "tyre", "rubber")
# Whole tokens only (single letters such as "f"/"b" are too ambiguous: packs use them as variant letters).
FRONT_TOKENS = {"fl", "fr", "front", "headlight", "headlights", "headlamp", "hood", "bonnet", "grille", "grill", "radiator"}
REAR_TOKENS = {"bl", "br", "rl", "rr", "rear", "back", "tail", "taillight", "taillights", "trunk", "boot", "reverse", "exhaust", "muffler"}


def args():
    a = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    o = {"src": None, "id": None, "name": None, "length": "0", "flip": "auto", "lod0": "120000", "part": "", "strip": "",
         "paint": "", "fix": "", "blank": "", "stripbox": "", "checkviews": "0", "forcelen": "0", "stripfaces": "", "out": ART}
    i = 0
    while i < len(a):
        k = a[i].lstrip("-")
        if k in o and i + 1 < len(a):
            o[k] = a[i + 1]
            i += 1
        i += 1
    return o


# ------------------------------------------------------------------------------------------------ licence gate
def licence_gate(src):
    low = os.path.basename(src.lower().replace("\\", "/"))
    base = os.path.splitext(src)[0]
    side = base + ".licence.json"
    if not os.path.exists(side):
        raise SystemExit("REFUSED: no licence sidecar " + side)
    lic = json.load(open(side, encoding="utf-8"))
    text = low + " " + str(lic.get("title", "")).lower()
    for w in FORBIDDEN:
        hit = re.search(r"(^|[^a-z])nfs([^a-z]|$)", text) if w == "nfs" else w in text
        if hit:
            raise SystemExit("REFUSED: '%s' looks like content from another game (%s)" % (src, w))
    if lic.get("licence") not in ALLOWED_LICENCES or not lic.get("author") or not lic.get("url"):
        raise SystemExit("REFUSED: licence sidecar incomplete or not allowed: " + json.dumps(lic))
    return lic


# ------------------------------------------------------------------------------------------------ import
def extract(src, work):
    if os.path.isdir(work):
        shutil.rmtree(work, ignore_errors=True)
    os.makedirs(work, exist_ok=True)
    with zipfile.ZipFile(src) as z:
        for m in z.namelist():
            if ".." in m or m.startswith(("/", "\\")) or ":" in m:
                continue
            z.extract(m, work)
    return work


def find_model(folder):
    for root, _, files in os.walk(folder):
        for f in sorted(files):
            if f.lower().endswith((".gltf", ".glb", ".blend", ".fbx", ".obj")):
                return os.path.join(root, f)
    raise RuntimeError("no model file in " + folder)


def import_model(path):
    low = path.lower()
    if low.endswith((".gltf", ".glb")):
        bpy.ops.import_scene.gltf(filepath=path)
    elif low.endswith(".fbx"):
        bpy.ops.import_scene.fbx(filepath=path)
    elif low.endswith(".obj"):
        bpy.ops.wm.obj_import(filepath=path)
    elif low.endswith(".blend"):
        with bpy.data.libraries.load(path, link=False) as (data_from, data_to):
            data_to.objects = list(data_from.objects)
        for ob in data_to.objects:
            if ob is not None:
                bpy.context.scene.collection.objects.link(ob)
    else:
        raise RuntimeError("unsupported " + path)


def meshes():
    return [o for o in bpy.context.scene.objects if o.type == "MESH"]


def world_bbox(objs):
    mn = Vector((1e9, 1e9, 1e9))
    mx = Vector((-1e9, -1e9, -1e9))
    for o in objs:
        for c in o.bound_box:
            w = o.matrix_world @ Vector(c)
            mn = Vector(map(min, mn, w))
            mx = Vector(map(max, mx, w))
    return mn, mx


def select_only(objs, active=None):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    if objs:
        bpy.context.view_layer.objects.active = active or objs[0]


def bake_hierarchy():
    """Clear parents keeping transforms, delete non-meshes, apply all transforms (instanced meshes split)."""
    bpy.context.view_layer.update()
    for o in list(bpy.context.scene.objects):
        if o.parent is not None:
            mw = o.matrix_world.copy()
            o.parent = None
            o.matrix_world = mw
    for o in list(bpy.context.scene.objects):
        if o.type != "MESH" or o.hide_render:
            bpy.data.objects.remove(o, do_unlink=True)
    for o in meshes():
        if o.data.users > 1:
            o.data = o.data.copy()
    select_only(meshes())
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)


def mat_names(o):
    return [s.material.name for s in o.material_slots if s.material]


def tokens(name):
    return set(t for t in re.split(r"[^a-z0-9]+", re.sub(r"([a-z])([A-Z])", r"\1_\2", name).lower()) if t)


# ------------------------------------------------------------------------------------------------ pack split
def keep_part(prefix):
    """Keep one car of a multi-car pack and turn it so its long axis is +Y."""
    objs = meshes()
    body = [o for o in objs if o.name.lower().startswith(prefix.lower())]
    if not body:
        raise SystemExit("no objects start with '%s'" % prefix)
    pts = [o.matrix_world @ v.co for o in body for v in o.data.vertices]
    cx = sum(p.x for p in pts) / len(pts)
    cy = sum(p.y for p in pts) / len(pts)
    sxx = sum((p.x - cx) ** 2 for p in pts) / len(pts)
    syy = sum((p.y - cy) ** 2 for p in pts) / len(pts)
    sxy = sum((p.x - cx) * (p.y - cy) for p in pts) / len(pts)
    ang = 0.5 * math.atan2(2 * sxy, sxx - syy)          # principal axis angle from +X
    ax = Vector((math.cos(ang), math.sin(ang), 0))
    side = Vector((-ax.y, ax.x, 0))
    half_l = max(abs((p - Vector((cx, cy, p.z))).dot(ax)) for p in pts) + 0.35
    half_w = max(abs((p - Vector((cx, cy, p.z))).dot(side)) for p in pts) + 0.35
    for o in objs:
        if o in body:
            continue
        mn, mx = world_bbox([o])
        c = (mn + mx) / 2
        d = Vector((c.x - cx, c.y - cy, 0))
        size = (mx - mn)
        if abs(d.dot(ax)) > half_l or abs(d.dot(side)) > half_w or max(size.x, size.y) > 2 * half_l:
            bpy.data.objects.remove(o, do_unlink=True)
    rot = Matrix.Rotation(math.pi / 2 - ang, 4, "Z") @ Matrix.Translation(Vector((-cx, -cy, 0)))
    for o in meshes():
        o.matrix_world = rot @ o.matrix_world
    select_only(meshes())
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)


# ------------------------------------------------------------------------------------------------ cleanup
def strip_parts(words):
    removed = []
    words = [w.strip().lower() for w in words if w.strip()]
    for o in list(meshes()):
        n = o.name.lower()
        names = [m.lower() for m in mat_names(o)]
        if "shadow" in n or (names and all("shadow" in m for m in names)):
            removed.append(o.name)
            bpy.data.objects.remove(o, do_unlink=True)
            continue
        if not words:
            continue
        bad = [i for i, s in enumerate(o.material_slots) if s.material and any(w in s.material.name.lower() for w in words)]
        if not bad:
            continue
        bm = bmesh.new()
        bm.from_mesh(o.data)
        dead = [f for f in bm.faces if f.material_index in bad]
        bmesh.ops.delete(bm, geom=dead, context="FACES")
        bm.to_mesh(o.data)
        bm.free()
        removed.append("%s (%d faces)" % (o.name, len(dead)))
        if len(o.data.polygons) == 0:
            bpy.data.objects.remove(o, do_unlink=True)
    return removed


# ------------------------------------------------------------------------------------------------ orientation
def detect_front_sign():
    """+1 if the front is at +Y (needs a flip), -1 if at -Y, 0 if unknown. Uses part names only."""
    fy, ry = [], []
    for o in meshes():
        t = tokens(o.name)
        mn, mx = world_bbox([o])
        c = (mn + mx) / 2
        if t & FRONT_TOKENS and not t & REAR_TOKENS:
            fy.append(c.y)
        elif t & REAR_TOKENS and not t & FRONT_TOKENS:
            ry.append(c.y)
    if not fy or not ry:
        return 0, len(fy), len(ry)
    f = sum(fy) / len(fy)
    r = sum(ry) / len(ry)
    if abs(f - r) < 1e-4:
        return 0, len(fy), len(ry)
    return (1 if f > r else -1), len(fy), len(ry)


def orient_and_scale(target_len, flip_opt, force_len=False):
    objs = meshes()
    mn, mx = world_bbox(objs)
    size = mx - mn
    if size.x > size.y:
        for o in objs:
            o.matrix_world = Matrix.Rotation(math.radians(90), 4, "Z") @ o.matrix_world
        select_only(objs)
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    sign, nf, nr = detect_front_sign()
    flip = (sign > 0) if flip_opt == "auto" else flip_opt == "1"
    if flip:
        for o in objs:
            o.matrix_world = Matrix.Rotation(math.radians(180), 4, "Z") @ o.matrix_world
    select_only(objs)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    mn, mx = world_bbox(objs)
    length = mx.y - mn.y
    k = 1.0
    if target_len > 0 and (force_len or length < 3.0 or length > 6.5 or abs(length - target_len) / target_len > 0.25):
        k = target_len / length
    centre = Vector(((mn.x + mx.x) / 2, (mn.y + mx.y) / 2, mn.z))
    for o in objs:
        o.matrix_world = Matrix.Scale(k, 4) @ Matrix.Translation(-centre) @ o.matrix_world
    select_only(objs)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    return k, {"auto_sign": sign, "front_named": nf, "rear_named": nr, "flipped": flip, "mode": flip_opt}


# ------------------------------------------------------------------------------------------------ wheels
def is_wheelish(o):
    n = o.name.lower()
    ms = " ".join(m.lower() for m in mat_names(o))
    return any(w in n for w in WHEEL_WORDS + BRAKE_WORDS) or any(w in ms for w in WHEEL_WORDS)


def is_tyre(o):
    ms = " ".join(m.lower() for m in mat_names(o))
    n = o.name.lower()
    return any(w in ms for w in TYRE_MAT_WORDS) or any(w in n for w in TYRE_MAT_WORDS)


def is_brake(o):
    n = o.name.lower()
    return any(w in n for w in BRAKE_WORDS) and not any(w in n for w in ("wheel", "tire", "tyre", "rim"))


def split_loose(objs):
    if not objs:
        return []
    before = set(meshes())
    select_only(objs)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.separate(type="LOOSE")
    bpy.ops.object.mode_set(mode="OBJECT")
    after = set(meshes())
    return list(set(objs) | (after - before))


def find_wheels():
    objs = meshes()
    mn, mx = world_bbox(objs)
    H = mx.z - mn.z
    W = mx.x - mn.x
    cands = [o for o in objs if is_wheelish(o)]
    pieces = [p for p in split_loose(cands) if p.name in bpy.context.scene.objects]
    corners = {"FL": [], "FR": [], "RL": [], "RR": []}
    for p in pieces:
        a, b = world_bbox([p])
        c = (a + b) / 2
        if c.z > H * 0.55 or abs(c.x) < W * 0.15:
            continue  # steering wheel, spare wheel, centre parts
        # Front is Blender -Y, so the car's left side is +X (facing -Y with Z up, right = -Y x Z = -X).
        # After the -Z forward / Y up export that side is Unity -X, as CLAUDE.md expects for Wheel_FL/RL.
        key = ("F" if c.y < 0 else "R") + ("L" if c.x > 0 else "R")
        corners[key].append(p)
    hubs = {}
    for key, ps in corners.items():
        if not ps:
            continue
        def disc(p):
            a, b = world_bbox([p])
            s = b - a
            if s.x > 0.6:
                return 0  # wider than any tyre: an axle or body part
            return min(s.y, s.z) if abs(s.y - s.z) < 0.35 * max(s.y, s.z, 1e-3) else 0
        # The tyre is the union of the largest disc-like pieces (tread, sidewalls, sidewall decals), preferring
        # tyre materials; it defines the hub centre, radius and width.
        pool = [p for p in ps if is_tyre(p) and disc(p) > 0] or [p for p in ps if disc(p) > 0] or ps
        dmax = max(disc(p) for p in pool)
        tyres = [p for p in pool if disc(p) >= 0.85 * dmax] or [max(pool, key=disc)]
        a, b = world_bbox(tyres)
        c = (a + b) / 2
        r = max(b.z - a.z, b.y - a.y) / 2
        w = b.x - a.x
        if r < 0.15 or r > 0.75:
            continue
        hubs[key] = {"centre": c, "radius": r, "width": w}
    groups = {k: [] for k in corners}
    calipers = {k: [] for k in corners}
    for key, ps in corners.items():
        if key not in hubs:
            continue
        h = hubs[key]
        c, r, w = h["centre"], h["radius"], h["width"]
        for p in ps:
            a, b = world_bbox([p])
            inside = (max(abs(a.y - c.y), abs(b.y - c.y)) <= r * 1.06 and max(abs(a.z - c.z), abs(b.z - c.z)) <= r * 1.06
                      and a.x >= c.x - w / 2 - 0.12 and b.x <= c.x + w / 2 + 0.12)
            if not inside:
                continue
            (calipers if is_brake(p) else groups)[key].append(p)
    return groups, calipers, hubs


def tri_count(o):
    return sum(len(p.vertices) - 2 for p in o.data.polygons)


def decimate(o, ratio):
    if ratio >= 0.999:
        return
    m = o.modifiers.new("dec", "DECIMATE")
    m.ratio = max(0.01, ratio)
    m.use_collapse_triangulate = True
    select_only([o])
    bpy.ops.object.modifier_apply(modifier=m.name)


def decimated_copy(src, name, ratio):
    o = src.copy()
    o.data = src.data.copy()
    o.name = name
    bpy.context.scene.collection.objects.link(o)
    decimate(o, ratio)
    return o


def join(objs, name):
    select_only(objs)
    if len(objs) > 1:
        bpy.ops.object.join()
    o = bpy.context.view_layer.objects.active
    o.name = name
    o.data.name = name
    return o


def convex_hull_object(src, name, max_faces=250):
    pts = [src.matrix_world @ v.co for v in src.data.vertices]
    for _ in range(8):
        bm = bmesh.new()
        verts = [bm.verts.new(p) for p in pts]
        res = bmesh.ops.convex_hull(bm, input=verts)
        for v in [v for v in bm.verts if v not in set(res["geom"])]:
            bm.verts.remove(v)
        faces = len(bm.faces)
        if faces <= max_faces:
            me = bpy.data.meshes.new(name)
            bm.to_mesh(me)
            bm.free()
            ob = bpy.data.objects.new(name, me)
            bpy.context.scene.collection.objects.link(ob)
            return ob, faces
        # thin the point set: keep hull vertices only, then drop every other one
        hull_pts = [v.co.copy() for v in bm.verts]
        bm.free()
        hull_pts.sort(key=lambda p: (round(p.z, 2), math.atan2(p.y, p.x)))
        pts = hull_pts[::2] if len(hull_pts) > 40 else hull_pts
    raise RuntimeError("hull did not simplify")


# ------------------------------------------------------------------------------------------------ materials
def load_gltf_json(model_path):
    if not model_path.lower().endswith(".gltf"):
        return None
    return json.load(open(model_path, encoding="utf-8"))


def tex_entry(g, info, folder, out_tex, cache):
    if not info:
        return None
    tex = g["textures"][info["index"]]
    src = tex.get("source")
    if src is None:
        ext = tex.get("extensions", {})
        for e in ext.values():
            src = e.get("source", src)
    img = g["images"][src]
    uri = img.get("uri")
    if not uri:
        return None
    path = os.path.join(folder, uri.replace("%20", " "))
    fn = os.path.basename(path)
    if fn not in cache:
        shutil.copyfile(path, os.path.join(out_tex, fn))
        cache[fn] = path
    tr = info.get("extensions", {}).get("KHR_texture_transform", {})
    sx, sy = tr.get("scale", [1, 1])
    ox, oy = tr.get("offset", [0, 0])
    # glTF UV origin is top-left; the mesh UVs are flipped on import, so Unity offset = (ox, 1 - oy - sy)
    return {"file": fn, "tiling": [sx, sy], "offset": [ox, 1 - oy - sy], "texCoord": info.get("texCoord", 0),
            "rotation": tr.get("rotation", 0)}


def repack_metal_rough(src_path, out_path, metal_factor, rough_factor, gloss=False):
    """Unity URP Lit metallic map: R = metallic, A = smoothness. glTF: G = roughness, B = metallic.
    Spec-gloss: A = glossiness. Factors are baked in so the Unity scales stay 1."""
    import numpy as np
    img = bpy.data.images.load(src_path, check_existing=False)
    w, h = img.size
    px = np.empty(w * h * 4, dtype=np.float32)
    img.pixels.foreach_get(px)
    px = px.reshape(-1, 4)
    out = np.zeros_like(px)
    if gloss:
        out[:, 0] = 0.0
        out[:, 3] = np.clip(px[:, 3] * rough_factor, 0, 1)  # rough_factor carries the glossiness factor here
    else:
        out[:, 0] = np.clip(px[:, 2] * metal_factor, 0, 1)
        out[:, 3] = np.clip(1.0 - px[:, 1] * rough_factor, 0, 1)
    o = bpy.data.images.new(os.path.basename(out_path), w, h, alpha=True, float_buffer=False)
    o.colorspace_settings.name = "Non-Color"
    o.pixels.foreach_set(out.reshape(-1))
    o.filepath_raw = out_path
    o.file_format = "PNG"
    o.save()
    bpy.data.images.remove(o)
    bpy.data.images.remove(img)


def convert_spec_gloss(diff_path, sg_path, diff_factor, spec_factor, gloss_factor, out_base, out_ms):
    """KHR_materials_pbrSpecularGlossiness → metallic-roughness, per pixel (Khronos reference conversion).
    Coloured specular (metallic paint authored as black diffuse + coloured specular) becomes base colour + metallic.
    Writes the converted base colour (sRGB) and Unity's metallic (R) + smoothness (A) map. Returns False if the
    two textures differ in size."""
    import numpy as np
    d_img = bpy.data.images.load(diff_path, check_existing=False)
    s_img = bpy.data.images.load(sg_path, check_existing=False)
    if tuple(d_img.size) != tuple(s_img.size):
        bpy.data.images.remove(d_img)
        bpy.data.images.remove(s_img)
        return False
    w, h = d_img.size
    d = np.empty(w * h * 4, dtype=np.float32)
    s = np.empty(w * h * 4, dtype=np.float32)
    d_img.pixels.foreach_get(d)          # Blender returns linear values for sRGB images
    s_img.pixels.foreach_get(s)
    d = d.reshape(-1, 4)
    s = s.reshape(-1, 4)
    diff = d[:, :3] * np.array(diff_factor[:3], dtype=np.float32)
    spec = s[:, :3] * np.array(spec_factor[:3], dtype=np.float32)
    gloss = np.clip(s[:, 3] * gloss_factor, 0, 1)
    dielectric = 0.04
    eps = 1e-6

    def bright(c):
        return np.sqrt(0.299 * c[:, 0] ** 2 + 0.587 * c[:, 1] ** 2 + 0.114 * c[:, 2] ** 2)
    one_minus = 1.0 - spec.max(axis=1)
    bd, bs = bright(diff), bright(spec)
    a = dielectric
    b = bd * one_minus / (1 - dielectric) + bs - 2 * dielectric
    c = dielectric - bs
    disc = np.maximum(b * b - 4 * a * c, 0)
    metal = np.where(bs < dielectric, 0.0, np.clip((-b + np.sqrt(disc)) / (2 * a), 0, 1))
    from_diff = diff * (one_minus / (1 - dielectric) / np.maximum(1 - metal, eps))[:, None]
    from_spec = (spec - dielectric * (1 - metal)[:, None]) / np.maximum(metal, eps)[:, None]
    base = np.clip(from_diff + (from_spec - from_diff) * (metal ** 2)[:, None], 0, 1)
    out = np.concatenate([base, d[:, 3:4] * diff_factor[3]], axis=1)
    o = bpy.data.images.new(os.path.basename(out_base), w, h, alpha=True, float_buffer=False)
    o.pixels.foreach_set(out.reshape(-1))   # linear in, saved as sRGB PNG
    o.filepath_raw = out_base
    o.file_format = "PNG"
    o.save()
    bpy.data.images.remove(o)
    ms = np.zeros((w * h, 4), dtype=np.float32)
    ms[:, 0] = metal
    ms[:, 3] = gloss
    o = bpy.data.images.new(os.path.basename(out_ms), w, h, alpha=True, float_buffer=False)
    o.colorspace_settings.name = "Non-Color"
    o.pixels.foreach_set(ms.reshape(-1))
    o.filepath_raw = out_ms
    o.file_format = "PNG"
    o.save()
    for im in (o, d_img, s_img):
        bpy.data.images.remove(im)
    return True


def classify(name, m, paint_words):
    n = name.lower()
    if any(w in n for w in paint_words):
        return "paint"
    if "glass" in n or "window" in n or "optic" in n:
        return "glass" if m.get("alphaMode") == "BLEND" or "glass" in n else "light"
    if "light" in n or "lamp" in n:
        return "light"
    if any(w in n for w in TYRE_MAT_WORDS) or "wheel" in n or "wheek" in n:
        return "tyre"
    if "interior" in n or "seat" in n:
        return "interior"
    if "plate" in n:
        return "plate"
    return "trim"


def export_materials(g, model_path, used, out_dir, paint_words):
    out_tex = os.path.join(out_dir, "textures")
    if os.path.isdir(out_tex):
        shutil.rmtree(out_tex, ignore_errors=True)
    os.makedirs(out_tex, exist_ok=True)
    folder = os.path.dirname(model_path)
    cache = {}
    result = {}
    if g is None:
        for b in used:
            result[b] = {"gltf": None, "role": classify(b, {}, paint_words)}
        return result
    by_name = {}
    for i, m in enumerate(g.get("materials", [])):
        by_name.setdefault(m.get("name", "mat%d" % i), []).append(m)
    for b in sorted(used):
        base = re.sub(r"\.\d{3}$", "", b)
        cand = by_name.get(b) or by_name.get(base)
        if not cand:
            result[b] = {"gltf": None, "role": classify(b, {}, paint_words)}
            continue
        m = cand[0]
        p = m.get("pbrMetallicRoughness", {})
        ext = m.get("extensions", {})
        e = {"gltf": m.get("name"), "alphaMode": m.get("alphaMode", "OPAQUE"), "alphaCutoff": m.get("alphaCutoff", 0.5),
             "doubleSided": m.get("doubleSided", False)}
        sg = ext.get("KHR_materials_pbrSpecularGlossiness")
        if sg:
            e["baseColorFactor"] = sg.get("diffuseFactor", [1, 1, 1, 1])
            e["baseColorTex"] = tex_entry(g, sg.get("diffuseTexture"), folder, out_tex, cache)
            e["metallic"] = 0.0
            e["smoothness"] = sg.get("glossinessFactor", 1.0)
            sgt = tex_entry(g, sg.get("specularGlossinessTexture"), folder, out_tex, cache)
            spec_factor = sg.get("specularFactor", [1, 1, 1])
            if sgt:
                fn = os.path.splitext(sgt["file"])[0] + "_MS.png"
                converted = False
                if e["baseColorTex"] and max(spec_factor) > 0.1:
                    base_fn = os.path.splitext(e["baseColorTex"]["file"])[0] + "_mr.png"
                    converted = convert_spec_gloss(os.path.join(out_tex, e["baseColorTex"]["file"]), os.path.join(out_tex, sgt["file"]),
                                                   e["baseColorFactor"], spec_factor, e["smoothness"],
                                                   os.path.join(out_tex, base_fn), os.path.join(out_tex, fn))
                    if converted:
                        e["baseColorTex"] = dict(e["baseColorTex"], file=base_fn)
                        e["baseColorFactor"] = [1, 1, 1, e["baseColorFactor"][3] if len(e["baseColorFactor"]) > 3 else 1]
                        e["metallic"] = 1.0
                        e["specGlossConverted"] = True
                if not converted:
                    repack_metal_rough(os.path.join(out_tex, sgt["file"]), os.path.join(out_tex, fn), 0, e["smoothness"], gloss=True)
                e["msTex"] = dict(sgt, file=fn)
        else:
            e["baseColorFactor"] = p.get("baseColorFactor", [1, 1, 1, 1])
            e["baseColorTex"] = tex_entry(g, p.get("baseColorTexture"), folder, out_tex, cache)
            mf, rf = p.get("metallicFactor", 1.0), p.get("roughnessFactor", 1.0)
            e["metallic"], e["smoothness"] = mf, 1.0 - rf
            mrt = tex_entry(g, p.get("metallicRoughnessTexture"), folder, out_tex, cache)
            if mrt:
                fn = os.path.splitext(mrt["file"])[0] + "_MS.png"
                if not os.path.exists(os.path.join(out_tex, fn)):
                    repack_metal_rough(os.path.join(out_tex, mrt["file"]), os.path.join(out_tex, fn), mf, rf)
                e["msTex"] = dict(mrt, file=fn)
        nt = m.get("normalTexture")
        if nt:
            e["normalTex"] = tex_entry(g, nt, folder, out_tex, cache)
            e["normalScale"] = nt.get("scale", 1.0)
        e["emissiveFactor"] = m.get("emissiveFactor", [0, 0, 0])
        if "KHR_materials_emissive_strength" in ext:
            s = ext["KHR_materials_emissive_strength"].get("emissiveStrength", 1)
            e["emissiveFactor"] = [c * s for c in e["emissiveFactor"]]
        e["emissiveTex"] = tex_entry(g, m.get("emissiveTexture"), folder, out_tex, cache)
        cc = ext.get("KHR_materials_clearcoat")
        if cc:
            e["clearcoat"] = cc.get("clearcoatFactor", 0)
            e["clearcoatSmoothness"] = 1.0 - cc.get("clearcoatRoughnessFactor", 0)
        e["role"] = classify(b, e, paint_words)
        result[b] = e
    # drop copied source images that only fed a repack
    used_files = set()
    for e in result.values():
        for k in ("baseColorTex", "msTex", "normalTex", "emissiveTex"):
            if e.get(k):
                used_files.add(e[k]["file"])
    for f in os.listdir(out_tex):
        if f not in used_files:
            os.remove(os.path.join(out_tex, f))
    return result


# Livery clean-ups: rectangles (x0, y0, x1, y1, top-left pixel coords at 2048 px) painted with a flat colour.
LIVERY_FIXES = {
    # Police cruiser: a film quote printed on both front wings (kept: POLICE, unit number, emergency text).
    "cop": {"file": "Fairheaven_LT80_Bodymat_baseColor.jpeg", "colour": (1, 1, 1),
            "rects": [(875, 695, 1010, 738), (890, 1908, 1025, 1948)]},
}


def blank_marks(out_dir, specs):
    """Paint out brand marks baked into textures (rebadged lookalikes). Each spec: {"tex": filename substring,
    and one of "full": true | "rect": [x0, y0, x1, y1] | "ring": [cx, cy, r0, r1]} in 0..1 top-left texture
    coordinates. The fill is the median colour around the region (whole texture for "full"). Normal maps of the
    same material are flattened in the same region so embossed lettering disappears too."""
    import numpy as np
    done = []
    tex_dir = os.path.join(out_dir, "textures")
    for spec in specs:
        for fn in sorted(os.listdir(tex_dir)):
            if spec["tex"].lower() not in fn.lower():
                continue
            normal = "normal" in fn.lower()
            img = bpy.data.images.load(os.path.join(tex_dir, fn), check_existing=False)
            w, h = img.size
            px = np.empty(w * h * 4, dtype=np.float32)
            img.pixels.foreach_get(px)
            px = px.reshape(h, w, 4)[::-1]            # top-left origin
            yy, xx = np.mgrid[0:h, 0:w]
            u, v = (xx + 0.5) / w, (yy + 0.5) / h
            if spec.get("full"):
                mask = np.ones((h, w), bool)
                around = mask
            elif "rect" in spec:
                x0, y0, x1, y1 = spec["rect"]
                mask = (u >= x0) & (u <= x1) & (v >= y0) & (v <= y1)
                pad = 0.015
                around = (u >= x0 - pad) & (u <= x1 + pad) & (v >= y0 - pad) & (v <= y1 + pad) & ~mask
            else:
                cx, cy, r0, r1 = spec["ring"]
                d = np.sqrt((u - cx) ** 2 + (v - cy) ** 2)
                mask = (d >= r0) & (d <= r1)
                around = ((d >= r0 - 0.01) & (d < r0)) | ((d > r1) & (d <= r1 + 0.01))
            if normal:
                px[mask, 0:3] = (0.5, 0.5, 1.0)
            else:
                ref = px[around][:, 0:3] if around.any() else px.reshape(-1, 4)[:, 0:3]
                px[mask, 0:3] = np.median(ref, axis=0)
            img.pixels.foreach_set(px[::-1].reshape(-1))
            img.file_format = "PNG" if fn.lower().endswith(".png") else "JPEG"
            img.save()
            bpy.data.images.remove(img)
            done.append("%s %s" % (fn, {k: v for k, v in spec.items() if k != "tex"}))
    return done


def fix_livery(out_dir, kind):
    import numpy as np
    fx = LIVERY_FIXES[kind]
    path = os.path.join(out_dir, "textures", fx["file"])
    img = bpy.data.images.load(path, check_existing=False)
    w, h = img.size
    px = np.empty(w * h * 4, dtype=np.float32)
    img.pixels.foreach_get(px)
    px = px.reshape(h, w, 4)
    k = w / 2048.0
    for x0, y0, x1, y1 in fx["rects"]:
        X0, X1 = int(x0 * k), int(x1 * k)
        Y0, Y1 = h - int(y1 * k), h - int(y0 * k)   # Blender rows start at the bottom
        px[Y0:Y1, X0:X1, 0:3] = fx["colour"]
    img.pixels.foreach_set(px.reshape(-1))
    img.file_format = "JPEG"
    bpy.context.scene.render.image_settings.quality = 95
    img.save()
    bpy.data.images.remove(img)
    return fx["rects"]


def lamp_anchors(mats, mn, mx):
    """Head- and tail-lamp centres (area-weighted) per corner, from faces with lamp materials near the car's ends.
    Returned in Unity model space (x = -X, y = Z, z = -Y: front is Unity +Z, left is Unity -X)."""
    lamp = {n for n, e in mats.items()
            if e.get("role") == "light" or (any(w in n.lower() for w in ("light", "lamp", "optic")) and e.get("alphaMode", "OPAQUE") != "BLEND")}
    if not lamp:
        return {}
    L = mx.y - mn.y
    acc = {k: [Vector((0, 0, 0)), 0.0] for k in ("FL", "FR", "RL", "RR")}
    for ob in meshes():
        if ob.name.startswith(("UBX_", "UCX_", "WheelMesh", "CaliperMesh", "Body_LOD1", "Body_LOD2")):
            continue
        idx = {i for i, s in enumerate(ob.material_slots) if s.material and s.material.name in lamp}
        if not idx:
            continue
        mw = ob.matrix_world
        for p in ob.data.polygons:
            if p.material_index not in idx:
                continue
            c = mw @ p.center
            front = c.y < mn.y + 0.22 * L
            rear = c.y > mx.y - 0.22 * L
            if not (front or rear) or abs(c.x) < 0.15:
                continue
            k = ("F" if front else "R") + ("L" if c.x > 0 else "R")
            acc[k][0] += c * p.area
            acc[k][1] += p.area
    out = {}
    for k, (s, a) in acc.items():
        if a > 1e-5:
            c = s / a
            out[k] = [round(-c.x, 4), round(c.z, 4), round(-c.y, 4)]
    return out


# ------------------------------------------------------------------------------------------------ preview
def render_preview(path, mn, mx):
    scn = bpy.context.scene
    for ob in scn.objects:
        if ob.name.startswith(("UBX_", "UCX_", "Body_LOD1", "Body_LOD2")):
            ob.hide_render = True
    for eng in ("BLENDER_EEVEE_NEXT", "BLENDER_EEVEE", "CYCLES"):
        try:
            scn.render.engine = eng
            break
        except TypeError:
            continue
    L = mx.y - mn.y
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    scn.collection.objects.link(cam)
    # front-left three-quarter: the front is at -Y, the left side at -X
    cam.location = (-L * 1.05, -L * 1.25, L * 0.42)
    d = Vector((0, 0, (mx.z - mn.z) * 0.4)) - cam.location
    cam.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()
    scn.camera = cam
    for rot, energy in (((math.radians(50), 0, math.radians(30)), 3.5), ((math.radians(60), 0, math.radians(200)), 1.5)):
        sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
        sun.data.energy = energy
        sun.rotation_euler = rot
        scn.collection.objects.link(sun)
    world = bpy.data.worlds.new("w")
    world.use_nodes = True
    bg = next(n for n in world.node_tree.nodes if n.type == "BACKGROUND")
    bg.inputs[0].default_value = (0.55, 0.6, 0.68, 1)
    bg.inputs[1].default_value = 0.8
    scn.world = world
    scn.render.resolution_x, scn.render.resolution_y = 960, 540
    scn.render.filepath = path
    bpy.ops.render.render(write_still=True)
    return cam


def render_check_views(cam, base, mn, mx):
    """Close views for the brand-mark check of rebadged cars: straight front, straight rear, front-left wheel."""
    scn = bpy.context.scene
    L, H = mx.y - mn.y, mx.z - mn.z
    views = {
        "front": (Vector((0, mn.y - L * 0.55, H * 0.75)), Vector((0, mn.y, H * 0.5))),
        "rear": (Vector((0, mx.y + L * 0.55, H * 0.75)), Vector((0, mx.y, H * 0.5))),
        "wheel": (Vector((mx.x + 1.3, mn.y + L * 0.2, 0.45)), Vector((mx.x - 0.1, mn.y + L * 0.2, 0.35))),
    }
    out = []
    for name, (pos, target) in views.items():
        cam.location = pos
        cam.rotation_euler = (target - pos).to_track_quat("-Z", "Y").to_euler()
        scn.render.filepath = base + "_" + name + ".png"
        bpy.ops.render.render(write_still=True)
        out.append(scn.render.filepath.replace("\\", "/"))
    return out


# ------------------------------------------------------------------------------------------------ main
def main():
    o = args()
    src, cid = o["src"].replace("\\", "/"), o["id"]
    lic = licence_gate(src)
    out = os.path.join(o["out"], cid)
    os.makedirs(out, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    work = os.path.join(WORK, cid)
    folder = extract(src, work) if src.lower().endswith(".zip") else os.path.dirname(src)
    model_path = find_model(folder) if src.lower().endswith(".zip") else src
    import_model(model_path)
    g = load_gltf_json(model_path)
    report = {"id": cid, "name": o["name"] or cid, "source": src, "licence": lic, "part": o["part"]}
    bake_hierarchy()
    if o["part"]:
        keep_part(o["part"])
    report["stripped"] = strip_parts(o["strip"].split(",") if o["strip"] else [])
    k, front = orient_and_scale(float(o["length"]), o["flip"], o["forcelen"] == "1")
    report["scale_applied"] = k
    report["front"] = front
    if o["stripbox"]:
        # Remove small parts (mascots, emblems) by position: boxes in metres, car space after orientation
        # (front = -Y, left = +X, z up from the ground). A part goes only if its whole bounding box is inside.
        boxes = json.loads(o["stripbox"])
        gone = []
        for ob in split_loose(meshes()):
            if ob.name not in bpy.context.scene.objects:
                continue
            a, b = world_bbox([ob])
            for x0, y0, z0, x1, y1, z1 in boxes:
                if a.x >= x0 and a.y >= y0 and a.z >= z0 and b.x <= x1 and b.y <= y1 and b.z <= z1:
                    gone.append(ob.name)
                    bpy.data.objects.remove(ob, do_unlink=True)
                    break
        report["stripbox_removed"] = len(gone)
    if o["stripfaces"]:
        # Face-level removal for emblems welded into a bigger mesh: faces whose centre is inside the box and whose
        # material name contains one of the given words (so body panels around the emblem stay intact).
        removed = 0
        for spec in json.loads(o["stripfaces"]):
            x0, y0, z0, x1, y1, z1 = spec["box"]
            words = [w.lower() for w in spec.get("mats", [])]
            for ob in meshes():
                slots = [i for i, s in enumerate(ob.material_slots) if s.material and (not words or any(w in s.material.name.lower() for w in words))]
                if not slots:
                    continue
                bm = bmesh.new()
                bm.from_mesh(ob.data)
                mw = ob.matrix_world
                dead = []
                for f in bm.faces:
                    if f.material_index not in slots:
                        continue
                    c = mw @ f.calc_center_median()
                    if x0 <= c.x <= x1 and y0 <= c.y <= y1 and z0 <= c.z <= z1:
                        dead.append(f)
                if dead:
                    bmesh.ops.delete(bm, geom=dead, context="FACES")
                    bm.to_mesh(ob.data)
                    removed += len(dead)
                bm.free()
        report["stripfaces_removed"] = removed
    groups, calipers, hubs = find_wheels()
    ok = len(hubs) == 4 and all(groups[g_] for g_ in groups)
    report["wheels_ok"] = ok
    report["hubs"] = {}
    wheel_objs = set()
    if ok:
        for key in ("FL", "FR", "RL", "RR"):
            h = hubs[key]
            c = h["centre"]
            parts = groups[key]
            wt = sum(tri_count(p) for p in parts)
            if wt > 9000:
                for p in parts:
                    decimate(p, 9000 / wt)
            wheel = join(parts, "WheelMesh_" + key)
            e = bpy.data.objects.new("Wheel_" + key, None)
            e.empty_display_type = "SPHERE"
            e.empty_display_size = h["radius"]
            bpy.context.scene.collection.objects.link(e)
            e.location = c
            bpy.context.view_layer.update()   # the empty's matrix_world must be current before parenting
            mw = wheel.matrix_world.copy()
            wheel.parent = e
            wheel.matrix_world = mw
            wheel_objs.add(wheel)
            if calipers[key]:
                cal = join(calipers[key], "CaliperMesh_" + key)
                ce = bpy.data.objects.new("Caliper_" + key, None)
                bpy.context.scene.collection.objects.link(ce)
                ce.location = c
                bpy.context.view_layer.update()
                mw = cal.matrix_world.copy()
                cal.parent = ce
                cal.matrix_world = mw
                wheel_objs.add(cal)
            report["hubs"][key] = {"centre": [round(c.x, 4), round(c.y, 4), round(c.z, 4)], "radius": round(h["radius"], 4),
                                   "width": round(h["width"], 4), "tris": tri_count(wheel)}
    else:
        report["hubs_found"] = {k_: [round(v, 3) for v in h["centre"]] + [round(h["radius"], 3)] for k_, h in hubs.items()}
        report["wheel_groups"] = {k_: len(v) for k_, v in groups.items()}
    body = [x for x in meshes() if x not in wheel_objs]
    lod0 = join(body, "Body_LOD0")
    tris0 = tri_count(lod0)
    limit = int(o["lod0"])
    if tris0 > limit:
        decimate(lod0, limit / tris0)
    t0 = tri_count(lod0)
    lod1 = decimated_copy(lod0, "Body_LOD1", min(0.45, 40000 / max(1, t0)))
    lod2 = decimated_copy(lod0, "Body_LOD2", min(0.15, 10000 / max(1, t0)))
    # collision
    mn, mx = world_bbox([lod0])
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co = Vector(((mn.x + mx.x) / 2 + v.co.x * (mx.x - mn.x) * 0.94, (mn.y + mx.y) / 2 + v.co.y * (mx.y - mn.y) * 0.96,
                       mn.z + 0.22 + (v.co.z + 0.5) * max(0.3, (mx.z - mn.z) * 0.55)))
    me = bpy.data.meshes.new("UBX_Chassis")
    bm.to_mesh(me)
    bm.free()
    bpy.context.scene.collection.objects.link(bpy.data.objects.new("UBX_Chassis", me))
    hullsrc = decimated_copy(lod0, "hull_src", min(1.0, 2500 / max(1, t0)))
    # keep the hull above the wheel-contact zone so it never touches the road before the tyres
    for v in hullsrc.data.vertices:
        if v.co.z < mn.z + 0.25:
            v.co.z = mn.z + 0.25
    ucx, hull_faces = convex_hull_object(hullsrc, "UCX_Body")
    bpy.data.objects.remove(hullsrc, do_unlink=True)
    allmn, allmx = world_bbox([lod0] + list(wheel_objs))
    report["dimensions"] = [round(allmx.x - allmn.x, 3), round(allmx.z - allmn.z, 3), round(allmx.y - allmn.y, 3)]
    if ok:
        hb = report["hubs"]
        report["wheelbase"] = round(abs(hb["FL"]["centre"][1] - hb["RL"]["centre"][1]), 4)
        report["track_front"] = round(abs(hb["FL"]["centre"][0] - hb["FR"]["centre"][0]), 4)
        report["track_rear"] = round(abs(hb["RL"]["centre"][0] - hb["RR"]["centre"][0]), 4)
        report["radius_front"] = hb["FL"]["radius"]
        report["radius_rear"] = hb["RL"]["radius"]
        report["width_front"] = hb["FL"]["width"]
        report["width_rear"] = hb["RL"]["width"]
    report["tris"] = {"Body_LOD0": tri_count(lod0), "Body_LOD1": tri_count(lod1), "Body_LOD2": tri_count(lod2),
                      "wheels": sum(tri_count(x) for x in wheel_objs), "UCX_Body_faces": hull_faces}
    used = sorted({s.material.name for ob in meshes() for s in ob.material_slots if s.material})
    paint_words = [w.strip().lower() for w in (o["paint"] or "bodymat,body_one,car_body,paint").split(",") if w.strip()]
    mats = export_materials(g, model_path, used, out, paint_words)
    if o["fix"]:
        report["livery_fix"] = {o["fix"]: fix_livery(out, o["fix"])}
    if o["blank"]:
        report["blanked"] = blank_marks(out, json.loads(o["blank"]))
    report["lamps"] = lamp_anchors(mats, allmn, allmx)
    with open(os.path.join(out, "materials.json"), "w") as f:
        json.dump(mats, f, indent=1)
    report["materials"] = {k_: v.get("role") for k_, v in mats.items()}
    fbx = os.path.join(out, cid + ".fbx")
    select_only([x for x in bpy.context.scene.objects if x.type in ("MESH", "EMPTY")])
    bpy.ops.export_scene.fbx(
        filepath=fbx, use_selection=True, object_types={"EMPTY", "MESH"},
        apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS", global_scale=1.0,
        axis_forward="-Z", axis_up="Y", bake_space_transform=True,
        mesh_smooth_type="FACE", use_mesh_modifiers=True, add_leaf_bones=False,
        use_tspace=False, path_mode="STRIP", embed_textures=False, bake_anim=False)
    report["fbx"] = fbx.replace("\\", "/")
    # Renders must show what ships: point Blender's images at the exported (blanked / converted) copies.
    tex_out = os.path.join(out, "textures")
    for im in bpy.data.images:
        fn = os.path.basename(bpy.path.abspath(im.filepath)) if im.filepath else ""
        if fn and os.path.exists(os.path.join(tex_out, fn)):
            im.filepath = os.path.join(tex_out, fn)
            im.reload()
    try:
        prev = os.path.join(out, cid + "_preview.png")
        cam = render_preview(prev, allmn, allmx)
        report["preview"] = prev.replace("\\", "/")
        if o["checkviews"] == "1":
            report["check_views"] = render_check_views(cam, os.path.join(out, cid), allmn, allmx)
    except Exception as ex:  # preview is optional
        report["preview_error"] = str(ex)
    with open(os.path.join(out, "process_report.json"), "w") as f:
        json.dump(report, f, indent=1)
    print("PROCESS_REPORT " + json.dumps({k_: report[k_] for k_ in ("id", "wheels_ok", "front", "dimensions", "tris") if k_ in report}))


main()

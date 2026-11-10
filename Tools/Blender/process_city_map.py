"""
VEYRA CITY - card B2: ingest a LICENSED downloaded city map or kit (segment, chunk/kitbash, LOD, collision).

  blender -b --factory-startup -P Tools/Blender/process_city_map.py -- --input ArtSource/Downloads/<file> --mode kitbash|aligned
          [--out <dir>] [--report Logs/ingest_<name>.json] [--scale <f>] [--chunk 250] [--max-objects 40] [--max-unclassified 10]
  blender -b --factory-startup -P Tools/Blender/process_city_map.py -- --make-test Logs/B2_test   (synthetic ALIGNED test map)

Gates (CLAUDE.md, card B1): the input must be ACCEPTED in Logs/B1/licence_gate.json (or carry an accepted .license.txt /
.licence.json sidecar); nothing under _Quarantine and nothing that looks ripped from a game is ever processed. World-positioned
export needs the S4 axis round-trip test (Tools/Blender/S4_PASSED).

Pipeline (card B2):
 1. Import by extension (zip safe-extract; glb/gltf, fbx, obj, blend append); apply all transforms.
 2. Normalise: --scale (or the triage estimate from Logs/triage.json), Z up, non-mesh objects removed, vertices merged by 1 mm,
    normals recalculated, loose and zero-area geometry removed. ALIGNED: Blender X/Y = Veyra x - 4000 / y - 3000, checked
    against the Veyra heightmap at 50 random road points (stop if the max offset > 1.5 m).
 3. Segmentation into Road_Surface, Sidewalk, Building, Prop, Terrain, Vegetation: a) name / material rules
    (segment_rules.json), b) otherwise per material and loose part by geometry; c) unclassified triangles > --max-unclassified %
    -> stop and render the unclassified parts in red (Logs/ingest_<name>_unclassified.png).
 4. ALIGNED: 250 m grid in Veyra coordinates; continuous surfaces bisected exactly on chunk lines (shared border vertices), whole
    buildings / props / trees assigned by base centre; Chunk_<Cell>_<i><j>. KITBASH: no world chunks; every building / prop is
    exported on its own with a base-centre pivot to Assets/_Veyra/Art/Models/Kitbash/<source>/VC_KB_<source>_<type>_<n>.fbx.
 5. Render meshes merged by category + material (<= 12 materials per chunk), LOD0 / LOD1 ~40 % / LOD2 ~10 % (roads: planar
    dissolve, edges kept); collision never rendered: COL_Road_<chunk> (road + sidewalk, markings removed, planar), COL_Ground_<chunk>,
    buildings UBX_ (box fit error < 0.5 m) or UCX_ (<= 255 triangles), props UBX_ (or none when tiny). Budgets: LOD0 <= 300k
    triangles per chunk (warn), <= 500k (fail). Pivots: chunk south-west corner at height 0; kitbash pieces at base centre.
 6. Export with the S4 presets (VC_Unity_World for chunks, VC_Unity_Prop for kitbash pieces); textures copied to
    Assets/_Veyra/Art/Textures/Imported/<source>/; material hints from material_map.json; manifest (chunks_manifest.json /
    kitbash_manifest.json).
 7. Report JSON: input vs output triangles per LOD, chunk / piece count, max chunk triangles, unclassified %, border cracks,
    warnings, time.
"""
import json
import math
import os
import random
import re
import shutil
import sys
import time
import zipfile

import bmesh
import bpy
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import vc_tools as vc  # noqa: E402

ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
RIPPED = re.compile(r"gta|grand.?theft|liberty.?city|vice.?city|san.?andreas|los.?santos|cyberpunk.?2077|night.?city|rockstar|cd.?projekt|need.?for.?speed|forza", re.I)
CATEGORIES = ("Road_Surface", "Sidewalk", "Building", "Prop", "Terrain", "Vegetation")
COLOURS = {"Road_Surface": (0.15, 0.15, 0.17, 1), "Sidewalk": (0.75, 0.75, 0.72, 1), "Building": (0.9, 0.55, 0.25, 1),
           "Prop": (0.2, 0.55, 0.95, 1), "Terrain": (0.35, 0.6, 0.25, 1), "Vegetation": (0.1, 0.8, 0.2, 1), None: (1, 0, 0, 1)}
HEIGHTMAP = os.path.join(ROOT, "ArtSource", "VeyraData", "terrain", "heightmap_world_4097x3073_16bit.png")
LOG = []
MAX_FOOT = 120.0  # m: widest plausible single kit building


def log(msg):
    LOG.append(msg)
    print("[B2] " + msg, flush=True)


# ---------------------------------------------------------------------------------------------------------------- arguments

def options():
    a = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    o = {"input": None, "mode": "kitbash", "out": None, "report": None, "scale": None, "chunk": "250", "max-objects": "40",
         "max-unclassified": "10", "make-test": None, "skip-height-check": False, "no-export": False}
    i = 0
    while i < len(a):
        k = a[i].lstrip("-")
        if k in ("skip-height-check", "no-export"):
            o[k] = True
        elif k in o and i + 1 < len(a):
            o[k] = a[i + 1]
            i += 1
        i += 1
    return o


def rel(p):
    return os.path.relpath(os.path.abspath(p), ROOT).replace("\\", "/")


# ---------------------------------------------------------------------------------------------------------------- licence

def licence(path):
    rp = rel(path)
    if "/_Quarantine/" in "/" + rp or RIPPED.search(os.path.basename(path)):
        raise SystemExit(f"REFUSED: {rp} is quarantined or looks ripped from a game (card B1).")
    gate = os.path.join(ROOT, "Logs", "B1", "licence_gate.json")
    if os.path.exists(gate):
        with open(gate, encoding="utf-8") as f:
            for e in json.load(f)["items"]:
                if e["file"] == rp:
                    if e["decision"] != "ACCEPT":
                        raise SystemExit(f"REFUSED: {rp} is {e['decision']} ({e.get('reason')}).")
                    return {"licence": e.get("licence", ""), "author": e.get("author", ""), "url": e.get("url", ""), "credit": e.get("credit", ""), "id": rp}
    for sc in (path + ".license.txt", path + ".licence.json", os.path.splitext(path)[0] + ".licence.json"):
        if os.path.exists(sc):
            with open(sc, encoding="utf-8") as f:
                t = f.read()
            lic = json.loads(t).get("licence", "") if sc.endswith(".json") else (re.search(r"^Licence:\s*(.*)$", t, re.M) or [None, ""])[1]
            u = (lic or "").upper()
            if ("CC0" in u or "CC-BY" in u) and not re.search(r"\b(NC|ND|SA)\b", u.replace("-", " ")):
                return {"licence": lic, "author": "", "url": "", "credit": "", "id": rp}
    raise SystemExit(f"REFUSED: {rp} has no accepted licence record (run Tools/Assets/licence_gate.py, card B1).")


# ---------------------------------------------------------------------------------------------------------------- import / normalise

def safe_extract(zpath, dest):
    os.makedirs(dest, exist_ok=True)
    root = os.path.realpath(dest)
    with zipfile.ZipFile(zpath) as z:
        for info in z.infolist():
            name = info.filename.replace("\\", "/")
            if name.startswith("/") or ".." in name.split("/") or ":" in name:
                continue
            target = os.path.realpath(os.path.join(dest, name))
            if not target.startswith(root):
                continue
            if info.is_dir():
                os.makedirs(target, exist_ok=True)
                continue
            os.makedirs(os.path.dirname(target), exist_ok=True)
            with z.open(info) as src, open(target, "wb") as dst:
                shutil.copyfileobj(src, dst, 1 << 20)


def import_any(path, work):
    low = path.lower()
    if low.endswith(".zip"):
        safe_extract(path, work)
        cands = []
        for a, _, fs in os.walk(work):
            for f in fs:
                if f.lower().endswith((".glb", ".gltf", ".fbx", ".obj", ".blend")):
                    cands.append(os.path.join(a, f))
        order = (".glb", ".gltf", ".fbx", ".obj", ".blend")
        cands.sort(key=lambda p: (order.index(os.path.splitext(p)[1].lower()), len(p)))
        if not cands:
            raise SystemExit("no model in the zip")
        return import_any(cands[0], work)
    if low.endswith((".glb", ".gltf")):
        bpy.ops.import_scene.gltf(filepath=path)
    elif low.endswith(".fbx"):
        bpy.ops.import_scene.fbx(filepath=path)
    elif low.endswith(".obj"):
        bpy.ops.wm.obj_import(filepath=path)
    elif low.endswith(".blend"):
        with bpy.data.libraries.load(path, link=False) as (df, dt):
            dt.objects = list(df.objects)
        for ob in dt.objects:
            if ob is not None:
                bpy.context.scene.collection.objects.link(ob)
    else:
        raise SystemExit("unsupported " + path)
    return path


def meshes():
    return [o for o in bpy.context.scene.objects if o.type == "MESH"]


def tris(obj):
    return sum(max(0, len(p.vertices) - 2) for p in obj.data.polygons)


def normalise(scale, offset=Vector((0, 0, 0))):
    for o in list(bpy.context.scene.objects):
        if o.parent is not None:
            mw = o.matrix_world.copy()
            o.parent = None
            o.matrix_world = mw
    for o in list(bpy.context.scene.objects):
        if o.type != "MESH":
            bpy.data.objects.remove(o, do_unlink=True)
    xf = Matrix.Translation(offset) @ Matrix.Scale(scale, 4)
    seen = set()
    for o in meshes():
        if o.data.users > 1 or o.data.name in seen:
            o.data = o.data.copy()
        seen.add(o.data.name)
        o.data.transform(xf @ o.matrix_world)
        o.matrix_world = Matrix.Identity(4)
        bm = bmesh.new()
        bm.from_mesh(o.data)
        bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.001)
        bmesh.ops.dissolve_degenerate(bm, edges=bm.edges, dist=1e-5)
        loose = [v for v in bm.verts if not v.link_faces]
        if loose:
            bmesh.ops.delete(bm, geom=loose, context="VERTS")
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        flipped = orient_open_surfaces_up(bm)
        if flipped:
            log(f"normals: {o.name}: {flipped} open surface island(s) faced down, flipped up")
        bm.to_mesh(o.data)
        bm.free()
        if not o.data.polygons:
            bpy.data.objects.remove(o, do_unlink=True)


def orient_open_surfaces_up(bm):
    """recalc_face_normals makes closed shells point outward, but an open surface (a road, sidewalk or ground sheet) has no
    outside and can come out facing down: invisible from above and missed by the wheel raycasts, which ignore back faces
    (B3 found this on the B2 test map). Flip every open island (one with boundary edges) whose area-weighted normal points
    down while it is mostly horizontal. Closed shells (buildings) are left as recalc decided. Returns the islands flipped."""
    bm.faces.index_update()  # remove_doubles / delete leave stale indices
    seen = set()
    flipped = 0
    for f0 in bm.faces:
        if f0.index in seen:
            continue
        island, stack = [], [f0]
        seen.add(f0.index)
        while stack:
            f = stack.pop()
            island.append(f)
            for e in f.edges:
                for g in e.link_faces:
                    if g.index not in seen:
                        seen.add(g.index)
                        stack.append(g)
        if not any(e.is_boundary for f in island for e in f.edges):
            continue
        n = Vector((0, 0, 0))
        for f in island:
            n += f.normal * f.calc_area()
        if n.length > 1e-9 and n.z < 0 and abs(n.z) > 0.5 * n.length:
            bmesh.ops.reverse_faces(bm, faces=island)
            flipped += 1
    return flipped


# ---------------------------------------------------------------------------------------------------------------- segmentation

def load_rules():
    with open(os.path.join(HERE, "segment_rules.json"), encoding="utf-8") as f:
        return [(c, re.compile(p, re.I)) for c, p in json.load(f)["rules"]]


def name_category(rules, text):
    for c, rx in rules:
        if rx.search(text):
            return c
    return None


def material_names(o):
    return " ".join(m.name for m in o.data.materials if m)


def split(o, how):
    """Separate o by MATERIAL or LOOSE parts (Blender's own operator); returns the resulting objects."""
    if len(o.data.polygons) == 0:
        return [o]
    before = set(bpy.context.scene.objects)
    bpy.ops.object.select_all(action="DESELECT")
    o.select_set(True)
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.separate(type=how)
    bpy.ops.object.mode_set(mode="OBJECT")
    return [o] + [x for x in bpy.context.scene.objects if x not in before]


def bounds(objs):
    lo = Vector((math.inf,) * 3)
    hi = Vector((-math.inf,) * 3)
    for o in objs:
        for v in o.data.vertices:
            c = o.matrix_world @ v.co
            lo = Vector(map(min, lo, c))
            hi = Vector(map(max, hi, c))
    return lo, hi


def geometry_category(o, ground):
    lo, hi = bounds([o])
    size = hi - lo
    area = sum(p.area for p in o.data.polygons) or 1e-6
    nz = sum(abs(p.normal.z) * p.area for p in o.data.polygons) / area
    h = lo.z - ground
    diag = size.length
    if nz > 0.95 and size.z < 0.6:
        if h < 0.35 and min(size.x, size.y) > 60.0:
            return "Terrain"
        if h < 0.35 and min(size.x, size.y) >= 5.0:
            return "Road_Surface"
        if 0.08 <= h <= 0.3:
            return "Sidewalk"
        if h > 3.0:
            return "Building"
        return "Prop" if diag < 8.0 else None
    if size.z > 3.0:
        return "Building"
    if diag < 8.0:
        return "Prop"
    return None


def segment(max_unclassified, name, out_png):
    rules = load_rules()
    objs = meshes()
    zs = sorted((o.matrix_world @ o.data.vertices[i].co).z for o in objs for i in range(0, len(o.data.vertices), max(1, len(o.data.vertices) // 200)))
    ground = zs[int(len(zs) * 0.02)] if zs else 0.0
    cats = {}
    for o in objs:
        c = name_category(rules, o.name + " " + material_names(o))
        if c:
            cats[o] = c
            continue
        parts = split(o, "MATERIAL") if len(o.data.materials) > 1 else [o]
        for p in parts:
            c = name_category(rules, p.name + " " + material_names(p))
            if c:
                cats[p] = c
                continue
            pieces = split(p, "LOOSE") if len(p.data.polygons) < 400000 else [p]
            for q in pieces:
                cats[q] = geometry_category(q, ground)
    total = sum(tris(o) for o in cats)
    unc = sum(tris(o) for o, c in cats.items() if c is None)
    share = 100.0 * unc / max(1, total)
    log(f"segmentation: {len(cats)} parts, {total:,} triangles, unclassified {share:.1f} % (ground at {ground:.2f} m)")
    if share > max_unclassified:
        render_categories(cats, out_png, highlight_unclassified=True)
        raise SystemExit(f"STOP: {share:.1f} % of the triangles are unclassified (> {max_unclassified} %); see {out_png}")
    return cats, ground, share


# ---------------------------------------------------------------------------------------------------------------- materials, LODs, collision

def material_map():
    with open(os.path.join(HERE, "material_map.json"), encoding="utf-8") as f:
        j = json.load(f)
    return [(re.compile(r["pattern"], re.I), r["key"], r.get("category")) for r in j["rules"]], j["category_defaults"]


MAT_RULES, MAT_DEFAULTS = material_map()


def veyra_key(mat_name, cat):
    for rx, key, only in MAT_RULES:
        if (only is None or only == cat) and rx.search(mat_name or ""):
            return key
    return MAT_DEFAULTS.get(cat, "FacadeConcrete")


def join(objs, name):
    objs = [o for o in objs if o is not None]
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    if len(objs) > 1:
        bpy.ops.object.join()
    ob = bpy.context.view_layer.objects.active
    ob.name = name
    ob.data.name = name
    return ob


GRID = 250.0


def on_grid(co, size=None):
    size = size or GRID
    return any(abs(co[a] / size - round(co[a] / size)) < 1e-6 for a in (0, 1))


def dissolve_keep_borders(bm, angle):
    """Planar dissolve that never removes a vertex lying on a chunk line, so neighbouring chunks keep identical borders."""
    inner = [v for v in bm.verts if not on_grid(v.co)]
    edges = [e for e in bm.edges if not (on_grid(e.verts[0].co) and on_grid(e.verts[1].co))]
    bmesh.ops.dissolve_limit(bm, angle_limit=angle, verts=inner, edges=edges)


def make_lods(obj, ratios, planar):
    """LODs via vc_tools; flat surfaces (roads) use a planar dissolve instead of collapse so their edges survive."""
    if not planar:
        return vc.vc_make_lods(obj, ratios)
    root, lods = vc.vc_make_lods(obj, (1.0,) + tuple(1.0 for _ in ratios[1:]))
    for i, lod in enumerate(lods[1:], start=1):
        bm = bmesh.new()
        bm.from_mesh(lod.data)
        dissolve_keep_borders(bm, math.radians(1.0 + 3.0 * i))
        bmesh.ops.triangulate(bm, faces=bm.faces)
        bm.to_mesh(lod.data)
        bm.free()
    return root, lods


def new_object(name, bm):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    return ob


def collider_for(obj, base_name, allow_none=False):
    """UBX_ when the convex hull fills the box (mean gap < 0.5 m), else UCX_ (convex hull, <= 255 triangles)."""
    lo, hi = bounds([obj])
    size = hi - lo
    if allow_none and size.length < 1.0:
        return []
    bm = bmesh.new()
    for v in obj.data.vertices:
        bm.verts.new(obj.matrix_world @ v.co)
    hull = bmesh.ops.convex_hull(bm, input=bm.verts)
    drop = list({g for g in hull["geom_interior"] + hull["geom_unused"] if isinstance(g, bmesh.types.BMVert)})
    if drop:
        bmesh.ops.delete(bm, geom=drop, context="VERTS")
    hull_vol = abs(bm.calc_volume()) if bm.faces else 0.0
    box_vol = max(size.x, 1e-3) * max(size.y, 1e-3) * max(size.z, 1e-3)
    box_area = 2 * (size.x * size.y + size.y * size.z + size.x * size.z) or 1e-6
    gap = (box_vol - hull_vol) / box_area
    if gap < 0.5 or len(bm.faces) == 0:
        bm.free()
        b = bmesh.new()
        bmesh.ops.create_cube(b, size=1.0)
        for v in b.verts:
            v.co = Vector(((lo.x + hi.x) / 2 + v.co.x * size.x, (lo.y + hi.y) / 2 + v.co.y * size.y, (lo.z + hi.z) / 2 + v.co.z * size.z))
        return [new_object(f"UBX_{base_name}", b)]
    bmesh.ops.triangulate(bm, faces=bm.faces)
    ob = new_object(f"UCX_{base_name}_0", bm)
    if len(ob.data.polygons) > 255:
        mod = ob.modifiers.new("hull", "DECIMATE")
        mod.ratio = 240.0 / len(ob.data.polygons)
        with bpy.context.temp_override(object=ob, active_object=ob, selected_objects=[ob]):
            bpy.ops.object.modifier_apply(modifier=mod.name)
    return [ob]


def hide_collision(objs):
    for o in objs:
        o.display_type = "WIRE"
        o.hide_render = True


# ---------------------------------------------------------------------------------------------------------------- KITBASH

def kitbash(cats, source, out_dir, max_objects, lic):
    """Clusters building parts by touching footprints (with their props), exports each building / prop separately."""
    out_dir = out_dir or os.path.join(ROOT, "Assets", "_Veyra", "Art", "Models", "Kitbash", source)
    os.makedirs(out_dir, exist_ok=True)
    parts = [(o, c) for o, c in cats.items() if c in ("Building", "Prop", "Vegetation")]
    boxes = {o: bounds([o]) for o, _ in parts}
    # A "building" part wider than MAX_FOOT is a district-sized merged mesh (hilly ground, a block plinth, a whole skyline
    # in one object). It is not a kit piece, and its footprint would chain every building of the map into one cluster.
    district = [o for o, c in parts if c == "Building" and max(boxes[o][1].x - boxes[o][0].x, boxes[o][1].y - boxes[o][0].y) > MAX_FOOT]
    if district:
        log(f"kitbash: {len(district)} building part(s) wider than {MAX_FOOT:.0f} m skipped as district meshes "
            f"({sum(tris(o) for o in district):,} triangles)")
        parts = [(o, c) for o, c in parts if o not in district]
    parent = {o: o for o, _ in parts}

    def find(x):
        while parent[x] is not x:
            parent[x] = parent[parent[x]]
            x = parent[x]
        return x

    blds = [o for o, c in parts if c == "Building"]
    blds.sort(key=lambda o: boxes[o][0].x)
    # Parts of one building overlap in plan (walls, windows, roof); neighbouring buildings only touch along a party wall,
    # so join parts whose footprints overlap by more than 30 % of the smaller footprint.
    for i, a in enumerate(blds):
        alo, ahi = boxes[a]
        aa = max(0.01, (ahi.x - alo.x) * (ahi.y - alo.y))
        for b in blds[i + 1:]:
            blo, bhi = boxes[b]
            if blo.x > ahi.x:
                break
            ox = min(ahi.x, bhi.x) - max(alo.x, blo.x)
            oy = min(ahi.y, bhi.y) - max(alo.y, blo.y)
            if ox > 0.3 and oy > 0.3 and ox * oy > 0.3 * min(aa, max(0.01, (bhi.x - blo.x) * (bhi.y - blo.y))):
                parent[find(b)] = find(a)
    roots = {}
    for o in blds:
        roots.setdefault(find(o), []).append(o)

    def fp(ms):
        return (min(boxes[m][0].x for m in ms), min(boxes[m][0].y for m in ms), max(boxes[m][1].x for m in ms), max(boxes[m][1].y for m in ms))

    # Thin parts (window panes, mullions, cornices, signs on the facade) fail the 30 % overlap test because their plan
    # footprint is a sliver. Fold every cluster whose footprint lies >= 70 % inside a larger cluster's footprint (grown by
    # 0.5 m) into that cluster, largest clusters first, using a 10 m spatial hash.
    footprints = {r: fp(ms) for r, ms in roots.items()}
    area = {r: max(0.01, (f[2] - f[0]) * (f[3] - f[1])) for r, f in footprints.items()}
    order = sorted(roots, key=lambda r: -area[r])
    grid = {}
    absorbed = 0
    for r in order:
        x0, y0, x1, y1 = footprints[r]
        host = None
        best = 0.0
        for gx in range(int(math.floor(x0 / 10)), int(math.floor(x1 / 10)) + 1):
            for gy in range(int(math.floor(y0 / 10)), int(math.floor(y1 / 10)) + 1):
                for h in grid.get((gx, gy), ()):
                    hx0, hy0, hx1, hy1 = footprints[h]
                    ox = min(x1, hx1 + 0.5) - max(x0, hx0 - 0.5)
                    oy = min(y1, hy1 + 0.5) - max(y0, hy0 - 0.5)
                    if ox > 0 and oy > 0 and area[h] > area[r]:
                        share = ox * oy / area[r]
                        if share > best:
                            host, best = h, share
        if host is not None and best >= 0.7:
            roots[host].extend(roots.pop(r))
            absorbed += 1
            continue
        for gx in range(int(math.floor(x0 / 10)), int(math.floor(x1 / 10)) + 1):
            for gy in range(int(math.floor(y0 / 10)), int(math.floor(y1 / 10)) + 1):
                grid.setdefault((gx, gy), []).append(r)
    if absorbed:
        log(f"kitbash: {absorbed} thin/contained part clusters folded into their buildings")
    # props inside a building footprint belong to that building (balconies, AC units on the facade)
    loose_props = []
    footprints = {r: fp(ms) for r, ms in roots.items()}
    for o, c in parts:
        if c == "Building":
            continue
        plo, phi = boxes[o]
        centre = (plo + phi) / 2
        home = None
        for r, (x0, y0, x1, y1) in footprints.items():
            if x0 - 0.5 <= centre.x <= x1 + 0.5 and y0 - 0.5 <= centre.y <= y1 + 0.5:
                home = r
                break
        if home is not None:
            roots[home].append(o)
        else:
            loose_props.append((o, c))
    # Biggest real buildings first (plan area x height), so the --max-objects cap keeps whole buildings, not fragments.
    def volume(ms):
        x0, y0, x1, y1 = fp(ms)
        return (x1 - x0) * (y1 - y0) * (max(boxes[m][1].z for m in ms) - min(boxes[m][0].z for m in ms))

    buildings = sorted(roots.values(), key=lambda ms: -volume(ms))
    props = sorted(loose_props, key=lambda oc: -tris(oc[0]))
    log(f"kitbash: {len(buildings)} buildings, {len(props)} props/vegetation (exporting up to {max_objects} of each)")
    manifest = {"source": source, "licence": lic, "pieces": []}
    images = set()
    warnings = []
    for kind, groups in (("Building", [ms for ms in buildings[:max_objects]]), ("Prop", [[o] for o, _ in props[:max_objects]])):
        for n, members in enumerate(groups):
            name = f"VC_KB_{source}_{kind}_{n:03d}"
            for m in members:
                for mat in m.data.materials:
                    if mat and mat.node_tree:
                        for node in mat.node_tree.nodes:
                            if node.type == "TEX_IMAGE" and node.image:
                                images.add(node.image)
            mats = sorted({mat.name for m in members for mat in m.data.materials if mat})
            obj = join(members, name)
            vc.vc_set_pivot(obj, "BASE_CENTER")
            lo, hi = bounds([obj])
            size = hi - lo
            small = size.length < 2.0
            t0 = tris(obj)
            if t0 > 500_000:
                warnings.append(f"{name}: {t0:,} triangles > 500k (FAIL budget), skipped")
                continue
            if t0 > 300_000:
                warnings.append(f"{name}: {t0:,} triangles > 300k (warn)")
            root, lods = vc.vc_make_lods(obj, (1.0, 0.4) if small else (1.0, 0.4, 0.1))
            cols = collider_for(lods[0], name, allow_none=(kind == "Prop"))
            hide_collision(cols)
            for c in cols:
                c.parent = root
                c.matrix_parent_inverse = root.matrix_world.inverted()
            path = os.path.join(out_dir, name + ".fbx")
            vc.vc_export_fbx([root] + lods + cols, path, mode="prop", pivot=root, path_mode="STRIP")
            manifest["pieces"].append({
                "name": name, "type": kind, "file": rel(path),
                "footprint_m": [round(size.x, 2), round(size.y, 2)], "height_m": round(size.z, 2),
                "triangles": [tris(l) for l in lods], "collision": [c.name for c in cols],
                "materials": [{"source": m, "veyra": veyra_key(m, kind)} for m in mats]})
            for o in [root] + lods + cols:
                o.hide_set(True)
    # Deviation from the card text (step 6), decided in B3: kit pieces are remapped to the VeyraMaterialLibrary palette on
    # import (VeyraModelPostprocessor + material_map.json), so the source textures are not used by the game. They stay in
    # the licensed source archive and are only listed here (copying them cost 336 MB of 4K-8K images in Assets).
    files = sorted({os.path.basename(bpy.path.abspath(img.filepath)) for img in images if img.filepath})
    manifest["textures"] = {"source_archive": lic.get("id", ""), "files": files, "copied": 0,
                            "note": "not copied: materials are remapped to the VeyraMaterialLibrary palette (B3)"}
    with open(os.path.join(out_dir, "kitbash_manifest.json"), "w", encoding="utf-8") as f:
        json.dump(manifest, f, indent=1)
    return manifest, warnings


# ---------------------------------------------------------------------------------------------------------------- ALIGNED

def heightmap_check(road_objs, samples=50):
    if not os.path.exists(HEIGHTMAP):
        return {"checked": 0, "note": "heightmap missing"}
    import numpy as np
    img = bpy.data.images.load(HEIGHTMAP, check_existing=True)
    img.colorspace_settings.name = "Non-Color"
    w, h = img.size
    px = np.empty(w * h * 4, dtype=np.float32)
    img.pixels.foreach_get(px)
    lum = px[0::4].reshape(h, w)  # row 0 = bottom (south)
    pts = []
    for o in road_objs:
        for p in o.data.polygons:
            pts.append(o.matrix_world @ p.center)
    random.seed(7)
    pick = random.sample(pts, min(samples, len(pts)))
    offs = []
    for p in pick:
        x, y = p.x + 4000.0, p.y + 3000.0
        col = int(round(x / 8000.0 * (w - 1)))
        row_from_top = int(round((1.0 - y / 6000.0) * (h - 1)))
        if not (0 <= col < w and 0 <= row_from_top < h):
            continue
        v = float(lum[h - 1 - row_from_top, col])
        ground = -60.0 + v * 560.0
        offs.append(abs(p.z - ground))
    return {"checked": len(offs), "mean_m": round(sum(offs) / max(1, len(offs)), 3), "max_m": round(max(offs) if offs else 0.0, 3)}


def chunk_of(x, y, size):
    """Veyra chunk of a Blender (Unity-frame) point: data x = bx + 4000, y = by + 3000."""
    dx, dy = x + 4000.0, y + 3000.0
    col = max(0, min(7, int(dx // 1000)))
    row_s = max(0, min(5, int(dy // 1000)))
    i = max(0, min(3, int((dx - col * 1000) // size)))
    j = max(0, min(3, int((dy - row_s * 1000) // size)))
    cell = f"{'ABCDEFGH'[col]}{6 - row_s}"
    corner = (col * 1000 + i * size - 4000.0, row_s * 1000 + j * size - 3000.0)
    return f"Chunk_{cell}_{i}{j}", cell, corner


def bisect_grid(o, size):
    bm = bmesh.new()
    bm.from_mesh(o.data)
    xs = [v.co.x for v in bm.verts]
    ys = [v.co.y for v in bm.verts]
    for gx in range(int(math.floor(min(xs) / size)) + 1, int(math.ceil(max(xs) / size))):
        geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
        bmesh.ops.bisect_plane(bm, geom=geom, plane_co=(gx * size, 0, 0), plane_no=(1, 0, 0))
    for gy in range(int(math.floor(min(ys) / size)) + 1, int(math.ceil(max(ys) / size))):
        geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
        bmesh.ops.bisect_plane(bm, geom=geom, plane_co=(0, gy * size, 0), plane_no=(0, 1, 0))
    bm.to_mesh(o.data)
    bm.free()


def split_by_chunk(o, size):
    """Faces of a bisected surface -> one object per chunk (border vertices stay identical on both sides)."""
    keys = {}
    for p in o.data.polygons:
        c = p.center
        keys.setdefault(chunk_of(c.x, c.y, size)[0], []).append(p.index)
    out = {}
    for key, idx in keys.items():
        bm = bmesh.new()
        bm.from_mesh(o.data)
        bm.faces.ensure_lookup_table()
        keep = set(idx)
        bmesh.ops.delete(bm, geom=[f for f in bm.faces if f.index not in keep], context="FACES")
        loose = [v for v in bm.verts if not v.link_faces]
        if loose:
            bmesh.ops.delete(bm, geom=loose, context="VERTS")
        part = new_object(f"{o.name}_{key}", bm)
        for m in o.data.materials:
            part.data.materials.append(m)
        out[key] = part
    bpy.data.objects.remove(o, do_unlink=True)
    return out


def border_keys(o, size):
    """Vertices of o lying on a chunk line, as (axis, line, position along it, height) at 1 mm, in world coordinates."""
    ks = set()
    for v in o.data.vertices:
        p = o.matrix_world @ v.co
        for axis in (0, 1):
            g = p[axis] / size
            if abs(g - round(g)) < 1e-5:
                ks.add((axis, round(g), round(p[1 - axis], 3), round(p.z, 3)))
    return ks


def border_cracks(keys, size):
    """A border vertex of one chunk's COL_Road must exist in a neighbour on the same chunk line (no T-junctions / gaps).
    Vertices on the outer edge of the whole map have no neighbour and are not counted."""
    lines = {}
    for k, ks in keys.items():
        for key in ks:
            lines.setdefault((key[0], key[1]), set()).add(k)
    cracks = 0
    for k, ks in keys.items():
        for (axis, line, along, z) in ks:
            if len(lines[(axis, line)]) < 2:
                continue  # map edge: no neighbour on this line
            if not any((axis, line, along, z) in ks2 for k2, ks2 in keys.items() if k2 != k):
                cracks += 1
    return cracks


def aligned(cats, source, out_dir, size, lic, ground, skip_height):
    global GRID
    GRID = size
    out_dir = out_dir or os.path.join(ROOT, "Assets", "_Veyra", "Art", "World", "Chunks")
    roads = [o for o, c in cats.items() if c == "Road_Surface"]
    hc = heightmap_check(roads) if roads else {"checked": 0}
    log(f"heightmap check: {hc}")
    if hc.get("checked") and hc["max_m"] > 1.5 and not skip_height:
        raise SystemExit(f"STOP: road points deviate up to {hc['max_m']} m from the Veyra heightmap (> 1.5 m)")
    chunks = {}
    for o, c in list(cats.items()):
        if c in ("Road_Surface", "Sidewalk", "Terrain"):
            bisect_grid(o, size)
            for key, part in split_by_chunk(o, size).items():
                chunks.setdefault(key, {}).setdefault(c, []).append(part)
        elif c is not None:
            lo, hi = bounds([o])
            key = chunk_of((lo.x + hi.x) / 2, (lo.y + hi.y) / 2, size)[0]
            chunks.setdefault(key, {}).setdefault(c, []).append(o)
    manifest = {"source": source, "licence": lic, "chunk_size_m": size, "chunks": []}
    warnings = []
    col_roads = {}
    max_tris = 0
    totals = [0, 0, 0]
    for key, by_cat in sorted(chunks.items()):
        _, cell, corner = chunk_of(*_corner_probe(key, size), size)
        objs, cols, mats = [], [], set()
        # collision first (from the un-merged surfaces)
        road_like = [o for c in ("Road_Surface", "Sidewalk") for o in by_cat.get(c, [])]
        if road_like:
            parts = []
            for o in road_like:
                bm = bmesh.new()
                bm.from_mesh(o.data)
                bm.faces.ensure_lookup_table()
                drop = [f for f in bm.faces if f.material_index < len(o.data.materials) and o.data.materials[f.material_index]
                        and re.search(r"marking|line|stripe|paint|crosswalk", o.data.materials[f.material_index].name, re.I)]
                if drop:
                    bmesh.ops.delete(bm, geom=drop, context="FACES")
                parts.append(new_object(f"tmp_col_{o.name}", bm))
            col = join(parts, f"COL_Road_{key}")
            bm = bmesh.new()
            bm.from_mesh(col.data)
            bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.001)
            dissolve_keep_borders(bm, math.radians(1.0))
            bmesh.ops.triangulate(bm, faces=bm.faces)
            bm.to_mesh(col.data)
            bm.free()
            col.data.materials.clear()
            cols.append(col)
            col_roads[key] = border_keys(col, size)  # before the pivot shift: world coordinates
        if by_cat.get("Terrain"):
            copies = []
            for t in by_cat["Terrain"]:
                cp = t.copy()
                cp.data = t.data.copy()
                bpy.context.scene.collection.objects.link(cp)
                copies.append(cp)
            g = join(copies, f"COL_Ground_{key}")
            g.data.materials.clear()
            cols.append(g)
        for o in by_cat.get("Building", []):
            cols += collider_for(o, f"{key}_{o.name}")
        for o in by_cat.get("Prop", []):
            cols += collider_for(o, f"{key}_{o.name}", allow_none=True)
        # render meshes merged by category + material
        for cat, items in by_cat.items():
            groups = {}
            for o in items:
                groups.setdefault(material_names(o) or "none", []).append(o)
            for mname, members in groups.items():
                mats.add((cat, mname))
                merged = join(members, f"{key}_{cat}_{re.sub(r'[^A-Za-z0-9]+', '', mname)[:24] or 'Mat'}")
                root, lods = make_lods(merged, (1.0, 0.4, 0.1), planar=(cat in ("Road_Surface", "Sidewalk", "Terrain")))
                objs += [root] + lods
        if len(mats) > 12:
            warnings.append(f"{key}: {len(mats)} materials (> 12)")
        lod_tris = [sum(tris(o) for o in objs if o.type == "MESH" and o.name.endswith(f"_LOD{i}")) for i in range(3)]
        for i in range(3):
            totals[i] += lod_tris[i]
        max_tris = max(max_tris, lod_tris[0])
        if lod_tris[0] > 500_000:
            warnings.append(f"{key}: LOD0 {lod_tris[0]:,} triangles > 500k (FAIL)")
        elif lod_tris[0] > 300_000:
            warnings.append(f"{key}: LOD0 {lod_tris[0]:,} triangles > 300k (warn)")
        hide_collision(cols)
        # pivot: chunk south-west corner at height 0
        # Every mesh's data moves by -corner; the LOD-group empties stay at the origin, which becomes the chunk's corner.
        shift = Matrix.Translation((-corner[0], -corner[1], 0.0))
        for o in [x for x in objs + cols if x.type == "MESH"]:
            o.data.transform(shift)
        path = os.path.join(out_dir, cell, key + ".fbx")
        os.makedirs(os.path.dirname(path), exist_ok=True)
        vc.vc_export_fbx(objs + cols, path, mode="world", path_mode="STRIP")
        manifest["chunks"].append({"chunk": key, "cell": cell, "file": rel(path),
                                   "corner_unity_xz": [corner[0], corner[1]], "triangles": lod_tris,
                                   "collision": [c.name for c in cols], "materials": [{"source": m, "category": c, "veyra": veyra_key(m, c)} for c, m in sorted(mats)]})
        for o in objs + cols:
            o.hide_set(True)
    cracks = border_cracks(col_roads, size) if len(col_roads) > 1 else 0
    manifest["border_cracks"] = cracks
    os.makedirs(out_dir, exist_ok=True)
    with open(os.path.join(out_dir, "chunks_manifest.json"), "w", encoding="utf-8") as f:
        json.dump(manifest, f, indent=1)
    return manifest, warnings, {"heightmap": hc, "lod_triangles": totals, "max_chunk_lod0": max_tris, "border_cracks": cracks}


def _corner_probe(key, size):
    """A point inside chunk <key> (Chunk_<Cell>_<i><j>) in Blender coordinates."""
    cell, ij = key.split("_")[1], key.split("_")[2]
    col = "ABCDEFGH".index(cell[0])
    row_s = 6 - int(cell[1:])
    i, j = int(ij[0]), int(ij[1])
    return (col * 1000 + i * size + 1 - 4000.0, row_s * 1000 + j * size + 1 - 3000.0)


# ---------------------------------------------------------------------------------------------------------------- evidence

def render_categories(cats, path, highlight_unclassified=False, collision=()):
    """Workbench render from above at an angle: categories colour-coded (unclassified red), collision in wireframe."""
    for o, c in cats.items():
        if o.name not in bpy.data.objects:
            continue
        o.color = COLOURS[c] if not highlight_unclassified else (COLOURS[None] if c is None else (0.6, 0.6, 0.6, 1))
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.color_type = "OBJECT"
    scene.display.shading.light = "STUDIO"
    scene.render.resolution_x, scene.render.resolution_y = 1600, 900
    objs = [o for o in bpy.data.objects if o.type == "MESH" and o.visible_get()]
    lo, hi = bounds(objs) if objs else (Vector((0, 0, 0)), Vector((1, 1, 1)))
    centre = (lo + hi) / 2
    radius = max((hi - lo).length * 0.6, 5.0)
    cam_data = bpy.data.cameras.new("B2_cam")
    cam = bpy.data.objects.new("B2_cam", cam_data)
    scene.collection.objects.link(cam)
    cam.location = centre + Vector((radius * 0.7, -radius * 0.9, radius * 0.8))
    cam.rotation_euler = (centre - cam.location).to_track_quat("-Z", "Y").to_euler()
    cam_data.lens = 35
    cam_data.clip_end = radius * 10
    scene.camera = cam
    scene.render.filepath = path
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(cam, do_unlink=True)


# ---------------------------------------------------------------------------------------------------------------- test map

def make_test(out_dir):
    """Synthetic CC0 map in Veyra (Unity-frame) coordinates over 4 chunks of D5 on the Veyra terrain height: a road cross,
    raised sidewalks, buildings and lamp props. Used to test the ALIGNED path (chunk borders, collision, manifest)."""
    import numpy as np
    bpy.ops.wm.read_factory_settings(use_empty=True)
    os.makedirs(out_dir, exist_ok=True)
    img = bpy.data.images.load(HEIGHTMAP)
    img.colorspace_settings.name = "Non-Color"
    w, h = img.size
    px = np.empty(w * h * 4, dtype=np.float32)
    img.pixels.foreach_get(px)
    lum = px[0::4].reshape(h, w)

    def ground(bx, by):
        x, y = bx + 4000.0, by + 3000.0
        col = int(round(x / 8000.0 * (w - 1)))
        row_from_top = int(round((1.0 - y / 6000.0) * (h - 1)))
        return -60.0 + float(lum[h - 1 - row_from_top, col]) * 560.0

    def mat(name, rgb):
        m = bpy.data.materials.new(name)
        m.diffuse_color = (*rgb, 1)
        return m
    road_m, side_m, bld_m, lamp_m, line_m = mat("asphalt", (0.1, 0.1, 0.1)), mat("pavement", (0.5, 0.5, 0.5)), mat("brick_facade", (0.6, 0.3, 0.2)), mat("lamp_metal", (0.3, 0.3, 0.3)), mat("marking_white", (0.9, 0.9, 0.9))
    cx, cy = -750.0, -1500.0   # Unity frame = data (3250, 1500): cell D5, on the corner of four of its 250 m chunks
    def grid_plane(name, x0, x1, y0, y1, dz, m, step=5.0):
        bm = bmesh.new()
        nx, ny = max(1, int((x1 - x0) / step)), max(1, int((y1 - y0) / step))
        verts = [[bm.verts.new((x0 + (x1 - x0) * i / nx, y0 + (y1 - y0) * j / ny, ground(x0 + (x1 - x0) * i / nx, y0 + (y1 - y0) * j / ny) + dz)) for j in range(ny + 1)] for i in range(nx + 1)]
        for i in range(nx):
            for j in range(ny):
                bm.faces.new((verts[i][j], verts[i + 1][j], verts[i + 1][j + 1], verts[i][j + 1]))
        ob = new_object(name, bm)
        ob.data.materials.append(m)
        return ob
    grid_plane("Road_EW", cx - 200, cx + 200, cy - 6, cy + 6, 0.0, road_m)
    grid_plane("Road_NS", cx - 6, cx + 6, cy - 200, cy - 6, 0.0, road_m)
    grid_plane("Road_NS2", cx - 6, cx + 6, cy + 6, cy + 200, 0.0, road_m)
    grid_plane("Line_Centre", cx - 200, cx + 200, cy - 0.08, cy + 0.08, 0.01, line_m, step=10.0)
    for k, x in enumerate(range(int(cx - 190), int(cx + 190), 60)):
        if x + 50 > cx - 10 and x < cx + 10:
            continue  # this block would sit on the north-south road (x = cx +/- 6): no building, no kerb across the road
        grid_plane(f"Sidewalk_{k}", x, x + 50, cy + 6, cy + 9, 0.15, side_m)
        g = ground(x + 25, cy + 30)
        bpy.ops.mesh.primitive_cube_add(size=1, location=(x + 25, cy + 30, g + 12))
        b = bpy.context.active_object
        b.scale = (40, 30, 24)
        b.name = f"Building_{k}"
        b.data.materials.append(bld_m)
        bpy.ops.mesh.primitive_cylinder_add(radius=0.12, depth=6, location=(x + 5, cy + 8, ground(x + 5, cy + 8) + 3.15))
        lamp = bpy.context.active_object
        lamp.name = f"Lamp_Pole_{k}"
        lamp.data.materials.append(lamp_m)
    path = os.path.join(out_dir, "test_map_aligned.glb")
    bpy.ops.export_scene.gltf(filepath=path, export_format="GLB")
    with open(path + ".license.txt", "w", encoding="utf-8") as f:
        f.write("File: test_map_aligned.glb\nLicence: CC0\nAuthor: VEYRA CITY test generator (process_city_map.py --make-test)\nSource: generated\n")
    log(f"test map {path} (Veyra coordinates, D5, on the heightmap)")
    return path


# ---------------------------------------------------------------------------------------------------------------- main

def main():
    o = options()
    t0 = time.time()
    if o["make-test"]:
        o["input"] = make_test(os.path.join(ROOT, o["make-test"]))
        o["mode"] = "aligned"
        o["out"] = o["out"] or os.path.join(ROOT, o["make-test"], "chunks")
    src = os.path.join(ROOT, o["input"]) if not os.path.isabs(o["input"]) else o["input"]
    source = re.sub(r"_[0-9a-f]{8}$", "", re.sub(r"[^A-Za-z0-9_]+", "", os.path.splitext(os.path.basename(src))[0]))
    lic = licence(src)
    if o["mode"] == "aligned" and not o["make-test"] and not os.path.exists(os.path.join(HERE, "S4_PASSED")):
        raise SystemExit("REFUSED: world-positioned export needs the S4 axis round-trip test (Tools/Blender/S4_PASSED).")
    scale = float(o["scale"]) if o["scale"] else 1.0
    if not o["scale"]:
        tri = os.path.join(ROOT, "Logs", "triage.json")
        if os.path.exists(tri):
            with open(tri, encoding="utf-8") as f:
                for m in json.load(f).get("maps", []):
                    if m["file"] == rel(src):
                        scale = float(m.get("estimated_scale", 1.0))
    report_path = os.path.join(ROOT, o["report"] or f"Logs/ingest_{source}.json")
    bpy.ops.wm.read_factory_settings(use_empty=True)
    import_any(src, os.path.join("E:/Temp/b2", source))
    in_tris = sum(tris(x) for x in meshes())
    normalise(scale)
    log(f"{source}: imported {len(meshes())} meshes, {in_tris:,} triangles, scale {scale}")
    unc_png = os.path.join(ROOT, "Logs", f"ingest_{source}_unclassified.png")
    cats, ground, share = segment(float(o["max-unclassified"]), source, unc_png)
    cat_tris = {c: sum(tris(x) for x, cc in cats.items() if cc == c) for c in CATEGORIES + (None,)}
    render_categories(cats, os.path.join(ROOT, "Logs", f"ingest_{source}_categories.png"))
    report = {"source": rel(src), "mode": o["mode"], "licence": lic, "scale": scale, "input_triangles": in_tris,
              "category_triangles": {str(k): v for k, v in cat_tris.items()}, "unclassified_percent": round(share, 2)}
    if o["mode"] == "kitbash":
        man, warn = kitbash(cats, source, o["out"], int(o["max-objects"]), lic)
        report.update(pieces=len(man["pieces"]), lod_triangles=[sum(p["triangles"][i] if i < len(p["triangles"]) else 0 for p in man["pieces"]) for i in range(3)],
                      max_piece_lod0=max((p["triangles"][0] for p in man["pieces"]), default=0), textures=man["textures"], warnings=warn,
                      manifest=rel(os.path.join(o["out"] or os.path.join(ROOT, "Assets", "_Veyra", "Art", "Models", "Kitbash", source), "kitbash_manifest.json")))
    else:
        man, warn, stats = aligned(cats, source, o["out"], float(o["chunk"]), lic, ground, o["skip-height-check"])
        report.update(chunks=len(man["chunks"]), warnings=warn, **stats,
                      manifest=rel(os.path.join(o["out"] or os.path.join(ROOT, "Assets", "_Veyra", "Art", "World", "Chunks"), "chunks_manifest.json")))
    report["seconds"] = round(time.time() - t0, 1)
    report["log"] = LOG
    os.makedirs(os.path.dirname(report_path), exist_ok=True)
    with open(report_path, "w", encoding="utf-8") as f:
        json.dump(report, f, indent=1)
    log(f"done in {report['seconds']} s -> {rel(report_path)}")


main()

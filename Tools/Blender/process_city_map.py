"""
VEYRA CITY - ingest a LICENSED downloaded city/map model into 250 m chunks (playbook card B2).

  blender -b --factory-startup --python process_city_map.py -- --src <model> --origin-x <data x> --origin-y <data y>
          [--scale 1.0] [--yaw 0] [--out E:/VeyraCity/Assets/_Veyra/Art/World/Chunks] [--test]
  blender -b --factory-startup --python process_city_map.py -- --make-test E:/VeyraCity/Logs/B2_test

Gates (CLAUDE.md):
  * Licence: <model>.licence.json must exist with "licence" in ALLOWED (CC0, CC-BY-4.0, own-work,
    purchased-royalty-free) plus "author" and "url". Ripped game content (GTA, Cyberpunk, etc.) is refused.
  * World export: world-positioned chunks go into Assets only after the S4 axis round-trip test passed
    (Tools/Blender/S4_PASSED). --test writes to Logs/B2_test instead.

Pipeline:
  1. import (glb/gltf/fbx/obj/blend/zip), bake hierarchy, scale to metres, rotate by --yaw
  2. place the model so its origin sits at data (origin-x, origin-y); Blender X = data x (east), Y = data y (north), Z = up
  3. classify every face: Road_Surface, Sidewalk, Building, Prop (names first, then geometry: flat+low+dark = road,
     flat+low = sidewalk, tall/vertical = building, rest = prop)
  4. slice along the 250 m grid and bucket faces per chunk: Chunk_<Cell>_<i><j>_<Category> (i west->east, j south->north)
  5. collision: COL_Road_<chunk> (non-convex copy of the road surface), UBX_Building_<n> (one box per building part)
  6. pivot each chunk at its south-west corner at height 0, export one FBX per chunk + chunk_report.json
"""
import bpy
import bmesh
import json
import math
import os
import sys
import zipfile
from mathutils import Vector, Matrix

ALLOWED = {"CC0", "CC-BY-4.0", "own-work", "purchased-royalty-free"}
FORBIDDEN = ("gta", "grand theft", "liberty city", "vice city", "san andreas", "los santos", "cyberpunk", "night city",
             "rockstar", "cd projekt", "need for speed", "forza")
CHUNK = 250.0
KEYS = {
    "Road_Surface": ("road", "asphalt", "street", "tarmac", "lane", "highway", "motorway"),
    "Sidewalk": ("sidewalk", "pavement", "curb", "kerb", "footpath", "walkway", "plaza"),
    "Building": ("building", "wall", "facade", "window", "roof", "house", "tower", "apartment", "office", "shop"),
}


def args():
    a = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    o = {"src": None, "origin-x": "0", "origin-y": "0", "scale": "1", "yaw": "0",
         "out": "E:/VeyraCity/Assets/_Veyra/Art/World/Chunks", "test": False, "make-test": None}
    i = 0
    while i < len(a):
        k = a[i].lstrip("-")
        if k == "test":
            o["test"] = True
        elif k in o and i + 1 < len(a):
            o[k] = a[i + 1]
            i += 1
        i += 1
    return o


def licence_gate(src):
    low = os.path.basename(src).lower()
    if any(f in low for f in FORBIDDEN):
        raise SystemExit(f"REFUSED: '{os.path.basename(src)}' looks like content ripped from another game. "
                         "Original content only (CLAUDE.md).")
    side = src + ".licence.json"
    if not os.path.exists(side):
        raise SystemExit(f"REFUSED: no licence file {side}. Add {{\"licence\": one of {sorted(ALLOWED)}, \"author\", \"url\"}}.")
    lic = json.load(open(side, encoding="utf-8"))
    if lic.get("licence") not in ALLOWED or not lic.get("author") or not lic.get("url"):
        raise SystemExit(f"REFUSED: licence '{lic.get('licence')}' not allowed or author/url missing in {side}.")
    return lic


def import_any(src, work):
    low = src.lower()
    if low.endswith(".zip"):
        os.makedirs(work, exist_ok=True)
        with zipfile.ZipFile(src) as z:
            for m in z.namelist():
                if ".." not in m and not m.startswith(("/", "\\")):
                    z.extract(m, work)
        for root, _, files in os.walk(work):
            for f in files:
                if f.lower().endswith((".glb", ".gltf", ".fbx", ".obj", ".blend")):
                    return import_any(os.path.join(root, f), work + "_x")
        raise SystemExit("no model inside zip")
    if low.endswith((".glb", ".gltf")):
        bpy.ops.import_scene.gltf(filepath=src)
    elif low.endswith(".fbx"):
        bpy.ops.import_scene.fbx(filepath=src)
    elif low.endswith(".obj"):
        bpy.ops.wm.obj_import(filepath=src)
    elif low.endswith(".blend"):
        with bpy.data.libraries.load(src, link=False) as (df, dt):
            dt.objects = list(df.objects)
        for ob in dt.objects:
            if ob is not None:
                bpy.context.scene.collection.objects.link(ob)
    else:
        raise SystemExit("unsupported " + src)


def meshes():
    return [o for o in bpy.context.scene.objects if o.type == "MESH"]


def bake_and_join(scale, yaw, ox, oy):
    for o in list(bpy.context.scene.objects):
        if o.parent is not None:
            mw = o.matrix_world.copy()
            o.parent = None
            o.matrix_world = mw
    for o in list(bpy.context.scene.objects):
        if o.type != "MESH":
            bpy.data.objects.remove(o, do_unlink=True)
    xf = Matrix.Translation((ox, oy, 0.0)) @ Matrix.Rotation(math.radians(yaw), 4, "Z") @ Matrix.Scale(scale, 4)
    for o in meshes():
        if o.data.users > 1:
            o.data = o.data.copy()
        # keep the source object name on each face via a material-name fallback slot
        o.matrix_world = xf @ o.matrix_world
    bpy.ops.object.select_all(action="SELECT")
    bpy.context.view_layer.objects.active = meshes()[0]
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)


def classify(ob, poly, mesh, ground):
    names = (ob.name + " " + (mesh.materials[poly.material_index].name if mesh.materials and poly.material_index < len(mesh.materials) and mesh.materials[poly.material_index] else "")).lower()
    for cat, words in KEYS.items():
        if any(w in names for w in words):
            return cat
    n = poly.normal
    c = poly.center
    h = c.z - ground
    if n.z > 0.92 and h < 1.2:
        col = None
        if mesh.materials and poly.material_index < len(mesh.materials) and mesh.materials[poly.material_index]:
            col = mesh.materials[poly.material_index].diffuse_color
        dark = col is not None and (0.3 * col[0] + 0.59 * col[1] + 0.11 * col[2]) < 0.3
        return "Road_Surface" if dark or h < 0.12 else "Sidewalk"
    if abs(n.z) < 0.35 or h > 4.0:
        return "Building"
    return "Prop"


def chunk_id(x, y):
    col = max(0, min(7, int(x // 1000)))
    row_s = max(0, min(5, int(y // 1000)))
    i = max(0, min(3, int((x - col * 1000) // CHUNK)))
    j = max(0, min(3, int((y - row_s * 1000) // CHUNK)))
    return f"Chunk_{'ABCDEFGH'[col]}{6 - row_s}_{i}{j}", (col * 1000 + i * CHUNK, row_s * 1000 + j * CHUNK)


def slice_grid(ob):
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    xs = [v.co.x for v in bm.verts]
    ys = [v.co.y for v in bm.verts]
    if not xs:
        bm.free()
        return
    for gx in range(int(min(xs) // CHUNK) + 1, int(max(xs) // CHUNK) + 1):
        geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
        bmesh.ops.bisect_plane(bm, geom=geom, plane_co=(gx * CHUNK, 0, 0), plane_no=(1, 0, 0))
    for gy in range(int(min(ys) // CHUNK) + 1, int(max(ys) // CHUNK) + 1):
        geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
        bmesh.ops.bisect_plane(bm, geom=geom, plane_co=(0, gy * CHUNK, 0), plane_no=(0, 1, 0))
    bm.to_mesh(ob.data)
    bm.free()


def bucket(objs):
    """Split all faces into (chunk, category) objects."""
    buckets = {}
    for ob in objs:
        me = ob.data
        zs = [v.co.z for v in me.vertices]
        ground = min(zs) if zs else 0.0
        bm = bmesh.new()
        bm.from_mesh(me)
        bm.faces.ensure_lookup_table()
        tags = {}
        for poly in me.polygons:
            cid, corner = chunk_id(poly.center.x, poly.center.y)
            tags[poly.index] = (cid, classify(ob, poly, me, ground), corner)
        for key in set((t[0], t[1]) for t in tags.values()):
            sub = bm.copy()
            sub.faces.ensure_lookup_table()
            drop = [f for f in sub.faces if (tags[f.index][0], tags[f.index][1]) != key]
            bmesh.ops.delete(sub, geom=drop, context="FACES")
            name = f"{key[0]}_{key[1]}"
            dst = buckets.get(name)
            nm = bpy.data.meshes.new(name + "_part")
            sub.to_mesh(nm)
            sub.free()
            for m in me.materials:
                nm.materials.append(m)
            part = bpy.data.objects.new(name + "_part", nm)
            bpy.context.scene.collection.objects.link(part)
            buckets.setdefault(name, {"objs": [], "corner": next(t[2] for t in tags.values() if (t[0], t[1]) == key), "chunk": key[0], "cat": key[1]})["objs"].append(part)
        bm.free()
        bpy.data.objects.remove(ob, do_unlink=True)
    return buckets


def join(objs, name):
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


def box_from(points, name):
    mn = Vector((min(p.x for p in points), min(p.y for p in points), min(p.z for p in points)))
    mx = Vector((max(p.x for p in points), max(p.y for p in points), max(p.z for p in points)))
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co = Vector(((mn.x + mx.x) / 2 + v.co.x * (mx.x - mn.x), (mn.y + mx.y) / 2 + v.co.y * (mx.y - mn.y), (mn.z + mx.z) / 2 + v.co.z * (mx.z - mn.z)))
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    return ob


def building_boxes(ob, chunk):
    """One UBX box per loose building part."""
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    bm.verts.ensure_lookup_table()
    seen = set()
    boxes = []
    for v in bm.verts:
        if v.index in seen:
            continue
        stack, island = [v], []
        seen.add(v.index)
        while stack:
            cur = stack.pop()
            island.append(cur.co.copy())
            for e in cur.link_edges:
                o = e.other_vert(cur)
                if o.index not in seen:
                    seen.add(o.index)
                    stack.append(o)
        if len(island) >= 4:
            boxes.append(box_from(island, f"UBX_Building_{chunk}_{len(boxes):03d}"))
    bm.free()
    return boxes


def make_test(out_dir):
    """Synthetic CC0 test map: a road cross, sidewalks and boxes spanning several chunks at data (3600, 2100)."""
    bpy.ops.wm.read_factory_settings(use_empty=True)
    os.makedirs(out_dir, exist_ok=True)

    def mat(name, rgb):
        m = bpy.data.materials.new(name)
        m.diffuse_color = (*rgb, 1)
        return m
    road_m, side_m, bld_m = mat("asphalt", (0.1, 0.1, 0.1)), mat("pavement", (0.5, 0.5, 0.5)), mat("facade", (0.7, 0.6, 0.5))
    for (sx, sy, cx, cy) in [(600, 12, 0, 0), (12, 600, 0, 0)]:
        bpy.ops.mesh.primitive_plane_add(size=1, location=(cx, cy, 0))
        p = bpy.context.active_object
        p.scale = (sx, sy, 1)
        p.data.materials.append(road_m)
        p.name = "Road_Main"
    for k in range(-3, 4):
        bpy.ops.mesh.primitive_plane_add(size=1, location=(k * 80, 10, 0.15))
        p = bpy.context.active_object
        p.scale = (70, 6, 1)
        p.data.materials.append(side_m)
        p.name = "Sidewalk"
        bpy.ops.mesh.primitive_cube_add(size=1, location=(k * 80, 40, 15))
        b = bpy.context.active_object
        b.scale = (30, 20, 30)
        b.data.materials.append(bld_m)
        b.name = "Building"
    path = os.path.join(out_dir, "test_map.glb")
    bpy.ops.export_scene.gltf(filepath=path, export_format="GLB")
    json.dump({"licence": "CC0", "author": "VEYRA CITY test generator", "url": "generated"}, open(path + ".licence.json", "w"))
    print("TEST MAP", path)
    return path


def main():
    o = args()
    if o["make-test"]:
        src = make_test(o["make-test"])
        o["src"], o["test"], o["origin-x"], o["origin-y"] = src, True, "3600", "2100"
    src = o["src"]
    lic = licence_gate(src)
    out = o["out"]
    if o["test"]:
        out = "E:/VeyraCity/Logs/B2_test/chunks"
    elif not os.path.exists(os.path.join(os.path.dirname(os.path.abspath(__file__)), "S4_PASSED")):
        raise SystemExit("REFUSED: world-positioned export needs the S4 axis round-trip test first (Tools/Blender/S4_PASSED). Use --test.")
    bpy.ops.wm.read_factory_settings(use_empty=True)
    import_any(src, "E:/Temp/VeyraCity/citymap")
    bake_and_join(float(o["scale"]), float(o["yaw"]), float(o["origin-x"]), float(o["origin-y"]))
    for ob in meshes():
        slice_grid(ob)
    buckets = bucket(meshes())
    os.makedirs(out, exist_ok=True)
    report = {"source": src, "licence": lic, "chunks": {}}
    by_chunk = {}
    for name, b in buckets.items():
        ob = join(b["objs"], f"{b['cat']}_{b['chunk']}")
        by_chunk.setdefault(b["chunk"], {"corner": b["corner"], "objs": []})["objs"].append(ob)
        if b["cat"] == "Road_Surface":
            col = ob.copy()
            col.data = ob.data.copy()
            col.name = f"COL_Road_{b['chunk']}"
            bpy.context.scene.collection.objects.link(col)
            by_chunk[b["chunk"]]["objs"].append(col)
        if b["cat"] == "Building":
            by_chunk[b["chunk"]]["objs"].extend(building_boxes(ob, b["chunk"]))
    for chunk, info in by_chunk.items():
        cx, cy = info["corner"]
        bpy.ops.object.select_all(action="DESELECT")
        for ob in info["objs"]:
            ob.data.transform(Matrix.Translation((-cx, -cy, 0.0)))  # pivot: chunk SW corner at height 0
            ob.matrix_world = Matrix.Identity(4)
            ob.select_set(True)
        path = os.path.join(out, chunk + ".fbx")
        bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"MESH"}, apply_unit_scale=True,
                                 apply_scale_options="FBX_SCALE_UNITS", axis_forward="-Z", axis_up="Y",
                                 bake_space_transform=True, mesh_smooth_type="FACE", add_leaf_bones=False, path_mode="COPY", embed_textures=True)
        tris = {ob.name: sum(len(p.vertices) - 2 for p in ob.data.polygons) for ob in info["objs"]}
        report["chunks"][chunk] = {"corner_data_xy": [cx, cy], "fbx": path, "objects": tris}
        for ob in info["objs"]:
            ob.hide_set(True)
    json.dump(report, open(os.path.join(out, "chunk_report.json"), "w"), indent=1)
    print("CHUNK_REPORT", json.dumps({k: list(v["objects"].keys()) for k, v in report["chunks"].items()}))


main()

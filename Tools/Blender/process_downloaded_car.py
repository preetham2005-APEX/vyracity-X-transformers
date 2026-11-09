"""
Turn a downloaded car model (glTF/GLB zip, .glb, .gltf, .blend, .fbx, .obj) into a VEYRA CITY vehicle asset.

  blender -b --factory-startup --python process_downloaded_car.py -- \
      --src <file or folder> --id <car_id> --name "<Display Name>" [--length 4.5] [--flip 0|1] [--yaw 0|90|180|270]

Pipeline (CLAUDE.md section 4):
  * import, find the car's long axis, rotate so the front faces Blender -Y (Unity +Z after export)
  * scale to the requested length when the model's own scale is implausible
  * detect wheels (by object name, else by disc-shaped objects near the four bottom corners)
  * create Wheel_FL/FR/RL/RR empties at the hub centres and parent the wheel meshes to them
    (origins on the hubs; location is NOT applied, rotation and scale are)
  * join everything else into Body_LOD0, decimate copies into Body_LOD1/2 (and LOD0 if it is huge)
  * UBX_Chassis box and UCX_Body convex hull for collision
  * export FBX (-Z forward, Y up, baked space transform, embedded textures) and a JSON report
    with dimensions, hub positions, wheel radius, track and wheelbase for the vehicle definition
  * render a three-quarter preview PNG for review
"""
import bpy
import bmesh
import json
import math
import os
import sys
import zipfile
from mathutils import Vector, Matrix

ART = "E:/VeyraCity/ArtSource/Vehicles"
WHEEL_WORDS = ("wheel", "tire", "tyre", "rim", "disc", "disk", "brake", "caliper", "hub", "rueda", "roda", "llanta", "felge", "reifen")


def args():
    a = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    o = {"src": None, "id": None, "name": None, "length": "0", "flip": "0", "yaw": "0", "lod0": "180000"}
    i = 0
    while i < len(a):
        k = a[i].lstrip("-")
        if k in o and i + 1 < len(a):
            o[k] = a[i + 1]
            i += 1
        i += 1
    return o


def import_any(src, work):
    if os.path.isdir(src):
        for root, _, files in os.walk(src):
            for f in files:
                if f.lower().endswith((".gltf", ".glb", ".blend", ".fbx", ".obj")):
                    return import_any(os.path.join(root, f), work)
        raise RuntimeError("no model file in " + src)
    low = src.lower()
    if low.endswith(".zip"):
        os.makedirs(work, exist_ok=True)
        with zipfile.ZipFile(src) as z:
            for m in z.namelist():
                if ".." in m or m.startswith(("/", "\\")):
                    continue
                z.extract(m, work)
        return import_any(work, work + "_x")
    if low.endswith((".gltf", ".glb")):
        bpy.ops.import_scene.gltf(filepath=src)
    elif low.endswith(".fbx"):
        bpy.ops.import_scene.fbx(filepath=src)
    elif low.endswith(".obj"):
        bpy.ops.wm.obj_import(filepath=src)
    elif low.endswith(".blend"):
        with bpy.data.libraries.load(src, link=False) as (data_from, data_to):
            data_to.objects = list(data_from.objects)
        for ob in data_to.objects:
            if ob is not None:
                bpy.context.scene.collection.objects.link(ob)
    else:
        raise RuntimeError("unsupported " + src)
    return src


def meshes():
    return [o for o in bpy.context.scene.objects if o.type == "MESH" and not o.hide_render]


def world_bbox(objs):
    mn = Vector((1e9, 1e9, 1e9))
    mx = Vector((-1e9, -1e9, -1e9))
    for o in objs:
        for c in o.bound_box:
            w = o.matrix_world @ Vector(c)
            mn = Vector(map(min, mn, w))
            mx = Vector(map(max, mx, w))
    return mn, mx


def bake_hierarchy():
    """Clear parents keeping transforms, apply rotation/scale to meshes (instancing split)."""
    for o in list(bpy.context.scene.objects):
        if o.parent is not None:
            mw = o.matrix_world.copy()
            o.parent = None
            o.matrix_world = mw
    for o in list(bpy.context.scene.objects):
        if o.type not in ("MESH",):
            bpy.data.objects.remove(o, do_unlink=True)
    bpy.ops.object.select_all(action="DESELECT")
    for o in meshes():
        if o.data.users > 1:
            o.data = o.data.copy()
        o.select_set(True)
    bpy.context.view_layer.objects.active = meshes()[0]
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)


def orient_and_scale(target_len, flip, yaw):
    objs = meshes()
    mn, mx = world_bbox(objs)
    size = mx - mn
    rot = Matrix.Identity(4)
    # make Y the long horizontal axis
    if size.x > size.y:
        rot = Matrix.Rotation(math.radians(90), 4, "Z") @ rot
    if yaw:
        rot = Matrix.Rotation(math.radians(yaw), 4, "Z") @ rot
    if flip:
        rot = Matrix.Rotation(math.radians(180), 4, "Z") @ rot
    for o in objs:
        o.matrix_world = rot @ o.matrix_world
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    mn, mx = world_bbox(objs)
    length = mx.y - mn.y
    k = 1.0
    if target_len > 0 and (length < 3.0 or length > 6.5 or abs(length - target_len) / target_len > 0.25):
        k = target_len / length
    centre = Vector(((mn.x + mx.x) / 2, (mn.y + mx.y) / 2, mn.z))
    for o in objs:
        o.matrix_world = Matrix.Scale(k, 4) @ Matrix.Translation(-centre) @ o.matrix_world
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    return k


def is_wheelish(o):
    n = (o.name + " " + (o.data.name if o.data else "")).lower()
    return any(w in n for w in WHEEL_WORDS)


def find_wheels():
    objs = meshes()
    mn, mx = world_bbox(objs)
    L = mx.y - mn.y
    W = mx.x - mn.x
    H = mx.z - mn.z
    groups = {"FL": [], "FR": [], "RL": [], "RR": []}
    named = [o for o in objs if is_wheelish(o)]
    cands = named
    if not cands:
        # disc-shaped objects near the bottom corners
        for o in objs:
            a, b = world_bbox([o])
            s = b - a
            if s.z < 0.2 or s.z > H * 0.7:
                continue
            if abs(s.y - s.z) / max(s.z, 1e-3) > 0.35 or s.x > s.z * 0.9:
                continue
            if a.z > H * 0.15:
                continue
            cands.append(o)
    for o in cands:
        a, b = world_bbox([o])
        c = (a + b) / 2
        if c.z > H * 0.6:
            continue  # steering wheel, roof items
        if abs(c.x) < W * 0.18:
            continue  # spare wheel / centre parts
        key = ("F" if c.y < 0 else "R") + ("L" if c.x < 0 else "R")
        groups[key].append(o)
    return groups


def hub_of(objs):
    a, b = world_bbox(objs)
    # the tyre is the widest disc: use the largest object for the radius
    big = max(objs, key=lambda o: (world_bbox([o])[1] - world_bbox([o])[0]).length)
    ba, bb = world_bbox([big])
    centre = Vector(((ba.x + bb.x) / 2, (ba.y + bb.y) / 2, (ba.z + bb.z) / 2))
    radius = max(bb.z - ba.z, bb.y - ba.y) / 2
    width = bb.x - ba.x
    return centre, radius, width


def tri_count(o):
    return sum(len(p.vertices) - 2 for p in o.data.polygons)


def decimated_copy(src, name, ratio):
    o = src.copy()
    o.data = src.data.copy()
    o.name = name
    bpy.context.scene.collection.objects.link(o)
    if ratio < 0.999:
        m = o.modifiers.new("dec", "DECIMATE")
        m.ratio = ratio
        m.use_collapse_triangulate = True
        bpy.context.view_layer.objects.active = o
        bpy.ops.object.select_all(action="DESELECT")
        o.select_set(True)
        bpy.ops.object.modifier_apply(modifier=m.name)
    return o


def main():
    o = args()
    src, cid = o["src"], o["id"]
    name = o["name"] or cid
    out = os.path.join(ART, cid)
    os.makedirs(out, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    import_any(src, os.path.join("E:/Temp/VeyraCity/cars", cid))
    bake_hierarchy()
    k = orient_and_scale(float(o["length"]), o["flip"] == "1", int(o["yaw"]))
    groups = find_wheels()
    report = {"id": cid, "name": name, "source": src, "scale_applied": k, "wheels_found": {g: [x.name for x in v] for g, v in groups.items()}}
    wheel_objs = set(x for v in groups.values() for x in v)
    hubs = {}
    ok = all(len(v) > 0 for v in groups.values())
    if ok:
        for g, objs in groups.items():
            c, r, w = hub_of(objs)
            e = bpy.data.objects.new("Wheel_" + g, None)
            e.empty_display_type = "SPHERE"
            e.empty_display_size = r
            bpy.context.scene.collection.objects.link(e)
            e.location = c
            for ob in objs:
                mw = ob.matrix_world.copy()
                ob.parent = e
                ob.matrix_world = mw
            hubs[g] = {"centre": [round(c.x, 4), round(c.y, 4), round(c.z, 4)], "radius": round(r, 4), "width": round(w, 4)}
    else:
        wheel_objs = set()
    report["wheels_ok"] = ok
    report["hubs"] = hubs
    # join body
    body = [x for x in meshes() if x not in wheel_objs]
    bpy.ops.object.select_all(action="DESELECT")
    for b in body:
        b.select_set(True)
    bpy.context.view_layer.objects.active = body[0]
    bpy.ops.object.join()
    lod0 = bpy.context.view_layer.objects.active
    lod0.name = "Body_LOD0"
    tris0 = tri_count(lod0)
    limit = int(o["lod0"])
    if tris0 > limit:
        tmp = decimated_copy(lod0, "Body_tmp", limit / tris0)
        bpy.data.objects.remove(lod0, do_unlink=True)
        tmp.name = "Body_LOD0"
        lod0 = tmp
    lod1 = decimated_copy(lod0, "Body_LOD1", 0.45)
    lod2 = decimated_copy(lod0, "Body_LOD2", 0.15)
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
    ubx = bpy.data.objects.new("UBX_Chassis", me)
    bpy.context.scene.collection.objects.link(ubx)
    hullsrc = decimated_copy(lod0, "hull_src", min(1.0, 3000 / max(1, tri_count(lod0))))
    bm = bmesh.new()
    bm.from_mesh(hullsrc.data)
    bm.verts.ensure_lookup_table()
    pts = [v for v in bm.verts if v.co.z > mn.z + 0.25]
    res = bmesh.ops.convex_hull(bm, input=pts)
    keep = set(res["geom"])
    for el in list(bm.verts):
        if el not in keep:
            bm.verts.remove(el)
    me2 = bpy.data.meshes.new("UCX_Body")
    bm.to_mesh(me2)
    bm.free()
    ucx = bpy.data.objects.new("UCX_Body", me2)
    bpy.context.scene.collection.objects.link(ucx)
    bpy.data.objects.remove(hullsrc, do_unlink=True)
    # report dims
    allmn, allmx = world_bbox([lod0] + list(wheel_objs))
    report["dimensions"] = [round(allmx.x - allmn.x, 3), round(allmx.z - allmn.z, 3), round(allmx.y - allmn.y, 3)]
    if ok:
        report["wheelbase"] = round(abs(hubs["FL"]["centre"][1] - hubs["RL"]["centre"][1]), 4)
        report["track_front"] = round(abs(hubs["FL"]["centre"][0] - hubs["FR"]["centre"][0]), 4)
        report["track_rear"] = round(abs(hubs["RL"]["centre"][0] - hubs["RR"]["centre"][0]), 4)
        report["radius_front"] = hubs["FL"]["radius"]
        report["radius_rear"] = hubs["RL"]["radius"]
    report["tris"] = {"Body_LOD0": tri_count(lod0), "Body_LOD1": tri_count(lod1), "Body_LOD2": tri_count(lod2),
                      "wheels": sum(tri_count(x) for x in wheel_objs)}
    report["materials"] = sorted({s.material.name for ob in [lod0] + list(wheel_objs) for s in ob.material_slots if s.material})
    # export
    fbx = os.path.join(out, cid + ".fbx")
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.export_scene.fbx(
        filepath=fbx, use_selection=True, object_types={"EMPTY", "MESH"},
        apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS", global_scale=1.0,
        axis_forward="-Z", axis_up="Y", bake_space_transform=True,
        mesh_smooth_type="FACE", use_mesh_modifiers=True, add_leaf_bones=False,
        use_tspace=False, path_mode="COPY", embed_textures=True, bake_anim=False)
    report["fbx"] = fbx
    # preview render
    try:
        scn = bpy.context.scene
        for eng in ("BLENDER_EEVEE_NEXT", "BLENDER_EEVEE", "CYCLES"):
            try:
                scn.render.engine = eng
                break
            except TypeError:
                continue
        cam_data = bpy.data.cameras.new("cam")
        cam = bpy.data.objects.new("cam", cam_data)
        scn.collection.objects.link(cam)
        cam.location = (5.5, -6.5, 2.4)
        cam.rotation_euler = (math.radians(75), 0, math.radians(40))
        scn.camera = cam
        sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
        sun.rotation_euler = (math.radians(50), 0, math.radians(30))
        scn.collection.objects.link(sun)
        scn.render.resolution_x, scn.render.resolution_y = 1280, 720
        scn.render.filepath = os.path.join(out, cid + "_preview.png")
        bpy.ops.render.render(write_still=True)
        report["preview"] = scn.render.filepath
    except Exception as e:  # preview is optional
        report["preview_error"] = str(e)
    with open(os.path.join(out, "process_report.json"), "w") as f:
        json.dump(report, f, indent=1)
    print("PROCESS_REPORT " + json.dumps(report))


main()

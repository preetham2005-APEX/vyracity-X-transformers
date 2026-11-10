"""Card B1 step 3: triage the accepted map downloads in headless Blender.

    "D:\\New folder\\blender.exe" -b --factory-startup -P Tools/Blender/triage_downloads.py -- \
        --manifest Logs/B1/licence_gate.json --out Logs/triage.json --work E:/Temp/triage

For each ACCEPTED download under ArtSource/Downloads/FullMapModels (zip / glb / gltf / fbx / obj): safe-extract the zip
(no absolute paths, no "..") into its own work folder, import it into an empty scene, and record: format, object count,
total triangles, the largest single mesh (triangles, size), material and texture counts, missing textures, estimated unit
scale, up axis and whether it is a CITY (one connected scene) or a KIT (separate buildings / props), plus a recommended mode:
KITBASH (split into reusable buildings/props placed from Veyra data), ALIGNED (only for content authored in Veyra
coordinates; none is) or REJECT. Vehicle downloads are listed but not imported (card A8 already processed them).
Quarantined files are never touched. Results are written after every model, so a crash keeps what was done.
"""
import json
import math
import os
import sys
import time
import zipfile

import bpy
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []


def arg(name, default):
    return argv[argv.index(name) + 1] if name in argv else default


ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
MANIFEST = os.path.join(ROOT, arg("--manifest", "Logs/B1/licence_gate.json"))
OUT = os.path.join(ROOT, arg("--out", "Logs/triage.json"))
WORK = arg("--work", "E:/Temp/triage")
ONLY = arg("--only", "")
MODEL_EXT = (".glb", ".gltf", ".fbx", ".obj")


def safe_extract(zpath, dest):
    marker = os.path.join(dest, ".extracted")
    if os.path.exists(marker):
        return
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
                while True:
                    chunk = src.read(1 << 20)
                    if not chunk:
                        break
                    dst.write(chunk)
    open(marker, "w").close()


def find_model(folder):
    found = []
    for a, _, fs in os.walk(folder):
        for f in fs:
            if f.lower().endswith(MODEL_EXT):
                found.append(os.path.join(a, f))
    found.sort(key=lambda p: (MODEL_EXT.index(os.path.splitext(p)[1].lower()), len(p)))
    return found[0] if found else None


def import_model(path):
    ext = os.path.splitext(path)[1].lower()
    if ext in (".glb", ".gltf"):
        bpy.ops.import_scene.gltf(filepath=path)
        return "glTF (Y-up, converted to Blender Z-up on import)"
    if ext == ".fbx":
        bpy.ops.import_scene.fbx(filepath=path)
        return "FBX (axes from the file header)"
    if ext == ".obj":
        bpy.ops.wm.obj_import(filepath=path)
        return "OBJ (Y-up assumed, converted to Z-up)"
    raise ValueError("unsupported " + ext)


def world_bounds(objs):
    lo = Vector((math.inf,) * 3)
    hi = Vector((-math.inf,) * 3)
    for o in objs:
        for c in o.bound_box:
            w = o.matrix_world @ Vector(c)
            lo = Vector(map(min, lo, w))
            hi = Vector(map(max, hi, w))
    return lo, hi


def tri_count(mesh):
    return sum(max(0, len(p.vertices) - 2) for p in mesh.polygons)


def analyse(entry):
    t0 = time.time()
    src = os.path.join(ROOT, entry["file"])
    name = os.path.splitext(os.path.basename(src))[0]
    work = os.path.join(WORK, name)
    if src.lower().endswith(".zip"):
        safe_extract(src, work)
        model = find_model(work)
    else:
        model = src
    res = {"file": entry["file"], "licence": entry.get("licence", ""), "author": entry.get("author", "")}
    if not model:
        res.update(mode="REJECT", reason="no importable model file (glb/gltf/fbx/obj) in the download")
        return res
    bpy.ops.wm.read_factory_settings(use_empty=True)
    axis = import_model(model)
    meshes = [o for o in bpy.data.objects if o.type == "MESH"]
    if not meshes:
        res.update(format=os.path.splitext(model)[1].lstrip("."), mode="REJECT", reason="no meshes")
        return res
    per = []
    for o in meshes:
        lo, hi = world_bounds([o])
        size = hi - lo
        per.append((o.name, tri_count(o.data), size, lo, hi))
    total = sum(p[1] for p in per)
    big = max(per, key=lambda p: p[1])
    lo, hi = world_bounds(meshes)
    ext = hi - lo
    images = list(bpy.data.images)
    missing = [i.name for i in images if not i.packed_file and i.source == "FILE" and i.filepath and not os.path.exists(bpy.path.abspath(i.filepath))]
    tall = [p for p in per if p[2].z > 3.0]
    flat_large = [p for p in per if p[2].z < 0.6 and max(p[2].x, p[2].y) > 30.0]
    footprint = max(ext.x, ext.y)
    # Unit scale: a city block up to ~2 km wide and ~500 m tall in metres; centimetre files come out 100x larger.
    if footprint > 20000 or ext.z > 5000:
        scale, why = 0.01, "extent > 20 km or height > 5 km: authored in centimetres"
    elif footprint < 2.0 and ext.z < 2.0:
        scale, why = 100.0, "whole model under 2 m: authored in hundredths (or a miniature)"
    else:
        scale, why = 1.0, "extent plausible in metres"
    city = footprint * scale > 150.0 and (len(flat_large) > 0 or big[1] > 0.3 * total)
    kind = "CITY" if city else "KIT"
    reasons = []
    if total > 5_000_000:
        reasons.append(f"{total:,} triangles (> 5 M)")
    if images and len(missing) > len(images) * 0.5:
        reasons.append(f"{len(missing)} of {len(images)} textures missing")
    mode = "REJECT" if reasons else "KITBASH"
    note = []
    if big[1] > 300_000:
        note.append(f"largest mesh {big[1]:,} tris: split by loose parts / material before use (BUG-003)")
    if kind == "CITY":
        note.append("a whole city scene: its layout will not match the Veyra road network; use only as a source of buildings and props")
    res.update({
        "model": os.path.relpath(model, WORK).replace("\\", "/") if model.startswith(WORK) else entry["file"],
        "format": os.path.splitext(model)[1].lstrip("."),
        "objects": len(bpy.data.objects), "meshes": len(meshes), "triangles": total,
        "largest_mesh": {"name": big[0], "triangles": big[1], "size_m": [round(big[2].x, 1), round(big[2].y, 1), round(big[2].z, 1)]},
        "extent_m": [round(ext.x, 1), round(ext.y, 1), round(ext.z, 1)],
        "materials": len(bpy.data.materials), "textures": len(images), "missing_textures": len(missing),
        "estimated_scale": scale, "scale_reason": why, "up_axis": "Z after import; " + axis,
        "tall_parts": len(tall), "flat_large_parts": len(flat_large), "kind": kind,
        "mode": mode, "reason": "; ".join(reasons) if reasons else "licence accepted; quality and scale usable as a kit",
        "notes": note, "seconds": round(time.time() - t0, 1),
    })
    return res


def main():
    with open(MANIFEST, encoding="utf-8") as f:
        man = json.load(f)
    items = [e for e in man["items"] if e.get("decision") == "ACCEPT"]
    out = {"date": time.strftime("%Y-%m-%d %H:%M"), "blender": bpy.app.version_string, "maps": [], "vehicles": []}
    if os.path.exists(OUT):
        try:
            with open(OUT, encoding="utf-8") as f:
                prev = json.load(f)
            out["maps"] = [m for m in prev.get("maps", []) if "triangles" in m or m.get("mode") == "REJECT"]
        except ValueError:
            pass
    done = {m["file"] for m in out["maps"]}
    for e in items:
        f = e["file"]
        if "/FullMapModels/" not in f:
            out["vehicles"].append({"file": f, "licence": e.get("licence", ""), "mode": "KIT", "note": "vehicle download: processed by card A8 into Assets/_Veyra/Art/Vehicles (not re-imported here)" + ("; real brand, rebadged" if e.get("real_brand") else "")})
            continue
        if e.get("kind") == "folder" or f in done or (ONLY and ONLY not in f):
            continue
        print(f"[triage] {f}", flush=True)
        try:
            res = analyse(e)
        except Exception as ex:  # keep going: one broken download must not stop the triage
            res = {"file": f, "mode": "REJECT", "reason": f"import failed: {type(ex).__name__}: {ex}"}
        print(f"[triage]   -> {res.get('mode')} {res.get('kind', '')} {res.get('triangles', '')} tris", flush=True)
        out["maps"].append(res)
        with open(OUT, "w", encoding="utf-8") as fo:
            json.dump(out, fo, indent=1)
    with open(OUT, "w", encoding="utf-8") as fo:
        json.dump(out, fo, indent=1)
    print(f"[triage] done: {len(out['maps'])} maps, {len(out['vehicles'])} vehicles -> {OUT}", flush=True)


main()

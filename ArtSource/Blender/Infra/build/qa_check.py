"""Self-check every built prop: transforms, LOD/collision naming, n-gons, normals, tri counts.

Checks the authored .blend files and then re-imports each exported FBX and compares.

    "D:\\New folder\\blender.exe" -b --factory-startup -P qa_check.py
"""
import glob
import json
import os
import sys

import bmesh
import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
INFRA = os.path.abspath(os.path.join(HERE, ".."))
EXPORT = os.path.join(INFRA, "export")
COL_PREFIXES = ("COL_", "UCX_", "UBX_")
# Project convention (VeyraModelPostprocessor): COL_<surface>_<name>, surface matching a
# Data/Surfaces asset filename, longest-first so dry_asphalt is not split on its underscore.
SURFACES = ("dry_asphalt", "wet_asphalt", "concrete", "gravel", "grass", "dirt")


def collision_name_issue(c, name):
    if c.startswith(("UCX_", "UBX_")):
        return None if c[4:] == name else "%s does not match the prop name" % c
    body = c[4:]
    for s in sorted(SURFACES, key=len, reverse=True):
        if body.startswith(s + "_"):
            rest = body[len(s) + 1:]
            return (None if rest.startswith(name)
                    else "%s: '%s' is not the prop name" % (c, rest))
    return ("%s: COL_ needs a surface token, COL_<surface>_<name> (valid: %s)"
            % (c, ", ".join(SURFACES)))


def shells(bm):
    seen, out = set(), []
    for f in bm.faces:
        if f.index in seen:
            continue
        stack, group = [f], []
        seen.add(f.index)
        while stack:
            cur = stack.pop()
            group.append(cur)
            for e in cur.edges:
                for nf in e.link_faces:
                    if nf.index not in seen:
                        seen.add(nf.index)
                        stack.append(nf)
        out.append(group)
    return out


def shell_volume(group):
    v = 0.0
    for f in group:
        vs = f.verts
        for i in range(1, len(vs) - 1):
            a, b, c = vs[0].co, vs[i].co, vs[i + 1].co
            v += a.dot(b.cross(c)) / 6.0
    return v


def inspect_mesh(ob):
    me = ob.data
    me.calc_loop_triangles()
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.faces.index_update()
    rep = dict(tris=len(me.loop_triangles),
               ngons=sum(1 for p in me.polygons if len(p.vertices) > 4),
               non_manifold=sum(1 for e in bm.edges if not e.is_manifold),
               loose_verts=sum(1 for v in bm.verts if not v.link_faces),
               degenerate=sum(1 for f in bm.faces if f.calc_area() < 1e-9),
               open_shells=0, inverted_shells=0, uv=bool(me.uv_layers),
               materials=[m.name if m else None for m in me.materials])
    for g in shells(bm):
        boundary = any(len(e.link_faces) == 1 for f in g for e in f.edges)
        if boundary:
            rep["open_shells"] += 1
        elif shell_volume(g) <= 0.0:
            rep["inverted_shells"] += 1
    bm.free()
    return rep


def check_blend(path):
    bpy.ops.wm.open_mainfile(filepath=path)
    name = os.path.splitext(os.path.basename(path))[0]
    issues, meshes = [], {}
    root = bpy.data.objects.get(name)
    if root is None or root.type != "EMPTY":
        issues.append("no root empty named %s" % name)
    else:
        if tuple(round(v, 6) for v in root.location) != (0.0, 0.0, 0.0):
            issues.append("root not at the origin: %s" % list(root.location))
        if any(abs(a) > 1e-6 for a in root.rotation_euler):
            issues.append("root rotation not applied")
        if any(abs(s - 1.0) > 1e-6 for s in root.scale):
            issues.append("root scale not 1")
    lods, cols = [], []
    for ob in bpy.data.objects:
        if ob.type != "MESH":
            continue
        if any(abs(v) > 1e-6 for v in ob.location) or \
           any(abs(a) > 1e-6 for a in ob.rotation_euler) or \
           any(abs(s - 1.0) > 1e-6 for s in ob.scale):
            issues.append("%s: transform not applied" % ob.name)
        if ob.parent is not root:
            issues.append("%s: not parented to the root empty" % ob.name)
        rep = inspect_mesh(ob)
        meshes[ob.name] = rep
        if rep["ngons"]:
            issues.append("%s: %d n-gons" % (ob.name, rep["ngons"]))
        if rep["inverted_shells"]:
            issues.append("%s: %d inverted shells" % (ob.name, rep["inverted_shells"]))
        if rep["open_shells"]:
            issues.append("%s: %d open shells" % (ob.name, rep["open_shells"]))
        if rep["degenerate"]:
            issues.append("%s: %d degenerate faces" % (ob.name, rep["degenerate"]))
        if rep["loose_verts"]:
            issues.append("%s: %d loose verts" % (ob.name, rep["loose_verts"]))
        if not rep["uv"]:
            issues.append("%s: no UV layer" % ob.name)
        if None in rep["materials"] or not rep["materials"]:
            issues.append("%s: missing material" % ob.name)
        if ob.name.startswith(COL_PREFIXES):
            cols.append(ob.name)
        elif "_LOD" in ob.name:
            lods.append(ob.name)
        else:
            issues.append("%s: neither an LOD nor a collision name" % ob.name)
    want = ["%s_LOD%d" % (name, i) for i in range(len(lods))]
    if sorted(lods) != sorted(want):
        issues.append("LOD naming: %s (expected %s)" % (sorted(lods), want))
    for c in cols:
        msg = collision_name_issue(c, name)
        if msg:
            issues.append(msg)
    return dict(name=name, issues=issues, meshes=meshes)


def check_fbx(name, blend_meshes):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=os.path.join(EXPORT, "%s.fbx" % name))
    issues = []
    found = {}
    for ob in bpy.data.objects:
        if ob.type == "MESH":
            ob.data.calc_loop_triangles()
            found[ob.name] = len(ob.data.loop_triangles)
    for mesh_name, rep in blend_meshes.items():
        if mesh_name not in found:
            issues.append("FBX is missing %s" % mesh_name)
        elif found[mesh_name] != rep["tris"]:
            issues.append("FBX %s has %d tris, blend has %d"
                          % (mesh_name, found[mesh_name], rep["tris"]))
    roots = [o for o in bpy.data.objects if o.parent is None]
    if len(roots) != 1:
        issues.append("FBX has %d roots (expected 1)" % len(roots))
    return issues


def main():
    out = []
    for path in sorted(glob.glob(os.path.join(INFRA, "*.blend"))):
        res = check_blend(path)
        res["issues"] += check_fbx(res["name"], res["meshes"])
        out.append(res)
        flag = "OK  " if not res["issues"] else "FAIL"
        print("[QA] %s %-32s %s" % (flag, res["name"], "; ".join(res["issues"])))
    with open(os.path.join(HERE, "qa_report.json"), "w") as fh:
        json.dump(out, fh, indent=1)
    bad = sum(1 for r in out if r["issues"])
    print("[QA] %d props, %d with issues" % (len(out), bad))


main()

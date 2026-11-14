"""Self-check the Veyra road-wear set: decal atlases and road-surface meshes.

    "D:\\New folder\\blender.exe" -b --factory-startup -P qa_check.py

Textures - read back from the written PNG files, not from the generator:
  power-of-two and square, correct channel count, alpha 0 along every cell border, decals
  actually reach a usable alpha, unit-length normals with +Z dominant, metallic channel 0,
  smoothness inside 0..1.
Meshes - as the Infra set, plus the CLAUDE.md physics rule:
  every collision surface within 0.05 m of the road plane (z = 0).
"""
import glob
import json
import os
import sys

import bmesh
import bpy
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)

import props_roadwear                      # noqa: E402  (puts Infra/build on sys.path)
import vc_tex                              # noqa: E402
import vc_tex2                             # noqa: E402
from vc_png import read_png                # noqa: E402

ROOT = os.path.abspath(os.path.join(HERE, ".."))
TEX = os.path.join(ROOT, "textures")
EXPORT = os.path.join(ROOT, "export")
COL_PREFIXES = ("COL_", "UCX_", "UBX_")
MAX_STEP = 0.05
# Props that deliberately ship no collider. A drive-over collider whose top face is exactly
# coplanar with the road would put the wheel raycast in a tie with the road collider, and the
# road already carries both the surface and the height (Docs/SURFACE_CONVENTION.md).
NO_COLLIDER = {"VC_ExpansionJoint_Strip_4m"}
# Project convention (VeyraModelPostprocessor): COL_<surface>_<name>, surface matching a
# Data/Surfaces asset filename, longest-first so dry_asphalt is not split on its underscore.
SURFACES = ("cobblestone", "concrete", "dirt", "dry_asphalt", "grass", "gravel", "kerb",
            "metal", "paving", "rock", "sand", "wet_asphalt")


def collision_name_issue(c, name):
    if c.startswith(("UCX_", "UBX_")):
        return None if c[4:] == name else "%s does not match the prop name" % c
    body = c[4:]
    for s_ in sorted(SURFACES, key=len, reverse=True):
        if body.startswith(s_ + "_"):
            rest = body[len(s_) + 1:]
            return None if rest.startswith(name) else "%s: '%s' is not the prop name" % (c, rest)
    return ("%s: COL_ needs a surface token, COL_<surface>_<name> (valid: %s)"
            % (c, ", ".join(SURFACES)))


# ------------------------------------------------------------------------------------ textures

def is_pow2(v):
    return v > 0 and (v & (v - 1)) == 0


def seam_ratio(a):
    """Wrap discontinuity measured against the texture's OWN internal transitions.

    The wrap is just one more adjacent line pair, so it is compared like for like: the mean
    absolute difference across the wrap, against the largest mean absolute difference between any
    adjacent interior line pair. A texture whose content has hard regular features (panel joints,
    for instance) legitimately has large internal steps, and a wrap that falls inside that range
    is indistinguishable from an ordinary internal feature.

    Comparing the wrap against the MEAN interior difference instead - the obvious first idea -
    fails any texture with hard edges, because the mean is dominated by flat areas. A genuinely
    non-tiling texture is still caught: for smooth content every interior line pair is tiny while
    the wrap is full content contrast (a 0-255 gradient scores ~1600 under this measure).

    Returns the ratio per the worst channel; <= 1 means the wrap is no worse than the texture's
    own worst internal transition.
    """
    f = a.astype(np.float64)
    worst = 0.0
    for c in range(f.shape[2] if f.ndim == 3 else 1):
        ch = f[:, :, c] if f.ndim == 3 else f
        cols = np.abs(np.diff(ch, axis=1)).mean(axis=0)
        rows = np.abs(np.diff(ch, axis=0)).mean(axis=1)
        inner = max(cols.max(), rows.max())
        edge = max(np.abs(ch[:, 0] - ch[:, -1]).mean(), np.abs(ch[0, :] - ch[-1, :]).mean())
        if edge > 0.25:                      # ignore channels that are flat or near-flat
            worst = max(worst, edge / (inner + 1e-9))
    return worst


def check_tiling():
    out = []
    for spec in vc_tex2.TILING:
        name, size, issues = spec["name"], spec["size"], []
        for suffix, chans in (("BC", 3), ("N", 3), ("MS", 4)):
            path = os.path.join(TEX, "%s_%s.png" % (name, suffix))
            if not os.path.exists(path):
                issues.append("%s missing" % os.path.basename(path))
                continue
            a = read_png(path)
            if a.shape[0] != size or a.shape[1] != size or not is_pow2(size):
                issues.append("%s is %dx%d, expected power-of-two %dx%d"
                              % (suffix, a.shape[1], a.shape[0], size, size))
            if a.shape[2] != chans:
                issues.append("%s has %d channels, expected %d" % (suffix, a.shape[2], chans))
            r = seam_ratio(a)
            if r > 1.25:
                issues.append("%s does not tile: the wrap step is %.1fx the texture's own worst "
                              "internal transition" % (suffix, r))
            if suffix == "MS":
                if a[:, :, 0].max() != 0:
                    issues.append("metallic channel is not 0 (max %d)" % a[:, :, 0].max())
                if a[:, :, 3].max() == 0:
                    issues.append("smoothness channel is empty")
            if suffix == "N":
                v = a.astype(np.float64) / 255.0 * 2.0 - 1.0
                ln = np.sqrt((v * v).sum(axis=2))
                if abs(ln.mean() - 1.0) > 0.02 or v[:, :, 2].min() <= 0.0:
                    issues.append("normal map not unit length / has non-positive Z")
        out.append(dict(name=name, issues=issues))
        print("[QA-TILE] %s %-20s %s" % ("OK  " if not issues else "FAIL", name,
                                         "; ".join(issues)))
    return out


def check_textures():
    out = []
    for atlas in vc_tex.ATLASES + vc_tex2.ATLASES2:
        name, size = atlas["name"], atlas["size"]
        issues = []
        maps = {}
        for suffix, chans in (("BC", 4), ("N", 3), ("MS", 4)):
            path = os.path.join(TEX, "%s_%s.png" % (name, suffix))
            if not os.path.exists(path):
                issues.append("%s missing" % os.path.basename(path))
                continue
            a = read_png(path)
            maps[suffix] = a
            if a.shape[0] != size or a.shape[1] != size:
                issues.append("%s is %dx%d, expected %dx%d"
                              % (suffix, a.shape[1], a.shape[0], size, size))
            if not (is_pow2(a.shape[0]) and is_pow2(a.shape[1])):
                issues.append("%s is not power-of-two" % suffix)
            if a.shape[2] != chans:
                issues.append("%s has %d channels, expected %d" % (suffix, a.shape[2], chans))
        n = size // 2
        if "BC" in maps:
            alpha = maps["BC"][:, :, 3].astype(np.int32)
            for i in range(4):
                cx, cy = (i % 2) * n, (i // 2) * n
                cell = alpha[cy:cy + n, cx:cx + n]
                border = max(cell[0, :].max(), cell[-1, :].max(),
                             cell[:, 0].max(), cell[:, -1].max())
                if border != 0:
                    issues.append("cell %d alpha does not reach 0 at the border (%d)" % (i, border))
                if cell.max() < 60:
                    issues.append("cell %d alpha peaks at only %d/255" % (i, cell.max()))
        if "N" in maps:
            v = maps["N"].astype(np.float64) / 255.0 * 2.0 - 1.0
            ln = np.sqrt((v * v).sum(axis=2))
            if abs(ln.mean() - 1.0) > 0.02 or ln.min() < 0.9:
                issues.append("normal map not unit length (mean %.3f, min %.3f)"
                              % (ln.mean(), ln.min()))
            if v[:, :, 2].min() <= 0.0:
                issues.append("normal map has non-positive Z (flipped tangent space?)")
        if "MS" in maps:
            ms = maps["MS"]
            if ms[:, :, 0].max() != 0:
                issues.append("metallic channel is not 0 (max %d)" % ms[:, :, 0].max())
            if ms[:, :, 3].max() == 0:
                issues.append("smoothness channel is empty")
        out.append(dict(name=name, issues=issues))
        print("[QA-TEX] %s %-16s %s" % ("OK  " if not issues else "FAIL", name, "; ".join(issues)))
    return out


# -------------------------------------------------------------------------------------- meshes

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
            v += vs[0].co.dot(vs[i].co.cross(vs[i + 1].co)) / 6.0
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
               max_z=max((v.co.z for v in me.vertices), default=0.0),
               materials=[m.name if m else None for m in me.materials])
    for g in shells(bm):
        if any(len(e.link_faces) == 1 for f in g for e in f.edges):
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
    elif (tuple(round(v, 6) for v in root.location) != (0.0, 0.0, 0.0)
          or any(abs(a) > 1e-6 for a in root.rotation_euler)
          or any(abs(s - 1.0) > 1e-6 for s in root.scale)):
        issues.append("root transform is not identity at the origin")
    lods, cols = [], []
    for ob in bpy.data.objects:
        if ob.type != "MESH":
            continue
        if (any(abs(v) > 1e-6 for v in ob.location) or any(abs(a) > 1e-6 for a in ob.rotation_euler)
                or any(abs(s - 1.0) > 1e-6 for s in ob.scale)):
            issues.append("%s: transform not applied" % ob.name)
        if ob.parent is not root:
            issues.append("%s: not parented to the root empty" % ob.name)
        rep = inspect_mesh(ob)
        meshes[ob.name] = rep
        for key, msg in (("ngons", "n-gons"), ("inverted_shells", "inverted shells"),
                         ("open_shells", "open shells"), ("degenerate", "degenerate faces"),
                         ("loose_verts", "loose verts")):
            if rep[key]:
                issues.append("%s: %d %s" % (ob.name, rep[key], msg))
        if not rep["uv"]:
            issues.append("%s: no UV layer" % ob.name)
        if None in rep["materials"] or not rep["materials"]:
            issues.append("%s: missing material" % ob.name)
        if ob.name.startswith(COL_PREFIXES):
            cols.append(ob.name)
            if rep["max_z"] > MAX_STEP + 1e-9:
                issues.append("%s: collider rises %.3f m above the road plane (max %.2f m)"
                              % (ob.name, rep["max_z"], MAX_STEP))
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
    if not cols and name not in NO_COLLIDER:
        issues.append("no collision mesh (every prop here is driven over)")
    return dict(name=name, issues=issues, meshes=meshes)


def check_fbx(name, blend_meshes):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=os.path.join(EXPORT, "%s.fbx" % name))
    issues, found = [], {}
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
    if len([o for o in bpy.data.objects if o.parent is None]) != 1:
        issues.append("FBX does not have exactly one root")
    return issues


def main():
    tex = check_textures() + check_tiling()
    meshes = []
    for path in sorted(glob.glob(os.path.join(ROOT, "*.blend"))):
        res = check_blend(path)
        res["issues"] += check_fbx(res["name"], res["meshes"])
        meshes.append(res)
        print("[QA-MESH] %s %-30s %s" % ("OK  " if not res["issues"] else "FAIL",
                                         res["name"], "; ".join(res["issues"])))
    with open(os.path.join(HERE, "qa_report.json"), "w") as fh:
        json.dump(dict(textures=tex, meshes=meshes), fh, indent=1)
    bad = sum(1 for r in tex + meshes if r["issues"])
    print("[QA] %d texture sets + %d meshes, %d with issues" % (len(tex), len(meshes), bad))


main()

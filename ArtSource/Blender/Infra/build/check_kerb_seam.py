"""Measure the vertical step where the split kerb colliders butt (CLAUDE.md: max 0.05 m).

The gutter solid (COL_dry_asphalt_*) and the kerb solid (COL_concrete_*) meet on the x = -0.450
plane. A wheel crossing that seam finds any error in it, so this measures the real built meshes
rather than trusting the profiles.

    "D:\\New folder\\blender.exe" -b --factory-startup -P check_kerb_seam.py
"""
import glob
import os

import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
INFRA = os.path.abspath(os.path.join(HERE, ".."))
SEAM_X = -0.450
TOL = 1e-5
MAX_STEP = 0.05


def coords(ob):
    return [ob.matrix_world @ v.co for v in ob.data.vertices]


def seam_pairs(pan, kerb, near=0.01):
    """Pair each gutter vertex with its nearest kerb vertex.

    The two solids are swept from profiles that share the toe and the buried bottom, so every
    seam vertex should have an EXACTLY coincident partner. Works for the straight sweeps and the
    arc alike, because it never assumes the seam is a plane.
    """
    a, b = coords(pan), coords(kerb)
    pairs = []
    for p in a:
        d, q = min(((p - q).length, q) for q in b)
        if d < near:
            pairs.append((p, q, d))
    return pairs


def main():
    worst = 0.0
    rows = []
    for path in sorted(glob.glob(os.path.join(INFRA, "*.blend"))):
        bpy.ops.wm.open_mainfile(filepath=path)
        name = os.path.splitext(os.path.basename(path))[0]
        pan = [o for o in bpy.data.objects
               if o.type == "MESH" and o.name.startswith("COL_dry_asphalt_")]
        kerb = [o for o in bpy.data.objects
                if o.type == "MESH" and o.name.startswith("COL_kerb_")]
        if not (pan and kerb):
            continue
        pairs = seam_pairs(pan[0], kerb[0])
        if not pairs:
            rows.append((name, None, "no coincident seam vertices - the solids do not meet"))
            continue
        step = max(abs(p.z - q.z) for p, q, _ in pairs)
        gap = max(d for _, _, d in pairs)
        worst = max(worst, step)
        rows.append((name, step, "%d coincident seam verts, max 3-D gap %.2e m" % (len(pairs), gap)))
    for name, step, note in rows:
        flag = "OK  " if step is not None and step <= MAX_STEP else "FAIL"
        print("[SEAM] %s %-28s %s  (%s)"
              % (flag, name, "%.6f m" % step if step is not None else "   n/a  ", note))
    print("[SEAM] worst vertical step %.6f m against a %.2f m limit" % (worst, MAX_STEP))


main()

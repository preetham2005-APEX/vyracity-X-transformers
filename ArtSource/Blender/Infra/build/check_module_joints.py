"""Do the kerb modules' END profiles match each other exactly?

A run mixes VC_Kerb_Straight_4m, VC_Kerb_DriveApron_4m and VC_Drain_Inlet_Kerb end to end. Each
piece's first and last cross-section should be the identical full-kerb profile, so every joint in
a mixed run is a zero step rather than a small one. This compares the actual built collider
vertices rather than trusting that the same function generated them.

    "D:\\New folder\\blender.exe" -b --factory-startup -P check_module_joints.py
"""
import os

import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
INFRA = os.path.abspath(os.path.join(HERE, ".."))
PIECES = {"VC_Kerb_Straight_4m": 4.00, "VC_Kerb_DriveApron_4m": 4.00, "VC_Drain_Inlet_Kerb": 1.20}
TOL = 1e-6


def end_profiles(name, length):
    """The (x, z) cross-section at y = 0 and at y = -length, across all COL_ meshes."""
    bpy.ops.wm.open_mainfile(filepath=os.path.join(INFRA, "%s.blend" % name))
    out = {0.0: set(), -length: set()}
    for ob in bpy.data.objects:
        if ob.type != "MESH" or not ob.name.startswith("COL_"):
            continue
        for v in ob.data.vertices:
            co = ob.matrix_world @ v.co
            for y in out:
                if abs(co.y - y) < 1e-5:
                    out[y].add((round(co.x, 6), round(co.z, 6)))
    return out[0.0], out[-length]


def main():
    prof = {}
    for name, length in PIECES.items():
        start, end = end_profiles(name, length)
        prof[name] = (start, end)
        print("[JOINT] %-26s start %2d pts, end %2d pts, %s"
              % (name, len(start), len(end),
                 "start == end" if start == end else "START AND END DIFFER"))
    names = list(PIECES)
    ref = prof[names[0]][1]
    worst = 0.0
    for other in names[1:]:
        a, b = ref, prof[other][0]
        if a == b:
            print("[JOINT] %s -> %s : IDENTICAL profile, zero step" % (names[0], other))
            continue
        # report the largest height difference between nearest counterparts
        for (x, z) in a:
            d, zz = min((abs(x - x2), z2) for (x2, z2) in b)
            if d < 0.001:
                worst = max(worst, abs(z - zz))
        print("[JOINT] %s -> %s : profiles differ, worst height delta %.6f m"
              % (names[0], other, worst))
    print("[JOINT] worst joint step across a mixed run: %.6f m" % worst)


main()

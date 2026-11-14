"""How much of each drive-over collider is COPLANAR with the road plane (z = 0)?

A coplanar collider is in a raycast tie with the road collider underneath it, so it must not
carry a surface that differs from the ground (Docs/SURFACE_CONVENTION.md). A collider that stands
proud is not in a tie: the nearest hit wins, and the tyre really is on the casting.

This measures it rather than assuming either way.

    "D:\\New folder\\blender.exe" -b --factory-startup -P check_coplanar.py
"""
import glob
import os

import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, ".."))
FLAT = 0.0005      # within 0.5 mm of the road plane counts as coplanar
PROUD = 0.002      # 2 mm or more above it counts as standing proud


def main():
    print("%-30s %9s %9s %9s  %s" % ("collider", "coplanar", "proud", "top", "verdict"))
    for path in sorted(glob.glob(os.path.join(ROOT, "*.blend"))):
        bpy.ops.wm.open_mainfile(filepath=path)
        for ob in bpy.data.objects:
            if ob.type != "MESH" or not ob.name.startswith("COL_"):
                continue
            me = ob.data
            me.calc_loop_triangles()
            copl = proud = 0.0
            top = -1e9
            for t in me.loop_triangles:
                zs = [(ob.matrix_world @ me.vertices[i].co).z for i in t.vertices]
                top = max(top, max(zs))
                if max(abs(z) for z in zs) <= FLAT:
                    copl += t.area
                elif min(zs) >= PROUD:
                    proud += t.area
            total = copl + proud
            if total <= 1e-9:
                verdict = "no horizontal drive surface"
            elif copl <= 1e-6:
                verdict = "PROUD - no tie, may carry its own surface"
            elif proud <= 1e-6:
                verdict = "COPLANAR - in a tie, must match the ground"
            else:
                verdict = "MIXED - %.0f%% coplanar" % (100.0 * copl / total)
            print("%-30s %8.3f~ %8.3f~ %+8.4f  %s"
                  % (ob.name[:30], copl, proud, top, verdict))


main()

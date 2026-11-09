"""Card S4 axis round-trip test: builds VC_AXIS_TEST and exports it with the VC_Unity_Prop and VC_Unity_World presets.

Headless: blender.exe -b --factory-startup -P Tools/Blender/vc_axis_test.py -- --out <Assets/_Veyra/Art/Models/_Test> [--blend <file.blend>]
MCP:      import sys; sys.path.insert(0, r"E:\\VeyraCity\\Tools\\Blender"); import vc_axis_test; vc_axis_test.build(); vc_axis_test.export(out_dir)

Scene (Blender, Z up, metres):
  VC_AXIS_BOX     empty at the origin with VC_AXIS_BOX_LOD0/1/2: a 10 (x) x 4 (y) x 3 (z) m box, base centre at the origin
  VC_AXIS_N       20 m arrow along +Y (shaft + cone) with a flat "N" label beyond the tip
  VC_AXIS_E       10 m arrow along +X with a flat "E" label
  VC_AXIS_MARKER  2 m cube centred at (100, 200, 5)
Files: VC_AxisTest_Prop.fbx (prop preset; box + arrows), VC_AxisTest_World.fbx (world preset; everything) and, as evidence
that axis_forward does not change the result once Unity bakes the axis conversion, VC_AxisTest_World_forwardZ.fbx (world mode
with axis_forward 'Z' instead of the preset's '-Z').
Expected in Unity: World (both files): N -> +Z, E -> +X, marker at (100, 5, 200). Prop: the model's front (Blender -Y) faces +Z,
so N -> -Z, E -> -X.
"""
import math
import os
import sys

import bpy

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import vc_tools  # noqa: E402

COLLECTION = "VC_AXIS_TEST"


def _clear():
    for o in list(bpy.data.objects):
        if o.name.startswith("VC_AXIS"):
            bpy.data.objects.remove(o, do_unlink=True)


def _collection():
    vc_tools.vc_setup_scene()
    col = bpy.data.collections.get(COLLECTION) or bpy.data.collections.new(COLLECTION)
    export = bpy.data.collections["EXPORT"]
    if col.name not in export.children:
        export.children.link(col)
    return col


def _link_only(obj, col):
    for c in list(obj.users_collection):
        c.objects.unlink(obj)
    col.objects.link(obj)


def _active():
    return bpy.context.view_layer.objects.active


def _arrow(name, length, axis, label):
    """Shaft from the origin, cone at the tip, flat text label beyond it; one mesh with its origin at the world origin."""
    head = 2.5
    d = (1.0, 0.0, 0.0) if axis == "X" else (0.0, 1.0, 0.0)
    shaft_len = length - head
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(d[0] * shaft_len / 2, d[1] * shaft_len / 2, 0.3))
    shaft = _active()
    shaft.scale = (shaft_len if axis == "X" else 0.6, shaft_len if axis == "Y" else 0.6, 0.6)
    rot = (0.0, math.radians(90), 0.0) if axis == "X" else (math.radians(-90), 0.0, 0.0)
    bpy.ops.mesh.primitive_cone_add(radius1=1.2, depth=head, location=(d[0] * (shaft_len + head / 2), d[1] * (shaft_len + head / 2), 0.3), rotation=rot)
    cone = _active()
    bpy.ops.object.text_add(location=(d[0] * (length + 2.0) - (1.2 if axis == "Y" else 0.0), d[1] * (length + 2.0) - (1.4 if axis == "X" else 0.0), 0.3))
    txt = _active()
    txt.data.body = label
    txt.data.size = 4.0
    txt.data.extrude = 0.15
    with bpy.context.temp_override(object=txt, active_object=txt, selected_objects=[txt], selected_editable_objects=[txt]):
        bpy.ops.object.convert(target="MESH")
    parts = [shaft, cone, txt]
    for p in parts:
        with bpy.context.temp_override(object=p, active_object=p, selected_objects=[p], selected_editable_objects=[p]):
            bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    with bpy.context.temp_override(object=shaft, active_object=shaft, selected_objects=parts, selected_editable_objects=parts):
        bpy.ops.object.join()
    shaft.name = name
    shaft.data.name = name
    bpy.context.scene.cursor.location = (0.0, 0.0, 0.0)
    with bpy.context.temp_override(object=shaft, active_object=shaft, selected_objects=[shaft], selected_editable_objects=[shaft]):
        bpy.ops.object.origin_set(type="ORIGIN_CURSOR")
    return shaft


def build():
    """Create VC_AXIS_TEST in the current file (replaces an earlier one). Returns the objects by role."""
    _clear()
    col = _collection()
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0.0, 0.0, 1.5))
    box = _active()
    box.scale = (10.0, 4.0, 3.0)
    with bpy.context.temp_override(object=box, active_object=box, selected_objects=[box], selected_editable_objects=[box]):
        bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
        bpy.ops.object.mode_set(mode="EDIT")
        bpy.ops.mesh.select_all(action="SELECT")
        bpy.ops.mesh.subdivide(number_cuts=4)
        bpy.ops.object.mode_set(mode="OBJECT")
    box.name = "VC_AXIS_BOX"
    vc_tools.vc_set_pivot(box, "BASE_CENTER")
    parent, lods = vc_tools.vc_make_lods(box, (1.0, 0.4, 0.1))
    north = _arrow("VC_AXIS_N", 20.0, "Y", "N")
    east = _arrow("VC_AXIS_E", 10.0, "X", "E")
    bpy.ops.mesh.primitive_cube_add(size=2.0, location=(100.0, 200.0, 5.0))
    marker = _active()
    marker.name = "VC_AXIS_MARKER"
    for o in [parent] + lods + [north, east, marker]:
        _link_only(o, col)
    return {"box": parent, "lods": lods, "north": north, "east": east, "marker": marker}


def export(out_dir):
    """Export the three test files; returns a report string."""
    objs = {o.name: o for o in bpy.data.objects if o.name.startswith("VC_AXIS")}
    box, north, east, marker = objs["VC_AXIS_BOX"], objs["VC_AXIS_N"], objs["VC_AXIS_E"], objs["VC_AXIS_MARKER"]
    os.makedirs(out_dir, exist_ok=True)
    lines = []
    a = vc_tools.vc_export_fbx([box, north, east], os.path.join(out_dir, "VC_AxisTest_Prop.fbx"), mode="prop", pivot=box)
    lines.append("VC_AxisTest_Prop.fbx       prop  axis_forward=%s axis_up=%s" % (a["axis_forward"], a["axis_up"]))
    a = vc_tools.vc_export_fbx([box, north, east, marker], os.path.join(out_dir, "VC_AxisTest_World.fbx"), mode="world")
    lines.append("VC_AxisTest_World.fbx      world axis_forward=%s axis_up=%s" % (a["axis_forward"], a["axis_up"]))
    a = vc_tools.vc_export_fbx([box, north, east, marker], os.path.join(out_dir, "VC_AxisTest_World_forwardZ.fbx"), mode="world", axis_forward="Z")
    lines.append("VC_AxisTest_World_forwardZ.fbx world axis_forward=%s axis_up=%s (evidence: axis_forward has no effect)" % (a["axis_forward"], a["axis_up"]))
    for o in [north, east, marker] + list(box.children):
        c = [o.matrix_world @ v for v in [__import__("mathutils").Vector(b) for b in o.bound_box]]
        lo = [min(p[i] for p in c) for i in range(3)]
        hi = [max(p[i] for p in c) for i in range(3)]
        lines.append("%-16s blender bounds min (%.1f, %.1f, %.1f) max (%.1f, %.1f, %.1f) tris %d" % (o.name, lo[0], lo[1], lo[2], hi[0], hi[1], hi[2], vc_tools.vc_tri_count([o])))
    return "\n".join(lines)


def _argv():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    opts = {"out": None, "blend": None}
    for i, a in enumerate(argv):
        if a == "--out" and i + 1 < len(argv):
            opts["out"] = argv[i + 1]
        if a == "--blend" and i + 1 < len(argv):
            opts["blend"] = argv[i + 1]
    return opts


if __name__ == "__main__":
    o = _argv()
    if not o["out"]:
        raise SystemExit("usage: blender -b --factory-startup -P vc_axis_test.py -- --out <dir> [--blend <file>]")
    for ob in list(bpy.data.objects):  # factory startup cube, light, camera
        bpy.data.objects.remove(ob, do_unlink=True)
    build()
    print("VC_AXIS_REPORT\n" + export(o["out"]))
    if o["blend"]:
        os.makedirs(os.path.dirname(o["blend"]), exist_ok=True)
        bpy.ops.wm.save_as_mainfile(filepath=o["blend"])
        print("VC_AXIS_REPORT saved", o["blend"])

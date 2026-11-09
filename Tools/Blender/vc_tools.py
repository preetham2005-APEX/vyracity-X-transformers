"""VEYRA CITY Blender helpers (playbook card S4). Import in an MCP session or a headless run:

    import sys; sys.path.insert(0, r"E:\\VeyraCity\\Tools\\Blender"); import vc_tools

Export presets (FBX, Blender 5.2), confirmed by the S4 axis round-trip test (Logs/S4/axis_test.txt; CLAUDE.md "Blender -> Unity"):
    VC_Unity_World - mode "world": world locations kept. Blender (x, y, z) -> Unity (x, z, y): +X east -> +X, +Y north -> +Z.
                     A marker at Blender (100, 200, 5) lands at Unity (100, 5, 200). Same as the Veyra data convention, so a
                     model authored in data coordinates minus (4000, 3000) lands at Unity = (x - 4000, z, y - 3000).
    VC_Unity_Prop  - mode "prop": the pivot object is moved to the origin AND turned 180 deg about Z for the export, so a
                     model's front (Blender -Y, Blender's front view) faces Unity +Z. Unity shows the file's top objects with
                     rotation (0, 180, 0).
Both use axis_forward '-Z', axis_up 'Y', bake_space_transform True, with Unity importing at Scale Factor 1, Convert Units
on and Bake Axis Conversion on. Measured: with Bake Axis Conversion on, Unity reads the axis system written in the FBX and
undoes it, so axis_forward '-Z' and 'Z' give identical results; the 180 deg turn is the only thing that changes a prop's
facing. LODs: Unity puts ONE LODGroup on the FBX root and puts every *_LOD<n> mesh of the file into level n; meshes without
a suffix always draw, so export one LOD set (one prop, one chunk) per file.
Never export world-positioned content with settings other than VC_Unity_World.

Units: metric, unit scale 1.0, metres (1 Blender unit = 1 m). Pivots: buildings and props at base centre, road modules at
the start of the piece, world chunks at the chunk's south-west corner at height 0. LOD names <name>_LOD0/_LOD1/_LOD2;
collision COL_ (non-convex), UCX_ (convex), UBX_ (box).
"""
import math

import bmesh
import bpy
from mathutils import Matrix, Vector

COLLECTIONS = ("EXPORT", "LOD0", "LOD1", "LOD2", "COLLISION", "REFERENCE")

# Confirmed by the S4 axis round-trip test (Logs/S4/axis_test.txt).
PRESETS = {
    "prop": {"axis_forward": "-Z", "axis_up": "Y"},   # VC_Unity_Prop  (plus the 180 deg turn about Z, PROP_TURN_DEG)
    "world": {"axis_forward": "-Z", "axis_up": "Y"},  # VC_Unity_World (no turn)
}
PROP_TURN_DEG = 180.0

BASE_ARGS = dict(
    use_selection=True,
    global_scale=1.0,
    apply_unit_scale=True,
    apply_scale_options="FBX_SCALE_UNITS",
    bake_space_transform=True,
    object_types={"MESH", "EMPTY"},
    mesh_smooth_type="FACE",
    use_mesh_modifiers=True,
    add_leaf_bones=False,
    path_mode="COPY",
    embed_textures=False,
    bake_anim=False,
)


def vc_setup_scene():
    """Metric units, unit scale 1.0, metres; creates the standard collections. Returns the collections by name."""
    units = bpy.context.scene.unit_settings
    units.system = "METRIC"
    units.scale_length = 1.0
    units.length_unit = "METERS"
    root = bpy.context.scene.collection
    out = {}
    for name in COLLECTIONS:
        col = bpy.data.collections.get(name) or bpy.data.collections.new(name)
        if col.name not in root.children:
            root.children.link(col)
        out[name] = col
    return out


def vc_move_to(obj, collection_name):
    """Link obj only to the named standard collection."""
    col = vc_setup_scene()[collection_name]
    for c in list(obj.users_collection):
        c.objects.unlink(obj)
    col.objects.link(obj)


def _with_children(objects):
    seen, out = set(), []
    for o in objects:
        for x in [o] + list(o.children_recursive):
            if x.name not in seen:
                seen.add(x.name)
                out.append(x)
    return out


def vc_export_fbx(objects, path, mode="prop", axis_forward=None, axis_up=None, pivot=None):
    """Export objects (and their children) to an FBX file.

    mode "prop": the pivot object (default: the first object) is moved to the world origin and turned PROP_TURN_DEG about
    Z for the export, then every object gets its exact original transform back; the file has a local pivot and the model's
    front (Blender -Y) faces Unity +Z. mode "world": world locations and orientation are kept (Blender +Y -> Unity +Z).
    axis_forward / axis_up override the preset (used only by the S4 round-trip test). Returns the export arguments used.
    """
    if mode not in PRESETS:
        raise ValueError("mode must be 'prop' or 'world'")
    objects = [o for o in objects if o is not None]
    if not objects:
        raise ValueError("nothing to export")
    args = dict(BASE_ARGS)
    args.update(PRESETS[mode])
    if axis_forward:
        args["axis_forward"] = axis_forward
    if axis_up:
        args["axis_up"] = axis_up

    view_layer = bpy.context.view_layer
    prev_sel = [o for o in view_layer.objects if o.select_get()]
    prev_active = view_layer.objects.active
    roots = [o for o in objects if o.parent is None or o.parent not in objects]
    saved = {r.name: r.matrix_world.copy() for r in roots}
    if mode == "prop":
        pivot = pivot or objects[0]
        to_origin = Matrix.Translation(-pivot.matrix_world.translation)
        turn = Matrix.Rotation(math.radians(PROP_TURN_DEG), 4, "Z")
        for r in roots:
            r.matrix_world = turn @ to_origin @ r.matrix_world
    try:
        for o in view_layer.objects:
            o.select_set(False)
        for o in _with_children(objects):
            o.hide_set(False)
            o.select_set(True)
        view_layer.objects.active = objects[0]
        bpy.ops.export_scene.fbx(filepath=path, **args)
    finally:
        for r in roots:
            r.matrix_world = saved[r.name]
        for o in view_layer.objects:
            o.select_set(o in prev_sel)
        view_layer.objects.active = prev_active
    return args


def vc_tri_count(objs):
    """Triangles of the evaluated meshes (modifiers applied)."""
    deps = bpy.context.evaluated_depsgraph_get()
    total = 0
    for o in objs:
        if o.type != "MESH":
            continue
        ev = o.evaluated_get(deps)
        me = ev.to_mesh()
        me.calc_loop_triangles()
        total += len(me.loop_triangles)
        ev.to_mesh_clear()
    return total


def vc_make_lods(obj, ratios=(1.0, 0.4, 0.1)):
    """Make <base>_LOD0.._LODn copies of obj with a collapse Decimate at each ratio (applied), parented to an empty <base>
    so Unity's importer builds one LODGroup. obj itself becomes LOD0. Returns (parent_empty, [lods])."""
    base = obj.name[:-5] if obj.name.endswith("_LOD0") else obj.name
    parent = bpy.data.objects.get(base) if bpy.data.objects.get(base) is not obj else None
    if parent is None:
        parent = bpy.data.objects.new(base + "_tmp", None)
        bpy.context.scene.collection.objects.link(parent)
        parent.matrix_world = obj.matrix_world.copy()
        obj.name = base + "_LOD0"
        parent.name = base
    else:
        obj.name = base + "_LOD0"
    obj.parent = parent
    obj.matrix_parent_inverse = parent.matrix_world.inverted()
    lods = [obj]
    for i, r in enumerate(ratios[1:], start=1):
        name = f"{base}_LOD{i}"
        old = bpy.data.objects.get(name)
        if old is not None:
            bpy.data.objects.remove(old, do_unlink=True)
        dup = obj.copy()
        dup.data = obj.data.copy()
        dup.name = name
        for c in obj.users_collection:
            c.objects.link(dup)
        dup.parent = parent
        dup.matrix_parent_inverse = obj.matrix_parent_inverse.copy()
        if r < 1.0:
            mod = dup.modifiers.new("VC_Decimate", "DECIMATE")
            mod.decimate_type = "COLLAPSE"
            mod.ratio = r
            with bpy.context.temp_override(object=dup, active_object=dup, selected_objects=[dup]):
                bpy.ops.object.modifier_apply(modifier=mod.name)
        lods.append(dup)
    return parent, lods


def vc_apply_and_check(obj, apply_location=False):
    """Apply rotation and scale (location only when asked: never on wheels, calipers or sockets), recalculate normals
    outwards, and report problems. Returns a dict with non_manifold_edges, zero_area_faces, tris."""
    with bpy.context.temp_override(object=obj, active_object=obj, selected_objects=[obj], selected_editable_objects=[obj]):
        bpy.ops.object.transform_apply(location=apply_location, rotation=True, scale=True)
    report = {"object": obj.name, "non_manifold_edges": 0, "zero_area_faces": 0, "tris": 0}
    if obj.type != "MESH":
        return report
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    report["non_manifold_edges"] = sum(1 for e in bm.edges if not e.is_manifold)
    report["zero_area_faces"] = sum(1 for f in bm.faces if f.calc_area() < 1e-8)
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()
    report["tris"] = vc_tri_count([obj])
    return report


def vc_set_pivot(obj, where="BASE_CENTER"):
    """Move obj's origin without moving its geometry: BASE_CENTER (bounding-box centre in x/y, lowest z: buildings, props),
    MIN_CORNER (lowest x, y, z) or SW_GROUND (lowest x and y at z = 0: world chunks)."""
    corners = [obj.matrix_world @ Vector(c) for c in obj.bound_box]
    lo = Vector((min(c.x for c in corners), min(c.y for c in corners), min(c.z for c in corners)))
    hi = Vector((max(c.x for c in corners), max(c.y for c in corners), max(c.z for c in corners)))
    if where == "BASE_CENTER":
        target = Vector(((lo.x + hi.x) / 2, (lo.y + hi.y) / 2, lo.z))
    elif where == "MIN_CORNER":
        target = lo
    elif where == "SW_GROUND":
        target = Vector((lo.x, lo.y, 0.0))
    else:
        raise ValueError("where must be BASE_CENTER, MIN_CORNER or SW_GROUND")
    scene = bpy.context.scene
    saved = scene.cursor.location.copy()
    scene.cursor.location = target
    with bpy.context.temp_override(object=obj, active_object=obj, selected_objects=[obj], selected_editable_objects=[obj]):
        bpy.ops.object.origin_set(type="ORIGIN_CURSOR")
    scene.cursor.location = saved
    return target

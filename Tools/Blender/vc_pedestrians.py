"""Original rigged pedestrians for Veyra City's GPU crowd (foliage/peds session, 2026-11-14).

Headless:
  set TEMP=E:\\Temp & "D:/New folder/blender.exe" -b --factory-startup -P Tools/Blender/vc_pedestrians.py -- --out E:/VeyraCity

Builds three body variants from seeds (no downloaded content, nothing to license):
  A  casual man (short hair), B  woman (long hair, slimmer build), C  heavy-set man in a long coat with a cap.
Each variant: a 21-bone humanoid armature, a skin-modifier body (LOD0 subdivided, LOD1 ~40 %, LOD2 ~10 % triangles),
heat-weighted skinning, an umbrella skinned to Hand.R, and five looping clips: Walk, WalkUmbrella, Run, Idle, IdleUmbrella.

Writes
  Assets/_Veyra/Art/Models/Characters/VC_Ped_{A,B,C}.fbx   rigged source (VC_Unity_Prop preset + armature + actions)
  ArtSource/Blender/Pedestrians/vc_peds_crowd.json         crowd bake: per variant and LOD the mesh in Unity mesh space
      (positions, normals, triangles, region/AO, 4 bone indices + weights) and per clip frame the bone skinning matrices
      (Unity space, 3 rows each). Assets/_Veyra/Editor/PedestrianCrowdBuilder.cs turns it into meshes + a bone texture.
  ArtSource/Blender/Pedestrians/VC_Pedestrians.blend
  Logs/Peds/peds.txt                                       triangles per LOD, vertex counts, clip table
  Screenshots/<date>_peds/blender_lineup.png               (with --render)

Unity mesh space: Blender (x, y, z) -> Unity (-x, z, -y): the VC_Unity_Prop mapping (180 deg about Z, then Z-up -> Y-up),
so the figure's front (Blender -Y) faces Unity +Z and its left (Blender +X) is Unity -X. Triangle winding is reversed
for the handedness change.

Vertex regions (uv0.x), coloured per instance by Veyra/Crowd:
  0 skin, 1 shirt/coat, 2 forearm (sleeve or skin), 3 trousers, 4 shin (trousers or skin), 5 shoes/umbrella shaft,
  6 hair, 7 accent (umbrella canopy, cap).
"""
import json
import math
import os
import random
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree
from mathutils.kdtree import KDTree

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import vc_tools  # noqa: E402


def arg(name, default):
    a = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    return a[a.index(name) + 1] if name in a and a.index(name) + 1 < len(a) else default


ROOT = arg("--out", "E:/VeyraCity")
RENDER = "--render" in sys.argv
MODEL_DIR = os.path.join(ROOT, "Assets/_Veyra/Art/Models/Characters")
BAKE_DIR = os.path.join(ROOT, "ArtSource/Blender/Pedestrians")
LOG = os.path.join(ROOT, "Logs/Peds/peds.txt")
SHOT_DIR = os.path.join(ROOT, arg("--shots", "Screenshots/2026-11-14_peds"))

# Blender -> Unity mesh space (VC_Unity_Prop): (x, y, z) -> (-x, z, -y)
C = Matrix(((-1, 0, 0, 0), (0, 0, 1, 0), (0, -1, 0, 0), (0, 0, 0, 1)))
C_INV = C.inverted()

BONES = [  # name, parent, head joint, tail joint
    ("Hips", None, "pelvis", "spine"),
    ("Spine", "Hips", "spine", "chest"),
    ("Chest", "Spine", "chest", "neck0"),
    ("Neck", "Chest", "neck0", "head0"),
    ("Head", "Neck", "head0", "headtop"),
]
for s in ("L", "R"):
    BONES += [
        (f"Shoulder.{s}", "Chest", f"clav.{s}", f"shoulder.{s}"),
        (f"UpperArm.{s}", f"Shoulder.{s}", f"shoulder.{s}", f"elbow.{s}"),
        (f"LowerArm.{s}", f"UpperArm.{s}", f"elbow.{s}", f"wrist.{s}"),
        (f"Hand.{s}", f"LowerArm.{s}", f"wrist.{s}", f"hand.{s}"),
        (f"UpperLeg.{s}", "Hips", f"hip.{s}", f"knee.{s}"),
        (f"LowerLeg.{s}", f"UpperLeg.{s}", f"knee.{s}", f"ankle.{s}"),
        (f"Foot.{s}", f"LowerLeg.{s}", f"ankle.{s}", f"ball.{s}"),
        (f"Toes.{s}", f"Foot.{s}", f"ball.{s}", f"toe.{s}"),
    ]
BONE_NAMES = [b[0] for b in BONES]

# clips: name, frames sampled per loop, metres travelled per loop at height scale 1 (0 = in place)
CLIPS = [("Walk", 24, 1.50), ("WalkUmbrella", 24, 1.40), ("Run", 16, 2.60), ("Idle", 32, 0.0), ("IdleUmbrella", 32, 0.0)]
IDLE_SECONDS = 6.0

VARIANTS = {
    # w: width scale of the torso, sh: shoulder half width, hip: hip joint x, h: height scale, coat: z where the coat ends
    "A": dict(w=1.0, depth=1.0, sh=0.185, hip=0.092, h=1.0, arm=1.0, leg=1.0, belt=0.95, coat=None, hair="short", cap=False, seed=3),
    "B": dict(w=0.9, depth=0.95, sh=0.163, hip=0.098, h=0.955, arm=0.88, leg=0.92, belt=0.97, coat=None, hair="long", cap=False, seed=5),
    "C": dict(w=1.16, depth=1.22, sh=0.195, hip=0.096, h=1.02, arm=1.12, leg=1.08, belt=0.95, coat=0.60, hair="short", cap=True, seed=7),
}


# ------------------------------------------------------------------------------------------------------------- joints

def joints(v):
    """Joint positions (Blender, metres, front -Y, left +X) for a variant before its height scale."""
    sh, hp = v["sh"], v["hip"]
    j = {
        "pelvis": (0, 0.0, 0.95), "spine": (0, 0.005, 1.10), "chest": (0, 0.0, 1.27), "neck0": (0, 0.01, 1.47),
        "head0": (0, 0.0, 1.56), "headtop": (0, -0.005, 1.76),
    }
    for s, x in (("L", 1), ("R", -1)):
        j[f"clav.{s}"] = (x * 0.03, 0.0, 1.43)
        j[f"shoulder.{s}"] = (x * sh, 0.01, 1.425)
        j[f"elbow.{s}"] = (x * (sh + 0.045), 0.025, 1.145)
        j[f"wrist.{s}"] = (x * (sh + 0.065), -0.0, 0.895)
        j[f"hand.{s}"] = (x * (sh + 0.07), -0.015, 0.79)
        j[f"hip.{s}"] = (x * hp, 0.0, 0.92)
        j[f"knee.{s}"] = (x * (hp + 0.008), -0.012, 0.515)
        j[f"ankle.{s}"] = (x * (hp + 0.012), 0.025, 0.088)
        j[f"ball.{s}"] = (x * (hp + 0.014), -0.105, 0.022)
        j[f"toe.{s}"] = (x * (hp + 0.016), -0.165, 0.02)
    h = v["h"]
    return {k: Vector(p) * h for k, p in j.items()}


# --------------------------------------------------------------------------------------------------------------- body

def skin_body(name, v, J):
    """Skin-modifier body over a joint graph; returns the object with the modifier applied (quad cage)."""
    w, d, a, lg = v["w"], v["depth"], v["arm"], v["leg"]
    h = v["h"]
    nodes = {  # name: (position, (rx, ry))
        "pelvis": (J["pelvis"], (0.155 * w, 0.105 * d)),
        "belly": ((J["pelvis"] + J["chest"]) * 0.5 + Vector((0, -0.006, 0)), (0.142 * w, 0.10 * d)),
        "chest": (J["chest"] + Vector((0, -0.008, 0)), (0.165 * w, 0.105 * d)),
        "upchest": (Vector((0, 0.005, 1.40 * h)), (0.155 * w, 0.092 * d)),
        "neck": (Vector((0, 0.012, 1.505 * h)), (0.052, 0.055)),
        "jaw": (Vector((0, -0.012, 1.575 * h)), (0.066, 0.075)),
        "head": (Vector((0, -0.002, 1.645 * h)), (0.088, 0.104)),
        "crown": (Vector((0, 0.002, 1.715 * h)), (0.07, 0.082)),
    }
    edges = [("pelvis", "belly"), ("belly", "chest"), ("chest", "upchest"), ("upchest", "neck"), ("neck", "jaw"),
             ("jaw", "head"), ("head", "crown")]
    for s in ("L", "R"):
        nodes[f"clav.{s}"] = (Vector((J[f"shoulder.{s}"].x * 0.5, 0.004, 1.415 * h)), (0.062 * w, 0.052 * d))
        nodes[f"sh.{s}"] = (J[f"shoulder.{s}"] + Vector((0, 0, -0.012)), (0.058 * a, 0.058 * a))
        nodes[f"el.{s}"] = (J[f"elbow.{s}"], (0.041 * a, 0.041 * a))
        nodes[f"wr.{s}"] = (J[f"wrist.{s}"], (0.03 * a, 0.026 * a))
        nodes[f"palm.{s}"] = ((J[f"wrist.{s}"] + J[f"hand.{s}"]) * 0.5, (0.036, 0.022))
        nodes[f"fing.{s}"] = (J[f"hand.{s}"], (0.028, 0.016))
        nodes[f"hip.{s}"] = (J[f"hip.{s}"] + Vector((0, 0, -0.02)), (0.084 * lg, 0.088 * lg))
        nodes[f"thigh.{s}"] = ((J[f"hip.{s}"] * 0.45 + J[f"knee.{s}"] * 0.55), (0.066 * lg, 0.07 * lg))
        nodes[f"kn.{s}"] = (J[f"knee.{s}"], (0.05 * lg, 0.052 * lg))
        nodes[f"calf.{s}"] = ((J[f"knee.{s}"] * 0.6 + J[f"ankle.{s}"] * 0.4) + Vector((0, 0.01, 0)), (0.05 * lg, 0.055 * lg))
        nodes[f"an.{s}"] = (J[f"ankle.{s}"] + Vector((0, 0, 0.01)), (0.036, 0.038))
        nodes[f"heel.{s}"] = (J[f"ankle.{s}"] + Vector((0, 0.01, -0.045)), (0.04, 0.035))
        nodes[f"ball.{s}"] = (J[f"ball.{s}"] + Vector((0, 0, 0.012)), (0.045, 0.03))
        nodes[f"toe.{s}"] = (J[f"toe.{s}"] + Vector((0, 0, 0.014)), (0.038, 0.024))
        edges += [("upchest", f"clav.{s}"), (f"clav.{s}", f"sh.{s}"), (f"sh.{s}", f"el.{s}"), (f"el.{s}", f"wr.{s}"), (f"wr.{s}", f"palm.{s}"),
                  (f"palm.{s}", f"fing.{s}"), ("pelvis", f"hip.{s}"), (f"hip.{s}", f"thigh.{s}"),
                  (f"thigh.{s}", f"kn.{s}"), (f"kn.{s}", f"calf.{s}"), (f"calf.{s}", f"an.{s}"), (f"an.{s}", f"heel.{s}"),
                  (f"heel.{s}", f"ball.{s}"), (f"ball.{s}", f"toe.{s}")]
    names = list(nodes)
    me = bpy.data.meshes.new(name)
    me.from_pydata([tuple(nodes[n][0]) for n in names], [(names.index(a_), names.index(b_)) for a_, b_ in edges], [])
    obj = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(obj)
    mod = obj.modifiers.new("Skin", "SKIN")
    mod.branch_smoothing = 0.6
    mod.use_smooth_shade = True
    mod.use_x_symmetry = True
    sv = me.skin_vertices[0].data
    for i, n in enumerate(names):
        sv[i].radius = nodes[n][1]
        sv[i].use_root = n == "pelvis"
    apply_modifiers(obj)
    return obj


def apply_modifiers(obj):
    deps = bpy.context.evaluated_depsgraph_get()
    ev = obj.evaluated_get(deps)
    me = bpy.data.meshes.new_from_object(ev)
    old = obj.data
    obj.modifiers.clear()
    obj.data = me
    bpy.data.meshes.remove(old)


def subdivide(obj, levels):
    m = obj.modifiers.new("Sub", "SUBSURF")
    m.levels = levels
    m.render_levels = levels
    apply_modifiers(obj)


def sphere(name, centre, r, seg=16, rings=10):
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=seg, v_segments=rings, radius=1.0)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    obj = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(obj)
    for vt in obj.data.vertices:
        vt.co = Vector((vt.co.x * r[0], vt.co.y * r[1], vt.co.z * r[2])) + centre
    return obj


def hair_long(v, J):
    """A bob/shoulder-length hair shell around the head (front pulled inside the face)."""
    h = v["h"]
    c = Vector((0, 0.004, 1.645 * h))
    obj = sphere("HairLong", c, (0.098, 0.114, 0.102), 18, 12)
    for vt in obj.data.vertices:
        p = vt.co - c
        if p.y < -0.035 and p.z < 0.045:                      # face: tucked inside the head
            vt.co = c + Vector((p.x * 0.75, p.y * 0.55, p.z * 0.85))
        elif p.z < 0.0:                                       # sides and back hang down to the neck/shoulders
            k = 1.0 + 1.6 * min(1.0, -p.z / 0.1) * (0.6 if p.y < 0 else 1.0)
            vt.co = c + Vector((p.x * (1.0 + 0.12 * (k - 1)), p.y + (0.012 if p.y > 0 else 0), p.z * k))
    return obj


def cap(v, J):
    h = v["h"]
    c = Vector((0, 0.0, 1.668 * h))
    obj = sphere("Cap", c, (0.094, 0.108, 0.068), 16, 8)
    me = obj.data
    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.delete(bm, geom=[vt for vt in bm.verts if vt.co.z < c.z - 0.005], context="VERTS")
    # brim: a flat disc in front
    ring = [bm.verts.new(c + Vector((math.cos(t) * 0.085, -0.03 + math.sin(t) * 0.11 * (1 if math.sin(t) < 0 else 0.3), -0.004)))
            for t in [i / 12 * math.tau for i in range(12)]]
    bm.faces.new(ring)
    bm.to_mesh(me)
    bm.free()
    return obj


def umbrella():
    """Shaft + canopy, built upright around the origin (handle at 0, canopy at +0.95 m)."""
    bm = bmesh.new()
    shaft = bmesh.ops.create_cone(bm, cap_ends=True, segments=6, radius1=0.012, radius2=0.012, depth=0.9)
    for vt in shaft["verts"]:
        vt.co.z += 0.45
    canopy_verts = []
    seg = 16
    for ring_i, (rr, z) in enumerate(((0.0, 1.16), (0.27, 1.11), (0.47, 1.01), (0.56, 0.93))):
        row = []
        for k in range(seg):
            t = k / seg * math.tau
            rk = rr * (0.94 if k % 2 else 1.0) if ring_i == 3 else rr
            row.append(bm.verts.new((math.cos(t) * rk, math.sin(t) * rk, z - (0.035 if (k % 2 and ring_i == 3) else 0))))
        canopy_verts.append(row)
    faces = []
    for i in range(1, 4):
        for k in range(seg):
            a, b = canopy_verts[i - 1][k], canopy_verts[i - 1][(k + 1) % seg]
            c, d = canopy_verts[i][(k + 1) % seg], canopy_verts[i][k]
            if i == 1:
                faces.append(bm.faces.new((a, d, c)) if a is b else bm.faces.new((a, d, c, b)))
            else:
                faces.append(bm.faces.new((a, d, c, b)))
    bmesh.ops.remove_doubles(bm, verts=canopy_verts[0], dist=1e-5)
    canopy_faces = [f for f in bm.faces if f.calc_center_median().z > 0.91]
    # inner shell (visible from below)
    dup = bmesh.ops.duplicate(bm, geom=canopy_faces)
    inner = [g for g in dup["geom"] if isinstance(g, bmesh.types.BMFace)]
    for vt in {vv for f in inner for vv in f.verts}:
        vt.co.z -= 0.008
    bmesh.ops.reverse_faces(bm, faces=inner)
    me = bpy.data.meshes.new("Umbrella")
    bm.normal_update()
    bm.to_mesh(me)
    bm.free()
    obj = bpy.data.objects.new("Umbrella", me)
    bpy.context.scene.collection.objects.link(obj)
    return obj


# ----------------------------------------------------------------------------------------------------------- armature

def build_armature(name, J):
    arm = bpy.data.armatures.new(name + "_Rig")
    obj = bpy.data.objects.new(name + "_Rig", arm)
    bpy.context.scene.collection.objects.link(obj)
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    eb = {}
    for bname, parent, hj, tj in BONES:
        b = arm.edit_bones.new(bname)
        b.head = J[hj]
        b.tail = J[tj]
        # roll: bone X axis along world +X (or -X) so local X rotations are pitch for limbs and spine
        b.align_roll(Vector((0, -1, 0)) if abs((J[tj] - J[hj]).normalized().z) > 0.7 else Vector((0, 0, 1)))
        if parent:
            b.parent = eb[parent]
            b.use_connect = (b.head - eb[parent].tail).length < 1e-4
        eb[bname] = b
    bpy.ops.object.mode_set(mode="OBJECT")
    for pb in obj.pose.bones:
        pb.rotation_mode = "QUATERNION"
    return obj


def world_rot(rig, bone, axis, deg):
    """A pose-bone local rotation equal to a rotation of `deg` about the armature-space `axis` (rest frame)."""
    r = rig.data.bones[bone].matrix_local.to_3x3()
    q = Matrix.Rotation(math.radians(deg), 3, axis)
    return (r.inverted() @ q @ r).to_quaternion()


def pose(rig, rots, hips_offset=Vector()):
    """rots: {bone: [(axis, deg), ...]} applied in order (armature-space axes in the bone's rest frame)."""
    for pb in rig.pose.bones:
        pb.rotation_quaternion = (1, 0, 0, 0)
        pb.location = (0, 0, 0)
    for bone, lst in rots.items():
        q = None
        for axis, deg in lst:
            qq = world_rot(rig, bone, axis, deg)
            q = qq if q is None else qq @ q
        rig.pose.bones[bone].rotation_quaternion = q
    if hips_offset.length > 0:
        r = rig.data.bones["Hips"].matrix_local.to_3x3()
        rig.pose.bones["Hips"].location = r.inverted() @ hips_offset


X, Y, Z = "X", "Y", "Z"


def bump(phi, c, w):
    d = (phi - c + 0.5) % 1.0 - 0.5
    return math.exp(-(d / w) ** 2)


def clip_pose(clip, phi):
    """Rotations (degrees, armature-space axes) and hip offset for a clip at loop phase phi in [0, 1)."""
    tau = math.tau
    r = {}
    off = Vector()
    if clip in ("Walk", "WalkUmbrella", "Run"):
        run = clip == "Run"
        th_amp = 40 if run else 24
        for s, ph in (("L", 0.0), ("R", 0.5)):
            p = (phi + ph) % 1.0
            thigh = -th_amp * math.cos(tau * p) - (8 if run else 0)
            knee = (12 if run else 6) + (95 if run else 52) * bump(p, 0.72 if not run else 0.68, 0.17) + (0 if run else 12) * bump(p, 0.1, 0.07)
            foot = -12 * bump(p, 0.02, 0.06) + 18 * bump(p, 0.45, 0.08) - 6 * bump(p, 0.75, 0.1)
            r[f"UpperLeg.{s}"] = [(X, thigh)]
            r[f"LowerLeg.{s}"] = [(X, knee)]
            r[f"Foot.{s}"] = [(X, foot)]
            r[f"Toes.{s}"] = [(X, -14 * bump(p, 0.45, 0.07))]
            # arms swing opposite to the leg on the same side
            arm_amp = 50 if run else 17
            sign = 1 if s == "L" else -1
            if clip == "WalkUmbrella" and s == "R":
                r["UpperArm.R"] = [(X, -42), (Y, -8)]
                r["LowerArm.R"] = [(X, -78)]
                r["Hand.R"] = [(X, 12)]
            else:
                amp = arm_amp * (0.6 if clip == "WalkUmbrella" else 1.0)
                r[f"UpperArm.{s}"] = [(X, amp * math.cos(tau * p)), (Y, sign * (14 if run else 6))]
                r[f"LowerArm.{s}"] = [(X, -(78 if run else 14) - (0 if run else 14) * max(0.0, -math.cos(tau * p)))]
        bob = 0.05 if run else 0.022
        off = Vector((0.012 * math.sin(tau * phi) * (0.5 if run else 1), 0, -bob * math.cos(2 * tau * phi) - (0.06 if run else 0.0)))
        lean = 13 if run else 3
        r["Hips"] = [(Z, 5 * math.cos(tau * phi)), (Y, 2.5 * math.sin(tau * phi))]
        # +X tips an upward bone forward (towards -Y, the figure's front)
        r["Spine"] = [(X, lean * 0.5), (Z, -4 * math.cos(tau * phi))]
        r["Chest"] = [(X, lean * 0.5), (Z, -3 * math.cos(tau * phi))]
        r["Neck"] = [(X, -lean * 0.4)]
        r["Head"] = [(X, -lean * 0.3), (Z, 1.5 * math.cos(tau * phi))]
    else:  # Idle: weight shift, breathing, a glance up and down the street
        sway = math.sin(tau * phi)
        breath = math.sin(tau * phi * 3)
        off = Vector((0.018 * sway, 0, -0.004 + 0.002 * breath))
        r["Hips"] = [(Y, -2.2 * sway)]
        r["Spine"] = [(Y, 1.2 * sway), (X, 1.0 + 0.6 * breath)]
        r["Chest"] = [(Y, 0.8 * sway), (X, -0.8 * breath)]
        look = 32 * math.sin(tau * phi) * bump(phi, 0.3, 0.2) - 28 * bump(phi, 0.75, 0.12)
        r["Neck"] = [(Z, look * 0.35)]
        r["Head"] = [(Z, look * 0.65), (X, 3 * bump(phi, 0.55, 0.1))]
        for s, sign in (("L", 1), ("R", -1)):
            load = max(0.0, sway * sign)        # the loaded leg straight, the other relaxed
            r[f"UpperLeg.{s}"] = [(X, -3 * (1 - load)), (Y, -sign * 2.2 * sway)]
            r[f"LowerLeg.{s}"] = [(X, 7 * (1 - load) + 2)]
            r[f"Foot.{s}"] = [(X, -4 * (1 - load)), (Y, sign * 2.2 * sway)]
            r[f"UpperArm.{s}"] = [(X, 4 + 1.5 * breath), (Y, sign * 4)]
            r[f"LowerArm.{s}"] = [(X, -10)]
        if clip == "IdleUmbrella":        # waiting in the rain: the right arm holds the umbrella as in WalkUmbrella
            r["UpperArm.R"] = [(X, -42), (Y, -8)]
            r["LowerArm.R"] = [(X, -78)]
            r["Hand.R"] = [(X, 12)]
    return r, off


def umbrella_clip_pose(clip, phi):
    """The umbrella pose at the walk's phase (for the rest-pose umbrella placement)."""
    return clip_pose(clip, phi)


# ------------------------------------------------------------------------------------------------------- per variant

def mark_regions(obj, v, J, rig):
    """Region per vertex of the LOD0 body from the dominant bone and height."""
    me = obj.data
    groups = {g.index: g.name for g in obj.vertex_groups}
    h = v["h"]
    head_c = Vector((0, -0.002, 1.645 * h))
    out = []
    for vt in me.vertices:
        best, bw = None, -1
        for g in vt.groups:
            if g.weight > bw:
                best, bw = groups[g.group], g.weight
        p = vt.co
        reg = 1
        if best is None:
            reg = 1
        elif best in ("Head", "Neck"):
            dd = p - head_c
            reg = 0
            if v["hair"] == "short" and best == "Head" and (dd.z > 0.035 - 0.25 * max(0.0, dd.y) or (dd.y > 0.035 and dd.z > -0.06)):
                reg = 6
            if best == "Neck" and p.z < 1.47 * h:
                reg = 1
        elif best.startswith("Hand"):
            reg = 0
        elif best.startswith("LowerArm"):
            reg = 2 if (p.z > J["wrist.L"].z + 0.035) else 0
        elif best.startswith(("UpperArm", "Shoulder")):
            reg = 1
        elif best in ("Chest", "Spine", "Hips") or best.startswith("UpperLeg"):
            # a level waistline (shirt over the belt), a coat down to mid-thigh
            reg = 1 if p.z > v["belt"] * h else 3
            if v["coat"] and p.z > v["coat"] * h:
                reg = 1
        elif best.startswith("LowerLeg"):
            reg = 4 if p.z > J["ankle.L"].z + 0.06 else 5
        elif best.startswith(("Foot", "Toes")):
            reg = 5
        out.append(reg)
    return out


def vertex_ao(objs, samples=24, dist=0.22, seed=1):
    """Per-vertex hemisphere occlusion of the rest pose (all pieces together)."""
    bm = bmesh.new()
    for o in objs:
        tmp = o.data.copy()
        tmp.transform(o.matrix_world)
        bm.from_mesh(tmp)
        bpy.data.meshes.remove(tmp)
    tree = BVHTree.FromBMesh(bm)
    bm.free()
    rng = random.Random(seed)
    dirs = []
    for _ in range(samples):
        u, w = rng.random(), rng.random()
        r, t = math.sqrt(u), math.tau * w
        dirs.append(Vector((r * math.cos(t), r * math.sin(t), math.sqrt(max(0.0, 1 - u)))))
    res = {}
    for o in objs:
        vals = []
        mw = o.matrix_world
        for vt in o.data.vertices:
            n = (mw.to_3x3() @ vt.normal).normalized()
            p = mw @ vt.co + n * 0.004
            tb = n.orthogonal().normalized()
            bb = n.cross(tb)
            hit = 0
            for d in dirs:
                wd = tb * d.x + bb * d.y + n * d.z
                if tree.ray_cast(p, wd, dist)[0] is not None:
                    hit += 1
            vals.append(1.0 - 0.75 * hit / samples)
        res[o.name] = vals
    return res


def weights_of(obj):
    groups = {g.index: g.name for g in obj.vertex_groups}
    out = []
    for vt in obj.data.vertices:
        ws = [(BONE_NAMES.index(groups[g.group]), g.weight) for g in vt.groups if groups.get(g.group) in BONE_NAMES and g.weight > 1e-4]
        ws.sort(key=lambda t: -t[1])
        ws = ws[:4] or [(0, 1.0)]
        s = sum(w for _, w in ws)
        ws = [(i, w / s) for i, w in ws] + [(0, 0.0)] * (4 - len(ws))
        out.append(ws)
    return out


def export_mesh_data(obj, regions, ao, weights):
    me = obj.data
    me.calc_loop_triangles()
    pos, nrm, uv, bi, bw = [], [], [], [], []
    for i, vt in enumerate(me.vertices):
        p = C @ vt.co
        n = (C.to_3x3() @ vt.normal).normalized()
        pos += [round(p.x, 5), round(p.y, 5), round(p.z, 5)]
        nrm += [round(n.x, 4), round(n.y, 4), round(n.z, 4)]
        uv += [regions[i], round(ao[i], 3)]
        bi += [w[0] for w in weights[i]]
        bw += [round(w[1], 4) for w in weights[i]]
    tris = []
    for t in me.loop_triangles:
        a, b, c = t.vertices
        tris += [a, c, b]          # handedness flip
    return dict(positions=pos, normals=nrm, uv=uv, boneIndices=bi, boneWeights=bw, triangles=tris,
                vertexCount=len(me.vertices), triangleCount=len(me.loop_triangles))


def nearest_transfer(src_obj, src_vals, dst_obj):
    kd = KDTree(len(src_obj.data.vertices))
    for i, vt in enumerate(src_obj.data.vertices):
        kd.insert(vt.co, i)
    kd.balance()
    return [src_vals[kd.find(vt.co)[1]] for vt in dst_obj.data.vertices]


def decimate_copy(obj, ratio, name):
    cp = obj.copy()
    cp.data = obj.data.copy()
    cp.name = name
    cp.data.name = name
    bpy.context.scene.collection.objects.link(cp)
    for m in list(cp.modifiers):
        cp.modifiers.remove(m)
    d = cp.modifiers.new("Dec", "DECIMATE")
    d.ratio = ratio
    d.use_collapse_triangulate = True
    apply_modifiers(cp)
    return cp


def bind(mesh_obj, rig, auto=True, bone=None):
    for o in bpy.context.view_layer.objects:
        o.select_set(False)
    if auto:
        mesh_obj.select_set(True)
        rig.select_set(True)
        bpy.context.view_layer.objects.active = rig
        bpy.ops.object.parent_set(type="ARMATURE_AUTO")
    else:
        mesh_obj.parent = rig
        vg = mesh_obj.vertex_groups.new(name=bone)
        vg.add(list(range(len(mesh_obj.data.vertices))), 1.0, "REPLACE")
        m = mesh_obj.modifiers.new("Armature", "ARMATURE")
        m.object = rig


def join(objs, name):
    for o in bpy.context.view_layer.objects:
        o.select_set(False)
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.join()
    objs[0].name = name
    objs[0].data.name = name
    return objs[0]


def bake_clips(rig):
    """Per clip, per frame, per bone: the skinning matrix (pose @ rest^-1) in Unity mesh space, 3 rows of 4."""
    clips = []
    for name, frames, stride in CLIPS:
        mats = []
        for f in range(frames):
            rots, off = clip_pose(name, f / frames)
            pose(rig, rots, off)
            bpy.context.view_layer.update()
            fr = []
            for b in BONE_NAMES:
                pb = rig.pose.bones[b]
                s = C @ (pb.matrix @ rig.data.bones[b].matrix_local.inverted()) @ C_INV
                fr += [round(s[r][c], 6) for r in range(3) for c in range(4)]
            mats.append(fr)
        # flat: frame-major, then bone, then the 12 values of the 3x4 matrix (JsonUtility reads no nested arrays)
        clips.append(dict(name=name, frames=frames, stride=stride,
                          seconds=(IDLE_SECONDS if name.startswith("Idle") else 0.0), matrices=[x for fr in mats for x in fr]))
    pose(rig, {})
    bpy.context.view_layer.update()
    return clips


def make_actions(rig, vname):
    """Keyframed actions (one per clip) for the rigged FBX source; 30 fps, one key per sampled frame."""
    if rig.animation_data is None:
        rig.animation_data_create()
    acts = []
    for name, frames, _ in CLIPS:
        act = bpy.data.actions.new(f"{vname}_{name}")
        act.use_fake_user = True
        rig.animation_data.action = act
        step = 2 if name != "Idle" else 6
        for f in range(frames + 1):
            rots, off = clip_pose(name, (f % frames) / frames)
            pose(rig, rots, off)
            for pb in rig.pose.bones:
                pb.keyframe_insert("rotation_quaternion", frame=1 + f * step)
                if pb.name == "Hips":
                    pb.keyframe_insert("location", frame=1 + f * step)
        acts.append(act)
    rig.animation_data.action = acts[0]
    pose(rig, {})
    return acts


def build_variant(vname, v):
    J = joints(v)
    rig = build_armature(f"VC_Ped_{vname}", J)
    body = skin_body(f"VC_Ped_{vname}_Body", v, J)
    subdivide(body, 2)
    bind(body, rig, auto=True)
    # skinning check: every vertex must have a weight
    unweighted = sum(1 for vt in body.data.vertices if not vt.groups)
    regions_body = mark_regions(body, v, J, rig)
    extra = []
    if v["hair"] == "long":
        hair = hair_long(v, J)
        subdivide(hair, 1)
        bind(hair, rig, auto=False, bone="Head")
        extra.append((hair, 6))
    if v["cap"]:
        cp = cap(v, J)
        bind(cp, rig, auto=False, bone="Head")
        extra.append((cp, 7))
    ao = vertex_ao([body] + [e for e, _ in extra], seed=v["seed"])
    regions = list(regions_body)
    aos = list(ao[body.name])
    for e, reg in extra:
        regions += [reg] * len(e.data.vertices)
        aos += ao[e.name]
    lod0 = join([body] + [e for e, _ in extra], f"VC_Ped_{vname}_LOD0")
    # the joined mesh keeps vertex order: body first, then the extras
    lod0.modifiers.clear()
    am = lod0.modifiers.new("Armature", "ARMATURE")
    am.object = rig
    lod1 = decimate_copy(lod0, 0.4, f"VC_Ped_{vname}_LOD1")
    lod2 = decimate_copy(lod0, 0.1, f"VC_Ped_{vname}_LOD2")
    for o in (lod1, lod2):
        o.parent = rig
        m = o.modifiers.new("Armature", "ARMATURE")
        m.object = rig

    # umbrella: built upright in the umbrella pose, moved into the hand's rest frame
    umb = umbrella()
    umb_regions = [7 if Vector((vt.co.x, vt.co.y)).length > 0.02 else 5 for vt in umb.data.vertices]
    rots, off = clip_pose("WalkUmbrella", 0.0)
    pose(rig, rots, off)
    bpy.context.view_layer.update()
    hand = rig.pose.bones["Hand.R"]
    grip = hand.head.copy()
    head_top = rig.pose.bones["Head"].tail.copy()
    up_at = Matrix.Translation(Vector((grip.x, grip.y, grip.z - 0.06)))
    skin_m = hand.matrix @ rig.data.bones["Hand.R"].matrix_local.inverted()
    umb.data.transform(skin_m.inverted() @ up_at)
    pose(rig, {})
    bpy.context.view_layer.update()
    bind(umb, rig, auto=False, bone="Hand.R")
    canopy_clear = (grip.z - 0.06 + 0.93) - head_top.z
    umb_ao = [1.0] * len(umb.data.vertices)

    data = dict(name=vname, lods=[], umbrella=None)
    for lod in (lod0, lod1, lod2):
        regs = regions if lod is lod0 else nearest_transfer(lod0, regions, lod)
        a = aos if lod is lod0 else nearest_transfer(lod0, aos, lod)
        data["lods"].append(export_mesh_data(lod, regs, a, weights_of(lod)))
    data["umbrella"] = export_mesh_data(umb, umb_regions, umb_ao, weights_of(umb))
    data["clips"] = bake_clips(rig)
    data["height"] = round((J["headtop"].z), 3)
    acts = make_actions(rig, f"VC_Ped_{vname}")
    stats = (f"VC_Ped_{vname}: LOD0 {data['lods'][0]['triangleCount']} tris / {data['lods'][0]['vertexCount']} verts, "
             f"LOD1 {data['lods'][1]['triangleCount']} ({100 * data['lods'][1]['triangleCount'] / data['lods'][0]['triangleCount']:.0f}%), "
             f"LOD2 {data['lods'][2]['triangleCount']} ({100 * data['lods'][2]['triangleCount'] / data['lods'][0]['triangleCount']:.0f}%), "
             f"umbrella {data['umbrella']['triangleCount']} tris, unweighted body verts {unweighted}, height {data['height']:.2f} m, "
             f"canopy {canopy_clear:.2f} m above the head")
    return rig, [lod0, lod1, lod2], umb, data, stats, acts


def export_fbx(rig, lods, umb, path):
    saved = dict(vc_tools.BASE_ARGS)
    vc_tools.BASE_ARGS.update(object_types={"MESH", "EMPTY", "ARMATURE"}, bake_anim=True, bake_anim_use_all_actions=True,
                              bake_anim_use_nla_strips=False, bake_anim_force_startend_keying=True, add_leaf_bones=False,
                              use_armature_deform_only=True, mesh_smooth_type="FACE")
    try:
        vc_tools.vc_export_fbx([rig] + lods + [umb], path, mode="prop", pivot=rig, path_mode="STRIP")
    finally:
        vc_tools.BASE_ARGS.clear()
        vc_tools.BASE_ARGS.update(saved)


def render_lineup(rigs_meshes, path):
    """Workbench line-up of the three variants mid-stride (authoring evidence)."""
    scene = bpy.context.scene
    try:
        scene.render.engine = "BLENDER_WORKBENCH"
    except TypeError:
        pass
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "VERTEX"
    scene.render.resolution_x, scene.render.resolution_y = 1600, 900
    scene.render.film_transparent = False
    cam_data = bpy.data.cameras.new("LineupCam")
    cam_data.lens = 50
    cam = bpy.data.objects.new("LineupCam", cam_data)
    scene.collection.objects.link(cam)
    cam.location = (1.6, -5.6, 1.25)
    cam.rotation_euler = (math.radians(86), 0, math.radians(16))
    scene.camera = cam
    scene.render.filepath = path
    scene.render.image_settings.file_format = "PNG"
    bpy.ops.render.render(write_still=True)


PALETTE = [(0.82, 0.62, 0.47), (0.16, 0.25, 0.48), (0.55, 0.38, 0.62), (0.13, 0.15, 0.25), (0.13, 0.15, 0.25),
           (0.08, 0.08, 0.08), (0.12, 0.08, 0.05), (0.6, 0.1, 0.1)]


def colour_lods(lod, regions):
    me = lod.data
    attr = me.color_attributes.new("Region", "FLOAT_COLOR", "POINT")
    for i, r in enumerate(regions):
        c = PALETTE[r]
        attr.data[i].color = (c[0], c[1], c[2], 1.0)


def main():
    os.makedirs(MODEL_DIR, exist_ok=True)
    os.makedirs(BAKE_DIR, exist_ok=True)
    os.makedirs(os.path.dirname(LOG), exist_ok=True)
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    vc_tools.vc_setup_scene()
    scene = bpy.context.scene
    scene.render.fps = 30
    out = dict(version=1, bones=BONE_NAMES, space="unity-mesh (x,y,z) = blender (-x, z, -y)",
               clips=[dict(name=n, frames=f, stride=s) for n, f, s in CLIPS], idleSeconds=IDLE_SECONDS, variants=[])
    lines = []
    shown = []
    for i, (vname, v) in enumerate(VARIANTS.items()):
        rig, lods, umb, data, stats, acts = build_variant(vname, v)
        out["variants"].append(data)
        lines.append(stats)
        print(stats)
        export_fbx(rig, lods, umb, os.path.join(MODEL_DIR, f"VC_Ped_{vname}.fbx"))
        # actions of every rig share bone names: drop this variant's before the next export (they regenerate from code)
        rig.animation_data.action = None
        for a in acts:
            bpy.data.actions.remove(a)
        shown.append((rig, lods, umb, data))
    with open(os.path.join(BAKE_DIR, "vc_peds_crowd.json"), "w", encoding="utf-8") as f:
        json.dump(out, f, separators=(",", ":"))
    lines.append("clips: " + ", ".join(f"{n} {f} frames, {s} m/loop" for n, f, s in CLIPS))
    with open(LOG, "w", encoding="utf-8") as f:
        f.write("\n".join(lines) + "\n")
    if RENDER:
        os.makedirs(SHOT_DIR, exist_ok=True)
        for k, (rig, lods, umb, data) in enumerate(shown):
            rig.location.x = (k - 1) * 0.9
            for j, lod in enumerate(lods):
                lod.hide_render = j != 0
            umb.hide_render = k != 1
            rots, off = clip_pose("WalkUmbrella" if k == 1 else "Walk", 0.15 + 0.3 * k)
            pose(rig, rots, off)
            colour_lods(lods[0], [int(data["lods"][0]["uv"][2 * q]) for q in range(data["lods"][0]["vertexCount"])])
            umb_regs = [int(data["umbrella"]["uv"][2 * q]) for q in range(data["umbrella"]["vertexCount"])]
            colour_lods(umb, umb_regs)
        bpy.context.view_layer.update()
        render_lineup(shown, os.path.join(SHOT_DIR, "blender_lineup.png"))
        # LOD sheet of variant A
        rig, lods, umb, data = shown[0]
        for k, (r2, l2, u2, d2) in enumerate(shown):
            for lod in l2:
                lod.hide_render = True
            u2.hide_render = True
        for j, lod in enumerate(lods):
            lod.hide_render = False
            colour_lods(lod, [int(data["lods"][j]["uv"][2 * q]) for q in range(data["lods"][j]["vertexCount"])]) if j else None
        rig.location.x = 0
        pose(rig, {})
        bpy.context.view_layer.update()
        for j, lod in enumerate(lods):
            for jj, l3 in enumerate(lods):
                l3.hide_render = jj != j
            bpy.context.scene.render.filepath = os.path.join(SHOT_DIR, f"blender_A_LOD{j}.png")
            bpy.ops.render.render(write_still=True)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(BAKE_DIR, "VC_Pedestrians.blend"))
    print("\n".join(lines))


main()

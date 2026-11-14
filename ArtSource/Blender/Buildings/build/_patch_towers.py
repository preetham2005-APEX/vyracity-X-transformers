import io, sys

q = r"E:\VeyraCity\ArtSource\Blender\Buildings\build\build_towers.py"
t = io.open(q, encoding="utf-8").read()
reps = [
# ---- fins / podium / crown take a detail level
("""def fins(bm, w, l, z0, z1, m, pitch=4.5, depth=0.30, wide=0.18):""",
 """def fins(bm, w, l, z0, z1, m, pitch=4.5, depth=0.30, wide=0.18, lod=0):
    if lod >= 2:
        return                      # LOD2 is massing only: the fins are what a collapse decimate eats first
    if lod == 1:
        pitch, wide, depth = pitch * 2.0, wide * 1.6, depth * 1.1"""),
("""def podium(bm, w, l, h=8.0, m_pier=STONE):""",
 """def podium(bm, w, l, h=8.0, m_pier=STONE, lod=0):"""),
("""    L.box(bm, (w - 2.2, l - 2.2, h - 0.6), base_at=(0, 0, 0.0), m=GLASS)      # recessed shopfront line
    nx = max(3, int(w / 6.0))
    ny = max(3, int(l / 6.0))""",
 """    if lod >= 2:
        L.box(bm, (w, l, h - 0.6), base_at=(0, 0, 0.0), m=STONE)
        L.box(bm, (w + 0.5, l + 0.5, 0.6), base_at=(0, 0, h - 0.6), m=CONC)
        return
    L.box(bm, (w - 2.2, l - 2.2, h - 0.6), base_at=(0, 0, 0.0), m=GLASS)      # recessed shopfront line
    step = 1 if lod == 0 else 2
    nx = max(3, int(w / 6.0))
    ny = max(3, int(l / 6.0))"""),
("""    for i in range(nx + 1):
        x = -w / 2.0 + w * i / nx
        for sy in (-1, 1):
            L.box(bm, (1.1, 1.1, h - 0.6), base_at=(x, sy * (l - 1.1) / 2.0, 0.0), m=m_pier)
    for j in range(1, ny):
        y = -l / 2.0 + l * j / ny
        for sx in (-1, 1):
            L.box(bm, (1.1, 1.1, h - 0.6), base_at=(sx * (w - 1.1) / 2.0, y, 0.0), m=m_pier)""",
 """    for i in range(0, nx + 1, step):
        x = -w / 2.0 + w * i / nx
        for sy in (-1, 1):
            L.box(bm, (1.1 * step, 1.1, h - 0.6), base_at=(x, sy * (l - 1.1) / 2.0, 0.0), m=m_pier)
    for j in range(step, ny, step):
        y = -l / 2.0 + l * j / ny
        for sx in (-1, 1):
            L.box(bm, (1.1, 1.1 * step, h - 0.6), base_at=(sx * (w - 1.1) / 2.0, y, 0.0), m=m_pier)"""),
("""    L.box(bm, (10.0, 2.6, 0.45), base_at=(0, -l / 2.0 - 1.1, 4.6), m=METAL)  # entrance canopy (front, -Y)""",
 """    if lod == 0:
        L.box(bm, (10.0, 2.6, 0.45), base_at=(0, -l / 2.0 - 1.1, 4.6), m=METAL)  # entrance canopy (front, -Y)"""),
("""def crown(bm, w, l, z, plant=True, mast=0.0, cap=True):""",
 """def crown(bm, w, l, z, plant=True, mast=0.0, cap=True, lod=0):
    if lod >= 2:
        plant = False"""),
# ---- families take a detail level
("""def tower_glass(w, l, h, pitch=4.5):""", """def tower_glass(w, l, h, lod=0, pitch=4.5):"""),
("""    podium(bm, w, l, pod)
    shaft(bm, w, l, pod, top)
    fins(bm, w, l, pod + 0.4, top - 0.4, METAL, pitch=pitch)""",
 """    podium(bm, w, l, pod, lod=lod)
    shaft(bm, w, l, pod, top)
    fins(bm, w, l, pod + 0.4, top - 0.4, METAL, pitch=pitch, lod=lod)"""),
("""    crown(bm, w, l, top, plant=True, mast=mast, cap=h > 60)
    return bm""",
 """    crown(bm, w, l, top, plant=True, mast=mast, cap=h > 60, lod=lod)
    return bm"""),
("""def tower_deco(w, l, h):""", """def tower_deco(w, l, h, lod=0):"""),
("""    podium(bm, w1, l1, pod)
    shaft(bm, w1, l1, pod, z1, pier=1.8)
    fins(bm, w1, l1, pod + 0.4, z1, STONE, pitch=4.0, depth=0.42, wide=0.80)""",
 """    podium(bm, w1, l1, pod, lod=lod)
    shaft(bm, w1, l1, pod, z1, pier=1.8)
    fins(bm, w1, l1, pod + 0.4, z1, STONE, pitch=4.0, depth=0.42, wide=0.80, lod=lod)"""),
("""    fins(bm, w2, l2, z1 + 1.8, z2, STONE, pitch=4.0, depth=0.42, wide=0.72)""",
 """    fins(bm, w2, l2, z1 + 1.8, z2, STONE, pitch=4.0, depth=0.42, wide=0.72, lod=lod)"""),
("""    fins(bm, w3, l3, z2 + 1.7, top, STONE, pitch=4.0, depth=0.38, wide=0.64)
    t = crown(bm, w3, l3, top, plant=False, cap=True)""",
 """    fins(bm, w3, l3, z2 + 1.7, top, STONE, pitch=4.0, depth=0.38, wide=0.64, lod=lod)
    t = crown(bm, w3, l3, top, plant=False, cap=True, lod=lod)"""),
("""def block_slab(w, l, h):""", """def block_slab(w, l, h, lod=0):"""),
("""    podium(bm, w, l, pod)
    shaft(bm, w, l, pod, top, pier=1.6)
    fins(bm, w, l, pod + 0.4, top - 0.4, SPAN, pitch=5.0, depth=0.26, wide=0.55)""",
 """    podium(bm, w, l, pod, lod=lod)
    shaft(bm, w, l, pod, top, pier=1.6)
    fins(bm, w, l, pod + 0.4, top - 0.4, SPAN, pitch=5.0, depth=0.26, wide=0.55, lod=lod)"""),
("""    crown(bm, w, l, top, plant=True, mast=0.0, cap=False)
    return bm""",
 """    crown(bm, w, l, top, plant=True, mast=0.0, cap=False, lod=lod)
    return bm"""),
# ---- authored LODs instead of a collapse decimate
("""    reset()
    bm = fn(w, l, h)
    obj = L.new_object(NAMES[key], bm, SLOTS)
    pts = [obj.matrix_world @ Vector(c) for c in obj.bound_box]
    lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    uv = L.unwrap(obj, margin=0.004, cube=4.0)   # one texture tile = 4 m = one storey
    rep0 = L.mesh_report(obj)
    r1 = min(1.0, max(0.40, 160.0 / max(rep0["tris"], 1)))
    r2 = min(1.0, max(0.09, 90.0 / max(rep0["tris"], 1)))
    parent, lods = vc_tools.vc_make_lods(obj, ratios=(1.0, r1, r2))
    for i, lod in enumerate(lods):
        if i:
            L.clean_lod(lod, ref=lods[0])""",
 """    reset()
    # LODs are AUTHORED, not decimated. A collapse decimate on a tower eats the massing first and leaves
    # the mullion fins behind - rendered backface-culled, the LOD2 was a see-through shell of slivers
    # (preview/lod2_backface_check_towers.png, before this change). Re-running the generator at a lower
    # detail level gives a LOD2 that is the silhouette, which is what carries most frames at city distance.
    lods = []
    uv = True
    for lod in (0, 1, 2):
        o = L.new_object("%s_LOD%d" % (NAMES[key], lod), fn(w, l, h, lod), SLOTS)
        uv = L.unwrap(o, margin=0.004, cube=4.0) and uv   # one texture tile = 4 m = one storey
        lods.append(o)
    obj = lods[0]
    pts = [obj.matrix_world @ Vector(c) for c in obj.bound_box]
    lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    rep0 = L.mesh_report(obj)
    r1 = r2 = None
    parent = bpy.data.objects.new(NAMES[key], None)
    bpy.context.scene.collection.objects.link(parent)
    for o in lods:
        o.parent = parent"""),
("""        "lod_ratios": [1.0, round(r1, 3), round(r2, 3)],""",
 """        "lod_mode": "authored",
        "lod_ratios": None,"""),
]
for a, b in reps:
    if a not in t:
        print("MISS:", a.strip()[:70]); sys.exit(1)
    t = t.replace(a, b, 1)
io.open(q, "w", encoding="utf-8").write(t)
print("patched towers")

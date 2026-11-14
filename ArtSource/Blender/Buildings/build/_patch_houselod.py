import io, sys

# ---------------- library: a module-level detail level the helpers obey
p = r"E:\VeyraCity\ArtSource\Blender\Buildings\build\vc_bld_lib.py"
s = io.open(p, encoding="utf-8").read()
s = s.replace('''# ---------------------------------------------------------------- materials''',
'''#: Detail level the helpers below obey, so one building description can be evaluated three times to give
#: AUTHORED LOD meshes. A collapse decimate cannot do this job: on geometry assembled from boxes it eats
#: the massing and leaves slivers (see preview/lod2_backface_check_*.png before the change).
#:   0 = everything, 1 = no sills, glazing panes or steps, 2 = massing only (solid walls, no openings)
DETAIL = 0


def set_detail(level):
    global DETAIL
    DETAIL = level


# ---------------------------------------------------------------- materials''')

# wall(): drop openings entirely at LOD2, drop sills and panes at LOD1
s = s.replace('''    wins = sorted(windows, key=lambda w: w[0])
    sgn = float(inward) if inward is not None else (-1.0 if fixed > 0 else 1.0)''',
'''    wins = sorted(windows, key=lambda w: w[0])
    if DETAIL >= 2:
        wins, m_glass = [], None          # massing only: a solid wall
    elif DETAIL == 1:
        sill = False                      # keep the openings (they carry the facade), drop the trim
    sgn = float(inward) if inward is not None else (-1.0 if fixed > 0 else 1.0)''')
s = s.replace('''    if m_glass is None:
        return
    gcen = fixed + sgn * (reveal + 0.03)''',
'''    if m_glass is None or DETAIL >= 1:
        return
    gcen = fixed + sgn * (reveal + 0.03)''')

for fn, guard in (("def door(", 2), ("def steps(", 1), ("def porch(", 2),
                  ("def entry_recess(", 2), ("def dormer(", 2)):
    i = s.index(fn)
    j = s.index('"""', s.index('"""', i) + 3) + 3      # end of the docstring
    s = s[:j] + ("\n    if DETAIL >= %d:\n        return" % guard) + s[j:]
io.open(p, "w", encoding="utf-8").write(s)

# ---------------- houses: authored LODs
q = r"E:\VeyraCity\ArtSource\Blender\Buildings\build\build_houses.py"
t = io.open(q, encoding="utf-8").read()
a = """    reset()
    obj, want_h = HOUSES[key]()
    pts = [obj.matrix_world @ Vector(c) for c in obj.bound_box]
    lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    uv = L.unwrap(obj)
    rep0 = L.mesh_report(obj)
    r1 = min(1.0, max(0.40, 120.0 / max(rep0["tris"], 1)))
    r2 = min(1.0, max(0.10, 60.0 / max(rep0["tris"], 1)))
    parent, lods = vc_tools.vc_make_lods(obj, ratios=(1.0, r1, r2))
    for i, lod in enumerate(lods):
        if i:
            L.clean_lod(lod, ref=lods[0])"""
b = """    reset()
    # LODs are AUTHORED, not decimated: the same house description is evaluated at three detail levels
    # (vc_bld_lib.set_detail). A collapse decimate shreds this kind of box-assembled geometry - the LOD2
    # was a cloud of slivers with no silhouette left (preview/lod2_backface_check_houses.png before this).
    lods = []
    uv = True
    name = None
    for lod in (0, 1, 2):
        L.set_detail(lod)
        obj, want_h = HOUSES[key]()
        name = name or obj.name
        obj.name = "%s_LOD%d" % (name, lod)
        uv = L.unwrap(obj) and uv
        lods.append(obj)
    L.set_detail(0)
    obj = lods[0]
    pts = [obj.matrix_world @ Vector(c) for c in obj.bound_box]
    lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    rep0 = L.mesh_report(obj)
    parent = bpy.data.objects.new(name, None)
    bpy.context.scene.collection.objects.link(parent)
    for o in lods:
        o.parent = parent"""
if a not in t:
    print("MISS house build"); sys.exit(1)
t = t.replace(a, b, 1)
t = t.replace('''        "lod_ratios": [1.0, round(r1, 3), round(r2, 3)],''',
              '''        "lod_mode": "authored",
        "lod_ratios": None,''')
io.open(q, "w", encoding="utf-8").write(t)
print("patched house LODs")

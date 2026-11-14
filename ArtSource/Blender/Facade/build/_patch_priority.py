"""Insert the three priority groups (window reveals, floor banding, tower crowns) ahead of the
shopfront and rooftop kits, per the main session's reorder. Writes files only - runs no Blender."""
import io, sys

p = r"E:\VeyraCity\ArtSource\Blender\Facade\build\build_facade.py"
s = io.open(p, encoding="utf-8").read()

# ---- 1. docstring: record the reorder and the window pivot
s = s.replace('''PIVOT - every wall-mounted piece pivots at the **wall face, centred horizontally, at ground level''',
'''PRIORITY - reordered by the main session (which is doing night ambient, per-window reveal shading and
floor/column coherence at the same time, so its shading and this geometry should meet in the middle
rather than both faking depth):
  1. window reveal depth - a real recess with head and jamb
  2. floor-level slab and cornice banding
  3. crown / setback pieces for tower tops
  then the ground-floor shopfront kit, then the rooftop clutter.
"Night facades read as flat boxes with lit rectangles painted on" is the problem these three fix: the
tower set carries its floor lines and window grid in the curtain-wall TEXTURE (that is what makes a 240 m
tower cost the same as a 28 m one), so at night there is no geometry for a lit pane to sit behind. These
modules put the recess back where it matters, at a cost that is paid per module placed rather than per
tower.

PIVOT - every wall-mounted piece pivots at the **wall face, centred horizontally, at ground level''')
s = s.replace('''base-centre pivot. Outward is Blender -Y''',
'''base-centre pivot. The window and banding modules are the one deviation: they repeat UP a facade, so
they anchor at the wall face, centred, at the BOTTOM OF THEIR OWN OPENING / BAND (z = 0 is the sill line
or the band soffit, not the ground). Outward is Blender -Y''')

# ---- 2. one more material: a blank emissive slot for crown coves and lit signage
s = s.replace('''SLOTS = ["VC_Fac_Metal", "VC_Fac_Glass", "VC_Fac_Panel", "VC_Fac_Fabric", "VC_Fac_Blank",
         "VC_Fac_Concrete"]
MET, GLA, PAN, FAB, BLK, CON = 0, 1, 2, 3, 4, 5''',
'''SLOTS = ["VC_Fac_Metal", "VC_Fac_Glass", "VC_Fac_Panel", "VC_Fac_Fabric", "VC_Fac_Blank",
         "VC_Fac_Concrete", "VC_Fac_Emissive"]
MET, GLA, PAN, FAB, BLK, CON, EMI = 0, 1, 2, 3, 4, 5, 6''')
s = s.replace('''    "VC_Fac_Concrete": ((0.400, 0.394, 0.380), 0.00, 0.68, None, 0.0),
}''',
'''    "VC_Fac_Concrete": ((0.400, 0.394, 0.380), 0.00, 0.68, None, 0.0),
    # BLANK emissive: crown light coves and lit sign faces. No artwork, no lettering - Unity drives
    # _EMISSION and the colour, same pattern as VC_Lamp_Emissive and VC_Neon_Emissive.
    "VC_Fac_Emissive": ((0.600, 0.580, 0.520), 0.00, 0.22, (1.00, 0.92, 0.76), 5.0),
}''')

# ---- 3. the three priority groups
new = '''
# ------------------------------------------------------------------ PART 0 (priority): reveal + banding
# Wall face at y = 0, street at -Y. These modules anchor at the bottom of their own opening or band.

def _reveal(bm, w, hh, lod, depth=0.26, mullions=0, transom=0.0):
    """A real window recess: head, two jambs and a sill, with the glass set `depth` behind the wall face.
    This is the geometry the night shading needs - a lit pane set back, with a head that casts onto it."""
    jam, hd, sl = 0.13, 0.16, 0.10
    L.box(bm, (w, depth, hd), base_at=(0, -depth / 2.0, hh - hd), m=PAN)              # head / lintel
    L.box(bm, (w, depth + 0.07, sl), base_at=(0, -depth / 2.0 - 0.035, 0.0), m=CON)   # sill, with a drip
    for sx in (-1, 1):                                                                 # jambs
        L.box(bm, (jam, depth, hh - hd - sl), base_at=(sx * (w / 2.0 - jam / 2.0), -depth / 2.0, sl),
              m=PAN)
    gw, gh = w - 2 * jam, hh - hd - sl
    L.box(bm, (gw, 0.05, gh), loc=(0, -depth + 0.03, sl + gh / 2.0), m=GLA)           # recessed glazing
    L.box(bm, (gw, 0.07, 0.07), loc=(0, -depth + 0.05, sl + 0.04), m=MET)             # frame bottom
    L.box(bm, (gw, 0.07, 0.07), loc=(0, -depth + 0.05, sl + gh - 0.04), m=MET)
    if transom > 0.0 and lod < 2:
        L.box(bm, (gw, 0.08, 0.08), loc=(0, -depth + 0.05, sl + transom), m=MET)
    if lod < 2:
        for i in range(1, mullions + 1):
            x = -w / 2.0 + w * i / (mullions + 1)
            L.box(bm, (0.07, 0.08, gh), base_at=(x, -depth + 0.05, sl), m=MET)
    if lod == 0:
        L.box(bm, (w + 0.10, 0.06, 0.05), loc=(0, -depth - 0.05, 0.02), m=CON)        # sill nosing
    return hh


def window_reveal_single(lod=0):
    """1.5 x 1.6 m punched window with a 0.26 m reveal - the repeating unit for a punched facade."""
    bm = bmesh.new()
    hh = _reveal(bm, 1.50, 1.60, lod, depth=0.26, mullions=1)
    return bm, (1.60, 0.33, hh), "window"


def window_reveal_band(lod=0):
    """3.0 m ribbon window with four lights - the half-bay horizontal module."""
    bm = bmesh.new()
    hh = _reveal(bm, HALF, 1.45, lod, depth=0.24, mullions=3)
    return bm, (HALF + 0.10, 0.31, hh), "window"


def window_reveal_storey(lod=0):
    """1.5 x 3.3 m storey-height bay with a transom - for curtain-wall and midrise frontages."""
    bm = bmesh.new()
    hh = _reveal(bm, 1.50, 3.30, lod, depth=0.30, mullions=1, transom=2.35)
    return bm, (1.60, 0.37, hh), "window"


def slab_band(lod=0):
    """Floor-line slab edge: the horizontal that makes a facade read as storeys rather than a box."""
    bm = bmesh.new()
    L.box(bm, (BAY, 0.20, 0.42), base_at=(0, -0.10, 0.0), m=CON)
    if lod < 2:
        L.box(bm, (BAY, 0.26, 0.09), base_at=(0, -0.13, 0.33), m=CON)                 # projecting lip
        L.box(bm, (BAY, 0.05, 0.05), loc=(0, -0.245, 0.30), m=CON)                    # drip groove edge
    return bm, (BAY, 0.28, 0.42), "band"


def cornice_band(lod=0):
    """A profiled cornice module - a deeper, moulded horizontal for the top of a base or a parapet line."""
    bm = bmesh.new()
    pts = [(0.0, 0.0), (-0.16, 0.10), (-0.20, 0.26), (-0.34, 0.40), (-0.34, 0.52),
           (-0.10, 0.66), (0.0, 0.66)]
    L.profile_solid(bm, pts, -BAY / 2.0, BAY / 2.0, CON)
    if lod < 2:
        L.box(bm, (BAY, 0.09, 0.07), loc=(0, -0.30, 0.46), m=CON)                     # bed mould
    return bm, (BAY, 0.36, 0.66), "band"


def sill_band(lod=0):
    """A thin string course / continuous sill - the cheapest horizontal, for mid-facade coherence."""
    bm = bmesh.new()
    L.box(bm, (BAY, 0.14, 0.16), base_at=(0, -0.07, 0.0), m=CON)
    if lod < 2:
        L.box(bm, (BAY, 0.19, 0.05), base_at=(0, -0.095, 0.11), m=CON)
    return bm, (BAY, 0.20, 0.16), "band"


def crown_setback(lod=0):
    """Tower crown: a parapet, a setback step and a coping, as a 6 m module along a roof edge.
    Stops a tower top reading as a flat lid, which is the other half of gap #5."""
    bm = bmesh.new()
    L.box(bm, (BAY, 0.52, 1.30), base_at=(0, -0.26, 0.0), m=PAN)                      # parapet upstand
    L.box(bm, (BAY, 0.66, 0.16), base_at=(0, -0.33, 1.30), m=CON)                     # coping
    L.box(bm, (BAY, 0.80, 0.70), base_at=(0, -0.10, 1.46), m=PAN)                     # setback step
    L.box(bm, (BAY, 0.92, 0.14), base_at=(0, -0.16, 2.16), m=CON)
    if lod < 2:
        L.box(bm, (BAY, 0.10, 0.26), loc=(0, -0.56, 1.00), m=EMI)                     # BLANK light cove
        L.box(bm, (BAY, 0.14, 0.10), loc=(0, -0.58, 1.17), m=MET)                     # cove shield
    return bm, (BAY, 0.92, 2.30), "band"


def crown_fin(lod=0):
    """A vertical crown fin / pilaster rising past the parapet - varies a roof line at 60 triangles."""
    bm = bmesh.new()
    L.box(bm, (0.70, 0.46, 3.40), base_at=(0, -0.23, 0.0), m=PAN)
    L.box(bm, (0.86, 0.58, 0.18), base_at=(0, -0.29, 3.40), m=CON)                    # cap
    if lod < 2:
        L.box(bm, (0.26, 0.10, 2.60), loc=(0, -0.50, 1.70), m=EMI)                    # BLANK lit reveal
        L.box(bm, (0.44, 0.12, 0.12), loc=(0, -0.50, 3.16), m=MET)
    return bm, (0.86, 0.58, 3.58), "band"

'''
s = s.replace("\n# ------------------------------------------------------------------ PART 1: ground floor",
              new + "\n# ------------------------------------------------------------------ PART 1: ground floor")

# ---- 4. reorder SPECS and widen the anchor flag
a = s.index("SPECS = [")
b = s.index("NAMES = {")
s = s[:a] + '''SPECS = [
    # key, fn, part, collider (None = none).  Part 0 first: the main session's priority order.
    ("window_reveal_single", window_reveal_single, 0, None),
    ("window_reveal_band",   window_reveal_band,   0, None),
    ("window_reveal_storey", window_reveal_storey, 0, None),
    ("slab_band",            slab_band,            0, None),
    ("cornice_band",         cornice_band,         0, None),
    ("sill_band",            sill_band,            0, None),
    ("crown_setback",        crown_setback,        0, None),
    ("crown_fin",            crown_fin,            0, None),
    ("entry_recess",   entry_recess,   1, "UBX"),
    ("shopfront_3m",   shopfront_3m,   1, "UBX"),
    ("shopfront_6m",   shopfront_6m,   1, "UBX"),
    ("grille_closed",  grille_closed,  1, "UBX"),
    ("grille_open",    grille_open,    1, "UBX"),
    ("awning_fabric",  awning_fabric,  1, None),
    ("awning_box",     awning_box,     1, None),
    ("sign_projecting", sign_projecting, 1, None),
    ("sign_fascia",    sign_fascia,    1, None),
    ("a_frame",        a_frame,        1, "UBX"),
    ("display_plinth", display_plinth, 1, "UBX"),
    ("hvac_small",     hvac_small,     2, "UBX"),
    ("hvac_large",     hvac_large,     2, "UBX"),
    ("fan_cowl",       fan_cowl,       2, "UBX"),
    ("duct_run",       duct_run,       2, "UBX"),
    ("water_tank",     water_tank,     2, "UCX"),
    ("stair_overrun",  stair_overrun,  2, "UBX"),
    ("parapet_cap",    parapet_cap,    2, None),
    ("antenna_cluster", antenna_cluster, 2, None),
    ("roof_hatch",     roof_hatch,     2, None),
]
#: part 0 window/band pieces and part 1 shopfront pieces live on a facade; crowns and rooftop on a roof.
PREFIX = {0: "VC_Fac_", 1: "VC_Fac_", 2: "VC_Roof_"}
for _k in ("crown_setback", "crown_fin"):
    pass   # crowns keep the VC_Fac_ prefix: they are a facade/parapet module, not roof clutter
''' + s[b:]
s = s.replace('''NAMES = {k: ("VC_Fac_" if p == 1 else "VC_Roof_") + "".join(x.capitalize() for x in k.split("_"))
         for k, _, p, _ in SPECS}''',
'''NAMES = {k: PREFIX[p] + "".join(x.capitalize() for x in k.split("_")) for k, _, p, _ in SPECS}''')

# ---- 5. the third return value is now an anchor kind, not a bool
s = s.replace('''    return bm, (w, dep, hh + 0.26), True''', '''    return bm, (w, dep, hh + 0.26), "wall"''')
for old, kind in ((["(HALF, 0.26, hh + 0.26), True", "(BAY, 0.26, hh + 0.26), True",
                    "(w, 0.30, hh + 0.34), True", "(w, 0.34, hh + 0.26), True",
                    "(w, proj, z0 + 0.60), False", "(w, proj, z0 + 0.70), False",
                    "(0.18, 1.45, z0 + 0.56), False", "(w, 0.22, z0 + 0.70), False"], "wall"),
                  ((["(0.74, 0.46, 1.10), False", "(1.72, 0.92, 0.85), False"]), "ground")):
    for o in old:
        s = s.replace("return bm, " + o, "return bm, " + o.rsplit(",", 1)[0] + ', "%s"' % kind)
s = s.replace('''    return bm, (1.35, 1.05, 0.94), True''', '''    return bm, (1.35, 1.05, 0.94), "ground"''')
s = s.replace('''    return bm, (2.76, 1.76, 2.06), True''', '''    return bm, (2.76, 1.76, 2.06), "ground"''')
s = s.replace('''    return bm, (0.92, 0.92, 1.04), False''', '''    return bm, (0.92, 0.92, 1.04), "ground"''')
s = s.replace('''    return bm, (0.72, length + 0.3, 0.98), True''', '''    return bm, (0.72, length + 0.3, 0.98), "ground"''')
s = s.replace('''    return bm, (2.20, 2.20, legh + 2.16), True''', '''    return bm, (2.20, 2.20, legh + 2.16), "ground"''')
s = s.replace('''    return bm, (w + 0.16, d + 0.16, hh + 0.14), True''', '''    return bm, (w + 0.16, d + 0.16, hh + 0.14), "ground"''')
s = s.replace('''    return bm, (HALF, 0.60, 0.46), False''', '''    return bm, (HALF, 0.60, 0.46), "band"''')
s = s.replace('''    return bm, (1.10, 0.52, 3.56), False''', '''    return bm, (1.10, 0.52, 3.56), "ground"''')
s = s.replace('''    return bm, (1.16, 1.30, 0.95), False''', '''    return bm, (1.16, 1.30, 0.95), "ground"''')

s = s.replace('''    probe_bm, size, wall_anchored = fn(0)''', '''    probe_bm, size, anchor = fn(0)''')
s = s.replace('''        bm, size, wall_anchored = fn(lod)''', '''        bm, size, anchor = fn(lod)''')
s = s.replace('''        "pivot": "wall face, centred, z=0" if wall_anchored else "base centre, z=0",
        "wall_anchored": wall_anchored,''',
'''        "pivot": {"wall": "wall face, centred, z = 0 at ground",
                  "window": "wall face, centred, z = 0 at the opening sill",
                  "band": "wall face, centred, z = 0 at the band soffit",
                  "ground": "base centre, z = 0"}[anchor],
        "anchor": anchor,''')
io.open(p, "w", encoding="utf-8").write(s)
print("patched build_facade.py: 28 models, part 0 first")

import io, sys

# ---------------------------------------------------------------- towers: height-band variants
q = r"E:\VeyraCity\ArtSource\Blender\Buildings\build\build_towers.py"
t = io.open(q, encoding="utf-8").read()
a = """    ("thin31_70",        tower_glass,  31.0,  60.0,  70.0),
    ("thin31_150",       tower_glass,  31.0, 117.0, 150.0),
]"""
b = """    ("thin31_70",        tower_glass,  31.0,  60.0,  70.0),
    ("thin31_150",       tower_glass,  31.0, 117.0, 150.0),
    # height-band gap fills, from the misfit analysis (build/analyse_misfits.py): each of these closes
    # records whose FOOTPRINT already fitted an existing model but whose height fell outside 0.65-1.5x.
    ("thin31_34",        tower_glass,  31.0,  60.0,  34.0),
    ("thin31_130",       tower_glass,  31.0,  60.0, 130.0),
    ("thin31_80l",       tower_glass,  31.0, 117.0,  80.0),
    ("slab115_200",      block_slab,   54.0, 115.0, 200.0),
    ("slab80_170",       block_slab,   54.0,  80.0, 170.0),
    ("block117_62",      block_slab,  117.0, 117.0,  62.0),
]"""
if a not in t:
    print("MISS towers specs"); sys.exit(1)
io.open(q, "w", encoding="utf-8").write(t.replace(a, b, 1))

# ---------------------------------------------------------------- neon: two more
q = r"E:\VeyraCity\ArtSource\Blender\Buildings\build\build_neon.py"
t = io.open(q, encoding="utf-8").read()
a = """    ("low_18L",   modern_block, 57.0, 112.0, 18.0, PANEL_SLOTS),
]"""
b = """    ("low_18L",   modern_block, 57.0, 112.0, 18.0, PANEL_SLOTS),
    # height-band gap fills from build/analyse_misfits.py
    ("brick_62L", brick_block,  56.0, 105.0, 62.0, BRICK_SLOTS),
    ("low_14L",   modern_block, 57.0, 112.0, 14.0, PANEL_SLOTS),
]"""
if a not in t:
    print("MISS neon specs"); sys.exit(1)
io.open(q, "w", encoding="utf-8").write(t.replace(a, b, 1))

# ---------------------------------------------------------------- houses: the two garage records
q = r"E:\VeyraCity\ArtSource\Blender\Buildings\build\build_houses.py"
t = io.open(q, encoding="utf-8").read()
a = """HOUSES = {"""
b = '''def h9_garage_small():
    """D5 `garage_small`, 8 x 7 m: a single-bay detached garage with a roller door and a low-pitch roof."""
    bm = bmesh.new()
    w, d = 8.0, 7.0
    fy, by, lx, rx = -d / 2.0, d / 2.0, -w / 2.0, w / 2.0
    eave, ridge = 2.95, 4.00
    L.wall(bm, "X", fy, lx, rx, 0.0, eave, 0.26, WALL, [(0.0, 0.0, 4.20, 2.35)], None, inward=1)
    L.wall(bm, "X", by, lx, rx, 0.0, eave, 0.26, WALL, [(1.6, G_SILL, 0.90, 0.90)], GLASS,
           sill=True, m_sill=TRIM, inward=-1)
    L.wall(bm, "Y", lx, fy, by, 0.0, eave, 0.26, WALL, [], None, inward=1)
    L.wall(bm, "Y", rx, fy, by, 0.0, eave, 0.26, WALL, [(0.0, G_SILL, 0.90, 0.90)], GLASS,
           sill=True, m_sill=TRIM, inward=-1)
    L.box(bm, (4.20, 0.14, 2.35), loc=(0.0, fy + 0.12, 1.175), m=GAR)
    L.box(bm, (4.50, 0.22, 0.16), loc=(0.0, fy + 0.02, 2.46), m=TRIM)
    if L.DETAIL == 0:
        for gz in (0.62, 1.14, 1.66):
            L.box(bm, (4.00, 0.06, 0.05), loc=(0.0, fy + 0.04, gz), m=TRIM)
    L.gable_roof(bm, w, d, eave, ridge, ROOF, "X", ov=0.35)
    L.fascia(bm, w, d, eave, TRIM, ov=0.35)
    return L.new_object("VC_House_GarageSmall", bm, SLOTS_BOARD + ["VC_Metal_Garage"]), 4.00


def h10_garage_community():
    """D5 `garage_community`, 40 x 22 m: a long block of lock-up bays behind a flat roof and parapet."""
    bm = bmesh.new()
    w, d = 40.0, 22.0
    fy, by, lx, rx = -d / 2.0, d / 2.0, -w / 2.0, w / 2.0
    top = 7.00 - 0.55
    bays = 8
    span = w / bays
    L.box(bm, (w, d, top), base_at=(0, 0.0, 0.0), m=WALL)
    for i in range(bays):                                  # roller-door bays along the street side
        cx = lx + span * (i + 0.5)
        L.box(bm, (span - 0.75, 0.16, 2.60), loc=(cx, fy + 0.10, 1.30), m=GAR)
        L.box(bm, (span - 0.45, 0.26, 0.20), loc=(cx, fy + 0.02, 2.72), m=TRIM)
        if L.DETAIL == 0:
            for gz in (0.70, 1.30, 1.90):
                L.box(bm, (span - 1.15, 0.06, 0.05), loc=(cx, fy + 0.02, gz), m=TRIM)
        L.box(bm, (0.75, 0.30, top), base_at=(lx + span * i, fy + 0.15, 0.0), m=WALL)
    L.box(bm, (0.75, 0.30, top), base_at=(rx, fy + 0.15, 0.0), m=WALL)
    L.wall(bm, "X", by, lx, rx, 0.0, top, 0.30, WALL,
           L.win_row(lx, rx, 6, 1.10, 4.20, 1.20), GLASS, sill=True, m_sill=TRIM, inward=-1)
    L.box(bm, (w, d, 0.16), base_at=(0, 0, top), m=CONC)
    L.parapet(bm, w, d, top + 0.16, WALL, h=0.39, t=0.26, m_cap=CONC)
    return L.new_object("VC_House_GarageCommunity", bm, SLOTS_BOARD + ["VC_Metal_Garage"]), 7.00


#: footprint the manifest reports per model (the records these are matched against)
FOOTPRINTS = {"garage_small": (8.0, 7.0), "garage_community": (40.0, 22.0)}

HOUSES = {'''
if a not in t:
    print("MISS houses dict"); sys.exit(1)
t = t.replace(a, b, 1)
t = t.replace('''    "ranch_carport": h8_ranch_carport,
}''', '''    "ranch_carport": h8_ranch_carport,
    "garage_small": h9_garage_small,
    "garage_community": h10_garage_community,
}''')
t = t.replace('''        "footprint_m": [W, D],''', '''        "footprint_m": list(FOOTPRINTS.get(key, (W, D))),''')
io.open(q, "w", encoding="utf-8").write(t)
print("patched gap fills")

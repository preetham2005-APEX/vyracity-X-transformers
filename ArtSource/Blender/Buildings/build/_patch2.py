import io, sys

p = r"E:\VeyraCity\ArtSource\Blender\Buildings\build\vc_bld_lib.py"
s = io.open(p, encoding="utf-8").read()

# steps must rise FROM the ground (the kit base sits on the data ground level), not sink below it
s = s.replace('''def steps(bm, x, y_front, w, z_floor, m, n=3, rise=0.14, run=0.30):
    for i in range(n):
        z = z_floor - (i + 1) * rise
        box(bm, (w, run * (i + 1), rise), base_at=(x, y_front - run * (i + 1) / 2.0, z), m=m)''',
'''def steps(bm, x, y_front, w, z_floor, m, n=3, run=0.20):
    """Entrance steps rising from z = 0 to z_floor. Nothing goes below the ground plane, and the bottom
    tread projects at most n * run in front of y_front."""
    h = z_floor / float(n)
    for i in range(n):
        out = run * (n - i)
        box(bm, (w, out, h), base_at=(x, y_front - out / 2.0, i * h), m=m)


def entry_recess(bm, x0, x1, y_front, depth, z0, z_head, m_wall, m_soffit, m_floor, inward=1.0):
    """A recessed entry bay carved into the front wall (the caller leaves a full-height opening there).
    Builds the two side returns, the back wall is the caller's job (it carries the door), plus the soffit
    over the recess and the recess floor. Front faces -Y, so the recess goes toward +Y."""
    t = 0.18
    box(bm, (t, depth, z_head - z0), base_at=(x0 + t / 2.0, y_front + depth / 2.0, z0), m=m_wall)
    box(bm, (t, depth, z_head - z0), base_at=(x1 - t / 2.0, y_front + depth / 2.0, z0), m=m_wall)
    box(bm, (x1 - x0, depth, 0.14), base_at=((x0 + x1) / 2.0, y_front + depth / 2.0, z_head), m=m_soffit)
    box(bm, (x1 - x0, depth, 0.06), base_at=((x0 + x1) / 2.0, y_front + depth / 2.0, z0), m=m_floor)''')
io.open(p, "w", encoding="utf-8").write(s)

q = r"E:\VeyraCity\ArtSource\Blender\Buildings\build\build_houses.py"
t = io.open(q, encoding="utf-8").read()
reps = [
    # ---- h1: porch becomes a recessed entry bay inside the footprint
    ("""          front_wins=front + [L.door_op(-3.40, 1.05, 2.15)] + L.win_row(-6.2, 6.2, 3, 1.10, U_SILL, U_H),""",
     """          front_wins=front + [(-3.40, 0.0, 2.80, 2.75)] + L.win_row(-6.2, 6.2, 3, 1.10, U_SILL, U_H),"""),
    ("""    L.door(bm, "X", FY, -3.40, 0.0, 1.05, 2.15, T, DOOR, TRIM)
    L.porch(bm, -5.80, -1.00, FY, 1.70, 0.18, 2.75, TRIM, TRIM, CONC, posts=3)
    L.steps(bm, -3.40, FY - 1.70, 1.60, 0.18, CONC)""",
     """    L.entry_recess(bm, -4.80, -2.00, FY, 1.50, 0.0, 2.75, WALL, TRIM, CONC)
    L.wall(bm, "X", FY + 1.50, -4.80, -2.00, 0.0, 2.75, T, WALL,
           [L.door_op(-3.40, 1.05, 2.15)], None, inward=1)
    L.door(bm, "X", FY + 1.50, -3.40, 0.0, 1.05, 2.15, T, DOOR, TRIM, inward=1)
    L.steps(bm, -3.40, FY, 1.60, 0.18, CONC, n=2)"""),
    # ---- h2: shallower bay, steps on the ground
    ("""    bx, bw, bd = -3.00, 3.00, 0.85""", """    bx, bw, bd = -3.00, 3.00, 0.72"""),
    ("""    L.steps(bm, 0.90, FY, 1.60, 0.18, CONC)""", """    L.steps(bm, 0.90, FY, 1.60, 0.18, CONC, n=2)"""),
    # ---- h3: the driveway apron belongs to the road build, not the kit
    ("""    L.box(bm, (3.90, 2.60, 0.10), base_at=(4.20, FY - 1.30, 0.0), m=CONC)      # apron""",
     """    L.box(bm, (3.90, 0.45, 0.10), base_at=(4.20, FY - 0.22, 0.0), m=CONC)     # threshold only"""),
    ("""    L.steps(bm, -2.40, FY, 1.70, 0.18, CONC)""", """    L.steps(bm, -2.40, FY, 1.70, 0.18, CONC, n=2)"""),
    # ---- h4
    ("""    L.steps(bm, 5.20, FY, 1.60, 0.18, CONC)""", """    L.steps(bm, 5.20, FY, 1.60, 0.18, CONC, n=2)"""),
    # ---- h5: the canopy fin must not stand on the pavement
    ("""    L.box(bm, (0.16, 1.50, 2.60), base_at=(2.70, FY - 0.75, 0.0), m=TRIM)       # canopy fin""",
     """    L.box(bm, (0.16, 0.55, 2.60), base_at=(2.70, FY - 0.28, 0.0), m=TRIM)      # canopy fin"""),
    ("""    L.steps(bm, 0.20, FY, 1.80, 0.18, CONC)""", """    L.steps(bm, 0.20, FY, 1.80, 0.18, CONC, n=2)"""),
    # ---- h6: recessed entry instead of the projecting porch
    ("""                      L.door_op(1.50, 1.10, 2.20)],""",
     """                      (1.50, 0.0, 2.60, 2.60)],"""),
    ("""    L.door(bm, "X", FY, 1.50, 0.0, 1.10, 2.20, T, DOOR, TRIM)
    L.porch(bm, 0.10, 2.90, FY, 1.30, 0.18, 2.60, TRIM, TRIM, CONC, posts=2)
    L.steps(bm, 1.50, FY - 1.30, 1.50, 0.18, CONC)""",
     """    L.entry_recess(bm, 0.20, 2.80, FY, 1.30, 0.0, 2.60, WALL, TRIM, CONC)
    L.wall(bm, "X", FY + 1.30, 0.20, 2.80, 0.0, 2.60, T, WALL,
           [L.door_op(1.50, 1.10, 2.20)], None, inward=1)
    L.door(bm, "X", FY + 1.30, 1.50, 0.0, 1.10, 2.20, T, DOOR, TRIM, inward=1)
    L.steps(bm, 1.50, FY, 1.50, 0.18, CONC, n=2)"""),
    # ---- h7 / h8
    ("""        L.steps(bm, sx * 3.85, FY, 1.50, 0.18, CONC)""",
     """        L.steps(bm, sx * 3.85, FY, 1.50, 0.18, CONC, n=2)"""),
    ("""    L.steps(bm, -1.70, FY, 1.60, 0.18, CONC)""", """    L.steps(bm, -1.70, FY, 1.60, 0.18, CONC, n=2)"""),
    # ---- the manifest footprint is the WALL footprint the data matches against, not the eaves bbox
    ("""        "footprint_m": [round(hi.x - lo.x, 2), round(hi.y - lo.y, 2)],""",
     """        "footprint_m": [W, D],
        "bbox_m": [round(hi.x - lo.x, 2), round(hi.y - lo.y, 2), round(hi.z - lo.z, 2)],"""),
]
for a, b in reps:
    if a not in t:
        print("MISS:", a.strip()[:70])
        sys.exit(1)
    t = t.replace(a, b, 1)
io.open(q, "w", encoding="utf-8").write(t)
print("patched2")

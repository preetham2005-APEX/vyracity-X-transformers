import io, sys

p = r"E:\VeyraCity\ArtSource\Blender\Buildings\build\vc_bld_lib.py"
s = io.open(p, encoding="utf-8").read()

# 1. drop the placeholder line in gable_roof
s = s.replace("""        faces = [(0, 1, 5, 4), (3, 7 - 4, 0, 0)]  # placeholder, replaced below
        faces = [(0, 1, 5, 4), (2, 3, 4, 5), (0, 4, 3), (1, 2, 5), (0, 3, 2, 1)]""",
              """        faces = [(0, 1, 5, 4), (2, 3, 4, 5), (0, 4, 3), (1, 2, 5), (0, 3, 2, 1)]""")

# 2. explicit interior direction (deriving it from the sign of `fixed` only works for a block centred on 0)
s = s.replace("""def wall(bm, axis, fixed, start, end, z0, z1, thick, m_wall, windows=(), m_glass=None,
         reveal=0.14, sill=None, m_sill=None, sill_depth=0.07):""",
              """def wall(bm, axis, fixed, start, end, z0, z1, thick, m_wall, windows=(), m_glass=None,
         reveal=0.14, sill=None, m_sill=None, sill_depth=0.07, inward=None):""")
s = s.replace("""    wins = sorted(windows, key=lambda w: w[0])
    # interior direction: the wall face sits at `fixed`, the body goes toward the building centre
    sgn = -1.0 if fixed > 0 else 1.0""",
              """    wins = sorted(windows, key=lambda w: w[0])
    # interior direction: the wall face sits at `fixed` and the body goes inward. Callers that build a
    # block away from the origin must pass `inward` (+1/-1); the sign of `fixed` is only right for a
    # block centred on the origin.
    sgn = float(inward) if inward is not None else (-1.0 if fixed > 0 else 1.0)""")
s = s.replace("""            if sill is not None:
                box(bm, (ww + 0.18, sill_depth * 2.0, 0.07),""",
              """            if sill:
                box(bm, (ww + 0.18, sill_depth * 2.0, 0.07),""")
s = s.replace("""            if sill is not None:
                box(bm, (sill_depth * 2.0, ww + 0.18, 0.07),""",
              """            if sill:
                box(bm, (sill_depth * 2.0, ww + 0.18, 0.07),""")

# 3. door openings: the door leaf needs a real hole, like a window
s = s.replace("""def door(bm, axis, fixed, at, z0, w, h, thick, m_door, m_trim, recess=0.12):
    \"\"\"Recessed door leaf plus a surrounding architrave.\"\"\"
    sgn = -1.0 if fixed > 0 else 1.0""",
              """def door_op(at, w, h, z0=0.0):
    \"\"\"The opening spec to pass to `wall` so the door leaf sits in a real hole.\"\"\"
    return (at, z0, w, h)


def door(bm, axis, fixed, at, z0, w, h, thick, m_door, m_trim, recess=0.12, inward=None):
    \"\"\"Recessed door leaf plus a surrounding architrave. Needs a matching `door_op` in the wall.\"\"\"
    sgn = float(inward) if inward is not None else (-1.0 if fixed > 0 else 1.0)""")
io.open(p, "w", encoding="utf-8").write(s)

q = r"E:\VeyraCity\ArtSource\Blender\Buildings\build\build_houses.py"
t = io.open(q, encoding="utf-8").read()

# shell(): pass the interior direction explicitly for every wall
t = t.replace("""    ms = TRIM if sill else None
    L.wall(bm, "X", y0, x0, x1, z0, z1, T, WALL, front_wins, GLASS, sill=sill, m_sill=ms)
    L.wall(bm, "X", y1, x0, x1, z0, z1, T, WALL, back_wins, GLASS, sill=sill, m_sill=ms)
    L.wall(bm, "Y", x0, y0, y1, z0, z1, T, WALL, left_wins, GLASS, sill=sill, m_sill=ms)
    L.wall(bm, "Y", x1, y0, y1, z0, z1, T, WALL, right_wins, GLASS, sill=sill, m_sill=ms)""",
              """    ms = TRIM if sill else None
    L.wall(bm, "X", y0, x0, x1, z0, z1, T, WALL, front_wins, GLASS, sill=sill, m_sill=ms, inward=1)
    L.wall(bm, "X", y1, x0, x1, z0, z1, T, WALL, back_wins, GLASS, sill=sill, m_sill=ms, inward=-1)
    L.wall(bm, "Y", x0, y0, y1, z0, z1, T, WALL, left_wins, GLASS, sill=sill, m_sill=ms, inward=1)
    L.wall(bm, "Y", x1, y0, y1, z0, z1, T, WALL, right_wins, GLASS, sill=sill, m_sill=ms, inward=-1)""")

# every door gets an opening in the wall it sits in
reps = [
    ("""          front_wins=front + L.win_row(-6.2, 6.2, 3, 1.10, U_SILL, U_H),""",
     """          front_wins=front + [L.door_op(-3.40, 1.05, 2.15)] + L.win_row(-6.2, 6.2, 3, 1.10, U_SILL, U_H),"""),
    ("""          front_wins=[(3.60, G_SILL, 1.30, G_H)] + L.win_row(-6.2, 6.2, 3, 1.10, U_SILL, U_H),""",
     """          front_wins=[(3.60, G_SILL, 1.30, G_H), L.door_op(0.90, 1.05, 2.15)]
          + L.win_row(-6.2, 6.2, 3, 1.10, U_SILL, U_H),"""),
    ("""          front_wins=[(-5.10, G_SILL, 1.20, G_H)] + L.win_row(LX, gx0, 2, 1.10, U_SILL, U_H),""",
     """          front_wins=[(-5.10, G_SILL, 1.20, G_H), L.door_op(-2.40, 1.10, 2.20)]
          + L.win_row(LX, gx0, 2, 1.10, U_SILL, U_H),"""),
    ("""          front_wins=[(2.40, G_SILL, 1.20, G_H)] + L.win_row(mid, RX, 2, 1.10, U_SILL, U_H),""",
     """          front_wins=[(2.40, G_SILL, 1.20, G_H), L.door_op(5.20, 1.05, 2.15)]
          + L.win_row(mid, RX, 2, 1.10, U_SILL, U_H),"""),
    ("""          front_wins=[(-4.60, G_SILL, 2.40, G_H), (2.60, G_SILL, 1.10, G_H),""",
     """          front_wins=[(-4.60, G_SILL, 2.40, G_H), (2.60, G_SILL, 1.10, G_H), L.door_op(0.20, 1.10, 2.25),"""),
    ("""          front_wins=[(-4.60, G_SILL, 1.30, G_H), (-1.60, G_SILL, 1.30, G_H), (4.40, G_SILL, 1.30, G_H)],""",
     """          front_wins=[(-4.60, G_SILL, 1.30, G_H), (-1.60, G_SILL, 1.30, G_H), (4.40, G_SILL, 1.30, G_H),
                      L.door_op(1.50, 1.10, 2.20)],"""),
    ("""                      (-5.20, U_SILL, 1.10, U_H), (-2.40, U_SILL, 1.10, U_H),""",
     """                      L.door_op(-3.85, 1.05, 2.15), L.door_op(3.85, 1.05, 2.15),
                      (-5.20, U_SILL, 1.10, U_H), (-2.40, U_SILL, 1.10, U_H),"""),
    ("""          front_wins=[(0.40, G_SILL, 2.80, G_H), (5.10, G_SILL, 1.40, G_H)],""",
     """          front_wins=[(0.40, G_SILL, 2.80, G_H), (5.10, G_SILL, 1.40, G_H),
                      L.door_op(-1.70, 1.10, 2.20)],"""),
    # garage wing: opening for the garage door, and the rear utility block brought to the kit height
    ("""    L.wall(bm, "X", FY, gx0, RX, 0.0, 4.40, T, WALL, [], None)""",
     """    L.wall(bm, "X", FY, gx0, RX, 0.0, 4.40, T, WALL, [(4.20, 0.0, 3.40, 2.45)], None, inward=1)"""),
    ("""    L.box(bm, (4.20, 3.00, 5.90), base_at=(3.60, 2.60, 0.0), m=WALL)
    L.box(bm, (4.50, 3.30, 0.30), base_at=(3.60, 2.60, 5.90), m=CONC)
    return L.new_object("VC_House_RanchCarport", bm, SLOTS_BOARD), 6.20""",
     """    L.box(bm, (4.20, 3.00, 6.90), base_at=(3.60, 2.60, 0.0), m=WALL)
    L.box(bm, (4.50, 3.30, 0.30), base_at=(3.60, 2.60, 6.90), m=CONC)
    return L.new_object("VC_House_RanchCarport", bm, SLOTS_BOARD), 7.20"""),
    # sub-block walls away from the origin
    ("""    L.wall(bm, "X", BY, gx0, RX, 0.0, 4.40, T, WALL, [], None)
    L.wall(bm, "Y", RX, FY, BY, 0.0, 4.40, T, WALL,
           L.win_row(FY, BY, 2, 1.00, U_SILL, U_H), GLASS, sill=True, m_sill=TRIM)""",
     """    L.wall(bm, "X", BY, gx0, RX, 0.0, 4.40, T, WALL, [], None, inward=-1)
    L.wall(bm, "Y", RX, FY, BY, 0.0, 4.40, T, WALL,
           L.win_row(FY, BY, 2, 1.00, U_SILL, U_H), GLASS, sill=True, m_sill=TRIM, inward=-1)"""),
    ("""    L.wall(bm, "Y", cx, FY, BY, 0.0, eave, T, WALL, [], None)""",
     """    L.wall(bm, "Y", cx, FY, BY, 0.0, eave, T, WALL, [], None, inward=1)"""),
]
for a, b in reps:
    if a not in t:
        print("MISS:", a.strip()[:70])
        sys.exit(1)
    t = t.replace(a, b, 1)
io.open(q, "w", encoding="utf-8").write(t)
print("patched")

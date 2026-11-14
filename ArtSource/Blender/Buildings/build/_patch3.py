import io, re, sys

p = r"E:\VeyraCity\ArtSource\Blender\Buildings\build\vc_bld_lib.py"
s = io.open(p, encoding="utf-8").read()
start = s.index("def wall(")
end = s.index("def win_row(")
new = '''def wall(bm, axis, fixed, start, end, z0, z1, thick, m_wall, windows=(), m_glass=None,
         reveal=0.14, sill=None, m_sill=None, sill_depth=0.07, inward=None):
    """A solid wall from `start` to `end` along `axis` ('X' or 'Y'), its OUTER face at `fixed` on the other
    axis, from z0 to z1 and `thick` metres thick toward the interior.

    `windows` is a list of (centre_along_axis, z_bottom, width, height); an opening whose z_bottom is 0 is a
    door (no glass, no sill - use `door` for the leaf). Each opening is a real hole with a `reveal` deep
    recess, which is the depth the generated box shells never had (BUG-004).

    The wall is emitted storey by storey: overlapping opening z-ranges are merged into bands, a full-width
    solid band fills each gap between them, and inside a band the openings are cut by a horizontal sweep.
    Openings inside one band must not overlap horizontally (different storeys are different bands, so a
    ground-floor window and the window above it are fine).
    """
    wins = sorted(windows, key=lambda w: w[0])
    sgn = float(inward) if inward is not None else (-1.0 if fixed > 0 else 1.0)
    cen = fixed + sgn * thick / 2.0

    def seg(a, b, zz0, zz1):
        if b - a < 1e-4 or zz1 - zz0 < 1e-4:
            return
        if axis == "X":
            box(bm, (b - a, thick, zz1 - zz0), loc=((a + b) / 2.0, cen, (zz0 + zz1) / 2.0), m=m_wall)
        else:
            box(bm, (thick, b - a, zz1 - zz0), loc=(cen, (a + b) / 2.0, (zz0 + zz1) / 2.0), m=m_wall)

    # merge the openings into storey bands
    bands = []
    for w in sorted(wins, key=lambda w: w[1]):
        a, b = max(z0, w[1]), min(z1, w[1] + w[3])
        if b <= a:
            continue
        if bands and a <= bands[-1][1] + 1e-6:
            bands[-1] = (bands[-1][0], max(bands[-1][1], b), bands[-1][2] + [w])
        else:
            bands.append((a, b, [w]))

    z = z0
    for (bz0, bz1, ws) in bands:
        seg(start, end, z, bz0)                       # solid band under this storey
        cursor = start
        for (c, wz0, ww, wh) in sorted(ws, key=lambda w: w[0]):
            a, b = c - ww / 2.0, c + ww / 2.0
            seg(cursor, a, bz0, bz1)                  # pier
            seg(a, b, bz0, max(bz0, min(wz0, bz1)))   # under the opening, inside the band
            seg(a, b, max(bz0, min(wz0 + wh, bz1)), bz1)   # over the opening, inside the band
            cursor = max(cursor, b)
        seg(cursor, end, bz0, bz1)
        z = bz1
    seg(start, end, z, z1)                            # solid band above the top storey

    if m_glass is None:
        return
    gcen = fixed + sgn * (reveal + 0.03)
    for (c, wz0, ww, wh) in wins:
        if wz0 < 0.05:            # a door opening: the leaf is placed by `door`, and it never gets a sill
            continue
        if axis == "X":
            box(bm, (ww - 0.06, 0.05, wh - 0.06), loc=(c, gcen, wz0 + wh / 2.0), m=m_glass)
            if sill:
                box(bm, (ww + 0.18, sill_depth * 2.0, 0.07),
                    loc=(c, fixed - sgn * sill_depth * 0.2, wz0 - 0.035), m=m_sill)
        else:
            box(bm, (0.05, ww - 0.06, wh - 0.06), loc=(gcen, c, wz0 + wh / 2.0), m=m_glass)
            if sill:
                box(bm, (sill_depth * 2.0, ww + 0.18, 0.07),
                    loc=(fixed - sgn * sill_depth * 0.2, c, wz0 - 0.035), m=m_sill)


'''
s = s[:start] + new + s[end:]
io.open(p, "w", encoding="utf-8").write(s)

q = r"E:\VeyraCity\ArtSource\Blender\Buildings\build\build_houses.py"
t = io.open(q, encoding="utf-8").read()
a = """    L.box(bm, (0.36, D + 0.8, 0.50), base_at=(0.0, 0.0, ridge - 0.50), m=WALL)   # party wall upstand"""
b = """    # party wall between the two halves: it also closes the junction of the two gable solids
    L.box(bm, (0.50, D + 0.9, ridge + 0.10 - (eave - 0.60)), base_at=(0.0, 0.0, eave - 0.60), m=WALL)"""
if a not in t:
    print("MISS party wall"); sys.exit(1)
t = t.replace(a, b, 1)
io.open(q, "w", encoding="utf-8").write(t)
print("patched3")

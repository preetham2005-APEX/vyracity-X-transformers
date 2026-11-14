"""Fix what the first QA run found: two real model bugs, and two of my own QA rules that were wrong.

MODEL BUGS (real)
  * window reveal sill nosing dipped 5 mm below the sill line (z = 0 for a `window` anchor)
  * the fabric awning profile repeated its first point, producing 3 zero-area faces

QA RULES (mine, wrong)
  * "bbox min z must be 0" was applied to every anchor. For a `wall` anchor the pivot is at GROUND and
    the piece carries its own mounting height - an awning legitimately starts at 3.3 m. The rule only
    holds for `window` (sill line), `band` (band soffit) and `ground` (base) anchors.
  * "centred on x" was applied to every prop. It is a tiling requirement for `window` and `band`
    modules; for a free-standing prop an off-centre ladder or downpipe is correct, and the water tank's
    BODY is centred on the pivot, which is what placement needs.
"""
import io
import os

HERE = os.path.dirname(os.path.abspath(__file__))

# ---------------------------------------------------------------- model fixes
p = os.path.join(HERE, "build_facade.py")
s = io.open(p, encoding="utf-8").read()
a = '''        L.box(bm, (w + 0.10, 0.06, 0.05), loc=(0, -depth - 0.05, 0.02), m=CON)        # sill nosing'''
b = '''        L.box(bm, (w + 0.10, 0.06, 0.05), loc=(0, -depth - 0.05, 0.025), m=CON)       # sill nosing'''
assert a in s, "sill nosing line not found"
s = s.replace(a, b, 1)

a = '''    pts = [(0.0, z0 + 0.42)]
    for i in range(seg + 1):                                                        # sagging canopy
        t = i / seg
        pts.append((-proj * t, z0 + 0.42 - 0.52 * t - 0.10 * math.sin(math.pi * t)))'''
b = '''    pts = [(0.0, z0 + 0.42)]
    for i in range(1, seg + 1):               # from 1: i = 0 repeats the point above -> zero-area faces
        t = i / seg
        pts.append((-proj * t, z0 + 0.42 - 0.52 * t - 0.10 * math.sin(math.pi * t)))'''
assert a in s, "awning profile loop not found"
s = s.replace(a, b, 1)
io.open(p, "w", encoding="utf-8").write(s)
print("  build_facade.py: sill nosing lifted to the sill line; awning profile duplicate point removed")

# ---------------------------------------------------------------- QA rule fixes
q = os.path.join(HERE, "qa_check.py")
s = io.open(q, encoding="utf-8").read()
a = '''        if abs(lo[2]) > 1e-3:
            rec["problems"].append("anchor not at z = 0: %.4f" % lo[2])
        if abs((lo[0] + hi[0]) / 2.0) > 0.02:
            rec["problems"].append("not centred on x: centre %.3f" % ((lo[0] + hi[0]) / 2.0))
        anchor = src.get("anchor")'''
b = '''        anchor = src.get("anchor")
        # Where z = 0 has to land depends on the anchor kind. `window` puts it on the sill line, `band`
        # on the band soffit and `ground` on the base, so the lowest geometry must sit there. A `wall`
        # piece pivots at GROUND and carries its own mounting height, so an awning starting at 3.3 m is
        # correct - only a piece reaching BELOW the ground plane is wrong.
        if anchor in ("window", "band", "ground"):
            if abs(lo[2]) > 1e-3:
                rec["problems"].append("anchor not at z = 0: %.4f" % lo[2])
        elif lo[2] < -1e-3:
            rec["problems"].append("wall-anchored piece reaches below the ground plane: %.4f" % lo[2])
        # Centring on x is a tiling requirement for the repeating modules. A free-standing prop may be
        # asymmetric (a tank's ladder and downpipe) as long as its body sits on the pivot.
        if anchor in ("window", "band") and abs((lo[0] + hi[0]) / 2.0) > 0.02:
            rec["problems"].append("not centred on x: centre %.3f" % ((lo[0] + hi[0]) / 2.0))'''
assert a in s, "QA anchor block not found"
s = s.replace(a, b, 1)
io.open(q, "w", encoding="utf-8").write(s)
print("  qa_check.py: anchor-z and x-centre rules scoped per anchor kind")

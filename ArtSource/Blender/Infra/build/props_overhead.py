"""VEYRA CITY - original overhead road infrastructure (gantries, signal mast arms, utility poles).

Same metadata contract as props_road.py. Conventions:
  * the prop front is Blender -Y and becomes Unity +Z,
  * gantry spans and mast arms reach out along Blender -X, i.e. Unity +X,
  * sign panels carry NO text or symbol geometry - they are blank faces for Veyra's own signage.
"""
import math

from mathutils import Matrix, Vector

from vc_infra_lib import MeshBuilder, box, prism, sweep, tube

STEEL_GALV = "VC_Steel_Galv"
STEEL_DARK = "VC_Steel_Dark"
CONC = "VC_Concrete_Struct"
PANEL = "VC_Sign_BlankFace"      # canonical; shared with the street set's blank sign plates
TIMBER = "VC_Timber_Pole"
PORC = "VC_Porcelain"
LENS = "VC_Signal_Lens"
COLMAT = "VC_Collision"


# ------------------------------------------------------------------------------ 5a gantry truss
# Box truss, 0.90 m wide x 1.25 m deep, 4 chord tubes with X-laced sides and laced top/bottom.

TRUSS_W, TRUSS_D = 0.90, 1.25


def _truss_lod(detail):
    return {
        0: dict(chord_sides=8, web_sides=6, vert_every=1, tb_cross=1, tb_diag=True, rails=True),
        1: dict(chord_sides=6, web_sides=4, vert_every=2, tb_cross=2, tb_diag=False, rails=True),
        2: dict(chord_sides=4, web_sides=4, vert_every=0, tb_cross=0, tb_diag=False, rails=False),
    }[detail]


def _truss(mb, span, detail):
    p = _truss_lod(detail)
    panels = max(4, int(round(span / 1.55)))
    xs = [-span * i / panels for i in range(panels + 1)]
    hy, zs = TRUSS_W * 0.5, (0.0, TRUSS_D)
    for y in (-hy, hy):
        for z in zs:
            mb.add(tube((0.0, y, z), (-span, y, z), 0.055, sides=p["chord_sides"]), STEEL_GALV)
    r, s = 0.030, p["web_sides"]
    for y in (-hy, hy):            # laced side faces
        for i in range(panels):
            x0, x1 = xs[i], xs[i + 1]
            if detail < 2:
                mb.add(tube((x0, y, 0.0), (x1, y, TRUSS_D), r, sides=s), STEEL_GALV)
                mb.add(tube((x0, y, TRUSS_D), (x1, y, 0.0), r, sides=s), STEEL_GALV)
            elif i % 2 == 0 and y > 0:
                mb.add(tube((x0, y, 0.0), (x1, y, TRUSS_D), r, sides=s), STEEL_GALV)
        if p["vert_every"]:
            for i in range(1, panels):
                if i % p["vert_every"] == 0:
                    mb.add(tube((xs[i], y, 0.0), (xs[i], y, TRUSS_D), r, sides=s), STEEL_GALV)
    if p["tb_cross"]:
        for z in zs:
            for i in range(panels + 1):
                if i % p["tb_cross"] == 0:
                    mb.add(tube((xs[i], -hy, z), (xs[i], hy, z), r, sides=s), STEEL_GALV)
    if p["tb_diag"]:
        for z in zs:
            for i in range(panels):
                a, b = (-hy, hy) if i % 2 == 0 else (hy, -hy)
                mb.add(tube((xs[i], a, z), (xs[i + 1], b, z), r, sides=s), STEEL_GALV)
    if p["rails"]:                 # sign hanger rails under the bottom chord
        for y in (-0.30, 0.30):
            mb.add(prism((0.0, y, -0.055), (-span, y, -0.055), 0.045, sides=4), STEEL_GALV)
    for x in (0.0, -span):         # end connection plates
        mb.add(box((0.025, TRUSS_W + 0.10, TRUSS_D + 0.14), (x, 0.0, TRUSS_D * 0.5)), STEEL_GALV)


def _gantry_span(span):
    name = "VC_Gantry_Truss_%dm" % int(round(span))
    lods = []
    for d in (0, 1, 2):
        mb = MeshBuilder()
        _truss(mb, span, d)
        lods.append(mb.build("%s_LOD%d" % (name, d)))
    return dict(name=name, lods=lods, collision=None, collision_type="none (overhead)",
                pivot="start of the span, bottom-chord plane, truss centreline (x = y = z = 0)",
                notes="%.0f m box-truss span, 0.90 m wide x 1.25 m deep, 110 mm chords. The span "
                      "reaches toward Unity +X; set the pivot at the leg-top level (legs are 7.60 m, "
                      "giving 6.35 m clear under a 4.80 x 2.40 m panel hung from the rails)." % span)


def build_gantry_truss_12m():
    return _gantry_span(12.0)


def build_gantry_truss_20m():
    return _gantry_span(20.0)


# ------------------------------------------------------------------------------- 5b gantry legs

LEG_H = 7.60


def build_gantry_leg_lattice():
    name = "VC_Gantry_Leg_Lattice"
    a = 0.275
    corners = [(-a, -a), (a, -a), (a, a), (-a, a)]
    lods = []
    for d in (0, 1, 2):
        cs = {0: 8, 1: 6, 2: 4}[d]
        ws = {0: 6, 1: 4, 2: 4}[d]
        panels = {0: 8, 1: 8, 2: 4}[d]
        mb = MeshBuilder()
        for (x, y) in corners:
            mb.add(tube((x, y, 0.05), (x, y, LEG_H), 0.045, sides=cs), STEEL_GALV)
        zs = [0.25 + (LEG_H - 0.45) * i / panels for i in range(panels + 1)]
        for f in range(4):
            p0, p1 = corners[f], corners[(f + 1) % 4]
            for i in range(panels):
                if d == 2 and (i % 2 or f % 2):
                    continue
                mb.add(tube((p0[0], p0[1], zs[i]), (p1[0], p1[1], zs[i + 1]), 0.024, sides=ws),
                       STEEL_GALV)
            if d == 0:
                for i in range(0, panels + 1, 2):
                    mb.add(tube((p0[0], p0[1], zs[i]), (p1[0], p1[1], zs[i]), 0.024, sides=ws),
                           STEEL_GALV)
        mb.add(box((0.90, 0.90, 0.05), (0.0, 0.0, 0.025)), STEEL_GALV)
        mb.add(box((0.70, 0.70, 0.04), (0.0, 0.0, LEG_H + 0.02)), STEEL_GALV)
        if d == 0:
            for (x, y) in ((-0.38, -0.38), (0.38, -0.38), (0.38, 0.38), (-0.38, 0.38)):
                mb.add(prism((x, y, 0.05), (x, y, 0.14), 0.026, sides=6), STEEL_GALV)
        lods.append(mb.build("%s_LOD%d" % (name, d)))
    mb = MeshBuilder()
    mb.add(box((0.68, 0.68, LEG_H), (0.0, 0.0, LEG_H * 0.5)), COLMAT)
    col = mb.build("UBX_%s" % name)
    return dict(name=name, lods=lods, collision=col, collision_type="UBX (BoxCollider)",
                pivot="base centre, z = 0 at the top of the foundation",
                notes="7.60 m laced steel upright, 0.55 m square, 0.90 m base plate with four anchor "
                      "bolts. Pairs with either gantry span.")


def build_gantry_leg_concrete():
    name = "VC_Gantry_Leg_Concrete"
    lods = []

    def shaft_profile(detail, w, d_):
        if detail == 0:
            c = 0.07
            return [(-w + c, -d_), (w - c, -d_), (w, -d_ + c), (w, d_ - c),
                    (w - c, d_), (-w + c, d_), (-w, d_ - c), (-w, -d_ + c)]
        return [(-w, -d_), (w, -d_), (w, d_), (-w, d_)]

    for d in (0, 1, 2):
        mb = MeshBuilder()
        if d == 0:
            zs = [0.25, 2.80, 5.30, LEG_H]
        elif d == 1:
            zs = [0.25, LEG_H]
        else:
            zs = [0.0, LEG_H]
        profs, sec = [], []
        for z in zs:
            t = (z - zs[0]) / (LEG_H - zs[0])
            w = 0.425 - 0.100 * t
            dd = 0.325 - 0.050 * t
            profs.append(shaft_profile(d, w, dd))
            sec.append(z)
        n = len(profs[0])
        verts, faces, sm = [], [], []
        for z, prof in zip(sec, profs):
            for (x, y) in prof:
                verts.append(Vector((x, y, z)))
        for s in range(len(sec) - 1):
            o0, o1 = s * n, (s + 1) * n
            for j in range(n):
                k = (j + 1) % n
                faces.append((o0 + j, o0 + k, o1 + k, o1 + j))
                sm.append(False)
        faces.append(tuple(range(n)))
        faces.append(tuple(range(len(verts) - 1, len(verts) - n - 1, -1)))
        sm += [False, False]
        mb.add((verts, faces, sm), CONC)
        if d < 2:
            mb.add(box((1.05, 0.85, 0.30), (0.0, 0.0, 0.15)), CONC)             # plinth
            mb.add(box((0.80, 0.70, 0.22), (0.0, 0.0, LEG_H + 0.11)), CONC)     # capital
            mb.add(box((0.62, 0.56, 0.04), (0.0, 0.0, LEG_H + 0.24)), STEEL_GALV)
        else:
            mb.add(box((1.05, 0.85, 0.30), (0.0, 0.0, 0.15)), CONC)
        lods.append(mb.build("%s_LOD%d" % (name, d)))
    mb = MeshBuilder()
    mb.add(box((0.90, 0.70, LEG_H + 0.35), (0.0, 0.0, (LEG_H + 0.35) * 0.5)), COLMAT)
    col = mb.build("UBX_%s" % name)
    return dict(name=name, lods=lods, collision=col, collision_type="UBX (BoxCollider)",
                pivot="base centre, z = 0 at the top of the foundation",
                notes="7.60 m tapered concrete upright (0.85 x 0.65 m at the plinth, 0.65 x 0.55 m at "
                      "the capital) with lift-joint steps and a steel bearing plate at 7.84 m.")


# ------------------------------------------------------------------------------- 5c sign panels

def _sign_panel(w, h, name):
    lods = []
    for d in (0, 1, 2):
        mb = MeshBuilder()
        mb.add(box((w, 0.040, h), (0.0, 0.0, -h * 0.5)), PANEL)
        if d == 0:
            for z in (-h * 0.22, -h * 0.78):
                mb.add(box((w - 0.10, 0.075, 0.090), (0.0, 0.058, z)), STEEL_GALV)
            for x in (-w * 0.34, w * 0.34):
                for z in (-h * 0.22, -h * 0.78):
                    mb.add(box((0.085, 0.130, 0.130), (x, 0.085, z)), STEEL_GALV)
        elif d == 1:
            for x in (-w * 0.34, w * 0.34):
                mb.add(box((0.085, 0.130, h * 0.70), (x, 0.085, -h * 0.5)), STEEL_GALV)
        lods.append(mb.build("%s_LOD%d" % (name, d)))
    return dict(name=name, lods=lods, collision=None, collision_type="none (overhead)",
                pivot="TOP centre of the panel face - the panel hangs from the gantry rails, so the "
                      "pivot is the hanger line, not the base centre (deliberate deviation)",
                notes="%.2f x %.2f m BLANK panel, 40 mm face, rear stiffeners and hanger brackets. No "
                      "text or symbol geometry: Veyra's own signage is applied on the Unity side. The "
                      "face looks toward Unity +Z." % (w, h))


def build_sign_panel_large():
    return _sign_panel(4.80, 2.40, "VC_SignPanel_Blank_4800x2400")


def build_sign_panel_small():
    return _sign_panel(2.40, 1.50, "VC_SignPanel_Blank_2400x1500")


# -------------------------------------------------------------------- 6 signal mast arm + head

def build_signal_mast():
    name = "VC_SignalMast_Cantilever"
    pole_h, reach = 7.00, 5.50
    lods = []
    for d in (0, 1, 2):
        sides = {0: 10, 1: 6, 2: 4}[d]
        segs = {0: 4, 1: 2, 2: 1}[d]
        mb = MeshBuilder()
        mb.add(tube((0.0, 0.0, 0.0), (0.0, 0.0, pole_h), 0.160, 0.120, sides=sides), STEEL_DARK)
        if d < 2:
            mb.add(box((0.52, 0.52, 0.045), (0.0, 0.0, 0.0225)), STEEL_DARK)
            mb.add(tube((0.0, 0.0, 0.045), (0.0, 0.0, 0.330), 0.230, 0.185, sides=max(6, sides - 2)),
                   STEEL_DARK)
        if d == 0:
            for k in range(4):
                a = math.pi * 0.25 + k * math.pi * 0.5
                x, y = math.cos(a) * 0.205, math.sin(a) * 0.205
                mb.add(prism((x, y, 0.045), (x, y, 0.125), 0.022, sides=6), STEEL_GALV)
        pts = []
        for i in range(segs + 1):
            t = i / segs
            pts.append(Vector((-reach * t, 0.0, 6.40 + 0.35 * t * t)))
        for i in range(segs):
            r0 = 0.095 - 0.040 * (i / segs)
            r1 = 0.095 - 0.040 * ((i + 1) / segs)
            mb.add(tube(pts[i], pts[i + 1], r0, r1, sides=max(4, sides - 2)), STEEL_DARK)
        if d < 2:
            mb.add(box((0.34, 0.26, 0.34), (-0.12, 0.0, 6.42)), STEEL_DARK)   # elbow casting
        lods.append(mb.build("%s_LOD%d" % (name, d)))
    mb = MeshBuilder()
    mb.add(prism((0.0, 0.0, 0.0), (0.0, 0.0, pole_h), 0.175, 0.135, sides=8), COLMAT)
    col = mb.build("UCX_%s" % name)
    return dict(name=name, lods=lods, collision=col, collision_type="UCX (convex MeshCollider)",
                pivot="base centre of the upright, z = 0 at the top of the foundation",
                notes="7.00 m tapered upright (320 -> 240 mm) with a 5.50 m cantilever arm rising from "
                      "6.40 to 6.75 m. The arm reaches toward Unity +X; heads hang below it.")


def build_signal_head_3():
    name = "VC_Signal_Head_3"
    lods = []
    for d in (0, 1):
        mb = MeshBuilder()
        mb.add(box((0.36, 0.30, 1.16), (0.0, 0.0, -0.62)), STEEL_DARK)          # housing body
        mb.add(box((0.70, 0.030, 1.42), (0.0, 0.085, -0.73)), STEEL_DARK)       # backplate
        mb.add(box((0.17, 0.17, 0.12), (0.0, 0.0, -0.06)), STEEL_DARK)           # arm bracket
        for i in range(3):
            z = -0.25 - i * 0.37
            if d == 0:
                mb.add(tube((0.0, -0.150, z), (0.0, -0.172, z), 0.125, sides=8), LENS)
                mb.add(tube((0.0, -0.172, z + 0.035), (0.0, -0.330, z + 0.075), 0.145, 0.150,
                            sides=8), STEEL_DARK)
            else:
                mb.add(prism((0.0, -0.150, z), (0.0, -0.180, z), 0.125, sides=6), LENS)
            if d == 0:
                mb.add(box((0.33, 0.035, 0.030), (0.0, -0.140, z + 0.185)), STEEL_DARK)
        lods.append(mb.build("%s_LOD%d" % (name, d)))
    return dict(name=name, lods=lods, collision=None, collision_type="none (overhead)",
                pivot="TOP centre of the mounting bracket - the head hangs from a mast arm, so the "
                      "pivot is the mount point, not the base centre (deliberate deviation)",
                notes="3-head housing, 1.16 m body + backplate, 300 mm lenses with visors. Lenses face "
                      "Unity +Z (toward oncoming traffic). Under 2 m, so LOD0 + LOD1 only.")


# ---------------------------------------------------------------------------- 7 utility poles

def _insulator(mb, x, y, z, detail, scale=1.0):
    s = scale
    if detail == 0:
        mb.add(prism((x, y, z), (x, y, z + 0.14 * s), 0.018 * s, sides=6), STEEL_GALV)
        mb.add(tube((x, y, z + 0.14 * s), (x, y, z + 0.20 * s), 0.055 * s, 0.045 * s, sides=6), PORC)
        mb.add(tube((x, y, z + 0.20 * s), (x, y, z + 0.28 * s), 0.065 * s, 0.032 * s, sides=6), PORC)
    elif detail == 1:
        mb.add(tube((x, y, z + 0.08 * s), (x, y, z + 0.26 * s), 0.058 * s, 0.034 * s, sides=4), PORC)


def _crossarm(mb, z, length, detail, braces=True):
    mb.add(box((length, 0.090, 0.115), (-length * 0.5 + length * 0.5, 0.0, z)), TIMBER)
    if braces and detail < 2:
        for sx in (-1.0, 1.0):
            # diagonal strut: pole foot (low, inboard) up to the arm soffit (high, outboard)
            v = [(sx * 0.06, -0.035, z - 0.62), (sx * 0.70, -0.035, z - 0.09),
                 (sx * 0.70, -0.035, z - 0.02), (sx * 0.06, -0.035, z - 0.55),
                 (sx * 0.06, 0.035, z - 0.62), (sx * 0.70, 0.035, z - 0.09),
                 (sx * 0.70, 0.035, z - 0.02), (sx * 0.06, 0.035, z - 0.55)]
            f = [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]
            mb.add((v, f, [False] * 6), STEEL_GALV)


def _pole(mb, height, r0, r1, detail, steps=0):
    sides = {0: 10, 1: 6, 2: 4}[detail]
    mb.add(tube((0.0, 0.0, -0.10), (0.0, 0.0, height), r0, r1, sides=sides), TIMBER)
    if detail == 0 and steps:
        for i in range(steps):
            z = 2.20 + i * 0.45
            sx = -1.0 if i % 2 else 1.0
            mb.add(prism((0.0, 0.0, z), (sx * 0.22, 0.0, z), 0.019, sides=4), STEEL_GALV)


def build_utility_pole_timber():
    name = "VC_UtilityPole_Timber"
    h = 11.00
    lods = []
    for d in (0, 1, 2):
        mb = MeshBuilder()
        _pole(mb, h, 0.145, 0.095, d, steps=6)
        if d < 2:
            _crossarm(mb, 10.20, 2.45, d)
            for i, x in enumerate((-1.05, -0.35, 0.35, 1.05)):
                _insulator(mb, x, 0.0, 10.26, d)
            mb.add(box((0.10, 0.28, 0.55), (0.0, -0.17, 8.95)), TIMBER)     # secondary rack
            for z in (8.78, 9.00, 9.22):
                if d == 0:
                    mb.add(tube((0.0, -0.30, z), (0.0, -0.37, z), 0.050, 0.038, sides=6), PORC)
                else:
                    mb.add(prism((0.0, -0.30, z), (0.0, -0.37, z), 0.045, sides=4), PORC)
        else:
            _crossarm(mb, 10.20, 2.45, d, braces=False)
        lods.append(mb.build("%s_LOD%d" % (name, d)))
    mb = MeshBuilder()
    mb.add(prism((0.0, 0.0, 0.0), (0.0, 0.0, h), 0.160, 0.110, sides=8), COLMAT)
    col = mb.build("UCX_%s" % name)
    return dict(name=name, lods=lods, collision=col, collision_type="UCX (convex MeshCollider)",
                pivot="base centre at the ground line (z = 0); the butt continues to -0.10 m",
                notes="11.00 m timber distribution pole, 290 -> 190 mm, one 2.45 m crossarm at 10.20 m "
                      "with four pin insulators, a three-spool secondary rack at 8.95 m and pole steps. "
                      "NO cables - the catenary is generated procedurally in Unity. Crossarm axis runs "
                      "along Unity X.")


def build_utility_pole_transmission():
    name = "VC_UtilityPole_Transmission"
    h = 14.50
    lods = []
    for d in (0, 1, 2):
        mb = MeshBuilder()
        _pole(mb, h, 0.175, 0.105, d, steps=10)
        if d < 2:
            _crossarm(mb, 13.60, 3.00, d)
            for x in (-1.30, 0.0, 1.30):
                _insulator(mb, x, 0.0, 13.66, d, scale=1.25)
            _crossarm(mb, 12.30, 2.45, d)
            for x in (-1.05, -0.35, 0.35, 1.05):
                _insulator(mb, x, 0.0, 12.36, d)
            mb.add(prism((0.0, 0.0, h), (0.0, 0.0, h + 0.45), 0.035, sides=4), STEEL_GALV)
        else:
            _crossarm(mb, 13.60, 3.00, d, braces=False)
            _crossarm(mb, 12.30, 2.45, d, braces=False)
        lods.append(mb.build("%s_LOD%d" % (name, d)))
    mb = MeshBuilder()
    mb.add(prism((0.0, 0.0, 0.0), (0.0, 0.0, h), 0.190, 0.120, sides=8), COLMAT)
    col = mb.build("UCX_%s" % name)
    return dict(name=name, lods=lods, collision=col, collision_type="UCX (convex MeshCollider)",
                pivot="base centre at the ground line (z = 0); the butt continues to -0.10 m",
                notes="14.50 m timber transmission-style pole, 350 -> 210 mm, 3.00 m upper arm at "
                      "13.60 m (three insulators) and 2.45 m lower arm at 12.30 m (four), plus an "
                      "earth-wire spike. NO cables.")


BUILDERS = [build_gantry_truss_12m, build_gantry_truss_20m, build_gantry_leg_lattice,
            build_gantry_leg_concrete, build_sign_panel_large, build_sign_panel_small,
            build_signal_mast, build_signal_head_3, build_utility_pole_timber,
            build_utility_pole_transmission]

"""Road-wear set 2: ground-plane contact (VISUAL_GAP_ANALYSIS gap #4) and clean road markings.

Same rules as vc_tex.py: everything is seeded numpy noise, no photographs or traced imagery; the
benchmark captures were read for placement and shape only, never for colour or contrast.

Two kinds of output:
  ATLASES2 - decal atlases, 2 x 2 cells, alpha faded to 0 at every cell border,
  TILING   - seamless tiling MATERIAL textures (no border fade, opaque), for the footway surface.
"""
import numpy as np

from vc_tex import (ASPHALT_SMOOTH, PAINT_T, WHITE, base, blur, border_fade, fbm, finish, rng,
                    seg_distance, ss, tint, uv, voronoi, _blob, _crack_net)

CONCRETE_SMOOTH = 0.20


# ------------------------------------------------------------------------------ shape helpers

def rect(n, u0, u1, v0, v1, soft=0.004, wobble=0.0, seed=0):
    u, v = uv(n)
    w = 0.0 if wobble == 0.0 else wobble * (fbm(n, 7, 7, seed, octaves=4) - 0.5) * 2.0
    return np.clip(ss(0.0, soft, u - u0 + w) * ss(0.0, soft, u1 - u + w)
                   * ss(0.0, soft, v - v0 + w) * ss(0.0, soft, v1 - v + w), 0, 1)


def half_plane(n, p, q, soft=0.004):
    """1 on the left of the directed line p -> q."""
    u, v = uv(n)
    dx, dy = q[0] - p[0], q[1] - p[1]
    s = ((u - p[0]) * dy - (v - p[1]) * dx) / np.hypot(dx, dy)
    return ss(soft * 0.5, -soft * 0.5, s)


def triangle(n, a, b, c, soft=0.004):
    return half_plane(n, a, b, soft) * half_plane(n, b, c, soft) * half_plane(n, c, a, soft)


def paint_wear(n, mask, seed, wear=0.26, scuff=0.30):
    """Light, patchy wear on an otherwise intact marking, plus a tyre-crossing scuff."""
    erosion = fbm(n, 6, 14, seed, octaves=4)
    keep = ss(wear - 0.10, wear + 0.20, erosion)
    cross = ss(0.58, 0.92, fbm(n, 16, 5, seed + 3, octaves=3)) * scuff
    return np.clip(mask * keep * (1.0 - cross), 0, 1), cross


def paint_decal(d, n, m, cross, colour=WHITE, thickness=PAINT_T):
    d["alpha"] = m
    tint(d, colour)
    d["albedo"] *= (1.0 - 0.34 * cross)[:, :, None]
    d["height"] = thickness * m
    d["smooth"] = ASPHALT_SMOOTH + 0.12 * m
    d["ao"] = 1.0 - 0.05 * m
    return finish(d, n)


# -------------------------------------------------------------------------- clean road markings
# World cell 4.00 x 4.00 m, so 1 cell unit = 4 m. Geometry is drawn at true size.

def mark_crosswalk_block(n, seed=701):
    d = base(n)
    m = np.zeros((n, n))
    for cu in (0.17, 0.50, 0.83):                     # 0.50 m stripes on 1.32 m centres
        m = np.maximum(m, rect(n, cu - 0.0625, cu + 0.0625, 0.055, 0.945,
                               soft=0.003, wobble=0.004, seed=seed + int(cu * 100)))
    m, cross = paint_wear(n, m, seed, wear=0.24, scuff=0.42)
    return paint_decal(d, n, m, cross)


def mark_arrow_straight(n, seed=709):
    d = base(n)
    shaft = rect(n, 0.4625, 0.5375, 0.12, 0.74, soft=0.003, wobble=0.003, seed=seed)
    head = triangle(n, (0.500, 0.955), (0.375, 0.715), (0.625, 0.715), soft=0.004)
    m = np.clip(np.maximum(shaft, head), 0, 1)
    m, cross = paint_wear(n, m, seed, wear=0.22, scuff=0.30)
    return paint_decal(d, n, m, cross)


def mark_arrow_turn(n, seed=719):
    d = base(n)
    u, v = uv(n)
    shaft = rect(n, 0.3625, 0.4375, 0.12, 0.62, soft=0.003, wobble=0.003, seed=seed)
    bend = ss(0.0375, 0.030, np.abs(np.hypot(u - 0.70, v - 0.62) - 0.2625)) \
        * ss(0.0, 0.01, u - 0.40) * ss(0.0, 0.01, 0.885 - v) * ss(0.0, 0.01, v - 0.615)
    head = triangle(n, (0.945, 0.700), (0.705, 0.820), (0.705, 0.580), soft=0.004)
    arm = rect(n, 0.66, 0.74, 0.845, 0.885, soft=0.004)
    m = np.clip(np.maximum(np.maximum(shaft, bend), np.maximum(head, arm * 0.0)), 0, 1)
    m, cross = paint_wear(n, m, seed, wear=0.22, scuff=0.30)
    return paint_decal(d, n, m, cross)


def mark_stop_bar(n, seed=727):
    d = base(n)
    bar = rect(n, 0.035, 0.965, 0.425, 0.575, soft=0.003, wobble=0.004, seed=seed)
    m, cross = paint_wear(n, bar, seed, wear=0.28, scuff=0.46)   # every wheel crosses a stop bar
    return paint_decal(d, n, m, cross)


# ----------------------------------------------------------------------- puddles and wet sheen
# World cell 4.00 x 4.00 m. Water darkens the albedo and raises smoothness; relief is ripple only.

def _ripples(n, seed, amp=0.0004, cu=9, cv=14):
    return amp * (fbm(n, cu, cv, seed, octaves=4) - 0.5) * 2.0


def puddle_sheet(n, seed=801):
    d = base(n)
    core, dist = _blob(n, 0.5, 0.48, 0.40, seed, warp=0.38)
    shallow = np.clip(ss(0.52, 0.34, dist) - core * 0.6, 0, 1)
    m = np.clip(core * 0.95 + shallow * 0.55, 0, 1)
    d["alpha"] = m
    tint(d, (0.030, 0.030, 0.032))
    tint(d, (0.018, 0.018, 0.020), core)
    d["smooth"] = ASPHALT_SMOOTH + 0.62 * core + 0.22 * shallow
    d["height"] = _ripples(n, seed + 1) * core
    d["ao"] = 1.0 - 0.10 * core
    return finish(d, n)


def puddle_gutter(n, seed=809):
    d = base(n)
    u, v = uv(n)
    band = ss(0.30, 0.02, u + 0.10 * (fbm(n, 2, 6, seed, octaves=4) - 0.5) * 2.0)
    broken = ss(0.30, 0.62, fbm(n, 2, 7, seed + 2, octaves=4))
    m = np.clip(band * broken, 0, 1)
    d["alpha"] = m * 0.95
    tint(d, (0.022, 0.022, 0.024))
    d["smooth"] = ASPHALT_SMOOTH + 0.60 * m
    d["height"] = _ripples(n, seed + 1, cu=5, cv=22) * m
    d["ao"] = 1.0 - 0.12 * m
    return finish(d, n)


def puddle_drying_ring(n, seed=811):
    d = base(n)
    core, dist = _blob(n, 0.5, 0.5, 0.22, seed, warp=0.4)
    damp = np.clip(ss(0.40, 0.22, dist) - core * 0.85, 0, 1)
    d["alpha"] = np.clip(core * 0.9 + damp * 0.6, 0, 1)
    tint(d, (0.040, 0.039, 0.038), damp)              # drying rim: dark but matt
    tint(d, (0.020, 0.020, 0.022), core)
    d["smooth"] = ASPHALT_SMOOTH + 0.55 * core + 0.10 * damp
    d["height"] = _ripples(n, seed + 1, amp=0.0003) * core
    d["ao"] = 1.0 - 0.08 * core
    return finish(d, n)


def puddle_damp_patch(n, seed=821):
    d = base(n)
    m = ss(0.34, 0.74, fbm(n, 3, 3, seed, octaves=5))
    m *= 0.7 + 0.3 * fbm(n, 12, 12, seed + 1, octaves=3)
    d["alpha"] = np.clip(m * 0.75, 0, 1)
    tint(d, (0.038, 0.038, 0.039))
    d["smooth"] = ASPHALT_SMOOTH + 0.30 * m
    d["ao"] = 1.0 - 0.06 * m
    return finish(d, n)


# ------------------------------------------------------------------------ ground-plane contact
# World cell 2.00 x 2.00 m. Gap #4: nothing in Veyra grounds a prop or meets the kerb.

def contact_base_ring(n, seed=901):
    d = base(n)
    u, v = uv(n)
    r = np.hypot(u - 0.5, v - 0.5) + 0.03 * (fbm(n, 6, 6, seed, octaves=4) - 0.5) * 2.0
    ring = ss(0.20, 0.055, r)                         # grime banked against a post or pole base
    ring *= 0.65 + 0.35 * fbm(n, 14, 14, seed + 1, octaves=3)
    grit = ss(0.70, 0.90, fbm(n, 46, 46, seed + 2, octaves=3)) * ss(0.22, 0.06, r)
    d["alpha"] = np.clip(ring * 0.80 + grit * 0.7, 0, 1)
    tint(d, (0.026, 0.025, 0.023))
    tint(d, (0.072, 0.066, 0.052), grit)
    d["height"] = 0.0015 * grit
    d["smooth"] = ASPHALT_SMOOTH - 0.12 * ring
    d["ao"] = 1.0 - 0.45 * ring
    return finish(d, n)


def contact_footway_kerb_line(n, seed=907):
    d = base(n)
    u, v = uv(n)
    edge = u + 0.05 * (fbm(n, 2, 10, seed, octaves=4) - 0.5) * 2.0
    line = ss(0.085, 0.004, edge)                     # tight dirt line in the kerb/footway joint
    spread = ss(0.42, 0.06, edge) * (0.5 + 0.5 * fbm(n, 4, 12, seed + 1, octaves=3))
    moss = ss(0.72, 0.92, fbm(n, 30, 10, seed + 2, octaves=3)) * ss(0.12, 0.01, edge)
    d["alpha"] = np.clip(line * 0.85 + spread * 0.40 + moss * 0.6, 0, 1)
    tint(d, (0.052, 0.050, 0.045), spread)
    tint(d, (0.024, 0.023, 0.021), line)
    tint(d, (0.030, 0.038, 0.020), moss)              # weed/moss in the joint, desaturated
    d["height"] = -0.0012 * line + 0.0016 * moss
    d["smooth"] = CONCRETE_SMOOTH - 0.08 * line
    d["ao"] = 1.0 - 0.55 * line - 0.20 * spread
    return finish(d, n)


def contact_wall_splash(n, seed=911):
    d = base(n)
    u, v = uv(n)
    rise = ss(0.46, 0.02, v + 0.10 * (fbm(n, 9, 3, seed, octaves=4) - 0.5) * 2.0)
    streak = 0.55 + 0.45 * fbm(n, 34, 4, seed + 1, octaves=3)
    spots = ss(0.74, 0.93, fbm(n, 40, 40, seed + 2, octaves=3)) * ss(0.30, 0.04, v)
    m = np.clip(rise * streak * 0.8 + spots * 0.7, 0, 1)
    d["alpha"] = m
    tint(d, (0.040, 0.038, 0.034))
    d["smooth"] = CONCRETE_SMOOTH - 0.07 * m
    d["ao"] = 1.0 - 0.30 * m
    return finish(d, n)


def contact_drip_run(n, seed=919):
    d = base(n)
    u, v = uv(n)
    m = np.zeros((n, n))
    r = rng(seed)
    for i in range(5):
        cx = 0.14 + r.random() * 0.72
        w = 0.010 + r.random() * 0.020
        wander = cx + 0.035 * (fbm(n, 1, 6, seed + i, octaves=4) - 0.5) * 2.0
        run = ss(w, w * 0.25, np.abs(u - wander)) * ss(0.0, 0.10, 0.97 - v)
        run *= ss(1.02, 0.30, v) * (0.6 + 0.4 * fbm(n, 3, 18, seed + 40 + i, octaves=3))
        m = np.maximum(m, run)
    pool, _ = _blob(n, 0.5, 0.10, 0.18, seed + 7, warp=0.5)
    m = np.clip(np.maximum(m, pool * 0.55), 0, 1)
    d["alpha"] = m * 0.9
    tint(d, (0.034, 0.032, 0.029))
    d["smooth"] = CONCRETE_SMOOTH + 0.10 * m
    d["ao"] = 1.0 - 0.35 * m
    return finish(d, n)


# -------------------------------------------------------------- tiling footway (gap #4, Layer 2)
# Seamless material texture, NOT a decal: no border fade, opaque. 4 x 4 panels of 1.20 m = 4.80 m.

def sidewalk_concrete(n, seed=1001, panels=4):
    d = base(n)
    u, v = uv(n)
    pu, pv = u * panels, v * panels
    iu, iv = np.floor(pu).astype(int) % panels, np.floor(pv).astype(int) % panels
    fu, fv = pu - np.floor(pu), pv - np.floor(pv)
    jd = np.minimum(np.minimum(fu, 1.0 - fu), np.minimum(fv, 1.0 - fv))
    joint = ss(0.016, 0.004, jd)                       # ~19 mm saw joint
    tone = rng(seed).random((panels, panels))[iv, iu]
    agg = fbm(n, 80, 80, seed + 1, octaves=4)
    fine = fbm(n, 220, 220, seed + 2, octaves=2)
    weather = fbm(n, 3, 3, seed + 3, octaves=5)
    hair = _crack_net(n, 7, seed + 4, 0.020) * ss(0.72, 0.95, fbm(n, 2, 2, seed + 5))
    chip = ss(0.80, 0.95, fbm(n, 60, 60, seed + 6, octaves=3)) * ss(0.05, 0.012, jd)

    grey = 0.168 * (0.84 + 0.32 * tone) * (0.88 + 0.24 * agg) * (0.94 + 0.12 * fine)
    grey *= 0.92 + 0.16 * weather
    d["albedo"] = np.repeat(grey[:, :, None], 3, axis=2) * np.array([1.00, 0.995, 0.975])
    tint(d, (0.052, 0.051, 0.049), joint)
    tint(d, (0.105, 0.103, 0.100), hair * 0.55)
    tint(d, (0.130, 0.127, 0.120), chip)
    d["alpha"] = np.ones((n, n))
    d["height"] = -0.008 * joint - 0.0012 * hair - 0.006 * chip + 0.0012 * (agg - 0.5)
    d["smooth"] = (CONCRETE_SMOOTH * (0.80 + 0.40 * tone) - 0.09 * joint - 0.06 * hair
                   - 0.02 * hair - 0.05 * weather)
    d["ao"] = 1.0 - 0.55 * blur(joint, 2) - 0.10 * hair - 0.25 * chip
    d["smooth"] = np.clip(d["smooth"], 0.0, 1.0)
    d["ao"] = np.clip(d["ao"], 0.0, 1.0)
    return d                                           # NO finish(): tiling, so no border fade


ATLASES2 = [
    dict(name="T_RW_Markings", size=2048, cell_m=4.0,
         cells=[("crosswalk_block", mark_crosswalk_block), ("arrow_straight", mark_arrow_straight),
                ("arrow_turn_right", mark_arrow_turn), ("stop_bar", mark_stop_bar)]),
    dict(name="T_RW_Puddle", size=2048, cell_m=4.0,
         cells=[("sheet_puddle", puddle_sheet), ("gutter_puddle", puddle_gutter),
                ("drying_ring", puddle_drying_ring), ("damp_patch", puddle_damp_patch)]),
    dict(name="T_RW_Contact", size=1024, cell_m=2.0,
         cells=[("prop_base_ring", contact_base_ring),
                ("footway_kerb_line", contact_footway_kerb_line),
                ("wall_base_splash", contact_wall_splash), ("drip_run", contact_drip_run)]),
]

TILING = [
    dict(name="T_SW_Concrete_Panel", size=2048, tile_m=4.80, fn=sidewalk_concrete),
]

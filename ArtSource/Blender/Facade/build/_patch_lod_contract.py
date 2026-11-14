"""Record the confirmed reveal depth, the near/far division of labour, and the LOD-distance maths the
main session asked for. Writes files only - runs no Blender."""
import io

# ---------------------------------------------------------------- build_facade.py
p = r"E:\VeyraCity\ArtSource\Blender\Facade\build\build_facade.py"
s = io.open(p, encoding="utf-8").read()
s = s.replace('''REVEAL_DEPTH = 0.26''',
'''REVEAL_DEPTH = 0.26

#: The 3 m ribbon module may later go shallower (0.18-0.20 m), because ribbon glazing really is shallower
#: than a punched masonry opening. Optional polish, not a blocker: set this and re-run that one model.
REVEAL_DEPTH_BAND = REVEAL_DEPTH''')
s = s.replace('''    hh = _reveal(bm, HALF, 1.45, lod, mullions=3)''',
              '''    hh = _reveal(bm, HALF, 1.45, lod, depth=REVEAL_DEPTH_BAND, mullions=3)''')
s = s.replace('''    return bm, (HALF + 0.10, REVEAL_DEPTH + 0.05, hh), "window"''',
              '''    return bm, (HALF + 0.10, REVEAL_DEPTH_BAND + 0.05, hh), "window"''')
io.open(p, "w", encoding="utf-8").write(s)

# ---------------------------------------------------------------- make_manifest.py
q = r"E:\VeyraCity\ArtSource\Blender\Facade\build\make_manifest.py"
t = io.open(q, encoding="utf-8").read()
t = t.replace('''w("It is the midpoint of the 0.24-0.30 m range put to the main session, which owns the night shading and")
w("has the final say; it had not confirmed a figure when this build ran.")''',
'''w("**Confirmed by the main session**, which owns the night shading. Its reasoning, recorded here so the")
w("number does not read as arbitrary: punched masonry openings sit 0.15-0.25 m back and curtain-wall")
w("glass behind its mullion line more like 0.05-0.10 m, but this reveal has to read at 50-150 m from a")
w("moving car rather than at arm's length, and a physically-perfect 0.12 m would vanish at exactly the")
w("distance where it matters. 0.26 m is deliberately at the generous end.")
w("")
w("`REVEAL_DEPTH_BAND` is a second constant, currently equal to `REVEAL_DEPTH`. The 3 m ribbon module")
w("may later go to 0.18-0.20 m, since ribbon glazing really is shallower; that is optional polish, not a")
w("blocker, and it is one line plus one model re-run.")
w("")
w("### Division of labour with the facade shader - do not \\"fix\\" this")
w("")
w("**This geometry owns NEAR depth; `VC_FacadeSurface.hlsl` owns FAR depth.** The shader already fades")
w("its interior raycast out between 40 m and 240 m, and its new per-window reveal shading is weighted")
w("the opposite way: near zero up close, where these modules cast a real shadow, ramping up as they LOD")
w("out. Full-strength fake reveal up close would stack two head shadows on one window and look worse")
w("than either alone.")
w("")
w("So this geometry is built **as if the shader does nothing** - confirmed by the session writing the")
w("shader. No sill is softened, no head shadow is pre-compensated, nothing here assumes something else")
w("will darken it. A future reader should not \\"correct\\" that.")''')

t = t.replace('''w("## Self-check")''',
'''w("## LOD crossover distances (what the shader fade has to match)")
w("")
w("This kit authors **no LOD distances**. The FBX carries only the `<name>_LOD<n>` naming; Unity's")
w("importer builds the LODGroup and owns the thresholds, so the crossover is set on the Unity side, not")
w("here. It is therefore **screen-size driven, not metres** - which is why the table below converts.")
w("")
w("Unity's screen-relative transition height for a vertical FOV is")
w("`h = size / (2 * d * tan(fov / 2))`, where `size` is the LODGroup size (the largest axis of the")
w("renderer bounds), so `d = size / (2 * h * tan(fov / 2))`. Veyra's scene camera is **60 deg vertical**")
w("(`VeyraCity.unity`); `ChaseCamera` moves between about 55 and 70 with speed, which shifts these")
w("distances by roughly -10 % / +21 %.")
w("")
w("Distance in metres at which a module reaches a given threshold, at 1440p and FOV 60:")
w("")
w("| Module (LODGroup size) | h = 0.5 | 0.25 | 0.10 | 0.05 | 0.035 | 0.02 | 0.01 |")
w("|---|---|---|---|---|---|---|---|")
import math as _math
_tan = _math.tan(_math.radians(60.0) / 2.0)
_seen = set()
for _r in R:
    if _r["part"] != 0:
        continue
    _size = max(_r["size_m"])
    _key = round(_size, 2)
    if _key in _seen:
        continue
    _seen.add(_key)
    _cells = " | ".join("%.1f" % (_size / (2.0 * _h * _tan)) for _h in
                        (0.5, 0.25, 0.10, 0.05, 0.035, 0.02, 0.01))
    w("| `%s` (%.2f m) | %s |" % (_r["name"], _size, _cells))
w("")
w("**What this means for the fade.** With a conventional 0.5 LOD0 threshold, a 1.6 m reveal module")
w("leaves LOD0 within about 3 m - far closer than the 40 m where the shader fade starts, which would")
w("leave a band with neither real depth nor fake depth. To hold these modules at LOD0 out to 40 m the")
w("LOD0 -> LOD1 threshold has to be about **0.035 or lower** for the 1.6 m modules (about 0.07 for the")
w("3.3 m storey bay, about 0.13 for the 6 m bands and crowns).")
w("")
w("**But LOD1 is not where the reveal dies.** On these modules LOD1 drops sills, mullions and transom")
w("bars and keeps the head, jambs, sill and recessed glass - the recess survives. Modules under 2 m")
w("have no LOD2 at all (CLAUDE.md). So the distance at which real depth actually disappears is the")
w("**cull** distance, not the first LOD step, and that is the number a shader ramp should key to.")
w("")
w("## Self-check")''')
io.open(q, "w", encoding="utf-8").write(t)
print("patched: REVEAL_DEPTH_BAND, confirmed-depth reasoning, near/far split, LOD distance table")

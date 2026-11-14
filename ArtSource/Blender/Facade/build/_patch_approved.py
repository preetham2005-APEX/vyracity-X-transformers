"""Apply the infra session's three approval conditions. Writes files only - runs no Blender.

1. reveal depth becomes ONE named constant at the 0.26 m midpoint
2. MANIFEST carries a one-line derivation of the 6.0 m grid
3. MANIFEST flags wall/window/band anchors as a deliberate, reasoned CLAUDE.md deviation
"""
import io, sys

# ---------------------------------------------------------------- build_facade.py
p = r"E:\VeyraCity\ArtSource\Blender\Facade\build\build_facade.py"
s = io.open(p, encoding="utf-8").read()

s = s.replace('''EXPORT = os.path.join(ROOT, "export")
BAY, HALF = 6.0, 3.0''',
'''EXPORT = os.path.join(ROOT, "export")
BAY, HALF = 6.0, 3.0

#: How far the glazing sits behind the wall face on every window reveal module. ONE constant, used by all
#: three reveals, so the main session's shading work can set it: change this line and re-run
#: `build_facade.py -- window_reveal_single window_reveal_band window_reveal_storey`. 0.26 m is the
#: midpoint of the 0.24-0.30 m range offered; the Unity session has it to confirm.
REVEAL_DEPTH = 0.26''')

s = s.replace('''def _reveal(bm, w, hh, lod, depth=0.26, mullions=0, transom=0.0):''',
              '''def _reveal(bm, w, hh, lod, depth=REVEAL_DEPTH, mullions=0, transom=0.0):''')
s = s.replace('''    hh = _reveal(bm, 1.50, 1.60, lod, depth=0.26, mullions=1)
    return bm, (1.60, 0.33, hh), "window"''',
              '''    hh = _reveal(bm, 1.50, 1.60, lod, mullions=1)
    return bm, (1.60, REVEAL_DEPTH + 0.07, hh), "window"''')
s = s.replace('''    hh = _reveal(bm, HALF, 1.45, lod, depth=0.24, mullions=3)
    return bm, (HALF + 0.10, 0.31, hh), "window"''',
              '''    hh = _reveal(bm, HALF, 1.45, lod, mullions=3)
    return bm, (HALF + 0.10, REVEAL_DEPTH + 0.05, hh), "window"''')
s = s.replace('''    hh = _reveal(bm, 1.50, 3.30, lod, depth=0.30, mullions=1, transom=2.35)
    return bm, (1.60, 0.37, hh), "window"''',
              '''    hh = _reveal(bm, 1.50, 3.30, lod, mullions=1, transom=2.35)
    return bm, (1.60, REVEAL_DEPTH + 0.07, hh), "window"''')
s = s.replace('''    """1.5 x 1.6 m punched window with a 0.26 m reveal - the repeating unit for a punched facade."""''',
              '''    """1.5 x 1.6 m punched window, reveal = REVEAL_DEPTH - the repeating unit for a punched facade."""''')
s = s.replace('''    """3.0 m ribbon window with four lights - the half-bay horizontal module."""''',
              '''    """3.0 m ribbon window, four lights, reveal = REVEAL_DEPTH - the half-bay horizontal module."""''')
s = s.replace('''    """1.5 x 3.3 m storey-height bay with a transom - for curtain-wall and midrise frontages."""''',
              '''    """1.5 x 3.3 m storey-height bay with a transom, reveal = REVEAL_DEPTH - curtain-wall frontages."""''')
io.open(p, "w", encoding="utf-8").write(s)

# ---------------------------------------------------------------- make_manifest.py
q = r"E:\VeyraCity\ArtSource\Blender\Facade\build\make_manifest.py"
t = io.open(q, encoding="utf-8").read()

# read the constant out of the builder rather than restating it
t = t.replace('''TEX = os.path.join(ROOT, "textures")''',
'''TEX = os.path.join(ROOT, "textures")
_src = open(os.path.join(B, "build_facade.py"), encoding="utf-8").read()
REVEAL_DEPTH = float(_src.split("REVEAL_DEPTH = ")[1].split("\\n")[0])''')

t = t.replace('''w("## Module grid")
w("")
w("Taken from the building kit, not invented: the ground-floor rhythm there is `int(w / 6.0)` colonnade")
w("piers on the tower podiums (`build_towers.podium`) and 6.0 m shopfront bays on the market-town")
w("terraces (`build_coast_town.shopfront`). So this kit is built on a **6.0 m bay with a 3.0 m")
w("half-bay**. The upper-facade pier pitches in the building kit (4.0 / 4.5 / 5.0 m) are deliberately")
w("ignored: the ground-floor kit sits below the first spandrel band, and the window modules are placed")
w("per opening rather than tiled.")''',
'''w("## Module grid: 6.0 m bay / 3.0 m half-bay")
w("")
w("**Derivation, in one line:** 6.0 m is the ground-floor rhythm already in the building kit - tower")
w("podium colonnade piers are spaced `int(w / 6.0)` (`build_towers.podium`) and market-town shopfront")
w("bays are 6.0 m (`build_coast_town.shopfront`) - so the facade kit inherits it rather than inventing")
w("a round number.")
w("")
w("The upper-facade pier pitches in the building kit (4.0 / 4.5 / 5.0 m) are deliberately ignored: the")
w("ground-floor kit sits below the first spandrel band, and the window modules are placed per opening")
w("rather than tiled, so neither needs to align to the structural grid above.")''')

t = t.replace('''w("## Pivot convention")
w("")''',
'''w("## Pivot convention")
w("")
w("> **Deliberate deviation from CLAUDE.md.** CLAUDE.md says \\"buildings and props at base centre\\".")
w("> Three of the four anchor kinds below (`wall`, `window`, `band`) instead pivot at the **wall face**,")
w("> and `window` and `band` put z = 0 at the sill line or band soffit rather than at the ground.")
w("> **Why:** these pieces mount onto a facade, and most of them repeat UP it. A base-centre pivot would")
w("> force the placer to compute the mounting height for every instance and to know each piece's depth")
w("> to sit it against the wall. Anchoring at the wall face at the piece's own datum means the placer")
w("> needs one point - a position on the building's front face - and the piece carries the rest.")
w("> Free-standing props (`ground`) keep the CLAUDE.md base-centre convention unchanged. The anchor kind")
w("> is listed per prop in the model tables below, and is also in `report_facade.json` as `anchor`.")
w("")''')

t = t.replace('''w("Outward is Blender -Y, so after the `VC_Unity_Prop` export every piece faces **Unity +Z** - the same")
w("way the kit buildings' fronts face, so a piece dropped on a building front needs no extra rotation.")''',
'''w("Outward is Blender -Y, so after the `VC_Unity_Prop` export every piece faces **Unity +Z** - the same")
w("way the kit buildings' fronts face, so a piece dropped on a building front needs no extra rotation.")
w("")
w("## Reveal depth")
w("")
w("The glazing on all three window modules sits **%.2f m** behind the wall face. That is a single named"
  % REVEAL_DEPTH)
w("constant, `REVEAL_DEPTH` in `build/build_facade.py`, used by every reveal - one line and one re-run")
w("away from being something else:")
w("")
w("```")
w('"D:\\\\New folder\\\\blender.exe" --factory-startup -b -noaudio -P ArtSource/Blender/Facade/build/build_facade.py \\\\')
w("    -- window_reveal_single window_reveal_band window_reveal_storey")
w("```")
w("")
w("%.2f m is the midpoint of the 0.24-0.30 m range put to the main session, which owns the night")
w("shading and has the final say; it had not confirmed a figure when this build ran.")''' % REVEAL_DEPTH
  if False else '''w("Outward is Blender -Y, so after the `VC_Unity_Prop` export every piece faces **Unity +Z** - the same")
w("way the kit buildings' fronts face, so a piece dropped on a building front needs no extra rotation.")
w("")
w("## Reveal depth")
w("")
w("The glazing on all three window modules sits **%.2f m** behind the wall face. That is a single named"
  % REVEAL_DEPTH)
w("constant, `REVEAL_DEPTH` in `build/build_facade.py`, used by every reveal - one line and one re-run")
w("away from being something else:")
w("")
w("```")
w('"D:\\\\New folder\\\\blender.exe" --factory-startup -b -noaudio -P ArtSource/Blender/Facade/build/build_facade.py \\\\')
w("    -- window_reveal_single window_reveal_band window_reveal_storey")
w("```")
w("")
w("It is the midpoint of the 0.24-0.30 m range put to the main session, which owns the night shading and")
w("has the final say; it had not confirmed a figure when this build ran.")''')
io.open(q, "w", encoding="utf-8").write(t)
print("patched: REVEAL_DEPTH constant, grid derivation, anchor deviation callout")

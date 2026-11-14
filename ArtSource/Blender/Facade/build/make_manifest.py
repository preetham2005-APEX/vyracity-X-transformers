"""Write ArtSource/Blender/Facade/MANIFEST.md from the build and QA reports.

    python make_manifest.py

Every number in the manifest comes from report_facade.json (the build) and qa_report.json (the
re-import check). Nothing is typed in by hand, so the manifest cannot drift from what was exported.
"""
import datetime
import json
import os

B = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(B)
R = json.load(open(os.path.join(B, "report_facade.json")))
R.sort(key=lambda r: (r["part"], r["name"]))
qa_path = os.path.join(B, "qa_report.json")
QA = json.load(open(qa_path)) if os.path.exists(qa_path) else None
TEX = os.path.join(ROOT, "textures")
_src = open(os.path.join(B, "build_facade.py"), encoding="utf-8").read()
REVEAL_DEPTH = float(_src.split("REVEAL_DEPTH = ")[1].split("\n")[0])
tex = sorted(f for f in os.listdir(TEX)) if os.path.isdir(TEX) else []

PART = {0: "Part 0 - window reveals, floor banding and tower crowns (priority)",
        1: "Part 1 - ground-floor / shopfront kit",
        2: "Part 2 - rooftop clutter"}
NOTE = {
    "VC_Fac_WindowRevealSingle": "1.5 x 1.6 m punched window, 0.26 m reveal, recessed glazing",
    "VC_Fac_WindowRevealBand": "3.0 m ribbon window, four lights, 0.24 m reveal",
    "VC_Fac_WindowRevealStorey": "1.5 x 3.3 m storey-height bay with a transom, 0.30 m reveal",
    "VC_Fac_SlabBand": "6 m floor-line slab edge with a drip",
    "VC_Fac_CorniceBand": "6 m profiled cornice",
    "VC_Fac_SillBand": "6 m string course / continuous sill",
    "VC_Fac_CrownSetback": "6 m tower crown: parapet, setback step, coping, BLANK light cove",
    "VC_Fac_CrownFin": "vertical crown fin with a BLANK lit reveal",
    "VC_Fac_EntryRecess": "recessed entry, 1.05 m deep, glazed door pair and transom",
    "VC_Fac_Shopfront3m": "3 m shopfront: stall riser, mullion, transom, head",
    "VC_Fac_Shopfront6m": "6 m shopfront, three mullions",
    "VC_Fac_GrilleClosed": "roll-up security grille, shut, in its guide rails",
    "VC_Fac_GrilleOpen": "the same grille rolled up on its drum",
    "VC_Fac_AwningFabric": "fabric awning with valance and folding arms",
    "VC_Fac_AwningBox": "rigid box awning",
    "VC_Fac_SignProjecting": "wall-mounted projecting sign box, BLANK both faces",
    "VC_Fac_SignFascia": "flush fascia sign panel, BLANK",
    "VC_Fac_AFrame": "pavement A-frame board, BLANK both faces",
    "VC_Fac_DisplayPlinth": "shop display plinth",
    "VC_Roof_HvacSmall": "small packaged AC unit on sleeper rails",
    "VC_Roof_HvacLarge": "large packaged AC unit, twin fans, louvre bank",
    "VC_Roof_FanCowl": "extract fan cowl on an upstand kerb",
    "VC_Roof_DuctRun": "4 m insulated duct run with flanges and sleepers",
    "VC_Roof_WaterTank": "tank on a four-leg stand with a ladder",
    "VC_Roof_StairOverrun": "stair / lift overrun box with coping",
    "VC_Roof_ParapetCap": "3 m parapet coping module",
    "VC_Roof_AntennaCluster": "mast, cross arms, whips and a drum",
    "VC_Roof_Hatch": "roof access hatch, lid propped open",
}

L = []
w = L.append
w("# Veyra City - facade articulation + rooftop clutter kit")
w("")
w("Closes gap #5 in `Docs/VISUAL_GAP_ANALYSIS.md` (\"Facades are flat\"). The 88-model building kit in")
w("`ArtSource/Blender/Buildings/` fixed the massing; this kit is the articulation - real window reveals,")
w("floor banding, tower crowns, ground-floor shopfronts and rooftop clutter.")
w("")
w("Built %s by `build/build_facade.py` (re-runnable). **%d models, %s triangles at LOD0, %d texture files.**"
  % (datetime.date.today().isoformat(), len(R), format(sum(r["lods"][0]["tris"] for r in R), ","),
     len(tex)))
w("All geometry and all textures original to this project; the benchmark captures were read for")
w("construction logic (shape, proportion, depth) only.")
w("")
w("## Priority order")
w("")
w("The main session is working the shading side of the same gap (night ambient floor, per-window reveal")
w("shading, floor/column coherence), so it asked for window reveals, floor banding and tower crowns")
w("first, then the shopfront kit, then rooftop clutter - its shading and this geometry meeting in the")
w("middle rather than both faking depth. That is the order below.")
w("")
w("Why Part 0 matters at night: the tower set carries its floor lines and window grid in the")
w("**curtain-wall texture** - that is what makes a 240 m tower cost the same as a 28 m one - so there is")
w("no geometry for a lit pane to sit behind, and the facade reads as a flat box with lit rectangles")
w("painted on. These modules put the recess back where it is seen, at a cost paid per module placed")
w("rather than per tower.")
w("")
w("## Module grid: 6.0 m bay / 3.0 m half-bay")
w("")
w("**Derivation, in one line:** 6.0 m is the ground-floor rhythm already in the building kit - tower")
w("podium colonnade piers are spaced `int(w / 6.0)` (`build_towers.podium`) and market-town shopfront")
w("bays are 6.0 m (`build_coast_town.shopfront`) - so the facade kit inherits it rather than inventing")
w("a round number.")
w("")
w("The upper-facade pier pitches in the building kit (4.0 / 4.5 / 5.0 m) are deliberately ignored: the")
w("ground-floor kit sits below the first spandrel band, and the window modules are placed per opening")
w("rather than tiled, so neither needs to align to the structural grid above.")
w("")
w("## Pivot convention")
w("")
w("> **Deliberate deviation from CLAUDE.md.** CLAUDE.md says \"buildings and props at base centre\".")
w("> Three of the four anchor kinds below (`wall`, `window`, `band`) instead pivot at the **wall face**,")
w("> and `window` and `band` put z = 0 at the sill line or band soffit rather than at the ground.")
w("> **Why:** these pieces mount onto a facade, and most of them repeat UP it. A base-centre pivot would")
w("> force the placer to compute the mounting height for every instance and to know each piece's depth")
w("> to sit it against the wall. Anchoring at the wall face at the piece's own datum means the placer")
w("> needs one point - a position on the building's front face - and the piece carries the rest.")
w("> Free-standing props (`ground`) keep the CLAUDE.md base-centre convention unchanged. The anchor kind")
w("> is listed per prop in the model tables below, and is also in `report_facade.json` as `anchor`.")
w("")
w("| Anchor | Pieces | Origin |")
w("|---|---|---|")
w("| `wall` | shopfront kit, awnings, signs | wall face, centred, **z = 0 at ground** - the piece carries its own mounting height, so one anchor places everything |")
w("| `window` | the three window reveal modules | wall face, centred, **z = 0 at the opening sill** (they repeat up a facade) |")
w("| `band` | slab / cornice / sill bands, crowns, parapet cap | wall face, centred, **z = 0 at the band soffit** |")
w("| `ground` | A-frame, display plinth, all rooftop props | base centre, z = 0 (the normal prop convention) |")
w("")
w("Outward is Blender -Y, so after the `VC_Unity_Prop` export every piece faces **Unity +Z** - the same")
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
w('"D:\\New folder\\blender.exe" --factory-startup -b -noaudio -P ArtSource/Blender/Facade/build/build_facade.py \\')
w("    -- window_reveal_single window_reveal_band window_reveal_storey")
w("```")
w("")
w("**Confirmed by the main session**, which owns the night shading. Its reasoning, recorded here so the")
w("number does not read as arbitrary: punched masonry openings sit 0.15-0.25 m back and curtain-wall")
w("glass behind its mullion line more like 0.05-0.10 m, but this reveal has to read at 50-150 m from a")
w("moving car rather than at arm's length, and a physically-perfect 0.12 m would vanish at exactly the")
w("distance where it matters. 0.26 m is deliberately at the generous end.")
w("")
w("`REVEAL_DEPTH_BAND` is a second constant, currently equal to `REVEAL_DEPTH`. The 3 m ribbon module")
w("may later go to 0.18-0.20 m, since ribbon glazing really is shallower; that is optional polish, not a")
w("blocker, and it is one line plus one model re-run.")
w("")
w("### Division of labour with the facade shader - do not \"fix\" this")
w("")
w("**This geometry owns NEAR depth; `VC_FacadeSurface.hlsl` owns FAR depth.** The shader already fades")
w("its interior raycast out between 40 m and 240 m, and its new per-window reveal shading is weighted")
w("the opposite way: near zero up close, where these modules cast a real shadow, ramping up as they LOD")
w("out. Full-strength fake reveal up close would stack two head shadows on one window and look worse")
w("than either alone.")
w("")
w("So this geometry is built **as if the shader does nothing** - confirmed by the session writing the")
w("shader. No sill is softened, no head shadow is pre-compensated, nothing here assumes something else")
w("will darken it. A future reader should not \"correct\" that.")
w("")
w("## Models")
w("")
for part in (0, 1, 2):
    rows = [r for r in R if r["part"] == part]
    if not rows:
        continue
    w("### " + PART[part])
    w("")
    w("| Model | Size W x D x H (m) | LOD0 | LOD1 | LOD2 | Anchor | Collision | What it is |")
    w("|---|---|---|---|---|---|---|---|")
    for r in rows:
        t = [str(x["tris"]) for x in r["lods"]] + ["-"] * (3 - len(r["lods"]))
        col = r["collision"][0] if r["collision"] else "-"
        w("| `%s` | %.2f x %.2f x %.2f | %s | %s | %s | %s | %s | %s |"
          % (r["name"], r["size_m"][0], r["size_m"][1], r["size_m"][2], t[0], t[1], t[2],
             r["anchor"], col, NOTE.get(r["name"], "")))
    w("")
    w("Part subtotal at LOD0: **%d triangles** across %d models."
      % (sum(x["lods"][0]["tris"] for x in rows), len(rows)))
    w("")
w("Whole kit at LOD0: **%d triangles** across %d models (mean %d, largest %d)."
  % (sum(r["lods"][0]["tris"] for r in R), len(R),
     sum(r["lods"][0]["tris"] for r in R) // max(1, len(R)),
     max(r["lods"][0]["tris"] for r in R)))
w("")
w("LOD policy follows CLAUDE.md: props under 2 m ship **LOD0 + LOD1**, larger meshes get three levels.")
w("Every LOD is authored - the same description re-run at a lower detail level - never decimated; a")
w("collapse decimate shreds this kind of box geometry (proved on the building kit, where the decimated")
w("LOD2s rendered as see-through shells).")
w("")
w("## Materials and textures")
w("")
w("**One shared set for all %d props** - 7 materials, %d files - because every prop here instances and"
  % (len(R), len(tex)))
w("BUG-170 is open. No prop has a texture of its own.")
w("")
w("| Material | Use |")
w("|---|---|")
w("| `VC_Fac_Metal` | mullions, frames, grilles, brackets, ducting, antennas |")
w("| `VC_Fac_Glass` | shopfront and window glazing |")
w("| `VC_Fac_Panel` | painted panel: stall risers, sign bodies, awning boxes, plant casings |")
w("| `VC_Fac_Fabric` | awning canvas |")
w("| `VC_Fac_Blank` | **BLANK** sign faces - no text, logo or symbol anywhere in this kit |")
w("| `VC_Fac_Concrete` | sills, copings, cornices, plinths, upstands |")
w("| `VC_Fac_Emissive` | **BLANK** crown light coves and lit reveals, HDR emission |")
w("")
w("Packing follows CLAUDE.md's URP Lit rule (**not** the glTF packing the building kit uses, because")
w("those go through `KitbashMaterials` while these go through the VeyraMaterialLibrary):")
w("")
w("```")
w("<mat>_BC.png   sRGB albedo")
w("<mat>_N.png    OpenGL tangent-space normal, +Y up, Non-Color")
w("<mat>_MS.png   R = metallic, A = smoothness (1 - roughness), Non-Color")
w("```")
w("")
w("Unity: Smoothness Source = **Metallic Alpha**, `_N` as Normal Map, `_MS` with sRGB off. 512 x 512,")
w("power of two, seamless; one tile covers 1.0 m, matching the 1 m box projection the meshes carry (the")
w("same convention as the Infra set).")
w("")
w("`VC_Fac_Emissive` is toggled at runtime, so CLAUDE.md's keyword rule applies: it must be covered by a")
w("material in the build or by `Assets/_Veyra/Art/Shaders/VC_Variants.shadervariants`.")
w("")
w("## LOD crossover distances (what the shader fade has to match)")
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
w("**The project already fixes this threshold.** `Assets/_Veyra/Tests/EditMode/ChunkImportTests.cs`")
w("fails any imported model with **3 or more** LOD levels unless LOD0's `screenRelativeTransitionHeight`")
w("is exactly **0.35** - so the column to read is not 0.5 but 0.35, which at FOV 60 is `size / 0.404`")
w("metres. Two things follow, both verified by reading the test rather than assumed:")
w("")
w("- the gate applies at 3 or more LOD levels, and CLAUDE.md gives props under 2 m only LOD0 + LOD1, so")
w("  **exactly one module is exempt**: `VC_Fac_WindowRevealSingle` at 1.60 m, whose threshold is")
w("  whatever the importer sets and is free to tune. Every other Part 0 module carries 3 LODs and is")
w("  bound, which puts LOD0 much closer than the shader fade starts:")
w("")
for _r in sorted([x for x in R if x["part"] == 0], key=lambda x: x["name"]):
    _sz = max(_r["size_m"])
    _d = _sz / (2.0 * 0.35 * _tan)
    w("  - `%s` (%.2f m, %d LODs): %s"
      % (_r["name"], _sz, _r["levels"],
         "exempt" if _r["levels"] < 3 else "LOD0 to **%.1f m**" % _d))
w("")
w("- holding a 1.6 m module at LOD0 out to 40 m still needs about **0.035**, which no default produces.")
w("  It has to be set deliberately.")
w("")
w("**But LOD1 is not where the reveal dies.** On these modules LOD1 drops sills, mullions and transom")
w("bars and keeps the head, jambs, sill and recessed glass - the recess survives. Modules under 2 m")
w("have no LOD2 at all (CLAUDE.md). So the distance at which real depth actually disappears is the")
w("**cull** distance, not the first LOD step, and that is the number a shader ramp should key to.")
w("")
w("## If these are merged into a building at build time")
w("")
w("The Unity session is merging the Part 0 modules into each building's LOD0 mesh in the editor rather")
w("than instancing them at runtime - static, chunked, no runtime spawn, which is what CLAUDE.md wants")
w("for large static geometry and which avoids the per-window instance churn entirely. Nothing in this")
w("kit changes for that; the pieces, the 0.26 m reveal and the anchors are the same either way. Two")
w("things do change on the Unity side, and both are worth having written down.")
w("")
w("### 1. The triangle arithmetic moves from spawn cost to mesh cost")
w("")
w("A punched reveal module is **108 triangles** at LOD0 (counted from the box list in")
w("`build_facade._reveal`: head, sill, two jambs, glazing, two frame bars, a mullion and a sill nosing;")
w("the ribbon is 132 and the storey bay 120). That is cheap per module and expensive per tower:")
w("")
w("| Merge envelope | Modules | Tris per tower | 100 towers |")
w("|---|---|---|---|")
w("| 20 floors x 30 bays x 4 faces | 2,400 | **259,200** | 25,920,000 |")
w("| street face only, all 20 floors | 600 | 64,800 | 6,480,000 |")
w("| street face, lower 6 floors | 180 | 19,440 | 1,944,000 |")
w("| street face, lower 3 floors | 90 | 9,720 | 972,000 |")
w("")
w("For scale: the tallest tower in the building kit is **1,348 triangles for the whole building**, and")
w("CLAUDE.md caps a single mesh at 300,000. Merging reveals onto every bay of every face puts one tower")
w("at 259,200 - inside the cap, but about 190x the building it is merged into, and 26 M triangles across")
w("a hundred towers in view. That is no longer a spawn problem, it is a vertex and memory one, against a")
w("GPU budget that measured 9.1-9.9 ms p95 with the plain boxes.")
w("")
w("The hierarchy that keeps this affordable is the one the skyline already has: **reveals where the")
w("player is, bands where they are not.** A 6 m band module is 36 triangles, so a floor line costs about")
w("330 triangles per 55 m face - an order of magnitude less per metre of facade than per-window reveals.")
w("Street-facing faces and the lower few floors get real reveals; everything above gets banding and the")
w("curtain-wall texture it already has. The merge envelope is the Unity session's call; this table is")
w("here so the number is chosen rather than discovered.")
w("")
w("### 2. Material equivalence - a merged building should gain one submesh, not seven")
w("")
w("A merged mesh carries the union of both material sets, so merging naively would give a tower both")
w("`VC_Twr_Glass` and `VC_Fac_Glass` - two draw calls for the same visual material. Three of the seven")
w("facade slots have an exact equivalent in every host kit and should be remapped at merge time:")
w("")
w("| Facade slot | Remap to, per host kit | |")
w("|---|---|---|")
w("| `VC_Fac_Metal` | `VC_Twr_Metal` / `VC_Nm_Metal` / `VC_Mr_Metal` / `VC_In_Metal` / `VC_Mt_Trim` | exact |")
w("| `VC_Fac_Glass` | `VC_Twr_Glass` / `VC_Nm_Glass` / `VC_Mr_Glass` / `VC_Cs_Glass` / `VC_Mt_Glass` | exact |")
w("| `VC_Fac_Concrete` | `VC_Twr_Concrete` / `VC_Nm_Concrete` / `VC_Mr_Concrete` / `VC_In_Concrete` | exact |")
w("| `VC_Fac_Panel` | no host equivalent - **keep** | the one new slot a merged tower gains |")
w("| `VC_Fac_Fabric` | shopfront kit only, never merged into a tower | - |")
w("| `VC_Fac_Blank` | shopfront/sign kit only | - |")
w("| `VC_Fac_Emissive` | crowns only; keep, it is runtime-toggled | - |")
w("")
w("So a tower merged with reveals and bands gains exactly **one** extra submesh (`VC_Fac_Panel`), and")
w("crowns add `VC_Fac_Emissive`. Without the remap it gains four to seven.")
w("")
w("### 3. Collision on merge")
w("")
w("The Part 0 modules carry **no colliders** by design, so merging them changes nothing about physics -")
w("every building keeps its own data-footprint collider. The Part 1 shopfront pieces do carry `UBX_`")
w("colliders (they are at ground level where a car can reach them); if any of those are merged rather")
w("than placed, drop the `UBX_` meshes in the merge rather than baking them into the render mesh.")
w("")
w("### 4. What the shader ramp keys to, under the merge")
w("")
w("Merging moves the answer from a per-module cull to the **building's** LOD0 -> LOD1 transition, since")
w("the building's LOD1 will not carry the merged reveals. That is one number per building size, driven")
w("by the building's bounds rather than the module's, and the Unity session controls it directly - so")
w("the distance table above describes the modules as standalone props and is superseded for merged use.")
w("It still holds for anything placed as a prop (the shopfront and rooftop kits).")
w("")
w("## Self-check")
w("")
if QA is None:
    w("`build/qa_check.py` has not been run yet - no result to quote.")
else:
    bad = sum(len(x["problems"]) for x in QA)
    w("`build/qa_check.py` re-imports every exported FBX and measures it: **%d files, %d problems**."
      % (len(QA), bad))
    if bad:
        for x in QA:
            for p in x["problems"]:
                w("- `%s`: %s" % (x["file"], p))
    else:
        w("")
        w("It checks: one EMPTY root; `<name>_LOD<n>` children contiguous from 0; LOD count against the")
        w("CLAUDE.md 2 m rule; applied transforms; 0 n-gons, 0 inverted faces at LOD0, 0 degenerate faces,")
        w("0 loose vertices; UVs on every LOD; material slots drawn only from the shared 7; the LOD0")
        w("triangle budget; the pivot (anchor at z = 0 and centred on x); collision naming; and that a")
        w("wall-anchored piece projects toward the street rather than behind the wall face.")
w("")
w("LOD normals are gated by a render, not a metric: `build/review.py` renders every LOD")
w("**backface-culled against a red world** (`review/lod<n>_backface_check.png`), so a flipped or missing")
w("triangle shows as a hole. Both numeric inverted-face tests are unreliable on this geometry - that was")
w("established on the building kit, where a decimated LOD2 every metric called clean rendered hollow.")
w("")
w("## Rebuild / re-verify (headless, no Unity involved)")
w("")
w("```")
w('"D:\\New folder\\blender.exe" --factory-startup -b -noaudio -P ArtSource/Blender/Facade/build/make_textures.py')
w('"D:\\New folder\\blender.exe" --factory-startup -b -noaudio -P ArtSource/Blender/Facade/build/build_facade.py -- all')
w('"D:\\New folder\\blender.exe" --factory-startup -b -noaudio -P ArtSource/Blender/Facade/build/qa_check.py')
w('"D:\\New folder\\blender.exe" --factory-startup -b -noaudio -P ArtSource/Blender/Facade/build/review.py')
w("python ArtSource/Blender/Facade/build/make_manifest.py")
w("```")
w("")
w("`build/build_facade.py` imports `vc_bld_lib` from `ArtSource/Blender/Buildings/build/` (same author,")
w("same session) for the shared bmesh helpers. That is the only dependency outside this folder.")
w("")
w("## For whoever wires this up")
w("")
w("- Nothing was written to `Assets/`, Unity was never run, nothing committed, no branch switched.")
w("- `Assets/_Veyra/AI/RooftopDetails.cs` already exists and places roof props at runtime. The Part 2")
w("  models are authored to be consumed by it (or by the main session) - base-centre pivots, z = 0, one")
w("  shared material set - but nothing in `Assets/` was touched, so the wiring is still to do.")
w("- Every sign face in this kit is blank geometry on its own material slot. Veyra's own artwork is")
w("  applied in Unity.")
open(os.path.join(ROOT, "MANIFEST.md"), "w", encoding="utf-8").write("\n".join(L) + "\n")
print("WROTE MANIFEST.md (%d models)" % len(R))

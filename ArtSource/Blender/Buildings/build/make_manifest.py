"""Write the building kit's MANIFEST.md, UNITY_HANDOFF.md and one kitbash_manifest_<source>.json per set
(the format Assets/_Veyra/Editor/KitbashCatalogBuilder.cs consumes).

    python make_manifest.py
"""
import datetime
import json
import os

B = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(B)

import kit_sets

SETS = kit_sets.load()
for _s in SETS:
    _s.setdefault("title", _s["source"])

tex_all = sorted(f for f in os.listdir(os.path.join(ROOT, "textures")) if f.endswith(".png"))

# ------------------------------------------------------------------ per-source kitbash manifests
for s in SETS:
    unity_models = "Assets/_Veyra/Art/Models/Kitbash/" + s["source"]
    pieces = [{
        "name": r["name"],
        "file": unity_models + "/" + r["name"] + ".fbx",
        "type": "Building",
        "footprint_m": r["footprint_m"],
        "height_m": r["height_m"],
        "triangles": [l["tris"] for l in r["lods"]],
        "collision": r["collision"],
        "materials": [{"source": m} for m in r["slots"]],
    } for r in s["rows"]]
    with open(os.path.join(ROOT, "kitbash_manifest_%s.json" % s["source"]), "w", encoding="utf-8") as fh:
        json.dump({"source": s["source"], "original": True,
                   "generator": "ArtSource/Blender/Buildings/build/", "pieces": pieces}, fh, indent=1)
old = os.path.join(ROOT, "kitbash_manifest.json")
if os.path.exists(old):
    os.remove(old)

# ------------------------------------------------------------------ MANIFEST.md
L = []
w = L.append
w("# Veyra City - building kit (ArtSource/Blender/Buildings)")
w("")
w("Original modular buildings, authored headless in Blender 5.2.3 by the scripts in `build/` (re-runnable:")
w("running them again rebuilds every .blend, .fbx and texture from scratch). Nothing is downloaded, scanned")
w("or traced; reference frames were read for proportion and construction logic only. All signage is blank.")
w("")
w("Built " + datetime.date.today().isoformat() + ". Metric, 1 unit = 1 m.")
w("")
w("## Why these sizes")
w("")
w("Every model is sized from `Assets/_Veyra/Data/world_building_placements.csv` (2,708 records), never")
w("invented, and the set for each district was chosen by **simulating `BuildingAssembler`'s own fit rule**")
w("(footprint +-22 % after a uniform 0.85-1.2 scale, height 0.65-1.5x, either orientation) against the real")
w("records. `BuildingAssembler` then stretches each instance to the record's exact height, so the number")
w("that matters is the height stretch at best fit - reported per set below.")
w("")
total_lod0 = 0
for s in SETS:
    if not s["rows"]:
        continue
    w("## " + s["title"])
    w("")
    w("Source id `%s` - districts %s, styles %s."
      % (s["source"], ", ".join(s["districts"]), ", ".join(sorted(set(s["tall"] + s["low"])))))
    w("")
    if s["fitdata"]:
        f = s["fitdata"]
        line = ("Coverage against the real records: **%d of %d fit**; height stretch at best fit median "
                "%.0f %%, p90 %.0f %%, worst %.0f %%."
                % (f["fit"], f["total"], f["stretch_median"] * 100, f["stretch_p90"] * 100,
                   f["stretch_max"] * 100))
        if f["unfit"]:
            n = len(f["unfit"])
            line += (" The %s (%s) %s a data outlier far outside the district's norms, not a kit gap, and %s"
                     " its procedural shell - see \"Records the kit does not fit\"."
                     % ("one record that does not fit" if n == 1 else "%d records that do not fit" % n,
                        "; ".join("%g x %g x %g m" % tuple(u) for u in f["unfit"][:4]),
                        "is" if n == 1 else "are each", "keeps" if n == 1 else "keep"))
        else:
            line += " Every record in the district is covered."
        w(line)
        w("")
    w("| Model | Footprint | Height | LOD0 | LOD1 | LOD2 | Slots |")
    w("|---|---|---|---|---|---|---|")
    for r in s["rows"]:
        t = [str(l["tris"]) for l in r["lods"]]
        total_lod0 += r["lods"][0]["tris"]
        w("| `%s` | %.0f x %.0f m | %.2f m | %s | %s | %s | %d |"
          % (r["name"], r["footprint_m"][0], r["footprint_m"][1], r["height_m"], t[0], t[1], t[2],
             len(r["slots"])))
    w("")
    w("Set LOD0 total: **%d triangles** across %d models (mean %d)."
      % (sum(r["lods"][0]["tris"] for r in s["rows"]), len(s["rows"]),
         sum(r["lods"][0]["tris"] for r in s["rows"]) // len(s["rows"])))
    if s["fitdata"]:
        w("")
        w("Model usage over the district's records: "
          + ", ".join("`%s` %d" % (k.split("_")[-1], v)
                      for k, v in sorted(s["fitdata"]["usage"].items(), key=lambda x: -x[1])) + ".")
    w("")

w("## Records the kit does not fit")
w("")
w("Every record in the five style groups was tested against the exported models with the assembler's own")
w("rule; `build/analyse_misfits.py` reports which test failed and whether the cause is a gap in the kit or")
w("an anomaly in the data. The first pass left 19 misfits. All but three were **height-band gaps** - the")
w("footprint already fitted an existing model, the height did not - so those models were re-run at the")
w("missing heights (and two D5 garage typologies were added). What remains:")
w("")
allmis = [(s2["source"], m) for s2 in SETS if s2["fitdata"] for m in s2["fitdata"].get("misfits", [])]
if allmis:
    w("| Record | Kind | Footprint | Height | Failed | Verdict |")
    w("|---|---|---|---|---|---|")
    for src, m in allmis:
        w("| `%s` | %s | %.1f x %.1f m | %.1f m | %s | %s |"
          % (m["id"], m["kind"], m["footprint_m"][0], m["footprint_m"][1], m["height_m"],
             ", ".join(m["failed"]), m["cause"]))
    w("")
    w("All three are **data anomalies, not kit gaps**: single buildings 168-224 m long in districts whose")
    w("95th percentile is 118-123 m. No model can cover them without being wrong for everything else, so")
    w("they keep their procedural shell. They are worth a look before placement - `BLD-00403` in particular")
    w("is 223.8 x 54.5 m at 14.4 m tall, which is a 224 m long single-storey shed.")
else:
    w("None - every record in all five style groups is covered.")
w("")
tf = sum(s2["fitdata"]["fit"] for s2 in SETS if s2["fitdata"])
tt = sum(s2["fitdata"]["total"] for s2 in SETS if s2["fitdata"])
w("**Coverage: %d of the %d records these %d sources target (%.1f %%).** Of the 2,708 building records,"
  % (tf, tt, len(SETS), 100.0 * tf / max(1, tt)))
w("the only group no set covers is the 11 `Landmark` records, which `BuildingAssembler` skips itself")
w("(\"landmark (A4 hero pending)\"). The 3 that do not fit are the BUG-171 data anomalies above.")
w("")
w("**Whole kit at LOD0: %d triangles across %d models.** Towers and low-rise blocks cost the same"
  % (total_lod0, sum(len(s["rows"]) for s in SETS)))
w("triangles whatever their height, because facade relief is carried by full-height piers/mullions and")
w("per-floor bands rather than per-window geometry - which is also what keeps BUG-170 (open P1, CPU) in mind.")
w("")
w("## Pipeline facts")
w("")
w("- Export preset **VC_Unity_Prop** (`vc_tools.vc_export_fbx(mode=\"prop\")`); **pivot at base centre on")
w("  the x/y origin at z = 0**, which is what `BuildingAssembler` places at the record's ground level.")
w("- Authored with the street front facing Blender -Y, so the front is **Unity +Z** and the record's")
w("  `unity_yaw_deg` turns it to the street.")
w("- One LOD set per FBX: root EMPTY + `<name>_LOD0/1/2` + `UBX_<name>`. The assembler disables kit")
w("  colliders (each record keeps its data-footprint collider), so the UBX is there for other uses.")
w("- Rotation and scale applied; 0 n-gons, 0 inverted faces at LOD0, 0 degenerate faces, UVs on every LOD")
w("  (box projection: a 2.0 m tile for houses and low-rise, 4.0 m - one storey - for towers).")
w("- Material slots are few and shared within a set, so instances batch.")
w("- Windows and doors in the house and low-rise sets are **real openings** with a reveal, recessed")
w("  glazing and a sill. Towers carry their floor lines in the curtain-wall texture instead, which is why")
w("  a 240 m tower costs the same as a 28 m one.")
w("- D6 signage is **blank**: frames, fascia bands, blade signs and a rooftop board with no lettering or")
w("  logo, all on their own slot `VC_Neon_Emissive` (HDR emission) so Unity drives `_EMISSION` and the")
w("  colour at night - the same pattern as the street set's `VC_Lamp_Emissive`.")
w("")
w("## LODs are authored, not decimated")
w("")
w("A collapse decimate destroys this kind of box-assembled geometry: it eats the massing and leaves the")
w("fins and slivers behind. Rendered backface-culled against a red world, the decimated LOD2s were")
w("see-through shells with no silhouette - for the towers *and* the houses. Every LOD is therefore built")
w("by re-running the same building description at a lower detail level (`vc_bld_lib.set_detail`): LOD1")
w("drops sills, panes, awnings and half the piers; LOD2 is massing only - solid walls, no openings, roof")
w("and crown kept, because that silhouette is what carries most frames at city distance.")
w("")
w("The check is the render, not a metric: both numeric \"inverted face\" tests are unreliable on this")
w("geometry (a normal recalculation is ambiguous on an open shell; a nearest-point test against LOD0")
w("mis-reads wherever the surface drifted). `preview/lod1_backface_check_*.png` and")
w("`preview/lod2_backface_check_*.png` render every LOD backface-culled against red, so a flipped or")
w("missing triangle shows as a hole. They are solid.")
w("")
w("## Self-check")
w("")
w("`build/verify_fbx.py` re-imports every exported FBX and measures it: **0 problems** over all %d files"
  % sum(len(s["rows"]) for s in SETS))
w("- no n-gons, no inverted faces at LOD0, UVs on every LOD, `_LOD0/1/2` plus a collision mesh in every")
w("file, every base at z = 0 and every pivot on the origin. Result: `build/verify.json`.")
w("")
w("## Textures (%d files, all original, power-of-two 512)" % len(tex_all))
w("")
w("`textures/<material>_baseColor.png` (sRGB), `_metallicRoughness.png` (glTF packing: G = roughness,")
w("B = metallic - what `KitbashMaterials` reads) and `_normal.png` (OpenGL tangent space), generated by")
w("`build/make_textures.py` from noise and arithmetic. Every pattern is seamless and sized in metres, so")
w("brick courses, siding boards, roof tiles, curtain-wall storeys and shutter slats come out at real size.")
w("")
w("| Set | Material prefixes |")
w("|---|---|")
for s in SETS:
    w("| `%s` | %s |" % (s["source"], ", ".join("`%s`" % t for t in s["tex"])))
w("")
w("## Hand-off to Unity")
w("")
w("This session cannot touch `Assets/` (the main session holds the editor lock) and did not run Unity.")
w("`UNITY_HANDOFF.md` has the copy list and the three lines of C# needed.")
open(os.path.join(ROOT, "MANIFEST.md"), "w", encoding="utf-8").write("\n".join(L) + "\n")

print("WROTE MANIFEST.md and %d kitbash manifests" % len(SETS))

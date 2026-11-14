"""Generate ArtSource/Blender/Infra/MANIFEST.md from build_stats.json + qa_report.json."""
import json
import os

HERE = os.path.dirname(os.path.abspath(__file__))
INFRA = os.path.abspath(os.path.join(HERE, ".."))

stats = json.load(open(os.path.join(HERE, "build_stats.json")))
qa = {r["name"]: r for r in json.load(open(os.path.join(HERE, "qa_report.json")))}

HEAD = """# Veyra City - road infrastructure prop set

Original geometry authored for Veyra City. Nothing here is derived from another game's assets:
the reference captures in `ReferenceImages/08_GTAV_VideoCapture_2026-11-12/` (VC_04 kerb profile,
VC_05 utility poles, VC_06 gantry + guardrail, VC_07 retaining wall, VC_08 piers, VC_33 gantry
truss) were read for construction logic and proportion only, and every vertex below is generated
by the scripts in `build/`.

* Blender 5.2.3 LTS, headless. Sources `*.blend`, exports `export/*.fbx`, review renders
  `review/*.png` (LOD0, 3/4 ortho).
* Rebuild: `"D:\\New folder\\blender.exe" -b --factory-startup -P build/build_all.py`
* Self-check: `... -P build/qa_check.py` - **%d/%d props pass with 0 issues** (report:
  `build/qa_report.json`).
* Export preset: `VC_Unity_Prop` via `Tools/Blender/vc_tools.py` (`vc_export_fbx(mode="prop")`),
  one LOD set per FBX, root empty + `*_LOD<n>` children so Unity builds a single LODGroup.

## Conventions

| Item | Value |
|---|---|
| Units | metric, 1 Blender unit = 1 m, scale 1.0 |
| Transforms | identity on every object (nothing to apply); root empty at the origin |
| Facing | model front = Blender -Y -> **Unity +Z**; Blender +X -> Unity -X; Blender +Z -> Unity +Y |
| Linear modules | run from the pivot toward Blender -Y, i.e. toward **Unity +Z** |
| Kerb side | the kerb rises toward **Unity +X** (right-hand kerb of a carriageway travelling +Z) |
| Gantry spans / mast arms | reach out toward **Unity +X** |
| Materials | PBR metal/roughness, Principled only, **no image textures** - the names below are stand-ins for the VeyraMaterialLibrary remap |
| UVs | world-aligned box projection, 1 UV unit = 1 m, ready for tiling materials |
| Shading | flat on hard surfaces, smooth only on >= 8-sided tubes |
| Collision | `COL_` non-convex, `UCX_` convex, `UBX_` box; collision meshes never render |

## Triangle counts

| FBX / .blend | LOD0 | LOD1 (%% of LOD0) | LOD2 (%% of LOD0) | Collision | Type | Pivot | LOD0 size (B x,y,z m) |
|---|---|---|---|---|---|---|---|
"""


def pct(n, base):
    return "%d (%d%%)" % (n, round(100.0 * n / base)) if base else str(n)


rows = []
for r in stats:
    t = r["tris"]
    l1 = pct(t[1], t[0]) if len(t) > 1 else "-"
    l2 = pct(t[2], t[0]) if len(t) > 2 else "- (prop < 2 m / module: LOD0+LOD1)"
    col = "%s (%d tris)" % (r["collision"], r["collision_tris"]) if r["collision"] else "none"
    rows.append("| `%s` | %d | %s | %s | %s | %s | %s | %s |"
                % (r["name"], t[0], l1, l2, col, r["collision_type"],
                   r["pivot"].split(" - ")[0], " x ".join("%.2f" % d for d in r["dims_lod0"])))

body = HEAD % (sum(1 for v in qa.values() if not v["issues"]), len(qa)) + "\n".join(rows)

body += "\n\n## Per-prop notes\n"
for r in stats:
    body += "\n### `%s`\n\n- **Pivot:** %s\n- **Collision:** %s\n- **Materials:** %s\n- %s\n" % (
        r["name"], r["pivot"], r["collision_type"],
        ", ".join(m for m in r["materials"] if m != "VC_Collision") or "-", r["notes"])

body += """
## What the self-check verifies

`build/qa_check.py` opens every `.blend`, then re-imports every `.fbx` and compares:

- root empty present, named after the file, at the origin, rotation 0, scale 1;
- every mesh parented to that root with an identity transform (rotation and scale applied);
- LOD names contiguous `_LOD0.._LODn`, collision names prefixed `COL_`/`UCX_`/`UBX_` and matching
  the prop name;
- **0 n-gons** anywhere (caps are triangulated, sides are quads);
- **0 inverted shells** (per-shell signed volume > 0), 0 open shells, 0 degenerate faces, 0 loose
  vertices, 0 non-manifold edges;
- a UV layer and a real material on every mesh;
- the FBX contains exactly one root and the same meshes with the same triangle counts.

## Notes for the Unity-side integration

1. **Budgets met:** kerb module 32 tris (limit 200), guardrail module 184 (limit 800), gantry truss
   1760 / 2760 tris for the 12 m / 20 m spans (limit 4000). Everything is single-material-per-part
   and instanceable, which matters while BUG-170 (CPU budget) is open.
2. **LOD ratios:** the ~40 % / ~10 % targets hold on the heavy props (gantries, legs, poles,
   guardrails). On the small concrete modules - kerbs, aprons, Jersey barriers - LOD0 is already
   16-76 tris, so a 40 % LOD1 would be a box; these ship LOD0 + LOD1 under the "prop < 2 m" rule
   and the actual percentages are printed in the table rather than forced.
3. **Two deliberate pivot deviations**, both for parts that hang rather than stand:
   `VC_SignPanel_Blank_*` pivots at the TOP centre of the panel face and `VC_Signal_Head_3` at the
   TOP centre of its mounting bracket. Say the word and they move to base centre.
4. **Sign panels are blank.** No text, numerals, symbols, shields or arrows exist in the geometry,
   by design - Veyra's own signage is applied on the Unity side.
5. **No cables.** The utility poles carry insulators only; the catenary is generated procedurally
   in Unity, as agreed.
6. **No image textures** are shipped, so the power-of-two rule does not bite yet. Every mesh does
   carry a 1 m-per-UV-unit box projection, so tiling materials from the library land at a sane
   texel density without re-unwrapping.
7. **Kerb surface mapping:** the gutter pan (first 450 mm from the road edge) wants the Asphalt or
   a dedicated Gutter `SurfaceDefinition`; the face and top want Kerb. The collision meshes are
   single-material, so the split has to happen on the render mesh or by splitting `COL_` if wheel
   surface response needs it.
8. **PLACEMENT FACTS THAT PER-PROP DATA DOES NOT SHOW.** A tiling module's correctness is a
   property of the RUN, not of the prop, and nothing in the self-check can see it:
   - **The guardrail is HANDED.** Rail face toward Unity -X when the module runs +Z, post on +X
     behind it. For the opposite road edge, rotate 180 deg about Y and step backwards along the
     tangent. Never mirror with a negative scale: it flips the normals.
   - **Guardrail pitch is exactly 4.00 m**, not the 4.12 m bounding length. The rail spans
     y = 0 to -4.00; the post, blockout and splice bolt reach 0.10 m back past the start plane so
     that in a run the splice post lands over the joint between two rails, as a real W-beam does.
   - **Every guardrail run wants `VC_Guardrail_WBeam_End` at both ends.** Departure end: same
     orientation as the modules. Approach end: rotated 180 deg about Y so the flare points away
     from oncoming traffic.
   - **The Jersey barrier pivots at BASE CENTRE, not the start of the piece** - the one exception
     in this set, following CLAUDE.md's rule for barriers rather than for road modules. Offset the
     first instance by +1.50 m along the tangent before stepping 3.00 m, or every median overhangs
     its start by half a module.
   - **`VC_Barrier_Jersey_EndTaper` takes the SAME yaw rule as the guardrail terminals**, and is
     base-centre like the barrier. Derived from the source: its full-height section is at
     Blender +Y, which is Unity **-Z**, and it tapers toward +Z. So at the **+Z (far) end of a
     run it is placed UNROTATED** - full height toward the run, tapering away - and at the
     **-Z (near) end it is turned 180 deg about Y**. (An earlier revision of this file said it
     needed the turn at the +Z end. That was backwards and is corrected here.)
   - Kerb, corner, apron and drain inlet need none of this: start-of-piece pivots, flat butt
     profiles, no protruding hardware, tiling at exactly 4.00 / 4.00 / 1.20 m.

9. **Left-hand kerb returns** are `VC_Kerb_Corner_90` mirrored on X. A mirrored instance flips
   normals in Unity unless the importer handles it; ask for a dedicated `_L` variant if that bites.
"""

with open(os.path.join(INFRA, "MANIFEST.md"), "w") as fh:
    fh.write(body)
print("wrote MANIFEST.md (%d rows)" % len(rows))

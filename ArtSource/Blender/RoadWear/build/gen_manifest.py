"""Generate ArtSource/Blender/RoadWear/MANIFEST.md from the build and QA reports."""
import json
import os

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, ".."))

_stats = json.load(open(os.path.join(HERE, "texture_stats.json")))
tex = _stats["atlases"] if isinstance(_stats, dict) else _stats
tiles = _stats.get("tiling", []) if isinstance(_stats, dict) else []
mesh = json.load(open(os.path.join(HERE, "mesh_stats.json")))
qa = json.load(open(os.path.join(HERE, "qa_report.json")))
qa_bad = sum(1 for r in qa["textures"] + qa["meshes"] if r["issues"])
qa_total = len(qa["textures"]) + len(qa["meshes"])

doc = []
w = doc.append

w("""# Veyra City - road surface wear (Layer 2)

Decal atlases and flush road-surface meshes. Everything is original: the textures are generated
from seeded numpy noise by `build/vc_tex.py` - no photographs, no traced or sampled imagery.
The reference captures (`ReferenceImages/08_GTAV_VideoCapture_2026-11-12/` VC_04, VC_15, VC_21,
VC_24) were read only for WHERE wear accumulates and WHAT SHAPE it takes; no colour, black level,
contrast or saturation value is taken from them, because they are compressed video captures.

* Rebuild textures: `"D:\\New folder\\blender.exe" -b --factory-startup -P build/build_textures.py`
  (plain Python 3 + numpy also works - the texture path never touches bpy)
* Rebuild meshes:   `... -P build/build_meshes.py`
* Self-check:       `... -P build/qa_check.py` - **{QA} pass with 0 issues**
  (`build/qa_report.json`)
* Previews: `review/*_preview.png` (each decal composited over flat asphalt and lit by its own
  normal map), `review/_contact_sheet.png`, `review/VC_*.png` (mesh 3/4 orthos).

## 1. Decal atlases

Each atlas is a 2 x 2 grid of decals. Three maps per atlas:

| File | Format | Channels |
|---|---|---|
| `<atlas>_BC.png` | RGBA, **sRGB** | RGB = albedo the decal paints, A = decal coverage |
| `<atlas>_N.png` | RGB, **raw / Normal Map** | tangent-space normal, OpenGL (+Y up) |
| `<atlas>_MS.png` | RGBA, **raw** | R = metallic (0 everywhere), G = AO, B = height, A = **smoothness** |

* **URP Lit packing as the project requires it:** smoothness (= 1 - roughness) lives in the
  alpha of the metallic map, so the material must use Smoothness Source = **Metallic Alpha** and
  `_MS` goes in the Metallic slot. `_N` must be imported with Texture Type = Normal Map; `_MS`
  with sRGB **off**.
* **Height channel (MS.b)**: 0.5 = the road plane, full range +/- 20 mm. It is there for
  parallax/blend-height use; the normal map already carries the same relief.
* **Border alpha**: every cell fades to alpha 0 over the outer 7 % of the cell, so a decal
  blends into asphalt instead of showing a rectangle. The QA script fails if any cell border
  is not exactly 0.
* **Smoothness** is the decal's own target value (dry Veyra asphalt reference = 0.25): rubber
  and polished wheel paths go up, crack interiors and grime go down, fresh fluid goes to ~0.8.
* All sizes are power-of-two. 8-bit PNG sources; compress to BC7 (PC) / ASTC 6x6 (Android) on
  import, and BC5 for the normal maps.

### Atlas table
""")

w("| Atlas | Size | Cell | Cell world size | Cells (UV rect x, y, w, h) | Source bytes |")
w("|---|---|---|---|---|---|")
for a in tex:
    cells = "<br>".join("`%s` (%.1f, %.1f, %.1f, %.1f)"
                        % (c["cell"], c["uv_rect"][0], c["uv_rect"][1], c["uv_rect"][2],
                           c["uv_rect"][3]) for c in a["cells"])
    w("| `%s` | %d x %d | %d px | %.2f m across x %.2f m along | %s | %.2f MB |"
      % (a["name"], a["size"], a["size"], a["cell_px"], a["cell_m"][0], a["cell_m"][1],
         cells, sum(a["bytes"].values()) / 1e6))

w("\n### Per-decal ranges\n")
w("| Atlas | Cell | Peak alpha | Relief (mm) | Smoothness range |")
w("|---|---|---|---|---|")
for a in tex:
    for c in a["cells"]:
        w("| `%s` | `%s` | %.2f | %+.1f .. %+.1f | %.2f .. %.2f |"
          % (a["name"], c["cell"], c["alpha_max"], c["height_mm"][0], c["height_mm"][1],
             c["smoothness"][0], c["smoothness"][1]))

w("""
## 1b. Tiling footway material

Not decals: seamless tiling MATERIAL textures for the footway surface, which the gap analysis
calls out as "untextured flat concrete; no panel joints, no tonal variation" (gap #4). Same
BC/N/MS packing as the atlases, except the albedo is **opaque RGB** (no alpha channel) because
the whole surface is covered. `qa_check.py` measures the wrap discontinuity against the
texture's own local contrast and fails anything that is not seamless.

| Texture | Size | Tile world size | Relief (mm) | Smoothness range | Source bytes |
|---|---|---|---|---|---|""")
for t in tiles:
    w("| `%s` | %d x %d | %.2f m (4 x 4 panels of 1.20 m) | %+.1f .. %+.1f | %.2f .. %.2f | %.2f MB |"
      % (t["name"], t["size"], t["size"], t["tile_m"], t["height_mm"][0], t["height_mm"][1],
         t["smoothness"][0], t["smoothness"][1], sum(t["bytes"].values()) / 1e6))

w("""
### DO NOT "FIX" LATER: the footway tile carries no distinctive features

`T_SW_Concrete_Panel` deliberately has **no cracks, no stains and no recognisable damage** - only
mottling, per-panel tone and chipped joints. This is not an omission to be filled in.

A distinctive feature in a tiling base becomes a *repeating* feature the moment the surface is
longer than the tile, and a pavement is always longer than 4.80 m. One memorable crack becomes
the same crack every 4.8 m down the whole street, which reads worse than no crack at all.
Anything recognisable belongs in the non-repeating decal atlases, which is what `T_RW_Crack`,
`T_RW_Patch`, `T_RW_Oil`, `T_RW_Grime` and `T_RW_Contact` are for. Agreed with the Unity session
on 2026-11-14 after an earlier version of this texture shipped hairline cracks and the 2x2 preview
showed them repeating.

The same rule applies to any future tiling surface in this set: incident goes in decals, not in
the tile.

## 2. Road-surface meshes

Same conventions as the Infra prop set: `VC_Unity_Prop` export preset via
`Tools/Blender/vc_tools.py`, root empty + `<name>_LOD<n>` children (one LOD set per FBX),
`COL_`/`UCX_`/`UBX_` collision in the same file, applied transforms, box UVs at 1 m per UV unit,
PBR metal/roughness stand-in materials for the VeyraMaterialLibrary remap.

**z = 0 is the road surface for every prop here.** All of them are driven over, so each one
carries a non-convex `COL_` collider that is a single flush plate - the collider never follows
the bars or the pattern relief, so wheels roll across instead of catching. The CLAUDE.md rule
(no vertical step over 0.05 m between adjacent collider surfaces) is checked by `qa_check.py`,
which fails the build if any collider rises more than 0.05 m above the road plane.

| FBX / .blend | LOD0 | LOD1 (% of LOD0) | Collision | Type | Collider top above road | Size (m) | Pivot |
|---|---|---|---|---|---|---|---|""")
for m in mesh:
    t = m["tris"]
    col = ("%s (%d tris)" % (m["collision"], m["collision_tris"]) if m["collision"]
           else "**none, deliberately**")
    w("| `%s` | %d | %d (%d%%) | %s | %s | **+%.0f mm** | %s | %s |"
      % (m["name"], t[0], t[1], round(100.0 * t[1] / t[0]), col,
         m["collision_type"].split(" (")[0], m["collision_top_m"] * 1000,
         " x ".join("%.2f" % d for d in m["dims_lod0"]), m["pivot"].split(";")[0]))

w("\n### Per-mesh notes\n")
for m in mesh:
    w("- **`%s`** - %s Materials: %s."
      % (m["name"], m["notes"], ", ".join(x for x in m["materials"] if x != "VC_Collision")))

w("""
Every prop here is under 2 m, so it ships LOD0 + LOD1 per the CLAUDE.md small-prop rule.

## 3. What the self-check verifies

Textures are read back **from the written PNG files**, not from the generator:
power-of-two and square, channel count per map, alpha exactly 0 on every cell border, each cell
reaching a usable peak alpha, unit-length normals with positive Z, metallic exactly 0,
smoothness non-empty.

Meshes: root empty at the origin with identity transform, every mesh parented to it with
rotation and scale applied, contiguous `_LOD` names, collision prefix and name match, 0 n-gons,
0 inverted shells, 0 open shells, 0 degenerate faces, 0 loose verts, UV and material present,
a collider on every prop, **collider top <= 0.05 m above the road plane**, and the FBX
re-imported with one root and matching triangle counts.

## 4. Notes for the Unity-side integration

1. **Decal projection scale.** Each atlas cell has a real-world size (table above); drive the
   decal projector from it instead of guessing. `T_RW_LaneWear` cells are **1.0 m across x 4.0 m
   along** so a 0.15 m line is a real 0.15 m - the other atlases are square.
2. **Lane-wear decals carry the paint itself**, not just wear: the white edge line, the dashed
   lane line and the double yellow centre line are drawn worn, so they can either replace a
   painted line or be laid over one at low alpha.
3. **Normal maps are OpenGL (+Y up)** - Unity's convention. No channel flip needed.
4. **Alpha is coverage, not opacity of a full material.** In a URP decal shader, feed BC.a to
   both base-colour and normal/MS blend so a decal does not flatten the road's own normal where
   it is faint.
5. **Mesh pivots** are at the road surface, so a cover drops straight onto the road mesh at the
   road's own height. The castings continue below z = 0 (down to -0.09 m for the grate sump), so
   they need a hole or simply sink into the road mesh.
6. `VC_ExpansionJoint_Strip_4m` runs toward **Unity +Z** like the other linear modules, pivot at
   the start of the piece. Rotate 90 deg for a transverse joint.
7. The joint sealant material `VC_Joint_Sealant` is new in this set (registered at runtime in
   `props_roadwear.py` so `ArtSource/Blender/Infra/` is left untouched); it needs an entry in the
   VeyraMaterialLibrary alongside the Infra names.
8. **Markings are drawn at true size** inside their 4 m cells (0.50 m zebra stripes on 1.32 m
   centres, 0.30 m arrow shaft, 0.60 m stop bar), lightly worn rather than pristine, with a
   tyre-crossing scuff where wheels actually cross them. `arrow_turn_right` mirrors on U for the
   left-turn arrow. They complement the worn lane lines in `T_RW_LaneWear`: use these where a
   marking should read as maintained, those where it should read as neglected.
9. **Puddles are a wet-surface override, not geometry**: the albedo darkens, smoothness goes to
   ~0.85 in standing water, and the only relief is a sub-millimetre ripple. Drive them from the
   same wetness parameter as the rain system so they fade in and out with the weather rather
   than sitting on a dry road.
10. **The contact atlas is the cheap half of gap #4** - a grime ring at the base of a pole,
    a dirt line with joint weeds where the footway meets the kerb, wall-base splash and drip
    runs. These are what stop props looking stuck on. `wall_base_splash` and `drip_run` are
    authored for a vertical surface (V runs up the wall).
11. **BUG-170 (CPU budget):** the meshes are 40-732 tris with one or two materials each and are
   meant to be GPU-instanced; the decals are 6 atlases, so a whole street's wear is a handful of
   draw calls if the decals share a material per atlas.
""")

with open(os.path.join(ROOT, "MANIFEST.md"), "w") as fh:
    fh.write("\n".join(doc).replace("{QA}", "%d/%d" % (qa_total - qa_bad, qa_total)))
print("wrote MANIFEST.md")

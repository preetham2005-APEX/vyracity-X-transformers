# Veyra City - road infrastructure prop set

Original geometry authored for Veyra City. Nothing here is derived from another game's assets:
the reference captures in `ReferenceImages/08_GTAV_VideoCapture_2026-11-12/` (VC_04 kerb profile,
VC_05 utility poles, VC_06 gantry + guardrail, VC_07 retaining wall, VC_08 piers, VC_33 gantry
truss) were read for construction logic and proportion only, and every vertex below is generated
by the scripts in `build/`.

* Blender 5.2.3 LTS, headless. Sources `*.blend`, exports `export/*.fbx`, review renders
  `review/*.png` (LOD0, 3/4 ortho).
* Rebuild: `"D:\New folder\blender.exe" -b --factory-startup -P build/build_all.py`
* Self-check: `... -P build/qa_check.py` - **18/18 props pass with 0 issues** (report:
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

| FBX / .blend | LOD0 | LOD1 (% of LOD0) | LOD2 (% of LOD0) | Collision | Type | Pivot | LOD0 size (B x,y,z m) |
|---|---|---|---|---|---|---|---|
| `VC_Kerb_Straight_4m` | 32 | 16 (50%) | - (prop < 2 m / module: LOD0+LOD1) | COL_dry_asphalt_VC_Kerb_Straight_4m_Gutter + COL_kerb_VC_Kerb_Straight_4m_Kerb (32 tris) | COL (non-convex MeshCollider) x2: dry_asphalt gutter + kerb face/top | start of the piece, at the road edge of the gutter pan, z = 0 = road surface | 0.90 x 4.00 x 0.49 |
| `VC_Kerb_Corner_90` | 158 | 46 (29%) | - (prop < 2 m / module: LOD0+LOD1) | COL_dry_asphalt_VC_Kerb_Corner_90_Gutter + COL_kerb_VC_Kerb_Corner_90_Kerb (132 tris) | COL (non-convex MeshCollider) x2: dry_asphalt gutter + kerb face/top | start of the piece (same datum as VC_Kerb_Straight_4m) | 3.45 x 3.45 x 0.49 |
| `VC_Kerb_DriveApron_4m` | 68 | 36 (53%) | - (prop < 2 m / module: LOD0+LOD1) | COL_dry_asphalt_VC_Kerb_DriveApron_4m_Gutter + COL_kerb_VC_Kerb_DriveApron_4m_Kerb (72 tris) | COL (non-convex MeshCollider) x2: dry_asphalt gutter + kerb face/top | start of the piece (same datum as VC_Kerb_Straight_4m) | 0.90 x 4.00 x 0.49 |
| `VC_Drain_Inlet_Kerb` | 240 | 128 (53%) | - (prop < 2 m / module: LOD0+LOD1) | COL_dry_asphalt_VC_Drain_Inlet_Kerb_Gutter + COL_kerb_VC_Drain_Inlet_Kerb_Kerb (32 tris) | COL (non-convex MeshCollider) x2: dry_asphalt gutter + kerb face/top (the gutter solid closes the sump) | start of the piece (same datum as VC_Kerb_Straight_4m) | 0.90 x 1.20 x 0.49 |
| `VC_Guardrail_WBeam_4m` | 164 | 60 (37%) | 24 (15%) | UBX_VC_Guardrail_WBeam_4m (12 tris) | UBX (BoxCollider) | start of the piece, post centreline, ground level (z = 0), rail back plane (x = 0) | 0.36 x 4.10 x 1.85 |
| `VC_Guardrail_WBeam_End` | 252 | 104 (41%) | 36 (14%) | UCX_VC_Guardrail_WBeam_End (12 tris) | UCX (convex MeshCollider) | start of the piece (mates with VC_Guardrail_WBeam_4m), ground level | 0.87 x 4.20 x 1.65 |
| `VC_Barrier_Jersey_3m` | 76 | 28 (37%) | - (prop < 2 m / module: LOD0+LOD1) | UCX_VC_Barrier_Jersey_3m (28 tris) | UCX (convex MeshCollider) | base centre (centre of the 3.00 m length, cross-section centre, z = 0) | 0.61 x 3.00 x 0.81 |
| `VC_Barrier_Jersey_EndTaper` | 76 | 28 (37%) | - (prop < 2 m / module: LOD0+LOD1) | UCX_VC_Barrier_Jersey_EndTaper (28 tris) | UCX (convex MeshCollider) | base centre (centre of the 3.00 m length, cross-section centre, z = 0) | 0.61 x 3.00 x 0.81 |
| `VC_Gantry_Truss_12m` | 1760 | 704 (40%) | 120 (7%) | none | none (overhead) | start of the span, bottom-chord plane, truss centreline (x = y = z = 0) | 12.06 x 1.01 x 1.41 |
| `VC_Gantry_Truss_20m` | 2760 | 1064 (39%) | 156 (6%) | none | none (overhead) | start of the span, bottom-chord plane, truss centreline (x = y = z = 0) | 20.06 x 1.01 x 1.41 |
| `VC_Gantry_Leg_Lattice` | 1256 | 488 (39%) | 120 (10%) | UBX_VC_Gantry_Leg_Lattice (12 tris) | UBX (BoxCollider) | base centre, z = 0 at the top of the foundation | 0.90 x 0.90 x 7.64 |
| `VC_Gantry_Leg_Concrete` | 96 | 48 (50%) | 24 (25%) | UBX_VC_Gantry_Leg_Concrete (12 tris) | UBX (BoxCollider) | base centre, z = 0 at the top of the foundation | 1.05 x 0.85 x 7.86 |
| `VC_SignPanel_Blank_4800x2400` | 84 | 36 (43%) | 12 (14%) | none | none (overhead) | TOP centre of the panel face | 4.80 x 0.17 x 2.40 |
| `VC_SignPanel_Blank_2400x1500` | 84 | 36 (43%) | 12 (14%) | none | none (overhead) | TOP centre of the panel face | 2.40 x 0.17 x 1.50 |
| `VC_SignalMast_Cantilever` | 280 | 88 (31%) | 24 (9%) | UCX_VC_SignalMast_Cantilever (28 tris) | UCX (convex MeshCollider) | base centre of the upright, z = 0 at the top of the foundation | 5.77 x 0.52 x 7.00 |
| `VC_Signal_Head_3` | 240 | 96 (40%) | - (prop < 2 m / module: LOD0+LOD1) | none | none (overhead) | TOP centre of the mounting bracket | 0.70 x 0.52 x 1.44 |
| `VC_UtilityPole_Timber` | 456 | 152 (33%) | 24 (5%) | UCX_VC_UtilityPole_Timber (28 tris) | UCX (convex MeshCollider) | base centre at the ground line (z = 0); the butt continues to -0.10 m | 2.45 x 0.52 x 11.10 |
| `VC_UtilityPole_Transmission` | 660 | 188 (28%) | 36 (5%) | UCX_VC_UtilityPole_Transmission (28 tris) | UCX (convex MeshCollider) | base centre at the ground line (z = 0); the butt continues to -0.10 m | 3.00 x 0.35 x 15.05 |

## Per-prop notes

### `VC_Kerb_Straight_4m`

- **Pivot:** start of the piece, at the road edge of the gutter pan, z = 0 = road surface
- **Collision:** COL (non-convex MeshCollider) x2: dry_asphalt gutter + kerb face/top
- **Materials:** VC_Concrete_Kerb
- 4.00 m module; 150 mm upstand + 25 mm chamfer + 450 mm gutter pan. Runs toward Unity +Z, kerb rises toward Unity +X. Surfaces: Kerb (face/top), Asphalt (pan).

### `VC_Kerb_Corner_90`

- **Pivot:** start of the piece (same datum as VC_Kerb_Straight_4m)
- **Collision:** COL (non-convex MeshCollider) x2: dry_asphalt gutter + kerb face/top
- **Materials:** VC_Concrete_Kerb
- 90 deg kerb return, 3.00 m face radius, turning toward Unity +X (footway on the inside). The left-hand return is this piece mirrored on X.

### `VC_Kerb_DriveApron_4m`

- **Pivot:** start of the piece (same datum as VC_Kerb_Straight_4m)
- **Collision:** COL (non-convex MeshCollider) x2: dry_asphalt gutter + kerb face/top
- **Materials:** VC_Concrete_Kerb
- 4.00 m dropped-kerb driveway apron: 0.80 m flare down, 2.40 m dropped crossing (30 mm upstand), 0.80 m flare up. Tiles between straight kerb modules.

### `VC_Drain_Inlet_Kerb`

- **Pivot:** start of the piece (same datum as VC_Kerb_Straight_4m)
- **Collision:** COL (non-convex MeshCollider) x2: dry_asphalt gutter + kerb face/top (the gutter solid closes the sump)
- **Materials:** VC_Concrete_Kerb, VC_Iron_Cast
- 1.20 m combination inlet: 0.90 m kerb throat (140 mm clear under a cast lintel) over a 0.90 x 0.42 m sump with a 7-bar cast grate. Collision closes the sump so wheels cannot drop in.

### `VC_Guardrail_WBeam_4m`

- **Pivot:** start of the piece, post centreline, ground level (z = 0), rail back plane (x = 0)
- **Collision:** UBX (BoxCollider)
- **Materials:** VC_Steel_Galv
- 4.00 m post-and-rail module, rail centre 550 mm, 1.85 m C-section post at the module start. The rail face looks toward Unity -X (the carriageway); the module runs toward Unity +Z. PITCH IS EXACTLY 4.00 m: the rail spans y = 0 to -4.00. The post, blockout and splice bolt straddle the start plane and reach 0.10 m BACK past it, which is deliberate - in a run that puts the splice post over the joint between two rails, as a real W-beam does. So the bounding length is 4.12 m but the placement pitch is 4.00 m; do not space them by the bbox.

### `VC_Guardrail_WBeam_End`

- **Pivot:** start of the piece (mates with VC_Guardrail_WBeam_4m), ground level
- **Collision:** UCX (convex MeshCollider)
- **Materials:** VC_Steel_Galv
- 4.00 m flared end terminal: the rail drops 550 -> 200 mm and flares 0.60 m away from the carriageway into an impact head and ground anchor.

### `VC_Barrier_Jersey_3m`

- **Pivot:** base centre (centre of the 3.00 m length, cross-section centre, z = 0)
- **Collision:** UCX (convex MeshCollider)
- **Materials:** VC_Concrete_Barrier
- 3.00 m NJ-type module, 815 mm high, 610 mm base, 156 mm top; the end 50 mm is inset 15 mm for the lap joint, so modules read as separate castings.

### `VC_Barrier_Jersey_EndTaper`

- **Pivot:** base centre (centre of the 3.00 m length, cross-section centre, z = 0)
- **Collision:** UCX (convex MeshCollider)
- **Materials:** VC_Concrete_Barrier
- 3.00 m tapered end: 815 -> 360 mm over the run, same NJ profile truncated. The full-height end (Blender +Y, Unity -Z) mates with VC_Barrier_Jersey_3m.

### `VC_Gantry_Truss_12m`

- **Pivot:** start of the span, bottom-chord plane, truss centreline (x = y = z = 0)
- **Collision:** none (overhead)
- **Materials:** VC_Steel_Galv
- 12 m box-truss span, 0.90 m wide x 1.25 m deep, 110 mm chords. The span reaches toward Unity +X; set the pivot at the leg-top level (legs are 7.60 m, giving 6.35 m clear under a 4.80 x 2.40 m panel hung from the rails).

### `VC_Gantry_Truss_20m`

- **Pivot:** start of the span, bottom-chord plane, truss centreline (x = y = z = 0)
- **Collision:** none (overhead)
- **Materials:** VC_Steel_Galv
- 20 m box-truss span, 0.90 m wide x 1.25 m deep, 110 mm chords. The span reaches toward Unity +X; set the pivot at the leg-top level (legs are 7.60 m, giving 6.35 m clear under a 4.80 x 2.40 m panel hung from the rails).

### `VC_Gantry_Leg_Lattice`

- **Pivot:** base centre, z = 0 at the top of the foundation
- **Collision:** UBX (BoxCollider)
- **Materials:** VC_Steel_Galv
- 7.60 m laced steel upright, 0.55 m square, 0.90 m base plate with four anchor bolts. Pairs with either gantry span.

### `VC_Gantry_Leg_Concrete`

- **Pivot:** base centre, z = 0 at the top of the foundation
- **Collision:** UBX (BoxCollider)
- **Materials:** VC_Concrete_Struct, VC_Steel_Galv
- 7.60 m tapered concrete upright (0.85 x 0.65 m at the plinth, 0.65 x 0.55 m at the capital) with lift-joint steps and a steel bearing plate at 7.84 m.

### `VC_SignPanel_Blank_4800x2400`

- **Pivot:** TOP centre of the panel face - the panel hangs from the gantry rails, so the pivot is the hanger line, not the base centre (deliberate deviation)
- **Collision:** none (overhead)
- **Materials:** VC_Sign_BlankFace, VC_Steel_Galv
- 4.80 x 2.40 m BLANK panel, 40 mm face, rear stiffeners and hanger brackets. No text or symbol geometry: Veyra's own signage is applied on the Unity side. The face looks toward Unity +Z.

### `VC_SignPanel_Blank_2400x1500`

- **Pivot:** TOP centre of the panel face - the panel hangs from the gantry rails, so the pivot is the hanger line, not the base centre (deliberate deviation)
- **Collision:** none (overhead)
- **Materials:** VC_Sign_BlankFace, VC_Steel_Galv
- 2.40 x 1.50 m BLANK panel, 40 mm face, rear stiffeners and hanger brackets. No text or symbol geometry: Veyra's own signage is applied on the Unity side. The face looks toward Unity +Z.

### `VC_SignalMast_Cantilever`

- **Pivot:** base centre of the upright, z = 0 at the top of the foundation
- **Collision:** UCX (convex MeshCollider)
- **Materials:** VC_Steel_Dark, VC_Steel_Galv
- 7.00 m tapered upright (320 -> 240 mm) with a 5.50 m cantilever arm rising from 6.40 to 6.75 m. The arm reaches toward Unity +X; heads hang below it.

### `VC_Signal_Head_3`

- **Pivot:** TOP centre of the mounting bracket - the head hangs from a mast arm, so the pivot is the mount point, not the base centre (deliberate deviation)
- **Collision:** none (overhead)
- **Materials:** VC_Signal_Lens, VC_Steel_Dark
- 3-head housing, 1.16 m body + backplate, 300 mm lenses with visors. Lenses face Unity +Z (toward oncoming traffic). Under 2 m, so LOD0 + LOD1 only.

### `VC_UtilityPole_Timber`

- **Pivot:** base centre at the ground line (z = 0); the butt continues to -0.10 m
- **Collision:** UCX (convex MeshCollider)
- **Materials:** VC_Porcelain, VC_Steel_Galv, VC_Timber_Pole
- 11.00 m timber distribution pole, 290 -> 190 mm, one 2.45 m crossarm at 10.20 m with four pin insulators, a three-spool secondary rack at 8.95 m and pole steps. NO cables - the catenary is generated procedurally in Unity. Crossarm axis runs along Unity X.

### `VC_UtilityPole_Transmission`

- **Pivot:** base centre at the ground line (z = 0); the butt continues to -0.10 m
- **Collision:** UCX (convex MeshCollider)
- **Materials:** VC_Porcelain, VC_Steel_Galv, VC_Timber_Pole
- 14.50 m timber transmission-style pole, 350 -> 210 mm, 3.00 m upper arm at 13.60 m (three insulators) and 2.45 m lower arm at 12.30 m (four), plus an earth-wire spike. NO cables.

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

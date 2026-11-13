# Street furniture set - DONE (session: street props)

16 props, each its own `.blend` + `.fbx`, in `E:\VeyraCity\ArtSource\Blender\Street\` and
`...\Street\export\`. Full table, material slots and collision list: `ArtSource/Blender/Street/MANIFEST.md`.

## For whoever does the Unity import

- All exported with `vc_tools.vc_export_fbx(mode="prop")` = **VC_Unity_Prop**. Import with the normal
  VeyraModelImportDefaults (Scale Factor 1, Convert Units on, Bake Axis Conversion on, Read/Write off).
- **Pivot = base centre at (0,0,0)**, front faces **Unity +Z**. Verified by re-import, not assumed
  (`ArtSource/Blender/Street/build/verify.json`, 0 problems over all 16 files).
- Each FBX root is an EMPTY with `<name>_LOD0/1/2` children -> Unity makes one LODGroup. Collision meshes
  (`UBX_` box, `UCX_` convex) are siblings in the same file and must be stripped from the renderers.
- **Luminaires**: slot 1 on every lamp is `VC_Lamp_Emissive` (HDR 1.00/0.92/0.74 @ 8). That is the slot to
  drive `_EMISSION` on at night. Remember CLAUDE.md: `_EMISSION` must be covered by a material in the
  build or by `VC_Variants.shadervariants`.
- Blank by design and waiting on Veyra artwork: `VC_Sign_BlankFace` (street-name blade, speed plate),
  `VC_Poster_Blank` (bus shelter, both faces of the poster bay).
- Hydrant ships one mesh; the second colour variant is the material `VC_Hydrant_Yellow` (present in
  `VC_Hydrant.blend` with a fake user).
- No texture maps yet - solid-colour metal/roughness Principled materials only. Slot order in the FBX is
  the Unity material order and is listed per prop in the MANIFEST.

## Cost (relevant to BUG-170)

Whole set is **3696 tris at LOD0** across 16 props; biggest single prop is the ornate twin lamp at 616.
Everything is one mesh with 1-4 material slots, so it instances well. LOD2 ratios were floored at ~56 tris
(a literal 10 % deletes the pole) - documented in the MANIFEST.

## Caveats

- Decimated LOD1/LOD2 meshes have a few non-manifold edges (collapse opens shells). LOD0 is fully clean.
- Preview renders for proportion review: `ArtSource/Blender/Street/preview/*.png` (front ortho + 3/4; the
  pale 1.80 m rods are a human-scale reference, not a prop).
- Regenerate anything with:
  `"D:\New folder\blender.exe" --factory-startup -b -noaudio -P ArtSource\Blender\Street\build\build_street.py -- all`
  then `verify_fbx.py`, `preview.py`, `make_manifest.py`. Re-runnable, no duplicates.
- Nothing written to `Assets/`, Unity never run, nothing committed, branch untouched.

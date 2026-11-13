# Material register — Blender kit imports

Decisions taken 2026-11-14 while the six authored kits were being prepared for import.
Recorded here because they were first written only into a side-session notes folder outside
the repo. **Correction:** that folder was not lost when its session ended — all nine notes are
now copied into `Docs/KitHandovers/`, including `infra-props-handover.md` and
`roadwear-handover.md`, whose authoring session has ended and which hold the `_MS` channel
layout, per-atlas world sizes and the flush-collider rule that appear in no MANIFEST. The
per-kit `ArtSource/Blender/*/MANIFEST.md` files remain the source of truth for per-asset
detail; this file holds the cross-kit decisions.

## Why this matters

`Tests/EditMode/ChunkImportTests.cs` requires every renderer material slot to resolve to a
material **registered in VeyraMaterialLibrary** — present in the project is not enough — and
fails if more than 5 % fall back to `M_Debug_Missing`. The FBX exports carry slot *names*
only. So every name below needs a library slot or the import errors.

Count: ~106–108 distinct names across six kits (buildings 68, facade 7, street furniture 19,
infra 11, roadwear 1 new, vegetation 5). The exact combined list is in
`Docs/KitHandovers/MATERIAL_REGISTER_ALL_SETS.md`.

**Two materials toggle `_EMISSION` at runtime, not one:** `VC_Fac_Emissive` (facade crown
light coves) and `VC_Neon_Emissive` (building kit). Both need coverage in
`Assets/_Veyra/Art/Shaders/VC_Variants.shadervariants` or a material in the build, or the
variant strips and the player throws — this is BUG-002's exact failure mode, and it works in
the editor either way, so it must be verified in a Dev Mono player.

The 11 category-scheme names in the building kit (`VC_Wall_*`, `VC_Roof_*`, `VC_Trim_Paint`,
`VC_Glass_Window`, `VC_Door_Wood`, `VC_Metal_Garage`) belong to the D5 suburban house set and
are deliberately not district-prefixed, per the standing rule below.

## Merged duplicate pairs

Independently authored kits named the same material twice. Merged, canonical name first.
The rule applied: **name the actual material, not a generic category.**

| Canonical | Dropped |
|---|---|
| `VC_Steel_Galv` | `VC_Metal_Galv` |
| `VC_Steel_Dark` | `VC_Metal_Dark` |
| `VC_Iron_Cast` | `VC_Metal_CastIron` |
| `VC_Sign_BlankFace` | `VC_SignPanel_Blank` |

Reason for merging at all: a guardrail and a lamp post standing side by side with two
different "galvanised" finishes is a bug a player sees. Saving four slots was the least of it.

## Values

At metallic 1.0 the base colour is **F0 (specular reflectance), not albedo**. Real steel and
iron sit around 0.55–0.60. Several authored values were dark colours at high metallic, which
renders as a near-black mirror — these read as black holes in wet-road reflections and would
have been chased as a lighting bug.

| Slot | Metallic | Base / albedo | Roughness | What it is |
|---|---|---|---|---|
| `VC_Steel_Galv` | 1.0 | 0.560 / 0.570 / 0.580 | 0.44 | Bare galvanised steel |
| `VC_Steel_Dark` | 0.0 | 0.060 / 0.062 / 0.065 | 0.45 | **Painted** steel only |
| `VC_Iron_Cast` | 1.0 | 0.440 / 0.430 / 0.420 | 0.72 | **Bare** cast iron — covers, grates |
| `VC_Iron_Painted` | 0.0 | 0.040 / 0.040 / 0.042 | 0.45 | **Painted** cast iron — lamp standards, bollards |

`VC_Iron_Painted` is a fourth slot, split out of the merge. The single slot was being asked to
serve a painted lamp standard and a bare iron manhole cover — dielectric paint over metal
versus bare metal, not the same material by any measure. The error would have been invisible
until a wet night scene put a reflection on both.

`VC_Steel_Dark` is **painted-only**. Any bare dark steel must not use it; a bare metal wearing
a metallic-0 paint material looks dead in exactly the night reflections this is meant to fix.
Applying that condition caught eight wrong assignments in the infra and roadwear kits
(guardrail splice bolts, end terminal impact head, gantry leg base/top plates and anchor
bolts → galvanised; manhole lift slots and utility cover chequer plate and studs → bare iron).

**Stale-table hazard:** any mapping written before the four-slot split is wrong. Street's
`VC_Metal_CastIron` goes to `VC_Iron_Painted` (ornate lamp standards), **not** `VC_Iron_Cast`.

## Standing rules

- **Per-set naming schemes may coexist.** Consistency within a set beats consistency across
  sets. `VC_Metal_Garage` stays as it is: it fits the building kit's own
  `VC_<Category>_<Variant>` scheme exactly and only looks wrong against the infra/street
  Steel/Iron scheme. Absorbing it into `VC_Steel_Dark` would make every suburban garage door
  near-black across three house models. Do not re-open this.
- **The eleven district concretes and ten glasses stay.** Deliberate style group, district
  identity is in the design. They share a template and differ by tint. Revisit only as a
  measured draw-call decision, never as tidying up.
- **Authoring is in roughness; conversion is at import.** Manifests quote roughness as prose.
  Do not switch them to smoothness — a manifest quoting the post-conversion number documents
  the import pipeline rather than the asset, and goes silently wrong the first time the
  convention changes.
- **Packed `_MS` maps already hold smoothness in alpha** per CLAUDE.md, from all three Blender
  sessions. They are import-ready. **Do not run a conversion over the texture.** A double
  inversion makes every rock, trunk and leaf read as wet plastic.

## Open, not settled

**Does ~108 materials cost us anything?** Raised against the CPU budget. Cannot be answered
with current evidence: materials cost draw calls rather than CPU, and `FrameCost` accounts for
only 9–23 % of frame time, so existing budget figures cannot speak to it. Measure against real
draw-call counts after import, then either keep the district split or collapse it with numbers
attached. Nobody should author around this in the meantime.

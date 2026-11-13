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

**`VC_Fac_Emissive` / `VC_Neon_Emissive` need no shadervariants work — checked 2026-11-14.**
Both Blender sessions flagged these as runtime-toggled and needing variant coverage. They do
not, for three independent reasons, any one of which is sufficient:
1. The importer **remaps** FBX material names to library materials, so neither authored
   material ever enters the build — the crown cove gets `NeonWhite`.
2. No non-Editor code in the project calls `EnableKeyword` or `DisableKeyword` at all
   (verified by search). Runtime code sets colours on materials whose keywords are serialized.
3. `VC_Neon.shader` declares no `_EMISSION` shader feature; emission is unconditional, so
   there is no variant to strip.
A reasonable thing for them to flag — they could see neither the remap nor the shaders. Do not
re-open it.

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

## Import mapping (2026-11-14)

The import does **not** need ~108 new library slots. `VeyraModelPostprocessor` maps each FBX
material *name* through `Tools/Blender/material_map.json` (regex, first match wins, optional
category filter) onto an existing `VeyraMat` slot. So the work was mapping, not authoring.

All 50 representative kit names now resolve; verified by running the names through the same
first-match-wins logic the importer uses, not by inspection.

### Two real mis-mappings this caught

- **`VC_Roof_Tile`, `VC_Cs_RoofTile` → `SidewalkConcrete`.** The generic `tile` rule fired
  before any roof rule, so every pitched suburban roof would have been surfaced with pavement
  concrete. Roof rules now sit above the sidewalk rule.
- Several painted-metal names (`VC_Steel_Dark`, `VC_Iron_Painted`, `VC_Metal_Garage`) fell
  through to `MetalGalvanized` — bare galvanised steel — because the generic
  `steel|metal|iron` rule caught them. They now map to `MetalPainted` ahead of it.

### Known-wrong and stand-in mappings — fix before shipping

| Name(s) | Maps to | Why it is wrong |
|---|---|---|
| all `*_Roof*`, `VC_Roof_Tile`, `VC_Roof_Shingle` | `RoofMembrane` | A dark flat commercial membrane on pitched suburban tile and shingle roofs. **Needs a `RoofTile` slot.** This is the most visible of the gaps. |
| `VC_Fac_Fabric` | `FacadeStuccoWhite` | Awning canvas as painted render. No cloth slot exists. |
| `VC_Trim_Paint` | `FacadeStuccoWhite` | Painted timber trim as wall render. Close, but not its own material. |
| `VC_Hydrant_Yellow` | `MetalPaintedRed` | Yellow hydrant rendered red. Only one painted-colour slot exists. |
| `VC_Signal_Lens` | `NeonSignalRed` | Signal lenses are red, amber and green; all three become red. |
| the 11 district concretes, 10 district glasses | `FacadeConcrete`, `GlassCurtainBlueGrey` | The district tints we deliberately kept collapse to one material each at import, so district identity is lost. Honouring that decision needs per-district slots. |

The last row matters: the decision above to keep district tints is **not** currently honoured
by the import. Either add the slots or revisit the decision — do not leave it looking kept
when it is not.

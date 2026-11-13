# VeyraMaterialLibrary — every material name the six authored sets need

> **DECIDED by the Unity session (2026-11-14).** The four duplicate pairs are merged; the
> district concretes and glasses are kept. Canonical names and the renames each set owes are in
> "Decisions" at the end. Re-exports fold into each session's next build — nobody re-runs a build
> just for this.

Compiled by the road-infrastructure session from all six MANIFESTs, at the facade session's
suggestion, so the Unity session gets one list instead of three. `ChunkImportTests` requires every
material slot to resolve to a material **registered in the library** — present is not enough — with
under 5 % falling back to `M_Debug_Missing`.

**Headline: 109 distinct names, not the ~75 the facade session estimated.** Its 68 + 7 was its own
two kits; street furniture adds 19, mine add 12, vegetation 3.

| Set | Names | Source |
|---|---|---|
| Buildings | 68 | `Buildings/UNITY_HANDOFF.md` §6b |
| Facade (not built yet) | 7 (`VC_Fac_*`, incl. `VC_Fac_Emissive`) | facade SPECS |
| Street furniture | 19 | `Street/MANIFEST.md` "Material library used by this set" |
| Infra | 11 | `Infra/build/vc_infra_lib.py` `MATERIALS` |
| RoadWear | 1 new (reuses 3 Infra names) | `RoadWear/build/props_roadwear.py` |
| Vegetation 1 + 2 | 3 | both vegetation MANIFESTs |

## Infra (11)

    VC_Concrete_Kerb      VC_Concrete_Barrier   VC_Concrete_Struct    VC_Asphalt_Gutter
    VC_Steel_Galv         VC_Steel_Dark         VC_Iron_Cast          VC_SignPanel_Blank
    VC_Timber_Pole        VC_Porcelain          VC_Signal_Lens

## RoadWear (1 new)

    VC_Joint_Sealant

Reuses `VC_Iron_Cast`, `VC_Steel_Dark`, `VC_Concrete_Struct`. The decal atlases are textures, not
mesh materials, so they need decal materials of their own rather than library slots — count 7 more
(6 first-wave atlases + 3 second-wave + 1 tiling footway, where several can share one material).

## Street furniture (19)

    VC_Hydrant_Red        VC_Hydrant_Yellow     VC_Metal_Galv         VC_Metal_Dark
    VC_Metal_CastIron     VC_Paint_Civic_Green  VC_Paint_Civic_Blue   VC_Paint_Slate
    VC_Paint_Orange       VC_Paint_White        VC_Paint_Alu_Grey     VC_Retro_White
    VC_Glass_Clear        VC_Sign_BlankFace     VC_Poster_Blank       VC_Concrete_Plinth
    VC_Wood_Slat          VC_Lamp_Emissive      (+ VC_Hydrant variant pair counted once)

## Vegetation (3)

    M_Veyra_Foliage       M_Veyra_Foliage_B     M_Veyra_Bark

## Buildings (68) and Facade (7)

See `Buildings/UNITY_HANDOFF.md` §6b for the full 68; the facade kit's 7 `VC_Fac_*` names land in
`Facade/MANIFEST.md` when it builds.

---

## Worth deciding before 109 slots are created

**1. There are near-duplicates across sets that were authored independently and could share one
slot each.** Nobody coordinated naming because the sets were authored in parallel:

| Probably one material | Names |
|---|---|
| galvanised steel | `VC_Steel_Galv` (Infra) / `VC_Metal_Galv` (Street) |
| dark painted steel | `VC_Steel_Dark` (Infra) / `VC_Metal_Dark` (Street) |
| cast iron | `VC_Iron_Cast` (Infra, RoadWear) / `VC_Metal_CastIron` (Street) |
| structural concrete | `VC_Concrete_Struct` (Infra) / `VC_Concrete_Plinth` (Street) / `VC_Wall_Concrete`, `VC_Al_Concrete`, `VC_Av_Concrete`, `VC_Cs_Concrete`, `VC_In_Concrete`, `VC_Mr_Concrete`, `VC_Mt_Concrete`, `VC_Nm_Concrete`, `VC_Rt_Concrete`, `VC_Ru_Concrete`, `VC_Twr_Concrete` (Buildings, per district) |
| blank sign face | `VC_SignPanel_Blank` (Infra) / `VC_Sign_BlankFace` (Street) / `VC_Poster_Blank` (Street) |
| glass | `VC_Glass_Clear` (Street) / `VC_Glass_Window`, `VC_*_Glass` x10 (Buildings) |

The building kit's per-district split is deliberate (a district tint per style group), so that one
is a real decision rather than an oversight. The Infra/Street pairs are not — they are the same
material authored twice under two names. Merging just those four pairs removes 4 slots and, more
usefully, stops two props standing side by side with visibly different "galvanised".

**2. Whoever merges has to say so, because the names are baked into exported FBX slot order.**
If a name is dropped, the set that used it must be re-exported or the slot goes to
`M_Debug_Missing`. Cheapest order: decide the merges first, tell the three Blender sessions, we
re-run our builds once (minutes each), then you import.

**3. Collision material.** My `COL_`/`UCX_`/`UBX_` meshes carry a `VC_Collision` material; the
street set's carry none. Neither matters — `ChunkImportTests` skips collision nodes before the
material check and the postprocessor strips the renderer — so `VC_Collision` does **not** need a
library slot. Flagged only so it is not counted or wondered about later.


---

## Decisions (Unity session, 2026-11-14)

**Merged — canonical name wins, the other is dropped.** Chosen so the name states the actual
material rather than a generic "Metal":

| Canonical | Dropped | Owed by |
|---|---|---|
| `VC_Steel_Galv` | `VC_Metal_Galv` | street furniture |
| `VC_Steel_Dark` | `VC_Metal_Dark` | street furniture |
| `VC_Iron_Cast` | `VC_Metal_CastIron` | street furniture (cast iron is iron, not steel, and it matters for how it reads) |
| `VC_Sign_BlankFace` | `VC_SignPanel_Blank` | **Infra — done**, `vc_infra_lib.MATERIALS` and `props_overhead.PANEL` |

Infra's rename is applied in code and lands on its next build. The prop FILE names
`VC_SignPanel_Blank_4800x2400` and `_2400x1500` are unchanged — they are prop names, not material
names, and renaming the FBX would churn references for no gain.

**Kept: the eleven district concretes and ten glasses.** Deliberate style-group identity, not an
oversight. Each gets a real library entry sharing a template and differing by tint. To be revisited
only as a measured performance decision, not as tidying up.

**`CASTING_SURFACE` stays `concrete`** behind the named constant, with the affected nodes listed in
the RoadWear MANIFEST. Intent stays recoverable without putting warning noise in an import log that
is being read for real problems.

**Kerb collider split by surface: approved**, and the Unity session will specifically check the
butt seam on import, since CLAUDE.md allows no vertical step over 0.05 m between adjacent collider
surfaces and a wheel crossing that seam will find any error there.

**Net slot count after the merges: 105.**

## Outstanding, and blocked on session availability

The street-furniture and vegetation sessions both **stopped** at ~20:54 on 2026-11-13 with their
builds scripted but never run (`ArtSource/Blender/Facade/build/`, `ArtSource/Blender/Terrain/scripts/`
— complete, syntax-checked, zero Blender invocations). They are no longer reachable by message.

So three re-exports are owed and only one of them can currently be done:

| Set | Owes | Can it be done now? |
|---|---|---|
| Infra | `VC_Sign_BlankFace` rename | yes — code applied, runs on the next build |
| Street furniture | three renames above | **no** — session stopped |
| Facade, Terrain | first build, never run | **no** — sessions stopped |


---

## FINAL material definitions (Unity session, 2026-11-14) — four slots, not three

The cast-iron pair was split rather than merged: a painted lamp standard and a bare iron manhole
cover are not one material, and merging them would have been a worse error than the name
duplication, invisible until a wet night scene put a reflection on both.

| Slot | Metallic | Base / albedo | Roughness | Use |
|---|---|---|---|---|
| `VC_Steel_Galv` | 1.0 | 0.560, 0.570, 0.580 | 0.44 | bare galvanised steel; F0 in band, blue-grey bias correct for spelter |
| `VC_Steel_Dark` | **0.0** | 0.060, 0.062, 0.065 | 0.45 | **PAINTED steel only.** Bare dark steel must not use this slot |
| `VC_Iron_Cast` | 1.0 | 0.440, 0.430, 0.420 | 0.72 | **BARE** cast iron: manhole covers, utility covers, storm grates |
| `VC_Iron_Painted` | 0.0 | 0.040, 0.040, 0.042 | 0.45 | **PAINTED** cast iron: ornate lamp standards, bollards, railings |

Author ROUGHNESS in manifests and material tables; the Unity side converts to smoothness in the
metallic alpha at import. Do not quote post-conversion numbers in a manifest — that documents the
import pipeline rather than the asset.

Net slot count: **106**.

### Infra + RoadWear: DONE (code applied, lands on next build)

Eight uses were reclassified by the painted/bare condition, not just renamed:
- to `VC_Steel_Galv`: guardrail splice bolts (both ends), guardrail end impact head (both branches),
  lattice leg base plate, lattice leg top plate, lattice leg anchor bolts, concrete leg bearing plate
- to `VC_Iron_Cast`: manhole lift slots (patterns A and B), square utility cover chequer plate, its studs
- kept on `VC_Steel_Dark` (genuinely painted): signal mast upright, base flange, bolt skirt, arm
  segments, elbow casting; signal head housing, backplate, bracket, visors, hood lips

RoadWear now uses no painted steel at all, which is correct: concrete and bare castings only.

### Street furniture: OWED — exact change list

**WARNING: do not apply the earlier merge table mechanically.** It said
`VC_Metal_CastIron -> VC_Iron_Cast`, but that table predates the four-slot split. Street's
`VC_Metal_CastIron` is used for **ornate lamp standards**, which are PAINTED, so its target is
**`VC_Iron_Painted`**, not `VC_Iron_Cast`. Sending it to bare metallic-1.0 iron would reintroduce
exactly the error the split was created to prevent.

| File | Line | Change |
|---|---|---|
| `Street/build/vc_street_lib.py` | 21 | `VC_Metal_Galv` -> `VC_Steel_Galv`, values -> 0.560/0.570/0.580, m 1.0, r 0.44 |
| | 22 | `VC_Metal_Dark` -> `VC_Steel_Dark`, values -> 0.060/0.062/0.065, **m 0.0**, r 0.45 |
| | 23 | `VC_Metal_CastIron` -> **`VC_Iron_Painted`**, values -> 0.040/0.040/0.042, **m 0.0**, r 0.45 |
| `Street/build/build_street.py` | 244, 259 | `VC_Metal_CastIron` -> `VC_Iron_Painted` (ornate lamp standards) |
| | 274, 285 | `VC_Metal_Dark` -> `VC_Steel_Dark` (sign posts and brackets — painted, correct slot) |
| `Street/build/make_manifest.py` | 25, 26, 27 | the three description-table keys |

Street already uses `VC_Sign_BlankFace`, so no change there. Its `VC_Paint_Slate` bollards stay as
they are. After the edit the set must be re-exported before import, since names are baked into FBX
slot order.


---

## STANDING RULE: per-set naming schemes are allowed to coexist

Decided twice on 2026-11-14 (district concretes, then `VC_Metal_Garage`) and recorded here so the
next person who notices a mismatched name does not reopen it.

**Consistency within a set beats consistency across sets.** A name that looks wrong is only wrong
if it breaks the scheme of the set it belongs to. Churning models to make one name rhyme with a
different set's convention is cost with no benefit, and twice tonight it would have made the game
visibly worse.

The three live schemes, all legitimate:

| Scheme | Owner | Example | Why |
|---|---|---|---|
| `VC_<Category>_<Variant>` | the D5 suburban house set, 11 names | `VC_Wall_Stucco`, `VC_Roof_Tile`, `VC_Door_Wood`, `VC_Metal_Garage` | those 11 materials are shared across 8 house designs that are **not** district-specific: the same stucco and the same roof tile wherever a D5 house lands |
| `VC_<District>_<Surface>` | the other 12 building sets, 57 names | `VC_Twr_Glass`, `VC_Nm_Brick`, `VC_Mt_Render` | a tower's glass and a market-town shop's glass are **genuinely different materials**, tuned per district |
| `VC_<Material>_<Qualifier>` | infra, road wear, street furniture | `VC_Steel_Galv`, `VC_Iron_Cast`, `VC_Concrete_Kerb` | physical material first, because these props appear in every district and must read identically wherever they stand |
| `M_Veyra_<Thing>` | vegetation, 5 names | `M_Veyra_Bark`, `M_Veyra_Rock` | its own prefix; shared atlases across both vegetation sets |

So two of the building kit's schemes coexist by design, not by accident, and that is not a tidy-up
waiting to happen.

### `VC_Metal_Garage` — examined and deliberately KEPT

Raised because it is the only `VC_Metal_*` name left after street furniture moved to
`VC_Steel_*` / `VC_Iron_*`. Keep it. Three reasons, strongest first:

1. **It is one of eleven members of the suburban category scheme, not an outlier.** Renaming only
   it would *make* it the outlier. It was being compared against a different set's convention.
2. **Absorbing it into `VC_Steel_Dark` would be a visible regression, not a tidy-up:**

       VC_Metal_Garage   0.400 / 0.405 / 0.405   metallic 0.6   roughness 0.45
       VC_Steel_Dark     0.060 / 0.062 / 0.065   metallic 0.0   roughness 0.45

   A 6.6x darkening **and** the loss of metallic response. A roller shutter is brushed metal that
   catches sky; on `VC_Steel_Dark` it becomes a flat near-black panel. Affects
   `VC_House_GarageFront`, `VC_House_GarageCommunity`, `VC_House_GarageSmall`.
3. Renaming costs a re-export of 88 models — the weakest reason, since that is a parametric re-run
   of a few minutes. Lead with 1 and 2.

### Count update

Vegetation contributes **five** names, not three: `M_Veyra_Foliage`, `M_Veyra_Foliage_B`,
`M_Veyra_Bark`, plus `M_Veyra_Rock` and `M_Veyra_Impostor` pending its bake. **Total 108.**

The vegetation session's standing question, not yet answered: 108 materials against a 10 ms CPU
budget with BUG-170 open deserves a measured second look rather than just a register entry. The
Unity session has said it will revisit the per-district split as a performance decision with
numbers, not as tidying-up, so this is live rather than closed.

## Packed texture channels — settled in both directions

Stated here because getting it wrong inverts every surface in the project:

- **Packed `_MS` alpha carries SMOOTHNESS** (= 1 - roughness), per CLAUDE.md, from **all three**
  Blender sessions. The maps are import-ready. **No conversion is applied at import.**
- **Manifests quote ROUGHNESS** in prose, because that is the authoring value; a manifest quoting
  the post-conversion number would document the import pipeline rather than the asset.
- These live at different layers and both are correct simultaneously. Vegetation additionally
  states the complement explicitly in its manifest prose, which is good practice worth copying.

---

## Terrain kit — CONFIRMED (vegetation session, built and verified)

`M_Veyra_Rock` and `M_Veyra_Impostor` are no longer pending. Both exist, both are in built and
verified FBX files, and the register's count of 108 stands.

| Material | Textures | Mode | Used by |
|---|---|---|---|
| `M_Veyra_Rock` | `T_Veyra_Rock_{BaseColor,Normal,MetallicSmoothness}.png`, 1024 | opaque, **tiling** (not an atlas) | all 13 rock assets |
| `M_Veyra_Impostor` | `T_Veyra_Impostor_Atlas_{BaseColor,Normal,MetallicSmoothness}.png`, 2048 | **alpha clip 0.5**, Render Face Both | all 6 far-tree impostors |

The fallen logs reuse `M_Veyra_Bark` rather than adding a name, so the whole natural world —
three kits, 52 assets — is **five** materials.

`M_Veyra_Rock` is a TILING set, not an atlas: rock UVs are box-projected per face and tile freely.
That buys 410 px/m at a 2.5 m repeat, which an atlas rect could not approach, and still costs one
material. Worth copying wherever an asset class is surface-driven rather than silhouette-driven.

Every terrain asset carries exactly **one** material; `verify_terrain.py` fails any that does not.

## Packed channels — terrain conforms

Both new sets pack smoothness in `_MS` alpha, import-ready, no conversion wanted. The terrain
manifest quotes roughness in prose (rock 0.55-0.98, impostors 0.78) and states the complement
explicitly, as agreed.

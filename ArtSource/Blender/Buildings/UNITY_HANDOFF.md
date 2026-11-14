# Veyra City building kit - Unity hand-off

Everything needed to wire the kit into the existing pipeline (`BuildingAssembler` +
`KitbashCatalogBuilder` + `KitbashMaterials`). No new systems; nothing about how buildings work changes.

Built 2026-11-14 from `ArtSource/Blender/Buildings/build/*.py` (re-runnable). **88 models, 13 catalogue sources, 88,160 triangles at LOD0, 210 textures.**
All geometry and all textures are original to this project.

---

## 1. The placement contract

This is the logic the models were sized against. I simulated it over every record before authoring
anything, so the coverage numbers in section 4 are predictions from this exact rule - if the rule in
the C# differs from what is written here, the coverage numbers are wrong and I would want to know.

Read from `Assets/_Veyra/Editor/BuildingAssembler.cs` (`Run`, the candidate loop) and
`KitbashCatalogBuilder.cs` (`Build`). For each record `b` and each catalogue entry `e`:

```
// 1. style and district gates
if (!StyleMatch(e, b.StyleKit)) continue;          // kit.StartsWith("GlassModern")
                                                   //   -> e.Styles has GlassModern OR ArtDeco
                                                   // otherwise -> e.Styles.Contains(kit)
if (!e.Districts.Contains(b.DistrictId)) continue;

// 2. both orientations; t == 1 means the model is turned 90 deg
for (int t = 0; t < 2; t++) {
    float kw = t == 0 ? e.Footprint.x : e.Footprint.y;
    float kl = t == 0 ? e.Footprint.y : e.Footprint.x;
    float s  = Mathf.Clamp(Mathf.Sqrt(b.FootprintWM * b.FootprintLM /
                                      Mathf.Max(1f, kw * kl)), 0.85f, 1.2f);
    float fw = kw * s / b.FootprintWM;
    float fl = kl * s / b.FootprintLM;
    float fh = e.Height * s / Mathf.Max(1f, b.HeightM);
    if (fw < 0.78f || fw > 1.22f || fl < 0.78f || fl > 1.22f ||
        fh < 0.65f || fh > 1.5f) continue;         // no fit in this orientation
    candidates.Add((e, s, turned: t == 1));
}

// 3. seeded pick, never one of the last two models used in the same chunk
// 4. yaw = b.UnityYawDeg + (turned ? 90 : 0); position = (b.UnityX, ground, b.UnityZ)
//    where ground = IsNaN(b.GroundMinM) ? b.UnityYBase : b.GroundMinM
// 5. localScale *= s, then FitHeight stretches the up axis so the top lands on
//    b.UnityYBase + b.HeightM
```

`e.Footprint`, `e.Height` and `e.Triangles` come straight from the `kitbash_manifest.json` fields
`footprint_m`, `height_m` and `triangles`. **The footprint I publish is the building's wall footprint,
not the model's bounding box** - eaves, balconies and roof overhangs stick out past it on purpose, so
the fit test compares like with like and the scale lands on 1.0 instead of shrinking the model to
swallow its own overhang.

### Entry gates in `KitbashCatalogBuilder.Build` (an entry that trips one is never placed)

```
type != "Building"                         -> excluded "prop piece"
Height < 6  ||  min(Footprint) < 6         -> excluded "fragment (under 6 m)"
Height > 350 || max(Footprint) > 120       -> excluded "oversize"
Triangles[0] > 60000                       -> excluded "LOD0 over the 60k per-building budget"
Prefab == null                             -> excluded "FBX not imported"
pack.exclude != null                       -> excluded with that note
```

Every model in this kit was audited against those gates (`build/` audit run). Two still trip one -
see section 5. Largest LOD0 in the kit is 2464 triangles, so the 60k gate is never close.

---

## 2. Copy the files

```
# D5 suburban houses  (10 models)
export/VC_House_*.fbx
    -> Assets/_Veyra/Art/Models/Kitbash/Veyra_Original_Suburban/
kitbash_manifest_Veyra_Original_Suburban.json
    -> Assets/_Veyra/Art/Models/Kitbash/Veyra_Original_Suburban/kitbash_manifest.json
textures/{VC_Wall_,VC_Roof_,VC_Trim_,VC_Glass_Window,VC_Door_,VC_Metal_Garage}*.png
    -> Assets/_Veyra/Art/Textures/Imported/Veyra_Original_Suburban/

# D1 Crown Downtown towers  (21 models)
export/VC_Tower_*.fbx
    -> Assets/_Veyra/Art/Models/Kitbash/Veyra_Original_Downtown/
kitbash_manifest_Veyra_Original_Downtown.json
    -> Assets/_Veyra/Art/Models/Kitbash/Veyra_Original_Downtown/kitbash_manifest.json
textures/{VC_Twr_}*.png
    -> Assets/_Veyra/Art/Textures/Imported/Veyra_Original_Downtown/

# D6 Neon Mile low-rise  (9 models)
export/VC_Neon_*.fbx
    -> Assets/_Veyra/Art/Models/Kitbash/Veyra_Original_NeonMile/
kitbash_manifest_Veyra_Original_NeonMile.json
    -> Assets/_Veyra/Art/Models/Kitbash/Veyra_Original_NeonMile/kitbash_manifest.json
textures/{VC_Nm_,VC_Neon_Emissive}*.png
    -> Assets/_Veyra/Art/Textures/Imported/Veyra_Original_NeonMile/

# D2 Azure Coast stucco  (8 models)
export/VC_Coast_*.fbx
    -> Assets/_Veyra/Art/Models/Kitbash/Veyra_Original_Coast/
kitbash_manifest_Veyra_Original_Coast.json
    -> Assets/_Veyra/Art/Models/Kitbash/Veyra_Original_Coast/kitbash_manifest.json
textures/{VC_Cs_}*.png
    -> Assets/_Veyra/Art/Textures/Imported/Veyra_Original_Coast/

# D8 Redwood Valley market town  (5 models)
export/VC_Town_*.fbx
    -> Assets/_Veyra/Art/Models/Kitbash/Veyra_Original_MarketTown/
kitbash_manifest_Veyra_Original_MarketTown.json
    -> Assets/_Veyra/Art/Models/Kitbash/Veyra_Original_MarketTown/kitbash_manifest.json
textures/{VC_Mt_}*.png
    -> Assets/_Veyra/Art/Textures/Imported/Veyra_Original_MarketTown/

# ResidentialMidrise (D1 + D5)  (7 models)
export/VC_Mid_*.fbx
    -> Assets/_Veyra/Art/Models/Kitbash/Veyra_Original_Midrise/
kitbash_manifest_Veyra_Original_Midrise.json
    -> Assets/_Veyra/Art/Models/Kitbash/Veyra_Original_Midrise/kitbash_manifest.json
textures/{VC_Mr_}*.png
    -> Assets/_Veyra/Art/Textures/Imported/Veyra_Original_Midrise/

# D3 Ironport sheds  (6 models)
export/VC_Ind_Shed*.fbx
    -> Assets/_Veyra/Art/Models/Kitbash/Veyra_Original_Ironport/
kitbash_manifest_Veyra_Original_Ironport.json
    -> Assets/_Veyra/Art/Models/Kitbash/Veyra_Original_Ironport/kitbash_manifest.json
textures/{VC_In_}*.png
    -> Assets/_Veyra/Art/Textures/Imported/Veyra_Original_Ironport/

# D3 Ironport tanks and process columns  (3 models)
export/VC_Ind_Tank*.fbx + VC_Ind_Process*.fbx
    -> Assets/_Veyra/Art/Models/Kitbash/Veyra_Original_IronportTanks/
kitbash_manifest_Veyra_Original_IronportTanks.json
    -> Assets/_Veyra/Art/Models/Kitbash/Veyra_Original_IronportTanks/kitbash_manifest.json
textures/{VC_In_}*.png
    -> Assets/_Veyra/Art/Textures/Imported/Veyra_Original_IronportTanks/

# D4 Blackridge alpine  (4 models)
export/VC_Alp_*.fbx
    -> Assets/_Veyra/Art/Models/Kitbash/Veyra_Original_Alpine/
kitbash_manifest_Veyra_Original_Alpine.json
    -> Assets/_Veyra/Art/Models/Kitbash/Veyra_Original_Alpine/kitbash_manifest.json
textures/{VC_Al_}*.png
    -> Assets/_Veyra/Art/Textures/Imported/Veyra_Original_Alpine/

# D8 Redwood Valley rural  (7 models)
export/VC_Rur_*.fbx
    -> Assets/_Veyra/Art/Models/Kitbash/Veyra_Original_Rural/
kitbash_manifest_Veyra_Original_Rural.json
    -> Assets/_Veyra/Art/Models/Kitbash/Veyra_Original_Rural/kitbash_manifest.json
textures/{VC_Ru_}*.png
    -> Assets/_Veyra/Art/Textures/Imported/Veyra_Original_Rural/

# D7 Falcon Airfield  (4 models)
export/VC_Avi_*.fbx
    -> Assets/_Veyra/Art/Models/Kitbash/Veyra_Original_Aviation/
kitbash_manifest_Veyra_Original_Aviation.json
    -> Assets/_Veyra/Art/Models/Kitbash/Veyra_Original_Aviation/kitbash_manifest.json
textures/{VC_Av_}*.png
    -> Assets/_Veyra/Art/Textures/Imported/Veyra_Original_Aviation/

# D5 suburban retail  (3 models)
export/VC_Ret_Retail*.fbx
    -> Assets/_Veyra/Art/Models/Kitbash/Veyra_Original_Retail/
kitbash_manifest_Veyra_Original_Retail.json
    -> Assets/_Veyra/Art/Models/Kitbash/Veyra_Original_Retail/kitbash_manifest.json
textures/{VC_Rt_}*.png
    -> Assets/_Veyra/Art/Textures/Imported/Veyra_Original_Retail/

# D4 roadside  (1 models)
export/VC_Ret_GasStation.fbx
    -> Assets/_Veyra/Art/Models/Kitbash/Veyra_Original_Roadside/
kitbash_manifest_Veyra_Original_Roadside.json
    -> Assets/_Veyra/Art/Models/Kitbash/Veyra_Original_Roadside/kitbash_manifest.json
textures/{VC_Rt_}*.png
    -> Assets/_Veyra/Art/Textures/Imported/Veyra_Original_Roadside/

```

Paths are relative to `ArtSource/Blender/Buildings/`. Each `kitbash_manifest_*.json` already points at
`Assets/_Veyra/Art/Models/Kitbash/<source>/<name>.fbx`; rename it to `kitbash_manifest.json` inside its
folder. Import the FBX with `VeyraModelImportDefaults` (Scale Factor 1, Convert Units on, Bake Axis
Conversion on, Read/Write off).

Note the texture prefixes repeat across sources that share a material family (`VC_In_` for both
Ironport sources, `VC_Rt_` for retail and roadside). `KitbashMaterials` looks under
`Art/Textures/Imported/<source>/`, so those files are copied into **both** folders.

## 3. Register the sources in `KitbashCatalogBuilder.Packs`

Without these the entries get no styles and no districts, `StyleMatch` fails and nothing is placed.

```csharp
["Veyra_Original_Suburban"] = ("VC-ORIGINAL", new[] { "Suburban" }, new[] { "Suburban" }, new[] { "D5" }, null),
["Veyra_Original_Downtown"] = ("VC-ORIGINAL", new[] { "GlassModern", "ArtDeco" }, new[] { "GlassModern", "ArtDeco" }, new[] { "D1" }, null),
["Veyra_Original_NeonMile"] = ("VC-ORIGINAL", new[] { "BrickIndustrial" }, new[] { "ModernLowrise" }, new[] { "D6" }, null),
["Veyra_Original_Coast"] = ("VC-ORIGINAL", new[] { "CoastalStucco" }, new[] { "CoastalStucco" }, new[] { "D2" }, null),
["Veyra_Original_MarketTown"] = ("VC-ORIGINAL", new[] { "MarketTownBrick" }, new[] { "MarketTownBrick" }, new[] { "D8" }, null),
["Veyra_Original_Midrise"] = ("VC-ORIGINAL", new[] { "ResidentialMidrise" }, new[] { "ResidentialMidrise" }, new[] { "D1", "D5" }, null),
["Veyra_Original_Ironport"] = ("VC-ORIGINAL", new[] { "Industrial" }, new[] { "Industrial" }, new[] { "D3", "D7" }, null),
["Veyra_Original_IronportTanks"] = ("VC-ORIGINAL", new[] { "IndustrialRefinery" }, new[] { "IndustrialTank" }, new[] { "D3" }, null),
["Veyra_Original_Alpine"] = ("VC-ORIGINAL", new[] { "Alpine" }, new[] { "Alpine" }, new[] { "D4" }, null),
["Veyra_Original_Rural"] = ("VC-ORIGINAL", new[] { "Rural" }, new[] { "Rural" }, new[] { "D8" }, null),
["Veyra_Original_Aviation"] = ("VC-ORIGINAL", new[] { "Aviation" }, new[] { "Aviation" }, new[] { "D7" }, null),
["Veyra_Original_Retail"] = ("VC-ORIGINAL", new[] { "SuburbanRetail" }, new[] { "SuburbanRetail" }, new[] { "D5" }, null),
["Veyra_Original_Roadside"] = ("VC-ORIGINAL", new[] { "Roadside" }, new[] { "Roadside" }, new[] { "D4" }, null),
```

The third and fourth entries are the `tall` and `low` style lists, chosen by
`Height >= 30f ? tall : low`. Only `Veyra_Original_NeonMile` and `Veyra_Original_IronportTanks` rely on
that split (brick blocks 30-62 m vs low-rise 16-21 m; process columns 52 m vs tanks 12-19 m). Every
other source lists the same styles in both slots, so the 30 m boundary cannot matter.

`"VC-ORIGINAL"` is a licence id for content made inside this project. It needs a row in
`Docs/THIRD_PARTY_LICENSE_REGISTER.csv` saying exactly that, or the entries read as `UNREGISTERED`.

## 4. Run it

`Veyra/Buildings/Build Kitbash Catalog`, then `Veyra/Buildings/Assemble Buildings`.

Expected, from simulating section 1 against the real records:

| Source | Records it targets | Fit | Height stretch med / p90 / worst |
|---|---|---|---|
| `Veyra_Original_Suburban` | 1923 | **1923** | 7 % / 14 % / 30 % |
| `Veyra_Original_Downtown` | 232 | **231** | 10 % / 17 % / 41 % |
| `Veyra_Original_NeonMile` | 116 | **114** | 8 % / 17 % / 27 % |
| `Veyra_Original_Coast` | 81 | **81** | 5 % / 26 % / 26 % |
| `Veyra_Original_MarketTown` | 65 | **65** | 3 % / 6 % / 10 % |
| `Veyra_Original_Midrise` | 87 | **87** | 7 % / 18 % / 23 % |
| `Veyra_Original_Ironport` | 41 | **41** | 6 % / 18 % / 26 % |
| `Veyra_Original_IronportTanks` | 83 | **83** | 10 % / 19 % / 29 % |
| `Veyra_Original_Alpine` | 25 | **25** | 6 % / 11 % / 11 % |
| `Veyra_Original_Rural` | 25 | **25** | 3 % / 4 % / 4 % |
| `Veyra_Original_Aviation` | 8 | **8** | 4 % / 15 % / 15 % |
| `Veyra_Original_Retail` | 3 | **3** | 13 % / 19 % / 19 % |
| `Veyra_Original_Roadside` | 1 | **1** | 10 % / 10 % / 10 % |
| **total** | **2690** | **2687** | |

`Logs/C/assembler.txt` should therefore show about **2687 more kit models placed** and the various
`"<district> <style>: no kit fits"` lines should drop to the handful in section 5. The height stretch
is what `FitHeight` applies after the uniform scale; it is the number worth watching in review, because
it is the one thing that makes a model not look as authored.

## 5. Records that will still not be placed, and why

### 5a. Three data anomalies (logged as BUG-171)

| Record | Kind | Footprint | Height | Why |
|---|---|---|---|---|
| `BLD-00181` | office | 52.0 x 168.3 m | 117.1 m | data outlier: 168 m long, far beyond the district's p95 of 118 m |
| `BLD-00388` | midrise | 56.9 x 222.4 m | 60.9 m | data outlier: 222 m long, far beyond the district's p95 of 123 m |
| `BLD-00403` | lowrise | 54.5 x 223.8 m | 14.4 m | data outlier: 224 m long, far beyond the district's p95 of 123 m |

These are single buildings 168-224 m long in districts whose 95th percentile is 118-123 m. No model
covers them without being wrong for everything else. `BLD-00403` is 223.8 x 54.5 m at **14.4 m** tall -
a 224 m single-storey shed, which reads as a data error rather than intent. They keep their procedural
shells. If they are deliberate, say so and I will author them as one-offs.

### 5b. Two models the catalogue's own `oversize` gate will drop

| Model | Footprint | Problem |
|---|---|---|
| `VC_Avi_Terminal` | 130 x 36 m | `max(Footprint) > 120` -> excluded as "oversize" |
| `VC_Avi_Grandstand` | 18 x 150 m | `max(Footprint) > 120` -> excluded as "oversize" |

**This one needs your decision, not more modelling.** The two D7 records are themselves 130 x 36 and
18 x 150, so *no* model can serve them while the gate is 120 m - anything that matches the footprint is
oversize by definition. The models are built, correct and exported; they simply will not enter the
catalogue until the limit is raised (160 m would cover both) or those two sources are special-cased.
Until then those 2 records keep their shells, and the Aviation line in section 4 reads 8/8 in the fit
simulation but will place 6.

Three more models sit exactly on that boundary and pass only because the test is a strict `>`:
`VC_Ind_ShedRail`, `VC_Ind_ShedRailLow` and `VC_Ret_RetailBigbox` are all 120 m on the long side. If
anyone ever changes that line to `>=`, those three drop out silently.

### 5c. Landmarks

The 11 `Landmark` records are skipped by `BuildingAssembler` itself ("landmark (A4 hero pending)")
and no set targets them.

## 6. What I read in the C# versus what I inferred

You asked me to flag this. Everything in section 1 is read from the source, not guessed:

| Behaviour | Status | Where |
|---|---|---|
| Fit rule: +-22 %% footprint, scale clamp 0.85-1.2, height 0.65-1.5, both orientations | **read** | `BuildingAssembler.Run`, candidate loop |
| `StyleMatch` (GlassModern also accepts ArtDeco) | **read** | `BuildingAssembler.StyleMatch` |
| Seeded pick, no repeat within the last two in a chunk | **read** | `BuildingAssembler.Run` |
| Styles chosen by `Height >= 30f ? tall : low`; licence, districts from `Packs` | **read** | `KitbashCatalogBuilder.Build` |
| Exclusion gates (fragment / oversize / 60k / prop piece) | **read** | `KitbashCatalogBuilder.Build` |
| `FitHeight` scales the ROOT localScale on its up axis, measured over **enabled** renderers, and does nothing when the model is under 2 m or the stretch is under 1 %% | **read** | `BuildingAssembler.FitHeight` |
| `HideUnsupported` hides a renderer whose bounds sit >1.5 m above ground with nothing overlapping below it | **read** | `BuildingAssembler.HideUnsupported` |
| Renderers named `UBX_` / `UCX_` / `COL_` are disabled; all kit colliders are disabled | **read** | `BuildingAssembler.Run` |
| `KitbashMaterials.Apply` matches a slot to a manifest `materials[].source` by case-insensitive substring of the FBX material name | **read** | `KitbashMaterials.Apply` |
| Plinth is drawn at the record footprint x 2.3 m, from 0.85 m below ground | **read** | `BuildingAssembler.Run` |
| Unity builds ONE LODGroup per FBX from `*_LOD<n>` names | **not read by me** - taken from `CLAUDE.md` ("Blender -> Unity"), which states it as a measured project fact | `CLAUDE.md` |
| Whether LOD1/LOD2 renderers are `enabled` at assemble time, which would change what `FitHeight` and `HideUnsupported` measure | **UNVERIFIED** | see below |
| `e.FloorHeight` (set to 4.0 for GlassModern, else 3.4) | read, but I never relied on it - I do not know what consumes it | `KitbashCatalogBuilder.Build` |

**The one thing worth checking.** `FitHeight` and `HideUnsupported` both iterate
`GetComponentsInChildren<Renderer>(true).Where(r => r.enabled)`. Each of my FBX files contains three
sibling LOD meshes, so if the LODGroup leaves LOD1 and LOD2 renderers enabled at edit time, those two
functions measure all three. For this kit that is harmless - every LOD keeps the same overall height and
the same ground contact, by design, so the measured top and the support test come out identical either
way. I am flagging it because it would NOT be harmless for a kit whose LOD2 is shorter than its LOD0,
and because I did not verify it in the editor.

Two further things I could not check because I never ran Unity: that the importer actually produces the
LODGroup for these particular files, and that `KitbashMaterials` finds the textures once copied. Both
are mechanical, but neither is confirmed.

## 6b. `ChunkImportTests` gates this kit has to pass

`Assets/_Veyra/Tests/EditMode/ChunkImportTests.cs` scans
`Art/World/Chunks` and **`Art/Models/Kitbash`** - which is where this kit lands, so it is already in
scope the moment the FBX are copied. Read from the test, not assumed:

| Gate | This kit |
|---|---|
| a `COL_` node needs a non-convex MeshCollider **and** a `VehicleSurface` with a `SurfaceDefinition` | **not applicable** - all 88 models use `UBX_` only, no `COL_`, no `UCX_` |
| a `UCX_` node needs a convex MeshCollider | not applicable |
| a `UBX_` node needs a `BoxCollider` | **applies to every model** - depends on `CollisionPrefixPostprocessor` adding it on import |
| a collision node must have **no Renderer** | **applies** - the FBX carry `UBX_` as mesh objects, so the postprocessor has to strip the renderer |
| every material slot must resolve to a material **registered in the VeyraMaterialLibrary** (present is not enough) | **applies to all 68 distinct material names below** |
| fewer than 5 % of slots may fall back to `M_Debug_Missing` | follows from the above |
| with **3 or more** LOD levels, LOD0 `screenRelativeTransitionHeight` must be exactly **0.35** | **applies to all models** - every one ships 3 LODs |

**The material gate is the one that will fail first.** The export strips textures and carries only
slot names, so unless the library has a slot for each of these, every renderer in the kit errors:

```
   VC_Al_Concrete          VC_Al_Glass             VC_Al_Roof              VC_Al_Stone
   VC_Al_Timber            VC_Al_Trim              VC_Av_Clad              VC_Av_Concrete
   VC_Av_Glass             VC_Av_Metal             VC_Av_Roof              VC_Cs_Concrete
   VC_Cs_Glass             VC_Cs_RoofTile          VC_Cs_Stucco            VC_Cs_Trim
   VC_Cs_Wood              VC_Door_Wood            VC_Glass_Window         VC_In_Clad
   VC_In_Concrete          VC_In_Glass             VC_In_Metal             VC_In_Roof
   VC_In_TankShell         VC_Metal_Garage         VC_Mr_Brick             VC_Mr_Concrete
   VC_Mr_Glass             VC_Mr_Metal             VC_Mr_Panel             VC_Mt_Brick
   VC_Mt_Concrete          VC_Mt_Glass             VC_Mt_Render            VC_Mt_Roof
   VC_Mt_Trim              VC_Neon_Emissive        VC_Nm_Brick             VC_Nm_Concrete
   VC_Nm_Glass             VC_Nm_Metal             VC_Nm_Panel             VC_Nm_Shutter
   VC_Roof_Shingle         VC_Roof_Tile            VC_Rt_Brick             VC_Rt_Concrete
   VC_Rt_Glass             VC_Rt_Metal             VC_Rt_Panel             VC_Ru_Board
   VC_Ru_Concrete          VC_Ru_Glass             VC_Ru_Metal             VC_Ru_Roof
   VC_Ru_Trim              VC_Trim_Paint           VC_Twr_Concrete         VC_Twr_Glass
   VC_Twr_Metal            VC_Twr_Spandrel         VC_Twr_Stone            VC_Wall_Board
   VC_Wall_Brick           VC_Wall_Concrete        VC_Wall_Stucco          VC_Wall_StuccoWarm
```

That is **68 names** from the building kit; the facade kit adds 7 more (`VC_Fac_*`). They can map to
existing library materials rather than needing new ones - several are deliberate duplicates of each
other across sets (every `*_Glass`, `*_Concrete`, `*_Metal`) - but each name needs a slot that
resolves, or the test reports it per renderer.

### What the 0.35 rule means for distance

With LOD0 pinned at 0.35 and a 60 deg vertical FOV, LOD0 holds to `size / 0.404` metres, where `size`
is the largest axis of the bounds. So: a 14 m house to **35 m**, a 55 m tower to **136 m**, a 137 m
tower to **339 m**, a 245 m tower to **606 m**. Two consequences worth knowing before merging facade
geometry into a building's LOD0:

- anything merged into a tall tower's LOD0 mesh is drawn out to several hundred metres, long past the
  distance where a 0.26 m window recess is a pixel. The triangles are paid for the whole way.
- if that matters, merge the reveals into a **separate chunked mesh with its own LODGroup** sized to
  the reveal band rather than into the building mesh: same draw-call saving, but it culls at a
  sensible distance instead of inheriting the building's bounds. That is a Unity-side call.

## 7. Checks worth making in the editor

- A D5 street: ten designs should appear along it, not one. Same for a D8 market street (five) and a
  D2 coastal road (eight).
- A downtown block: curtain wall, mullions and setbacks, not boxes.
- Neon Mile at night: `VC_Neon_Emissive` should be the only thing glowing, and every sign face is
  **blank** - Veyra's own artwork still has to be applied. `_EMISSION` is toggled at runtime, so
  CLAUDE.md's rule applies: that material must be in the build or in `VC_Variants.shadervariants`.
- Nothing on the pavement: ground-level projections are inside 0.75 m of the footprint by construction,
  but the data footprint is the plot, so one screenshot is worth it.
- If a wall renders flat beige, the textures did not land in `Art/Textures/Imported/<source>/`.

## 8. Facts about the models

- Export preset **VC_Unity_Prop**; **pivot at base centre, on the x/y origin, at z = 0**. Front faces
  **Unity +Z**, so `b.UnityYawDeg` turns it to the street.
- Root EMPTY + `<name>_LOD0/1/2` + one `UBX_<name>`. Kit colliders are disabled by the assembler -
  every record keeps its own data-footprint collider - so the UBX is there for other uses.
- **LODs are authored, not decimated.** A collapse decimate shreds box-assembled architecture: it eats
  the massing and leaves fins and slivers, and the LOD2s came out as see-through shells with no
  silhouette. Each LOD is the same building description re-run at a lower detail level
  (`vc_bld_lib.set_detail`). Do not replace them with decimates.
- The gate for that is a render, not a metric: `preview/lod*_backface_check_*.png` renders every LOD
  backface-culled against a red world, so a flipped or missing triangle shows as a hole. Both numeric
  inverted-face tests are unreliable on this geometry and disagree with themselves.
- `build/verify_fbx.py` re-imports all 88 FBX: 0 n-gons, 0 inverted faces at LOD0, UVs on every LOD,
  `_LOD0/1/2` plus a collision mesh in every file, every base at z = 0, every pivot on the origin.
- Facade relief is carried by full-height piers/mullions and per-floor bands, and tower floor lines live
  in the texture, so triangle count is independent of building height: a 240 m tower costs the same as a
  28 m one. That is deliberate, for BUG-170.

## 9. Every model

| Model | Source | Footprint | Height | LOD0 | LOD1 | LOD2 | Material slots |
|---|---|---|---|---|---|---|---|
| `VC_House_DormerCottage` | Suburban | 14.0 x 11.0 m | 7.27 m | 852 | 588 | 140 | `VC_Wall_StuccoWarm`, `VC_Roof_Tile`, `VC_Trim_Paint`, `VC_Glass_Window`, `VC_Door_Wood`, `VC_Wall_Concrete` |
| `VC_House_Duplex` | Suburban | 14.0 x 11.0 m | 7.30 m | 1096 | 568 | 100 | `VC_Wall_Brick`, `VC_Roof_Shingle`, `VC_Trim_Paint`, `VC_Glass_Window`, `VC_Door_Wood`, `VC_Wall_Concrete` |
| `VC_House_FlatParapet` | Suburban | 14.0 x 11.0 m | 7.14 m | 756 | 588 | 192 | `VC_Wall_Stucco`, `VC_Roof_Tile`, `VC_Trim_Paint`, `VC_Glass_Window`, `VC_Door_Wood`, `VC_Wall_Concrete` |
| `VC_House_GablePorch` | Suburban | 14.0 x 11.0 m | 7.27 m | 1184 | 704 | 140 | `VC_Wall_Stucco`, `VC_Roof_Tile`, `VC_Trim_Paint`, `VC_Glass_Window`, `VC_Door_Wood`, `VC_Wall_Concrete` |
| `VC_House_GarageCommunity` | Suburban | 40.0 x 22.0 m | 7.08 m | 960 | 528 | 432 | `VC_Wall_Board`, `VC_Roof_Shingle`, `VC_Trim_Paint`, `VC_Glass_Window`, `VC_Door_Wood`, `VC_Wall_Concrete`, `VC_Metal_Garage` |
| `VC_House_GarageFront` | Suburban | 14.0 x 11.0 m | 7.20 m | 904 | 568 | 184 | `VC_Wall_StuccoWarm`, `VC_Roof_Tile`, `VC_Trim_Paint`, `VC_Glass_Window`, `VC_Door_Wood`, `VC_Wall_Concrete`, `VC_Metal_Garage` |
| `VC_House_GarageSmall` | Suburban | 8.6 x 7.5 m | 6.10 m | 320 | 236 | 140 | `VC_Wall_Board`, `VC_Roof_Shingle`, `VC_Trim_Paint`, `VC_Glass_Window`, `VC_Door_Wood`, `VC_Wall_Concrete`, `VC_Metal_Garage` |
| `VC_House_HipBay` | Suburban | 14.0 x 11.0 m | 7.27 m | 1112 | 656 | 200 | `VC_Wall_Brick`, `VC_Roof_Shingle`, `VC_Trim_Paint`, `VC_Glass_Window`, `VC_Door_Wood`, `VC_Wall_Concrete` |
| `VC_House_RanchCarport` | Suburban | 14.0 x 11.0 m | 7.12 m | 616 | 424 | 176 | `VC_Wall_Board`, `VC_Roof_Shingle`, `VC_Trim_Paint`, `VC_Glass_Window`, `VC_Door_Wood`, `VC_Wall_Concrete` |
| `VC_House_SplitLevel` | Suburban | 14.0 x 11.0 m | 7.20 m | 1032 | 600 | 132 | `VC_Wall_Board`, `VC_Roof_Shingle`, `VC_Trim_Paint`, `VC_Glass_Window`, `VC_Door_Wood`, `VC_Wall_Concrete` |
| `VC_Tower_Block117100` | Downtown | 117.0 x 117.0 m | 101.20 m | 2304 | 1260 | 204 | `VC_Twr_Glass`, `VC_Twr_Spandrel`, `VC_Twr_Stone`, `VC_Twr_Concrete`, `VC_Twr_Metal` |
| `VC_Tower_Block11762` | Downtown | 117.0 x 117.0 m | 63.20 m | 2304 | 1260 | 204 | `VC_Twr_Glass`, `VC_Twr_Spandrel`, `VC_Twr_Stone`, `VC_Twr_Concrete`, `VC_Twr_Metal` |
| `VC_Tower_Deco120` | Downtown | 54.0 x 54.0 m | 124.70 m | 2464 | 1468 | 388 | `VC_Twr_Glass`, `VC_Twr_Spandrel`, `VC_Twr_Stone`, `VC_Twr_Concrete`, `VC_Twr_Metal` |
| `VC_Tower_Deco175` | Downtown | 54.0 x 54.0 m | 179.70 m | 2464 | 1468 | 388 | `VC_Twr_Glass`, `VC_Twr_Spandrel`, `VC_Twr_Stone`, `VC_Twr_Concrete`, `VC_Twr_Metal` |
| `VC_Tower_Deco240` | Downtown | 54.0 x 54.0 m | 244.70 m | 2464 | 1468 | 388 | `VC_Twr_Glass`, `VC_Twr_Spandrel`, `VC_Twr_Stone`, `VC_Twr_Concrete`, `VC_Twr_Metal` |
| `VC_Tower_Glass130` | Downtown | 55.0 x 56.0 m | 137.10 m | 1348 | 832 | 256 | `VC_Twr_Glass`, `VC_Twr_Spandrel`, `VC_Twr_Stone`, `VC_Twr_Concrete`, `VC_Twr_Metal` |
| `VC_Tower_Glass190` | Downtown | 55.0 x 56.0 m | 197.10 m | 1348 | 832 | 256 | `VC_Twr_Glass`, `VC_Twr_Spandrel`, `VC_Twr_Stone`, `VC_Twr_Concrete`, `VC_Twr_Metal` |
| `VC_Tower_Glass28` | Downtown | 55.0 x 56.0 m | 28.70 m | 1284 | 768 | 192 | `VC_Twr_Glass`, `VC_Twr_Spandrel`, `VC_Twr_Stone`, `VC_Twr_Concrete`, `VC_Twr_Metal` |
| `VC_Tower_Glass42` | Downtown | 55.0 x 56.0 m | 42.70 m | 1284 | 768 | 192 | `VC_Twr_Glass`, `VC_Twr_Spandrel`, `VC_Twr_Stone`, `VC_Twr_Concrete`, `VC_Twr_Metal` |
| `VC_Tower_Glass62` | Downtown | 55.0 x 56.0 m | 63.10 m | 1308 | 792 | 216 | `VC_Twr_Glass`, `VC_Twr_Spandrel`, `VC_Twr_Stone`, `VC_Twr_Concrete`, `VC_Twr_Metal` |
| `VC_Tower_Glass90` | Downtown | 55.0 x 56.0 m | 91.10 m | 1320 | 804 | 228 | `VC_Twr_Glass`, `VC_Twr_Spandrel`, `VC_Twr_Stone`, `VC_Twr_Concrete`, `VC_Twr_Metal` |
| `VC_Tower_Slab115110` | Downtown | 54.0 x 115.0 m | 111.20 m | 1752 | 996 | 204 | `VC_Twr_Glass`, `VC_Twr_Spandrel`, `VC_Twr_Stone`, `VC_Twr_Concrete`, `VC_Twr_Metal` |
| `VC_Tower_Slab115200` | Downtown | 54.0 x 115.0 m | 201.20 m | 1752 | 996 | 204 | `VC_Twr_Glass`, `VC_Twr_Spandrel`, `VC_Twr_Stone`, `VC_Twr_Concrete`, `VC_Twr_Metal` |
| `VC_Tower_Slab11550` | Downtown | 54.0 x 115.0 m | 51.20 m | 1752 | 996 | 204 | `VC_Twr_Glass`, `VC_Twr_Spandrel`, `VC_Twr_Stone`, `VC_Twr_Concrete`, `VC_Twr_Metal` |
| `VC_Tower_Slab80170` | Downtown | 54.0 x 80.0 m | 171.20 m | 1440 | 852 | 204 | `VC_Twr_Glass`, `VC_Twr_Spandrel`, `VC_Twr_Stone`, `VC_Twr_Concrete`, `VC_Twr_Metal` |
| `VC_Tower_Slab8070` | Downtown | 54.0 x 80.0 m | 71.20 m | 1440 | 852 | 204 | `VC_Twr_Glass`, `VC_Twr_Spandrel`, `VC_Twr_Stone`, `VC_Twr_Concrete`, `VC_Twr_Metal` |
| `VC_Tower_Thin31130` | Downtown | 31.0 x 60.0 m | 137.10 m | 1156 | 712 | 256 | `VC_Twr_Glass`, `VC_Twr_Spandrel`, `VC_Twr_Stone`, `VC_Twr_Concrete`, `VC_Twr_Metal` |
| `VC_Tower_Thin31150` | Downtown | 31.0 x 117.0 m | 157.10 m | 1684 | 1000 | 256 | `VC_Twr_Glass`, `VC_Twr_Spandrel`, `VC_Twr_Stone`, `VC_Twr_Concrete`, `VC_Twr_Metal` |
| `VC_Tower_Thin3134` | Downtown | 31.0 x 60.0 m | 34.70 m | 1092 | 648 | 192 | `VC_Twr_Glass`, `VC_Twr_Spandrel`, `VC_Twr_Stone`, `VC_Twr_Concrete`, `VC_Twr_Metal` |
| `VC_Tower_Thin3170` | Downtown | 31.0 x 60.0 m | 71.10 m | 1116 | 672 | 216 | `VC_Twr_Glass`, `VC_Twr_Spandrel`, `VC_Twr_Stone`, `VC_Twr_Concrete`, `VC_Twr_Metal` |
| `VC_Tower_Thin3180l` | Downtown | 31.0 x 117.0 m | 81.10 m | 1644 | 960 | 216 | `VC_Twr_Glass`, `VC_Twr_Spandrel`, `VC_Twr_Stone`, `VC_Twr_Concrete`, `VC_Twr_Metal` |
| `VC_Neon_Brick30` | NeonMile | 57.0 x 57.0 m | 30.00 m | 1224 | 804 | 372 | `VC_Nm_Brick`, `VC_Nm_Glass`, `VC_Nm_Metal`, `VC_Nm_Concrete`, `VC_Nm_Shutter`, `VC_Neon_Emissive` |
| `VC_Neon_Brick33l` | NeonMile | 56.0 x 105.0 m | 33.00 m | 1524 | 948 | 372 | `VC_Nm_Brick`, `VC_Nm_Glass`, `VC_Nm_Metal`, `VC_Nm_Concrete`, `VC_Nm_Shutter`, `VC_Neon_Emissive` |
| `VC_Neon_Brick42` | NeonMile | 57.0 x 57.0 m | 42.00 m | 1260 | 816 | 372 | `VC_Nm_Brick`, `VC_Nm_Glass`, `VC_Nm_Metal`, `VC_Nm_Concrete`, `VC_Nm_Shutter`, `VC_Neon_Emissive` |
| `VC_Neon_Brick62` | NeonMile | 57.0 x 57.0 m | 62.00 m | 1332 | 852 | 372 | `VC_Nm_Brick`, `VC_Nm_Glass`, `VC_Nm_Metal`, `VC_Nm_Concrete`, `VC_Nm_Shutter`, `VC_Neon_Emissive` |
| `VC_Neon_Brick62l` | NeonMile | 56.0 x 105.0 m | 62.00 m | 1620 | 996 | 372 | `VC_Nm_Brick`, `VC_Nm_Glass`, `VC_Nm_Metal`, `VC_Nm_Concrete`, `VC_Nm_Shutter`, `VC_Neon_Emissive` |
| `VC_Neon_Low14l` | NeonMile | 57.0 x 112.0 m | 14.00 m | 1260 | 852 | 408 | `VC_Nm_Panel`, `VC_Nm_Glass`, `VC_Nm_Metal`, `VC_Nm_Concrete`, `VC_Nm_Shutter`, `VC_Neon_Emissive` |
| `VC_Neon_Low16` | NeonMile | 58.0 x 58.0 m | 16.00 m | 1044 | 732 | 408 | `VC_Nm_Panel`, `VC_Nm_Glass`, `VC_Nm_Metal`, `VC_Nm_Concrete`, `VC_Nm_Shutter`, `VC_Neon_Emissive` |
| `VC_Neon_Low18l` | NeonMile | 57.0 x 112.0 m | 18.00 m | 1260 | 852 | 408 | `VC_Nm_Panel`, `VC_Nm_Glass`, `VC_Nm_Metal`, `VC_Nm_Concrete`, `VC_Nm_Shutter`, `VC_Neon_Emissive` |
| `VC_Neon_Low21` | NeonMile | 58.0 x 58.0 m | 21.00 m | 1056 | 732 | 408 | `VC_Nm_Panel`, `VC_Nm_Glass`, `VC_Nm_Metal`, `VC_Nm_Concrete`, `VC_Nm_Shutter`, `VC_Neon_Emissive` |
| `VC_Coast_ClubA` | Coast | 24.0 x 12.0 m | 6.80 m | 744 | 552 | 144 | `VC_Cs_Stucco`, `VC_Cs_RoofTile`, `VC_Cs_Trim`, `VC_Cs_Glass`, `VC_Cs_Wood`, `VC_Cs_Concrete` |
| `VC_Coast_ClubB` | Coast | 24.0 x 12.0 m | 10.29 m | 1296 | 876 | 216 | `VC_Cs_Stucco`, `VC_Cs_RoofTile`, `VC_Cs_Trim`, `VC_Cs_Glass`, `VC_Cs_Wood`, `VC_Cs_Concrete` |
| `VC_Coast_Marina` | Coast | 33.0 x 20.0 m | 7.60 m | 1016 | 716 | 152 | `VC_Cs_Stucco`, `VC_Cs_RoofTile`, `VC_Cs_Trim`, `VC_Cs_Glass`, `VC_Cs_Wood`, `VC_Cs_Concrete` |
| `VC_Coast_VillaA` | Coast | 20.0 x 15.0 m | 9.60 m | 1364 | 824 | 140 | `VC_Cs_Stucco`, `VC_Cs_RoofTile`, `VC_Cs_Trim`, `VC_Cs_Glass`, `VC_Cs_Wood`, `VC_Cs_Concrete` |
| `VC_Coast_VillaB` | Coast | 20.0 x 15.0 m | 9.69 m | 828 | 696 | 276 | `VC_Cs_Stucco`, `VC_Cs_RoofTile`, `VC_Cs_Trim`, `VC_Cs_Glass`, `VC_Cs_Wood`, `VC_Cs_Concrete` |
| `VC_Coast_VillaC` | Coast | 20.0 x 15.0 m | 9.62 m | 1196 | 740 | 200 | `VC_Cs_Stucco`, `VC_Cs_RoofTile`, `VC_Cs_Trim`, `VC_Cs_Glass`, `VC_Cs_Wood`, `VC_Cs_Concrete` |
| `VC_Coast_VillaD` | Coast | 20.0 x 15.0 m | 6.40 m | 788 | 488 | 128 | `VC_Cs_Stucco`, `VC_Cs_RoofTile`, `VC_Cs_Trim`, `VC_Cs_Glass`, `VC_Cs_Wood`, `VC_Cs_Concrete` |
| `VC_Coast_VillaE` | Coast | 20.0 x 15.0 m | 7.88 m | 908 | 608 | 200 | `VC_Cs_Stucco`, `VC_Cs_RoofTile`, `VC_Cs_Trim`, `VC_Cs_Glass`, `VC_Cs_Wood`, `VC_Cs_Concrete` |
| `VC_Town_TerraceA` | MarketTown | 12.0 x 12.0 m | 7.90 m | 684 | 468 | 168 | `VC_Mt_Brick`, `VC_Mt_Roof`, `VC_Mt_Trim`, `VC_Mt_Glass`, `VC_Mt_Render`, `VC_Mt_Concrete` |
| `VC_Town_TerraceB` | MarketTown | 12.0 x 12.0 m | 8.88 m | 924 | 588 | 168 | `VC_Mt_Brick`, `VC_Mt_Roof`, `VC_Mt_Trim`, `VC_Mt_Glass`, `VC_Mt_Render`, `VC_Mt_Concrete` |
| `VC_Town_TerraceC` | MarketTown | 12.0 x 12.0 m | 9.62 m | 548 | 356 | 104 | `VC_Mt_Brick`, `VC_Mt_Roof`, `VC_Mt_Trim`, `VC_Mt_Glass`, `VC_Mt_Render`, `VC_Mt_Concrete` |
| `VC_Town_TerraceD` | MarketTown | 12.0 x 12.0 m | 6.82 m | 824 | 488 | 128 | `VC_Mt_Brick`, `VC_Mt_Roof`, `VC_Mt_Trim`, `VC_Mt_Glass`, `VC_Mt_Render`, `VC_Mt_Concrete` |
| `VC_Town_TerraceE` | MarketTown | 12.0 x 12.0 m | 8.11 m | 708 | 480 | 180 | `VC_Mt_Render`, `VC_Mt_Roof`, `VC_Mt_Trim`, `VC_Mt_Glass`, `VC_Mt_Brick`, `VC_Mt_Concrete` |
| `VC_Mid_Mid34A` | Midrise | 34.0 x 18.0 m | 18.68 m | 624 | 408 | 144 | `VC_Mr_Panel`, `VC_Mr_Glass`, `VC_Mr_Metal`, `VC_Mr_Concrete`, `VC_Mr_Brick` |
| `VC_Mid_Mid34B` | Midrise | 34.0 x 18.0 m | 18.68 m | 588 | 408 | 144 | `VC_Mr_Brick`, `VC_Mr_Glass`, `VC_Mr_Metal`, `VC_Mr_Concrete`, `VC_Mr_Panel` |
| `VC_Mid_Mid34C` | Midrise | 34.0 x 18.0 m | 15.68 m | 588 | 396 | 144 | `VC_Mr_Brick`, `VC_Mr_Glass`, `VC_Mr_Metal`, `VC_Mr_Concrete`, `VC_Mr_Panel` |
| `VC_Mid_Mid3830` | Midrise | 38.0 x 24.0 m | 31.68 m | 816 | 480 | 144 | `VC_Mr_Panel`, `VC_Mr_Glass`, `VC_Mr_Metal`, `VC_Mr_Concrete`, `VC_Mr_Brick` |
| `VC_Mid_Mid3845` | Midrise | 38.0 x 24.0 m | 46.68 m | 852 | 540 | 144 | `VC_Mr_Panel`, `VC_Mr_Glass`, `VC_Mr_Metal`, `VC_Mr_Concrete`, `VC_Mr_Brick` |
| `VC_Mid_Mid3860` | Midrise | 38.0 x 24.0 m | 61.68 m | 1140 | 588 | 144 | `VC_Mr_Brick`, `VC_Mr_Glass`, `VC_Mr_Metal`, `VC_Mr_Concrete`, `VC_Mr_Panel` |
| `VC_Mid_Mid3875` | Midrise | 38.0 x 24.0 m | 76.68 m | 1068 | 648 | 144 | `VC_Mr_Panel`, `VC_Mr_Glass`, `VC_Mr_Metal`, `VC_Mr_Concrete`, `VC_Mr_Brick` |
| `VC_Ind_ShedBarrel` | Ironport | 64.0 x 40.0 m | 16.82 m | 408 | 184 | 44 | `VC_In_Clad`, `VC_In_Roof`, `VC_In_Glass`, `VC_In_Metal`, `VC_In_Concrete` |
| `VC_Ind_ShedFlat` | Ironport | 64.0 x 40.0 m | 10.92 m | 680 | 324 | 132 | `VC_In_Clad`, `VC_In_Roof`, `VC_In_Glass`, `VC_In_Metal`, `VC_In_Concrete` |
| `VC_Ind_ShedMonitor` | Ironport | 90.0 x 50.0 m | 15.90 m | 664 | 280 | 52 | `VC_In_Clad`, `VC_In_Roof`, `VC_In_Glass`, `VC_In_Metal`, `VC_In_Concrete` |
| `VC_Ind_ShedRail` | Ironport | 120.0 x 24.0 m | 16.30 m | 376 | 376 | 64 | `VC_In_Clad`, `VC_In_Roof`, `VC_In_Glass`, `VC_In_Metal`, `VC_In_Concrete` |
| `VC_Ind_ShedRailLow` | Ironport | 120.0 x 24.0 m | 10.80 m | 376 | 376 | 64 | `VC_In_Clad`, `VC_In_Roof`, `VC_In_Glass`, `VC_In_Metal`, `VC_In_Concrete` |
| `VC_Ind_ShedSawtooth` | Ironport | 90.0 x 50.0 m | 17.50 m | 660 | 312 | 60 | `VC_In_Clad`, `VC_In_Roof`, `VC_In_Glass`, `VC_In_Metal`, `VC_In_Concrete` |
| `VC_Ind_ProcessColumn` | IronportTanks | 8.0 x 8.0 m | 52.10 m | 578 | 218 | 70 | `VC_In_TankShell`, `VC_In_Metal`, `VC_In_Concrete` |
| `VC_Ind_TankCone` | IronportTanks | 34.0 x 34.0 m | 19.10 m | 1020 | 600 | 108 | `VC_In_TankShell`, `VC_In_Metal`, `VC_In_Concrete` |
| `VC_Ind_TankDome` | IronportTanks | 34.0 x 34.0 m | 14.55 m | 1058 | 626 | 126 | `VC_In_TankShell`, `VC_In_Metal`, `VC_In_Concrete` |
| `VC_Alp_ChaletA` | Alpine | 12.0 x 10.0 m | 8.02 m | 956 | 668 | 176 | `VC_Al_Timber`, `VC_Al_Stone`, `VC_Al_Roof`, `VC_Al_Glass`, `VC_Al_Trim`, `VC_Al_Concrete` |
| `VC_Alp_ChaletB` | Alpine | 12.0 x 10.0 m | 8.00 m | 836 | 572 | 152 | `VC_Al_Timber`, `VC_Al_Stone`, `VC_Al_Roof`, `VC_Al_Glass`, `VC_Al_Trim`, `VC_Al_Concrete` |
| `VC_Alp_ChaletC` | Alpine | 12.0 x 10.0 m | 8.00 m | 520 | 376 | 116 | `VC_Al_Timber`, `VC_Al_Stone`, `VC_Al_Roof`, `VC_Al_Glass`, `VC_Al_Trim`, `VC_Al_Concrete` |
| `VC_Alp_Workshop` | Alpine | 42.0 x 24.0 m | 8.50 m | 680 | 416 | 152 | `VC_Al_Timber`, `VC_Al_Stone`, `VC_Al_Roof`, `VC_Al_Glass`, `VC_Al_Trim`, `VC_Al_Concrete` |
| `VC_Rur_BarnBig` | Rural | 64.0 x 30.0 m | 10.85 m | 380 | 260 | 104 | `VC_Ru_Board`, `VC_Ru_Roof`, `VC_Ru_Metal`, `VC_Ru_Glass`, `VC_Ru_Trim`, `VC_Ru_Concrete` |
| `VC_Rur_BarnGambrel` | Rural | 26.0 x 14.0 m | 9.70 m | 384 | 264 | 72 | `VC_Ru_Board`, `VC_Ru_Roof`, `VC_Ru_Metal`, `VC_Ru_Glass`, `VC_Ru_Trim`, `VC_Ru_Concrete` |
| `VC_Rur_BarnLeanto` | Rural | 26.0 x 14.0 m | 8.40 m | 368 | 272 | 56 | `VC_Ru_Board`, `VC_Ru_Roof`, `VC_Ru_Metal`, `VC_Ru_Glass`, `VC_Ru_Trim`, `VC_Ru_Concrete` |
| `VC_Rur_FarmhouseA` | Rural | 12.0 x 10.0 m | 7.02 m | 644 | 452 | 128 | `VC_Ru_Board`, `VC_Ru_Roof`, `VC_Ru_Metal`, `VC_Ru_Glass`, `VC_Ru_Trim`, `VC_Ru_Concrete` |
| `VC_Rur_FarmhouseB` | Rural | 12.0 x 10.0 m | 7.02 m | 572 | 404 | 140 | `VC_Ru_Board`, `VC_Ru_Roof`, `VC_Ru_Metal`, `VC_Ru_Glass`, `VC_Ru_Trim`, `VC_Ru_Concrete` |
| `VC_Rur_Mill` | Rural | 32.0 x 18.0 m | 11.00 m | 952 | 520 | 76 | `VC_Ru_Board`, `VC_Ru_Roof`, `VC_Ru_Metal`, `VC_Ru_Glass`, `VC_Ru_Trim`, `VC_Ru_Concrete` |
| `VC_Rur_Silo` | Rural | 7.0 x 7.0 m | 16.55 m | 556 | 316 | 112 | `VC_Ru_Board`, `VC_Ru_Roof`, `VC_Ru_Metal`, `VC_Ru_Glass`, `VC_Ru_Trim`, `VC_Ru_Concrete` |
| `VC_Avi_Grandstand` | Aviation | 18.0 x 150.0 m | 14.00 m | 256 | 208 | 52 | `VC_Av_Clad`, `VC_Av_Roof`, `VC_Av_Glass`, `VC_Av_Metal`, `VC_Av_Concrete` |
| `VC_Avi_HangarArch` | Aviation | 62.0 x 74.0 m | 18.50 m | 304 | 236 | 80 | `VC_Av_Clad`, `VC_Av_Roof`, `VC_Av_Glass`, `VC_Av_Metal`, `VC_Av_Concrete` |
| `VC_Avi_HangarMonitor` | Aviation | 62.0 x 74.0 m | 17.70 m | 532 | 376 | 88 | `VC_Av_Clad`, `VC_Av_Roof`, `VC_Av_Glass`, `VC_Av_Metal`, `VC_Av_Concrete` |
| `VC_Avi_Terminal` | Aviation | 130.0 x 36.0 m | 14.15 m | 456 | 384 | 156 | `VC_Av_Clad`, `VC_Av_Roof`, `VC_Av_Glass`, `VC_Av_Metal`, `VC_Av_Concrete` |
| `VC_Ret_RetailBigbox` | Retail | 120.0 x 60.0 m | 11.46 m | 548 | 324 | 120 | `VC_Rt_Panel`, `VC_Rt_Glass`, `VC_Rt_Metal`, `VC_Rt_Concrete`, `VC_Rt_Brick` |
| `VC_Ret_RetailSmall` | Retail | 24.0 x 14.0 m | 6.20 m | 192 | 168 | 120 | `VC_Rt_Panel`, `VC_Rt_Glass`, `VC_Rt_Metal`, `VC_Rt_Concrete`, `VC_Rt_Brick` |
| `VC_Ret_RetailStrip` | Retail | 40.0 x 30.0 m | 7.72 m | 360 | 312 | 120 | `VC_Rt_Panel`, `VC_Rt_Glass`, `VC_Rt_Metal`, `VC_Rt_Concrete`, `VC_Rt_Brick` |
| `VC_Ret_GasStation` | Roadside | 26.0 x 16.0 m | 6.64 m | 208 | 168 | 96 | `VC_Rt_Panel`, `VC_Rt_Glass`, `VC_Rt_Metal`, `VC_Rt_Concrete`, `VC_Rt_Brick` |

Full per-set detail, the record groupings and the texture list are in `MANIFEST.md` next to this file.

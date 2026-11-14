"""Generate Terrain/MANIFEST.md from the build and verify reports (numbers are never hand-typed).

Run: blender -b --python make_manifest_terrain.py
"""
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import terrain_lib as T

HERE = os.path.dirname(os.path.abspath(__file__))
b = json.load(open(os.path.join(HERE, "build_stats.json")))
vr = json.load(open(os.path.join(HERE, "verify_report.json")))
v = {r["asset"]: r for r in vr}
n_issues = sum(1 for r in vr if r["issues"])

ORDER = ["VC_Rock_Boulder_S_Angular", "VC_Rock_Boulder_S_Rounded",
         "VC_Rock_Boulder_M_Angular", "VC_Rock_Boulder_M_Rounded",
         "VC_Rock_Boulder_L_Angular", "VC_Rock_Boulder_L_Rounded",
         "VC_Rock_Outcrop_A", "VC_Rock_Outcrop_B", "VC_Rock_CutFace_8m",
         "VC_Rock_ScreePatch", "VC_Rock_ShoreGroup",
         "VC_Log_Fallen_A", "VC_Log_Fallen_B",
         "VC_Impostor_Redwood_46m", "VC_Impostor_Broadleaf_Spreading",
         "VC_Impostor_Pine_Tall", "VC_Impostor_Palm_Royal_16m", "VC_Impostor_Spruce_Alpine",
         "VC_Impostor_Palm_Fan_Tall"]

DISTRICT = {
    "VC_Rock_Boulder_S_Angular": "D4, D8, D2", "VC_Rock_Boulder_S_Rounded": "D8, D2",
    "VC_Rock_Boulder_M_Angular": "D4, D8", "VC_Rock_Boulder_M_Rounded": "D8, D2",
    "VC_Rock_Boulder_L_Angular": "D4", "VC_Rock_Boulder_L_Rounded": "D8, D2",
    "VC_Rock_Outcrop_A": "D4, D8", "VC_Rock_Outcrop_B": "D4",
    "VC_Rock_CutFace_8m": "D4, D8 road cuts", "VC_Rock_ScreePatch": "D4, D8",
    "VC_Rock_ShoreGroup": "D2 shoreline", "VC_Log_Fallen_A": "D8", "VC_Log_Fallen_B": "D8",
}

DENSITY = [
    ("VC_Rock_Boulder_S_*", "scatter", "0.03-0.10 /m^2, min 0.8 m apart",
     "Slope dressing and verge edges. The cheapest rock in the kit; use it where count is wanted."),
    ("VC_Rock_Boulder_M_*", "scatter / small group", "0.008-0.030 /m^2, min 2.2 m apart",
     "Groups of 2-4 read better than singles. Rotate the yaw freely; the pivot is at the base centre."),
    ("VC_Rock_Boulder_L_*", "accent", "1 per 400-1500 m^2, min 6 m apart",
     "Landmark scale. Each one is a thing the player will aim at, so place rather than scatter."),
    ("VC_Rock_Outcrop_A/B", "slope break", "1 per 600-2500 m^2 on slopes over ~12 deg",
     "The asset that breaks a bare grass slope (VC_25). Sit it so the blocks emerge along the "
     "contour, not across it."),
    ("VC_Rock_CutFace_8m", "tiled module", "exactly 8.00 m pitch along the cut",
     "Verified periodic across the module boundary, so runs butt with no seam and no collision "
     "step. Yaw to the road tangent; do not mirror (the talus foot would face the wrong way)."),
    ("VC_Rock_ScreePatch", "scatter, masked to slope", "0.004-0.015 /m^2, min 4 m apart",
     "Drive-over. Below the outcrops and cut faces, never on a carriageway."),
    ("VC_Rock_ShoreGroup", "linear along the waterline", "6-14 m pitch",
     "D2 only. Sink to taste: the group is authored sitting 30 % buried already."),
    ("VC_Log_Fallen_A/B", "rare single", "1 per 800-3000 m^2 of D8 forest floor",
     "Never in rows, never on a slope steeper than the log (it will float at one end)."),
]

RULE = """
### The density rule, restated for rock

Same contract as the vegetation sets:

    instances_in_view  ~=  density * pi * cull_distance^2

Rock is heavier per instance than ground cover but is placed far more sparsely, so the binding
constraint is different: a boulder field is a *renderer count* problem, not a triangle problem.
Keep rock under **2500 instances in view** and under **1500 for the small boulders alone**.

Cull distances that match how each asset reads:
small boulders 90 m, medium 160 m, large 400 m, outcrops 500 m, cut-face modules 400 m,
scree 120 m, shore groups 250 m, fallen logs 150 m. As always the cull distance is the lever: it
is squared, the density is not.
"""


def rows():
    out, tot = [], [0, 0, 0]
    for n in ORDER:
        bs, vs = b[n], v[n]
        t = [l["tris"] for l in vs["lods"]]
        for i in range(3):
            tot[i] += t[i]
        coll = bs["collision"]
        if not coll:
            cs = "none"
        elif len(coll) == 1:
            cs = "%s (%d tris)" % (coll[0], bs["collision_tris"])
        else:
            cs = "%d x UCX_ (%d tris)" % (len(coll), bs["collision_tris"])
        out.append("| `%s` | %s | %s | %d | %d | %d | %s | %s |" % (
            n, DISTRICT.get(n, "all"), " x ".join("%.2f" % s for s in bs["size_m"]),
            t[0], t[1], t[2], cs, bs["materials"][0] if bs["materials"] else "-"))
    return out, tot


def impostor_rows():
    out = []
    for n in ORDER:
        if not n.startswith("VC_Impostor_"):
            continue
        r = b[n].get("impostor")
        if not r:
            continue
        t = v[n]["lods"][0]["tris"]
        out.append("| `%s` | %s | %.1f m | %d px | %.0f m | %.3f | **%.0f m** | %.2f | %d / %d / %d |"
                   % (n.replace("VC_Impostor_", ""), r["role"], r["height_m"], r["tile_px"],
                      r["d_texel_min_m"], r["screen_height_switch"], r["d_switch_m"], r["margin"],
                      t, v[n]["lods"][1]["tris"], v[n]["lods"][2]["tris"]))
    return out


r, tot = rows()
imp = impostor_rows()

md = """# Veyra City terrain kit - MANIFEST

Rock and terrain dressing for D4 Blackridge, D8 Redwood Valley and the D2 shoreline, plus the
far-tree impostors for BUG-117. All geometry and textures original; the GTA V video captures in
`ReferenceImages/08_GTAV_VideoCapture_2026-11-12/` (VC_25, VC_26, VC_07) were read for rock shape,
bedding and placement logic only - never for colour, black level or contrast, which those captures
cannot carry.

Blender 5.2.3 LTS headless, exported through `Tools/Blender/vc_tools.py` (`VC_Unity_Prop`),
1 Blender unit = 1 m. Pivot at base centre with the asset sitting on z = 0 (rocks bed a declared
fraction below it - see the sink column in the verify report).

## Materials: 2 new, 1 reused. Whole natural world now 5.

| Material | Textures | Mode | Used by |
|---|---|---|---|
| `M_Veyra_Rock` | `T_Veyra_Rock_{BaseColor,Normal,MetallicSmoothness}.png`, 1024 | opaque, **tiling** | every rock asset |
| `M_Veyra_Impostor` | `T_Veyra_Impostor_Atlas_{BaseColor,Normal,MetallicSmoothness}.png`, 2048 | **alpha clip 0.5**, Render Face Both | the five impostors |
| `M_Veyra_Bark` | **reused** from `../Vegetation/textures/` | opaque | the two fallen logs |

The rock set is a **tiling texture set, not an atlas**. Rock UVs are box-projected (dominant axis
per face) and tile freely, which buys 410 px/m at a 2.5 m repeat - an atlas rect could not come
close - and still costs exactly one material, which is what the "one shared rock material" ask was
actually for. Every asset in this kit has exactly **one** material; the verify pass fails any that
does not.

Across all three kits the natural world is now 5 materials: `M_Veyra_Foliage`,
`M_Veyra_Foliage_B`, `M_Veyra_Bark`, `M_Veyra_Rock`, `M_Veyra_Impostor`.

### Channel layout (all three new texture sets)

| Map | Channels | Encoding |
|---|---|---|
| BaseColor | RGB albedo, A coverage | sRGB; alpha hard-cut at 0.5 for the impostor atlas, solid 1.0 for rock |
| Normal | RGB tangent-space normal, A unused | **non-sRGB**, OpenGL +Y up |
| MetallicSmoothness | RGB metallic (0), **A smoothness = 1 - roughness** | non-sRGB, URP Lit "Metallic Alpha" |

Authoring values are quoted as ROUGHNESS throughout, which is what was authored; the packed alpha
carries its complement. Rock roughness runs **0.55-0.98** (rock that looks wet is the commonest way
a rock kit reads as plastic); the impostor atlas carries a single **0.78**.

These maps are import-ready: the alpha already holds smoothness, so nothing should invert it again
on the way in.

## Assets

| Asset (FBX) | district | size x/y/z m | LOD0 | LOD1 | LOD2 | collision | material |
|---|---|---|---|---|---|---|---|
%s
| **TOTAL** | | | **%d** | **%d** | **%d** | | |

Files: `export/<asset>.fbx` (one LOD set per file), group `.blend` files at
`VC_Terrain_<Group>.blend`, textures in `textures/`, review sheets in `review/`.

LODs are authored, not decimated: each rock LOD is the same noise field re-evaluated on a coarser
sphere, so the silhouette holds while the triangle count falls. Decimating a faceted rock rounds off
exactly the fracture edges that make it read as rock.

## Collision

| Kind | Used on | Why |
|---|---|---|
| `UCX_<name>` convex | boulders, logs | A boulder is convex enough that a hull is the right shape, and it is the cheapest thing PhysX can test. |
| `UCX_<name>_01..n` | outcrops, shore group | One hull **per block**. A single hull over a cluster would fill the gaps between blocks that you can see straight through. `VeyraModelPostprocessor` already walks every `UCX_` node, so numbered parts need no code change. |
| `COL_concrete_<name>` non-convex | road-cut panel | A leaning wall is not convex; a hull would bulge out over the carriageway. |
| `COL_gravel_<name>` non-convex | scree patch | Drive-over. The mound rim sits at exactly z = 0, so there is no step where it meets the terrain (CLAUDE.md: no vertical step over 0.05 m between adjacent collider surfaces). The verify pass measures this on the imported mesh and fails above 0.05 m. |
| none | impostors | An impostor is a render swap. The real tree keeps its own trunk collider; giving the impostor one too would double it up. |

The loose stones on the scree patch are **not** in the collider. They are all under 0.14 m, well
inside what the wheel raycast tolerates, and colliding them would cost far more than it buys.

### Collider surface naming: `COL_<surface>_<name>`

This kit found, and the Unity session has since made final, the convention these names follow.
`VeyraModelPostprocessor.MakeMeshCollider` used to map `COL_Ground*` to `grass` and everything else
to `dry_asphalt`, which meant only 2 of the 6 `SurfaceDefinition` assets that exist were reachable
from an imported model at all. Since wheel grip, tyre FX and surface audio all read
`SurfaceDefinition`, that is a correctness bug, not a nicety: it is why a cast-iron manhole cover
currently behaves and sounds like asphalt.

The convention now is **`COL_<surface>_<name>`**, surface token lowercase, matching a
`Data/Surfaces` asset filename:

- Valid today: `concrete`, `dirt`, `dry_asphalt`, `grass`, `gravel`, `wet_asphalt`.
- `dry_asphalt` has an underscore in it; the parser matches longest-first against known asset
  names rather than splitting naively, so `COL_dry_asphalt_Something` is correct and needs no
  workaround.
- `COL_Road` and `COL_Ground` keep working as aliases, so nothing already imported shifts.
- An unmatched token falls back to `dry_asphalt` **and logs an import warning** - silent fallback
  is what hid this in the first place.

In this kit: the scree patch is `COL_gravel_VC_Rock_ScreePatch`, and the road-cut panel is
`COL_concrete_VC_Rock_CutFace_8m`. Concrete is the nearest available match for a hard mineral face
rather than a true one - there is no `rock` surface asset, and this kit does not invent surface
tokens that have no asset behind them, because under the new parser every such name would warn on
every import. A `rock` surface is the right answer whenever someone adds one; until then concrete
gives the correct hard-surface behaviour for scraping along a cut face.

## Far-tree impostors (BUG-117)

**Chosen: a 3-card impostor - two crossed vertical cards plus one horizontal card - each with its
own separately baked view and a real baked normal map. Not octahedral.** The reasoning, stated
plainly because the brief asked for it:

1. An octahedral impostor needs a Unity shader to select and blend the three nearest views out of
   the hemi-octahedron. That shader cannot be written, compiled or tested from this session, and an
   atlas nothing can sample is worth nothing. The brief explicitly allows the fallback.
2. The cross billboard drops into the existing URP Lit alpha-clip setup with **no new shader**, so
   it is usable the day it lands.
3. What BUG-117 is actually about is alpha-tested overdraw from distant trees. The win comes from
   replacing a several-hundred-triangle card cloud with 4 triangles of compact silhouette. It does
   not require view-dependent parallax.

Each card carries its own baked view - 0 deg azimuth, 90 deg azimuth, and straight down - so two
of the four cardinal directions and the overhead view are exactly right rather than interpolated.

The horizontal card sits at the canopy's **alpha-weighted centroid**, measured from the side view
with the coverage weighted by its square. Plain alpha weighting drags the centroid down the trunk:
it put the royal palm's card at 11.7 m, below a crown that starts at 14 m. Squaring makes a
metres-wide crown row dominate a centimetres-wide trunk row, which puts the card in the foliage
where it belongs. The top camera looks down on the ORIGIN rather than the bounding-box centre, so
all three cards share the asset's pivot.

**Rejected: octahedral impostors. Recorded here so the question is not re-opened without new
information.** The blocker is not the bake - baking a hemi-octahedron grid of views is a contained
change to `scripts/impostor_bake.py`: a grid of camera directions instead of two, and a larger
atlas. The blocker is the sampling side. An octahedral impostor needs a Unity shader that maps the
view direction onto the octahedron, picks the three nearest cells and blends them, and that shader
has to be written, compiled, profiled and owned by whoever owns `Assets/` - the Unity session, not
this one. Revisit only when (a) somebody owns that shader, and (b) a measured frame capture shows
the cross billboard's 45-degree narrowing or its missing parallax actually costing something at the
switch distances in the table. Neither is true today.

### How they were baked

From the existing LOD0 meshes, with the materials temporarily swapped for Emission so the render
*is* the data rather than a lit picture: albedo from the base-colour texture, coverage from its
alpha on a transparent film, and normals from the camera-space surface normal. For a camera-facing
card, camera space **is** the card's tangent space (right, up, toward-camera), so the baked map
drops straight into a URP Lit normal slot with no conversion. Everything is read back from 32-bit
EXR and written by the project's own PNG writer, so no view transform ever touches the data.

### Switch distances

`d_texel` is where one atlas texel equals one screen pixel at 1440p with a 60 deg vertical FOV -
below it the impostor is being magnified. It is a hard floor: no species switches nearer than its
own texel limit, whichever way the art direction leans.

**The switch height is per role, not uniform.** A single 0.25 for everything is wrong, because a
lower h swaps FURTHER out - it keeps the real mesh for longer - and the species that need that are
the small ones:

- **Forest species, h = 0.25** (`Redwood_46m`, `Pine_Tall`, `Spruce_Alpine`). Seen en masse, at
  distance, usually against sky or haze. Nobody gets close to a redwood crown, and a stand of pines
  is exactly the overdraw case BUG-117 is about, so swap early and take the win.
- **Street species, h = 0.11** (`Broadleaf_Spreading`, `Palm_Royal_16m`, `Palm_Fan_Tall`). These
  are props the player drives directly past, with a camera that is close and moving. A 2-card cross
  at that range reads flat and visibly turns as the camera swings by. Keeping the real mesh roughly
  twice as far out costs mesh in exactly the places that are least densely planted, which is the
  right trade.

The fan palm is in the set specifically because it is the small boulevard species: at 6.5 m a
uniform 0.25 would have swapped it at about 21 m, in the player's face.

| Species | role | height | tile | d_texel | screen h | **switch at** | margin | tris L0/L1/L2 |
|---|---|---|---|---|---|---|---|---|
%s

The Unity session needs the **metres** column, not the ratio. `screen h` is the LODGroup
`screenRelativeTransitionHeight` that produces that distance at 1440p / 60 deg vertical FOV; if the
project changes FOV or resolution, regenerate from the metres rather than keeping the ratio.

Suggested LODGroup for an impostor-equipped tree: **LOD0 ends 0.45, LOD1 ends the species' own
`screen h` from the table, impostor ends 0.012 (cull)**. For the forest species that moves the
impostor band considerably nearer than the 0.12 the vegetation manifests suggested, which is the
whole point: the old LOD2 was not good enough to switch to early, and these are.

### These thresholds are NOT the project's 0.35 convention, deliberately

`Tests/EditMode/ChunkImportTests.cs:79` fails any imported model with three or more LOD levels
unless LOD0's `screenRelativeTransitionHeight` is exactly **0.35**. That test covers only
`Art/World/Chunks` and `Art/Models/Kitbash` today, so vegetation, rock and impostors are outside
its scope - but it is the only written LOD convention in the project and someone will apply it by
habit. It is wrong for all three kits:

- 0.35 means LOD0 ends at d = size / (1.1547 * 0.35) = **2.47x the asset's size in metres**. That
  is sensible for a 40 m building chunk (LOD0 out to 99 m). For a 0.72 m boulder it puts LOD0 at
  1.8 m - nearer than the car's own bonnet - so LOD0 would effectively never draw.
- These LODs are authored around silhouette, not screen area. A tree's LOD1 has to hold the crown
  shape and a rock's LOD1 has to hold its fracture edges; those fail at different distances than a
  building's panel detail does.

So: chunks keep 0.35; vegetation, rock and impostors use the per-asset thresholds in this manifest
and in the two vegetation manifests. If that test is ever widened to `Art/Models`, it needs a
per-category expectation rather than one constant.

### Where the impostor breaks down - honestly

- **45 degrees of azimuth.** With cards at 0 and 90 degrees, a view from 45 degrees sees both cards
  foreshortened and the tree reads roughly 25-30 %% narrower than it should. This is the classic
  cross-billboard artefact and it is the single biggest reason not to switch nearer than the table
  says. It is worst on the broadleaf (widest crown) and barely visible on the spruce (narrow).
- **No canopy parallax at all.** Orbiting the camera around a stand of impostors, nothing inside
  the crown moves relative to anything else. At the recommended distances this reads as "distant
  trees are flat", which is what every game does; nearer, it reads as cardboard.
- **Looking down: FIXED, at a cost of 2 triangles.** Two vertical cards foreshorten to slivers
  from above and the crown vanishes - which is precisely the D4 and D8 clifftop-road case, where a
  player can sit still and look down at a forest. The third card is a horizontal quad at the canopy
  centroid carrying a baked top view. It is edge-on and effectively invisible from ground level
  (confirmed in the review sheets), so it costs nothing at the common viewing angle.
- **The horizontal card at a shallow angle.** Around 15-30 degrees of elevation the flat card is
  neither edge-on nor face-on and can read as a faint disc through the crown. The two vertical
  cards hide most of it and it is a far smaller error than the collapsed crown it replaces, but it
  is the new worst case and it is the reason not to switch nearer than the table says.
- **Lighting.** The baked normal map carries the real foliage normals, so the impostor responds to
  the sun correctly, but self-shadowing inside the crown is baked flat; at dawn and dusk a distant
  stand will be slightly too bright compared with the LOD1 trees in front of it.
- **No wind.** If vegetation ever gets vertex-animated wind, impostors will stand still while the
  LOD1 trees next to them move, and the transition will become visible.

### What it costs

Six impostors, **6 triangles each at LOD0** (3 cards), 2 at LOD1 and LOD2. One 2048 atlas,
18 tiles, 1792 of 2048 rows used, three maps - about 5.5 MB as BC7 on PC, 2.8 MB as ASTC 6x6. Against that, each distant tree stops submitting its LOD1 card cloud
(`VC_Tree_Pine_Tall_LOD1` alone is 636 triangles of alpha-tested cards, most of whose texels fail
the clip). I have no Unity access, so I will not pretend to a measured overdraw figure: the
triangle reduction is 100-200x per distant tree, and the alpha-tested fill reduction should be
large but needs the main session to measure it in a release build before anyone writes it down as
a fix for BUG-117.

## Placement spacing and scatter density

Authoring intent, in the same form as the vegetation manifests.

%s
%s
## Rebuilding

```
blender -b --python scripts/rock_tex.py            # the tiling rock texture set
blender -b --python scripts/impostor_bake.py       # bakes the impostor atlas from the LOD0 meshes
blender -b --python scripts/build_terrain.py       # .blend files + FBX + build_stats.json
blender -b --python scripts/verify_terrain.py      # re-imports every FBX and checks it
blender -b --python scripts/make_review_terrain.py # review sheets
blender -b --python scripts/make_manifest_terrain.py
```

`impostor_bake.py` reads `../Vegetation/` and `../Vegetation2/` but never writes to them.

Verify result at the time of writing: **%d assets, %d with issues**
(`scripts/verify_report.json`).
""" % ("\n".join(r), tot[0], tot[1], tot[2], "\n".join(imp),
       "| Asset | Pattern | Spacing / density | Notes |\n|---|---|---|---|\n"
       + "\n".join("| `%s` | %s | %s | %s |" % d for d in DENSITY),
       RULE, len(vr), n_issues)

with open(os.path.join(T.ROOT, "MANIFEST.md"), "w", encoding="utf-8") as f:
    f.write(md)
print("Terrain/MANIFEST.md written")

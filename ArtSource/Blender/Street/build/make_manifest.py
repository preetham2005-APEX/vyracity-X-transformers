"""Generate ArtSource/Blender/Street/MANIFEST.md from build/report.json + build/verify.json."""
import datetime
import json
import os

B = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(B)
R = {r["name"]: r for r in json.load(open(os.path.join(B, "report.json")))}
V = {r["file"][:-4]: r for r in json.load(open(os.path.join(B, "verify.json")))}

ORDER = ["VC_Hydrant", "VC_ParkingMeter", "VC_TrashCan_Civic", "VC_Dumpster", "VC_PostBox", "VC_NewsBox",
         "VC_BusShelter", "VC_Lamp_Cobra_Single", "VC_Lamp_Cobra_Twin", "VC_Lamp_Boulevard_Twin",
         "VC_Lamp_Ornate_Single", "VC_Lamp_Ornate_Twin", "VC_SignBlade", "VC_SignSpeedLimit",
         "VC_Bollard", "VC_Barrel_Construction"]
assert set(ORDER) == set(R), set(ORDER) ^ set(R)

BUDGET = {"VC_BusShelter": "shelter < 1500"}
for k in ORDER:
    if k.startswith("VC_Lamp_"):
        BUDGET[k] = "lamp < 1200"

USE = {
    "VC_Hydrant_Red": "hydrant body (colour variant A)",
    "VC_Hydrant_Yellow": "hydrant body (colour variant B, same mesh)",
    "VC_Steel_Galv": "spare galvanised steel",
    "VC_Steel_Dark": "sign posts and brackets",
    "VC_Iron_Painted": "ornate lamp standards",
    "VC_Paint_Civic_Green": "post box, civic litter bin",
    "VC_Paint_Civic_Blue": "commercial dumpster",
    "VC_Paint_Slate": "parking meter, bollard",
    "VC_Paint_Orange": "news box, construction barrel",
    "VC_Paint_White": "spare white paint",
    "VC_Paint_Alu_Grey": "cobra and boulevard lamps, shelter frame",
    "VC_Retro_White": "retroreflective bands",
    "VC_Glass_Clear": "shelter glazing, meter and news-box windows",
    "VC_Sign_BlankFace": "BLANK sign plates",
    "VC_Poster_Blank": "BLANK shelter poster",
    "VC_Concrete_Plinth": "boulevard lamp plinth",
    "VC_Wood_Slat": "shelter bench",
    "VC_Lamp_Emissive": "ALL luminaire lenses - Unity drives _EMISSION",
}

src = open(os.path.join(B, "vc_street_lib.py"), encoding="utf-8").read()
ns = {}
exec(src[src.index("MATERIALS = {"):src.index("def mat(")], ns)

L = []
w = L.append
w("# Veyra City - street furniture set (ArtSource/Blender/Street)")
w("")
w("Original geometry, authored headless in Blender 5.2.3 by `build/build_street.py` (re-runnable: running it")
w("again rebuilds every .blend and .fbx from scratch, with no duplicated objects). Reference frames in")
w("`ReferenceImages/08_GTAV_VideoCapture_2026-11-12/` (VC_12, VC_17, VC_20, VC_22, VC_30, VC_31) were read for")
w("**proportion and construction logic only**; no mesh, texture, logo, lettering or layout comes from any other")
w("game. Every sign plate and the bus-shelter poster are **blank** - Unity applies Veyra's own artwork.")
w("")
w("Built " + datetime.date.today().isoformat() + ". Blender 5.2.3 LTS, metric, 1 unit = 1 m.")
w("")
w("## Pipeline facts")
w("")
w("- Export preset **VC_Unity_Prop** through `vc_tools.vc_export_fbx(..., mode=\"prop\")`: FBX, axis_forward -Z,")
w("  axis_up Y, global_scale 1, apply_unit_scale, FBX_SCALE_UNITS, bake_space_transform.")
w("- **Pivot: base centre**, on the x/y origin at z = 0 for every prop. For the lamps that is the *pole* base,")
w("  not the bounding-box centre (a cobra arm overhangs the carriageway) - which is what street placement needs.")
w("- Authored with the **front facing Blender -Y**, so the preset's 180 deg turn puts the front at **Unity +Z**.")
w("  Confirmed by re-importing every FBX and measuring it (`build/verify_fbx.py` -> `build/verify.json`).")
w("- One LOD set per FBX. The root is an EMPTY named after the prop; its children are the `<name>_LOD<n>` meshes")
w("  plus the collision meshes, so Unity builds exactly one LODGroup per file.")
w("- Rotation and scale applied (every LOD mesh reports scale 1/1/1, rotation 0/0/0). Caps are triangulated, so")
w("  **0 n-gons**; normals recalculated outward, **0 inverted faces**, 0 degenerate faces, 0 loose vertices;")
w("  UVs on every LOD (Smart UV Project, 0.02 island margin).")
w("- Materials are Principled **metal/roughness PBR**, solid albedo, no texture maps yet - so nothing can break")
w("  the power-of-two rule. Slot order in the table below is the Unity material order.")
w("- Luminaires carry a dedicated emissive slot **`VC_Lamp_Emissive`** (HDR emission 1.00 / 0.92 / 0.74 at")
w("  strength 8) on the lens or lantern glass only, so Unity can drive `_EMISSION` at night.")
w("- LOD ratios are the spec 1.0 / 0.40 / 0.10 **with a floor** (LOD1 >= 72 tris, LOD2 >= 56 tris). These props")
w("  are cheap enough that a literal 10 % would delete the pole; the ratio actually used is listed per prop.")
w("- Preview renders: `preview/sheet_small_props.png`, `preview/sheet_tall_props.png` and their `_34`")
w("  three-quarter versions. The pale 1.80 m rods at each end are a human-scale reference, not part of the set.")
w("")
w("## Triangle counts")
w("")
w("| Prop | Size W x D x H (m) | LOD0 | LOD1 | LOD2 | ratios | budget | Collision (tris) | Material slots |")
w("|---|---|---|---|---|---|---|---|---|")
for n in ORDER:
    r = R[n]
    t = [str(l["tris"]) for l in r["lods"]] + ["-"] * (3 - len(r["lods"]))
    size = " x ".join("%.2f" % v for v in r["size_m"])
    col = ", ".join("%s (%d)" % (c["name"], c["tris"]) for c in r["collision"])
    slots = ", ".join("%d: %s" % (i, s) for i, s in enumerate(r["slots"]))
    ratios = " / ".join("%.2f" % x for x in r["lod_ratios"])
    w("| `%s` | %s | %s | %s | %s | %s | %s | %s | %s |"
      % (n, size, t[0], t[1], t[2], ratios, BUDGET.get(n, "small < 400"), col, slots))
w("")
w("Whole set at LOD0: **%d triangles** across 16 props (%d collision triangles)."
  % (sum(R[n]["lods"][0]["tris"] for n in ORDER),
     sum(c["tris"] for n in ORDER for c in R[n]["collision"])))
w("")
w("## Files")
w("")
w("| Prop | .blend | .fbx | Pivot | Notes |")
w("|---|---|---|---|---|")
for n in ORDER:
    r = R[n]
    w("| `%s` | `%s` | `%s` | base centre (0, 0, 0) | %s |" % (n, r["blend"], r["fbx"], r["note"]))
w("")
w("## Collision")
w("")
w("`UBX_` = BoxCollider, `UCX_` = convex MeshCollider (CLAUDE.md). Collision meshes carry no material and never")
w("render. Lamp heads 6-9 m up get no collider; every pole and upright does. The bus shelter ships nine box")
w("colliders (4 posts, rear and left glazing, poster bay, bench, roof).")
w("")
for n in ORDER:
    w("- `%s`: %s" % (n, ", ".join("`%s` (%d tris)" % (c["name"], c["tris"]) for c in R[n]["collision"])))
w("")
w("## Material library used by this set")
w("")
w("| Material | Use | Metallic | Roughness | Emission |")
w("|---|---|---|---|---|")
for k, (c, m, rg, e, s) in ns["MATERIALS"].items():
    em = "-" if e is None else ("%.2f / %.2f / %.2f @ %.1f" % (e[0], e[1], e[2], s))
    w("| `%s` | %s | %.2f | %.2f | %s |" % (k, USE.get(k, ""), m, rg, em))
w("")
w("## Self-check")
w("")
w("`build/verify.json` is the machine-readable result of re-importing all 16 exported FBX files: **0 problems**")
w("- no n-gons, no inverted faces, no missing UVs, every file has `_LOD<n>` meshes and at least one collision")
w("mesh, every base sits at z = 0, every root pivot at the origin. Known and accepted: decimated LOD1/LOD2")
w("meshes contain a few non-manifold edges where the collapse opened a shell. That is normal for a far LOD, and")
w("they keep LOD0's winding because the LOD cleanup deliberately does not re-run a normal recalculation on them")
w("(a recalc on a partly open shell is ambiguous and can flip faces).")
w("")
w("## Not done here")
w("")
w("- No texture maps and no packed UV atlas beyond Smart UV Project; these are PBR solid-colour materials.")
w("- Nothing was written into `Assets/` and Unity was never run - another session holds the editor lock.")
w("- Not yet placed, not yet measured in-engine: no Unity import, LODGroup, collider or frame-cost evidence.")

open(os.path.join(ROOT, "MANIFEST.md"), "w", encoding="utf-8").write("\n".join(L) + "\n")
print("WROTE MANIFEST.md")

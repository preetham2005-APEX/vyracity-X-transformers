"""Add the ChunkImportTests gates to the building kit's UNITY_HANDOFF. Verified by reading
Assets/_Veyra/Tests/EditMode/ChunkImportTests.cs, not taken on report. Writes files only."""
import io, json, glob, os

B = r"E:\VeyraCity\ArtSource\Blender\Buildings\build"
mats = set()
for f in glob.glob(os.path.join(B, "report_*.json")):
    if "fit" in os.path.basename(f):
        continue
    for r in json.load(open(f)):
        mats.update(s for s in r["slots"] if s)
mats = sorted(mats)

p = os.path.join(B, "make_handoff.py")
s = io.open(p, encoding="utf-8").read()
s = s.replace('''h("## 7. Checks worth making in the editor")''',
'''h("## 6b. `ChunkImportTests` gates this kit has to pass")
h("")
h("`Assets/_Veyra/Tests/EditMode/ChunkImportTests.cs` scans")
h("`Art/World/Chunks` and **`Art/Models/Kitbash`** - which is where this kit lands, so it is already in")
h("scope the moment the FBX are copied. Read from the test, not assumed:")
h("")
h("| Gate | This kit |")
h("|---|---|")
h("| a `COL_` node needs a non-convex MeshCollider **and** a `VehicleSurface` with a `SurfaceDefinition` | **not applicable** - all %d models use `UBX_` only, no `COL_`, no `UCX_` |" % len(mats and [1] or [1]))
h("| a `UCX_` node needs a convex MeshCollider | not applicable |")
h("| a `UBX_` node needs a `BoxCollider` | **applies to every model** - depends on `CollisionPrefixPostprocessor` adding it on import |")
h("| a collision node must have **no Renderer** | **applies** - the FBX carry `UBX_` as mesh objects, so the postprocessor has to strip the renderer |")
h("| every material slot must resolve to a material **registered in the VeyraMaterialLibrary** (present is not enough) | **applies to all %d distinct material names below** |" % len(mats))
h("| fewer than 5 %% of slots may fall back to `M_Debug_Missing` | follows from the above |")
h("| with **3 or more** LOD levels, LOD0 `screenRelativeTransitionHeight` must be exactly **0.35** | **applies to all models** - every one ships 3 LODs |")
h("")
h("**The material gate is the one that will fail first.** The export strips textures and carries only")
h("slot names, so unless the library has a slot for each of these, every renderer in the kit errors:")
h("")
h("```")
for i in range(0, len(mats), 4):
    h("   " + "  ".join("%-22s" % m for m in mats[i:i + 4]).rstrip())
h("```")
h("")
h("That is **%d names** from the building kit; the facade kit adds 7 more (`VC_Fac_*`). They can map to" % len(mats))
h("existing library materials rather than needing new ones - several are deliberate duplicates of each")
h("other across sets (every `*_Glass`, `*_Concrete`, `*_Metal`) - but each name needs a slot that")
h("resolves, or the test reports it per renderer.")
h("")
h("### What the 0.35 rule means for distance")
h("")
h("With LOD0 pinned at 0.35 and a 60 deg vertical FOV, LOD0 holds to `size / 0.404` metres, where `size`")
h("is the largest axis of the bounds. So: a 14 m house to **35 m**, a 55 m tower to **136 m**, a 137 m")
h("tower to **339 m**, a 245 m tower to **606 m**. Two consequences worth knowing before merging facade")
h("geometry into a building's LOD0:")
h("")
h("- anything merged into a tall tower's LOD0 mesh is drawn out to several hundred metres, long past the")
h("  distance where a 0.26 m window recess is a pixel. The triangles are paid for the whole way.")
h("- if that matters, merge the reveals into a **separate chunked mesh with its own LODGroup** sized to")
h("  the reveal band rather than into the building mesh: same draw-call saving, but it culls at a")
h("  sensible distance instead of inheriting the building's bounds. That is a Unity-side call.")
h("")
h("## 7. Checks worth making in the editor")''')
io.open(p, "w", encoding="utf-8").write(s)
print("patched make_handoff.py with the ChunkImportTests gates (%d material names)" % len(mats))

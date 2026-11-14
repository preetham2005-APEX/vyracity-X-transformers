"""A real `rock` SurfaceDefinition now exists, so the cut face drops its concrete stand-in.

Run with plain python, then delete. The manifest template is %-formatted with literal percents
escaped as %%, so new prose here must do the same.
"""
import os

HERE = os.path.dirname(os.path.abspath(__file__))


def patch(fname, pairs):
    p = os.path.join(HERE, fname)
    s = open(p, encoding="utf-8").read()
    for a, b in pairs:
        assert a in s, "%s: anchor not found: %r" % (fname, a[:70])
        s = s.replace(a, b)
    open(p, "w", encoding="utf-8").write(s)
    print("patched", fname)


patch("build_terrain.py", [
    ('("col", R.cut_face_collider, "COL_concrete_")',
     '("col", R.cut_face_collider, "COL_rock_")'),
])

patch("make_manifest_terrain.py", [
    ("| `COL_concrete_<name>` non-convex | road-cut panel |",
     "| `COL_rock_<name>` non-convex | road-cut panel |"),

    ("""Six `SurfaceDefinition` assets exist (`concrete`, `dirt`,
`dry_asphalt`, `grass`, `gravel`, `wet_asphalt`) and only two of them are reachable from an
imported model at all; CLAUDE.md names eight surfaces, so `metal` and `kerb` do not exist yet
either. The road-infrastructure session reports the same problem landing its kerbs and manhole
covers on `dry_asphalt`.""",
     """When this kit was authored, six `SurfaceDefinition` assets existed
(`concrete`, `dirt`, `dry_asphalt`, `grass`, `gravel`, `wet_asphalt`) and only TWO of them were
reachable from an imported model at all, because the postprocessor hard-coded `COL_Ground` to
`grass` and everything else to `dry_asphalt`. Since wheel grip, tyre FX and surface audio all read
`SurfaceDefinition`, that was a correctness bug rather than a nicety: it is why a cast-iron manhole
cover behaved and sounded like asphalt.

That is now fixed. `Data/Surfaces` holds twelve assets - `cobblestone`, `concrete`, `dirt`,
`dry_asphalt`, `grass`, `gravel`, `kerb`, `metal`, `paving`, `rock`, `sand`, `wet_asphalt` - and all
eight surfaces CLAUDE.md names are backed by a real asset."""),

    ("""In this kit: the scree patch is `COL_gravel_VC_Rock_ScreePatch`, and the road-cut panel is
`COL_concrete_VC_Rock_CutFace_8m`. Concrete is the nearest available match for a hard mineral face
rather than a true one - there is no `rock` surface asset, and this kit does not invent surface
tokens that have no asset behind them, because under the new parser every such name would warn on
every import. A `rock` surface is the right answer whenever someone adds one; until then concrete
gives the correct hard-surface behaviour for scraping along a cut face.""",
     """In this kit: the scree patch is `COL_gravel_VC_Rock_ScreePatch` and the road-cut panel is
`COL_rock_VC_Rock_CutFace_8m`.

The cut face shipped briefly as `COL_concrete_` because no `rock` surface existed yet, and this kit
does not invent tokens with nothing behind them - under the parser an unmatched token falls back to
`dry_asphalt` and warns on every import, so a speculative `COL_rock_` would have produced warning
noise on every model in the project until the asset arrived, by which time the warning would have
been ignored. Taking an honest stand-in and flagging it beat being early. The real surface now
exists and the stand-in is gone.

**The scree patch is a single collider, all `gravel`, deliberately.** It was asked whether the
drive-over rim at z = 0 should carry a different surface from the patch body, since the rim is what
a tyre contacts at speed. It should not, for three reasons measured off the geometry:
- The mound profile is `0.10 * (1 - (r/2.25)^2)^1.3`, so the outer 0.25 m of radius is under 5 mm
  high. There is no distinguishable rim zone to stand on - it is a feather edge.
- At 60 km/h a wheel crosses a 0.5 m annulus in about 30 ms, under two frames at 60 Hz. A surface
  change that brief reads as a glitch, not as detail.
- At the rim the collider is exactly coplanar with the terrain by design (that is what keeps the
  step at zero). The wheel raycast is therefore already ambiguous between scree and ground there.
  Adding a third surface zone would stack a second flicker boundary on top of an existing one, and
  surface flicker is audible before it is visible.

If graded surface response is wanted for scree, the place to get it is a larger asset - a 10 m
spill with a genuine loose centre and a thin outer scatter - not a subdivision of a 4.5 m patch."""),
])
print("done")

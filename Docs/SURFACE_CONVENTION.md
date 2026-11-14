# Collider surfaces — naming convention and hazards

How an imported model's colliders get their `SurfaceDefinition`, and the two traps that go with it.
Agreed 2026-11-14 across the Unity session and the three Blender sessions.

## The convention

    COL_<surface>_<name>

`<surface>` is the filename of an asset in `Assets/_Veyra/Data/Surfaces`, lowercase.
`VeyraModelPostprocessor.SurfaceIdFor` resolves it at import.

Available (12): `cobblestone`, `concrete`, `dirt`, `dry_asphalt`, `grass`, `gravel`, `kerb`,
`metal`, `paving`, `rock`, `sand`, `wet_asphalt`. All eight surfaces CLAUDE.md names are backed.

- **Matched longest-first**, because ids contain underscores themselves. A naive
  `Split('_')[1]` on `COL_dry_asphalt_Gutter` yields `"dry"` and matches nothing.
- `COL_Road` → `dry_asphalt` and `COL_Ground` → `grass` keep working, so nothing already
  imported changes behaviour.
- **An unrecognised name warns.** It still falls back to `dry_asphalt`, but it says so.

### What this replaced, and why it matters

One ternary: `COL_Ground` meant grass, everything else meant `dry_asphalt`. **Only two of the
surface assets were reachable by any imported model.** A cast-iron manhole, a granite kerb and a
scree patch all got asphalt grip, asphalt tyre FX and asphalt audio. Nothing reported it, because
the fallback was silent. Found by the vegetation session while checking its own scree patch.

**One MeshCollider carries one surface.** A kerb that is concrete on the face and asphalt in the
gutter genuinely needs two colliders, butted on the same plane at the same toe height —
`COL_concrete_<prop>_Kerb` and `COL_dry_asphalt_<prop>_Gutter`. CLAUDE.md allows no vertical step
over 0.05 m between adjacent collider surfaces, and a wheel crossing that seam finds any error.

## Hazard: coplanar drive-over colliders are already in a raycast tie

Raised by the vegetation session, and it generalises well past the case that prompted it.

A flush, decal-style collider sitting at z = 0 — a scree rim, a manhole, a joint strip, a drain
cover — is **exactly coplanar with the ground collider underneath it, by design**, because that
is what keeps the vertical step at zero. So the wheel raycast is already ambiguous there: which
collider wins is effectively a coin flip.

That is harmless today **only because both surfaces are at the same height and both answers are
plausible**. It stops being harmless the moment two coplanar colliders disagree about what they
are. Then the coin flip has an audible consequence, and **surface flicker is audible well before
it is visible** — a tyre-audio blip and a one-frame grip change.

**So: do not give a flush drive-over collider a surface that differs from the ground it sits on**
unless the contact zone is metres wide. Check what is underneath before assigning.

## Worked example: why the scree rim stays one surface

The vegetation session's reasoning, kept because the method is reusable:

1. **No zone to stand on.** The mound profile `0.10·(1-(r/2.25)²)^1.3` puts the outer 0.25 m of
   radius under 5 mm high. A feather edge, not a lip — a tyre cannot distinguish it from the
   ground beside it.
2. **Too brief to read as detail.** At 60 km/h a wheel crosses a 0.5 m annulus in ~30 ms, under
   two frames at 60 Hz. A surface change that short reads as a glitch, not as texture.
3. **It would stack a second ambiguity on the existing tie** described above.

Graded scree response belongs in a larger asset — a 10 m spill with a loose centre and a thin
outer scatter, where each zone is metres wide and a wheel spends real time in it. Subdividing a
4.5 m patch buys nothing and risks the flicker.

## The live instance: flush castings must NOT become `metal`

Raised by the vegetation session after the hazard above was written, and it is not hypothetical.

**A manhole cover sitting flush in an asphalt road is the exact case.** Its `COL_` collider is
coplanar with the road collider, so the wheel raycast ties between them. Today that is silent
only because the RoadWear castings are mapped to `concrete` behind the `CASTING_SURFACE`
constant — wrong, but *consistently* wrong, and close enough to asphalt that nobody hears it.

**Moving them to `metal` now that the asset exists is what makes it audible.** A tyre crossing
the cover would flip between `metal` and `dry_asphalt` grip and audio, several times a second,
at every manhole in the city. The "fix" would ship the defect.

Resolution, for any flush drive-over casting — manhole, utility cover, storm grate, joint strip:

1. **Preferred: no collider at all.** The road collider underneath already carries the surface
   and the correct height. A decorative cover that is flush by definition does not need its own
   drivable collider, and without one there is no tie.
2. **Otherwise: match the road.** Keep it on `dry_asphalt`, the same answer as the ground, so
   the tie stays silent.

`metal` is still the right surface for castings that stand **proud** of the road, or that sit in
a surface a wheel does not cross. The asset is not wasted; it is the flush case that cannot use
it.

## Still a stand-in

`VC_Fac_*`, `VC_Steel_Dark` and friends are materials, not surfaces — see
`Docs/MATERIAL_REGISTER.md`. The only surface stand-in left is anything still named
`COL_concrete_*` that is really metal; the RoadWear castings were deliberately placed on
`concrete` behind a named `CASTING_SURFACE` constant while no `metal` asset existed. That asset
now exists, so those can move.

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

## Is it actually flush? Measure before assuming

I asserted that the RoadWear castings were flush and therefore must not take `metal`. **That was
wrong, and the infra session corrected it with measurements.** From its manifest:

| Casting | Stands proud |
|---|---|
| `VC_Manhole_Round_A` / `_B` | **+10 mm** (frame +6 mm, rim tapers to the road plane) |
| `VC_UtilityCover_Square` | **+10 mm** |
| `VC_StormDrain_Grate_Road` | **+6 mm** |

A collider 6–10 mm above the road plane is **not** coplanar with it, so there is no raycast tie
and no flicker. `metal` is correct for all four, and they now use it.

The expansion joint strip genuinely is flush, and it ships with **no collider** — the road
underneath carries surface and height, which is the right treatment for that case.

**The rule, stated properly:** the hazard is coplanarity, not category. A casting is not "a
flush thing" by virtue of being a casting. Before giving a drive-over collider a surface that
differs from the ground beneath it, **check its actual height above the road**:

- **Coplanar (0 mm):** no own collider, or match the ground's surface. The tie is real.
- **Proud by a few mm or more:** its own collider and its own true surface. The raycast has an
  unambiguous winner.

A millimetre figure settles it; an assumption about what kind of object it is does not. I had
the hazard right and the instance wrong, which is the easier half to get right.

## Still a stand-in

`VC_Fac_*`, `VC_Steel_Dark` and friends are materials, not surfaces - see
`Docs/MATERIAL_REGISTER.md`.

No surface stand-ins remain. The RoadWear castings were on `concrete` behind a named
`CASTING_SURFACE` constant while no `metal` asset existed; `metal` exists now, they measure
6-10 mm proud, and they use it. Kerb colliders moved to the new `kerb` surface with the seam
still at 0.000000 m.

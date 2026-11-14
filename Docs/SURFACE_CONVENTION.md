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

## Hazard: coplanar drive-over colliders are already in a contact tie

Raised by the vegetation session, and it generalises well past the case that prompted it.

A flush, decal-style collider sitting at z = 0 — a scree rim, a manhole, a joint strip, a drain
cover — is **exactly coplanar with the ground collider underneath it, by design**, because that
is what keeps the vertical step at zero. So the wheel contact test is already ambiguous there: which
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

### The wheel is a sphere sweep, not a ray

Verified in `VehicleController.cs:845` by the infra session rather than relayed: since 9e945b87
wheel contact is `Physics.SphereCastNonAlloc` at 0.85 x tyre radius, nearest hit along the sweep,
hits with `normal.y < 0.35` ignored, centre ray only as a fallback.

This **strengthens** the conclusion above rather than changing it. The sphere meets a proud
casting's raised top or rim on approach, so the wheel reads its surface while over it and the
suspension takes the 6-10 mm bump — which is what you want. For genuinely coplanar colliders the
sweep distances tie exactly as a ray would, so the hazard is unchanged.

Its one refinement: **"flush" needs a tolerance under a sphere sweep**, not an exact zero. The
+/-0.5 mm band used by `RoadWear/build/check_coplanar.py` is the right line.

**And a constraint on mountable edges — but it is HEIGHT, not face angle.**

My first version of this said a kerb a car should mount must not present a face steeper than the
0.35 cutoff, and recommended a chamfer. **That was wrong**, and the vehicle-physics session
corrected it with measurements.

`DriveOffOnTests` drives at a synthetic slab: a BoxCollider with a 90-degree face and a perfectly
sharp arris. **It passes.** The sphere never lands on the vertical face — it lands on the arris,
and the normal reported there points from the edge toward the sphere centre, not along the face.
Lowest wheel `normal.y` while crossing at 40 km/h at 85 degrees:

| Step | reported `normal.y` | cutoff |
|---|---|---|
| 0.15 m | **0.648** | 0.35 |
| 0.20 m | **0.591** | 0.35 |

So a near-vertical kerb with a sharp arris is fine, and a chamfer is not needed for mounting.

What actually fails is **step height**. The sphere is 0.85 x tyre radius, about 0.28 m on these
cars; it only touches the vertical face itself — `normal.y` near 0, a wall — once the arris is at
or above the sphere centre.

**Authoring constraint:**

- **<= 0.20 m:** any face angle, sharp arris fine. Measured.
- **0.20-0.25 m:** pending a further run.
- **above ~0.25 m:** treat as a wall unless chamfered.

A split kerb collider is fine **provided the `COL_kerb_*` face and top are one continuous mesh**,
with no gap for the sphere to drop into.

Note the 0.25 m failure originally recorded was **not the wheel** — it was the car's front
overhang striking the step first (BUG-152).

### A kerb climb changes surface partway up — that is intended

The kit's kerb pieces carry **two** colliders, not one: `COL_dry_asphalt_*_Gutter` from 0 to
0.450 m out, then `COL_kerb_*_Kerb` beyond it. A car crossing at an angle is therefore on
`dry_asphalt` for the gutter pan and switches to `kerb` partway up the climb. **This is correct
and deliberate** — it is the same split that makes a kerb face concrete and its gutter asphalt —
but it looks like a surface-flicker bug if you meet it without knowing, which is exactly the
symptom this document warns about elsewhere. The difference is that this is a *lateral* handover
between two colliders at different heights, not a *vertical* tie between two coplanar ones.

### CLAUDE.md's 0.05 m rule is about the junction, not the rise

A kerb face is ~150 mm tall and the total upstand ~172 mm. That does **not** violate the rule
that no vertical step above 0.05 m may exist between adjacent collider surfaces. That rule
governs the **junction between two colliders** — which here measures 0.000000 m — not a single
collider surface rising, which is what a kerb is for. A naive reading flags every kerb in the
city and wastes a day.

## Is it actually flush? Measure before assuming

I asserted that the RoadWear castings were flush and therefore must not take `metal`. **That was
wrong, and the infra session corrected it with measurements.** From its manifest:

| Casting | Stands proud |
|---|---|
| `VC_Manhole_Round_A` / `_B` | **+10 mm** (frame +6 mm, rim tapers to the road plane) |
| `VC_UtilityCover_Square` | **+10 mm** |
| `VC_StormDrain_Grate_Road` | **+6 mm** |

A collider 6–10 mm above the road plane is **not** coplanar with it, so there is no contact tie
and no flicker. `metal` is correct for all four, and they now use it.

The expansion joint strip genuinely is flush, and it ships with **no collider** — the road
underneath carries surface and height, which is the right treatment for that case.

**The rule, stated properly:** the hazard is coplanarity, not category. A casting is not "a
flush thing" by virtue of being a casting. Before giving a drive-over collider a surface that
differs from the ground beneath it, **check its actual height above the road**:

- **Coplanar (0 mm):** no own collider, or match the ground's surface. The tie is real.
- **Proud by a few mm or more:** its own collider and its own true surface. The contact test has an
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

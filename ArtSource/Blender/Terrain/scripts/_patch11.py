"""Manifest update for the third impostor card. Run with plain python, then delete.

Note: the template is %-formatted and every literal percent in it is already escaped as %%, so any
new prose added here must do the same.
"""
import os

HERE = os.path.dirname(os.path.abspath(__file__))
p = os.path.join(HERE, "make_manifest_terrain.py")
s = open(p, encoding="utf-8").read()

PAIRS = [
    ("""**Chosen: a 2-card cross billboard with two separately baked views and a real baked normal map.
Not octahedral.** The reasoning, stated plainly because the brief asked for it:""",
     """**Chosen: a 3-card impostor - two crossed vertical cards plus one horizontal card - each with its
own separately baked view and a real baked normal map. Not octahedral.** The reasoning, stated
plainly because the brief asked for it:"""),

    ("""Each card carries its own baked view (0 deg and 90 deg azimuth), so two of the four cardinal
directions are exactly right rather than interpolated.""",
     """Each card carries its own baked view - 0 deg azimuth, 90 deg azimuth, and straight down - so two
of the four cardinal directions and the overhead view are exactly right rather than interpolated.

The horizontal card sits at the canopy's **alpha-weighted centroid**, measured from the side view
with the coverage weighted by its square. Plain alpha weighting drags the centroid down the trunk:
it put the royal palm's card at 11.7 m, below a crown that starts at 14 m. Squaring makes a
metres-wide crown row dominate a centimetres-wide trunk row, which puts the card in the foliage
where it belongs. The top camera looks down on the ORIGIN rather than the bounding-box centre, so
all three cards share the asset's pivot."""),

    ("""- **Looking down.** The bake is a horizontal view. From a helicopter or a clifftop road looking
  down at 30 degrees or more, the cards foreshorten to slivers and the crown vanishes. D4 and D8
  both have clifftop roads, so this will be seen. If it matters, the fix is a third horizontal card
  (2 more triangles) baked from a top view - say the word and it is an afternoon.""",
     """- **Looking down: FIXED, at a cost of 2 triangles.** Two vertical cards foreshorten to slivers
  from above and the crown vanishes - which is precisely the D4 and D8 clifftop-road case, where a
  player can sit still and look down at a forest. The third card is a horizontal quad at the canopy
  centroid carrying a baked top view. It is edge-on and effectively invisible from ground level
  (confirmed in the review sheets), so it costs nothing at the common viewing angle.
- **The horizontal card at a shallow angle.** Around 15-30 degrees of elevation the flat card is
  neither edge-on nor face-on and can read as a faint disc through the crown. The two vertical
  cards hide most of it and it is a far smaller error than the collapsed crown it replaces, but it
  is the new worst case and it is the reason not to switch nearer than the table says."""),

    ("""Five impostors, 4 triangles each at LOD0. One 2048 atlas, three maps - about 5.5 MB as BC7 on PC,
2.8 MB as ASTC 6x6.""",
     """Six impostors, **6 triangles each at LOD0** (3 cards), 2 at LOD1 and LOD2. One 2048 atlas,
18 tiles, 1792 of 2048 rows used, three maps - about 5.5 MB as BC7 on PC, 2.8 MB as ASTC 6x6."""),

    ("""LOD1/LOD2: a single card on the 0 deg view (2 tris).""",
     """LOD1/LOD2: a single card on the 0 deg view (2 tris)."""),
]

for a, b in PAIRS:
    assert a in s, "anchor not found: %r" % a[:70]
    s = s.replace(a, b)
open(p, "w", encoding="utf-8").write(s)
print("manifest template updated for the third card")

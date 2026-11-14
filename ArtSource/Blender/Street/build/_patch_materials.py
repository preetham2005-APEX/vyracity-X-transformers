"""Street set material renames, per MATERIAL_REGISTER_ALL_SETS.md (the four-slot split).

VC_Metal_Galv     -> VC_Steel_Galv    bare galvanised steel, metallic 1.0
VC_Metal_Dark     -> VC_Steel_Dark    PAINTED steel, metallic 0.0
VC_Metal_CastIron -> VC_Iron_Painted  PAINTED cast iron, metallic 0.0

NOT VC_Iron_Cast: that slot is BARE cast iron at metallic 1.0 (manhole covers, grates). The ornate lamp
standards in this set are painted castings; sending them to bare iron would put a near-black mirror on a
lamp standard in wet night reflections, which is the failure the split exists to prevent.

Writes files only - runs no Blender.
"""
import io
import os
import sys

S = os.path.dirname(os.path.abspath(__file__))

# --- vc_street_lib.py: rename and re-value the three slots
p = os.path.join(S, "vc_street_lib.py")
s = io.open(p, encoding="utf-8").read()
EDITS = [
    ('    "VC_Metal_Galv":         ((0.33, 0.34, 0.345), 1.0, 0.55, None, 0.0),',
     '    "VC_Steel_Galv":         ((0.560, 0.570, 0.580), 1.0, 0.44, None, 0.0),'),
    ('    "VC_Metal_Dark":         ((0.055, 0.057, 0.060), 1.0, 0.48, None, 0.0),',
     '    "VC_Steel_Dark":         ((0.060, 0.062, 0.065), 0.0, 0.45, None, 0.0),'),
    ('    "VC_Metal_CastIron":     ((0.035, 0.036, 0.040), 0.7, 0.52, None, 0.0),',
     '    "VC_Iron_Painted":       ((0.040, 0.040, 0.042), 0.0, 0.45, None, 0.0),'),
]
for a, b in EDITS:
    if a not in s:
        print("MISS in vc_street_lib.py:", a.strip()[:60])
        sys.exit(1)
    s = s.replace(a, b, 1)
io.open(p, "w", encoding="utf-8").write(s)
print("  vc_street_lib.py: 3 slots renamed and re-valued")

# --- every other reference in the builder and the manifest generator
RENAME = [("VC_Metal_CastIron", "VC_Iron_Painted"),
          ("VC_Metal_Dark", "VC_Steel_Dark"),
          ("VC_Metal_Galv", "VC_Steel_Galv")]
for fname in ("build_street.py", "make_manifest.py"):
    p = os.path.join(S, fname)
    s = io.open(p, encoding="utf-8").read()
    counts = []
    for old, new in RENAME:
        n = s.count(old)
        if n:
            s = s.replace(old, new)
        counts.append("%s->%s x%d" % (old.replace("VC_Metal_", ""), new.split("_", 1)[1], n))
    io.open(p, "w", encoding="utf-8").write(s)
    print("  %s: %s" % (fname, ", ".join(counts)))

"""Build every Veyra road-infrastructure prop: one .blend and one FBX per prop.

Run headless:
    "D:\\New folder\\blender.exe" -b --factory-startup -P build_all.py
"""
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)

import vc_infra_lib as lib          # noqa: E402
import props_overhead               # noqa: E402
import props_road                   # noqa: E402

INFRA = os.path.abspath(os.path.join(HERE, ".."))
EXPORT = os.path.join(INFRA, "export")


def main():
    os.makedirs(EXPORT, exist_ok=True)
    results = []
    for fn in props_road.BUILDERS + props_overhead.BUILDERS:
        lib.reset_scene()
        meta = fn()
        col = meta["collision"]
        cols = col if isinstance(col, list) else ([col] if col else [])
        children = list(meta["lods"]) + cols
        root = lib.make_root(meta["name"], children)
        tris = [lib.tri_count(o) for o in meta["lods"]]
        col_tris = sum(lib.tri_count(o) for o in cols)
        mats = sorted({m.name for o in children for m in o.data.materials if m})
        dims = []
        for o in meta["lods"][:1]:
            dims = [round(v, 3) for v in o.dimensions]
        lib.save_and_export(meta["name"], root, children, INFRA, EXPORT)
        results.append(dict(name=meta["name"], tris=tris, collision_tris=col_tris,
                            collision=" + ".join(o.name for o in cols) if cols else None,
                            collision_type=meta["collision_type"], pivot=meta["pivot"],
                            notes=meta["notes"], materials=mats, dims_lod0=dims))
        print("[VC] %-34s LOD %s  collision %s" % (meta["name"], tris, col_tris))
    with open(os.path.join(HERE, "build_stats.json"), "w") as fh:
        json.dump(results, fh, indent=1)
    print("[VC] built %d props" % len(results))


main()

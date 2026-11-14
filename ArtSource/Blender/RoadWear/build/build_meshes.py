"""Build the Veyra road-surface detail meshes: one .blend and one FBX per prop."""
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)

import props_roadwear              # noqa: E402  (adds the Infra library to sys.path)
import vc_infra_lib as lib         # noqa: E402

ROOT = os.path.abspath(os.path.join(HERE, ".."))
EXPORT = os.path.join(ROOT, "export")


def main():
    os.makedirs(EXPORT, exist_ok=True)
    results = []
    for fn in props_roadwear.BUILDERS:
        lib.reset_scene()
        meta = fn()
        children = list(meta["lods"]) + ([meta["collision"]] if meta["collision"] else [])
        root = lib.make_root(meta["name"], children)
        tris = [lib.tri_count(o) for o in meta["lods"]]
        col_tris = lib.tri_count(meta["collision"]) if meta["collision"] else 0
        col_top = max(((meta["collision"].matrix_world @ v.co).z
                       for v in meta["collision"].data.vertices)) if meta["collision"] else 0.0
        mats = sorted({m.name for o in children for m in o.data.materials if m})
        dims = [round(v, 3) for v in meta["lods"][0].dimensions]
        lib.save_and_export(meta["name"], root, children, ROOT, EXPORT)
        results.append(dict(name=meta["name"], tris=tris, collision_tris=col_tris,
                            collision=meta["collision"].name if meta["collision"] else None,
                            collision_type=meta["collision_type"],
                            collision_top_m=round(col_top, 4),
                            pivot=meta["pivot"], notes=meta["notes"], materials=mats,
                            dims_lod0=dims))
        print("[RW] %-30s LOD %s  collision %d  top %+.3f m"
              % (meta["name"], tris, col_tris, col_top))
    with open(os.path.join(HERE, "mesh_stats.json"), "w") as fh:
        json.dump(results, fh, indent=1)
    print("[RW] built %d meshes" % len(results))


main()

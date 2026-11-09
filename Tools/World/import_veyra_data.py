"""Copy the Veyra world-design data into the Unity project (playbook card S3).

Copies (never moves) from the read-only design package and verifies every file by SHA-256.
Re-runnable: identical files are skipped; a destination that differs from the source is an
error unless --update is given (then it is overwritten and reported).

Usage:
    python -I Tools/World/import_veyra_data.py [--src <VEYRA_CITY_WORLD_DESIGN>] [--project <E:/VeyraCity>]
                                              [--update] [--check]
    --check   verify only (no copying); exit 1 if anything is missing or differs.

Writes Logs/S3/import_manifest.csv (dest, source, bytes, sha256, action).
"""
import argparse
import csv
import glob
import hashlib
import os
import shutil
import sys

DEFAULT_SRC = r"E:\PROJECT BANGERRR\VEYRA_CITY_WORLD_DESIGN"
DEFAULT_PROJECT = r"E:\VeyraCity"

# (source glob relative to the package, destination folder relative to the project, keep_subpath)
RULES = [
    ("06_DESIGN_DOCUMENTATION/location_registry.csv", "Assets/_Veyra/Data", False),
    ("08_3D_ASSETS/roads/road_network_geometry.json", "Assets/_Veyra/Data", False),
    ("08_3D_ASSETS/placement_data/*.csv", "Assets/_Veyra/Data", False),
    ("09_ART_PASS_BENCHMARK/assets/crown_downtown_building_placements.csv", "Assets/_Veyra/Data", False),
    ("08_3D_ASSETS/terrain/terrain_tiles_manifest.json", "Assets/_Veyra/Data", False),
    ("08_3D_ASSETS/terrain/tiles_raw16/*.raw", "ArtSource/VeyraData/terrain/tiles_raw16", False),
    ("08_3D_ASSETS/terrain/*.png", "ArtSource/VeyraData/terrain", False),
    ("01_MASTER_MAP/*.png", "Assets/_Veyra/Art/Textures/Maps", False),
    ("06_DESIGN_DOCUMENTATION/*", "Docs/design/06_DESIGN_DOCUMENTATION", False),
    ("09_ART_PASS_BENCHMARK/BENCHMARK_REPORT.md", "Docs/design/09_ART_PASS_BENCHMARK", False),
    ("09_ART_PASS_BENCHMARK/renders/*", "Docs/design/09_ART_PASS_BENCHMARK/renders", False),
]

EXPECTED_COUNTS = {
    "08_3D_ASSETS/placement_data/*.csv": 16,
    "08_3D_ASSETS/terrain/tiles_raw16/*.raw": 48,
    "08_3D_ASSETS/terrain/*.png": 4,
    "01_MASTER_MAP/*.png": 3,
    "06_DESIGN_DOCUMENTATION/*": 8,
    "09_ART_PASS_BENCHMARK/renders/*": 14,
}


def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as fh:
        for block in iter(lambda: fh.read(1 << 20), b""):
            h.update(block)
    return h.hexdigest()


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--src", default=DEFAULT_SRC)
    ap.add_argument("--project", default=DEFAULT_PROJECT)
    ap.add_argument("--update", action="store_true")
    ap.add_argument("--check", action="store_true")
    a = ap.parse_args()

    if not os.path.isdir(a.src):
        sys.exit("Design package not found: %s (unzip VEYRA_CITY_WORLD_DESIGN.zip first)" % a.src)

    rows, errors = [], []
    for pattern, dest_rel, _ in RULES:
        matches = sorted(p for p in glob.glob(os.path.join(a.src, pattern)) if os.path.isfile(p))
        want = EXPECTED_COUNTS.get(pattern, 1)
        if len(matches) != want:
            errors.append("%s: expected %d file(s), found %d" % (pattern, want, len(matches)))
        for src in matches:
            dest_dir = os.path.join(a.project, dest_rel)
            dest = os.path.join(dest_dir, os.path.basename(src))
            src_hash = sha256(src)
            size = os.path.getsize(src)
            if os.path.exists(dest):
                dest_hash = sha256(dest)
                if dest_hash == src_hash:
                    action = "identical"
                elif a.update and not a.check:
                    shutil.copy2(src, dest)
                    action = "updated"
                else:
                    action = "DIFFERS"
                    errors.append("%s differs from %s (use --update to overwrite)" % (dest, src))
            elif a.check:
                action = "MISSING"
                errors.append("missing: %s" % dest)
            else:
                os.makedirs(dest_dir, exist_ok=True)
                shutil.copy2(src, dest)
                action = "copied"
            if action in ("copied", "updated") and sha256(dest) != src_hash:
                errors.append("hash mismatch after copy: %s" % dest)
                action += "+BADHASH"
            rows.append((os.path.relpath(dest, a.project).replace("\\", "/"),
                         os.path.relpath(src, a.src).replace("\\", "/"), size, src_hash, action))

    log_dir = os.path.join(a.project, "Logs", "S3")
    os.makedirs(log_dir, exist_ok=True)
    with open(os.path.join(log_dir, "import_manifest.csv"), "w", newline="", encoding="utf-8") as fh:
        w = csv.writer(fh)
        w.writerow(["dest", "source", "bytes", "sha256", "action"])
        w.writerows(rows)

    counts = {}
    for r in rows:
        counts[r[4]] = counts.get(r[4], 0) + 1
    total_mb = sum(r[2] for r in rows) / 1048576
    print("files: %d (%.1f MB)  %s" % (len(rows), total_mb, ", ".join("%s %d" % kv for kv in sorted(counts.items()))))
    for e in errors:
        print("ERROR:", e)
    sys.exit(1 if errors else 0)


if __name__ == "__main__":
    main()

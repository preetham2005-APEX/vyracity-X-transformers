"""Vegetation kit assignment (foliage/peds session, 2026-11-14).

    python -I Tools/vegetation_kit_assign.py            (from E:/VeyraCity)

Maps every row of the Veyra vegetation placement data (WorldSource/placement/vegetation_D*.csv: the four generic types
pine_tree_14m, deciduous_tree_10m, bush_2m, palm_tree_12m) onto the original vegetation kit
(ArtSource/Blender/Vegetation + Vegetation2, see their MANIFEST.md). The data stays the authority:
  - position (unity_x, unity_y, unity_z) is copied unchanged, the yaw is CityBuilder's hash of the position (the data has
    no heading), the data scale is kept but compressed towards 1 (the kit models already have real heights);
  - only the species is chosen, per district, with a hash of the position (deterministic, stable across runs), following
    the kit manifests (royal palm heights ~2:3:2 with the 16 m at most 1 in 4; spruce/fir ~60/40 in D4; redwood
    22/34/46 m ~50/30/1-in-10 in D8; broadleaf spreading/upright ~1:1; snags 1 per ~30 conifers).
Road check (CLAUDE.md "nothing blocks a road"): every trunk is tested against every carriageway of
Assets/_Veyra/Resources/World/roads.json (the runtime road table, Unity space, with widths):
  - trunk inside half-width + kerb margin (1.0 m) + trunk radius  -> the row is dropped (reported);
  - a wide crown whose low foliage would hang over the carriageway -> a narrow species of the same type is used instead.
Writes
  Assets/_Veyra/Data/Derived/vegetation_kit_placements.csv   (derived output, outside the validator scope) model,unity_x,unity_y,unity_z,yaw_deg,scale,source_type,district
  Logs/Foliage/kit_assignment.txt                     counts per district and model, dropped/narrowed rows
  Logs/Foliage/road_conflicts.csv                     every exception to the data: source file + row, position, road, distance
"""
import csv
import glob
import json
import math
import os
import collections

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
SRC = os.path.join(ROOT, "WorldSource", "placement")
ROADS = os.path.join(ROOT, "Assets", "_Veyra", "Resources", "World", "roads.json")
OUT = os.path.join(ROOT, "Assets", "_Veyra", "Data", "Derived", "vegetation_kit_placements.csv")
LOG = os.path.join(ROOT, "Logs", "Foliage", "kit_assignment.txt")
CONFLICTS = os.path.join(ROOT, "Logs", "Foliage", "road_conflicts.csv")
KERB_MARGIN = 1.0      # m beyond the carriageway edge that a trunk must stay clear of
CLEARANCE = 4.6        # m: foliage lower than this may not hang over a carriageway (trucks, buses)

# model: (trunk radius m, crown radius m, height of the lowest foliage m) from the MANIFEST sizes
KIT = {
    "VC_Palm_Royal_8m": (0.25, 2.3, 6.0), "VC_Palm_Royal_12m": (0.3, 3.4, 9.0), "VC_Palm_Royal_16m": (0.35, 4.4, 12.5),
    "VC_Palm_Fan_Short": (0.25, 1.9, 2.4), "VC_Palm_Fan_Tall": (0.25, 2.7, 4.0),
    "VC_Tree_Broadleaf_Spreading": (0.35, 5.0, 2.8), "VC_Tree_Broadleaf_Upright": (0.3, 4.2, 3.2),
    "VC_Tree_Cypress_6m": (0.15, 0.8, 0.6), "VC_Tree_Cypress_9m": (0.2, 1.2, 0.8),
    "VC_Tree_Pine_Tall": (0.3, 3.5, 4.5), "VC_Tree_Pine_Umbrella": (0.35, 4.1, 5.5),
    "VC_Shrub_DryScrub": (0.0, 0.8, 0.0), "VC_Shrub_GreenBush": (0.0, 1.0, 0.0), "VC_Shrub_VergeTuft": (0.0, 0.6, 0.0),
    "VC_Tree_Spruce_Alpine": (0.3, 2.7, 1.2), "VC_Tree_Fir_Alpine": (0.35, 4.1, 1.5), "VC_Tree_Snag_Weathered": (0.3, 1.1, 99.0),
    "VC_Shrub_MountainScrub": (0.0, 1.0, 0.0), "VC_Tree_VolunteerSapling": (0.1, 1.4, 1.0),
    "VC_Tree_Redwood_22m": (0.6, 4.4, 6.0), "VC_Tree_Redwood_34m": (0.95, 6.1, 9.0), "VC_Tree_Redwood_46m": (1.2, 8.1, 12.0),
}
# narrow stand-ins when a crown would hang low over a carriageway
NARROW = {"VC_Tree_Broadleaf_Spreading": "VC_Tree_Broadleaf_Upright", "VC_Tree_Broadleaf_Upright": "VC_Tree_Cypress_9m",
          "VC_Palm_Fan_Tall": "VC_Palm_Royal_8m", "VC_Palm_Fan_Short": "VC_Palm_Royal_8m", "VC_Tree_Pine_Umbrella": "VC_Tree_Pine_Tall",
          "VC_Tree_Pine_Tall": "VC_Tree_Cypress_9m", "VC_Tree_Fir_Alpine": "VC_Tree_Spruce_Alpine", "VC_Tree_Spruce_Alpine": "VC_Tree_Cypress_9m",
          "VC_Tree_Redwood_46m": "VC_Tree_Redwood_22m", "VC_Tree_Redwood_34m": "VC_Tree_Redwood_22m", "VC_Tree_Redwood_22m": "VC_Tree_Cypress_9m"}

W = lambda **k: list(k.items())
MIX = {
    "pine_tree_14m": {
        "D4": W(VC_Tree_Spruce_Alpine=55, VC_Tree_Fir_Alpine=37, VC_Tree_Pine_Tall=5, VC_Tree_Snag_Weathered=3),
        "D8": W(VC_Tree_Redwood_22m=26, VC_Tree_Redwood_34m=16, VC_Tree_Redwood_46m=4, VC_Tree_Pine_Tall=34, VC_Tree_Pine_Umbrella=17, VC_Tree_Snag_Weathered=3),
        "*": W(VC_Tree_Pine_Tall=70, VC_Tree_Pine_Umbrella=30),
    },
    "deciduous_tree_10m": {
        "D1": W(VC_Tree_Broadleaf_Upright=70, VC_Tree_Broadleaf_Spreading=30),
        "D2": W(VC_Tree_Broadleaf_Spreading=45, VC_Tree_Broadleaf_Upright=35, VC_Tree_Cypress_9m=12, VC_Tree_Cypress_6m=8),
        "D4": W(VC_Tree_Broadleaf_Spreading=45, VC_Tree_Broadleaf_Upright=35, VC_Tree_VolunteerSapling=20),
        "D5": W(VC_Tree_Broadleaf_Spreading=48, VC_Tree_Broadleaf_Upright=42, VC_Tree_Cypress_9m=10),
        "D8": W(VC_Tree_Broadleaf_Spreading=55, VC_Tree_Broadleaf_Upright=35, VC_Tree_VolunteerSapling=10),
        "*": W(VC_Tree_Broadleaf_Spreading=50, VC_Tree_Broadleaf_Upright=50),
    },
    "bush_2m": {
        "D1": W(VC_Shrub_GreenBush=85, VC_Shrub_VergeTuft=15),
        "D2": W(VC_Shrub_GreenBush=45, VC_Shrub_DryScrub=40, VC_Shrub_VergeTuft=15),
        "D5": W(VC_Shrub_GreenBush=60, VC_Shrub_DryScrub=25, VC_Shrub_VergeTuft=15),
        "D8": W(VC_Shrub_GreenBush=50, VC_Shrub_MountainScrub=30, VC_Shrub_DryScrub=20),
        "*": W(VC_Shrub_GreenBush=60, VC_Shrub_DryScrub=40),
    },
    "palm_tree_12m": {
        "*": W(VC_Palm_Royal_8m=28, VC_Palm_Royal_12m=44, VC_Palm_Royal_16m=18, VC_Palm_Fan_Tall=10),
    },
}


def h32(x, z, salt):
    """The same integer hash as InstancedPropRenderer.Hash, on centimetre-snapped positions plus a salt."""
    a = (int(math.floor(x * 4)) * 73856093) & 0xFFFFFFFF
    b = (int(math.floor(z * 4)) * 19349663) & 0xFFFFFFFF
    v = (a ^ b ^ (salt * 2654435761)) & 0xFFFFFFFF
    v ^= v >> 13
    v = (v * 0x5BD1E995) & 0xFFFFFFFF
    v ^= v >> 15
    return v


def pick(mix, x, z):
    total = sum(w for _, w in mix)
    r = (h32(x, z, 17) % 10000) / 10000.0 * total
    for m, w in mix:
        r -= w
        if r < 0:
            return m
    return mix[-1][0]


class RoadIndex:
    def __init__(self, path, cell=50.0):
        data = json.load(open(path, encoding="utf-8"))["roads"]
        self.cell = cell
        self.grid = collections.defaultdict(list)
        self.segs = []
        for r in data:
            pts = r["points"] if isinstance(r["points"], list) else json.loads(r["points"])
            half = float(r["width"]) * 0.5
            p = [(pts[i], pts[i + 1], pts[i + 2]) for i in range(0, len(pts), 3)]
            closed = str(r.get("closed")) == "True" or r.get("closed") is True
            n = len(p) if closed else len(p) - 1
            for i in range(n):
                a, b = p[i], p[(i + 1) % len(p)]
                s = len(self.segs)
                self.segs.append((a, b, half, r["id"]))
                pad = half + 12.0
                for cx in range(int(math.floor((min(a[0], b[0]) - pad) / cell)), int(math.floor((max(a[0], b[0]) + pad) / cell)) + 1):
                    for cz in range(int(math.floor((min(a[2], b[2]) - pad) / cell)), int(math.floor((max(a[2], b[2]) + pad) / cell)) + 1):
                        self.grid[(cx, cz)].append(s)

    def nearest(self, x, y, z):
        """(edge distance m: negative inside the carriageway, road id, road height at the closest point)."""
        best = (1e9, None, 0.0)
        for s in self.grid.get((int(math.floor(x / self.cell)), int(math.floor(z / self.cell))), ()):
            a, b, half, rid = self.segs[s]
            abx, abz = b[0] - a[0], b[2] - a[2]
            l2 = abx * abx + abz * abz or 1e-9
            t = max(0.0, min(1.0, ((x - a[0]) * abx + (z - a[2]) * abz) / l2))
            px, pz = a[0] + abx * t, a[2] + abz * t
            ry = a[1] + (b[1] - a[1]) * t
            if abs(ry - y) > 6.0:      # a bridge or tunnel at another level does not block this tree
                continue
            d = math.hypot(x - px, z - pz) - half
            if d < best[0]:
                best = (d, rid, ry)
        return best


def main():
    roads = RoadIndex(ROADS)
    rows, dropped, narrowed = [], [], collections.Counter()
    conflicts = []
    counts = collections.Counter()
    for f in sorted(glob.glob(os.path.join(SRC, "vegetation_D*.csv"))):
        district = os.path.basename(f).split("_")[1]
        with open(f, encoding="utf-8") as fh:
            for row_no, row in enumerate(csv.DictReader(fh), start=2):   # file line number (line 1 is the header)
                t = row["type"]
                if t not in MIX:
                    continue
                x, y, z = float(row["unity_x"]), float(row["unity_y"]), float(row["unity_z"])
                s = float(row.get("scale") or 1.0)
                mix = MIX[t].get(district) or MIX[t]["*"]
                model = pick(mix, x, z)
                d, rid, _ = roads.nearest(x, y, z)
                trunk, crown, low = KIT[model]
                if trunk > 0 and d < KERB_MARGIN + trunk * s:
                    dropped.append((district, t, model, x, y, z, rid, round(d, 2)))
                    conflicts.append((os.path.basename(f), row_no, "dropped", t, model, "", x, y, z, rid, round(d, 2)))
                    continue
                first = model
                guard = 0
                while d < crown * s and low * s < CLEARANCE and model in NARROW and guard < 4:
                    model = NARROW[model]
                    trunk, crown, low = KIT[model]
                    narrowed[(district, t)] += 1
                    guard += 1
                if model != first:
                    conflicts.append((os.path.basename(f), row_no, "narrowed", t, first, model, x, y, z, rid, round(d, 2)))
                yaw = abs(x * 13.37 + z * 7.11) % 360.0           # CityBuilder.BuildProps' yaw for rows without a heading
                scale = round(1.0 + (s - 1.0) * 0.6, 3)            # kit models have real heights: keep 60 % of the data's spread
                rows.append((model, x, y, z, round(yaw, 1), scale, t, district))
                counts[(district, model)] += 1
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, "w", newline="\n", encoding="utf-8") as fh:
        w = csv.writer(fh, lineterminator="\n")
        w.writerow(["model", "unity_x", "unity_y", "unity_z", "yaw_deg", "scale", "source_type", "district"])
        w.writerows(rows)
    os.makedirs(os.path.dirname(LOG), exist_ok=True)
    with open(CONFLICTS, "w", newline="\n", encoding="utf-8") as fh:
        w = csv.writer(fh, lineterminator="\n")
        w.writerow(["source_file", "source_line", "action", "data_type", "kit_model", "replaced_by", "unity_x", "unity_y", "unity_z",
                    "road_id", "edge_distance_m (negative = inside the carriageway)"])
        w.writerows(conflicts)
    lines = [f"Vegetation kit assignment: {len(rows)} placed, {len(dropped)} dropped (trunk within {KERB_MARGIN} m of a carriageway), "
             f"{sum(narrowed.values())} narrowed (low crown over a carriageway)"]
    for (dist, m), n in sorted(counts.items()):
        lines.append(f"  {dist} {m}: {n}")
    for k, n in sorted(narrowed.items()):
        lines.append(f"  narrowed {k}: {n}")
    lines.append("dropped rows (district, data type, model, unity x y z, road, edge distance m):")
    lines += [f"  {r}" for r in dropped[:400]]
    if len(dropped) > 400:
        lines.append(f"  ... {len(dropped) - 400} more")
    open(LOG, "w", encoding="utf-8").write("\n".join(lines) + "\n")
    print("\n".join(lines[:60]))


if __name__ == "__main__":
    main()

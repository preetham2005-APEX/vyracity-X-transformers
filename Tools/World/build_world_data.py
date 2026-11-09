"""
VEYRA CITY world data builder.

Reads the validated world design package (VEYRA_CITY_WORLD_DESIGN) and writes game-ready data:

  WorldSource/terrain/terrain_<cell>.raw   16-bit RAW tiles with road corridors lowered 0.25 m under the road
                                           mesh, so the road surface never z-fights with the terrain
  WorldSource/terrain/holes_<cell>.bin     512x512 bytes, 1 = terrain hole (tunnel portals)
  WorldSource/meshes/<district>.vmesh      roads, structures, buildings and water from the district
                                           blockout GLBs, converted to Unity coordinates
  WorldSource/placement/*.csv              vegetation and street furniture placements (copied)
  Assets/_Veyra/Resources/World/*.json     roads, race routes, locations and districts for runtime use

Unity coordinates: X = x - 4000, Y = elevation, Z = y - 3000 (x east, y north, metres from the SW corner).

Usage:  python build_world_data.py <design_package_dir> <unity_project_dir>
"""
import array
import csv
import json
import math
import os
import re
import shutil
import struct
import sys

CLASS_WIDTH = {"MW": 32.0, "EX": 24.0, "HW": 18.0, "AR": 20.0, "CO": 11.0, "MT": 7.5,
               "RU": 7.0, "GR": 5.5, "SV": 12.0, "RW": 45.0}
CLASS_LANES = {"MW": 3, "EX": 2, "HW": 2, "AR": 2, "CO": 1, "MT": 1, "RU": 1, "GR": 1, "SV": 1, "RW": 2}
CLASS_SPEED = {"MW": 110, "EX": 100, "HW": 90, "AR": 60, "CO": 40, "MT": 60, "RU": 70, "GR": 50, "SV": 40, "RW": 120}
URBAN = {"AR", "CO", "SV"}
STRUCT_CODE = {"": 0, "bridge_river": 1, "bridge_sea": 1, "tunnel": 2, "elevated": 3, "viaduct": 3}


def to_unity(x, y, z):
    return (x - 4000.0, z, y - 3000.0)


# ----------------------------------------------------------------------------------------------
# Roads
# ----------------------------------------------------------------------------------------------
class Road:
    def __init__(self, r):
        self.id = r["id"]
        self.name = r["name"]
        self.cls = r["cls"]
        self.closed = bool(r["closed"])
        self.pts = [tuple(p) for p in r["profile_xyz_every_4m"]]
        self.struct = [STRUCT_CODE.get(s, 0) for s in r["profile_structure"]]
        self.width = CLASS_WIDTH.get(self.cls, 10.0)
        self.s = [0.0]
        for i in range(1, len(self.pts)):
            a, b = self.pts[i - 1], self.pts[i]
            self.s.append(self.s[-1] + math.hypot(b[0] - a[0], b[1] - a[1]))
        self.length = self.s[-1] + (self.seg_len(len(self.pts) - 1, 0) if self.closed else 0.0)

    def seg_len(self, i, j):
        a, b = self.pts[i], self.pts[j]
        return math.hypot(b[0] - a[0], b[1] - a[1])

    def nearest(self, x, y):
        best, bi = 1e18, 0
        for i, p in enumerate(self.pts):
            d = (p[0] - x) ** 2 + (p[1] - y) ** 2
            if d < best:
                best, bi = d, i
        return bi, math.sqrt(best)

    def walk(self, i0, i1, direction):
        """Indices from i0 to i1 inclusive, stepping by direction (+1/-1), wrapping on closed roads."""
        n = len(self.pts)
        out = [i0]
        i = i0
        guard = 0
        while i != i1:
            i += direction
            if self.closed:
                i %= n
            elif i < 0 or i >= n:
                return None
            out.append(i)
            guard += 1
            if guard > n + 2:
                return None
        return out

    def path_len(self, idx):
        return sum(self.seg_len(idx[k - 1], idx[k]) for k in range(1, len(idx)))


class Grid:
    def __init__(self, road, cell=20.0):
        self.cell = cell
        self.road = road
        self.map = {}
        for i, p in enumerate(road.pts):
            self.map.setdefault((int(p[0] // cell), int(p[1] // cell)), []).append(i)

    def near(self, x, y, z, max_h, max_v):
        cx, cy = int(x // self.cell), int(y // self.cell)
        best, bi = 1e18, -1
        for gx in (cx - 1, cx, cx + 1):
            for gy in (cy - 1, cy, cy + 1):
                for i in self.map.get((gx, gy), ()):
                    p = self.road.pts[i]
                    if abs(p[2] - z) > max_v:
                        continue
                    d = math.hypot(p[0] - x, p[1] - y)
                    if d < best:
                        best, bi = d, i
        return (bi, best) if best <= max_h else (-1, best)


def junctions(a, b, grid_b):
    """Local closest-approach points between road a and road b: list of (index_on_a, index_on_b)."""
    hits = []
    for i, p in enumerate(a.pts):
        j, d = grid_b.near(p[0], p[1], p[2], 16.0, 4.0)
        if j >= 0:
            hits.append((i, j, d))
    clusters, cur = [], []
    for h in hits:
        if cur and h[0] - cur[-1][0] > 3:
            clusters.append(cur)
            cur = []
        cur.append(h)
    if cur:
        clusters.append(cur)
    # merge wrap-around cluster on closed roads
    if a.closed and len(clusters) > 1 and clusters[0][0][0] == 0 and clusters[-1][-1][0] == len(a.pts) - 1:
        clusters[0] = clusters[-1] + clusters[0]
        clusters.pop()
    return [min(c, key=lambda h: h[2])[:2] for c in clusters]


def build_route(route, roads, grids):
    names = route["roads"]
    kind = route["kind"]
    circuit = kind.lower().startswith("circuit") or "loop" in kind.lower()
    m = re.search(r"(\d+)\s+lap", kind)
    laps = int(m.group(1)) if m else 1
    target = float(route["length_m"])
    seq = [roads[n] for n in names]
    sx, sy = route["start"]
    start_idx, start_err = seq[0].nearest(sx, sy)

    # Single closed road circuit: the whole loop.
    if circuit and len(seq) == 1:
        r = seq[0]
        best = None
        for direction in (1, -1):
            end = (start_idx - direction) % len(r.pts)
            idx = r.walk(start_idx, end, direction)
            L = r.path_len(idx) + r.seg_len(idx[-1], idx[0])
            if best is None or abs(L - target) < abs(best[0] - target):
                best = (L, [(r, idx)])
        return finish_route(route, best[1], circuit, laps, target, start_err)

    steps = []  # per step: road and list of (entry->exit candidates)
    finish = route.get("finish")

    def options(road, entry, next_road, closing):
        """Candidate (exit_index_on_road, entry_index_on_next, idx_list) from entry."""
        res = []
        if next_road is None:
            if closing is not None:
                ends = [closing]
            else:
                fx, fy = finish
                ends = [road.nearest(fx, fy)[0]]
            for e in ends:
                for direction in (1, -1):
                    idx = road.walk(entry, e, direction)
                    if idx and len(idx) > 1:
                        res.append((e, None, idx))
            return res
        for (ia, ib) in junctions(road, next_road, grids[next_road.id]):
            for direction in (1, -1):
                idx = road.walk(entry, ia, direction)
                if idx and len(idx) > 2:
                    res.append((ia, ib, idx))
        return res

    order = list(seq)
    if circuit:
        order = order + [seq[0]]
    best = [None]

    def search(k, entry, acc, total):
        if best[0] is not None and total - target > target * 0.6:
            return
        road = order[k]
        last = k == len(order) - 1
        nxt = None if last else order[k + 1]
        closing = start_idx if (last and circuit) else None
        for (exit_i, next_entry, idx) in options(road, entry, nxt, closing):
            L = road.path_len(idx)
            acc2 = acc + [(road, idx)]
            if last:
                tot = total + L
                if best[0] is None or abs(tot - target) < abs(best[0][0] - target):
                    best[0] = (tot, acc2)
            else:
                search(k + 1, next_entry, acc2, total + L)

    search(0, start_idx, [], 0.0)
    if best[0] is None:
        raise RuntimeError(f"route {route['id']}: no path found")
    return finish_route(route, best[0][1], circuit, laps, target, start_err)


def finish_route(route, parts, circuit, laps, target, start_err):
    pts = []
    structs = []
    for road, idx in parts:
        for i in idx:
            p = road.pts[i]
            q = to_unity(*p)
            if pts and math.dist(pts[-1], q) < 0.5:
                continue
            pts.append(q)
            structs.append(road.struct[i])
    # resample every 4 m
    res, rs = [pts[0]], [structs[0]]
    carry = 0.0
    for i in range(1, len(pts)):
        a, b = pts[i - 1], pts[i]
        seg = math.dist(a, b)
        t = 4.0 - carry
        while t <= seg:
            f = t / seg
            res.append(tuple(a[k] + (b[k] - a[k]) * f for k in range(3)))
            rs.append(structs[i])
            t += 4.0
        carry = seg - (t - 4.0)
    length = sum(math.dist(res[i - 1], res[i]) for i in range(1, len(res)))
    if circuit:
        length += math.dist(res[-1], res[0])
    gaps = max(math.dist(res[i - 1], res[i]) for i in range(1, len(res)))
    err = abs(length - target) / target
    return {
        "id": route["id"], "name": route["name"], "kind": route["kind"], "district": route.get("district", ""),
        "circuit": circuit, "laps": laps, "designLength": target, "length": round(length, 1),
        "lengthError": round(err, 4), "maxGap": round(gaps, 2), "startError": round(start_err, 2),
        "roads": route["roads"],
        "points": [round(v, 2) for p in res for v in p],
        "structure": rs,
    }


# ----------------------------------------------------------------------------------------------
# Terrain
# ----------------------------------------------------------------------------------------------
def build_terrain(pkg, out_dir, roads):
    man = json.load(open(os.path.join(pkg, "08_3D_ASSETS/terrain/terrain_tiles_manifest.json")))
    res = 513
    spacing = 1000.0 / (res - 1)
    tiles = {}
    for t in man["tiles"]:
        data = array.array("H")
        with open(os.path.join(pkg, "08_3D_ASSETS/terrain", t["file"]), "rb") as f:
            data.frombytes(f.read())
        tiles[t["tile"]] = {"x0": t["world_x_m"][0], "y0": t["world_y_m"][0], "h": data,
                            "holes": bytearray(512 * 512)}

    def tile_at(x, y):
        col = int(x // 1000)
        row = 6 - int(y // 1000)
        if col < 0 or col > 7 or row < 1 or row > 6:
            return None
        return tiles.get("ABCDEFGH"[col] + str(row))

    def to_m(v):
        return -60.0 + v / 65535.0 * 560.0

    def to_v(m):
        return max(0, min(65535, int(round((m + 60.0) / 560.0 * 65535.0))))

    lowered = 0
    holes = 0
    for road in roads.values():
        n = len(road.pts)
        half = road.width * 0.5 + (3.6 if road.cls in URBAN else 0.8)
        segs = range(n if road.closed else n - 1)
        for i in segs:
            j = (i + 1) % n
            st = road.struct[i]
            a, b = road.pts[i], road.pts[j]
            if st in (1, 3):
                continue  # bridges / elevated: leave the ground alone
            minx, maxx = min(a[0], b[0]) - half, max(a[0], b[0]) + half
            miny, maxy = min(a[1], b[1]) - half, max(a[1], b[1]) + half
            dx, dy = b[0] - a[0], b[1] - a[1]
            L2 = dx * dx + dy * dy or 1e-6
            gx0, gx1 = int(math.floor(minx / spacing)), int(math.ceil(maxx / spacing))
            gy0, gy1 = int(math.floor(miny / spacing)), int(math.ceil(maxy / spacing))
            for gx in range(gx0, gx1 + 1):
                x = gx * spacing
                for gy in range(gy0, gy1 + 1):
                    y = gy * spacing
                    t = max(0.0, min(1.0, ((x - a[0]) * dx + (y - a[1]) * dy) / L2))
                    px, py = a[0] + dx * t, a[1] + dy * t
                    d = math.hypot(x - px, y - py)
                    if d > half:
                        continue
                    z = a[2] + (b[2] - a[2]) * t
                    # write into every tile that owns this vertex (shared borders)
                    for tx in {int(x // 1000), int((x - 1e-3) // 1000)}:
                        for ty in {int(y // 1000), int((y - 1e-3) // 1000)}:
                            if tx < 0 or tx > 7 or ty < 0 or ty > 5:
                                continue
                            tile = tiles.get("ABCDEFGH"[tx] + str(6 - ty))
                            if tile is None:
                                continue
                            lx = int(round((x - tile["x0"]) / spacing))
                            ly = int(round((y - tile["y0"]) / spacing))
                            if not (0 <= lx < res and 0 <= ly < res):
                                continue
                            k = ly * res + lx
                            cur = to_m(tile["h"][k])
                            if st == 2:
                                # tunnel: open a hole where the ground is only a little above the road (portals)
                                if cur > z + 1.0 and cur < z + 14.0 and d < road.width * 0.5 + 1.5:
                                    hx, hy = min(lx, 511), min(ly, 511)
                                    if not tile["holes"][hy * 512 + hx]:
                                        tile["holes"][hy * 512 + hx] = 1
                                        holes += 1
                                continue
                            target = z - 0.25
                            if cur > target:
                                tile["h"][k] = to_v(target)
                                lowered += 1
    os.makedirs(out_dir, exist_ok=True)
    for name, t in tiles.items():
        with open(os.path.join(out_dir, f"terrain_{name}.raw"), "wb") as f:
            f.write(t["h"].tobytes())
        with open(os.path.join(out_dir, f"holes_{name}.bin"), "wb") as f:
            f.write(bytes(t["holes"]))
    meta = {"resolution": res, "tileSize": 1000, "heightOffset": -60.0, "heightRange": 560.0,
            "rowsSouthToNorth": True, "tiles": [{"name": k, "x0": v["x0"] - 4000.0, "z0": v["y0"] - 3000.0}
                                                for k, v in sorted(tiles.items())]}
    json.dump(meta, open(os.path.join(out_dir, "terrain_meta.json"), "w"), indent=1)
    return lowered, holes


# ----------------------------------------------------------------------------------------------
# GLB -> vmesh
# ----------------------------------------------------------------------------------------------
def read_accessor(j, binbuf, idx):
    acc = j["accessors"][idx]
    bv = j["bufferViews"][acc["bufferView"]]
    comp = {5126: ("f", 4), 5125: ("I", 4), 5123: ("H", 2), 5121: ("B", 1)}[acc["componentType"]]
    ncomp = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4}[acc["type"]]
    count = acc["count"]
    off = bv.get("byteOffset", 0) + acc.get("byteOffset", 0)
    stride = bv.get("byteStride", 0)
    elem = comp[1] * ncomp
    if stride and stride != elem:
        raw = b"".join(binbuf[off + i * stride: off + i * stride + elem] for i in range(count))
    else:
        raw = binbuf[off: off + count * elem]
    arr = array.array(comp[0])
    arr.frombytes(raw)
    return arr, ncomp


def convert_glb(path, out_path, keep_nodes):
    data = open(path, "rb").read()
    jl = struct.unpack("<I", data[12:16])[0]
    j = json.loads(data[20:20 + jl])
    bl = struct.unpack("<I", data[20 + jl:24 + jl])[0]
    binbuf = data[28 + jl:28 + jl + bl]
    prims = []
    for node in j["nodes"]:
        nname = node.get("name", "")
        if "mesh" not in node or not any(k in nname for k in keep_nodes):
            continue
        for p in j["meshes"][node["mesh"]]["primitives"]:
            mat = j["materials"][p["material"]]["name"] if "material" in p else "default"
            pos, _ = read_accessor(j, binbuf, p["attributes"]["POSITION"])
            nrm, _ = read_accessor(j, binbuf, p["attributes"]["NORMAL"]) if "NORMAL" in p["attributes"] else (None, 0)
            uv, _ = read_accessor(j, binbuf, p["attributes"]["TEXCOORD_0"]) if "TEXCOORD_0" in p["attributes"] else (None, 0)
            ind, _ = read_accessor(j, binbuf, p["indices"])
            vc = len(pos) // 3
            # glTF (+X east, +Y up, -Z north) -> Unity (+X east, +Y up, +Z north): negate Z, flip winding
            for k in range(2, len(pos), 3):
                pos[k] = -pos[k]
            if nrm is not None:
                for k in range(2, len(nrm), 3):
                    nrm[k] = -nrm[k]
            ind = array.array("I", ind)
            for k in range(0, len(ind), 3):
                ind[k + 1], ind[k + 2] = ind[k + 2], ind[k + 1]
            prims.append((nname, mat, vc, pos, nrm, uv, ind))
    with open(out_path, "wb") as f:
        f.write(b"VMSH")
        f.write(struct.pack("<II", 1, len(prims)))
        for (nname, mat, vc, pos, nrm, uv, ind) in prims:
            for s in (nname, mat):
                b = s.encode("utf-8")
                f.write(struct.pack("<I", len(b)))
                f.write(b)
            f.write(struct.pack("<III", vc, len(ind), (1 if nrm is not None else 0) | (2 if uv is not None else 0)))
            f.write(array.array("f", pos).tobytes())
            if nrm is not None:
                f.write(array.array("f", nrm).tobytes())
            if uv is not None:
                f.write(array.array("f", uv).tobytes())
            f.write(ind.tobytes())
    return [(p[0], p[1], p[2], len(p[6]) // 3) for p in prims]


# ----------------------------------------------------------------------------------------------
def main():
    pkg = sys.argv[1]
    proj = sys.argv[2]
    src = os.path.join(proj, "WorldSource")
    res_dir = os.path.join(proj, "Assets", "_Veyra", "Resources", "World")
    os.makedirs(res_dir, exist_ok=True)
    geo = json.load(open(os.path.join(pkg, "08_3D_ASSETS/roads/road_network_geometry.json")))
    roads = {r["id"]: Road(r) for r in geo["roads"]}
    grids = {k: Grid(v) for k, v in roads.items()}

    # roads.json (runtime)
    rj = {"roads": []}
    for r in roads.values():
        rj["roads"].append({
            "id": r.id, "name": r.name, "cls": r.cls, "closed": r.closed, "width": r.width,
            "lanes": CLASS_LANES.get(r.cls, 1), "speedKmh": CLASS_SPEED.get(r.cls, 50), "length": round(r.length, 1),
            "points": [round(v, 2) for p in r.pts for v in to_unity(*p)],
            "structure": r.struct,
        })
    json.dump(rj, open(os.path.join(res_dir, "roads.json"), "w"), separators=(",", ":"))

    # routes.json
    routes = []
    report = []
    for rt in geo["race_routes"]:
        out = build_route(rt, roads, grids)
        routes.append(out)
        report.append(f"{out['id']:8s} {out['name']:30s} len {out['length']:8.1f} / {out['designLength']:7.0f}"
                      f" err {out['lengthError']*100:5.1f}%  maxGap {out['maxGap']:5.2f} m  startErr {out['startError']:5.1f} m  laps {out['laps']}")
    json.dump({"routes": routes}, open(os.path.join(res_dir, "routes.json"), "w"), separators=(",", ":"))

    # locations.json
    locs = []
    with open(os.path.join(pkg, "06_DESIGN_DOCUMENTATION/location_registry.csv"), encoding="utf-8") as f:
        for row in csv.DictReader(f):
            try:
                locs.append({"id": row["id"], "name": row["name"], "category": row["category"],
                             "district": row["district_id"], "x": float(row["unity_x"]), "y": float(row["unity_y"]),
                             "z": float(row["unity_z"]), "road": row["road"], "crew": row.get("crew", ""),
                             "description": row["description"]})
            except (ValueError, KeyError):
                pass
    json.dump({"locations": locs}, open(os.path.join(res_dir, "locations.json"), "w"), separators=(",", ":"))

    # districts.json (+ water outlines)
    dj = {"districts": [{"id": k, "name": v["name"], "color": v["color"],
                         "polygon": [round(c, 1) for p in v["polygon"] for c in (p[0] - 4000.0, p[1] - 3000.0)]}
                        for k, v in geo["districts"].items()],
          "coastline": [round(c, 1) for p in geo["coastline"] for c in (p[0] - 4000.0, p[1] - 3000.0)],
          "river": [round(c, 1) for p in geo["river_centreline"] for c in (p[0] - 4000.0, p[1] - 3000.0)],
          "lake": [round(c, 1) for p in geo["lake_polygon"] for c in (p[0] - 4000.0, p[1] - 3000.0)],
          "lakeLevel": geo["lake_level_m"]}
    json.dump(dj, open(os.path.join(res_dir, "districts.json"), "w"), separators=(",", ":"))

    # terrain
    lowered, holes = build_terrain(pkg, os.path.join(src, "terrain"), roads)

    # meshes
    mesh_dir = os.path.join(src, "meshes")
    os.makedirs(mesh_dir, exist_ok=True)
    mesh_report = []
    bdir = os.path.join(pkg, "08_3D_ASSETS/district_blockouts")
    for fn in sorted(os.listdir(bdir)):
        m = re.match(r"blockout_(D\d)_", fn)
        if not m:
            continue
        info = convert_glb(os.path.join(bdir, fn), os.path.join(mesh_dir, m.group(1) + ".vmesh"),
                           ("_roads", "_structures", "_water"))
        mesh_report.append(f"{m.group(1)}: " + ", ".join(f"{n.split('_',1)[1]}/{mat} {tris}t" for n, mat, vc, tris in info))

    # placements
    pdir = os.path.join(src, "placement")
    os.makedirs(pdir, exist_ok=True)
    for fn in os.listdir(os.path.join(pkg, "08_3D_ASSETS/placement_data")):
        shutil.copy(os.path.join(pkg, "08_3D_ASSETS/placement_data", fn), os.path.join(pdir, fn))

    with open(os.path.join(src, "build_report.txt"), "w") as f:
        f.write("ROUTES\n" + "\n".join(report) + "\n\n")
        f.write(f"TERRAIN: {lowered} height samples lowered under roads, {holes} hole cells at tunnel portals\n\n")
        f.write("MESHES\n" + "\n".join(mesh_report) + "\n")
    print("\n".join(report))
    print(f"terrain lowered {lowered}, holes {holes}")
    print("\n".join(mesh_report))


if __name__ == "__main__":
    main()

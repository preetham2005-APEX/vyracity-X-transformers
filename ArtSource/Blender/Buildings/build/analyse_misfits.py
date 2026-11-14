"""For every building record the kit does NOT fit, work out WHICH test failed and whether the cause is a
gap in the kit or an anomaly in the data. Rewrites the *_fit.json files with a `misfits` table.

    python analyse_misfits.py

The fit rule is BuildingAssembler.Run's (iteration 1): try both orientations; uniform scale
s = clamp(sqrt(recordArea / kitArea), 0.85, 1.2); require footprint ratios fw, fl in [0.78, 1.22] and
height ratio fh in [0.65, 1.5].
"""
import csv
import json
import math
import os

B = os.path.dirname(os.path.abspath(__file__))
CSV = r"E:\VeyraCity\Assets\_Veyra\Data\world_building_placements.csv"
ROWS = list(csv.DictReader(open(CSV)))
F = lambda r, k: float(r[k])

SETS = [
    ("report_towers.json", "tower_fit.json", lambda r: r["district_id"] == "D1" and r["style_kit"].startswith("GlassModern")),
    ("report_neon.json", "neon_fit.json", lambda r: r["district_id"] == "D6" and r["style_kit"] in ("BrickIndustrial", "ModernLowrise")),
    ("report_coast.json", "coast_fit.json", lambda r: r["style_kit"] == "CoastalStucco"),
    ("report_town.json", "town_fit.json", lambda r: r["style_kit"] == "MarketTownBrick"),
    ("report_houses.json", "houses_fit.json", lambda r: r["style_kit"] == "Suburban"),
    ("report_midrise.json", "midrise_fit.json", lambda r: r["style_kit"] == "ResidentialMidrise"),
    ("report_ironport.json", "ironport_fit.json", lambda r: r["style_kit"] == "Industrial"),
    ("report_ironporttanks.json", "ironporttanks_fit.json",
     lambda r: r["style_kit"] in ("IndustrialTank", "IndustrialRefinery")),
    ("report_alpine.json", "alpine_fit.json", lambda r: r["style_kit"] == "Alpine"),
    ("report_rural.json", "rural_fit.json", lambda r: r["style_kit"] == "Rural"),
    ("report_aviation.json", "aviation_fit.json", lambda r: r["style_kit"] == "Aviation"),
    ("report_retail.json", "retail_fit.json", lambda r: r["style_kit"] == "SuburbanRetail"),
    ("report_roadside.json", "roadside_fit.json", lambda r: r["style_kit"] == "Roadside"),
]


def test(kw, kl, kh, r):
    """Best attempt over both orientations. Returns (ok, stretch, failed_tests)."""
    best = None
    for (a, b) in ((kw, kl), (kl, kw)):
        s = min(1.2, max(0.85, math.sqrt(F(r, "footprint_w_m") * F(r, "footprint_l_m") / max(1.0, a * b))))
        fw = a * s / F(r, "footprint_w_m")
        fl = b * s / F(r, "footprint_l_m")
        fh = kh * s / max(1.0, F(r, "height_m"))
        bad = []
        if not (0.78 <= fw <= 1.22):
            bad.append("width")
        if not (0.78 <= fl <= 1.22):
            bad.append("depth")
        if not (0.65 <= fh <= 1.5):
            bad.append("height")
        cand = (len(bad), bad, abs(F(r, "height_m") / (kh * s) - 1.0))
        if best is None or cand[0] < best[0]:
            best = cand
    return best[0] == 0, best[2], best[1]


for rep, fitfile, pred in SETS:
    rp = os.path.join(B, rep)
    if not os.path.exists(rp):
        continue
    models = [(m["name"], m["footprint_m"][0], m["footprint_m"][1], m["height_m"])
              for m in json.load(open(rp))]
    recs = [r for r in ROWS if pred(r)]
    shorts = sorted(min(F(r, "footprint_w_m"), F(r, "footprint_l_m")) for r in recs)
    longs = sorted(max(F(r, "footprint_w_m"), F(r, "footprint_l_m")) for r in recs)
    p95_long = longs[int(len(longs) * 0.95)]
    misfits, fits, stretches, usage = [], 0, [], {}
    for r in recs:
        results = [(test(w, l, h, r), n) for n, w, l, h in models]
        ok = [(res[1], n) for res, n in results if res[0]]
        if ok:
            fits += 1
            st, n = min(ok)
            stretches.append(st)
            usage[n] = usage.get(n, 0) + 1
            continue
        # why did it miss? take the closest candidate (fewest failed tests)
        why = {}
        for res, n in results:
            for t in res[2]:
                why[t] = why.get(t, 0) + 1
        near = min(results, key=lambda x: (len(x[0][2]), x[0][1]))
        # a model whose FOOTPRINT fits (ignoring height) tells us the gap is a height band, not a plan size
        fp_ok = [n for res, n in results if "width" not in res[2] and "depth" not in res[2]]
        w_m, l_m = F(r, "footprint_w_m"), F(r, "footprint_l_m")
        aspect = max(w_m, l_m) / max(1e-6, min(w_m, l_m))
        if max(w_m, l_m) > p95_long * 1.35:
            cause = ("data outlier: %.0f m long, far beyond the district's p95 of %.0f m"
                     % (max(w_m, l_m), p95_long))
        elif fp_ok:
            cause = "kit gap: footprint fits `%s`, but no model in this height band" % fp_ok[0]
        elif aspect >= 2.6:
            cause = "kit gap: no model this elongated (aspect %.1f:1)" % aspect
        else:
            cause = "kit gap: no model at this footprint"
        if fp_ok:
            near = next(x for x in results if x[1] == fp_ok[0])
        misfits.append({
            "id": r["id"], "name": r["name"], "kind": r["kind"], "typology": r["typology"],
            "footprint_m": [round(w_m, 1), round(l_m, 1)], "height_m": round(F(r, "height_m"), 1),
            "aspect": round(aspect, 2), "chunk": r["chunk"],
            "failed": sorted(set(near[0][2])), "closest_model": near[1], "cause": cause,
        })
    stretches.sort()
    out = {
        "fit": fits, "total": len(recs), "models": len(models),
        "usage": usage,
        "stretch_median": stretches[len(stretches) // 2] if stretches else 0.0,
        "stretch_p90": stretches[int(len(stretches) * 0.9)] if stretches else 0.0,
        "stretch_max": stretches[-1] if stretches else 0.0,
        "unfit": [[m["footprint_m"][0], m["footprint_m"][1], m["height_m"]] for m in misfits],
        "misfits": misfits,
        "district_p95_long_side_m": round(p95_long, 1),
    }
    json.dump(out, open(os.path.join(B, fitfile), "w"), indent=1)
    print("%-18s fit %d/%d  misfits %d" % (fitfile, fits, len(recs), len(misfits)))
    for m in misfits:
        print("    %-12s %-22s %6.1f x %-6.1f h %-6.1f  failed=%-18s  %s"
              % (m["id"], m["kind"], m["footprint_m"][0], m["footprint_m"][1], m["height_m"],
                 ",".join(m["failed"]), m["cause"]))

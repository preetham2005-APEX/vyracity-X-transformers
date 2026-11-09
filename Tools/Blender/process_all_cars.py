"""
Run process_downloaded_car.py in headless Blender for every car in ArtSource/Vehicles/car_map.json.

  python -I Tools/Blender/process_all_cars.py [--only id1,id2] [--jobs 3] [--blender "D:/New folder/blender.exe"]

Each car writes ArtSource/Vehicles/<id>/{<id>.fbx, materials.json, textures/, process_report.json, <id>_preview.png};
Blender's log goes to Logs/A8/blender_<id>.log. A summary table is printed and saved to Logs/A8/process_summary.json.
"""
import json
import os
import subprocess
import sys
from concurrent.futures import ThreadPoolExecutor

ROOT = "E:/VeyraCity"
MAP = ROOT + "/ArtSource/Vehicles/car_map.json"
DL = ROOT + "/ArtSource/Downloads"
SCRIPT = ROOT + "/Tools/Blender/process_downloaded_car.py"
LOGS = ROOT + "/Logs/A8"


def opt(name, default):
    return sys.argv[sys.argv.index(name) + 1] if name in sys.argv else default


def run(car, blender):
    cmd = [blender, "-b", "--factory-startup", "--python", SCRIPT, "--",
           "--src", DL + "/" + car["src"], "--id", car["id"], "--name", car["name"], "--length", str(car.get("length", 0))]
    for k in ("part", "strip", "paint", "flip"):
        if car.get(k):
            cmd += ["--" + k, str(car[k])]
    if car.get("fixLivery"):
        cmd += ["--fix", car["fixLivery"]]
    env = dict(os.environ, TEMP="E:/Temp/VeyraCity", TMP="E:/Temp/VeyraCity")
    log = os.path.join(LOGS, "blender_%s.log" % car["id"])
    with open(log, "w", encoding="utf-8", errors="replace") as f:
        p = subprocess.run(cmd, stdout=f, stderr=subprocess.STDOUT, env=env, timeout=1800)
    rep_path = os.path.join(ROOT, "ArtSource/Vehicles", car["id"], "process_report.json")
    rep = json.load(open(rep_path)) if os.path.exists(rep_path) and p.returncode == 0 else None
    refused = None
    for line in open(log, encoding="utf-8", errors="replace"):
        if "REFUSED" in line or "Error" in line or "Traceback" in line:
            refused = (refused or "") + line.strip()[:200] + " | "
    return car["id"], p.returncode, rep, refused


def main():
    os.makedirs(LOGS, exist_ok=True)
    cars = json.load(open(MAP, encoding="utf-8"))["cars"]
    only = opt("--only", "")
    if only:
        keep = set(only.split(","))
        cars = [c for c in cars if c["id"] in keep]
    blender = opt("--blender", "D:/New folder/blender.exe")
    jobs = int(opt("--jobs", "3"))
    # the processed report of an earlier run must not be mistaken for this run's result
    for c in cars:
        rp = os.path.join(ROOT, "ArtSource/Vehicles", c["id"], "process_report.json")
        if os.path.exists(rp):
            os.remove(rp)
    with ThreadPoolExecutor(max_workers=jobs) as ex:
        results = list(ex.map(lambda c: run(c, blender), cars))
    summary = []
    print("%-20s %4s %6s %-22s %-22s %9s %7s %6s %s" % ("id", "rc", "wheels", "dims (w,h,l)", "front", "LOD0", "wheels", "hull", "notes"))
    for cid, rc, rep, err in results:
        if rep is None:
            print("%-20s %4s FAILED %s" % (cid, rc, err or ""))
            summary.append({"id": cid, "ok": False, "rc": rc, "error": err})
            continue
        fr = rep.get("front", {})
        t = rep.get("tris", {})
        print("%-20s %4s %6s %-22s %-22s %9s %7s %6s %s" % (
            cid, rc, rep.get("wheels_ok"), rep.get("dimensions"), "flip=%s named=%s/%s" % (fr.get("flipped"), fr.get("front_named"), fr.get("rear_named")),
            t.get("Body_LOD0"), t.get("wheels"), t.get("UCX_Body_faces"), "" if rep.get("wheels_ok") else rep.get("hubs_found")))
        summary.append({"id": cid, "ok": bool(rep.get("wheels_ok")), "report": rep})
    json.dump(summary, open(os.path.join(LOGS, "process_summary.json"), "w"), indent=1)


main()

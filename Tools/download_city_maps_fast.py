import os
import sys
import json
import urllib.request
import urllib.error

import os
TOKEN = os.environ.get("SKETCHFAB_API_TOKEN", "")
if not TOKEN:
    raise SystemExit("Set the SKETCHFAB_API_TOKEN environment variable; never hard-code the token.")
TARGET_DIR = r"E:\VeyraCity\ArtSource\Downloads\FullMapModels"
HEADERS = {"Authorization": f"Token {TOKEN}", "User-Agent": "Mozilla/5.0"}

MODELS = [
    ("Asian_Buildings_Set", "d3d7c14c576547e8912df310eb1f0476"),
    ("Futuristic_City", "688a186605d4456c998544b7fe536081"),
    ("Atlanta_Corporate_Office", "d96380fb001345cca9a9be121f3e43d5"),
    ("City_Road_Layout_Mockup", "3260e3fef3434939bc8df94a0a15ead3"),
    ("Modular_Roads_Pack", "99023ee0b2564395b47b40d22f51f7c2")
]

for name, uid in MODELS:
    out_file = os.path.join(TARGET_DIR, f"{name}_{uid[:8]}.zip")
    if os.path.exists(out_file) and os.path.getsize(out_file) > 100000:
        print(f"Already exists: {name}")
        continue
    api_url = f"https://api.sketchfab.com/v3/models/{uid}/download"
    req = urllib.request.Request(api_url, headers=HEADERS)
    try:
        with urllib.request.urlopen(req, timeout=30) as resp:
            data = json.loads(resp.read().decode("utf-8"))
        gltf = data.get("gltf")
        if not gltf:
            print(f"No gltf for {name}")
            continue
        url = gltf.get("url")
        size = gltf.get("size", 0) / (1024 * 1024)
        print(f"Downloading {name} ({size:.1f} MB)...")
        tmp_path = out_file + ".dl_tmp"
        dl_req = urllib.request.Request(url, headers={"User-Agent": "Mozilla/5.0"})
        with urllib.request.urlopen(dl_req, timeout=120) as dl_resp:
            with open(tmp_path, "wb") as f:
                while True:
                    chunk = dl_resp.read(256 * 1024)
                    if not chunk:
                        break
                    f.write(chunk)
        if os.path.exists(out_file):
            os.remove(out_file)
        os.rename(tmp_path, out_file)
        print(f"Saved {name} ({os.path.getsize(out_file)/(1024*1024):.1f} MB)")
    except Exception as e:
        print(f"Error {name}: {e}")


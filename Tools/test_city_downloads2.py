import urllib.request
import json

import os
TOKEN = os.environ.get("SKETCHFAB_API_TOKEN", "")
if not TOKEN:
    raise SystemExit("Set the SKETCHFAB_API_TOKEN environment variable; never hard-code the token.")
headers = {"Authorization": f"Token {TOKEN}", "User-Agent": "Mozilla/5.0"}

test_uids2 = [
    ("Futuristic_Dystopian_City", "925230e8f5ef414d96e32e75078f9b58"),
    ("Full_Gameready_City_Retexture", "5fad2351e34f4c069a2eec45cce0824c"),
    ("Plaza_Night_Time", "282f497334f64a589edee4e63ad7e428"),
    ("San_Francisco_City", "108841754fd3485886c1dde13301d341"),
    ("Asian_Buildings_Set", "d3d7c14c576547e8912df310eb1f0476"),
    ("Futuristic_City", "688a186605d4456c998544b7fe536081"),
    ("City_Suburbs", "727bc270062e4109bd8d9d37391e9990"),
    ("Atlanta_Corporate_Office", "d96380fb001345cca9a9be121f3e43d5"),
    ("City_Road_Layout_Mockup", "3260e3fef3434939bc8df94a0a15ead3"),
    ("Modular_Roads_Pack", "99023ee0b2564395b47b40d22f51f7c2")
]

for name, uid in test_uids2:
    url = f"https://api.sketchfab.com/v3/models/{uid}/download"
    req = urllib.request.Request(url, headers=headers)
    try:
        with urllib.request.urlopen(req) as resp:
            data = json.loads(resp.read().decode("utf-8"))
            gltf = data.get("gltf", {})
            size = gltf.get("size", 0) / (1024 * 1024)
            print(f"OK: {name:<35} | glTF size: {size:6.1f} MB")
    except urllib.error.HTTPError as e:
        print(f"FAILED {e.code}: {name} ({e.reason})")
    except Exception as e:
        print(f"ERROR: {name} ({e})")


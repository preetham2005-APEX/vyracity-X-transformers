import urllib.request
import json

TOKEN = "43a37c72070946648110abd6795ba410"
headers = {"Authorization": f"Token {TOKEN}", "User-Agent": "Mozilla/5.0"}

test_uids = [
    ("Modern_City_Block", "c80dba249d9547cbb48d00828d23cfa7"),
    ("Cyberpunk_City", "3f24e5c5bf924f46b30d9a392afa9624"),
    ("Neighbourhood_City_Modular", "6a0e3b97a8a54f2a909d351322916293"),
    ("Full_Gameready_City_HongKong", "31c1ede1fcbd49c7af85825905446ec5"),
    ("NY_Highway_Interstate_95", "f668f36f01d44a6cae4b6bfa2f9b3021"),
    ("Downtown_Buildings_Set", "7378e7fb9c914c39880d9913a6f4e1d6"),
    ("Brownstone_Building_Set", "b15e6344acd844eabc823e1cc8332574"),
    ("Japanese_Street_At_Night", "fb1bdcd71a5544d699379d2d13dd1171"),
    ("Realistic_Chicago_Buildings", "c4a3993ef1e84e6d9d5a15c78da88aae"),
    ("CCity_Building_Set_1", "a2d5c7bfcc2148fb8994864c43dfcc97"),
    ("New_York_City", "4737b6ee73ed4634ad5b414558fdfa45"),
    ("Modular_City_Builder_Starter_Kit", "784358d2b44a43398079770d316b6de7")
]

for name, uid in test_uids:
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


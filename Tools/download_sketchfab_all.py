"""
Automated Sketchfab 3D Model Batch Downloader for VEYRA CITY.
Downloads all cars and map models from DOWNLOAD_LIST.md directly into E:\VeyraCity\ArtSource\Downloads\
using the official Sketchfab API.
"""
import os
import sys
import json
import urllib.request
import urllib.error

DEST_DIR = r"E:\VeyraCity\ArtSource\Downloads"

MODELS = [
    {"id": "6695e98d6d534d90a23993289ee4ecbf", "name": "GtaLC_2001_FULL_MAP"},
    {"id": "0a20e49ad5774d778567cb5c3f345786", "name": "V12_Goblin_Supercar"},
    {"id": "47a361137a3749858d21b2ce8bcbe21e", "name": "Iron_Howl_V8"},
    {"id": "b1cc3110ed6e41248b0ccadb28e41e54", "name": "Orion_Skylark_GT"},
    {"id": "c880d700b7ec445ea9718e1b73f97e3e", "name": "Coupe_33_HotRod_2"},
    {"id": "1f00f875bd594469ad41e377585dc0fc", "name": "Coupe_33_HotRod_1"},
    {"id": "955edc733c6d44fabc0ad7c246a15896", "name": "Phoenix_455_71"},
    {"id": "59a0eaaa2af144b2956957e7032d0221", "name": "Tiara_GT_83_Tuned"},
    {"id": "1269720ec7b84c899eac77c1d325141e", "name": "Lasley_67"},
    {"id": "22015d1863d6455aa31cfd738b972c50", "name": "Bokaroo_67"},
    {"id": "30700a84311c4bea9ef722f0773ce92a", "name": "Doutrive_78"},
    {"id": "5c17c582ef9549798f694b660feea42a", "name": "Kiri_86"},
    {"id": "8509a7f82665442592627940b7a1cfab", "name": "Riverside_88"},
    {"id": "663a0953c038434a918cb85725c88ffa", "name": "LCT_3000_95"},
    {"id": "09d718c9cf72401b8534d265a06a803f", "name": "Shvan_92"},
    {"id": "d604871eee5b4181bb298163b0b675b7", "name": "Illinois_90"},
    {"id": "e2678da920cc4be68dbc193727919ffb", "name": "Fairheaven_LT_80"},
    {"id": "892b2684fb37442299bfad9222cf331c", "name": "Negotiator_80"},
    {"id": "9e8f92a215784f3d8aaaaeab1bef54c4", "name": "Canyon_75_Taxi"},
    {"id": "61766c1e6e1e4b2b803c174f33433a74", "name": "Fairheaven_Cop_Cruiser"},
    {"id": "20f9af9b8a404d5cb022ac6fe87f21f5", "name": "Generic_Passenger_Car_Pack"}
]

def download_all(api_token):
    os.makedirs(DEST_DIR, exist_ok=True)
    headers = {
        "Authorization": f"Token {api_token}",
        "User-Agent": "VeyraCity-AssetDownloader/1.0"
    }

    print(f"Starting download of {len(MODELS)} models into {DEST_DIR}...")
    success = 0
    failed = 0

    for i, m in enumerate(MODELS, 1):
        uid = m["id"]
        name = m["name"]
        print(f"[{i}/{len(MODELS)}] Requesting download link for {name} ({uid})...")
        api_url = f"https://api.sketchfab.com/v3/models/{uid}/download"
        
        try:
            req = urllib.request.Request(api_url, headers=headers)
            with urllib.request.urlopen(req) as response:
                data = json.loads(response.read().decode())
                
            # Pick gltf or glb
            download_info = data.get("gltf") or data.get("glb")
            if not download_info:
                print(f"  [ERROR] No glTF/GLB download available for {name}.")
                failed += 1
                continue
                
            file_url = download_info["url"]
            size_mb = download_info.get("size", 0) / (1024 * 1024)
            filename = f"{name}_{uid[:8]}.zip"
            dest_file = os.path.join(DEST_DIR, filename)
            
            print(f"  Downloading {filename} ({size_mb:.1f} MB)...")
            dl_req = urllib.request.Request(file_url, headers={"User-Agent": "Mozilla/5.0"})
            with urllib.request.urlopen(dl_req) as dl_resp, open(dest_file, "wb") as out:
                chunk_size = 1024 * 1024
                while True:
                    chunk = dl_resp.read(chunk_size)
                    if not chunk:
                        break
                    out.write(chunk)
            print(f"  [OK] Saved to {dest_file}")
            success += 1
        except urllib.error.HTTPError as e:
            print(f"  [HTTP ERROR {e.code}] for {name}: {e.read().decode()[:200]}")
            failed += 1
        except Exception as ex:
            print(f"  [ERROR] {name}: {ex}")
            failed += 1

    print(f"\nDone: {success} downloaded, {failed} failed.")

if __name__ == "__main__":
    if len(sys.argv) < 2:
        print("Usage: python download_sketchfab_all.py <YOUR_SKETCHFAB_API_TOKEN>")
        sys.exit(1)
    token = sys.argv[1].strip()
    download_all(token)


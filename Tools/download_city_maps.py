import os
import sys
import json
import time
import urllib.request
import urllib.error

import os
TOKEN = os.environ.get("SKETCHFAB_API_TOKEN", "")
if not TOKEN:
    raise SystemExit("Set the SKETCHFAB_API_TOKEN environment variable; never hard-code the token.")
TARGET_DIR = r"E:\VeyraCity\ArtSource\Downloads\FullMapModels"
os.makedirs(TARGET_DIR, exist_ok=True)

HEADERS = {
    "Authorization": f"Token {TOKEN}",
    "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64)"
}

MODELS = [
    {
        "id": "Modern_City_Block",
        "uid": "c80dba249d9547cbb48d00828d23cfa7",
        "creator": "akselmot",
        "desc": "Realistic modern urban city block complete with roads, sidewalks, storefronts, and crosswalks"
    },
    {
        "id": "Cyberpunk_City",
        "uid": "3f24e5c5bf924f46b30d9a392afa9624",
        "creator": "golukumar",
        "desc": "Massive cyberpunk neon metropolis, high-density skyscrapers and neon billboards"
    },
    {
        "id": "Neighbourhood_City_Modular",
        "uid": "6a0e3b97a8a54f2a909d351322916293",
        "creator": "golukumar",
        "desc": "Modular city neighborhood layout with streets, residential and commercial buildings"
    },
    {
        "id": "Full_Gameready_City_HongKong",
        "uid": "31c1ede1fcbd49c7af85825905446ec5",
        "creator": "abhayexe",
        "desc": "Dense gameready Asian/Hong Kong high-rise city block with air conditioners and antennas"
    },
    {
        "id": "NY_Highway_Interstate_95",
        "uid": "f668f36f01d44a6cae4b6bfa2f9b3021",
        "creator": "99.Miles",
        "desc": "Realistic multi-lane highway, elevated overpasses, viaducts and interchanges"
    },
    {
        "id": "Downtown_Buildings_Set",
        "uid": "7378e7fb9c914c39880d9913a6f4e1d6",
        "creator": "Daniel Zhabotinsky",
        "desc": "Downtown skyscraper modular building set"
    },
    {
        "id": "Brownstone_Building_Set",
        "uid": "b15e6344acd844eabc823e1cc8332574",
        "creator": "Daniel Zhabotinsky",
        "desc": "Classic brownstone multi-storey residential street blocks"
    },
    {
        "id": "Japanese_Street_At_Night",
        "uid": "fb1bdcd71a5544d699379d2d13dd1171",
        "creator": "AFX/CGMotion 3DModel Maker",
        "desc": "Ultra atmospheric Japanese street at night with neon signage, storefronts, asphalt details"
    },
    {
        "id": "Realistic_Chicago_Buildings",
        "uid": "c4a3993ef1e84e6d9d5a15c78da88aae",
        "creator": "99.Miles",
        "desc": "Realistic Chicago-style high-rise architecture"
    },
    {
        "id": "CCity_Building_Set_1",
        "uid": "a2d5c7bfcc2148fb8994864c43dfcc97",
        "creator": "Neberkenezer",
        "desc": "Comprehensive modern city building kit"
    },
    {
        "id": "New_York_City",
        "uid": "4737b6ee73ed4634ad5b414558fdfa45",
        "creator": "golukumar",
        "desc": "New York City high-rise blocks and streets"
    },
    {
        "id": "Modular_City_Builder_Starter_Kit",
        "uid": "784358d2b44a43398079770d316b6de7",
        "creator": "Pieter Ferreira",
        "desc": "Modular city kit containing roads, buildings, intersections, and street furniture"
    },
    {
        "id": "Futuristic_Dystopian_City",
        "uid": "925230e8f5ef414d96e32e75078f9b58",
        "creator": "golukumar",
        "desc": "Futuristic dystopian city structures and elevated roadways"
    },
    {
        "id": "Full_Gameready_City_Retexture",
        "uid": "5fad2351e34f4c069a2eec45cce0824c",
        "creator": "golukumar",
        "desc": "High detail textured city block with storefronts and street assets"
    },
    {
        "id": "Plaza_Night_Time",
        "uid": "282f497334f64a589edee4e63ad7e428",
        "creator": "patrix",
        "desc": "Photorealistic night city plaza with illuminated architecture"
    },
    {
        "id": "San_Francisco_City",
        "uid": "108841754fd3485886c1dde13301d341",
        "creator": "Abimael Gonzalez",
        "desc": "San Francisco bay and city landmarks"
    },
    {
        "id": "Asian_Buildings_Set",
        "uid": "d3d7c14c576547e8912df310eb1f0476",
        "creator": "golukumar",
        "desc": "Asian urban buildings and neon night street props"
    },
    {
        "id": "Futuristic_City",
        "uid": "688a186605d4456c998544b7fe536081",
        "creator": "golukumar",
        "desc": "Futuristic city with towers, bridges, and skyways"
    },
    {
        "id": "Atlanta_Corporate_Office",
        "uid": "d96380fb001345cca9a9be121f3e43d5",
        "creator": "99.Miles",
        "desc": "Corporate glass office high-rise tower"
    },
    {
        "id": "City_Road_Layout_Mockup",
        "uid": "3260e3fef3434939bc8df94a0a15ead3",
        "creator": "99.Miles",
        "desc": "Full urban street layout with road markings, lanes, and intersections"
    },
    {
        "id": "Modular_Roads_Pack",
        "uid": "99023ee0b2564395b47b40d22f51f7c2",
        "creator": "SquadCell",
        "desc": "Modular road pieces, junctions, curbs, and markings"
    }
]

def download_file(url, target_path):
    req = urllib.request.Request(url, headers={"User-Agent": "Mozilla/5.0"})
    with urllib.request.urlopen(req, timeout=120) as resp:
        total = int(resp.headers.get("Content-Length", 0))
        downloaded = 0
        chunk_size = 256 * 1024
        with open(target_path, "wb") as f:
            while True:
                chunk = resp.read(chunk_size)
                if not chunk:
                    break
                f.write(chunk)
                downloaded += len(chunk)
                if total > 0 and downloaded % (2 * 1024 * 1024) < chunk_size:
                    sys.stdout.write(f"\r  Downloaded {downloaded / (1024*1024):.1f}/{total / (1024*1024):.1f} MB ({(downloaded/total)*100:.0f}%)")
                    sys.stdout.flush()
    print()

def main():
    catalog_lines = [
        "# Veyra City - Full Map & City Block 3D Models Catalog",
        "",
        "This folder contains complete 3D city blocks, road networks, highways, and downtown environments downloaded from Sketchfab and internal design archives.",
        "",
        "| Model Name | File | Creator | Description |",
        "|---|---|---|---|"
    ]
    
    # Add local files
    catalog_lines.append("| Isparta City Block (Turkey) | `isparta_city_block_turkey.glb` | Local / Photogrammetry | Highly detailed photorealistic city block |")
    catalog_lines.append("| GTA Liberty City 2001 Full Map | `gtalc-2001-full-map-3dmodel.zip` / `GtaLC_2001_FULL_MAP_6695e98d.zip` | Archive | Complete 3D city map with terrain, roads, and buildings |")

    success_count = 0
    fail_count = 0

    for idx, item in enumerate(MODELS, 1):
        model_id = item["id"]
        uid = item["uid"]
        creator = item["creator"]
        desc = item["desc"]
        out_filename = f"{model_id}_{uid[:8]}.zip"
        out_path = os.path.join(TARGET_DIR, out_filename)

        print(f"\n[{idx}/{len(MODELS)}] {model_id} ({uid})...")
        catalog_lines.append(f"| {model_id.replace('_', ' ')} | `{out_filename}` | {creator} | {desc} |")

        if os.path.exists(out_path) and os.path.getsize(out_path) > 100000:
            print(f"  Already exists ({os.path.getsize(out_path) / (1024*1024):.1f} MB), skipping.")
            success_count += 1
            continue

        api_url = f"https://api.sketchfab.com/v3/models/{uid}/download"
        req = urllib.request.Request(api_url, headers=HEADERS)
        try:
            with urllib.request.urlopen(req, timeout=30) as resp:
                data = json.loads(resp.read().decode("utf-8"))
            
            gltf_data = data.get("gltf")
            if not gltf_data:
                print(f"  No glTF download format available.")
                fail_count += 1
                continue
            
            download_url = gltf_data.get("url")
            expected_size = gltf_data.get("size", 0) / (1024 * 1024)
            print(f"  Fetching glTF archive ({expected_size:.1f} MB)...")

            temp_path = out_path + ".tmp"
            download_file(download_url, temp_path)
            if os.path.exists(out_path):
                os.remove(out_path)
            os.rename(temp_path, out_path)
            print(f"  Successfully saved {out_filename} ({os.path.getsize(out_path)/(1024*1024):.1f} MB)")
            success_count += 1
            time.sleep(1)
        except Exception as e:
            print(f"  Error: {e}")
            fail_count += 1

    catalog_path = os.path.join(TARGET_DIR, "MAP_MODELS_CATALOG.md")
    with open(catalog_path, "w", encoding="utf-8") as f:
        f.write("\n".join(catalog_lines) + "\n")
    print(f"\nUpdated catalog at {catalog_path}")
    print(f"Done! Successfully acquired: {success_count}, Failed: {fail_count}")

if __name__ == "__main__":
    main()


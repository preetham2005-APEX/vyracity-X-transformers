import urllib.request
import json
import os
import sys
import time

TOKEN = os.environ.get("SKETCHFAB_API_TOKEN", "")
if not TOKEN:
    raise SystemExit("Set the SKETCHFAB_API_TOKEN environment variable; never hard-code the token.")
HEADERS = {"Authorization": f"Token {TOKEN}", "User-Agent": "Mozilla/5.0"}
TARGET_DIR = r"E:\VeyraCity\ArtSource\Downloads"

CARS = [
    ("7ad3dffa9d344c3c978eafcc220cb709", "Lamborghini_Terzo_Millennio_Concept"),
    ("4f63f5a74611477989cefd9861b9784a", "Bugatti_Tourbillon_2026"),
    ("747cedadc302451db61deafc85941395", "McLaren_P1_Hypercar"),
    ("2c80c667232544649328cf3589921bcd", "Ferrari_SF90_XX_Stradale"),
    ("f17a982d5d8a4d97baef4b00b51a4e9a", "Porsche_911_GT3_RS_2023"),
    ("1f614e803fc24946b83bdc2dd9926d62", "Rolls_Royce_Ghost_Luxury"),
    ("8bae74bd359940508717afb5c1f212a3", "Koenigsegg_Gemera_Hypercar"),
    ("27956e4ebaaa468980bc48e50106d5c6", "BMW_M6_GT3_RaceCar"),
    ("936854ba7dde46f09a47d355d433808c", "Aston_Martin_DB11_V12_Luxury"),
    ("658684653b154ffba72d5f9511312ca8", "Bugatti_Bolide_Track_Hypercar"),
    ("4af92c51ecdd4efa9b1c19a1163d9f46", "Bugatti_EB110_SuperSport"),
    ("790c40ccff6843eab0b7b4bd18421ff8", "Chevrolet_Corvette_C8_Stingray"),
]

def check_and_download():
    os.makedirs(TARGET_DIR, exist_ok=True)
    print(f"Target directory: {TARGET_DIR}")
    
    for uid, name in CARS:
        filename = f"{name}_{uid[:8]}.zip"
        dest_path = os.path.join(TARGET_DIR, filename)
        
        if os.path.exists(dest_path) and os.path.getsize(dest_path) > 1000:
            print(f"[EXISTS] {filename} ({os.path.getsize(dest_path) / (1024*1024):.1f} MB)")
            continue
            
        print(f"\n[REQUESTING] {name} ({uid})...")
        download_api_url = f"https://api.sketchfab.com/v3/models/{uid}/download"
        req = urllib.request.Request(download_api_url, headers=HEADERS)
        
        try:
            with urllib.request.urlopen(req) as resp:
                data = json.loads(resp.read().decode("utf-8"))
                gltf = data.get("gltf")
                if not gltf or not gltf.get("url"):
                    print(f"  [ERROR] No glTF download link for {uid}")
                    continue
                
                download_url = gltf["url"]
                expected_size = gltf.get("size", 0)
                print(f"  [DOWNLOADING] {expected_size / (1024*1024):.1f} MB...")
                
                tmp_dest = dest_path + ".tmp"
                urllib.request.urlretrieve(download_url, tmp_dest)
                if os.path.exists(dest_path):
                    os.remove(dest_path)
                os.rename(tmp_dest, dest_path)
                print(f"  [SUCCESS] Saved {filename} ({os.path.getsize(dest_path) / (1024*1024):.1f} MB)")
                time.sleep(1) # respectful pacing
                
        except urllib.error.HTTPError as e:
            print(f"  [HTTP ERROR] {e.code}: {e.reason}")
        except Exception as e:
            print(f"  [ERROR] {e}")

if __name__ == "__main__":
    check_and_download()


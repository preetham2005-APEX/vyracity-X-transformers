import urllib.request
import json
import sys

import os
TOKEN = os.environ.get("SKETCHFAB_API_TOKEN", "")
if not TOKEN:
    raise SystemExit("Set the SKETCHFAB_API_TOKEN environment variable; never hard-code the token.")
headers = {"Authorization": f"Token {TOKEN}", "User-Agent": "Mozilla/5.0"}

query = "city block realistic"
url = f"https://api.sketchfab.com/v3/search?type=models&downloadable=true&q={urllib.parse.quote(query)}&sort_by=-likeCount"

req = urllib.request.Request(url, headers=headers)
try:
    with urllib.request.urlopen(req) as resp:
        data = json.loads(resp.read().decode("utf-8"))
        results = data.get("results", [])
        print(f"Total results: {len(results)}")
        for item in results[:10]:
            uid = item.get("uid")
            name = item.get("name")
            faces = item.get("faceCount")
            likes = item.get("likeCount")
            license_info = item.get("license", {}).get("label")
            print(f"[{uid}] {name} | {faces} faces | {likes} likes | {license_info}")
except Exception as e:
    print(f"Error: {e}")


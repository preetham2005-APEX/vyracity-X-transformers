import urllib.request
import urllib.parse
import json

import os
TOKEN = os.environ.get("SKETCHFAB_API_TOKEN", "")
if not TOKEN:
    raise SystemExit("Set the SKETCHFAB_API_TOKEN environment variable; never hard-code the token.")
headers = {"Authorization": f"Token {TOKEN}", "User-Agent": "Mozilla/5.0"}

queries = [
    "GTA map",
    "city map full",
    "open world city",
    "race track city",
    "isparta turkey",
    "city environment"
]

all_models = {}

for q in queries:
    url = f"https://api.sketchfab.com/v3/search?type=models&downloadable=true&q={urllib.parse.quote(q)}&sort_by=-likeCount"
    req = urllib.request.Request(url, headers=headers)
    try:
        with urllib.request.urlopen(req) as resp:
            data = json.loads(resp.read().decode("utf-8"))
            results = data.get("results", [])
            for item in results:
                uid = item.get("uid")
                if uid not in all_models:
                    all_models[uid] = {
                        "name": item.get("name"),
                        "faces": item.get("faceCount", 0),
                        "likes": item.get("likeCount", 0),
                        "views": item.get("viewCount", 0),
                        "user": item.get("user", {}).get("displayName"),
                        "license": item.get("license", {}).get("label"),
                        "query": q
                    }
    except Exception as e:
        print(f"Error on query {q}: {e}")

sorted_models = sorted(all_models.items(), key=lambda x: x[1]["likes"], reverse=True)

print(f"Total unique models found: {len(sorted_models)}\n")
for uid, info in sorted_models[:30]:
    print(f"[{uid}] {info['name'][:45]:<45} | {info['faces']:>8} faces | {info['likes']:>5} likes | {info['license']} | by {info['user']}")


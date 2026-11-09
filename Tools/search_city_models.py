import urllib.request
import urllib.parse
import json

TOKEN = "43a37c72070946648110abd6795ba410"
headers = {"Authorization": f"Token {TOKEN}", "User-Agent": "Mozilla/5.0"}

queries = [
    "city realistic",
    "cyberpunk city",
    "downtown city",
    "city block",
    "tokyo street",
    "modular city",
    "gta city",
    "urban street"
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

# Sort by likes descending
sorted_models = sorted(all_models.items(), key=lambda x: x[1]["likes"], reverse=True)

print(f"Total unique models found: {len(sorted_models)}\n")
for uid, info in sorted_models[:40]:
    print(f"[{uid}] {info['name'][:40]:<40} | {info['faces']:>8} faces | {info['likes']:>5} likes | {info['license']} | by {info['user']}")


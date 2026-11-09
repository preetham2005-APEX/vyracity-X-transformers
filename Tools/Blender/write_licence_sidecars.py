"""
Write <download>.licence.json next to every downloaded zip in ArtSource/Downloads from the license.txt that
Sketchfab ships inside the zip. The sidecar is what the licence gate in process_downloaded_car.py checks.

  python -I Tools/Blender/write_licence_sidecars.py [E:/VeyraCity/ArtSource/Downloads]

Zips without a readable licence, with a licence outside the allowed list, or whose name points at another game
get no sidecar (and are listed as REFUSED), so they can never enter the project.
"""
import glob
import json
import os
import re
import sys
import zipfile

ALLOWED = {"CC-BY-4.0": "CC-BY-4.0", "CC0": "CC0", "CC-BY-SA-4.0": None, "CC-BY-NC-4.0": None, "CC-BY-ND-4.0": None}
FORBIDDEN = ("gta", "grand theft", "liberty city", "rockstar", "cyberpunk", "night city", "forza", "need for speed",
             "nfs", "asphalt", "gran turismo", "burnout", "midnight club")


def forbidden(text):
    """Substring match ("GtaLC" must not slip through a word-boundary test); only "nfs" needs a boundary."""
    for w in FORBIDDEN:
        if w == "nfs":
            if re.search(r"(^|[^a-z])nfs([^a-z]|$)", text):
                return True
        elif w in text:
            return True
    return False


def parse(text):
    out = {}
    for key, pat in (("title", r"\*\s*title:\s*(.+)"), ("url", r"\*\s*source:\s*(\S+)"), ("author", r"\*\s*author:\s*(.+)"),
                     ("licence_raw", r"\*\s*license type:\s*(.+)")):
        m = re.search(pat, text)
        if m:
            out[key] = m.group(1).strip()
    if "author" in out:
        m = re.match(r"(.+?)\s*\((https?://[^)]+)\)", out["author"])
        if m:
            out["author"], out["author_url"] = m.group(1).strip(), m.group(2)
    if "licence_raw" in out:
        out["licence_code"] = out["licence_raw"].split()[0]
    return out


def main():
    folder = sys.argv[1] if len(sys.argv) > 1 else "E:/VeyraCity/ArtSource/Downloads"
    for z in sorted(glob.glob(os.path.join(folder, "*.zip"))):
        base = os.path.basename(z).lower()
        side = os.path.splitext(z)[0] + ".licence.json"
        try:
            with zipfile.ZipFile(z) as f:
                name = next((n for n in f.namelist() if n.lower().endswith(("license.txt", "licence.txt"))), None)
                text = f.read(name).decode("utf-8", "replace") if name else ""
        except zipfile.BadZipFile:
            print("REFUSED (bad zip):", os.path.basename(z))
            continue
        info = parse(text)
        if forbidden(base + " " + info.get("title", "").lower()):
            print("REFUSED (other game's content):", os.path.basename(z))
            if os.path.exists(side):
                os.remove(side)
            continue
        lic = ALLOWED.get(info.get("licence_code", ""))
        if not lic or not info.get("author") or not info.get("url"):
            print("REFUSED (licence %r):" % info.get("licence_code"), os.path.basename(z))
            continue
        data = {
            "licence": lic,
            "licence_url": "https://creativecommons.org/licenses/by/4.0/" if lic == "CC-BY-4.0" else "https://creativecommons.org/publicdomain/zero/1.0/",
            "title": info.get("title", ""),
            "author": info["author"],
            "author_url": info.get("author_url", ""),
            "url": info["url"],
            "file": os.path.basename(z),
            "credit": '"%s" by %s (%s), licensed under %s' % (info.get("title", ""), info["author"], info["url"], lic),
            "evidence": "license.txt inside the downloaded zip",
        }
        with open(side, "w", encoding="utf-8") as f:
            json.dump(data, f, indent=1)
        print("OK", lic, "|", info["author"], "|", info.get("title"))


main()

"""Card B1: licence gate for ArtSource/Downloads (stdlib only, idempotent).

Every downloaded asset needs a licence record next to it: <file>.license.txt (card format) or <file>.licence.json (format of
the vehicle pipeline). When neither exists, the record is built from the license.txt inside the zip or the extracted folder
(Sketchfab ships one) and written as <file>.license.txt.

Rules (card B1):
  ACCEPT     CC0, CC-BY x.x (attribution recorded); a store licence that permits commercial game use (needs a record).
  QUARANTINE no licence record; NonCommercial (NC); NoDerivatives (ND; kitbashing modifies); ShareAlike (SA: adaptations would
             have to be released under the same licence); Sketchfab Standard / Editorial
             without proof of a purchase; "no AI" terms; anything that looks ripped from a commercial game (GTA / Liberty
             City / Vice City / San Andreas / Cyberpunk 2077 / Night City names). Ripped files are matched by NAME ONLY and
             are never opened.
Quarantined files (and their sidecars and extracted folders) are MOVED to ArtSource/Downloads/_Quarantine/, never deleted,
and listed in _Quarantine/QUARANTINE.md. Nothing in _Quarantine is processed.

Outputs: Logs/B1/licence_gate.json (input for Tools/Blender/triage_downloads.py), Logs/B1/licence_gate.txt and
Docs/ASSET_SOURCES.md.

Usage: python -I Tools/Assets/licence_gate.py [--dry-run]
"""
import datetime
import json
import os
import re
import shutil
import sys
import zipfile

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
DL = os.path.join(ROOT, "ArtSource", "Downloads")
Q = os.path.join(DL, "_Quarantine")
ASSET_EXT = (".zip", ".glb", ".gltf", ".fbx", ".obj", ".blend")
RIPPED = re.compile(r"gta|grand.?theft|liberty.?city|vice.?city|san.?andreas|cyberpunk.?2077|night.?city|ripped", re.I)
REAL_BRANDS = re.compile(r"aston.?martin|bmw|bugatti|chevrolet|corvette|ferrari|koenigsegg|lamborghini|mclaren|porsche|rolls.?royce|mercedes|audi|nissan|toyota|honda|ford", re.I)


def rel(p):
    return os.path.relpath(p, ROOT).replace("\\", "/")


def read_license_txt_from_zip(path):
    """Reads only a license.txt member of a zip (no extraction)."""
    try:
        with zipfile.ZipFile(path) as z:
            for info in z.infolist():
                if os.path.basename(info.filename).lower() in ("license.txt", "licence.txt") and info.file_size < 65536:
                    return z.read(info).decode("utf-8", "replace")
    except (zipfile.BadZipFile, OSError):
        return None
    return None


def parse_sketchfab_license(text):
    def field(name):
        m = re.search(r"\*\s*" + name + r":\s*(.+)", text)
        return m.group(1).strip() if m else ""
    lic = field("license type")
    title_m = re.search(r"Model Information:\s*\*\s*title:\s*(.+)", text)
    return {
        "licence": lic,
        "title": (title_m.group(1).strip() if title_m else field("title")),
        "author": field("author"),
        "url": field("source"),
        "credit": (re.search(r"(This work is based on .+)", text).group(1).strip() if re.search(r"(This work is based on .+)", text) else ""),
    }


def classify(lic):
    l = (lic or "").upper()
    if not l:
        return "QUARANTINE", "no licence record"
    if "NC" in re.split(r"[^A-Z]+", l) or "NONCOMMERCIAL" in l.replace("-", "").replace(" ", ""):
        return "QUARANTINE", f"non-commercial licence ({lic})"
    if "ND" in re.split(r"[^A-Z]+", l) or "NODERIV" in l.replace("-", "").replace(" ", ""):
        return "QUARANTINE", f"no-derivatives licence ({lic}); kitbashing modifies the model"
    if "SA" in re.split(r"[^A-Z]+", l) or "SHAREALIKE" in l.replace("-", "").replace(" ", ""):
        return "QUARANTINE", f"share-alike licence ({lic}); adapted models would have to be released under the same licence"
    if "STANDARD" in l or "EDITORIAL" in l:
        return "QUARANTINE", f"{lic}: not CC, no proof of a commercial-use purchase on record"
    if "NO AI" in l or "NOAI" in l:
        return "QUARANTINE", f"no-AI terms ({lic})"
    if "CC0" in l or "CC-BY" in l or "CC BY" in l or "PUBLIC DOMAIN" in l:
        return "ACCEPT", lic
    return "QUARANTINE", f"unrecognised licence ({lic})"


def sidecars(path):
    stem, _ = os.path.splitext(path)
    return [p for p in (path + ".license.txt", stem + ".licence.json", path + ".licence.json") if os.path.exists(p)]


def extracted_folder(path):
    stem = os.path.splitext(os.path.basename(path))[0]
    d = os.path.join(os.path.dirname(path), "extracted", stem)
    return d if os.path.isdir(d) else None


def items():
    out = []
    for base in (DL, os.path.join(DL, "FullMapModels")):
        if not os.path.isdir(base):
            continue
        for name in sorted(os.listdir(base)):
            p = os.path.join(base, name)
            if os.path.isfile(p) and name.lower().endswith(ASSET_EXT):
                out.append(p)
    # extracted folders that have no zip of their own (renamed on extraction)
    ext = os.path.join(DL, "FullMapModels", "extracted")
    if os.path.isdir(ext):
        zips = {os.path.splitext(os.path.basename(p))[0] for p in out}
        for name in sorted(os.listdir(ext)):
            if name not in zips and os.path.isdir(os.path.join(ext, name)):
                out.append(os.path.join(ext, name))
    return out


def record_for(path):
    """Licence record: existing sidecar, else license.txt inside the zip / extracted folder."""
    for sc in sidecars(path):
        if sc.endswith(".licence.json"):
            with open(sc, encoding="utf-8") as f:
                j = json.load(f)
            return {"licence": j.get("licence", ""), "title": j.get("title", ""), "author": j.get("author", ""), "url": j.get("url", ""),
                    "credit": j.get("credit", ""), "real_brand": j.get("real_brand", ""), "evidence": rel(sc), "sidecar": rel(sc)}
        if sc.endswith(".license.txt"):
            with open(sc, encoding="utf-8") as f:
                t = f.read()
            g = lambda k: (re.search(r"^" + k + r":\s*(.*)$", t, re.M).group(1).strip() if re.search(r"^" + k + r":\s*(.*)$", t, re.M) else "")
            return {"licence": g("Licence"), "title": g("Title"), "author": g("Author"), "url": g("Source"), "credit": g("Attribution"),
                    "evidence": g("Evidence") or rel(sc), "sidecar": rel(sc)}
    text = None
    evidence = ""
    if os.path.isdir(path):
        lt = os.path.join(path, "license.txt")
        if os.path.exists(lt):
            with open(lt, encoding="utf-8", errors="replace") as f:
                text = f.read()
            evidence = rel(lt)
    elif path.lower().endswith(".zip"):
        text = read_license_txt_from_zip(path)
        evidence = "license.txt inside " + os.path.basename(path)
    else:
        ef = extracted_folder(path)
        if ef and os.path.exists(os.path.join(ef, "license.txt")):
            with open(os.path.join(ef, "license.txt"), encoding="utf-8", errors="replace") as f:
                text = f.read()
            evidence = rel(os.path.join(ef, "license.txt"))
    if not text:
        return None
    r = parse_sketchfab_license(text)
    r["evidence"] = evidence
    return r


def write_sidecar(path, r):
    sc = path + ".license.txt"
    mtime = datetime.datetime.fromtimestamp(os.path.getmtime(path)).strftime("%Y-%m-%d")
    credit = r.get("credit") or f"\"{r.get('title', '')}\" by {r.get('author', '')} ({r.get('url', '')}), licensed under {r.get('licence', '')}"
    with open(sc, "w", encoding="utf-8", newline="\n") as f:
        f.write(f"File: {os.path.basename(path)}\nTitle: {r.get('title', '')}\nSource: {r.get('url', '')}\nAuthor: {r.get('author', '')}\n"
                f"Licence: {r.get('licence', '')}\nAttribution: {credit}\nDownloaded: {mtime}\nEvidence: {r.get('evidence', '')}\n"
                f"Recorded by: Tools/Assets/licence_gate.py (card B1)\n")
    return sc


def quarantine(path, dry):
    moved = []
    targets = [path] + sidecars(path)
    ef = extracted_folder(path) if os.path.isfile(path) else None
    if ef:
        targets.append(ef)
    for t in targets:
        dest = os.path.join(Q, os.path.relpath(t, DL))
        moved.append(rel(dest))
        if not dry:
            os.makedirs(os.path.dirname(dest), exist_ok=True)
            if os.path.exists(dest):
                continue
            shutil.move(t, dest)
    return moved


def main():
    dry = "--dry-run" in sys.argv
    results = []
    for p in items():
        name = os.path.basename(p)
        kind = "folder" if os.path.isdir(p) else os.path.splitext(name)[1].lower().lstrip(".")
        entry = {"file": rel(p), "kind": kind, "size_mb": round((os.path.getsize(p) if os.path.isfile(p) else sum(os.path.getsize(os.path.join(a, b)) for a, _, bs in os.walk(p) for b in bs)) / 1048576, 1)}
        if RIPPED.search(name):
            entry.update(decision="QUARANTINE", reason="looks ripped from a commercial game (name match; never opened)", licence="")
        else:
            r = record_for(p)
            if r is None:
                entry.update(decision="QUARANTINE", reason="no licence record (no sidecar, no license.txt in the zip or extracted folder)", licence="")
            else:
                decision, reason = classify(r.get("licence", ""))
                entry.update({k: r.get(k, "") for k in ("licence", "title", "author", "url", "credit", "evidence")}, decision=decision, reason=reason)
                brand = r.get("real_brand") or (REAL_BRANDS.search(r.get("title", "") + " " + name).group(0) if REAL_BRANDS.search(r.get("title", "") + " " + name) else "")
                if brand:
                    entry["real_brand"] = brand
                    entry["note"] = "real brand: only rebadged under a fictional name with all marks removed (user decision 2026-11-10)"
                if decision == "ACCEPT" and os.path.isfile(p) and not sidecars(p) and not dry:
                    entry["sidecar"] = rel(write_sidecar(p, r))
                elif decision == "ACCEPT":
                    entry["sidecar"] = r.get("sidecar") or (rel(p + ".license.txt") if os.path.exists(p + ".license.txt") else "")
        if entry["decision"] == "QUARANTINE":
            entry["moved_to"] = quarantine(p, dry)
        results.append(entry)
    # Already quarantined earlier (idempotent re-runs).
    if os.path.isdir(Q):
        for a, ds, fs in os.walk(Q):
            for f in fs:
                if f.lower().endswith(ASSET_EXT):
                    rp = rel(os.path.join(a, f))
                    if not any(rp in e.get("moved_to", []) for e in results):
                        results.append({"file": rp, "kind": os.path.splitext(f)[1].lstrip("."), "decision": "QUARANTINE", "reason": "already in _Quarantine", "licence": ""})

    os.makedirs(os.path.join(ROOT, "Logs", "B1"), exist_ok=True)
    with open(os.path.join(ROOT, "Logs", "B1", "licence_gate.json"), "w", encoding="utf-8") as f:
        json.dump({"date": datetime.datetime.now().isoformat(timespec="seconds"), "dry_run": dry, "items": results}, f, indent=1)
    acc = [e for e in results if e["decision"] == "ACCEPT"]
    qua = [e for e in results if e["decision"] == "QUARANTINE"]
    lines = [f"Licence gate (card B1) {datetime.datetime.now():%Y-%m-%d %H:%M} {'DRY RUN ' if dry else ''}- {len(acc)} accepted, {len(qua)} quarantined"]
    for e in results:
        lines.append(f"{e['decision']:<10} {e['file']}  [{e.get('licence', '')}]  {e['reason'] if e['decision'] == 'QUARANTINE' else e.get('author', '')}{'  ** ' + e['note'] if e.get('note') else ''}")
    txt = "\n".join(lines) + "\n"
    with open(os.path.join(ROOT, "Logs", "B1", "licence_gate.txt"), "w", encoding="utf-8") as f:
        f.write(txt)
    if not dry:
        os.makedirs(Q, exist_ok=True)
        with open(os.path.join(Q, "QUARANTINE.md"), "w", encoding="utf-8", newline="\n") as f:
            f.write("# Quarantined downloads (card B1)\n\nNever process, import or open these files. They were moved here by `Tools/Assets/licence_gate.py`; nothing was deleted. "
                    "Decide per file: delete it, or add proof of a commercial-use licence and move it back.\n\n| File | Reason | Licence |\n|---|---|---|\n")
            for e in qua:
                f.write(f"| `{e['file']}` | {e['reason']} | {e.get('licence', '')} |\n")
        write_asset_sources(acc, qua)
    print(txt)


def write_asset_sources(acc, qua):
    vehicles_map = {}
    cm = os.path.join(ROOT, "ArtSource", "Vehicles", "car_map.json")
    if os.path.exists(cm):
        try:
            with open(cm, encoding="utf-8") as f:
                j = json.load(f)
            for k, v in (j.items() if isinstance(j, dict) else []):
                src = json.dumps(v)
                for e in acc:
                    stem = os.path.splitext(os.path.basename(e["file"]))[0]
                    if stem in src:
                        vehicles_map.setdefault(e["file"], []).append(k)
        except (ValueError, OSError):
            pass
    p = os.path.join(ROOT, "Docs", "ASSET_SOURCES.md")
    with open(p, "w", encoding="utf-8", newline="\n") as f:
        f.write("# Asset sources (card B1)\n\nGenerated by `Tools/Assets/licence_gate.py` from the licence records in `ArtSource/Downloads` "
                "(`*.license.txt`, `*.licence.json`, or the `license.txt` shipped inside each download). Accepted downloads only; quarantined files are "
                "listed in `ArtSource/Downloads/_Quarantine/QUARANTINE.md`. Procedural textures (`Assets/_Veyra/Art/Textures/Palette`, card F2) are "
                "generated in the project and need no record.\n\n| Asset | File | Source | Author | Licence | Attribution | Used in |\n|---|---|---|---|---|---|---|\n")
        for e in acc:
            used = ", ".join(f"`Art/Vehicles/{v}`" for v in vehicles_map.get(e["file"], [])) or ("not used (map download; see `Logs/triage.json`)" if "FullMapModels" in e["file"] else "see `Docs/THIRD_PARTY_LICENSE_REGISTER.csv`")
            note = " (real brand: rebadged only)" if e.get("real_brand") else ""
            f.write(f"| {e.get('title', '')}{note} | `{e['file']}` | {e.get('url', '')} | {e.get('author', '')} | {e.get('licence', '')} | {e.get('credit', '').replace('|', '/')} | {used} |\n")
        f.write(f"\nQuarantined: {len(qua)} file(s), see `ArtSource/Downloads/_Quarantine/QUARANTINE.md`.\n")


if __name__ == "__main__":
    main()

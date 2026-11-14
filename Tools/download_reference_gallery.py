"""Download curated gameplay & rendering reference screenshots for Veyra City.

Usage:
    python Tools/download_reference_gallery.py
    python Tools/download_reference_gallery.py --extract-videos
    python Tools/download_reference_gallery.py --offline-init

Visual benchmark ONLY (CLAUDE.md rule):
  - Never import reference images, logos, brands, or game assets into Assets/.
  - Use images strictly to inspect lighting, reflections, road grime, foliage silhouettes,
    fog density, camera framing, and neon layering against Veyra's review screenshots.
"""

from __future__ import annotations

import argparse
import html
import json
import os
import shutil
import subprocess
import sys
import urllib.error
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
OUT_DIR = ROOT / "ReferenceImages"
RAW_VIDEO_DIR = OUT_DIR / "_RawVideos"

STEAM_APPS = [
    ("3240220", "01_GTAV_Enhanced_City_Day", "gtav_enhanced", "GTA V Enhanced (Official Steam Store)"),
    ("271590", "01_GTAV_Enhanced_City_Day", "gtav_legacy", "GTA V Legacy (Official Steam Store)"),
    ("1091500", "02_Cyberpunk2077_Neon_WetNight", "cp2077", "Cyberpunk 2077 (Official Steam Store)"),
    ("1815780", "03_Asphalt_Legends_Nitro_HUD", "asphalt_legends", "Asphalt Legends Unite (Official Steam Store)"),
    ("1222680", "04_NFS_StreetRacing_Rain_Night", "nfs_heat", "Need for Speed Heat (Official Steam Store)"),
    ("1846380", "04_NFS_StreetRacing_Rain_Night", "nfs_unbound", "Need for Speed Unbound (Official Steam Store)"),
    ("1551360", "05_Forza_Crew_Biomes_Roads_Nature", "fh5", "Forza Horizon 5 (Official Steam Store)"),
    ("2698940", "05_Forza_Crew_Biomes_Roads_Nature", "crew_motorfest", "The Crew Motorfest (Official Steam Store)"),
]

TECH_STUDIES = [
    # Courreges GTA V Graphics Study (Docs/VISUAL_BENCHMARK_REPORT.md section 8)
    ("06_Rendering_Studies_NVIDIA_Courreges", "gtav_study_00_final_frame.jpg",
     "https://www.adriancourreges.com/img/blog/2015/gtav/a/00_final_frame.jpg",
     "GTA V finished daytime street composition"),
    ("06_Rendering_Studies_NVIDIA_Courreges", "gtav_study_06_shadow_cascades.jpg",
     "https://www.adriancourreges.com/img/blog/2015/gtav/a/06_shadow.jpg",
     "GTA V 4 shadow cascades"),
    ("06_Rendering_Studies_NVIDIA_Courreges", "gtav_study_20_light_shafts.jpg",
     "https://www.adriancourreges.com/img/blog/2015/gtav/a/20_light_shaft.jpg",
     "GTA V half-res light shafts"),
    ("06_Rendering_Studies_NVIDIA_Courreges", "gtav_study_25_atmospheric_fog.jpg",
     "https://www.adriancourreges.com/img/blog/2015/gtav/a/25_atm_fog.jpg",
     "GTA V height fog hiding distant low-LOD geometry"),
    ("06_Rendering_Studies_NVIDIA_Courreges", "gtav_study_27_sky_clouds.jpg",
     "https://www.adriancourreges.com/img/blog/2015/gtav/a/27_atm_cloud.jpg",
     "GTA V sky dome & cloud ring"),
    ("06_Rendering_Studies_NVIDIA_Courreges", "gtav_study_night_bulb_sprites.jpg",
     "https://www.adriancourreges.com/img/blog/2015/gtav/b/01_bulb_after.jpg",
     "GTA V distant city made of light sprites"),
    ("06_Rendering_Studies_NVIDIA_Courreges", "gtav_study_wet_road_streaks.jpg",
     "https://www.adriancourreges.com/img/blog/2015/gtav/b/40_light_refl.jpg",
     "GTA V headlight and streetlamp streaks on wet tarmac"),
    ("06_Rendering_Studies_NVIDIA_Courreges", "gtav_study_little_seoul_lod.png",
     "https://www.adriancourreges.com/img/blog/2015/gtav/b/05_little_seoul.png",
     "GTA V district HLOD / proxy mesh density"),
    ("06_Rendering_Studies_NVIDIA_Courreges", "gtav_study_anamorphic_flares.jpg",
     "https://www.adriancourreges.com/img/blog/2015/gtav/c/03_anamorphic_2.jpg",
     "GTA V anamorphic headlight streaks"),
    ("06_Rendering_Studies_NVIDIA_Courreges", "gtav_study_39_minimap.png",
     "https://www.adriancourreges.com/img/blog/2015/gtav/a/39_map.png",
     "GTA V vector road minimap tiles"),
    # NVIDIA GTA V PC Guide
    ("06_Rendering_Studies_NVIDIA_Courreges", "nvidia_gtav_rain_night_reflections_ultra.png",
     "https://international.download.nvidia.com/geforce-com/international/images/grand-theft-auto-v/grand-theft-auto-v-pc-reflection-quality-003-ultra.png",
     "GTA V rain-night reflections"),
    ("06_Rendering_Studies_NVIDIA_Courreges", "nvidia_gtav_population_density_100pct.png",
     "https://international.download.nvidia.com/geforce-com/international/images/grand-theft-auto-v/grand-theft-auto-v-pc-population-density-001-100-percent.png",
     "GTA V street density: cars, pedestrians, furniture"),
    ("06_Rendering_Studies_NVIDIA_Courreges", "nvidia_gtav_dusk_post_fx_ultra.png",
     "https://international.download.nvidia.com/geforce-com/international/images/grand-theft-auto-v/grand-theft-auto-v-pc-post-fx-001-ultra.png",
     "GTA V dusk bloom, haze and flare"),
    # NVIDIA Cyberpunk 2077 RT Overdrive / Ray Reconstruction Comparisons
    ("06_Rendering_Studies_NVIDIA_Courreges", "nvidia_cp2077_alley_raster_off.jpg",
     "https://images.nvidia.com/aem-dam/Solutions/geforce/news/cyberpunk-2077-ray-tracing-overdrive-update-launches-april-11/cp2077-alleyway2-rtx-off-badged.jpg",
     "Cyberpunk 2077 wet alley (raster baseline)"),
    ("06_Rendering_Studies_NVIDIA_Courreges", "nvidia_cp2077_alley_rt_on.jpg",
     "https://images.nvidia.com/aem-dam/Solutions/geforce/news/cyberpunk-2077-ray-tracing-overdrive-update-launches-april-11/cp2077-alleyway2-rtx-on-badged.jpg",
     "Cyberpunk 2077 wet alley (RT target look)"),
    ("06_Rendering_Studies_NVIDIA_Courreges", "nvidia_cp2077_shadows_raster_off.jpg",
     "https://images.nvidia.com/aem-dam/Solutions/geforce/news/cyberpunk-2077-ray-tracing-overdrive-update-launches-april-11/cp2077-shadows2-rt-off-badged.jpg",
     "Cyberpunk 2077 local light shadow density (off)"),
    ("06_Rendering_Studies_NVIDIA_Courreges", "nvidia_cp2077_shadows_rt_on.jpg",
     "https://images.nvidia.com/aem-dam/Solutions/geforce/news/cyberpunk-2077-ray-tracing-overdrive-update-launches-april-11/cp2077-shadows2-rt-on-badged.jpg",
     "Cyberpunk 2077 local light shadow density (on)"),
    ("06_Rendering_Studies_NVIDIA_Courreges", "nvidia_cp2077_car_grounding_off.jpg",
     "https://images.nvidia.com/aem-dam/Solutions/geforce/news/cyberpunk-2077-ray-tracing-overdrive-update-launches-april-11/cp2077-porschedof-rtx-off-badged.jpg",
     "Car contact shadow & street grounding benchmark (off)"),
    ("06_Rendering_Studies_NVIDIA_Courreges", "nvidia_cp2077_car_grounding_on.jpg",
     "https://images.nvidia.com/aem-dam/Solutions/geforce/news/cyberpunk-2077-ray-tracing-overdrive-update-launches-april-11/cp2077-porschedof-rtx-on-badged.jpg",
     "Car contact shadow & street grounding benchmark (on)"),
    ("06_Rendering_Studies_NVIDIA_Courreges", "nvidia_cp2077_dogtown_rtxoff.jpg",
     "https://images.nvidia.com/aem-dam/Solutions/geforce/news/cyberpunk-2077-ray-tracing-overdrive-update-launches-april-11/cyberpunk-2077-phantom-liberty-ray-tracing-overdrive-dlss-3-5-ray-reconstruction-comparison-rtxoff-001.jpg",
     "Dogtown dense street clutter & overcast light (raster)"),
    ("06_Rendering_Studies_NVIDIA_Courreges", "nvidia_cp2077_dogtown_rtxon.jpg",
     "https://images.nvidia.com/aem-dam/Solutions/geforce/news/cyberpunk-2077-ray-tracing-overdrive-update-launches-april-11/cyberpunk-2077-phantom-liberty-ray-tracing-overdrive-dlss-3-5-ray-reconstruction-comparison-rtxon-001.jpg",
     "Dogtown dense street clutter & light spill (RT)"),
    ("06_Rendering_Studies_NVIDIA_Courreges", "nvidia_cp2077_neon_reflections.jpg",
     "https://images.nvidia.com/aem-dam/Solutions/geforce/news/gamescom-ct0037/dlss-3-5-ray-reconstruction-improves-cyberpunk-2077-reflections.jpg",
     "Neon & billboard reflections on wet city street"),
]

FOLDER_GUIDE = [
    ("01_GTAV_Enhanced_City_Day",
     "Daytime urban realism, skyscraper materials, paved plazas, palm/broadleaf foliage density, dusk haze, highways, airport/port scale — target for D1 Crown Downtown, D2 Azure Coast, D3 Ironport, D5 Westhaven, D7 Airfield."),
    ("02_Cyberpunk2077_Neon_WetNight",
     "Layered 3D neon signs, gritty wet alleys, overhead cables, steam/fog light scatter, puddles mirroring emissives, storefront depth — target for D6 Neon Mile & Downtown night."),
    ("03_Asphalt_Legends_Nitro_HUD",
     "Aggressive chase camera framing, motion blur + speed lines, saturated clear-coat car reflections, multi-tier nitro shockwave flames, colourful drift/race tracks — target for VehicleFX, ChaseCamera, CarDetailFX."),
    ("04_NFS_StreetRacing_Rain_Night",
     "Rain street racing, realistic raindrop streaks, tail-light reflection trails, wet road clear-coat sheen without icy frost glare — target for Rain/WetNight shaders & Lens/VFX fix."),
    ("05_Forza_Crew_Biomes_Roads_Nature",
     "Rural highways, dense pine forests, mountain switchbacks with guardrails/rocks, sandy beaches, lakes, farmland fields, overcast/golden skies — target for D4 Blackridge, D8 Redwood Valley, D2 beaches, road crowns."),
    ("06_Rendering_Studies_NVIDIA_Courreges",
     "Frame-by-frame shadow cascade, distant light sprite, anamorphic headlight, SSR puddle, and car-grounding technical references."),
    ("07_Video_Keyframes",
     "Auto-extracted frames from any gameplay videos you place in ReferenceImages/_RawVideos/ (use --extract-videos)."),
]

HEADERS = {
    "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0 Safari/537.36"
}
IMAGE_EXTS = {".jpg", ".jpeg", ".png", ".webp"}


def is_valid_image(data: bytes) -> bool:
    if len(data) < 2048:
        return False
    if data[:3] == b"\xff\xd8\xff":  # JPEG
        return True
    if data[:8] == b"\x89PNG\r\n\x1a\n":  # PNG
        return True
    if data[:4] == b"RIFF" and data[8:12] == b"WEBP":  # WEBP
        return True
    return False


def download_bytes(url: str, timeout: int = 25) -> bytes:
    req = urllib.request.Request(url, headers=HEADERS)
    with urllib.request.urlopen(req, timeout=timeout) as resp:
        return resp.read()


def ensure_folders() -> None:
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    RAW_VIDEO_DIR.mkdir(parents=True, exist_ok=True)
    for folder, _ in FOLDER_GUIDE:
        (OUT_DIR / folder).mkdir(parents=True, exist_ok=True)
    gitignore = OUT_DIR / ".gitignore"
    if not gitignore.exists():
        gitignore.write_text(
            "*.jpg\n*.jpeg\n*.png\n*.webp\n*.mp4\n*.mkv\n*.webm\n*.mov\n_RawVideos/\n",
            encoding="utf-8",
        )


def fetch_steam_screenshots(max_per_game: int = 12) -> tuple[int, int, list[str]]:
    ok, fail, notes = 0, 0, []
    for appid, folder, slug, label in STEAM_APPS:
        api_url = f"https://store.steampowered.com/api/appdetails?appids={appid}&l=english"
        try:
            payload = json.loads(download_bytes(api_url).decode("utf-8"))
        except Exception as exc:
            fail += 1
            notes.append(f"- FAIL `{label}` (App {appid}): `{exc}`")
            continue

        entry = payload.get(appid, {})
        shots = entry.get("data", {}).get("screenshots", []) if entry.get("success") else []
        if not shots:
            fail += 1
            notes.append(f"- WARN `{label}` (App {appid}): no screenshots returned")
            continue

        saved_here = 0
        for idx, shot in enumerate(shots[:max_per_game], start=1):
            img_url = shot.get("path_full") or shot.get("path_thumbnail")
            if not img_url:
                continue
            clean_url = img_url.split("?")[0]
            ext = Path(clean_url).suffix.lower()
            if ext not in IMAGE_EXTS:
                ext = ".jpg"
            dest = OUT_DIR / folder / f"{slug}_{idx:02d}{ext}"
            if dest.exists() and dest.stat().st_size > 2048:
                saved_here += 1
                ok += 1
                continue
            try:
                raw = download_bytes(img_url)
                if not is_valid_image(raw):
                    raise ValueError("Response was not a valid image")
                dest.write_bytes(raw)
                saved_here += 1
                ok += 1
            except Exception as exc:
                fail += 1
                notes.append(f"- FAIL `{dest.name}`: `{exc}`")
        notes.append(f"- OK `{label}`: `{saved_here}` screenshot(s) in `ReferenceImages/{folder}/`")
    return ok, fail, notes


def fetch_tech_studies() -> tuple[int, int, list[str]]:
    ok, fail, notes = 0, 0, []
    for folder, filename, url, desc in TECH_STUDIES:
        dest = OUT_DIR / folder / filename
        if dest.exists() and dest.stat().st_size > 2048:
            ok += 1
            continue
        try:
            raw = download_bytes(url)
            if not is_valid_image(raw):
                raise ValueError("Response was not a valid image")
            dest.write_bytes(raw)
            ok += 1
        except Exception as exc:
            fail += 1
            notes.append(f"- FAIL `{filename}` ({desc}): `{exc}`")
    if ok:
        notes.insert(0, f"- OK Rendering study stills present: `{ok}`")
    return ok, fail, notes


def extract_videos(interval_sec: int = 8, max_frames_per_video: int = 12) -> None:
    ffmpeg = shutil.which("ffmpeg")
    videos = [
        p for p in sorted(RAW_VIDEO_DIR.iterdir())
        if p.is_file() and p.suffix.lower() in {".mp4", ".mkv", ".webm", ".mov", ".avi"}
    ]
    if not videos:
        print(f"[Video Extract] No video clips in {RAW_VIDEO_DIR}. Drop clips there and re-run with --extract-videos.")
        return
    if not ffmpeg:
        print("[Video Extract] ffmpeg not found in PATH. Install with: winget install Gyan.FFmpeg")
        return

    out_vid_dir = OUT_DIR / "07_Video_Keyframes"
    out_vid_dir.mkdir(parents=True, exist_ok=True)
    for clip in videos:
        stem = "".join(ch if ch.isalnum() or ch in "-_" else "_" for ch in clip.stem)
        pattern = str(out_vid_dir / f"{stem}_%03d.jpg")
        cmd = [
            ffmpeg, "-y", "-i", str(clip),
            "-vf", f"fps=1/{max(1, interval_sec)},scale='min(1920,iw)':-1",
            "-frames:v", str(max_frames_per_video),
            "-q:v", "2", pattern,
        ]
        print("[Video Extract]", clip.name)
        subprocess.run(cmd, check=False)


def build_manifest_and_gallery() -> int:
    total_images = 0
    lines = [
        "# VEYRA CITY — Visual Reference Gallery for Claude Code Opus",
        "",
        "**STRICT RULE (CLAUDE.md):** All images in this folder are **VISUAL TARGET BENCHMARKS ONLY**.",
        "- Never import images, brand names, logos, HUD artwork, or map layouts into `Assets/`.",
        "- Open and visually inspect these images alongside Veyra screenshots (`Screenshots/2026-11-10_iter32/`, `2026-11-10_V1_player/`) to diagnose lighting ratios, specular roughness, tree silhouettes, road wear, storefront depth, volumetric fog, and camera speed framing.",
        "",
        "## How to populate (if folders are currently empty)",
        "",
        "Run from an online Windows terminal:",
        "```bat",
        "Tools\\download_references.cmd",
        "```",
        "Optional: drop recorded gameplay clips (`.mp4`/`.mkv`/`.webm`) into `ReferenceImages/_RawVideos/` and run:",
        "```bat",
        "python Tools\\download_reference_gallery.py --extract-videos",
        "```",
        "",
        "## Reference Categories & What Claude Should Compare",
        "",
    ]

    html_sections: list[str] = []
    for folder, purpose in FOLDER_GUIDE:
        fdir = OUT_DIR / folder
        imgs = sorted(
            p for p in fdir.iterdir()
            if p.is_file() and p.suffix.lower() in IMAGE_EXTS
        ) if fdir.exists() else []
        total_images += len(imgs)

        lines.append(f"### `{folder}/` ({len(imgs)} images)")
        lines.append(f"- **Target for Veyra:** {purpose}")
        if imgs:
            for p in imgs:
                lines.append(f"  - `ReferenceImages/{folder}/{p.name}`")
        else:
            lines.append("  - *(Empty — run `Tools\\download_references.cmd` online or paste gameplay screenshots here)*")
        lines.append("")

        cards = []
        for p in imgs:
            rel = f"{folder}/{p.name}"
            esc = html.escape(p.name)
            cards.append(
                f'<a class="card" href="{html.escape(rel)}" target="_blank">'
                f'<img src="{html.escape(rel)}" loading="lazy" alt="{esc}"/>'
                f'<span>{esc}</span></a>'
            )
        empty_note = '<p class="empty">Empty — run <code>Tools\\download_references.cmd</code> online or drop PNG/JPG screenshots into this folder.</p>' if not cards else ""
        html_sections.append(
            f'<section><h2>{html.escape(folder)} ({len(imgs)})</h2>'
            f'<p class="purpose">{html.escape(purpose)}</p>'
            f'{empty_note}<div class="grid">{"".join(cards)}</div></section>'
        )

    lines.extend([
        "## Curated Direct URL List (Included in Downloader)",
        "- Steam App `3240220` — GTA V Enhanced (`01_GTAV_Enhanced_City_Day/`)",
        "- Steam App `271590` — GTA V Legacy (`01_GTAV_Enhanced_City_Day/`)",
        "- Steam App `1091500` — Cyberpunk 2077 (`02_Cyberpunk2077_Neon_WetNight/`)",
        "- Steam App `1815780` — Asphalt Legends Unite (`03_Asphalt_Legends_Nitro_HUD/`)",
        "- Steam App `1222680` — Need for Speed Heat (`04_NFS_StreetRacing_Rain_Night/`)",
        "- Steam App `1846380` — Need for Speed Unbound (`04_NFS_StreetRacing_Rain_Night/`)",
        "- Steam App `1551360` — Forza Horizon 5 (`05_Forza_Crew_Biomes_Roads_Nature/`)",
        "- Steam App `2698940` — The Crew Motorfest (`05_Forza_Crew_Biomes_Roads_Nature/`)",
        f"- Verified Adrian Courreges + NVIDIA Graphics Study Stills: {len(TECH_STUDIES)} images (`06_Rendering_Studies_NVIDIA_Courreges/`)",
    ])

    (OUT_DIR / "MANIFEST_FOR_CLAUDE.md").write_text("\n".join(lines) + "\n", encoding="utf-8")

    html_doc = f"""<!doctype html>
<html lang="en"><head><meta charset="utf-8"/>
<title>Veyra City — Visual Reference Gallery ({total_images} images)</title>
<style>
body {{ font-family: system-ui, sans-serif; background: #10131a; color: #e6ebf5; margin: 24px; }}
h1 {{ margin-bottom: 6px; }}
.sub {{ color: #9aa7bd; margin-bottom: 28px; }}
section {{ margin-bottom: 36px; border-top: 1px solid #252c3b; padding-top: 18px; }}
.purpose {{ color: #a7b4cc; font-size: 14px; }}
.empty {{ color: #f0ad4e; font-size: 14px; }}
.grid {{ display: grid; grid-template-columns: repeat(auto-fill, minmax(260px, 1fr)); gap: 12px; }}
.card {{ background: #181d28; border: 1px solid #273042; border-radius: 6px; overflow: hidden; text-decoration: none; color: #d5deef; display: flex; flex-direction: column; }}
.card img {{ width: 100%; height: 155px; object-fit: cover; display: block; }}
.card span {{ padding: 6px 8px; font-size: 12px; word-break: break-all; }}
</style></head>
<body>
<h1>Veyra City — AAA Visual Reference Gallery ({total_images} images)</h1>
<p class="sub">Reference only (do not import into Unity Assets/). Generated by Tools/download_reference_gallery.py</p>
{''.join(html_sections)}
</body></html>
"""
    (OUT_DIR / "GALLERY.html").write_text(html_doc, encoding="utf-8")
    return total_images


def main() -> int:
    parser = argparse.ArgumentParser(description="Download Veyra City gameplay & rendering reference pack.")
    parser.add_argument("--offline-init", action="store_true", help="Create folders, manifest, and HTML gallery without network calls.")
    parser.add_argument("--extract-videos", action="store_true", help="Extract keyframes from clips placed in ReferenceImages/_RawVideos/.")
    parser.add_argument("--max-per-game", type=int, default=12, help="Max Steam screenshots per game (default 12).")
    parser.add_argument("--interval", type=int, default=8, help="Video frame extraction interval in seconds (default 8).")
    args = parser.parse_args()

    ensure_folders()
    steam_notes: list[str] = []
    tech_notes: list[str] = []

    if not args.offline_init:
        print("Downloading official Steam promotional screenshots...")
        s_ok, s_fail, steam_notes = fetch_steam_screenshots(args.max_per_game)
        print(f"Steam: {s_ok} downloaded/verified, {s_fail} errors")
        for n in steam_notes:
            print(" ", n)

        print("Downloading NVIDIA & Courreges graphics study frames...")
        t_ok, t_fail, tech_notes = fetch_tech_studies()
        print(f"Tech studies: {t_ok} downloaded/verified, {t_fail} errors")
        for n in tech_notes:
            print(" ", n)

    if args.extract_videos:
        extract_videos(interval_sec=args.interval)

    total = build_manifest_and_gallery()
    print(f"Updated ReferenceImages/MANIFEST_FOR_CLAUDE.md and ReferenceImages/GALLERY.html ({total} total images).")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())


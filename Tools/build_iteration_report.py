"""
Builds the self-contained visual iteration report (HTML, images embedded as JPEG data URIs).

  python -I Tools/build_iteration_report.py <out.html> <jpeg_dir>

<jpeg_dir> holds sheet_iterNN.jpg (one per iteration, made by Tools/Blender/to_jpeg.py from
Screenshots/<date>_iterNN/sheet.png) and optional cars.jpg / crash_final.jpg. Data: Docs/visual_iterations.json
and Logs/A8/handling_benchmark.txt.
"""
import base64
import html
import json
import os
import sys

out, jdir = sys.argv[1], sys.argv[2]
root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
data = json.load(open(os.path.join(root, "Docs", "visual_iterations.json"), encoding="utf-8"))
rubric = data["rubric"]
its = data["iterations"]
last = its[-1]


def img(name):
    p = os.path.join(jdir, name)
    if not os.path.exists(p):
        return None
    return "data:image/jpeg;base64," + base64.b64encode(open(p, "rb").read()).decode()


def avg(scores):
    v = [s for s in scores if s is not None]
    return sum(v) / len(v) if v else 0.0


# ---------------------------------------------------------------- handling benchmark
bench = []
bp = os.path.join(root, "Logs", "A8", "handling_benchmark.txt")
if os.path.exists(bp):
    for line in open(bp, encoding="utf-8").read().splitlines()[1:]:
        parts = [x.strip() for x in line.split("|")]
        if len(parts) >= 3:
            try:
                bench.append((parts[0], float(parts[1]), float(parts[2]), float(parts[5]) if len(parts) > 5 else None))
            except ValueError:
                pass
names = {
    "lct_3000_95": "LCT 3000 (truck)", "shvan_92": "Shvan (van)", "bokaroo_67": "Bokaroo (off-road)",
    "canyon_75_taxi": "Canyon taxi", "fairheaven_lt": "Fairheaven LT", "coupe_33_hotrod_1": "Coupe '33 hot rod",
    "iron_howl": "Iron Howl (muscle)", "phoenix_455": "Phoenix 455 (muscle)", "kiri_86": "Kiri '86",
    "tiara_gt_83": "Tiara GT", "orion_skylark": "Orion Skylark (GT)", "ashcombe_tempest": "Ashcombe Tempest (GT)",
    "halcyon_regent": "Halcyon Regent (luxury)", "stanton_sidewinder": "Stanton Sidewinder",
    "v12_goblin": "V12 Goblin", "ardente_furia": "Ardente Furia", "nordvik_hex": "Nordvik Hex",
}
pick = [b for b in bench if b[0] in names]
pick.sort(key=lambda b: b[2])

# ---------------------------------------------------------------- pieces
cols = "".join(f'<th scope="col" title="{html.escape(t)}">{k}</th>' for k, t in rubric)
rows = []
for it in its:
    cells = []
    for s in it["scores"]:
        if s is None:
            cells.append('<td class="na">n/a</td>')
        else:
            cells.append(f'<td style="--s:{s / 5:.2f}"><span>{s:g}</span></td>')
    a = avg(it["scores"])
    rows.append(f'<tr><th scope="row">{it["n"]:02d}</th>{"".join(cells)}<td class="avg">{a:.2f}</td></tr>')
score_table = f'<table class="grid"><thead><tr><th scope="col">Iter.</th>{cols}<th scope="col">Mean</th></tr></thead><tbody>{"".join(rows)}</tbody></table>'

# frame-time chart (p99 ms per iteration), drawn to scale
W, H, PADL, PADB, PADT = 640, 220, 44, 30, 14
ymax = 60.0
pts = [(it["n"], it["p99"]) for it in its if it.get("p99") is not None]
nmax = max(it["n"] for it in its)


def X(n):
    return PADL + (n - 1) * (W - PADL - 16) / max(1, nmax - 1)


def Y(v):
    return PADT + (1 - min(v, ymax) / ymax) * (H - PADT - PADB)


grid = "".join(f'<line x1="{PADL}" x2="{W - 8}" y1="{Y(v):.1f}" y2="{Y(v):.1f}" class="gl"/><text x="{PADL - 8}" y="{Y(v) + 4:.1f}" class="ax" text-anchor="end">{v:g}</text>' for v in (0, 20, 40, 60))
target = f'<line x1="{PADL}" x2="{W - 8}" y1="{Y(16.6):.1f}" y2="{Y(16.6):.1f}" class="tg"/><text x="{W - 10}" y="{Y(16.6) - 5:.1f}" class="tgl" text-anchor="end">16.6 ms (60 FPS)</text>'
poly = " ".join(f"{X(n):.1f},{Y(v):.1f}" for n, v in pts)
dots = "".join(f'<circle cx="{X(n):.1f}" cy="{Y(v):.1f}" r="{4.5 if n == pts[-1][0] else 3}" class="pt"/><text x="{X(n):.1f}" y="{Y(v) - 9:.1f}" class="vl" text-anchor="middle">{v:g}</text>' for n, v in pts)
xl = "".join(f'<text x="{X(it["n"]):.1f}" y="{H - 10}" class="ax" text-anchor="middle">{it["n"]}</text>' for it in its)
ft_chart = f'<svg viewBox="0 0 {W} {H}" role="img" aria-label="p99 frame time per iteration">{grid}{target}<polyline points="{poly}" class="ln"/>{dots}{xl}</svg>'

# handling bars (skidpad g)
bars = []
gmin, gmax = 0.7, 1.3
BW = 640
for i, (cid, kg, g, ti) in enumerate(pick):
    w = (g - gmin) / (gmax - gmin) * (BW - 230)
    y = 8 + i * 22
    bars.append(f'<text x="200" y="{y + 12}" class="bl" text-anchor="end">{html.escape(names[cid])}</text>'
                f'<rect x="210" y="{y}" width="{max(2, w):.1f}" height="15" class="bar"/>'
                f'<text x="{214 + w:.1f}" y="{y + 12}" class="bv">{g:.2f} g</text>')
BH = 8 + len(pick) * 22 + 26
ref = (f'<rect x="{210 + (0.85 - gmin) / (gmax - gmin) * (BW - 230):.1f}" y="0" width="{(1.05 - 0.85) / (gmax - gmin) * (BW - 230):.1f}" height="{BH - 22}" class="band"/>'
       f'<text x="{210 + (0.95 - gmin) / (gmax - gmin) * (BW - 230):.1f}" y="{BH - 6}" class="ax" text-anchor="middle">real road cars 0.85–1.05 g</text>')
hand_chart = f'<svg viewBox="0 0 {BW} {BH}" role="img" aria-label="Skidpad grip by car">{ref}{"".join(bars)}</svg>' if pick else "<p>No benchmark file.</p>"

timeline = []
for it in its:
    src = img(f"sheet_iter{it['n']:02d}.jpg")
    pic = f'<img src="{src}" alt="Iteration {it["n"]}: eight scenes" loading="lazy">' if src else ""
    note = f'<p class="note">{html.escape(it["note"])}</p>' if it.get("note") else ""
    perf = f'{it["fps"]:.1f} FPS avg · p99 {it["p99"]:g} ms' if it.get("fps") else (f'p99 {it["p99"]:g} ms' if it.get("p99") else "")
    timeline.append(f'<article class="it"><header><span class="num">{it["n"]:02d}</span><h3>{html.escape(it["title"])}</h3><span class="perf">{perf}</span></header>{note}{pic}</article>')

first_img, last_img = img("sheet_iter01.jpg"), img(f"sheet_iter{last['n']:02d}.jpg")
cars_img, crash_img = img("cars.jpg"), img("crash_final.jpg")
a0, a1 = avg(its[0]["scores"]), avg(last["scores"])
scene_key = "Scenes, left to right: noon city · golden hour · neon night · rain night / nitro at 172 km/h · brake-tap drift · evening traffic · dusk close-up."

page = f'''<title>Veyra City Visual Iterations</title>
<meta name="claude:viewport-fit-sides" content="cover">
<link rel="preconnect" href="https://fonts.googleapis.com">
<link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=Barlow+Condensed:wght@500;600;700&family=IBM+Plex+Sans:wght@400;500;600&family=IBM+Plex+Mono:wght@400;500&display=swap">
<style>
/* Layout: one reading column (max 1080px); figures span it; the score grid and charts scroll inside their own boxes on phones */
:root {{
  --bg: #eceff2; --surface: #ffffff; --ink: #15191e; --muted: #5a6370; --line: #d2d8df; --accent: #c4711b; --accent-soft: #f3e3cf;
  --display: "Barlow Condensed", "Arial Narrow", "Roboto Condensed", sans-serif;
  --body: "IBM Plex Sans", "Segoe UI", system-ui, sans-serif;
  --mono: "IBM Plex Mono", ui-monospace, "Cascadia Mono", Consolas, monospace;
}}
@media (prefers-color-scheme: dark) {{ :root:not([data-theme="light"]) {{ --bg: #121518; --surface: #1a1e23; --ink: #e6e9ed; --muted: #98a2ae; --line: #2b3139; --accent: #efa142; --accent-soft: #3a2a17; color-scheme: dark; }} }}
:root[data-theme="dark"] {{ --bg: #121518; --surface: #1a1e23; --ink: #e6e9ed; --muted: #98a2ae; --line: #2b3139; --accent: #efa142; --accent-soft: #3a2a17; color-scheme: dark; }}
body {{ background: var(--bg); color: var(--ink); font: 400 15px/1.55 var(--body); }}
.wrap {{ max-width: 1080px; margin: 0 auto; padding-block: 40px 64px; padding-left: max(16px, env(safe-area-inset-left, 0px)); padding-right: max(16px, env(safe-area-inset-right, 0px)); display: grid; gap: 48px; }}
h1, h2, h3 {{ font-family: var(--display); font-weight: 600; letter-spacing: 0.01em; text-wrap: balance; margin: 0; }}
h1 {{ font-size: clamp(34px, 6vw, 56px); line-height: 1.02; }}
h2 {{ font-size: 28px; line-height: 1.1; }}
h3 {{ font-size: 21px; line-height: 1.15; }}
p {{ margin: 0; max-width: 68ch; }}
.eyebrow {{ font: 500 12px/1 var(--mono); letter-spacing: 0.12em; text-transform: uppercase; color: var(--accent); }}
.lede {{ display: grid; gap: 14px; }}
.lede p {{ color: var(--muted); font-size: 16px; }}
.stats {{ display: flex; flex-wrap: wrap; gap: 10px 28px; font: 500 13px/1.3 var(--mono); color: var(--muted); }}
.stats b {{ display: block; font: 600 26px/1.1 var(--display); color: var(--ink); }}
section {{ display: grid; gap: 16px; min-width: 0; }}
section > header {{ display: grid; gap: 6px; }}
section > header p {{ color: var(--muted); }}
figure {{ margin: 0; display: grid; gap: 8px; min-width: 0; }}
figure img {{ width: 100%; height: auto; display: block; border-radius: 4px; border: 1px solid var(--line); background: var(--surface); }}
figcaption {{ font: 400 13px/1.45 var(--body); color: var(--muted); }}
figcaption b {{ font: 600 12px/1 var(--mono); letter-spacing: 0.08em; text-transform: uppercase; color: var(--ink); margin-right: 8px; }}
.scroll {{ overflow-x: auto; min-width: 0; border: 1px solid var(--line); border-radius: 4px; background: var(--surface); }}
table.grid {{ border-collapse: collapse; width: 100%; min-width: 640px; font: 500 13px/1 var(--mono); font-variant-numeric: tabular-nums; }}
.grid th, .grid td {{ padding: 9px 6px; text-align: center; border-bottom: 1px solid var(--line); }}
.grid thead th {{ color: var(--muted); font-weight: 500; letter-spacing: 0.06em; }}
.grid tbody th {{ color: var(--muted); font-weight: 500; }}
.grid td {{ background: color-mix(in srgb, var(--accent) calc(var(--s, 0) * 62%), var(--surface)); color: var(--ink); }}
.grid td.na {{ color: var(--muted); background: var(--surface); }}
.grid td.avg {{ font-weight: 600; background: var(--surface); border-left: 1px solid var(--line); }}
.charts {{ display: grid; grid-template-columns: repeat(auto-fit, minmax(300px, 1fr)); gap: 24px; }}
.chart {{ display: grid; gap: 10px; min-width: 0; }}
.chart svg {{ width: 100%; height: auto; display: block; }}
svg text {{ font-family: var(--mono); }}
.ax {{ font-size: 11px; fill: var(--muted); }}
.gl {{ stroke: var(--line); stroke-width: 1; }}
.tg {{ stroke: var(--accent); stroke-width: 1.2; stroke-dasharray: 5 4; }}
.tgl {{ font-size: 11px; fill: var(--accent); }}
.ln {{ fill: none; stroke: var(--ink); stroke-width: 2; }}
.pt {{ fill: var(--ink); }}
.vl {{ font-size: 11px; fill: var(--ink); }}
.bar {{ fill: var(--accent); }}
.band {{ fill: var(--accent-soft); }}
.bl {{ font-size: 12px; fill: var(--ink); }}
.bv {{ font-size: 11px; fill: var(--muted); }}
.timeline {{ display: grid; gap: 28px; }}
.it {{ display: grid; gap: 10px; min-width: 0; }}
.it header {{ display: flex; flex-wrap: wrap; align-items: baseline; gap: 6px 14px; }}
.it .num {{ font: 700 26px/1 var(--display); color: var(--accent); font-variant-numeric: tabular-nums; }}
.it h3 {{ flex: 1 1 260px; }}
.it .perf {{ font: 400 12px/1 var(--mono); color: var(--muted); font-variant-numeric: tabular-nums; }}
.it img {{ width: 100%; height: auto; display: block; border-radius: 4px; border: 1px solid var(--line); }}
.note {{ font-size: 13px; color: var(--muted); }}
.open {{ display: grid; gap: 10px; padding: 0; margin: 0; list-style: none; }}
.open li {{ display: grid; grid-template-columns: 120px 1fr; gap: 12px; padding: 12px 14px; background: var(--surface); border: 1px solid var(--line); border-radius: 4px; }}
.open .who {{ font: 500 11px/1.4 var(--mono); letter-spacing: 0.08em; text-transform: uppercase; color: var(--accent); }}
@media (max-width: 520px) {{ .open li {{ grid-template-columns: 1fr; gap: 4px; }} }}
</style>
<div class="wrap">
  <div class="lede">
    <span class="eyebrow">Veyra City · open-world street racing · Unity 6.6 URP</span>
    <h1>{len(its)} capture–compare–fix passes against GTA V, Asphalt and Cyberpunk</h1>
    <p>Each pass drives the same scripted scenes in the Dev build, scores them on ten criteria taken from the reference games, fixes the biggest gaps in the car, effects, street-life and handling code, rebuilds and captures again. Scores are 0 (missing) to 5 (reference level).</p>
    <div class="stats">
      <span><b>{a0:.2f} → {a1:.2f}</b>mean score, pass 1 → {last["n"]}</span>
      <span><b>{(str(last["p99"]) + " ms") if last.get("p99") else "n/a"}</b>p99 frame time, last pass</span>
      <span><b>{len(bench)}</b>cars in the handling benchmark</span>
      <span><b>26</b>drivable car models checked</span>
    </div>
  </div>

  <section>
    <header><h2>Before and after</h2><p>{scene_key}</p></header>
    {f'<figure><img src="{first_img}" alt="Pass 1 contact sheet"><figcaption><b>Pass 1</b>Bare pavements, flat sky, faint lamps, no traffic near the car.</figcaption></figure>' if first_img else ''}
    {f'<figure><img src="{last_img}" alt="Final pass contact sheet"><figcaption><b>Pass {last["n"]}</b>Cloud decks and city glow, lit headlights and tail-light spill, trees, walkers, hydrants and benches, signal-obeying traffic.</figcaption></figure>' if last_img else ''}
  </section>

  <section>
    <header><h2>Scores by pass</h2><p>R1 car look · R2 speed sensation · R3 wet night · R4 night lighting · R5 atmosphere · R6 sky · R7 street life · R8 street detail · R9 drift and effects · R10 frame time (p99 ≤ 16.6 ms at 1440p).</p></header>
    <div class="scroll">{score_table}</div>
  </section>

  <section>
    <header><h2>Frame time and handling</h2></header>
    <div class="charts">
      <div class="chart"><h3>p99 frame time per pass (ms)</h3>{ft_chart}<p class="note">Dev Mono player, 2560×1440, RTX 4070 Laptop. Pass 3's spike was full-detail street trees in every shadow cascade; pass 4 split them into near/far LOD.</p></div>
      <div class="chart"><h3>Skidpad grip at 80 km/h</h3>{hand_chart}<p class="note">Before pass 5 every car cornered at 1.05–1.18 g because one global yaw cap flattened them. Grip now follows the car class.</p></div>
    </div>
  </section>

  {f'<section><header><h2>The car line-up</h2><p>Every drivable model in the game, front three-quarter, same street at noon.</p></header><figure><img src="{cars_img}" alt="All drivable cars" loading="lazy"></figure></section>' if cars_img else ''}

  <section>
    <header><h2>Every pass</h2></header>
    <div class="timeline">{"".join(timeline)}</div>
  </section>

  <section>
    <header><h2>Still open</h2></header>
    <ul class="open">
      <li><span class="who">You</span><span>Revoke the Sketchfab API token hard-coded in the untracked <code>Tools/download_fancy_cars.py</code> (SEC-003). It was never committed.</span></li>
      <li><span class="who">Legal</span><span>The five rebadged cars (Ardente Furia, Halcyon Regent, Nordvik Hex, Ashcombe Tempest, Stanton Sidewinder) are CC-BY with all badges removed, but their body shapes still resemble real cars. They need a design check before any public release (licence register L-051 to L-055).</span></li>
      <li><span class="who">Lighting cards</span><span>Wet roads that read as frost, dark noon street canyons, street-lamp light pools and night fog belong to the lighting cards (L1–L5) of the other session; the sky layer from pass 5 is built for them to extend.</span></li>
    </ul>
  </section>
</div>
'''
open(out, "w", encoding="utf-8").write(page)
print("REPORT", out, len(page) // 1024, "KB")

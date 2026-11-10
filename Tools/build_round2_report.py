"""
Builds the self-contained round-2 visual iteration report (HTML, images embedded as JPEG data URIs).

  python -I Tools/build_round2_report.py <out.html> <jpeg_dir>

<jpeg_dir> holds the JPEGs named in FIGURES below (made by Tools/Blender/to_jpeg.py from the Screenshots/<date>_iterNN
captures). Data: Docs/visual_iterations_round2.json.
"""
import base64
import html
import json
import os
import sys

out, jdir = sys.argv[1], sys.argv[2]
root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
data = json.load(open(os.path.join(root, "Docs", "visual_iterations_round2.json"), encoding="utf-8"))
rubric, its = data["rubric"], data["iterations"]
first_n, last = its[0]["n"], its[-1]
meta = data.get("report", {})


def img(name):
    p = os.path.join(jdir, name)
    if not os.path.exists(p):
        return None
    return "data:image/jpeg;base64," + base64.b64encode(open(p, "rb").read()).decode()


def fig(name, caption, alt):
    src = img(name)
    if not src:
        return ""
    return f'<figure><img src="{src}" alt="{html.escape(alt)}" loading="lazy"><figcaption>{caption}</figcaption></figure>'


def pair(before, after, cap_b, cap_a, alt):
    a, b = fig(before, cap_b, alt + ", before"), fig(after, cap_a, alt + ", after")
    return f'<div class="pair">{a}{b}</div>' if a and b else (a or b)


def avg(scores):
    v = [s for s in scores if s is not None]
    return sum(v) / len(v) if v else None


# ---------------------------------------------------------------- scores grid
cols = "".join(f'<th scope="col" title="{html.escape(t)}">{k}</th>' for k, t in rubric)
rows = []
for it in its:
    cells = []
    for s in it["scores"]:
        if s is None:
            cells.append('<td class="na">–</td>')
        else:
            cells.append(f'<td style="--s:{(s - 1) / 4:.3f}">{s:g}</td>')
    a = avg(it["scores"])
    cells.append(f'<td class="avg">{a:.1f}</td>' if a is not None else '<td class="avg na">–</td>')
    rows.append(f'<tr><th scope="row">{it["n"]}</th><td class="title">{html.escape(it["title"])}</td>{"".join(cells)}</tr>')
grid = f'<table class="grid"><thead><tr><th scope="col">Pass</th><th scope="col" class="title">What changed</th>{cols}<th scope="col">Avg</th></tr></thead><tbody>{"".join(rows)}</tbody></table>'

# ---------------------------------------------------------------- frame time table
def num(v, fmt):
    return format(v, fmt) if v is not None else "–"


frows = "".join(
    f'<tr><th scope="row">{it["n"]}</th><td>{num(it["fps"], ".1f")}</td><td>{num(it["p99"], ".1f")}</td>'
    f'<td>{num(it["cruise_p99"], ".1f")}</td><td>{num(it["hitches50"], "d")}</td></tr>'
    for it in its)
ftable = ('<table class="frames"><thead><tr><th scope="col">Pass</th><th scope="col">Whole run avg FPS</th>'
          '<th scope="col">Whole run p99 ms</th><th scope="col">Cruise p99 ms</th><th scope="col">Frames over 50 ms</th>'
          f'</tr></thead><tbody>{frows}</tbody></table>')

first_avg = avg(next(i for i in its if avg(i["scores"]) is not None)["scores"])
last_avg = avg(last["scores"])
open_items = meta.get("open", [])
open_html = "".join(f'<li><b>{html.escape(k)}</b><span>{html.escape(v)}</span></li>' for k, v in open_items)
stats = meta.get("stats", [])
stats_html = "".join(f'<div class="stat"><span class="v">{html.escape(v)}</span><span class="k">{html.escape(k)}</span></div>' for k, v in stats)
F = meta.get("figures", {})


def section(title, lede, body):
    if not body.strip():
        return ""
    return f'<section><header><h2>{title}</h2><p>{lede}</p></header>{body}</section>'


map_body = "".join(fig(n, c, a) for n, c, a in F.get("map", []))
car_body = "".join(fig(n, c, a) for n, c, a in F.get("car", []))
world_body = "".join(pair(b, a, cb, ca, alt) for b, a, cb, ca, alt in F.get("world_pairs", [])) + \
             "".join(fig(n, c, a) for n, c, a in F.get("world", []))
street_body = "".join(fig(n, c, a) for n, c, a in F.get("street", []))

page = f"""<title>Veyra Round Two</title>
<meta name="claude:viewport-fit-sides" content="cover">
<link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=Barlow+Condensed:wght@500;600;700&family=IBM+Plex+Sans:wght@400;500;600&family=IBM+Plex+Mono:wght@400;500&display=swap">
<style>
/* Layout: one reading column of evidence (captures first, numbers after), asphalt-dark by default like the game's HUD */
:root {{
  --bg: #f3f4f6; --surface: #ffffff; --ink: #14171c; --muted: #5b6472; --line: #d9dde3; --accent: #0e9fbf; --hot: #d9367f;
  --display: "Barlow Condensed", "Arial Narrow", sans-serif; --body: "IBM Plex Sans", "Segoe UI", sans-serif; --mono: "IBM Plex Mono", Consolas, monospace;
}}
@media (prefers-color-scheme: dark) {{ :root:not([data-theme="light"]) {{ --bg: #0d1014; --surface: #151a20; --ink: #e8ecf1; --muted: #8d97a5; --line: #262d36; --accent: #35c8e8; --hot: #ff5aa5; color-scheme: dark; }} }}
:root[data-theme="dark"] {{ --bg: #0d1014; --surface: #151a20; --ink: #e8ecf1; --muted: #8d97a5; --line: #262d36; --accent: #35c8e8; --hot: #ff5aa5; color-scheme: dark; }}
* {{ box-sizing: border-box; }}
body {{ background: var(--bg); color: var(--ink); font: 400 15px/1.55 var(--body); }}
main {{ max-width: 1180px; margin: 0 auto; padding-block: 40px 64px; padding-left: max(16px, env(safe-area-inset-left, 0px)); padding-right: max(16px, env(safe-area-inset-right, 0px)); display: grid; gap: 56px; }}
h1, h2 {{ font-family: var(--display); font-weight: 700; letter-spacing: 0.01em; text-wrap: balance; margin: 0; }}
h1 {{ font-size: clamp(40px, 7vw, 72px); line-height: 0.95; text-transform: uppercase; }}
h1 em {{ font-style: normal; color: var(--accent); }}
h2 {{ font-size: 30px; text-transform: uppercase; }}
p {{ margin: 0; max-width: 68ch; }}
.hero {{ display: grid; gap: 18px; }}
.hero .kicker {{ font: 500 13px/1 var(--mono); letter-spacing: 0.12em; text-transform: uppercase; color: var(--muted); }}
.stats {{ display: grid; grid-template-columns: repeat(auto-fit, minmax(150px, 1fr)); gap: 10px; }}
.stat {{ display: grid; gap: 2px; padding: 12px 14px; border: 1px solid var(--line); border-radius: 4px; background: var(--surface); }}
.stat .v {{ font: 600 26px/1.1 var(--display); color: var(--ink); font-variant-numeric: tabular-nums; }}
.stat .k {{ font-size: 12.5px; color: var(--muted); }}
section {{ display: grid; gap: 16px; min-width: 0; }}
section > header {{ display: grid; gap: 6px; }}
section > header p {{ color: var(--muted); }}
figure {{ margin: 0; display: grid; gap: 6px; min-width: 0; }}
figure img {{ width: 100%; max-width: 100%; height: auto; display: block; border-radius: 4px; border: 1px solid var(--line); background: var(--surface); }}
figcaption {{ font-size: 13.5px; color: var(--muted); max-width: 90ch; }}
figcaption b {{ color: var(--ink); font-weight: 600; margin-right: 6px; }}
.pair {{ display: grid; grid-template-columns: 1fr 1fr; gap: 12px; }}
@media (max-width: 720px) {{ .pair {{ grid-template-columns: 1fr; }} }}
.scroll {{ overflow-x: auto; min-width: 0; border: 1px solid var(--line); border-radius: 4px; background: var(--surface); }}
table {{ border-collapse: collapse; width: 100%; font-variant-numeric: tabular-nums; }}
th, td {{ padding: 7px 10px; border-bottom: 1px solid var(--line); text-align: center; font-size: 13.5px; }}
thead th {{ font: 600 12px/1.2 var(--mono); letter-spacing: 0.06em; text-transform: uppercase; color: var(--muted); }}
td.title, th.title {{ text-align: left; min-width: 280px; }}
.grid td {{ background: color-mix(in srgb, var(--accent) calc(var(--s, 0) * 55%), var(--surface)); }}
.grid td.title {{ background: var(--surface); }}
.grid td.na {{ color: var(--muted); background: var(--surface); }}
.grid td.avg {{ font-weight: 600; background: var(--surface); border-left: 1px solid var(--line); }}
.frames td {{ font-family: var(--mono); }}
.open {{ list-style: none; padding: 0; margin: 0; display: grid; gap: 8px; }}
.open li {{ display: grid; grid-template-columns: 170px 1fr; gap: 12px; padding: 12px 14px; background: var(--surface); border: 1px solid var(--line); border-radius: 4px; }}
.open li b {{ font: 600 15px/1.3 var(--display); text-transform: uppercase; letter-spacing: 0.03em; color: var(--hot); }}
@media (max-width: 560px) {{ .open li {{ grid-template-columns: 1fr; gap: 4px; }} }}
.note {{ font-size: 13px; color: var(--muted); }}
</style>
<main>
  <div class="hero">
    <span class="kicker">Veyra City · visual passes {first_n}–{last["n"]} · {html.escape(meta.get("date", ""))}</span>
    <h1>Round two: <em>the city</em>, the map, the car</h1>
    <p>{html.escape(meta.get("summary", ""))}</p>
    <div class="stats">{stats_html}</div>
  </div>
  {section("The map on M", "A real aerial of this city with a building layer, roads, district borders, filterable icons, a GPS route on the road graph and an atlas view.", map_body)}
  {section("Car detail", "Instrument cluster, brakes, nitro and speed: what the HUD and the car now show.", car_body)}
  {section("Buildings, water, sky", "Same viewpoints before and after.", world_body)}
  {section("Streets", "The eight street scenes of the latest pass (noon, golden hour, neon night, rain night, nitro, drift, traffic, close-up).", street_body)}
  <section>
    <header><h2>Scores by pass</h2><p>1–5 against the GTA V / Asphalt / Cyberpunk bar for each area. B7 is frame time: the city scenes run cool and stayed at ~8.5 ms; late-run numbers swing with laptop heat.</p></header>
    <div class="scroll">{grid}</div>
    <p class="note">Average over the scored areas: {first_avg:.1f} at the first scored pass, {last_avg:.1f} at pass {last["n"]}.</p>
  </section>
  <section>
    <header><h2>Frame time</h2><p>Development Mono build, 2560 × 1440, PC_High, RTX 4070 Laptop. The cruise is a 28 s drive with traffic late in each run.</p></header>
    <div class="scroll">{ftable}</div>
  </section>
  <section>
    <header><h2>Still open</h2></header>
    <ul class="open">{open_html}</ul>
  </section>
</main>
"""
open(out, "w", encoding="utf-8").write(page)
print("wrote", out, os.path.getsize(out) // 1024, "KB")

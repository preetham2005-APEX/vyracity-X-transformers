"""D2 / BUG-170: summary table of the release route runs in Logs/D2_BUG170 (stats.json + [IterCost] line per run)."""
import json, os, re, sys

out = sys.argv[1] if len(sys.argv) > 1 else 'E:/VeyraCity/Logs/D2_BUG170'
WANT = ['RoadWear', 'Streamer.CellLoaded callbacks', 'CarProbe', 'Pedestrians', 'ParkedCars', 'Traffic', 'TrafficPhysics']
rows = []
for d in sorted(os.listdir(out)):
    p = os.path.join(out, d)
    sj, pl = os.path.join(p, 'stats.json'), os.path.join(p, 'player.log')
    if not os.path.isfile(sj):
        continue
    s = json.load(open(sj, encoding='utf-8'))
    r = s.get('render', {})
    cpu = r.get('cpu_main_ms', {})
    seg = s.get('vehicle', {})
    log = open(pl, encoding='utf-8', errors='replace').read() if os.path.isfile(pl) else ''
    cost = {}
    m = re.search(r'\[IterCost\](.*?)\|', log)
    if m:
        for mm in re.finditer(r'([A-Za-z. ]+?):max([\d.]+)/avg([\d.]+)/over8x(\d+)', m.group(1)):
            cost[mm.group(1).strip()] = (float(mm.group(3)), float(mm.group(2)), int(mm.group(4)))
    cyc = re.search(r'CarProbe\.cycles=(\d+)', log)
    phys = re.search(r'physics steps per frame: max (\d+), frames with >=3 (\d+), >=5 (\d+), of (\d+)', log)
    results = re.findall(r'\[AutoTest\] segment \d+ .*? -> ([a-z ]+?) in', log)
    rows.append((d, s.get('avg_fps', 0), cpu.get('p95', -1), cpu.get('p99', -1), s.get('hitches_over_50ms', -1),
                 f"{seg.get('segments_completed', '?')}/{seg.get('segments_total', '?')}", cost, cyc.group(1) if cyc else '-',
                 phys.groups() if phys else None, results, s.get('duration_s', 0)))

print(f"{'run':40s} {'fps':>5} {'cpuP95':>7} {'cpuP99':>7} {'hitch':>6} {'segs':>6} {'probe':>6}  physics(max/>=3/>=5)")
for d, fps, p95, p99, h, segs, cost, cyc, phys, res, dur in rows:
    ph = f"{phys[0]}/{phys[1]}/{phys[2]} of {phys[3]}" if phys else '-'
    print(f"{d:40s} {fps:5.1f} {p95:7.1f} {p99:7.1f} {h:6d} {segs:>6} {cyc:>6}  {ph}")
    for k in WANT:
        if k in cost:
            a, mx, o = cost[k]
            print(f"    {k:32s} avg {a:5.2f} ms  max {mx:6.1f}  >8ms x{o}")
    if res:
        tally = {}
        for x in res:
            tally[x.strip()] = tally.get(x.strip(), 0) + 1
        print('    segments: ' + ', '.join(f'{k} {v}' for k, v in tally.items()))

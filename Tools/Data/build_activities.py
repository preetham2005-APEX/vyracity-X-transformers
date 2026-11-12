"""G5 (round 3): activities.json for the player from Assets/_Veyra/Data/location_registry.csv.

The CSV is not shipped in the player and WorldData's locations carry no radius or end point, so this writes the activity
subset (id, name, category, Unity position, radius, end point, length) to Resources/Activities/activities.json.
Unity = (x - 4000, elevation, y - 3000) as in Core/WorldCoordinates. EditMode test ActivityDataTests keeps it in sync.
Usage: python -I Tools/Data/build_activities.py
"""
import csv, json, re, sys

SRC = 'E:/VeyraCity/Assets/_Veyra/Data/location_registry.csv'
DST = 'E:/VeyraCity/Assets/_Veyra/Resources/Activities/activities.json'
CATS = ['Speed zone', 'Drift zone', 'Drag strip', 'Stunt jump', 'Collectible area', 'Photo challenge', 'Hidden shortcut',
        'Scenic viewpoint', 'Time trial / test', 'Garage', 'Hidden garage / barn find', 'Dealership', 'Delivery hub', 'Car meet']

def main():
    out = []
    with open(SRC, encoding='utf-8') as f:
        for r in csv.DictReader(f):
            if r['category'] not in CATS:
                continue
            x, y, e = float(r['x_m']), float(r['y_m']), float(r['elevation_m'] or 0)
            a = {'id': r['id'], 'name': r['name'], 'category': r['category'],
                 'x': round(x - 4000, 2), 'y': round(e, 2), 'z': round(y - 3000, 2),
                 'radius': 0.0, 'hasEnd': False, 'ex': 0.0, 'ez': 0.0, 'length': 0.0}
            d = r['details'] or ''
            m = re.search(r'radius\s+([\d.]+)\s*m', d)
            if m: a['radius'] = float(m.group(1))
            m = re.search(r'end=\(([-\d.]+),\s*([-\d.]+)\)', d)
            if m:
                a['hasEnd'] = True
                a['ex'] = round(float(m.group(1)) - 4000, 2)
                a['ez'] = round(float(m.group(2)) - 3000, 2)
            m = re.search(r'length\s+([\d.]+)\s*m', d)
            if m: a['length'] = float(m.group(1))
            out.append(a)
    import os
    os.makedirs(os.path.dirname(DST), exist_ok=True)
    with open(DST, 'w', encoding='utf-8', newline='\n') as f:
        json.dump({'activities': out}, f, indent=1, ensure_ascii=False)
        f.write('\n')
    print(f'{len(out)} activities -> {DST}')

if __name__ == '__main__':
    sys.exit(main())

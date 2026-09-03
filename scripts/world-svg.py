#!/usr/bin/env python3
"""Render a world as one SVG: coastline and land, rivers, contour lines,
every placed location with its approach circle, and the road routes, so
road distribution can be judged on paper. Pure python, no PIL/matplotlib.

    scripts/world-svg.py <World>.world.csv <World>.locations.csv [<World>.routes.csv]
        [--out world.svg] [--zoom cx,cz,half] [--contour 10] [--px 0.1]

Inputs come from road_world_dump (or [Debug] WorldDump = true) and the
self-test's routes CSV. --zoom renders an inset window centred on (cx,cz)
with half-size `half` metres beside the world.
"""
import argparse
import csv
import math
import sys
from collections import defaultdict

SEA = 30.0
BIOME_FILL = {
    'Meadows': '#b9d38a', 'BlackForest': '#4f7a4a', 'Swamp': '#6b6a3f', 'Mountain': '#e8ecef',
    'Plains': '#d8cd7a', 'Mistlands': '#8a8aa8', 'AshLands': '#b05a3a', 'DeepNorth': '#dfe8f0',
    'Ocean': '#8fb7d9',
}
LABEL_KEYS = ('Eikthyr', 'GDKing', 'Bonemass', 'Dragonqueen', 'GoblinKing', 'Mistlands_DvergrBossEntrance',
              'Crypt', 'SunkenCrypt', 'TrollCave', 'MountainCave', 'Mistlands_DvergrTownEntrance',
              'StartTemple', 'Vendor', 'Hildir')


def f(v):
    return f'{v:.1f}'


def read_world(path):
    xs, zs = set(), set()
    cells = {}
    with open(path) as fh:
        for row in csv.DictReader(fh):
            x, z = int(float(row['x'])), int(float(row['z']))
            xs.add(x); zs.add(z)
            cells[(x, z)] = (float(row['height']), row['biome'], float(row['river']))
    xs, zs = sorted(xs), sorted(zs)
    return xs, zs, cells


def marching_squares(xs, zs, cells, level):
    """Contour segments of `height == level` as world-space line pairs."""
    segs = []
    step = xs[1] - xs[0]
    h = lambda x, z: cells[(x, z)][0]

    def interp(p, q, hp, hq):
        t = 0.5 if hq == hp else (level - hp) / (hq - hp)
        return (p[0] + (q[0] - p[0]) * t, p[1] + (q[1] - p[1]) * t)

    for z in zs[:-1]:
        for x in xs[:-1]:
            a, b, c, d = (x, z), (x + step, z), (x + step, z + step), (x, z + step)
            if not all(k in cells for k in (a, b, c, d)):
                continue
            ha, hb, hc, hd = h(*a), h(*b), h(*c), h(*d)
            idx = (ha >= level) | ((hb >= level) << 1) | ((hc >= level) << 2) | ((hd >= level) << 3)
            if idx in (0, 15):
                continue
            e = {
                'ab': interp(a, b, ha, hb), 'bc': interp(b, c, hb, hc),
                'cd': interp(c, d, hc, hd), 'da': interp(d, a, hd, ha),
            }
            table = {
                1: [('da', 'ab')], 2: [('ab', 'bc')], 3: [('da', 'bc')], 4: [('bc', 'cd')],
                5: [('da', 'ab'), ('bc', 'cd')], 6: [('ab', 'cd')], 7: [('da', 'cd')], 8: [('cd', 'da')],
                9: [('ab', 'cd')], 10: [('ab', 'da'), ('bc', 'cd')], 11: [('bc', 'cd')], 12: [('bc', 'da')],
                13: [('ab', 'bc')], 14: [('da', 'ab')],
            }
            for p, q in table[idx]:
                segs.append((e[p], e[q]))
    return segs


class View:
    def __init__(self, x0, z0, x1, z1, px):
        self.x0, self.z0, self.x1, self.z1, self.px = x0, z0, x1, z1, px
        self.w = (x1 - x0) * px
        self.h = (z1 - z0) * px

    def X(self, x):
        return (x - self.x0) * self.px

    def Y(self, z):
        return (self.z1 - z) * self.px  # north up

    def inside(self, x, z, pad=0.0):
        return self.x0 - pad <= x <= self.x1 + pad and self.z0 - pad <= z <= self.z1 + pad


def render(view, xs, zs, cells, locations, routes, contour, title):
    out = []
    step = xs[1] - xs[0]
    out.append(f'<g>')
    out.append(f'<rect x="0" y="0" width="{f(view.w)}" height="{f(view.h)}" fill="{BIOME_FILL["Ocean"]}"/>')
    # land and river cells as run-length rects per row
    for z in zs:
        if not (view.z0 - step <= z <= view.z1):
            continue
        run_fill, run_x0, run_x1 = None, None, None

        def flush():
            if run_fill is not None:
                out.append(f'<rect x="{f(view.X(run_x0))}" y="{f(view.Y(z + step))}" '
                           f'width="{f((run_x1 - run_x0) * view.px)}" height="{f(step * view.px)}" fill="{run_fill}"/>')
        for x in xs:
            if not (view.x0 - step <= x <= view.x1):
                continue
            cell = cells.get((x, z))
            fill = None
            if cell is not None:
                h, biome, river = cell
                if h >= SEA:
                    fill = BIOME_FILL.get(biome, '#cccccc')
                    if river > 0.5:
                        fill = '#9bc0a3'  # dry river band: valley floor
                elif river > 0.5:
                    fill = '#4a7fc1'  # river water
            if fill == run_fill and run_x1 == x:
                run_x1 = x + step
            else:
                flush()
                run_fill, run_x0, run_x1 = fill, x, x + step
        flush()
    # contours
    top = max(c[0] for c in cells.values())
    levels = [SEA] + [lv for lv in range(int(SEA) + contour, int(top) + 1, contour)]
    for lv in levels:
        heavy = abs(lv - SEA) < 1e-6
        d = []
        for (p, q) in marching_squares(xs, zs, cells, lv):
            if view.inside(p[0], p[1], step) or view.inside(q[0], q[1], step):
                d.append(f'M{f(view.X(p[0]))} {f(view.Y(p[1]))}L{f(view.X(q[0]))} {f(view.Y(q[1]))}')
        if d:
            out.append(f'<path d="{" ".join(d)}" fill="none" stroke="{"#1f3a5f" if heavy else "#5a4a30"}" '
                       f'stroke-width="{"1.2" if heavy else "0.35"}" stroke-opacity="{"1" if heavy else "0.6"}"/>')
    # routes
    for label, pts in routes.items():
        if not pts:
            continue
        length = sum(math.dist(pts[i][:2], pts[i + 1][:2]) for i in range(len(pts) - 1))
        stub = length < 40
        for i in range(len(pts) - 1):
            (x0, z0, y0), (x1, z1, y1) = pts[i], pts[i + 1]
            if not (view.inside(x0, z0) or view.inside(x1, z1)):
                continue
            ymin = min(y0, y1)
            color = '#e03030' if stub else ('#f08a24' if ymin < 28 else ('#1fa8a0' if ymin < 30.5 else '#3a3a3a'))
            width = 3 if stub else (2.4 if ymin < 30.5 else 1.4)
            out.append(f'<line x1="{f(view.X(x0))}" y1="{f(view.Y(z0))}" x2="{f(view.X(x1))}" y2="{f(view.Y(z1))}" '
                       f'stroke="{color}" stroke-width="{width}" stroke-linecap="round"/>')
    # locations
    for name, x, z, r in locations:
        if not view.inside(x, z, r):
            continue
        out.append(f'<circle cx="{f(view.X(x))}" cy="{f(view.Y(z))}" r="{f(r * view.px)}" fill="none" '
                   f'stroke="#7a2ea0" stroke-width="0.6" stroke-opacity="0.5"/>')
        out.append(f'<circle cx="{f(view.X(x))}" cy="{f(view.Y(z))}" r="2.2" fill="#7a2ea0"/>')
        if any(k in name for k in LABEL_KEYS):
            out.append(f'<text x="{f(view.X(x) + 4)}" y="{f(view.Y(z) - 3)}" font-size="9" fill="#3b1050">{name}</text>')
    out.append(f'<text x="6" y="14" font-size="12" fill="#111">{title}</text>')
    out.append('</g>')
    return '\n'.join(out)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('world')
    ap.add_argument('locations')
    ap.add_argument('routes', nargs='?')
    ap.add_argument('--out')
    ap.add_argument('--zoom', help='cx,cz,half (metres): inset window rendered beside the world')
    ap.add_argument('--contour', type=int, default=10)
    ap.add_argument('--px', type=float, default=0.1, help='pixels per metre for the world view')
    a = ap.parse_args()

    xs, zs, cells = read_world(a.world)
    locations = []
    with open(a.locations) as fh:
        for row in csv.DictReader(fh):
            locations.append((row['name'], float(row['x']), float(row['z']), float(row['radius'])))
    routes = defaultdict(list)
    if a.routes:
        with open(a.routes) as fh:
            for row in csv.DictReader(fh):
                routes[(row['route_index'], row['label'])].append((float(row['x']), float(row['z']), float(row['y'])))

    # trim the world view to the land extent (plus margin) so islands fill the page
    land = [(x, z) for (x, z), (h, _, _) in cells.items() if h >= SEA]
    if land:
        lx = [p[0] for p in land]; lz = [p[1] for p in land]
        margin = 300
        x0, x1 = min(lx) - margin, max(lx) + margin
        z0, z1 = min(lz) - margin, max(lz) + margin
    else:
        x0, x1, z0, z1 = xs[0], xs[-1], zs[0], zs[-1]
    world_view = View(x0, z0, x1, z1, a.px)
    n_routes = len(routes)
    stubs = sum(1 for pts in routes.values()
                if sum(math.dist(pts[i][:2], pts[i + 1][:2]) for i in range(len(pts) - 1)) < 40)
    title = f'{a.world.split("/")[-1]} — {len(locations)} locations, {n_routes} routes ({stubs} stubs < 40 m), contours every {a.contour} m, 30 m coast'
    parts = [render(world_view, xs, zs, cells, locations, routes, a.contour, title)]
    total_w, total_h = world_view.w, world_view.h
    if a.zoom:
        cx, cz, half = (float(v) for v in a.zoom.split(','))
        zpx = min(2.0, 700 / (2 * half))
        zoom_view = View(cx - half, cz - half, cx + half, cz + half, zpx)
        inner = render(zoom_view, xs, zs, cells, locations, routes, a.contour, f'zoom ({cx:.0f},{cz:.0f}) ±{half:.0f} m')
        parts.append(f'<g transform="translate({f(world_view.w + 20)},0)">{inner}</g>')
        total_w += 20 + zoom_view.w
        total_h = max(total_h, zoom_view.h)
    svg = (f'<svg xmlns="http://www.w3.org/2000/svg" width="{f(total_w)}" height="{f(total_h)}" '
           f'viewBox="0 0 {f(total_w)} {f(total_h)}" font-family="sans-serif">\n' + '\n'.join(parts) + '\n</svg>\n')
    out = a.out or a.world.replace('.world.csv', '.world.svg')
    with open(out, 'w') as fh:
        fh.write(svg)
    print(f'{out}: {len(cells)} cells, {len(locations)} locations, {n_routes} routes, {len(svg) // 1024} KB')


if __name__ == '__main__':
    main()

#!/usr/bin/env python3
"""Render a world as one SVG: coastline and land, rivers, contour lines,
every placed location with its approach circle, and the road network, so
road distribution can be judged on paper. Pure python, no PIL/matplotlib.

    scripts/world-svg.py <World>.world.csv <World>.locations.csv [<World>.routes.csv]
        [--crossings <World>.crossings.csv] [--manifest <World>.manifest.json]
        [--out world.svg] [--zoom cx,cz,half] [--contour 10] [--px 0.1]

The world and locations CSVs come from cli_world_dump; the routes,
crossings and manifest from road_routes (this branch). A route is drawn in
the colours of the stretches that made it, so how the road met the water is
visible on the map: ordinary road, waded ford, raised ford, and the
unpainted span of a bridge as a dashed line between its banks.

--zoom renders an inset window centred on (cx,cz) with half-size `half`
metres beside the world.
"""
import argparse
import array
import base64
import csv
import json
import math
import os
import struct
import sys
import zlib
from xml.sax.saxutils import escape
from collections import defaultdict

SEA = 30.0
# Swamp sits at and below the waterline: most of it is shallow water with trees
# in it, not sea. Drawing it as ocean made every swamp road look like a road
# into the sea, which is the one thing a reader must not be misled about.
SWAMP_WATER = '#9FAF86'
OCEAN_WATER = '#B9D2E8'
RIVER_WATER = '#7FA6D4'
DRY_RIVER = '#D3E4D4'
BIOME_FILL = {
    # Light tints: the roads must carry the contrast, not the land.
    'Meadows': '#E4EED3', 'BlackForest': '#C9DCC4', 'Swamp': '#DCD9C0', 'Mountain': '#F3F5F7',
    'Plains': '#F0EACB', 'Mistlands': '#DCDBE6', 'AshLands': '#EACFC3', 'DeepNorth': '#EEF3F7',
    'Ocean': '#B9D2E8',
}
# One colour per way a stretch of road met the ground. A reader should be
# able to tell a bridge from a ford from the map alone.
KIND_STYLE = {
    'Road':  ('#1f1f1f', 2.0, None),
    'Wade':  ('#0f8f88', 2.8, None),
    'Raise': ('#c06010', 2.8, None),
    'Span':  ('#7a2fb0', 3.0, '5,3'),
}
# A road standing in water is not always a fault: in a swamp the mod wades on
# purpose, down to 28 m. Drawn apart from ordinary road so a reader is not left
# thinking the network runs into the sea, and apart from water in any other
# biome, which is a finding.
SWAMP_WADE_STYLE = ('#4a8f5a', 2.6, None)
IN_WATER_STYLE = ('#e07a10', 2.8, None)
SEA_ROAD_FLOOR = 30.5
# Labels are for orientation, not inventory: at world scale a label per dungeon
# buries the roads the picture is about.
LABEL_SETS = {
    'none': (),
    'bosses': ('Eikthyr', 'GDKing', 'Bonemass', 'Dragonqueen', 'GoblinKing',
               'Mistlands_DvergrBossEntrance', 'StartTemple', 'Vendor', 'Hildir'),
    'all': ('Eikthyr', 'GDKing', 'Bonemass', 'Dragonqueen', 'GoblinKing', 'Mistlands_DvergrBossEntrance',
            'Crypt', 'SunkenCrypt', 'TrollCave', 'MountainCave', 'Mistlands_DvergrTownEntrance',
            'StartTemple', 'Vendor', 'Hildir'),
}
LABEL_KEYS = LABEL_SETS['bosses']


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


class Grid:
    """A dumped grid held compactly: an 8 m dump of a whole world is over six
    million samples, so heights, biomes and river weights are kept in flat
    arrays rather than a dictionary of tuples."""

    def __init__(self, x0, z0, step, nx, nz, heights, biomes, rivers, names):
        self.x0, self.z0, self.step, self.nx, self.nz = x0, z0, step, nx, nz
        self.heights, self.biomes, self.rivers, self.names = heights, biomes, rivers, names

    def index(self, x, z):
        ix = int(round((x - self.x0) / self.step))
        iz = int(round((z - self.z0) / self.step))
        if ix < 0 or iz < 0 or ix >= self.nx or iz >= self.nz:
            return None
        return iz * self.nx + ix

    def at(self, x, z):
        """(height, biome name, river weight) at a world point, or None."""
        i = self.index(x, z)
        if i is None:
            return None
        return self.heights[i], self.names[self.biomes[i]], self.rivers[i] / 255.0


def read_grid(path):
    """Streams a dump into flat arrays. Two passes: the first learns the grid's
    shape, the second fills it."""
    with open(path) as fh:
        header = fh.readline().strip().split(',')
        cx, cz = header.index('x'), header.index('z')
        ch, cb, cr = header.index('height'), header.index('biome'), header.index('river')
        xs_min = zs_min = float('inf')
        xs_max = zs_max = float('-inf')
        second_x = second_z = float('inf')
        rows = 0
        for line in fh:
            if not line.strip():
                continue
            cells = line.split(',')
            x, z = float(cells[cx]), float(cells[cz])
            if x < xs_min:
                second_x, xs_min = xs_min, x
            elif xs_min < x < second_x:
                second_x = x
            if z < zs_min:
                second_z, zs_min = zs_min, z
            elif zs_min < z < second_z:
                second_z = z
            xs_max, zs_max = max(xs_max, x), max(zs_max, z)
            rows += 1

    step = second_x - xs_min
    nx = int(round((xs_max - xs_min) / step)) + 1
    nz = int(round((zs_max - zs_min) / step)) + 1
    if nx * nz != rows:
        sys.exit(f'{path}: {rows} samples do not fill a {nx}x{nz} grid')

    heights = array.array('f', bytes(4 * nx * nz))
    biomes = bytearray(nx * nz)
    rivers = bytearray(nx * nz)
    names, ids = ['Ocean'], {'Ocean': 0}

    with open(path) as fh:
        fh.readline()
        for line in fh:
            if not line.strip():
                continue
            cells = line.split(',')
            ix = int(round((float(cells[cx]) - xs_min) / step))
            iz = int(round((float(cells[cz]) - zs_min) / step))
            i = iz * nx + ix
            heights[i] = float(cells[ch])
            name = cells[cb].strip()
            if name not in ids:
                ids[name] = len(names)
                names.append(name)
            biomes[i] = ids[name]
            rivers[i] = min(255, int(float(cells[cr]) * 255))

    return Grid(xs_min, zs_min, step, nx, nz, heights, biomes, rivers, names)


def ground_colour(height, biome, river):
    """What one cell of ground looks like. Water is not one thing: a swamp's
    shallows, a river and the sea are three."""
    if height >= SEA:
        return DRY_RIVER if river > 0.5 else BIOME_FILL.get(biome, '#cccccc')
    if biome == 'Swamp':
        return SWAMP_WATER
    if river > 0.5:
        return RIVER_WATER
    return OCEAN_WATER


def png_data_uri(width, height, pixels):
    """A PNG as a data URI. The land is a picture, not tens of thousands of
    rectangles: drawing it as vectors made a file too large to open."""
    raw = b''.join(b'\x00' + bytes(row) for row in pixels)

    def chunk(tag, data):
        return (struct.pack('>I', len(data)) + tag + data
                + struct.pack('>I', zlib.crc32(tag + data) & 0xffffffff))

    png = (b'\x89PNG\r\n\x1a\n'
           + chunk(b'IHDR', struct.pack('>IIBBBBB', width, height, 8, 2, 0, 0, 0))
           + chunk(b'IDAT', zlib.compress(raw, 9))
           + chunk(b'IEND', b''))
    return 'data:image/png;base64,' + base64.b64encode(png).decode('ascii')


def rgb(colour):
    return (int(colour[1:3], 16), int(colour[3:5], 16), int(colour[5:7], 16))


def background_image(view, grid):
    """One pixel per dumped cell over the view, as a PNG. Returns the SVG
    element and the world rectangle it covers."""
    step = grid.step
    ix0 = max(0, int(math.floor((view.x0 - grid.x0) / step)))
    ix1 = min(grid.nx - 1, int(math.ceil((view.x1 - grid.x0) / step)))
    iz0 = max(0, int(math.floor((view.z0 - grid.z0) / step)))
    iz1 = min(grid.nz - 1, int(math.ceil((view.z1 - grid.z0) / step)))
    if ix1 < ix0 or iz1 < iz0:
        return '', None

    cache = {}

    def colour_bytes(height, biome_id, river):
        key = (round(height, 1) >= SEA, biome_id, river > 127)
        hit = cache.get(key)
        if hit is None:
            hit = bytes(rgb(ground_colour(height, grid.names[biome_id], river / 255.0)))
            cache[key] = hit
        return hit

    pixels = []
    for iz in range(iz1, iz0 - 1, -1):   # north up
        row = bytearray()
        base = iz * grid.nx
        for ix in range(ix0, ix1 + 1):
            i = base + ix
            row += colour_bytes(grid.heights[i], grid.biomes[i], grid.rivers[i])
        pixels.append(row)

    uri = png_data_uri(ix1 - ix0 + 1, iz1 - iz0 + 1, pixels)
    x0, x1 = grid.x0 + ix0 * step, grid.x0 + (ix1 + 1) * step
    z0, z1 = grid.z0 + iz0 * step, grid.z0 + (iz1 + 1) * step
    element = (f'<image x="{f(view.X(x0))}" y="{f(view.Y(z1))}" '
               f'width="{f((x1 - x0) * view.px)}" height="{f((z1 - z0) * view.px)}" '
               f'preserveAspectRatio="none" href="{uri}"/>')
    return element, (x0, z0, x1, z1)


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


def render(view, xs, zs, cells, grid, locations, routes, crossings, contour, title, min_radius):
    out = []
    step = xs[1] - xs[0]
    out.append(f'<g>')
    out.append(f'<rect x="0" y="0" width="{f(view.w)}" height="{f(view.h)}" fill="{BIOME_FILL["Ocean"]}"/>')
    image, _ = background_image(view, grid)
    if image:
        out.append(image)
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
    # routes, drawn stretch by stretch: how the road met the ground is the
    # thing being judged, so the kind decides the colour. A short road is
    # still called out in red - a stub that reaches nothing is a finding.
    for label, pts in routes.items():
        if not pts:
            continue
        length = sum(math.dist(pts[i][:2], pts[i + 1][:2]) for i in range(len(pts) - 1))
        stub = length < 40

        def style_of(i):
            x, z, y, kind, _ = pts[i]
            if stub:
                return ('#e03030', 3.2, None)
            if kind == 'Road' and y < SEA_ROAD_FLOOR:
                cell = grid.at(x, z)
                swamp = cell is not None and cell[1] == 'Swamp'
                return SWAMP_WADE_STYLE if swamp else IN_WATER_STYLE
            return KIND_STYLE.get(kind, KIND_STYLE['Road'])

        # One polyline per run of points that share a stretch and a style: a
        # line element per point pair made a file an order of magnitude larger
        # than the picture needs.
        run, run_style, run_segment = [], None, None
        def flush():
            if len(run) < 2:
                return
            if not any(view.inside(x, z) for x, z in run):
                return
            color, width, dash = run_style
            dash_attr = f' stroke-dasharray="{dash}"' if dash else ''
            points = ' '.join(f'{f(view.X(x))},{f(view.Y(z))}' for x, z in run)
            out.append(f'<polyline points="{points}" fill="none" stroke="{color}" '
                       f'stroke-width="{width}" stroke-linecap="round" stroke-linejoin="round"{dash_attr}/>')

        for i in range(len(pts)):
            x, z, y, kind, segment = pts[i]
            style = style_of(i)
            if segment != run_segment or style != run_style:
                flush()
                run, run_style, run_segment = [], style, segment
            run.append((x, z))
        flush()
    # crossings: where the network met a river, and what it built there
    for c in crossings:
        if not view.inside(c['x'], c['z'], 40):
            continue
        cx, cy = view.X(c['x']), view.Y(c['z'])
        if c['kind'] == 'Bridge':
            r = 3.4
            out.append(f'<path d="M{f(cx)} {f(cy - r)}L{f(cx + r)} {f(cy)}L{f(cx)} {f(cy + r)}L{f(cx - r)} {f(cy)}Z" '
                       f'fill="#ffffff" stroke="#7a2fb0" stroke-width="1.2"/>')
        else:
            out.append(f'<circle cx="{f(cx)}" cy="{f(cy)}" r="2.6" fill="#ffffff" '
                       f'stroke="{"#0f8f88" if c["style"] == "Wade" else "#c06010"}" stroke-width="1.2"/>')
    # locations
    for name, x, z, r in locations:
        if r < min_radius or not view.inside(x, z, r):
            continue
        out.append(f'<circle cx="{f(view.X(x))}" cy="{f(view.Y(z))}" r="{f(r * view.px)}" fill="none" '
                   f'stroke="#8a4ab0" stroke-width="0.5" stroke-opacity="0.4"/>')
        out.append(f'<circle cx="{f(view.X(x))}" cy="{f(view.Y(z))}" r="1.8" fill="#8a4ab0" fill-opacity="0.8"/>')
        if any(k in name for k in LABEL_KEYS):
            out.append(f'<text x="{f(view.X(x) + 4)}" y="{f(view.Y(z) - 3)}" font-size="9" fill="#3b1050">{escape(name)}</text>')
    out.append(f'<text x="6" y="14" font-size="12" fill="#111">{escape(title)}</text>')
    out.append('</g>')
    return '\n'.join(out)


def manifest_caption(manifest):
    """The run, in one line: what code, what world, and the settings that
    decide a road network - as the plugin held them after clamping."""
    if not manifest:
        return ''
    cfg = manifest.get('config', {})
    res = manifest.get('result', {})
    parts = []
    # A run in the game names the build it ran on; a run offline names the
    # terrain it read and says that terrain is approximate.
    if manifest.get('modVersion'):
        parts.append(f"mod {manifest['modVersion']}")
    if manifest.get('gameVersion'):
        parts.append(f"game {manifest['gameVersion']}")
    if manifest.get('worldName'):
        parts.append(f"world {manifest['worldName']} (seed {manifest.get('worldSeed', '?')})")
    if manifest.get('label'):
        parts.append(f"run {manifest['label']}")
    if manifest.get('terrain'):
        dumps = ', '.join(manifest.get('terrainDumps', []))
        parts.append(f"terrain {manifest['terrain']}" + (f" from {dumps}" if dumps else ""))
    if manifest.get('scope'):
        parts.append(f"scope {manifest['scope']}")
    if cfg.get('Strategy'):
        parts.append(f"policy {cfg['Strategy']}")
    parts.append(f"islands {cfg.get('IslandRoadPercentage', '?')}%")
    parts.append(f"max POI/island {cfg.get('MaxLocationsPerIsland', '?')}")
    parts.append(f"iterations {cfg.get('PathfindingMaxIterations', '?')}")
    parts.append(f"fords {'on' if cfg.get('FordsEnabled') else 'off'}")
    parts.append(f"bridges {'on' if cfg.get('BridgesEnabled') else 'off'}")
    if res:
        parts.append(f"{res.get('totalLengthMeters', '?')} m of road")
    return '; '.join(parts)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('world')
    ap.add_argument('locations')
    ap.add_argument('routes', nargs='?')
    ap.add_argument('--background', help='a finer dump (8 m) used for the land picture and for judging '
                                         'which road points stand in a swamp; the coarse world file still '
                                         'draws the contours')
    ap.add_argument('--crossings', help='road_routes crossings CSV: fords and bridges are marked on the map')
    ap.add_argument('--manifest', help='road_routes manifest JSON: its settings go in the caption')
    ap.add_argument('--out')
    ap.add_argument('--zoom', help='cx,cz,half (metres): inset window rendered beside the world')
    ap.add_argument('--zoom-only', action='store_true',
                    help='render only the inset, as its own square image (island views for a comparison sheet)')
    ap.add_argument('--contour', type=int, default=10)
    ap.add_argument('--px', type=float, default=0.1, help='pixels per metre for the world view')
    ap.add_argument('--labels', choices=sorted(LABEL_SETS), default='bosses',
                    help='which locations are named on the map (default: bosses and the spawn)')
    ap.add_argument('--min-radius', type=float, default=10.0,
                    help='hide locations whose approach radius is smaller (runestones, spawners)')
    a = ap.parse_args()

    global LABEL_KEYS
    LABEL_KEYS = LABEL_SETS[a.labels]

    xs, zs, cells = read_world(a.world)
    grid = read_grid(a.background) if a.background else read_grid(a.world)
    locations = []
    with open(a.locations) as fh:
        for row in csv.DictReader(fh):
            locations.append((row['name'], float(row['x']), float(row['z']), float(row['radius'])))
    routes = defaultdict(list)
    if a.routes:
        with open(a.routes) as fh:
            for row in csv.DictReader(fh):
                # segment_index and kind are this branch's columns; an older
                # routes CSV without them is read as one ordinary road.
                routes[(row['route_index'], row['label'])].append((
                    float(row['x']), float(row['z']), float(row['y']),
                    row.get('kind', 'Road'), row.get('segment_index', '0')))
    crossings = []
    if a.crossings:
        with open(a.crossings) as fh:
            for row in csv.DictReader(fh):
                crossings.append({'x': float(row['center_x']), 'z': float(row['center_z']),
                                  'kind': row['kind'], 'style': row['style'],
                                  'width': float(row['width']), 'depth': float(row['depth'])})
    manifest = {}
    if a.manifest:
        with open(a.manifest) as fh:
            manifest = json.load(fh)

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
    shown = sum(1 for l in locations if l[3] >= a.min_radius)
    bridges = sum(1 for c in crossings if c['kind'] == 'Bridge')
    title = (f'{os.path.basename(a.world)}: {len(locations)} locations ({shown} with radius >= {a.min_radius:.0f} m shown), '
             f'{n_routes} routes ({stubs} stubs < 40 m), '
             f'{len(crossings)} crossings ({bridges} bridges); land drawn at {grid.step:.0f} m, '
             f'contours every {a.contour} m, 30 m coast heavy; '
             f'black road, green road wading a swamp, orange road in water elsewhere, '
             f'teal ford wade, dashed purple bridge span, red stub')
    subtitle = manifest_caption(manifest)
    if a.zoom_only and a.zoom:
        cx, cz, half = (float(v) for v in a.zoom.split(','))
        px = min(2.0, 1400 / (2 * half))
        zoom_view = View(cx - half, cz - half, cx + half, cz + half, px)
        caption = (f'{os.path.basename(a.world)} at ({cx:.0f},{cz:.0f}) +-{half:.0f} m: '
                   f'{n_routes} routes on the world, {len(crossings)} crossings; '
                   f'black road, green road wading a swamp, orange road in water elsewhere, '
                   f'teal ford wade, dashed purple bridge span')
        inner = render(zoom_view, xs, zs, cells, grid, locations, routes, crossings, a.contour,
                       caption, a.min_radius)
        subtitle = manifest_caption(manifest)
        body = inner + (f'\n<text x="6" y="27" font-size="10" fill="#444">{escape(subtitle)}</text>'
                        if subtitle else '')
        svg = ('<?xml version="1.0" encoding="UTF-8"?>\n'
               f'<svg xmlns="http://www.w3.org/2000/svg" width="{f(zoom_view.w)}" height="{f(zoom_view.h)}" '
               f'viewBox="0 0 {f(zoom_view.w)} {f(zoom_view.h)}" font-family="sans-serif">\n'
               + body + '\n</svg>\n')
        out = a.out or a.world.replace('.world.csv', '.island.svg')
        with open(out, 'w') as fh:
            fh.write(svg)
        print(f'{out}: island view at ({cx:.0f},{cz:.0f}), {len(svg) // 1024} KB')
        return

    parts = [render(world_view, xs, zs, cells, grid, locations, routes, crossings, a.contour, title, a.min_radius)]
    if subtitle:
        parts.append(f'<text x="6" y="27" font-size="10" fill="#444">{escape(subtitle)}</text>')
    total_w, total_h = world_view.w, world_view.h
    if a.zoom:
        cx, cz, half = (float(v) for v in a.zoom.split(','))
        zpx = min(2.0, 700 / (2 * half))
        zoom_view = View(cx - half, cz - half, cx + half, cz + half, zpx)
        inner = render(zoom_view, xs, zs, cells, grid, locations, routes, crossings, a.contour, f'zoom ({cx:.0f},{cz:.0f}) +-{half:.0f} m', a.min_radius)
        parts.append(f'<g transform="translate({f(world_view.w + 20)},0)">{inner}</g>')
        total_w += 20 + zoom_view.w
        total_h = max(total_h, zoom_view.h)
    svg = ('<?xml version="1.0" encoding="UTF-8"?>\n' + f'<svg xmlns="http://www.w3.org/2000/svg" width="{f(total_w)}" height="{f(total_h)}" '
           f'viewBox="0 0 {f(total_w)} {f(total_h)}" font-family="sans-serif">\n' + '\n'.join(parts) + '\n</svg>\n')
    out = a.out or a.world.replace('.world.csv', '.world.svg')
    with open(out, 'w') as fh:
        fh.write(svg)
    print(f'{out}: {len(cells)} cells, {len(locations)} locations, {n_routes} routes, {len(svg) // 1024} KB')


if __name__ == '__main__':
    main()

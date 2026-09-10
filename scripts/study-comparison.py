#!/usr/bin/env python3
"""The two charts the planner comparison turns on, drawn from the runs.

    scripts/study-comparison.py tradeoff --runs DIR --out FILE
    scripts/study-comparison.py coverage --runs DIR --out FILE [--world Issue7]

Both read the sweep directory directly - `<world>.<plan>.<rep>.manifest.json`
and `<world>.<plan>.r1.places.csv` - so a chart cannot drift from the run it
claims to draw. Runtime is the median of the measured repetitions, never the
warm-up.

Pure python, no libraries, same plain style as study-charts.py.
"""
import argparse
import csv
import glob
import json
import os
import statistics
import sys
from xml.sax.saxutils import escape

INK, GRID = '#1f2933', '#d7dee5'
PLANS = ['parity', 'routed-mst', 'trunk', 'reverse']
PLAN_LABEL = {
    'parity': 'shipped plan',
    'routed-mst': 'routed-cost MST',
    'trunk': 'trunk + spurs',
    'reverse': 'POI-to-network',
}
PLAN_COLOUR = {
    'parity': '#1f2933',
    'routed-mst': '#2f6f8f',
    'trunk': '#c0621a',
    'reverse': '#3f7a4d',
}
CATEGORIES = ['boss', 'dungeon', 'mistlands', 'settlement', 'ruin']
CAT_LABEL = {
    'boss': 'boss altars (required)',
    'dungeon': 'dungeons',
    'mistlands': 'Mistlands structures',
    'settlement': 'settlements',
    'ruin': 'ruins and towers',
}
CAT_COLOUR = {
    'boss': '#a03030',
    'dungeon': '#2f6f8f',
    'mistlands': '#8a4ab0',
    'settlement': '#3f7a4d',
    'ruin': '#9aa5b1',
}


def load(runs):
    """Every measured run in the directory, grouped by world and plan."""
    data = {}
    for path in sorted(glob.glob(os.path.join(runs, '*.manifest.json'))):
        label = os.path.basename(path)[:-len('.manifest.json')]
        parts = label.split('.')
        if len(parts) != 3:
            continue
        world, plan, rep = parts
        manifest = json.load(open(path))
        entry = data.setdefault((world, plan), {'reps': [], 'result': manifest['result']})
        if rep != 'warm':
            entry['reps'].append(manifest['result']['generateSeconds'])
        if rep == 'r1':
            entry['result'] = manifest['result']
            entry['places'] = os.path.join(runs, f'{label}.places.csv')
    for entry in data.values():
        entry['median'] = statistics.median(entry['reps']) if entry['reps'] else 0.0
        entry['low'] = min(entry['reps']) if entry['reps'] else 0.0
        entry['high'] = max(entry['reps']) if entry['reps'] else 0.0
    return data


def svg(body, width, height, title=''):
    head = (f'<text x="10" y="21" font-size="15" fill="{INK}">{escape(title)}</text>'
            if title else '')
    # Padded to a square, and the drawing's own size printed in a comment: the
    # only SVG rasteriser to hand fits a drawing into a square canvas and the
    # fit it chooses depends on the aspect ratio, so a wide chart came back
    # with its right-hand panel cut off. A square goes in one to one, and
    # png-crop takes the drawing back out of it.
    side = max(width, height)
    return ('<?xml version="1.0" encoding="UTF-8"?>\n'
            f'<!-- drawing {width}x{height} in a {side}x{side} canvas -->\n'
            f'<svg xmlns="http://www.w3.org/2000/svg" width="{side}" height="{side}" '
            f'viewBox="0 0 {side} {side}" font-family="sans-serif">\n'
            f'<rect width="{side}" height="{side}" fill="#ffffff"/>\n'
            + head + '\n' + body + '\n</svg>\n')


def tradeoff(a):
    data = load(a.runs)
    worlds = sorted({w for w, _ in data})
    if not worlds:
        sys.exit(f'{a.runs}: no runs found')

    panel_w, panel_h, gap = 300, 250, 26
    left, top = 56, 46
    width = left + len(worlds) * (panel_w + gap) + 10
    bars_h = 150
    height = top + panel_h + 60 + bars_h + 40
    out = []

    served = [data[(w, p)]['result']['placesServedTolerant'] for w in worlds for p in PLANS
              if (w, p) in data]
    road = [data[(w, p)]['result']['uniqueRoadLengthMeters'] / 1000 for w in worlds for p in PLANS
            if (w, p) in data]
    # Shared axes across panels: three panels on three different scales would
    # invite exactly the comparison they cannot support.
    ylo, yhi = min(served) - 6, max(served) + 6
    xlo, xhi = min(road) - 4, max(road) + 4

    for i, world in enumerate(worlds):
        x0 = left + i * (panel_w + gap)
        y0, y1 = top + panel_h, top
        out.append(f'<rect x="{x0}" y="{y1}" width="{panel_w}" height="{panel_h}" fill="none" '
                   f'stroke="{GRID}"/>')
        out.append(f'<text x="{x0 + panel_w / 2}" y="{y1 - 8}" font-size="12" fill="{INK}" '
                   f'text-anchor="middle">{escape(world)}</text>')

        def px(v):
            return x0 + (v - xlo) / (xhi - xlo) * panel_w

        def py(v):
            return y0 - (v - ylo) / (yhi - ylo) * panel_h

        for frac in range(1, 5):
            v = ylo + (yhi - ylo) * frac / 5
            out.append(f'<line x1="{x0}" y1="{py(v):.1f}" x2="{x0 + panel_w}" y2="{py(v):.1f}" '
                       f'stroke="{GRID}" stroke-dasharray="2 3"/>')
            if i == 0:
                out.append(f'<text x="{x0 - 6}" y="{py(v) + 4:.1f}" font-size="10" fill="{INK}" '
                           f'text-anchor="end">{v:.0f}</text>')
        for frac in range(1, 5):
            v = xlo + (xhi - xlo) * frac / 5
            out.append(f'<text x="{px(v):.1f}" y="{y0 + 15}" font-size="10" fill="{INK}" '
                       f'text-anchor="middle">{v:.0f}</text>')

        # Each plan's label sits at its own offset from its marker. Two plans
        # that land on the same point are the interesting case, and a label
        # under another label hides exactly that.
        offsets = {'parity': -12, 'routed-mst': -25, 'trunk': 17, 'reverse': 30}
        for plan in PLANS:
            if (world, plan) not in data:
                continue
            r = data[(world, plan)]['result']
            x, y = px(r['uniqueRoadLengthMeters'] / 1000), py(r['placesServedTolerant'])
            out.append(f'<circle cx="{x:.1f}" cy="{y:.1f}" r="6" fill="{PLAN_COLOUR[plan]}" '
                       f'fill-opacity="0.85"/>')
            ly = y + offsets[plan]
            out.append(f'<line x1="{x:.1f}" y1="{y:.1f}" x2="{x:.1f}" '
                       f'y2="{ly + (4 if offsets[plan] < 0 else -9):.1f}" '
                       f'stroke="{PLAN_COLOUR[plan]}" stroke-width="0.8" stroke-opacity="0.5"/>')
            anchor = 'middle'
            lx = x
            if x < x0 + 46:
                anchor, lx = 'start', x0 + 3
            elif x > x0 + panel_w - 46:
                anchor, lx = 'end', x0 + panel_w - 3
            out.append(f'<text x="{lx:.1f}" y="{ly:.1f}" font-size="9.5" '
                       f'fill="{PLAN_COLOUR[plan]}" text-anchor="{anchor}">'
                       f'{escape(PLAN_LABEL[plan])}</text>')

    out.append(f'<text x="{left + (width - left) / 2 - 5}" y="{top + panel_h + 34}" font-size="11" '
               f'fill="{INK}" text-anchor="middle">distinct road on the ground (km)</text>')
    out.append(f'<text x="16" y="{top + panel_h / 2}" font-size="11" fill="{INK}" '
               f'text-anchor="middle" transform="rotate(-90 16 {top + panel_h / 2})">'
               f'places served</text>')

    # Runtime is its own panel rather than a marker size: it is the third
    # quantity the choice turns on and it varies by a factor of four, which no
    # readable marker can carry.
    by = top + panel_h + 62
    out.append(f'<text x="10" y="{by + 12}" font-size="12" fill="{INK}">'
               f'generation time, median of three measured runs '
               f'(Release build, terrain already loaded)</text>')
    top_sec = max(data[k]['high'] for k in data) * 1.1
    bar_top, bar_h = by + 26, bars_h - 46
    slot = (width - left - 20) / (len(worlds) * len(PLANS) + len(worlds))
    n = 0
    for world in worlds:
        first = n
        for plan in PLANS:
            if (world, plan) not in data:
                continue
            e = data[(world, plan)]
            x = left + n * slot
            h = e['median'] / top_sec * bar_h
            out.append(f'<rect x="{x:.1f}" y="{bar_top + bar_h - h:.1f}" width="{slot * 0.8:.1f}" '
                       f'height="{h:.1f}" fill="{PLAN_COLOUR[plan]}" fill-opacity="0.85"/>')
            out.append(f'<line x1="{x + slot * 0.4:.1f}" y1="{bar_top + bar_h - e["low"] / top_sec * bar_h:.1f}" '
                       f'x2="{x + slot * 0.4:.1f}" y2="{bar_top + bar_h - e["high"] / top_sec * bar_h:.1f}" '
                       f'stroke="{INK}" stroke-width="1"/>')
            out.append(f'<text x="{x + slot * 0.4:.1f}" y="{bar_top + bar_h - h - 5:.1f}" '
                       f'font-size="9.5" fill="{INK}" text-anchor="middle">'
                       f'{e["median"]:.1f}s</text>')
            n += 1
        out.append(f'<text x="{left + (first + (n - first) / 2) * slot:.1f}" '
                   f'y="{bar_top + bar_h + 15}" font-size="10.5" fill="{INK}" '
                   f'text-anchor="middle">{escape(world)}</text>')
        n += 1
    out.append(f'<line x1="{left}" y1="{bar_top + bar_h}" x2="{width - 14}" y2="{bar_top + bar_h}" '
               f'stroke="{INK}"/>')

    for i, plan in enumerate(PLANS):
        x = left + i * 190
        out.append(f'<rect x="{x}" y="{height - 20}" width="11" height="11" '
                   f'fill="{PLAN_COLOUR[plan]}"/>')
        out.append(f'<text x="{x + 16}" y="{height - 10}" font-size="11" fill="{INK}">'
                   f'{escape(PLAN_LABEL[plan])}</text>')

    return svg('\n'.join(out), width, height, title=a.title)


def coverage(a):
    data = load(a.runs)
    world = a.world
    counts, totals = {}, {}
    for plan in PLANS:
        entry = data.get((world, plan))
        if not entry or 'places' not in entry:
            continue
        served = dict.fromkeys(CATEGORIES, 0)
        selected = dict.fromkeys(CATEGORIES, 0)
        for row in csv.DictReader(open(entry['places'])):
            cat = row['category']
            if cat not in served:
                continue
            if row['selected'] == 'true':
                selected[cat] += 1
            if row['served_tolerant'] == 'true':
                served[cat] += 1
        counts[plan] = served
        totals[plan] = selected
    if not counts:
        sys.exit(f'{a.runs}: no places tables for {world}')

    width, height = 760, 400
    left, right, bottom, topgap = 60, 20, 96, 56
    plot_h = height - bottom - topgap
    out = []
    top = max(max(c.values()) for c in counts.values()) * 1.15
    group = (width - left - right) / len(counts)
    bar = group / (len(CATEGORIES) + 0.8)

    for frac in range(1, 6):
        v = top * frac / 5
        y = topgap + plot_h - v / top * plot_h
        out.append(f'<line x1="{left}" y1="{y:.1f}" x2="{width - right}" y2="{y:.1f}" '
                   f'stroke="{GRID}"/>')
        out.append(f'<text x="{left - 6}" y="{y + 4:.1f}" font-size="10" fill="{INK}" '
                   f'text-anchor="end">{v:.0f}</text>')

    for g, plan in enumerate([p for p in PLANS if p in counts]):
        gx = left + g * group
        for c, cat in enumerate(CATEGORIES):
            v = counts[plan][cat]
            h = v / top * plot_h
            x = gx + 8 + c * bar
            out.append(f'<rect x="{x:.1f}" y="{topgap + plot_h - h:.1f}" width="{bar * 0.86:.1f}" '
                       f'height="{h:.1f}" fill="{CAT_COLOUR[cat]}"/>')
            out.append(f'<text x="{x + bar * 0.43:.1f}" y="{topgap + plot_h - h - 4:.1f}" '
                       f'font-size="9.5" fill="{INK}" text-anchor="middle">{v}</text>')
            if cat == 'boss':
                out.append(f'<text x="{x + bar * 0.43:.1f}" y="{topgap + plot_h - h - 15:.1f}" '
                           f'font-size="9" fill="{CAT_COLOUR[cat]}" text-anchor="middle">'
                           f'of {totals[plan][cat]}</text>')
        out.append(f'<text x="{gx + group / 2:.1f}" y="{topgap + plot_h + 17}" font-size="11" '
                   f'fill="{PLAN_COLOUR[plan]}" text-anchor="middle">'
                   f'{escape(PLAN_LABEL[plan])}</text>')

    out.append(f'<line x1="{left}" y1="{topgap + plot_h}" x2="{width - right}" '
               f'y2="{topgap + plot_h}" stroke="{INK}"/>')
    out.append(f'<text x="16" y="{topgap + plot_h / 2}" font-size="11" fill="{INK}" '
               f'text-anchor="middle" transform="rotate(-90 16 {topgap + plot_h / 2})">'
               f'places served</text>')
    for i, cat in enumerate(CATEGORIES):
        x = left + (i % 3) * 230
        y = height - 40 + (i // 3) * 18
        out.append(f'<rect x="{x}" y="{y - 9}" width="11" height="11" fill="{CAT_COLOUR[cat]}"/>')
        out.append(f'<text x="{x + 16}" y="{y}" font-size="11" fill="{INK}">'
                   f'{escape(CAT_LABEL[cat])}</text>')
    out.append(f'<text x="10" y="40" font-size="11" fill="{INK}">'
               f'{escape(world)}; every boss altar on a selected island is required, '
               f'the rest are what the priority table happened to select</text>')

    return svg('\n'.join(out), width, height, title=a.title)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('chart', choices=['tradeoff', 'coverage'])
    ap.add_argument('--runs', required=True)
    ap.add_argument('--out', required=True)
    ap.add_argument('--world', default='Issue7')
    ap.add_argument('--title', default='')
    a = ap.parse_args()
    text = tradeoff(a) if a.chart == 'tradeoff' else coverage(a)
    with open(a.out, 'w') as fh:
        fh.write(text)
    print(f'wrote {a.out}')


if __name__ == '__main__':
    main()

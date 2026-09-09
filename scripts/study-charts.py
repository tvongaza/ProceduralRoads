#!/usr/bin/env python3
"""Draw the study's charts as SVG. Pure python, no libraries.

    scripts/study-charts.py <chart> --out FILE [chart arguments]

Charts:
    funnel      --counts "label=n,label=n,..."      the places funnel
    plateau     --rows "iters,roads,budget,frontier;..."
    tradeoff    --rows "label,x,y;..." --x-label .. --y-label ..
    histogram   --series "name=v,v,v;name=v,v,v" --bins 20 --x-label ..

Small and deliberately plain: these sit beside maps in a study document, so
they need to be legible at a glance and readable in both a browser and a
markdown preview, not pretty.
"""
import argparse
import sys
from xml.sax.saxutils import escape

W, H = 720, 360
PAD_L, PAD_R, PAD_T, PAD_B = 70, 24, 34, 52
INK, GRID, ACCENT = '#1f2933', '#d7dee5', '#2f6f8f'
SERIES = ['#2f6f8f', '#c0621a', '#3f7a4d', '#8a4ab0', '#a03030']


def svg(body, width=W, height=H, title=''):
    head = (f'<text x="8" y="20" font-size="14" fill="{INK}">{escape(title)}</text>'
            if title else '')
    return ('<?xml version="1.0" encoding="UTF-8"?>\n'
            f'<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}" '
            f'viewBox="0 0 {width} {height}" font-family="sans-serif">\n'
            f'<rect width="{width}" height="{height}" fill="#ffffff"/>\n'
            + head + '\n' + body + '\n</svg>\n')


def axes(x_label, y_label, height=H, width=W):
    x0, y0, x1, y1 = PAD_L, height - PAD_B, width - PAD_R, PAD_T
    return ([f'<line x1="{x0}" y1="{y0}" x2="{x1}" y2="{y0}" stroke="{INK}" stroke-width="1"/>',
             f'<line x1="{x0}" y1="{y0}" x2="{x0}" y2="{y1}" stroke="{INK}" stroke-width="1"/>',
             f'<text x="{(x0 + x1) / 2}" y="{height - 12}" font-size="11" fill="{INK}" '
             f'text-anchor="middle">{escape(x_label)}</text>',
             f'<text x="14" y="{(y0 + y1) / 2}" font-size="11" fill="{INK}" text-anchor="middle" '
             f'transform="rotate(-90 14 {(y0 + y1) / 2})">{escape(y_label)}</text>'],
            (x0, y0, x1, y1))


def funnel(a):
    pairs = [(p.split('=')[0], float(p.split('=')[1])) for p in a.counts.split(',')]
    # The gutter is sized to the longest label and the tail to the widest
    # number: a funnel whose stages are cut off says nothing.
    gutter = 14 + int(max(len(label) for label, _ in pairs) * 6.4)
    # Generous: the converters do not all scale a non-square drawing the
    # same way, and a clipped number is worse than white space.
    tail = 60 + int(len(f'{max(v for _, v in pairs):,.0f}') * 10)
    width_total = gutter + 560 + tail
    out, top = [], max(v for _, v in pairs)
    bar_h, gap = 34, 12
    height = PAD_T + len(pairs) * (bar_h + gap) + 16
    for i, (label, value) in enumerate(pairs):
        y = PAD_T + i * (bar_h + gap)
        width = max(2, (value / top) * 560)
        out.append(f'<rect x="{gutter}" y="{y}" width="{width:.1f}" height="{bar_h}" '
                   f'fill="{ACCENT}" fill-opacity="{0.85 - i * 0.1:.2f}"/>')
        out.append(f'<text x="{gutter - 6}" y="{y + bar_h * 0.66:.0f}" font-size="11" fill="{INK}" '
                   f'text-anchor="end">{escape(label)}</text>')
        out.append(f'<text x="{gutter + width + 6:.1f}" y="{y + bar_h * 0.66:.0f}" font-size="12" '
                   f'fill="{INK}">{value:,.0f}</text>')
    return svg('\n'.join(out), width=width_total, height=height, title=a.title)


def plateau(a):
    rows = [[float(v) for v in r.split(',')] for r in a.rows.split(';') if r]
    out, (x0, y0, x1, y1) = axes(a.x_label, a.y_label)
    xs = [r[0] for r in rows]
    top = max(max(r[1:]) for r in rows)

    def px(x):
        return x0 + (x - min(xs)) / (max(xs) - min(xs)) * (x1 - x0)

    def py(v):
        return y0 - (v / top) * (y0 - y1)

    for s, (name, idx) in enumerate([('roads built', 1), ('budget spent', 2), ('frontier exhausted', 3)]):
        pts = ' '.join(f'{px(r[0]):.1f},{py(r[idx]):.1f}' for r in rows)
        out.append(f'<polyline points="{pts}" fill="none" stroke="{SERIES[s]}" stroke-width="2.4"/>')
        for r in rows:
            out.append(f'<circle cx="{px(r[0]):.1f}" cy="{py(r[idx]):.1f}" r="3" fill="{SERIES[s]}"/>')
        out.append(f'<text x="{x1 - 4}" y="{PAD_T + 14 + s * 16}" font-size="11" fill="{SERIES[s]}" '
                   f'text-anchor="end">{escape(name)}</text>')
    for r in rows:
        # Compact ticks: at linear spacing the low values crowd, and "5k" is as
        # readable as "5,000" without colliding with its neighbour.
        label = f'{r[0] / 1000:g}k' if r[0] >= 1000 else f'{r[0]:g}'
        out.append(f'<text x="{px(r[0]):.1f}" y="{y0 + 16}" font-size="10" fill="{INK}" '
                   f'text-anchor="middle">{label}</text>')
    for frac in (0.25, 0.5, 0.75, 1.0):
        y = py(top * frac)
        out.append(f'<line x1="{x0}" y1="{y:.1f}" x2="{x1}" y2="{y:.1f}" stroke="{GRID}"/>')
        out.append(f'<text x="{x0 - 6}" y="{y + 4:.1f}" font-size="10" fill="{INK}" '
                   f'text-anchor="end">{top * frac:.0f}</text>')
    return svg('\n'.join(out), title=a.title)


def tradeoff(a):
    rows = []
    for r in a.rows.split(';'):
        if not r:
            continue
        label, x, y = r.rsplit(',', 2)
        rows.append((label, float(x), float(y)))
    out, (x0, y0, x1, y1) = axes(a.x_label, a.y_label)
    xmax, ymax = max(r[1] for r in rows) * 1.1, max(r[2] for r in rows) * 1.15

    def px(v):
        return x0 + v / xmax * (x1 - x0)

    def py(v):
        return y0 - v / ymax * (y0 - y1)

    pts = sorted(rows, key=lambda r: r[1])
    out.append('<polyline points="' + ' '.join(f'{px(r[1]):.1f},{py(r[2]):.1f}' for r in pts)
               + f'" fill="none" stroke="{GRID}" stroke-width="1.5"/>')
    for label, x, y in rows:
        out.append(f'<circle cx="{px(x):.1f}" cy="{py(y):.1f}" r="5" fill="{ACCENT}"/>')
        out.append(f'<text x="{px(x):.1f}" y="{py(y) - 10:.1f}" font-size="10" fill="{INK}" '
                   f'text-anchor="middle">{escape(label)}</text>')
    for frac in (0.25, 0.5, 0.75, 1.0):
        y = py(ymax * frac)
        out.append(f'<line x1="{x0}" y1="{y:.1f}" x2="{x1}" y2="{y:.1f}" stroke="{GRID}"/>')
        out.append(f'<text x="{x0 - 6}" y="{y + 4:.1f}" font-size="10" fill="{INK}" '
                   f'text-anchor="end">{ymax * frac:,.0f}</text>')
        x = px(xmax * frac)
        out.append(f'<text x="{x:.1f}" y="{y0 + 16}" font-size="10" fill="{INK}" '
                   f'text-anchor="middle">{xmax * frac:,.0f}</text>')
    return svg('\n'.join(out), title=a.title)


def histogram(a):
    series = []
    for part in a.series.split(';'):
        if not part:
            continue
        name, values = part.split('=', 1)
        series.append((name, [float(v) for v in values.split(',') if v]))
    out, (x0, y0, x1, y1) = axes(a.x_label, 'share of samples')
    top = max(max(v) for _, v in series)
    bins = a.bins

    def px(v):
        return x0 + v / top * (x1 - x0)

    peak = 0
    hists = []
    for name, values in series:
        counts = [0] * bins
        for v in values:
            counts[min(bins - 1, int(v / top * bins))] += 1
        share = [c / len(values) for c in counts]
        peak = max(peak, max(share))
        hists.append((name, share))

    for s, (name, share) in enumerate(hists):
        pts = []
        for i, v in enumerate(share):
            x = x0 + (i + 0.5) / bins * (x1 - x0)
            pts.append(f'{x:.1f},{y0 - v / peak * (y0 - y1):.1f}')
        out.append(f'<polyline points="{" ".join(pts)}" fill="none" stroke="{SERIES[s]}" '
                   f'stroke-width="2.2"/>')
        out.append(f'<text x="{x1 - 4}" y="{PAD_T + 14 + s * 16}" font-size="11" fill="{SERIES[s]}" '
                   f'text-anchor="end">{escape(name)}</text>')
    for frac in (0.25, 0.5, 0.75, 1.0):
        out.append(f'<text x="{px(top * frac):.1f}" y="{y0 + 16}" font-size="10" fill="{INK}" '
                   f'text-anchor="middle">{top * frac:,.0f}</text>')
    return svg('\n'.join(out), title=a.title)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('chart', choices=['funnel', 'plateau', 'tradeoff', 'histogram'])
    ap.add_argument('--out', required=True)
    ap.add_argument('--title', default='')
    ap.add_argument('--counts', default='')
    ap.add_argument('--rows', default='')
    ap.add_argument('--series', default='')
    ap.add_argument('--bins', type=int, default=24)
    ap.add_argument('--x-label', dest='x_label', default='')
    ap.add_argument('--y-label', dest='y_label', default='')
    a = ap.parse_args()

    body = {'funnel': funnel, 'plateau': plateau, 'tradeoff': tradeoff, 'histogram': histogram}[a.chart](a)
    with open(a.out, 'w') as fh:
        fh.write(body)
    print(f'{a.out}: {len(body) // 1024 + 1} KB')


if __name__ == '__main__':
    main()

#!/usr/bin/env python3
"""Put island views side by side on one sheet, so they can be compared.

    scripts/island-sheet.py --out sheet.svg "label=view.png" ["label=view.png" ...]
        [--columns 3] [--title "..."]

The views must already share bounds and scale - the renderer's --zoom-only
produces them that way. This only lays them out and labels them, because a
comparison the reader has to make by flipping between two files is a
comparison most readers will not make.
"""
import argparse
import base64
import os
import struct
import sys
from xml.sax.saxutils import escape

LABEL_HEIGHT = 26


def png_size(path):
    with open(path, 'rb') as fh:
        head = fh.read(24)
    if head[:8] != b'\x89PNG\r\n\x1a\n':
        sys.exit(f'{path}: not a PNG')
    return struct.unpack('>II', head[16:24])


def data_uri(path):
    with open(path, 'rb') as fh:
        return 'data:image/png;base64,' + base64.b64encode(fh.read()).decode('ascii')


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('views', nargs='+', help='label=path.png')
    ap.add_argument('--out', required=True)
    ap.add_argument('--columns', type=int, default=0, help='default: all in one row')
    ap.add_argument('--title', default='')
    ap.add_argument('--width', type=float, default=700, help='width of each view in the sheet')
    a = ap.parse_args()

    panels = []
    for view in a.views:
        if '=' not in view:
            sys.exit(f'expected label=path.png, got {view}')
        label, path = view.split('=', 1)
        if not os.path.exists(path):
            sys.exit(f'{path}: no such file')
        panels.append((label, path, *png_size(path)))

    columns = a.columns or len(panels)
    rows = (len(panels) + columns - 1) // columns
    scale = a.width / max(w for _, _, w, _ in panels)
    cell_h = max(h for _, _, _, h in panels) * scale + LABEL_HEIGHT
    top = 22 if a.title else 0

    out = []
    for i, (label, path, w, h) in enumerate(panels):
        x = (i % columns) * a.width
        y = top + (i // columns) * cell_h
        out.append(f'<text x="{x + 4:.1f}" y="{y + 16:.1f}" font-size="13" fill="#111">{escape(label)}</text>')
        out.append(f'<image x="{x:.1f}" y="{y + LABEL_HEIGHT:.1f}" width="{w * scale:.1f}" '
                   f'height="{h * scale:.1f}" href="{data_uri(path)}"/>')

    width = columns * a.width
    height = top + rows * cell_h
    title = (f'<text x="4" y="15" font-size="14" fill="#111">{escape(a.title)}</text>'
             if a.title else '')
    svg = ('<?xml version="1.0" encoding="UTF-8"?>\n'
           f'<svg xmlns="http://www.w3.org/2000/svg" width="{width:.0f}" height="{height:.0f}" '
           f'viewBox="0 0 {width:.0f} {height:.0f}" font-family="sans-serif">\n'
           f'<rect width="{width:.0f}" height="{height:.0f}" fill="#ffffff"/>\n'
           + title + '\n' + '\n'.join(out) + '\n</svg>\n')

    with open(a.out, 'w') as fh:
        fh.write(svg)
    print(f'{a.out}: {len(panels)} view(s) in {rows} row(s), {len(svg) // 1024} KB')


if __name__ == '__main__':
    main()

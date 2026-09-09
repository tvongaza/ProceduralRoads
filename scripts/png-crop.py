#!/usr/bin/env python3
"""Crop a PNG to its top-left region.

    scripts/png-crop.py <in.png> <out.png>                  trim to the content
    scripts/png-crop.py <in.png> <out.png> <width> <height>  crop from the top-left

The SVG-to-PNG converters on hand pad a non-square drawing into a square
canvas and centre it, and the only crop tool available crops from the centre
too - which cuts the top off a chart. This decodes, crops from the top-left
and re-encodes, so a 720x360 drawing rendered into a square comes back as the
drawing.

Handles what those converters emit: 8-bit RGB or RGBA, non-interlaced.
"""
import struct
import sys
import zlib


def read_png(path):
    data = open(path, 'rb').read()
    if data[:8] != b'\x89PNG\r\n\x1a\n':
        sys.exit(f'{path}: not a PNG')

    pos, idat, meta = 8, bytearray(), None
    while pos < len(data):
        length, tag = struct.unpack('>I4s', data[pos:pos + 8])
        body = data[pos + 8:pos + 8 + length]
        if tag == b'IHDR':
            meta = struct.unpack('>IIBBBBB', body)
        elif tag == b'IDAT':
            idat += body
        elif tag == b'IEND':
            break
        pos += 12 + length

    width, height, depth, colour, compression, filt, interlace = meta
    if depth != 8 or colour not in (2, 6) or interlace != 0:
        sys.exit(f'{path}: only 8-bit RGB/RGBA without interlacing (got depth {depth}, colour {colour})')

    channels = 3 if colour == 2 else 4
    raw = zlib.decompress(bytes(idat))
    stride = width * channels
    rows, prior = [], bytearray(stride)
    pos = 0
    for _ in range(height):
        method = raw[pos]
        line = bytearray(raw[pos + 1:pos + 1 + stride])
        pos += 1 + stride
        for i in range(stride):
            a = line[i - channels] if i >= channels else 0
            b = prior[i]
            c = prior[i - channels] if i >= channels else 0
            if method == 1:
                line[i] = (line[i] + a) & 0xFF
            elif method == 2:
                line[i] = (line[i] + b) & 0xFF
            elif method == 3:
                line[i] = (line[i] + ((a + b) >> 1)) & 0xFF
            elif method == 4:
                p = a + b - c
                pa, pb, pc = abs(p - a), abs(p - b), abs(p - c)
                pred = a if (pa <= pb and pa <= pc) else (b if pb <= pc else c)
                line[i] = (line[i] + pred) & 0xFF
        rows.append(line)
        prior = line

    return width, height, channels, rows


def write_png(path, width, height, channels, rows):
    raw = b''.join(b'\x00' + bytes(row) for row in rows)

    def chunk(tag, body):
        return (struct.pack('>I', len(body)) + tag + body
                + struct.pack('>I', zlib.crc32(tag + body) & 0xffffffff))

    colour = 2 if channels == 3 else 6
    png = (b'\x89PNG\r\n\x1a\n'
           + chunk(b'IHDR', struct.pack('>IIBBBBB', width, height, 8, colour, 0, 0, 0))
           + chunk(b'IDAT', zlib.compress(raw, 9))
           + chunk(b'IEND', b''))
    open(path, 'wb').write(png)


def content_bounds(width, height, channels, rows, margin=6, white=248):
    """The box that holds everything that is not background.

    Guessing where a converter put the drawing inside its square canvas got it
    wrong twice - clipping the tops of charts and the ends of their labels - so
    the crop now looks at the pixels instead of trusting an assumption.
    """
    left, right, top, bottom = width, -1, height, -1
    for y, row in enumerate(rows):
        for x in range(width):
            i = x * channels
            if row[i] < white or row[i + 1] < white or row[i + 2] < white:
                if x < left: left = x
                if x > right: right = x
                if y < top: top = y
                if y > bottom: bottom = y
    if right < 0:
        return 0, 0, width, height
    left = max(0, left - margin)
    top = max(0, top - margin)
    right = min(width - 1, right + margin)
    bottom = min(height - 1, bottom + margin)
    return left, top, right - left + 1, bottom - top + 1


def main():
    if len(sys.argv) not in (3, 5):
        sys.exit(__doc__)
    source, target = sys.argv[1], sys.argv[2]
    width, height, channels, rows = read_png(source)

    if len(sys.argv) == 3:
        x, y, want_w, want_h = content_bounds(width, height, channels, rows)
    else:
        x, y = 0, 0
        want_w, want_h = min(int(sys.argv[3]), width), min(int(sys.argv[4]), height)

    cropped = [row[x * channels:(x + want_w) * channels] for row in rows[y:y + want_h]]
    write_png(target, want_w, want_h, channels, cropped)
    print(f'{target}: {want_w}x{want_h} from {width}x{height}')


if __name__ == '__main__':
    main()

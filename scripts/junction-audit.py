#!/usr/bin/env python3
"""Two screens on things the geometry metrics cannot say about themselves.

1. ENDPOINT-HEIGHT SCREEN. A junction is a 2-D test: an end within 12 m of
   another road's length, with no regard for height. This walks every junction
   the metric counts and reports the height difference across it at the nearest
   point, which is an ordering for an inspection and NOT a walkability check:
   two endpoints at the same height can still have a river, a wall or a ravine
   between them, and this sees none of those. It also screens only the 24 m
   endpoint rule; the study's component count has a second rule - two roads
   ending near the same place are one component, however far apart they finish
   on its approach circle - and no distance screen here covers that.

   Tee counts match the run manifest by construction. End-to-end counts do not:
   the manifest counts ROADS THAT HAVE an end-to-end join, this counts
   junctions.

2. OUT-OF-WORLD SETTLING. The offline search is not bounded to the world disc,
   and the dump answers a point past the rim with the rim's own values. This
   counts the failed attempts whose settled box left the disc and what they
   cost. It does not correct for them, and the study's runtimes are not
   corrected either.

    junction-audit.py <routes.csv> <attempts.csv>
"""
import csv, math, sys
from collections import defaultdict

JUNCTION_RADIUS = 12.0     # NetworkMetrics.JunctionRadius
END_MARGIN = 24.0          # NetworkMetrics.EndMargin
WORLD_RADIUS = 10000.0

routes = defaultdict(list)
with open(sys.argv[1]) as fh:
    for r in csv.DictReader(fh):
        routes[int(r['route_index'])].append(
            (float(r['x']), float(r['y']), float(r['z']), r['kind']))
routes = {i: p for i, p in routes.items() if len(p) >= 2}

def flat(a, b):
    return math.hypot(a[0] - b[0], a[2] - b[2])

lengths = {}
cum = {}
for i, pts in routes.items():
    run = [0.0]
    for k in range(1, len(pts)):
        run.append(run[-1] + flat(pts[k - 1], pts[k]))
    cum[i] = run
    lengths[i] = run[-1]

# One junction, not one point pair: a road end within twelve metres of
# another road matches dozens of that road's points, and counting those as
# dozens of junctions would say nothing about how many places two roads
# actually meet. Each (end, other road) pair is collapsed to the point that
# is nearest on the ground, which is where a player would step across.
best = {}
for i, pts in routes.items():
    for e, end in enumerate((pts[0], pts[-1])):
        for j, other in routes.items():
            if j == i:
                continue
            for k, p in enumerate(other):
                gap = flat(p, end)
                if gap > JUNCTION_RADIUS:
                    continue
                from_end = min(cum[j][k], lengths[j] - cum[j][k])
                key = (i, e, j)
                rec = [i, j, abs(p[1] - end[1]), gap, end[0], end[2], p[3],
                       from_end > END_MARGIN]
                if key not in best or gap < best[key][3]:
                    # The classification is the metric's, which asks whether
                    # ANY matched point is clear of the other road's ends;
                    # the height is read at the nearest point, which is where
                    # a player would actually step across.
                    rec[7] = rec[7] or (key in best and best[key][7])
                    best[key] = rec
                elif rec[7]:
                    best[key][7] = True

tees = [r for r in best.values() if r[7]]
ends = [r for r in best.values() if not r[7]]

def report(name, rows):
    if not rows:
        print(f'{name}: none')
        return
    deltas = sorted(r[2] for r in rows)
    print(f'{name}: {len(rows)} junctions over {len({r[0] for r in rows})} roads '
          f'(the run reports the road count, not the junction count)')
    print(f'   height difference across the join: median {deltas[len(deltas)//2]:.2f} m, '
          f'p90 {deltas[int(len(deltas)*0.9)]:.2f} m, max {deltas[-1]:.2f} m')
    for limit in (1, 2, 5, 10):
        n = sum(1 for dv in deltas if dv > limit)
        print(f'   over {limit:>2} m: {n:4} ({n/len(deltas)*100:.0f} %)')
    worst = sorted(rows, key=lambda r: -r[2])[:5]
    for w in worst:
        print(f'   road {w[0]} -> road {w[1]}: {w[2]:.1f} m apart in height, '
              f'{w[3]:.1f} m apart on the ground, at ({w[4]:.0f}, {w[5]:.0f}), '
              f'other road is {w[6]} there')

report('tee junctions', tees)
report('end-to-end joins', ends)

print()
out = 0
cells = 0
with open(sys.argv[2]) as fh:
    for r in csv.DictReader(fh):
        if r['connected'] == 'true':
            continue
        box = [float(r['settled_min_x']), float(r['settled_min_z']),
               float(r['settled_max_x']), float(r['settled_max_z'])]
        corners = [(box[0], box[1]), (box[0], box[3]), (box[2], box[1]), (box[2], box[3])]
        if any(math.hypot(x, z) > WORLD_RADIUS for x, z in corners):
            out += 1
            cells += int(r['settled_cells'])
print(f'failed attempts whose settled box reaches past the world rim: {out}, '
      f'{cells:,} cells settled between them')

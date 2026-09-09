# Road networks: a routing study

**Routing study; gameplay validation pending.** Everything below is measured
on generated networks — road counts, lengths, what connects to what, and why a
connection failed. None of it has been played. Where a number would change a
design decision, it needs a session in game first, and the places that most
need one are named at the end.

This came out of issue #7 ("Roads seem to be limited to 2-3 per island").
It reproduces what the issue describes, finds a different cause than the one
being tuned there, and lays out some options. The choice among them is yours.

## How this was measured

Road generation was run against terrain read back from a dump of the world,
using the mod's own island detection, location rules, pathfinder and painting.
A whole world takes about four seconds that way, so a comparison that would
have taken hours in game takes minutes, and every run can be repeated exactly.

The dumps sample the positions the game itself samples: the 128 m grid island
detection reads, and the 8 m cells the pathfinder walks. Move costs are
therefore exact rather than interpolated.

What it does not reproduce: the pathfinder's terrain-variance ring and every
crossing-depth judgement sample between those positions, so they are
interpolated. That matters most for fords — see "What this cannot tell you".

Checked against the game on the issue's own seed, same settings both sides:

| | in game | offline |
|---|---|---|
| roads | 89 | 88 |
| total length | 59.9 km | 58.5 km |
| attempts | 158 | 158 |
| failed | 69 | 70 |

81 of 88 matched roads follow the same line within one 8 m cell. Three worlds
were used throughout: the issue's seed and two others, so nothing below rests
on a single map.

## A crash, first

Generating a whole world with bridges enabled threw and left the world with no
roads at all. Two crossings on one road can overlap on the path — a bridge's
banks walk out to the bank tops, a swamp bridge's on to dry ground — and the
painting step assumed they never did.

Fixed, with tests for both shapes, in the bridges branch.

## What the issue is actually about

Every place in the world, and what became of it, on the shipped settings with
crossings on and every island selected:

| | issue seed | world B | world C |
|---|---|---|---|
| places in the world | 11 477 | 11 407 | 11 413 |
| on a detected island | 10 051 | 10 027 | 10 091 |
| eligible for a road | 2 470 | 2 416 | 2 450 |
| selected by the island's quota | 158 | 163 | 171 |
| connected | 128 | 131 | 141 |

Of the places that are eligible, by what became of them:

| | issue seed | world B | world C |
|---|---|---|---|
| lost the island's quota | 2 312 | 2 253 | 2 279 |
| connected | 128 | 131 | 141 |
| attempted, no route exists | 29 | 32 | 29 |
| attempted, iteration budget spent | 1 | 0 | 1 |

**Ninety-three per cent of eligible places never get an attempt.** They lose
their island's quota. Pathfinding failure accounts for about one per cent, and
the iteration budget — the setting the issue discusses — for one place per
world.

The quota is `2 + area / 2 km²`. These worlds have a median island under
2 km², so more than half of all islands are capped at two places, which is one
road. That is exactly the "2-3 roads per island" the issue reports, and it is
arithmetic rather than a failure.

`MaxLocationsPerIsland` is not the lever it looks like. Raising it from 12 to
100 moves the issue's seed from 88 roads to 91. The ceiling almost never
binds; the area formula does.

## The iteration plateau

The issue reports diminishing returns around 30 000 iterations. That
reproduces, and the reason is visible once failures are split by the
pathfinder's own two reasons:

| iterations | roads | budget spent | no route exists |
|---|---|---|---|
| 5 000 | 62 | 48 | 48 |
| 10 000 | 74 | 33 | 51 |
| 20 000 | 78 | 19 | 61 |
| 30 000 | 84 | 8 | 66 |
| 60 000 | 87 | 4 | 67 |
| 120 000 | 88 | 1 | 69 |

As the budget grows, attempts that used to stop at the cap run to completion
and report that the destination cannot be reached over land at all. Past about
30 000 there is almost nothing left for a larger budget to rescue. The setting
has ranged 1 000 to 100 000 since it was added, so a reported 100 000 was
never clamped.

## What a player actually gets

The biggest island on the issue's seed: 26.7 km², 1 936 places on it, 426 of
them eligible for roads. It gets 12 selected, 6 roads, 5.2 km of road, serving
8 places — in **three separate networks**.

Across that world: 22 of 67 islands get no road, 22 get exactly one, and the
most any island gets is six. The largest connected run of road anywhere in the
world is 4 to 6 roads, and that holds on all three worlds and under every plan
tried below.

That is the ceiling worth knowing about before tuning anything: these worlds
are archipelagos, roads do not cross open sea, and no connection plan changes
that.

## Options

### The endpoint policy in PR #16

Measured one change at a time from the shipped policy, crossings on, issue
seed. "Served" counts places a road end reaches.

| change | roads | served | attempts | failed |
|---|---|---|---|---|
| shipped, unchanged | 88 | 123 | 158 | 70 |
| anchor on a location, not a coast cell | 82 | 124 | 109 | 27 |
| quota by priority then nearest | 101 | 142 | 158 | 57 |
| tree grown outward with retries | 90 | 126 | 208 | 118 |
| endpoints snapped to reachable ground | 90 | 124 | 158 | 68 |
| all of it together | 96 | 138 | 119 | 23 |

The quota change is the biggest single gain. The anchor change serves the same
places with a third of the wasted attempts.

Island selection needs its own row, because with every island selected it can
make no difference. At half the islands:

| | roads | served | attempts | failed |
|---|---|---|---|---|
| shipped | 73 | 101 | 126 | 53 |
| shipped, islands balanced over rings | 61 | 86 | 106 | 45 |
| all of #16 | 72 | 97 | 92 | 20 |
| all of #16, largest islands first | 84 | 116 | 104 | 20 |

Ring balancing takes one island per ring from the inside out, so the largest
landmasses in the world can go without roads. On these three worlds it is the
one part of the package that costs coverage; the rest gains it.

### The coast-cell anchor

Off the starter island, each island's network is rooted at the island cell
nearest its bounding box, with radius 0 — a coast cell, not a place. On the
issue's seed, 36 of 158 attempts start there and die on the first expansion:
the anchor itself is not ground a road can stand on.

They do not cost connections — rooting on a location instead builds one fewer
road, because the anchor is then spent as the root — but they waste a quarter
of all attempts and, where such a road does succeed, it ends on a beach.

### Connection plans

Same islands, same selected places, same anchor, same budget; only the plan
differs.

| plan | roads | length | served | networks | builds | planning searches |
|---|---|---|---|---|---|---|
| chain/MST by island parity (today) | 88 | 58.5 km | 123 | 54 | 158 | 0 |
| tree grown outward with retries (#16) | 90 | 61.9 km | 126 | 56 | 208 | 0 |
| MST on routed cost | 90 | 57.6 km | 126 | 56 | 90 | 261 |
| trunk and spurs | 85 | 62.1 km | 116 | 47 | 85 | 261 |
| hub and spoke | 88 | 58.2 km | 123 | 56 | 88 | 261 |

- **MST on routed cost** plans on what the pathfinder charges rather than on
  straight-line distance, so a strait or a mountain counts as the distance it
  really is. It reaches as much as any plan for the least road — 64.9 km
  against 71.0 on world B.
- **Trunk and spurs** lays one road along the island's long axis and joins
  everything else to it. Fewest separate networks on every world, about a
  tenth fewer places reached.
- **Hub and spoke** lands close to what ships today.

The last column matters: the three plans that price a connection before
building it have no failed builds, but they run 261 to 299 searches to find
out. That is not a saving, it is the same work moved earlier.

The pictures say more than the table. On the same island, the shipped plan
puts short branches around the start; trunk and spurs lays one road down the
length of the chain. Which of those is a better road network is a judgement
about playing the game, not a number, and it is the first thing worth trying
in game.

## What this cannot tell you

- **Fords.** Whether water is knee-deep is judged from blended heights sampled
  between the dumped positions, so the offline model interpolates them. On the
  issue's seed the game found 5 fords and the offline run found none. Nothing
  about fords should be concluded from these numbers.
- **Any single road.** Aggregates agree with the game to about one per cent,
  and 92 % of roads follow the same line, but 7 of 88 deviate and one existed
  only in game. A claim about one particular road needs that road, in game.
- **Whether any of this is fun.** No session was played. Cart travel,
  approaches, whether a road goes somewhere a player wants to go — none of it
  is measured here.

## Not reproduced

The issue reports 80 islands on that seed; detection finds 67 with the same
rule. Island detection does not depend on any setting, so the difference is in
the world itself, most likely a worldgen change since the report.

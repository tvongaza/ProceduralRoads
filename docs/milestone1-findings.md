# Road network study: Milestone 1 findings

Diagnostics and reproduction, measured on three worlds. Everything here is
evidence for the study document, not the study document itself: nothing in it
is phrased as a recommendation, and the in-game confirmations it still needs
are named where they are missing.

Runs are reproducible from their manifests. Terrain comes from world dumps
taken on the machine that runs the game, at the game's own sampling positions.

## How the numbers were produced

Road generation runs offline against a world read back from a dump: the mod's
own island detection, location rules, pathfinder and painting, driven by the
study runner. A whole world takes about four seconds, against roughly a minute
and a half in game plus a world load.

Three dumps per world, all on the lattice the game itself samples
(`-10000 + i*step`):

| step | samples | what it is exact for |
|---|---|---|
| 128 m | 24 649 | island detection, which reads base height on that lattice |
| 8 m | 6 255 001 | every pathfinder move cost, at the cell centres it walks |
| 50 m | 160 801 | pictures only |

What stays approximate: the pathfinder's terrain-variance ring samples at
±11.3 m, off the grid, and every crossing-depth judgement samples the blended
height every 2 m. Both are interpolated offline. That is the whole reason the
calibration below exists.

## Terrain dumps must come from the machine that runs the game

An earlier set of dumps was taken on a second machine. Same world file, byte
for byte; same game build. The terrain still differed: 28 634 of 160 801
samples differed in height, up to 311 m, 8 455 of them on land, and 13 001
differed in biome. Two dumps taken minutes apart on one machine are identical,
so the dump itself is deterministic.

The cause was a worldgen-altering mod loaded on that machine at the time. The
lesson is not about that mod: a dump is only a record of the world *as that
installation generates it*, so every dump here carries the loaded plugin set,
the world file's hash and the game build beside it, and the old dumps were
discarded rather than reconciled.

## Calibration: what an offline run may and may not claim

Same world (`nRleKzu9bI`), same settings, in game and offline: the shipped
strategy, fords and bridges on, every island, 100 000 iterations.

| | in game | offline |
|---|---|---|
| roads | 89 | 88 |
| total length | 59.9 km | 58.5 km |
| attempts | 158 | 158 |
| failed attempts | 69 | 70 |
| crossings | 24 (19 bridges, 5 fords) | 15 (15 bridges, 0 fords) |

Route by route, matching each in-game road to its offline counterpart by name
and endpoints, then measuring how far apart the two lines run:

- 88 of 89 in-game roads have an offline counterpart.
- 81 of 88 (92 %) follow the same line, median deviation within one 8 m cell.
- Median deviation across all points is 0.0 m; the 90th percentile is 7.3 m.
- 7 roads deviate materially, the worst by 68 m median and 367 m at its worst
  point; one in-game road has no offline counterpart at all.

So an offline run **may** be used to compare strategies, which is what it is
for: every strategy sees the same terrain, and the aggregate agrees with the
game to about one percent. It **may not** be used to explain why one
particular road failed or where one particular bridge stands.

The crossings row shows why in the sharpest way: offline finds no fords at
all on this world, because whether water is knee-deep is decided from blended
heights sampled every 2 m, off the grid. Any claim about fords needs the game.

## Road generation crashed with bridges on

The first in-game run of this configuration threw an
`ArgumentOutOfRangeException` partway through and produced no network at all.
The painting step assumed a road's crossings had disjoint spans; a bridge's
banks walk out to the bank tops and a swamp bridge's on to dry ground, so one
crossing's span can reach past the start of the next.

Fixed, with both shapes covered by tests, and the fix carried to the bridges
PR. The numbers above are from the fixed build.

## Islands, and where the "two or three roads per island" comes from

| | issue #7 seed | world B | world C |
|---|---|---|---|
| islands detected | 67 | 73 | 66 |
| land | 182 km² | 171 km² | 178 km² |
| median island | 1.8 km² | 1.4 km² | 1.7 km² |
| islands with no eligible location | 17 | 16 | 7 |
| islands capped at 2 locations | 38 | 47 | 38 |

The per-island cap is `2 + area / 2 km²`. On worlds whose median island is
under 2 km², over half of all islands are capped at two locations, which is
one road each. That is where "two or three roads per island" comes from, and
it is arithmetic, not a failure.

It is **not** where a low road count for a whole world comes from: the cap
still allows around 110 roads per world on these seeds. The reported 24 roads
are not explained by the cap.

## Where the places go: the funnel

One row per place in the world, with the trail kept in separate columns, on
the shipped policy with crossings on and every island selected.

| | issue #7 seed | world B | world C |
|---|---|---|---|
| placed in the world | 11 477 | 11 407 | 11 413 |
| on a detected island | 10 051 | 10 027 | 10 091 |
| an eligible road location | 2 470 | 2 416 | 2 450 |
| selected by its island's quota | 158 | 163 | 171 |
| connected | 128 | 131 | 141 |

Of the eligible places, by what became of them:

| outcome | issue #7 seed | world B | world C |
|---|---|---|---|
| lost the island's quota | 2 312 | 2 253 | 2 279 |
| connected | 128 | 131 | 141 |
| attempted, no reachable path | 29 | 32 | 29 |
| attempted, iteration budget spent | 1 | 0 | 1 |

This is the study's clearest result so far. Ninety-three per cent of the
places that are eligible for a road never get an attempt at all: they lose
their island's quota. Pathfinding failure accounts for about one per cent, and
the iteration budget - the setting the issue is about - decides the fate of a
single place per world.

The quota that does this is `2 + area / 2 km²`, not the configurable ceiling.
Raising `MaxLocationsPerIsland` from 12 to 100 moves the issue #7 seed from 88
roads to 91, and from 123 places served to 127. The config knob that looks
like it should open the network up is not the one holding it shut.

(The "attempted" count is a fuzzy join: a place is counted as attempted when
an attempt endpoint lands within 32 m of it, which can also catch a neighbour
of the real endpoint. The connected and quota rows are exact.)

## The iteration plateau

Shipped strategy, crossings on, every island, issue #7 seed, on exact 8 m
terrain. Failures are split by the pathfinder's own two reasons.

| iterations | roads | budget exhausted | frontier exhausted |
|---|---|---|---|
| 5 000 | 62 | 48 | 48 |
| 10 000 | 74 | 33 | 51 |
| 20 000 | 78 | 19 | 61 |
| 30 000 | 84 | 8 | 66 |
| 60 000 | 87 | 4 | 67 |
| 120 000 | 88 | 1 | 69 |

Roads climb steeply to about 30 000 and then flatten: 30 000 to 120 000 buys
four more roads. The split says why. As the budget grows, attempts that used
to stop at the cap run to completion and report the destination genuinely
unreachable — budget-exhausted attempts fall from 48 to 1 while
frontier-exhausted rise from 48 to 69. Past the plateau almost nothing is
budget-limited, so raising the limit cannot help.

The setting has ranged 1 000 to 100 000 since it was introduced, so a reported
100 000 was not clamped.

## What each policy difference is worth

One factor changed at a time from the shipped policy, crossings on, exact
terrain, issue #7 seed. "Served" counts places a road end reaches; road count
is a description, not a score, because a policy that selects more destinations
builds more roads between them either way.

| change | roads | served | attempts | failed |
|---|---|---|---|---|
| shipped, unchanged | 88 | 123 | 158 | 70 |
| anchor on a location instead of a coast cell | 82 | 124 | 109 | 27 |
| quota by priority then nearest | 101 | 142 | 158 | 57 |
| one tree grown outward with retries | 90 | 126 | 208 | 118 |
| endpoints filtered to reachable ground | 87 | 123 | 158 | 71 |
| endpoints snapped to reachable ground | 90 | 124 | 158 | 68 |
| all of the above together | 96 | 138 | 119 | 23 |

Island selection is missing from that table on purpose: with every island
selected it can make no difference. At half the islands, where it can:

| | roads | served | attempts | failed |
|---|---|---|---|---|
| shipped | 73 | 101 | 126 | 53 |
| shipped with ring-balanced islands | 61 | 86 | 106 | 45 |
| all changes together | 72 | 97 | 92 | 20 |
| all changes, but largest islands first | 84 | 116 | 104 | 20 |

Ring balancing takes one island per ring from the inside out, so the largest
landmasses in the world can go without roads. It is the one change that costs
coverage; the rest of the package gains it.

## The coast-cell anchor

Off the starter island the shipped policy roots each island's network at the
island cell nearest its own bounding box, radius 0 — a coast cell, not a place.

On the issue #7 seed, 36 of 158 attempts start there and die on the first
expansion: the search settles one cell and stops, because the anchor itself is
not ground a road can stand on. They cost time and fill the log, and where such
a road does succeed it ends on a coast cell.

They are not, however, what limits the network: rooting on a location instead
connects one *fewer* road (82 against 88) while serving the same number of
places, because the anchor location is then spent as the root. The honest
statement is that the coast-cell anchor wastes attempts and puts road ends in
odd places, not that it costs connections.

## Three worlds, both policies

Crossings on, every island, exact terrain.

| world | policy | roads | length | served | networks | attempts | failed | crossings |
|---|---|---|---|---|---|---|---|---|
| issue #7 seed | shipped | 88 | 58.5 km | 123 | 54 | 158 | 70 | 15 |
| issue #7 seed | all changes | 96 | 34.6 km | 138 | 65 | 119 | 23 | 4 |
| world B | shipped | 93 | 71.0 km | 134 | 62 | 163 | 70 | 31 |
| world B | all changes | 94 | 41.7 km | 140 | 61 | 111 | 17 | 16 |
| world C | shipped | 96 | 55.5 km | 141 | 72 | 171 | 75 | 16 |
| world C | all changes | 94 | 26.5 km | 144 | 71 | 137 | 43 | 11 |

The same shape on all three: slightly more places served, far fewer wasted
attempts, and roughly half the road length. The two policies do not differ
mainly in how much they connect but in what they build — long cross-country
roads against short local links — and that is a judgement about the game, not
a number. It is the first thing to put in front of a player.

## What the quota costs

The quota is the one thing that decides most of the outcome, so it was worth
asking what happens without it. Places per island varied, everything else
fixed, shipped plan, crossings on, issue #7 seed:

| places per island | roads | length | served | networks | attempts | failed | metres per place served |
|---|---|---|---|---|---|---|---|
| `2 + area/2 km²` (today) | 88 | 58.5 km | 123 | 54 | 158 | 44 % | 476 |
| 4 | 117 | 63.4 km | 155 | 67 | 190 | 38 % | 409 |
| 8 | 258 | 99.4 km | 319 | 127 | 356 | 28 % | 312 |
| 16 | 466 | 154.1 km | 557 | 200 | 620 | 25 % | 277 |
| 32 | 838 | 224.0 km | 959 | 370 | 1 052 | 20 % | 234 |
| 64 | 1 266 | 301.3 km | 1 434 | 555 | 1 563 | 19 % | 210 |
| every eligible place | 2 080 | 410.7 km | 2 322 | 825 | 2 470 | 16 % | 177 |

Two things stand out.

The quota is not protecting generation from failure - it is the point on the
curve where failure is *most* likely. Nearly half of all attempts fail under
today's quota and one in six with every place selected, because the places
added later are close to places already connected, and a short road over
known-good ground is the easiest kind to build.

The same holds for the road itself: today's quota spends 476 metres of road
per place it reaches, and every-place spends 177. The current setting is the
least efficient point on the curve by that measure.

What it costs is generation time and a very different world: 411 km of road
instead of 58, and about twenty-five seconds of generation offline instead of
three. Whether a world webbed with roads is the game anyone wants is exactly
the sort of question this study cannot answer.

### A scaling limit in PR #16

The same experiment run with PR #16's connection plan does not scale:

| plan, every eligible place | roads | served | attempts | failed |
|---|---|---|---|---|
| chain/MST by parity | 2 080 | 2 322 | 2 470 | 390 |
| tree grown outward with retries | 271 | 296 | 1 008 | 737 |
| MST on routed cost | 2 112 | 2 234 | 2 240 | 128 |

PR #16 stops an island after 24 failed edge attempts. With a dozen places
that cap is generous; with four hundred it stops the island long before the
places are connected, and the island keeps only a fraction of its network.
The cap is a fixed number where the thing it limits scales with the island.

(The routed MST run took 199 seconds against 25, because it prices its edges
first. Its candidate set is each place's eight nearest neighbours - all pairs
would be four hundred squared searches on the biggest island.)

## Do roads cluster at the edges?

The issue says roads cluster "at the edges". That can mean two things - the
shoreline of an island, or the outer parts of the world - so both were
measured, for every one of the 57 862 centreline points, against the land
itself. Roads near shores prove nothing on their own: most land in these
worlds is near a shore, so the question is whether roads are nearer than the
ground they are drawn on.

| | road points | the land itself |
|---|---|---|
| distance to open water, median | 97 m | 74 m |
| distance from the world's centre, median | 5 146 m | 7 037 m |

**Neither reading holds.** Roads sit further from open water than the land
does, and nearer the world's centre than the land is. The same holds inside
every biome separately, and connected places are further inland than
unconnected ones (median 111 m against 68 m).

A first version of this measurement said the opposite, and the reason is worth
recording. It called everything below the road floor "water", which puts all
of a swamp in the sea - swamp sits below that line by nature. Every swamp road
then came out eight metres from a shore, and the study was one step from
reporting that roads hug coastlines. Water here is open water: below sea level
and not swamp, or swamp too deep for a road to wade. A swamp a player can walk
is land.

### What roads really favour

| biome | share of road | share of land | ratio |
|---|---|---|---|
| Swamp | 35.1 % | 7.3 % | **4.8x** |
| BlackForest | 24.1 % | 13.9 % | 1.7x |
| Meadows | 6.9 % | 5.5 % | 1.3x |
| Mountain | 8.5 % | 11.4 % | 0.7x |
| Plains | 10.7 % | 16.0 % | 0.7x |
| Mistlands | 13.4 % | 29.2 % | 0.5x |
| DeepNorth | 1.2 % | 11.0 % | 0.1x |

A third of the network is in swamp, which is a fourteenth of the land. The
pathfinder is doing what its cost model tells it: swamp is flat, and the mod
wades it for a modest penalty, while the Mistlands and the mountains are steep
and dear. It is also what makes roads look like they run through water on a
map, and it is a gameplay question rather than a routing one - a third of the
network runs through the biome players like least to travel.

Connected places by biome tell the same story from the other side: 18.8 % of
eligible swamp places get a road, against 2.4 % in the Mistlands and 1.7 % in
the plains.

## Connection plans on identical inputs

The same islands, the same selected places, the same anchor, the same routing
rules and the same per-attempt budget, crossings on: only the plan differs.
"Served" counts places a road end reaches, "networks" how many separate pieces
the roads form, and "largest" how many roads are in the biggest one.

| world | plan | roads | length | served | networks | largest | planning probes |
|---|---|---|---|---|---|---|---|
| issue #7 seed | chain/MST by parity | 88 | 58.5 km | 123 | 54 | 5 | 0 |
| | tree with retries | 90 | 61.9 km | 126 | 56 | 5 | 0 |
| | MST on routed cost | 90 | 57.6 km | 126 | 56 | 5 | 261 |
| | trunk and spurs | 85 | 62.1 km | 116 | 47 | 5 | 261 |
| | hub and spoke | 88 | 58.2 km | 123 | 56 | 4 | 261 |
| world B | chain/MST by parity | 93 | 71.0 km | 134 | 62 | 6 | 0 |
| | tree with retries | 93 | 70.0 km | 136 | 64 | 5 | 0 |
| | MST on routed cost | 93 | 64.9 km | 136 | 65 | 6 | 283 |
| | trunk and spurs | 86 | 71.1 km | 129 | 50 | 6 | 283 |
| | hub and spoke | 90 | 64.0 km | 134 | 57 | 5 | 283 |
| world C | chain/MST by parity | 96 | 55.5 km | 141 | 72 | 4 | 0 |
| | tree with retries | 95 | 56.6 km | 139 | 69 | 4 | 0 |
| | MST on routed cost | 94 | 55.3 km | 136 | 70 | 4 | 299 |
| | trunk and spurs | 81 | 56.4 km | 118 | 53 | 4 | 299 |
| | hub and spoke | 94 | 63.2 km | 138 | 62 | 4 | 299 |

The planning probes column is there so the comparison stays honest. The three
plans that price a connection before building it have no failed builds, but
that is not a saving: they run 261 to 299 searches to find out, on top of the
builds. A plan that probes every pair is not cheaper than one that tries and
fails, it just fails earlier and more quietly.

What the numbers say:

- **MST on routed cost** reaches as much as any other plan for the least
  road - 58 km against 71 on world B - because it plans on what the
  pathfinder charges rather than on straight-line distance, and never
  commits to an edge it has not already priced.
- **Trunk and spurs** produces the fewest separate networks on every world,
  which is the "one long road with junctions" shape the issue asks for, but
  it reaches about a tenth fewer places. It trades reach for cohesion.
- **Hub and spoke** is close to the shipped plan everywhere.

And the finding that dwarfs all of them: the largest connected network is
**four to six roads** whatever the plan. These worlds are archipelagos, roads
do not cross open sea, and no connection plan can change that. The differences
between plans are small beside the shape of the world they are drawn on.

## Not reproduced

The issue reports 80 islands on this seed; detection finds 67 with the same
rule. Island detection does not depend on any setting, so the difference is in
the world itself — a worldgen change between the report and now is the likely
cause, and it is stated here rather than papered over.

## Still owed

- In-game confirmation of anything about fords, which the offline terrain
  cannot see.
- The per-island table, and the study document itself.
- In-game runs of the shortlisted plans, once there is a shortlist.

# Road networks: a routing study

**Preliminary; routing study; gameplay validation pending.** Two strategy
implementations do not yet match their descriptions here (see "Where this is
still wrong"), so the strategy ranking should not be read as settled. Everything below is measured
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
detection reads, and the 8 m cells the pathfinder walks. Heights, biomes and
river weights at those positions are therefore exact rather than interpolated.

Move costs are **not** exact, and an earlier draft of this said they were. A
move's cost includes a terrain-variance term sampled on a ring 16 m out at
eight angles, four of which fall between the dumped positions and are
interpolated. So the ordinary cost of a step is close but not identical to the
game's, which is one reason the calibration below matters.

What it does not reproduce: the pathfinder's terrain-variance ring and every
crossing-depth judgement sample between those positions, so they are
interpolated. That matters most for fords — see "What this cannot tell you".

Checked against the game on the issue's own seed, same settings both sides:

| | in game | offline |
|---|---|---|
| roads | 89 | 88 |
| total length, summed over routes | 59.9 km | 58.5 km |
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

## And a second one

Running the same world twice found another. The second run reported 78 roads
and 40 787 metres, but the spatial grid held 115 202 points for a network of
40 970, and the 24 river crossings it reported were the previous run's -
nineteen of them bridges, in a run with bridges switched off.

The reset before a forced regeneration asks whether roads were *generated*
this session. A world loaded from a save has roads without having generated
them, so the reset was skipped and the new network was laid on top of the old
one. Anyone regenerating roads in an existing world gets both networks in the
terrain, and the old network's bridges keep their sites.

Fixed the same way: both guards now ask whether the world has a network at
all. This one is in the base code rather than in the crossings work.

## What the issue is actually about

Every place in the world, and what became of it, on the shipped settings with
crossings on and every island selected:

| | issue seed | world B | world C |
|---|---|---|---|
| places in the world | 11 477 | 11 407 | 11 413 |
| on a detected island | 10 051 | 10 027 | 10 091 |
| eligible for a road | 2 470 | 2 416 | 2 450 |
| selected by the island's quota | 158 | 163 | 171 |
| connected — its planned road was built | 128 | 131 | 141 |

("Connected" here counts places, matched by the place's own identity. The
tables of levers below also report *served*, which counts places with a road
end within reach, and *planned and built*, which counts connections rather
than places. On the issue's seed those are 128, 123 and 129 — three different
questions about the same run.)

Of the places that are eligible, by what became of them:

| | issue seed | world B | world C |
|---|---|---|---|
| lost the island's quota | 2 312 | 2 253 | 2 279 |
| connected | 128 | 131 | 141 |
| attempted, search frontier exhausted | 29 | 32 | 29 |
| attempted, iteration budget spent | 1 | 0 | 1 |

![the funnel from placed to connected](https://raw.githubusercontent.com/tvongaza/ProceduralRoads/docs/validation-gap/validation-results/screenshots/study-2026-09-09/chart-funnel.png)

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

### And the quota is spent on dungeons

Of the 2 470 eligible places, which ones win the 158 slots:

| priority | offered | selected | share |
|---|---|---|---|
| 100 (bosses) | 19 | 19 | 100 % |
| 80 (crypts, sunken crypts, mountain caves, anything registered through the API) | 517 | 106 | 20.5 % |
| 75 (Mistlands town entrances, older crypts) | 421 | 24 | 5.7 % |
| 70 and below | 1 513 | 9 | 0.6 % |

The network connects bosses and dungeons and almost nothing else. Villages,
farms, towers, ruins — the great majority of what is on a map — share nine
road ends across a whole world, and no setting changes that, because they are
never attempted.

It is also why registering a location through the API matters more than it
looks: a registered location gets priority 80, straight into the band that
wins slots.

The same run by what the place actually is, rather than by its number:

| what it is | in the world | eligible | selected | a road reached it |
|---|---|---|---|---|
| boss altars | 19 | 19 | 19 | 14 |
| dungeons (crypts, sunken crypts, caves) | 920 | 913 | 109 | 94 |
| Mistlands structures | 890 | 828 | 28 | 20 |
| settlements (villages, farms, swamp huts) | 50 | 49 | **0** | 0 |
| ruins, towers and stone circles | 716 | 661 | 2 | 0 |

Not one of the world's fifty settlements is ever selected, and of 661 eligible
ruins and towers two are selected and neither is reached. A player walking
this world finds roads between crypts.

**Every place the generator required and did not get.** Bosses are selected on
every island they sit on, so a boss without a road is a road the generator
tried to build and failed:

| boss | island | nearest road end |
|---|---|---|
| GoblinKing (3904, 3904) | 45 | 932 m |
| GoblinKing (3520, −640) | 29 | 626 m |
| Dragonqueen (−1983, −4420) | 22 | 1 692 m |
| Dragonqueen (3069, −4396) | 29 | 1 341 m |
| Dragonqueen (6727, 1534) | 60 | 630 m |

Five of the world's nineteen boss altars, all "no reachable path" — none of
them ran out of budget. Fifteen selected dungeons end the same way; the full
list, with the distance to the nearest road end, is in
`Issue7-outcomes.csv` beside this document.

## The iteration plateau

The issue reports diminishing returns around 30 000 iterations. That
reproduces, and the reason is visible once failures are split by the
pathfinder's own two reasons:

| iterations | roads | budget spent | frontier exhausted |
|---|---|---|---|
| 5 000 | 62 | 48 | 48 |
| 10 000 | 74 | 33 | 51 |
| 20 000 | 78 | 19 | 61 |
| 30 000 | 84 | 8 | 66 |
| 60 000 | 87 | 4 | 67 |
| 120 000 | 88 | 1 | 69 |

![roads and the two failure kinds against the iteration budget](https://raw.githubusercontent.com/tvongaza/ProceduralRoads/docs/validation-gap/validation-results/screenshots/study-2026-09-09/chart-plateau.png)

As the budget grows, attempts that used to stop at the cap run to completion
and instead exhaust their frontier: every cell they can reach, settled,
without arriving. Past about
30 000 there is almost nothing left for a larger budget to rescue. The setting
has ranged 1 000 to 100 000 since it was added, so a reported 100 000 was
never clamped.

## Do roads cluster at the edges?

That can mean the shoreline of an island or the outer parts of the world, so
both were measured for all 57 862 centreline points — against the land itself,
because most land in these worlds is near a shore and roads near shores prove
nothing on their own.

| | road points | the land itself |
|---|---|---|
| distance to open water, median | 97 m | 74 m |
| distance from the world's centre, median | 5 146 m | 7 037 m |

**Neither holds.** Roads sit *further* from open water than the land does and
*nearer* the world's centre, and connected places are further inland than
unconnected ones. The same is true inside every biome taken separately.

The distributions say it more plainly than the medians can: the land spikes
hard against the shoreline where the roads do not, sitting instead in the band
fifty to two hundred and fifty metres inland.

![distance to open water, roads against land](https://raw.githubusercontent.com/tvongaza/ProceduralRoads/docs/validation-gap/validation-results/screenshots/study-2026-09-09/chart-shore.png)

![distance from the world centre, roads against land](https://raw.githubusercontent.com/tvongaza/ProceduralRoads/docs/validation-gap/validation-results/screenshots/study-2026-09-09/chart-centre.png)

These are whole-world distributions. They rule out a bias toward either edge
at the scale of a world; they cannot rule out a single island where roads do
hug its coast, and the per-place table is published so that can be checked.

What roads do favour is swamp:

| biome | share of road | share of land |
|---|---|---|
| Swamp | 35.1 % | 7.3 % |
| BlackForest | 24.1 % | 13.9 % |
| Mistlands | 13.4 % | 29.2 % |
| DeepNorth | 1.2 % | 11.0 % |

A third of the network is in swamp, which is a fourteenth of the land, and
18.8 % of eligible swamp places get a road against 2.4 % in the Mistlands. The
pathfinder is following its cost model — swamp is flat and waded for a modest
penalty, the Mistlands and the mountains are steep and dear — and the result
is a network that spends a third of its length in swamp. Whether that is good
or bad is a gameplay question this study cannot answer — it is raised here
because a third of a network in one biome is a design choice worth making
deliberately rather than inheriting from a cost constant.

It is also why roads look like they run into the sea on a map: a swamp sits at
and below the waterline, and a road wading one is doing what it was told to.

## What a player actually gets

The biggest island on the issue's seed: 26.7 km², 1 936 places on it, 426 of
them eligible for roads. It gets 12 selected, 6 roads, 5.2 km of road, serving
8 places — in **three separate networks**.

Across that world: 22 of 67 islands get no road, 22 get exactly one, and the
most any island gets is six. Three of the 45 islands that do get roads end up
with the island's roads in more than one disconnected piece, and each of those
three had failed attempts — the chain carries on from the next place after a
failure, and what it left behind becomes its own network. The largest connected run of road anywhere in the
world is 4 to 6 roads, and that holds on all three worlds and under every plan
tried below.

![the whole world under the study baseline](https://raw.githubusercontent.com/tvongaza/ProceduralRoads/docs/validation-gap/validation-results/screenshots/study-2026-09-09/issue7-shipped.png)

Six islands under the study baseline, at identical bounds and scale — 6 km
across each, so they can be read against one another:

![six islands at the same scale](https://raw.githubusercontent.com/tvongaza/ProceduralRoads/docs/validation-gap/validation-results/screenshots/study-2026-09-09/six-islands.png)

The largest island in the world is the top left panel: 26.7 km², and its roads
are six of them in three separate pieces, none of which meet. Read across the
six panels and the island's size barely shows in how much road it gets — the
quota decides that, not the land. Red rings are places that were selected and
never reached.

That is the ceiling worth knowing about before tuning anything. These worlds
are archipelagos, and a road crosses water only where a bridge can span it —
128 m at most, where most of the channels here are wider. Bridges do join
neighbouring islands in places, but not enough to make one network out of an
archipelago, and no connection plan changes that.

## The levers, and what each is worth

### First, what the baseline is

Everything in this document that says "the baseline" means **crossings on and
every island selected**, on the issue's seed. That is not what the mod ships
with, and an earlier draft of this document called it "today", which was
wrong. The shipped defaults are 50 % of islands, fords off, bridges off and a
10 000 iteration budget:

| | roads | road summed over routes | distinct road | served | networks |
|---|---|---|---|---|---|
| shipped defaults | 30 | 12.4 km | 12.0 km | 46 | 21 |
| the baseline used below | 88 | 58.5 km | 54.0 km | 123 | 49 |

Two lengths, because they answer different questions and an earlier draft
reported only the first. **Summed over routes** adds up every road as built,
so a stretch two roads share is counted twice; **distinct road** counts the
ground once. Where only one figure appears below it is the summed one, and
the tables that turn on sharing give both.

Three times the network, before any change proposed here. The baseline was
chosen so that a lever's effect is not hidden by another setting suppressing
it, but it means every number below is measured on a more generous
configuration than a player gets out of the box.

Everything below is one change at a time from that baseline. "Served" counts
places a road end reaches.

### How many places each island may have

| places per island | roads | road summed over routes | served | attempts failing | metres per place served |
|---|---|---|---|---|---|
| `2 + area/2 km²` (the baseline) | 88 | 58.5 km | 123 | 44 % | 476 |
| 8 | 258 | 99.4 km | 319 | 28 % | 312 |
| 16 | 466 | 154.1 km | 557 | 25 % | 277 |
| 32 | 838 | 224.0 km | 959 | 20 % | 234 |
| every eligible place | 2 080 | 410.7 km | 2 322 | 16 % | 177 |

![places served against distinct road built](https://raw.githubusercontent.com/tvongaza/ProceduralRoads/docs/validation-gap/validation-results/screenshots/study-2026-09-09/chart-quota.png)

Two things run against intuition here. The quota is not protecting generation
from failure — it sits where failure is *likeliest*, because the places added
later are near ones already connected and short roads over known-good ground
are the easiest to build. And it is the least efficient point on the curve for
road spent per place reached: 476 metres today against 177 with everything
selected.

What it buys is a very different world — 411 km of road instead of 58 — and
about twenty-five seconds of generation offline instead of three. Whether a
land webbed with roads is the game anyone wants is not a question these
numbers can answer.

### Where those places sit, at the same count

| arrangement | roads | road summed over routes | served | metres per place |
|---|---|---|---|---|
| priority, truncated (the baseline) | 88 | 58.5 km | 123 | 476 |
| priority, then nearest (PR #16) | 101 | 43.3 km | **142** | **305** |
| priority, then farthest | 77 | 84.1 km | 115 | 731 |
| a fixed draw, ignoring priority | 72 | 58.2 km | 109 | 534 |

Arrangement alone moves coverage from 109 to 142 places — a wider spread than
any routing change below produces. Choosing destinations near one another is
worth more than choosing cleverly between them. Deliberately spreading them,
which sounds like what a road network wants, is the worst of the four.

### Fords and bridges

| offline | roads | road summed over routes | served |
|---|---|---|---|
| neither | 47 | 20.9 km | 72 |
| fords only | 78 | 42.8 km | 111 |
| bridges only | 57 | 30.6 km | 86 |
| both | 88 | 58.5 km | 123 |

Crossings are worth more than any other single lever measured: 72 places
served becomes 123.

The fords row needed the game to read properly, because the flag does two
things — it lets a road jump a fordable river, and it lets a road wade a
swamp — and only the second can be measured offline. Three runs in game on one
world state, so the rows are comparable:

| in game | roads | road summed over routes | crossings |
|---|---|---|---|
| neither | 49 | 21.7 km | 0 |
| fords only | 90 | 41.5 km | **3, all fords** |
| both | 98 | 56.8 km | 15 (13 bridges, 2 fords) |

Three river fords in a whole world. Turning fords on adds 41 roads and almost
none of it is river crossing: it is the swamp wading that comes with the same
flag. Bridges then add eight more roads with thirteen bridges, so a bridge
earns its place at a far higher rate than a ford does. Worth knowing before
more effort goes into ford geometry.

### Can a road follow another one?

Not today. The only place an existing road enters the cost of a move is a
river crossing, which costs half when both banks already carry road. Ordinary
road carries no discount, so two roads to nearby places run side by side and a
junction only happens where one road ends near another.

Added as a lever and swept, it does nothing:

| a step on existing road costs | roads | distinct road | served | joined groups |
|---|---|---|---|---|
| full price (the baseline) | 88 | 54.0 km | 123 | 49 |
| half | 88 | 54.0 km | 123 | 49 |
| a quarter | 88 | 54.0 km | 122 | 49 |
| a twentieth | 88 | 53.4 km | 122 | 49 |

The first version of that sweep was wrong in two ways, both found in review:
the discount was applied after the early returns for slope, variance, water
and river, so it never touched the moves whose cost shapes a route; and the
search's heuristic is straight-line metres, which stops being admissible once
moves are cheaper than their length, so the search could prune the very routes
the discount was meant to open. Both are fixed — every move class is
discounted, and the heuristic is scaled by the discount — and the table above
is from the repaired experiment.

It still shows almost nothing, and now the reason is clear: **at this baseline
there is nothing to share.** The quota gives an island two or three roads, and
two roads that start from the same anchor and end in different directions have
no common stretch to reuse.

Where there is something to share, it works. With every eligible place
selected — 2 080 roads instead of 88 — and the discount requiring a step
within 4 m of a road rather than merely near one:

| every place selected | roads | distinct road | summed over routes | served |
|---|---|---|---|---|
| no discount | 2 080 | 383.4 km | 410.7 km | 2 322 |
| a quarter price within 4 m | 2 084 | 371.5 km | 458.4 km | 2 315 |
| a twentieth within 4 m | 2 086 | 373.2 km | 489.2 km | 2 315 |

Distinct road falls by 12 km while the summed route length rises by 48: the
routes really are running along each other. Coverage does not move, and
neither does the largest joined group.

So a sharing discount is not a way to reach more places. It is a way to build
the same network out of less road, and only where the network is dense enough
to have roads worth following. Reaching *junctions* — roads meeting rather
than running side by side — took a plan that attaches to a road, which is
what trunk and spurs now does.

### How many islands get roads

| islands selected | roads | served | networks |
|---|---|---|---|
| 10 % | 24 | 34 | 12 |
| 25 % | 49 | 68 | 24 |
| 50 % (default) | 73 | 101 | 41 |
| 100 % | 88 | 123 | 49 |

Near enough linear to three quarters and then flat, because the largest
islands are taken first. The default gives up about a fifth of the network the
same world would support.

### What the network is for

Three presets, bosses required in each, the rest drawn per place from the
world seed:

| preset | selected | roads | road summed over routes | served | connect rate |
|---|---|---|---|---|---|
| the built-in table (the baseline) | 158 | 88 | 58.5 km | 123 | 78 % |
| bosses only | 19 | 9 | 11.4 km | 12 | 63 % |
| bosses and half the dungeons | 139 | 77 | 53.6 km | 108 | 78 % |
| settlements raised to compete | 146 | 67 | 71.5 km | 100 | 68 % |

Bosses alone are barely a network: nine roads in a world, because most bosses
are alone on their island and a road needs two ends. The last row is the
interesting one — aiming at where people live selects about as many places,
connects ten points fewer of them, and spends more road doing it, because
settlements sit in scattered awkward spots. Worth wanting, but not free.

## The same world, six ways

Six configurations on the issue's seed, drawn at identical bounds and scale,
locations left off so the roads carry the picture:

![six configurations on one world](https://raw.githubusercontent.com/tvongaza/ProceduralRoads/docs/validation-gap/validation-results/screenshots/study-2026-09-09/world-sheet.png)

| panel | roads | distinct road | served | networks |
|---|---|---|---|---|
| the shipped defaults | 30 | 12.0 km | 46 | 21 |
| the study baseline (crossings on, every island) | 88 | 54.0 km | 123 | 49 |
| all of PR #16 | 96 | 33.8 km | 138 | 47 |
| quota by priority then nearest | 101 | 42.6 km | 142 | 47 |
| the anchor walked onto land | 104 | 64.6 km | 124 | 51 |
| eight places per island | 258 | 92.2 km | 319 | 67 |

The shipped panel is the issue: most of the world has no road at all. Every
other panel fills more of the world in, and none of them joins it up. Between
30 roads and 258, the number of separate networks goes from 21 to 67 and never
falls below 47 — more road on this world means more islands with roads on
them, not bigger networks. Only PR #16 and the quota change reduce the network
count at all, and then by two.

## PR #16, one change at a time

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

## The coast-cell anchor

Off the starter island, each island's network is rooted at the island cell
nearest its bounding box, with radius 0 — a coast cell, not a place. Islands
are found on a 128 m grid and a cell counts as land on its base height, so a
cell that straddles the shore is land while the point the anchor actually
uses, its centre, can be a long way out to sea. `GetEdgePoint` returns that
centre unchanged.

On the issue's seed, 32 of the 70 failed attempts settle a single cell and
stop. Every one of them starts in a cell below the waterline. From a
submerged cell there is nowhere to go: of the sixteen directions the search
may take, the eight straight ones are blocked water, and the eight knight
moves are refused outright by the crossing scan, which walks whole cells and
will not start a crossing from a jump. Across those 32 attempts the refusals
are exactly 256 knight moves, 190 no-bank-found and 54 no-river — sixteen
refusals apiece, no exceptions.

Start cells and what became of them, over all 158 attempts:

| the attempt's start cell | attempts | connected |
|---|---|---|
| above the waterline | 102 | 75 (74%) |
| below it | 56 | 13 (23%) |

Walking that anchor inland to the first point above the waterline is a
three-line change, and the study runs it as its own anchor mode:

| anchor | roads | distinct road | planned and built | served | joined groups | attempts | failed | of those stillborn |
|---|---|---|---|---|---|---|---|---|
| the island's edge cell (ships today) | 88 | 54.0 km | 129 | 123 | 49 | 158 | 70 | 32 |
| the same cell, walked onto land | 104 | 64.6 km | 130 | 124 | 51 | 158 | 54 | 5 |
| the island's highest-priority place (#16) | 82 | 54.9 km | 131 | 124 | 49 | 109 | 27 | 0 |

The failure class all but disappears — 32 stillborn attempts become 5 — and
19% more road gets built. What it does **not** buy is coverage: 123 places
served becomes 124. The shipped chain carries on from the place it was
heading for whether or not the leg to it was built, so a stillborn first leg
costs the road, not the destination.

All three anchors land within one place of each other on coverage. The anchor
rule is not a coverage lever. It decides how much road exists and how many
searches are spent finding out, and on the shipped rule a quarter of every
search in the run is spent starting in the sea.

## Connection plans

Same islands, same selected places, same anchor, same budget; only the plan
differs.

| plan | roads | distinct road | planned and built | served | joined groups | planning searches |
|---|---|---|---|---|---|---|
| chain/MST by island parity (the baseline) | 88 | 54.0 km | 129 | 123 | 49 | 0 |
| tree grown outward with retries (#16) | 90 | 57.2 km | 131 | 126 | 50 | 0 |
| MST on the search's own cost | 90 | 56.6 km | 131 | 126 | 50 | 261 |
| trunk with spurs onto the road | 82 | 52.9 km | 118 | 118 | 46 | 261 |
| hub and spoke | 89 | 54.7 km | 131 | 126 | 51 | 261 |

Two of these were corrected after review and rerun: the MST now compares the
cost the search accumulated rather than the length of the path it returned,
and a spur now starts at the nearest point on a road already built rather than
at another place. The second change is visible in the roads themselves — 83
spurs attempted where there were none before, 30 built, with a median length
of 161 m against 459 m for a place-to-place road.

- **MST on the search's own cost** plans on what the pathfinder charges rather
  than on straight-line distance, so a strait or a mountain counts for what it
  really costs. On this world it lands where the tree does; the change from
  routed distance to routed cost moved it very little, which is itself worth
  knowing.
- **Trunk with spurs onto the road** lays one road along the island's long
  axis and joins everything else to the nearest point on a road, making a
  junction there. It builds the fewest and shortest roads and reaches about a
  tenth fewer places: short spurs are cheap, but a spur that starts on a road
  fails more often than one starting at a place, because the road is not
  always on the useful side of the terrain.
- **Hub and spoke** lands close to what ships today.

The last column matters: the three plans that price a connection before
building it have no failed builds, but they run 261 to 299 searches to find
out. That is not a saving, it is the same work moved earlier.

![one island under four connection plans](https://raw.githubusercontent.com/tvongaza/ProceduralRoads/docs/validation-gap/validation-results/screenshots/study-2026-09-09/island-sheet.png)

The same island under four plans, at identical bounds and scale. On the same island, the shipped plan
puts short branches around the start; trunk and spurs lays one road down the
length of the chain. Which of those is a better road network is a judgement
about playing the game, not a number, and it is the first thing worth trying
in game.

## Reading a failure on the map

Places are drawn by what became of them and failed attempts by where the
search actually stopped, so a missing road can be looked at rather than
inferred: a filled dot is a place a road reached, a red ring one that was
selected and never reached, a dashed red line an attempt drawn to the nearest
the search came, and a dashed box the ground that search settled before giving
up.

![an island with outcomes and failed attempts drawn](https://raw.githubusercontent.com/tvongaza/ProceduralRoads/docs/validation-gap/validation-results/screenshots/study-2026-09-09/island-diagnostic.png)

### Three failures, in full

One attempt at a time, at the scale it happened. In each: a white ring is
where the road was to start, a filled dot the nearest the search ever came to
its destination, a cross the destination itself, and the dashed box the ground
the search settled before it gave up.

**The road that never started.** 32 of the 70 failures on this seed look like
this one, and it is the cheapest to fix.

![a coast anchor sitting in open water](https://raw.githubusercontent.com/tvongaza/ProceduralRoads/docs/validation-gap/validation-results/screenshots/study-2026-09-09/failure-anchor.png)

The island's anchor sits about 150 m off its own coast, in open water, because
the island grid is 128 m and the cell it came from straddles the shore. One
cell settled, sixteen moves refused, no road. The destination is a crypt 222 m
inland that no road was ever laid toward.

**The search that filled its island and found no way off it.** 97 290 cells
settled, and the destination is 3.4 km away on the far side of an archipelago.

![a search that settled a whole archipelago](https://raw.githubusercontent.com/tvongaza/ProceduralRoads/docs/validation-gap/validation-results/screenshots/study-2026-09-09/failure-frontier.png)

The box is the ground the search covered before its open set ran empty. It
walked the islands it could reach, took the crossings it could take, and
stopped 1 144 m short. Nothing here is a budget problem: the search ran out of
places to go, not out of iterations. Only a longer crossing would join these.

**The search that ran out of budget.** 100 000 cells — the whole allowance —
for a destination 1.9 km away.

![a search that spent its whole budget](https://raw.githubusercontent.com/tvongaza/ProceduralRoads/docs/validation-gap/validation-results/screenshots/study-2026-09-09/failure-budget.png)

This is the one attempt in 158 that hit the iteration cap. It filled its
island, reached the south shore, and stopped 1 207 m short of a place on the
next island down. The dashed box runs past the top of the frame and past the
edge of the world: once the land was exhausted the search spent what was left
crawling north over open ocean. The offline harness answers points past the
world's rim with the rim's own values, so the box's exact northern extent is
an artefact of the dump — that the search goes out there at all is not.

## The runs behind these numbers

Every table above comes from a run whose manifest carries a run id, the study
commit, the settings and a content hash of every input. The manifests, the
per-place outcomes, the per-island table, the selection and attempt tables and
the crossings are published beside this document:

[validation-results/study-2026-09-09](https://github.com/tvongaza/ProceduralRoads/tree/docs/validation-gap/validation-results/study-2026-09-09)

Route geometry is not published — several megabytes a run — but it regenerates
from the manifest, which names the code and the inputs exactly.

## Where this is still wrong

Raised in review. Fixed since, and named here so the record is plain:

- The MST compared route length, not the cost the search accumulated. It now
  compares the cost, and the plan rows are rerun.
- Spurs joined places rather than roads. They now start at the nearest point
  on a road already built, and the rows are rerun.
- The road-sharing experiment discounted only the moves that survived the
  early returns, with a heuristic that stopped being admissible once moves
  were cheap. Both fixed, and the question re-answered.
- "Connected" carried several meanings at once. A run now reports them apart:
  places whose planned road was built, matched by the place's own identity;
  places with a road end within reach; and roads joined to each other.
- Length is now reported both as distinct road on the ground and as the sum
  over routes, because summing punishes a strategy for sharing road.

Still true of the numbers here, and not fixed:

- **Roads are joined geometrically**, by an endpoint within 24 m of another
  road, with no regard for elevation or what lies between. Two roads on
  opposite banks of a narrow river count as one network. It is a drawing-level
  measure, not a walkable one, and nothing here has been walked.
- **The strategies differ by a few places served**, which is inside the
  distance between this model and the game: the calibration matched 92 % of
  roads but not all of them. Treat the plan table as a description of
  behaviour, not a ranking.
- **One world carries most of the detail.** The three-seed table holds for the
  broad results; the finer ones — clustering, the priority table, the sharing
  sweep — were measured on the issue seed alone.

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

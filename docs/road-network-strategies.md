# Road networks: a routing study

**Preliminary; routing study; gameplay validation pending.** Everything below
is measured on generated networks — road counts, lengths, what connects to
what, and why a connection failed. None of it has been played. Where a number
would change a design decision, it needs a session in game first, and the
places that most need one are named in section 6. The strategy ranking in
particular should not be read as settled: the plans differ by a few places
served, which is inside the distance between this offline model and the game.
What was raised in review and has since been corrected is listed under "Where
this is still wrong", so the record of what changed is in the document.

This came out of issue #7 ("Roads seem to be limited to 2-3 per island").
It reproduces what the issue describes, finds a different cause than the one
being tuned there, and lays out a shortlist of what to build next and the
evidence that would settle the choice. Section 1 carries both.

**How to read the authorship of this document.** The measurements, the runs and
the code are the study's. The prose is written by an AI assistant working from
those runs, for a human to check and edit. Headings marked with a dagger (†)
are new or substantially rewritten in the 9 September restructure; everything
else is earlier text, moved but not reworded. Any claim that would need a
measurement nobody has taken is written as a block quote beginning
**MEASUREMENT NEEDED**, so the gaps can be found by searching for that phrase
rather than inferred from silence.

## 1. Executive summary and recommended next experiments †

**Preliminary. This section is a provisional recommendation, not a decision.**

*Why are networks sparse?* Not the search. On the issue's seed 2 470 places are
eligible for a road and the per-island quota — `2 + area / 2 km²` — selects 158
of them. **About 93 % of eligible places never get an attempt at all**;
pathfinding failure accounts for roughly one per cent, and the iteration budget
the issue discusses for one place in the world. Raising the search budget
cannot recover destinations that were never selected.

*Which controls matter most?* Three, in this order. **The quota**: lifting it
to every eligible place takes the network from 88 roads and 123 places served
to 2 080 roads and 2 322 served. **The selection policy** at a fixed count:
choosing the nearest places after priority serves 142 where a fixed draw serves
109 — a wider spread than any planner change measured here. **Water
traversal**: fords and bridges together take 72 places served to 123.

*Which planners deserve further work?* **Routed-cost MST**, as a backbone
candidate, because it prices every edge by running the pathfinder on it before
committing. **POI-to-network search**, as the junction candidate, because it
produces 28 tee junctions against the shipped plan's 1 and 0.1 km of road
running alongside other road against 4.8 km.

*What remains unproven?* Walkable connectivity — roads are joined here
geometrically, by an endpoint within 24 m, with no regard for elevation or what
lies between. Gameplay quality — nothing in this document has been played.
And whether the finer strategy results generalise: the broad results hold on
three worlds, the plan comparison was measured on the issue seed alone.

### The shortlist †

| approach | recommendation | reason |
|---|---|---|
| routed-cost MST | advance as a backbone candidate | plans around actual routing costs; modest coverage gain (126 served against 123); the extra planning work must stay visible — 261 planning searches on this world |
| POI-to-network search | advance as the junction candidate | same reported served count (123), 28 tees against 1, and 0.1 km alongside road against 4.8 km |
| trunk plus spurs | retain as a contrasting gameplay candidate | strong main-road shape (29 tees, the most of any plan), but lower coverage (118) and 5.1 km of parallel running |
| nearest-point growth / hub-and-spoke | lower priority for now | current results show less compelling advantages |
| road-sharing discount | conditional optimisation | helps dense networks: 12 km of distinct road saved with every place selected, nothing at all at the study baseline |
| larger iteration budget / simple fallback | lower priority | limited coverage benefit under the tested conditions; the fallbacks move served count by zero |

**A routed-cost backbone followed by POI-to-network branches is a promising
next hypothesis — not a demonstrated winner.** Nothing here has tested the
combination. It has to be run against both standalone approaches on the same
inputs before it can be preferred to either.

### The evidence needed to choose †

The shortlist above is ordered on geometry. Four measurements would turn it
into a recommendation, and none of them exists yet:

1. **Destination overlap.** Routed MST and POI-to-network report 126 and 123
   places served against the baseline's 123. Equal or near-equal counts do not
   establish that the *same* places were served, or that the same bosses were.
   See the MEASUREMENT NEEDED note in section 4.
2. **Per-island generation time.** The POI-to-network search costs 84 seconds
   offline against 4 — a **21× increase** in offline runtime. Whether that is
   acceptable for the single-island regeneration the tooling already has
   depends on a per-island timing measurement nobody has taken.
3. **The combination.** Routed-cost backbone plus POI-to-network branches, on
   the same seed and the same selected places as both standalone runs.
4. **The in-game checklist in section 6**, on at least one island from each of
   the three contrasting island types named there.

## 2. Experiment setup

### How this was measured

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

### The study baseline

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

Every lever in section 5, and every plan in section 4, is one change at a time
from that baseline. "Served" counts places a road end reaches.

## 3. Why the current network is sparse

### The quota, and what it excludes

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
tables of levers in section 5 also report *served*, which counts places with a road
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

### What a player actually gets

The biggest island on the issue's seed: 26.7 km², 1 936 places on it, 426 of
them eligible for roads. It gets 12 selected, 6 roads, 5.2 km of road, serving
8 places — in **three separate networks**.

Across that world: 22 of 67 islands get no road, 22 get exactly one, and the
most any island gets is six. Three of the 45 islands that do get roads end up
with the island's roads in more than one disconnected piece, and each of those
three had failed attempts — the chain carries on from the next place after a
failure, and what it left behind becomes its own network. The largest connected run of road anywhere in the
world is 4 to 6 roads, and that holds on all three worlds and under every plan
tried in section 4.

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

### The coast-cell anchor

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

### The iteration plateau

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

### Do roads cluster at the edges?

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

## 4. Strategy comparison

### How a connection is planned, and what that costs

Every road runs **from one place to another place**. Nothing in the shipped
code ever starts a road from the network it has already built.

- On odd island ids the plan is a nearest-neighbour chain: the anchor to the
  nearest place, that place to the nearest of the rest, and so on.
- On even ids it is a minimum spanning tree over the anchor and the selected
  places, and the tree is built on **straight-line distance** — `Vector3.
  Distance` between two places — before any of it is routed.
- Either way each edge is handed to the pathfinder as two points, and
  `GenerateRoad` searches from the first place's centre to the second's.

Three consequences worth naming:

1. **A plan cannot see water.** The tree is chosen on straight-line distance,
   so the cheapest-looking neighbour is often the one across a channel. Most
   of the 27 failures in Appendix A are edges no planner with a map would have
   drawn.
2. **Roads do not join except by accident.** Two roads to nearby places both
   leave the same anchor and run alongside each other; a junction exists only
   where one road's endpoint happens to land within 24 m of another, which is
   what the joined-group count measures.
3. **A place that fails is not retried from anywhere else.** There is no
   fallback to the nearest road, or to the nearest connected place.

The one plan tried here that does otherwise is trunk-and-spurs, which attaches
a spur to the nearest point on a road already built — the only code path in
this study that starts a road from the network rather than from a place. It
attempted 83 such spurs and built 30, at a median 161 m against 459 m for a
place-to-place road.

### The plans, side by side

The section above says what the shipped plan cannot do: it draws lines between
places, on straight-line distance, with no second chance and no way to join a
road it has already built. Each of those is a question with an answer, and all
four were tried on the issue's seed against the same baseline.

The two columns that matter for the last three are new here. **Tees** counts
roads whose end lands on another road's *length* — a fork, the shape a walked
path network has. **Alongside** counts metres of road running within 12 m of
another road without joining it, which is the shape nobody wants.

| plan | roads | distinct road | served | tees | ends | alongside | attempts | failed |
|---|---|---|---|---|---|---|---|---|
| chain/MST by parity (the baseline) | 88 | 54.0 km | 123 | **1** | 43 | **4.8 km** | 158 | 70 |
| MST on the search's own cost | 90 | 56.6 km | **126** | 0 | 40 | 2.7 km | 90 | **0** |
| trunk with spurs onto the road | 82 | 52.9 km | 118 | 29 | 12 | 5.1 km | 135 | 53 |
| grow from the network | 82 | 46.6 km | 116 | 15 | 37 | 1.6 km | 103 | 21 |
| **the place reaches for the network** | 86 | 55.7 km | **123** | **28** | 23 | **0.1 km** | 157 | 30 |

### What each plan does

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

### Can a plan see water?

Yes, and it already does. The routed MST prices every candidate edge by
*running the pathfinder on it* before choosing, so an edge across a strait
either costs what the detour really costs or has no cost at all and is never
chosen. The result is the cleanest line in the table: **no failed attempts at
all** — 90 planned, 90 built — three more places served than the baseline, and
the road running alongside other road nearly halved.

It is not free. Pricing edges costs 261 planning searches on this world, so the
total pathfinder work is higher than the baseline's, not lower; what changes is
that the work happens before a road is committed rather than after one fails.
"No failed builds" is a tidier log, not a saving.

Its one real limit is the candidate set: each place offers only its nearest
neighbours by straight-line distance, because pricing every pair is hopeless
past a dozen places. Widening that set is the obvious worry, so it was swept:

| candidates per place | roads | served | failed |
|---|---|---|---|
| 3 | 89 | 125 | 0 |
| 6 (the default) | 90 | 126 | 0 |
| 12 | 90 | 126 | 0 |
| 24 | 90 | 126 | 0 |

Flat from six onward, identically so. The candidate set is not what binds — the
water is, exactly as the failure census says.

### Can roads join by design instead of by accident?

Yes, and this is where the shipped plan is weakest. **On the whole world it
produces one tee junction.** Every other meeting of two roads is two ends at
the same place, because every road it builds runs between two places and two
roads to neighbouring places both leave the same anchor. That is where the
4.8 km of road running alongside other road comes from.

Three plans here make junctions on purpose, and they are not equal:

- **Trunk with spurs** lays one road along the island's routed long axis and
  joins everything else at the nearest point on a road. 29 tees, the most of
  any plan — but it still runs 5.1 km alongside itself, because a spur aimed at
  the straight-line nearest point on the trunk often parallels it to get there.
- **Grow from the network** drops the trunk and simply attaches the nearest
  waiting place to the nearest point on the road so far. It cuts road running
  alongside to 1.6 km, but reaches seven fewer places, because a place with no
  road near enough to join has to fall back to a place-to-place link.
- **The place reaching for the network** — below — gets 28 tees *and* 0.1 km
  alongside, at the baseline's coverage.

### Can a place reach for the network, instead of the network reaching for it?

This is the one that works, and it is a different algorithm rather than a
different plan.

Every plan above has to choose a destination before it can search: another
place, or a point on a road. It chooses on straight-line distance, because that
is the only thing available before a search runs — and straight-line distance
is the thing a road cannot use. Aiming at the nearest point on a road across a
channel fails exactly the way aiming at the nearest place across a channel
fails.

So the search is turned round. `FindPathToNetwork` starts at the place and
expands outward with **no destination at all**, stopping at the first ground it
settles that already carries road. Whatever it finds is, by construction, the
cheapest way onto the network from that place, and it is a junction wherever it
lands. The heuristic is the straight-line distance to the nearest known road
point less the reach, floored at zero, which never overestimates because a move
costs at least its length; with no roads to aim at it degrades to Dijkstra.

On the issue's seed, against the shipped plan:

- the same coverage: **123 places served**, exactly the baseline;
- **28 tee junctions against 1**;
- **0.1 km of road running alongside another road, against 4.8 km** — and the
  summed route length equals the distinct road on the ground, 55.7 km both
  ways, which means almost nothing is built twice;
- failed attempts fall from 70 to 30.

The cost is time: 84 seconds against 4, because a search with no destination
settles far more ground than one aimed at a point. That is a real objection for
a whole world at load, and no objection at all for the single-island
regeneration the tooling already has.

What it does not do is reach more places. Coverage is identical, for the reason
the failure census gives: the places the baseline misses are across water, and
a search from the other side meets the same water.

### Can a failed link fall back to something nearer?

Measured, and the answer is no — but the reason is worth more than the answer.

| fallback after a failed link | roads | served | tees | alongside | recovered | searches |
|---|---|---|---|---|---|---|
| none (the baseline) | 88 | 123 | 1 | 4.8 km | — | 158 |
| the nearest point on a road | 93 | 123 | 1 | 5.2 km | 5 | 208 |
| the nearest place already connected | 97 | 123 | 1 | 7.4 km | 9 | 228 |
| the road, then the place | 97 | 123 | 1 | 6.4 km | 9 | 273 |

**Places served does not move at all.** Not by one, under any of them. A
fallback recovers five to nine *links* and builds five to nine more roads to
places that already had one — which is the same finding as before, from the
other direction: the destination of a failed link is usually already on the
network, so a second road to it adds road and no coverage. The nearest-place
fallback makes the alongside-road problem measurably worse, 4.8 km to 7.4 km,
which is the opposite of what anyone wants.

A fallback is worth having only if the thing it falls back to is somewhere the
first search could not reach. The reverse search is that idea done properly:
rather than trying a second guessed destination after the first guess fails, it
never guesses.

## 5. Gameplay policy: selection, planning, routing

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
any routing change in section 4 produces. Choosing destinations near one another is
worth more than choosing cleverly between them. Deliberately spreading them,
which sounds like what a road network wants, is the worst of the four.

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

### The same world, six ways

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

## 6. Recommended validation sequence and acceptance criteria †

Everything above is geometry. None of it has been walked, driven or carted, and
several of the metrics it turns on are drawing-level measures that a player
would not recognise. This is the shortest sequence that would turn the
geometric results into a defensible gameplay recommendation, in the order it
should be run.

**Run it on three contrasting islands, not one.** A dense flat island, a steep
one, and a water-fragmented one. The study's own island views show how
differently the same rules land on different terrain, and a single island
cannot separate a planner's behaviour from its terrain.

| # | what to check | how | acceptance criterion |
|---|---|---|---|
| 1 | **Cart-traversable junctions** | drive a cart through every junction the run reports — the tee count is the whole claim for POI-to-network search | a junction a cart can take without dismounting or dropping the cart, at every reported tee |
| 2 | **Sensible POI entrances** | walk each served place's road end | the road ends on ground a player would walk in on, not on a beach, a cliff face or the far side of the location's own wall |
| 3 | **Elevation-correct connectivity** | walk between every pair of roads the run counts as one network | two roads counted as joined are actually walkable one to the other; the study's 24 m geometric join has no elevation test at all |
| 4 | **Journey detours** | time a handful of journeys along the road against the same journey overland | a road is worth taking; a detour a player would refuse is a planner failure the coverage numbers cannot see |
| 5 | **Per-island generation time** | regenerate one island at a time under each candidate planner | a number that decides whether the 21× offline cost of POI-to-network search is acceptable in game |

> **MEASUREMENT NEEDED — per-island generation time.** The study reports whole-world
> offline runtimes only (3.9 s for the study baseline, 83.8 s for the
> POI-to-network run; both in the published manifests). No per-island timing
> exists, in game or offline. Until it does, no claim can be made about what
> the reverse search costs the single-island regeneration path.

**What would make the checklist fail.** Any of: a tee a cart cannot take; a
road end a player cannot enter the location from; two roads reported as one
network that a player cannot walk between. Each of those would invalidate a
metric this study leans on, not merely lower a score.

### What this cannot tell you

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

## Appendix A. The failure atlas

### Reading a failure on the map

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

### Why every failure on one run failed

Three examples are three examples. This is the whole set: the 27 failed
attempts of the run anchored on places, which is the run with no stillborn
anchors in it, so every failure in it is a search that really ran.

**All 27 report "no reachable path".** Not one hit the iteration cap. In every
case the open set emptied: the search exhausted everything it could walk or
bridge to, and the destination was not in it. They were not cheap failures
either — they settled up to 97 290 cells and took up to 2 406 crossings on the
way.

Measuring the ground between where each search stopped and where it was going,
on the 8 m dump, using the pathfinder's own rule for what a road may enter:

| what stopped it | attempts |
|---|---|
| open water wider than a bridge may span | 21 |
| a bridgeable channel, but the destination is in the Mistlands, where bridges are refused outright | 3 |
| a bridgeable channel, but no usable bank — every candidate refused for want of one, or for banks more than 2.5 m apart in height | 2 |
| the destination's own cell is under water | 1 |

Twenty-six of twenty-seven are water. The median channel is 256 m and the
widest is 1.6 km. Two of them are rules rather than geography, and both are
ours to change: the ban on bridges in the Mistlands, and the 128 m span cap.
Raising the cap has sharply diminishing returns on this world:

| longest bridge allowed | destinations still cut off |
|---|---|
| 128 m (today) | 21 of 27 |
| 192 m | 17 |
| 256 m | 13 |
| 512 m | 7 |

Even a fourfold span buys back fourteen of twenty-seven, and buys them with
half-kilometre bridges.

One caveat on the method: the channel is measured along the straight line from
where the search stopped to the destination, not over every crossing point
that exists, so a narrower crossing could lie off that line. What corroborates
the number is the searches themselves, which took thousands of crossings and
still could not get there.

### What a failed attempt actually costs

Less than the count suggests. **Twelve of the 27 destinations have a built
road ending within 40 m of them anyway**, because the plan carries on from the
place it was heading for whether or not the leg to it was built: the place
that could not be reached from A becomes the start of the leg to B, and that
leg succeeds. The failure costs the link, not the destination.

![a destination a road already reaches from the other side](https://raw.githubusercontent.com/tvongaza/ProceduralRoads/docs/validation-gap/validation-results/screenshots/study-2026-09-09/failure-on-network-anyway.png)

The other fifteen are places genuinely left off. This is the same effect that
made the anchor fix worth 19% more road and one more place served, and it is
why a failed-attempt count is a poor proxy for coverage.

### Four failures on the map

![a channel wider than any bridge](https://raw.githubusercontent.com/tvongaza/ProceduralRoads/docs/validation-gap/validation-results/screenshots/study-2026-09-09/failure-wide-channel.png)

**Bonemass to GoblinKing, 1 648 m apart.** The search settled 29 466 cells
across the whole group of islands it could reach, came within 626 m on a north
shore, and stopped. The plan drew this edge because the two places are near
each other in a straight line.

![a bridgeable channel with no bank a bridge could stand on](https://raw.githubusercontent.com/tvongaza/ProceduralRoads/docs/validation-gap/validation-results/screenshots/study-2026-09-09/failure-no-bank.png)

**A road that reaches the destination's island but not the destination.** The
search crossed to within 209 m; the water left on the line is only 72 m, well
inside a bridge's reach, but every crossing candidate was refused for want of
a usable bank. Note the green road already on the destination's island: that
one was built from the other side.

![bridges are refused in the Mistlands](https://raw.githubusercontent.com/tvongaza/ProceduralRoads/docs/validation-gap/validation-results/screenshots/study-2026-09-09/failure-mistlands-ban.png)

**182 m, and 40 m of water.** Easily bridgeable, except that the destination
is in the Mistlands and `TryGetRiverCrossing` refuses a bridge there outright.
The search filled its island and stopped 111 m short.

![a destination whose own cell is under water](https://raw.githubusercontent.com/tvongaza/ProceduralRoads/docs/validation-gap/validation-results/screenshots/study-2026-09-09/failure-harbour.png)

**A harbour, which is the point.** `Mistlands_Harbour1` sits below the
waterline, as a harbour does. The search reached the cell beside it — 6 m
away, of 225 m — and could never enter the goal cell, because a move into
water below 28 m is blocked. No budget and no bridge would change this one;
the destination is not ground a road can end on.

## Appendix B. Parameter sweeps and one change at a time

### PR #16, one change at a time

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

## Appendix C. Two bugs found on the way

### A crash, first

Generating a whole world with bridges enabled threw and left the world with no
roads at all. Two crossings on one road can overlap on the path — a bridge's
banks walk out to the bank tops, a swamp bridge's on to dry ground — and the
painting step assumed they never did.

Fixed, with tests for both shapes, in the bridges branch.

### And a second one

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

## Appendix D. Review history: where this is still wrong

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
  over routes, because summing punishes a strategy for sharing road. Every
  table now says which of the two it is reporting.
- The document asserted things about the map that a reader could not look at.
  The world is now drawn under six configurations at one scale, six islands at
  one scale, and three failures one at a time; and the run's places are broken
  out by what they are — boss, dungeon, settlement, ruin — with every required
  destination the generator did not reach named.

Still true of the numbers here, and not fixed:

- **The offline search can walk off the edge of the world.** Nothing bounds
  the pathfinder to the world disc, and a dump answers a point past the rim
  with the rim's own values, so a search that exhausts the land can spend its
  remaining budget over an ocean the harness invents. One attempt on this seed
  did. In game the values out there would differ; the behaviour would not.

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

## Appendix E. Reproducibility: the runs behind these numbers

Every table above comes from a run whose manifest carries a run id, the study
commit, the settings and a content hash of every input. The manifests, the
per-place outcomes, the per-island table, the selection and attempt tables and
the crossings are published beside this document:

[validation-results/study-2026-09-09](https://github.com/tvongaza/ProceduralRoads/tree/docs/validation-gap/validation-results/study-2026-09-09)

Route geometry is not published — several megabytes a run — but it regenerates
from the manifest, which names the code and the inputs exactly.

## Appendix F. Not reproduced

The issue reports 80 islands on that seed; detection finds 67 with the same
rule. Island detection does not depend on any setting, so the difference is in
the world itself, most likely a worldgen change since the report.

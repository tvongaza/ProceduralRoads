# Road networks: a routing study

**Offline routing study. No gameplay traversal has been validated.** The
planner comparison is entirely offline, on terrain read back from a dump.
Older in-game generation runs appear in three places, each labelled where it
occurs: the calibration in section 2, the ford and bridge counts in section 5,
and one run used to confirm world A's seed and build.
Nothing here has been walked, driven or carted. Section 6 says which claims a
session in game would settle and which it would not.

This came out of issue #7 ("Roads seem to be limited to 2-3 per island"). It
reproduces what the issue describes, finds a different cause than the one being
tuned there, and ends with a shortlist and the evidence behind it.

**It is not measured on the issue's own seed.** World A — `Issue7` in the run
files, which is where the mistake came from — has seed `gqZ5SrFUjk`, not the
`nRleKzu9bI` the issue reports. The sparseness the issue describes reproduces
on all three worlds, but nothing here is a statement about the reporter's map.
Appendix F.

*Authorship and provenance.* The measurements, the runs and the code are the
study's; the prose is written by an AI assistant from those runs, for a human
to check and edit. Appendix D names the code, inputs and machine behind every
table; Appendix E lists what earlier drafts got wrong.

## 1. Executive summary

**Why are networks sparse?** Not mainly the search. On world A 2 470 places are
eligible for a road and the per-island quota — `2 + area / 2 km²` — selects 158
of them. **About 93 % of eligible places never get an attempt at all**, and no
search setting can reach a place selection never chose. The iteration budget,
the setting the issue discusses, is worth something but not that: at the study
baseline's 100 000 it binds one attempt in 158, while at the **shipped 10 000**
it binds 33, and raising it to 30 000 is worth 10 roads and 10 more places
served. That is a real gain inside the 7 % that were selected, and it leaves
the 93 % untouched.

**What limits the network above that?** Water. Of the 27 failures in the run
with no wasted anchors, 26 are a channel a road cannot cross; the median is
256 m against a 128 m bridge cap. And the largest connected run of road
anywhere, under every plan and on all three worlds, is four to six roads,
because these worlds are archipelagos. Plan choice matters far less than that.

**Which planners deserve further work?**

| approach | recommendation | why |
|---|---|---|
| **routed-cost MST** | **advance** | the strongest candidate for preserving destination coverage among the alternatives tested: of the shipped plan's own destinations it drops 0, 3 and 7 on the three worlds, and of its boss altars 0, 0 and 2 — less than half what either other plan drops. Against that it produces **very few tees — 0, 1 and 0 across the three worlds** — and costs 2.3× the generation time on this harness (Appendix C: a figure the boundary artifact flatters). |
| **POI-to-network search** | **advance as a branch mechanism, not as a whole plan** | the only plan that makes junctions without spending road to do it: 28 tees against 1, and 0.1 km running alongside other road against 4.8 km. Against that it drops 10, 17 and 32 of the shipped plan's destinations and up to 6 of its boss altars. |
| trunk and spurs | deprioritise | most junctions of any plan (29), but the worst boss coverage on all three worlds (drops 5, 1 and 6): its junctions are bought by not reaching things. |
| the shipped plan | keep as the baseline | it reaches the most required destinations on two of three worlds; its weakness is shape, not reach — one tee in a world. |
| nearest-connected-place fallback | worth a second look | serves 3 more places, for 38 more searches and 3.7 km more road running alongside other road. It does not join existing components. |
| road-sharing discount | conditional | 12 km of distinct road saved once every place is selected; nothing at all at the study baseline. |
| larger iteration budget | **test 30 000 as a candidate** | at the study's 100 000 the budget binds one attempt in 158; the mod ships at 10 000, where it binds 33, and on world A 10 000 → 30 000 added 10 roads and 10 strictly served places. Measured inside the study configuration, not a whole shipped default; diminishing above ~30 000; and no budget reaches what selection never chose. |

**The one hypothesis this study now has a reason to test.** Routed-cost MST
holds destinations and makes almost no tees; POI-to-network makes tees and
drops destinations. A routed-cost backbone with POI-to-network branches is the
obvious combination, and **it has not been built or measured.** It is the first
follow-up experiment, not a recommendation.

**What remains unproven.** Connectivity is measured geometrically, by endpoint
proximity and by a shared-place rule, and neither establishes a walkable
connection. The endpoint-height screen on world A helps prioritise inspection;
it does not bound traversal risk. Gameplay validation remains outstanding, and
nothing here has been played.

The planner-comparison charts and the world and island maps use **served
(+0.5 m)**. The quota chart uses the strict served count; the funnel reports
selection and connection outcomes, not coverage; the plateau, shore and centre
charts do not measure coverage at all. Each figure names its own metric. Across
the twelve planner runs the tolerance adds between 2 and 10 places.

![four planners, three worlds: coverage, road and time](https://raw.githubusercontent.com/tvongaza/ProceduralRoads/605e9620ec9469955fc91018dda72c29a9abef1f/validation-results/screenshots/study-2026-09-10/chart-tradeoff.png)

## 2. Setup and definitions

### What produced the numbers

Road generation was run against terrain read back from a dump of the world,
using the mod's own island detection, location rules, pathfinder and painting.
A whole world takes a few seconds that way, and every run repeats exactly.

The dumps sample the positions the game samples — the 128 m island grid and the
8 m cells the pathfinder walks — so heights, biomes and river weights are exact.
Move costs are **not**: the terrain-variance term is sampled on a ring 16 m out
at eight angles, four of which fall between dumped positions, and crossing
depths are interpolated the same way. That is why nothing about fords should be
concluded here.

Checked against the game on world A, same settings both sides:
89 roads in game against 88 offline, 59.9 km against 58.5 km summed over
routes, 158 attempts both sides, 69 failures against 70, and 81 of 88 matched
roads following the same line within one 8 m cell.

**Versions and worlds.** The terrain was dumped from **Valheim 0.221.12,
buildid 21981559**, the build before 1.0, and **no number here was measured on
1.0**. Nor can it be assumed to transfer: 1.0 changes `GetBiome`, and
`GetHeight` reads the biome to choose a height function, so an unchanged method
body can still return a different height — and island detection, elevation and
the road costs all follow from those heights. That comparison has not been
run.

| in this document | in the run files | seed | terrain dump |
|---|---|---|---|
| world A | `Issue7` | `gqZ5SrFUjk` (42686952, worldgen 2) | `c1e0add2864b8518` |
| world B | `RoadTestMac2` | not recovered — the world file is on the machine that dumped it | `52866baff84f4a28` |
| world C | `RoadTestAuto1` | not recovered, as above | `a5b115417ea8206e` |

World A's seed comes from its own `.fwl`, and the stored seed integer is the
stable hash of that string, so the pair checks itself; an in-game run three
hours after the dump reports the same seed and `gameVersion 0.221.12`, which is
also where the build above comes from.

### The study baseline

"The baseline" means this exact configuration, which is **not what the mod
ships with**:

| setting | study baseline | shipped default |
|---|---|---|
| `IslandRoadPercentage` | 100 (every island) | 50 |
| fords | on | (before the crossings work, off) |
| bridges | on | (before the crossings work, off) |
| `PathfindingMaxIterations` | 100 000 | 10 000 |
| `MaxLocationsPerIsland` | 12 | 12 |
| places per island | `2 + area / 2 km²` | `2 + area / 2 km²` |
| `RoadWidth` | 4 | 4 |

| | roads | summed over routes | distinct road | served | networks |
|---|---|---|---|---|---|
| shipped defaults | 30 | 12.4 km | 12.0 km | 46 | 21 |
| the study baseline | 88 | 58.5 km | 54.0 km | 123 | 49 |

Three times the network before any change proposed here. The baseline was
chosen so a lever's effect is not hidden by another setting suppressing it,
which also means every number here sits on a more generous configuration than
a player gets.

### What the metrics mean

Defined once, used throughout.

| term | what it counts |
|---|---|
| **connections** | decisions the plan made. One connection can cost more than one search: the POI-to-network plan runs a destination-free search and then builds along what it found, and both are logged. |
| **build searches** | pathfinder calls that tried to lay a road. This is the attempt log's row count. |
| **planning searches** | pathfinder calls a plan ran to *price* a candidate edge before choosing. They lay no road and are not in the attempt log. |
| **total searches** | the two added. This is the work a plan costs. |
| **roads** | searches that returned a route and were painted. |
| **planned and built** | places sitting at an end of one of those roads, matched by the place's own coordinates within 1.5 m. On the study baseline this is 129 against the 128 selected places connected, the extra one being the start temple, which is an anchor rather than a destination. |
| **served** | places with a road end within 25 m, or within the place's own exterior radius if that is larger. |
| **distinct road** | length of road on the ground, counting a stretch used twice once. |
| **summed over routes** | every route's length added up, so shared road counts twice. |
| **components** | how many disconnected pieces the island's roads fall into, by two rules: an endpoint within 24 m of any point of another road, **and** two roads whose endpoints both land within a place's serving reach plus 8 m, however far apart they finish on its approach circle. A drawing-level measure with no elevation test and no test of what lies between: see Appendix C. **More components means more fragmentation, not better joining** — a number that rises has got worse, or has gained an isolated new piece. |
| **tees** | roads whose end lands on another road's *length*, more than 24 m from that road's own ends. The shape a walked path network has. |
| **alongside** | metres of road running within 12 m of another road without joining it. |

**Served has a boundary, and eight places on world A sat on it.** A road built
*for* a place is trimmed to that place's exterior radius, so its last point
lands **on** the circle — and the strict test then asks whether a float distance
is at most the float radius the point was built to equal. Both measures are
reported: **served** is that strict test, **served (+0.5 m)** the same test with
half a metre of slack. The second is a wider proximity test, not a corrected
one; Appendix C splits what it adds. On the study baseline they are 123 and 131.
Section 4 carries both; sections 3 and 5 carry the strict one, because those
runs were not repeated.

## 3. Why the current network is sparse

### The quota, and what it excludes

Every place in the world, and what became of it, on the study baseline:

| | world A | world B | world C |
|---|---|---|---|
| places in the world | 11 477 | 11 407 | 11 413 |
| on a detected island | 10 051 | 10 027 | 10 091 |
| eligible for a road | 2 470 | 2 416 | 2 450 |
| selected by the island's quota | 158 | 163 | 171 |
| connected — its planned road was built | 128 | 131 | 141 |

Of the eligible places, by what became of them:

| | world A | world B | world C |
|---|---|---|---|
| lost the island's quota | 2 312 | 2 253 | 2 279 |
| connected | 128 | 131 | 141 |
| attempted, search frontier exhausted | 29 | 32 | 29 |
| attempted, iteration budget spent | 1 | 0 | 1 |

![the funnel from placed to connected](https://raw.githubusercontent.com/tvongaza/ProceduralRoads/605e9620ec9469955fc91018dda72c29a9abef1f/validation-results/screenshots/study-2026-09-10/chart-funnel.png)

The funnel's last bar is the table's last row, 128 — selected places whose
planned road was built. **Planned and built** counts 129 on the same run,
because it counts every place at the end of a road rather than only the
selected ones, and the 129th is the start temple, which is an anchor rather
than a road destination.

**Ninety-three per cent of eligible places never get an attempt.** The quota is
`2 + area / 2 km²`. These worlds have a median island under 2 km², so more than
half of all islands are capped at two places, which is one road. That is
exactly the "2-3 roads per island" the issue reports, and it is arithmetic
rather than a failure.

`MaxLocationsPerIsland` is not the lever it looks like. Raising it from 12 to
100 moves world A from 88 roads to 91. The ceiling almost never binds;
the area formula does.

### The quota is spent on dungeons

| priority | offered | selected | share |
|---|---|---|---|
| 100 (bosses) | 19 | 19 | 100 % |
| 80 (crypts, sunken crypts, mountain caves, anything registered through the API) | 517 | 106 | 20.5 % |
| 75 (Mistlands town entrances, older crypts) | 421 | 24 | 5.7 % |
| 70 and below | 1 513 | 9 | 0.6 % |

Each eligible place falls into exactly one category. The grouping is the
study's own, not the mod's — see Appendix C. The last column is the *+0.5 m*
served measure, so it agrees with section 4. An earlier
draft counted this column a third way — an attempt endpoint within 32 m — and
that definition has been dropped.

| what it is | in the world | eligible | selected | a road reached it |
|---|---|---|---|---|
| boss altars | 19 | 19 | 19 | 14 |
| dungeons (crypts, sunken crypts, caves) | 920 | 913 | 109 | 94 |
| Mistlands structures | 3 014 | 828 | 28 | 21 |
| settlements (villages, farms, swamp huts) | 50 | 49 | **0** | 0 |
| ruins, towers and stone circles | 671 | 661 | 2 | 0 |

Not one of the world's fifty settlements is ever selected. A player walking
this world finds roads between crypts. It is also why registering a location
through the API matters more than it looks: a registered location gets priority
80, straight into the band that wins slots.

Five of the nineteen boss altars get no road, all of them "no reachable path"
and none out of budget: two GoblinKing and three Dragonqueen, at 626 m to
1 692 m from the nearest road end. The full list is in `Issue7-outcomes.csv`.

### The coast-cell anchor

Off the starter island, each island's network is rooted at the island cell
nearest its bounding box — a coast cell, not a place. Islands are found on a
128 m grid and a cell counts as land on its base height, so a cell straddling
the shore is land while its centre, which is what the anchor uses, can be well
out to sea.

On world A **32 of the 70 failed attempts settle a single cell and
stop**, and every one of them starts below the waterline. Across those 32 the
refusals are exactly 256 knight moves, 190 no-bank-found and 54 no-river —
sixteen apiece, no exceptions.

| the attempt's start cell | attempts | connected |
|---|---|---|
| above the waterline | 102 | 75 (74 %) |
| below it | 56 | 13 (23 %) |

| anchor | roads | distinct road | served | components | attempts | failed | of those stillborn |
|---|---|---|---|---|---|---|---|
| the island's edge cell (baseline) | 88 | 54.0 km | 123 | 49 | 158 | 70 | 32 |
| the same cell, walked onto land | 104 | 64.6 km | 124 | 51 | 158 | 54 | 5 |
| the island's highest-priority place | 82 | 54.9 km | 124 | 49 | 109 | 27 | 0 |

The failure class all but disappears and 19 % more road gets built, but
coverage goes 123 to 124: the chain carries on from the place it was heading
for whether or not the leg to it was built, so a stillborn first leg costs the
road, not the destination. **The anchor rule is not a coverage lever**; it
decides how much road exists and how many searches are wasted.

### The iteration plateau

| iterations | roads | budget spent | frontier exhausted |
|---|---|---|---|
| 5 000 | 62 | 48 | 48 |
| 10 000 | 74 | 33 | 51 |
| 20 000 | 78 | 19 | 61 |
| 30 000 | 84 | 8 | 66 |
| 60 000 | 87 | 4 | 67 |
| 100 000 (the setting's ceiling) | 88 | 1 | 69 |
| 120 000 (past it; see below) | 88 | 1 | 69 |

![roads and the two failure kinds against the iteration budget](https://raw.githubusercontent.com/tvongaza/ProceduralRoads/605e9620ec9469955fc91018dda72c29a9abef1f/validation-results/screenshots/study-2026-09-09/chart-plateau.png)

As the budget grows, attempts that used to stop at the cap instead exhaust
their frontier: every cell they can reach, settled, without arriving. Past about
30 000 there is almost nothing left for a larger budget to rescue — but the
**shipped default is 10 000**, not 100 000, and between the two the network
gains 14 roads and 15 places served on this world. The plateau is an argument
against raising the budget far, not against raising it at all.

The 120 000 row is a real run, and it is a budget **a player cannot select**:
the config binds `PathfindingMaxIterations` to 1 000–100 000 and the offline
harness sets the field directly. It is here to show the plateau continues past
the ceiling, not as a setting anyone can use.

### Do roads cluster at the edges?

Measured for all 57 862 centreline points against the land itself, because most
land in these worlds is near a shore.

| | road points | the land itself |
|---|---|---|
| distance to open water, median | 97 m | 74 m |
| distance from the world's centre, median | 5 146 m | 7 037 m |

**No aggregate edge concentration was detected.** Roads sit *further* from open
water than the land does and *nearer* the world's centre. That is a statement
about the whole-world distribution; an island-level report of roads hugging its
coast is not contradicted by it.

![distance to open water, roads against land](https://raw.githubusercontent.com/tvongaza/ProceduralRoads/605e9620ec9469955fc91018dda72c29a9abef1f/validation-results/screenshots/study-2026-09-09/chart-shore.png)

![distance from the world's centre, roads against land](https://raw.githubusercontent.com/tvongaza/ProceduralRoads/605e9620ec9469955fc91018dda72c29a9abef1f/validation-results/screenshots/study-2026-09-09/chart-centre.png)

What roads do favour is swamp: 35.1 % of road on 7.3 % of the land, against
13.4 % on the Mistlands' 29.2 %. The pathfinder is following its cost model —
swamp is flat and waded for a modest penalty, the mountains and the Mistlands
are steep and dear — and a third of a network in one biome is a design choice
worth making deliberately rather than inheriting from a cost constant. It is
also why roads look like they run into the sea: a swamp sits at and below the
waterline.

## 4. Strategy comparison

### What the shipped plan cannot do

Every road runs **from one place to another place**; nothing in the shipped code
starts a road from the network it has already built. On odd island ids the plan
is a nearest-neighbour chain, on even ids a minimum spanning tree over the
anchor and the selected places — and the tree is built on **straight-line
distance** before any of it is routed. Three consequences:

1. **A plan cannot see water.** The cheapest-looking neighbour is often the one
   across a channel; most of Appendix B's failures are edges no planner with a
   map would have drawn.
2. **Roads do not join except by accident** — a junction happens only where an
   endpoint lands within 24 m of another road. On the whole world: **one** tee.
3. **A place that fails is not retried from anywhere else.**

### The four plans, measured

Four plans, three worlds, identical inputs, settings and metric collection; one
warm-up and three measured runs each in the same Release build. Every run is
deterministic — the three repetitions agree to the last metre.

| world | plan | roads | distinct road | summed | served | served (+0.5 m) | components | tees | alongside |
|---|---|---|---|---|---|---|---|---|---|
| Issue7 | shipped plan | 88 | 54.0 km | 58.5 km | 123 | 131 | 49 | **1** | 4.8 km |
| Issue7 | routed-cost MST | 90 | 56.6 km | 59.1 km | 126 | **134** | 50 | 0 | 2.7 km |
| Issue7 | trunk and spurs | 82 | 52.9 km | 57.6 km | 118 | 128 | 46 | **29** | 5.1 km |
| Issue7 | POI-to-network | 86 | 55.7 km | 55.7 km | 123 | 132 | 46 | 28 | **0.1 km** |
| world B | shipped plan | 93 | 67.5 km | 71.0 km | 134 | 139 | 50 | 3 | 3.9 km |
| world B | routed-cost MST | 93 | 64.8 km | 65.5 km | 136 | **141** | 51 | 1 | 0.9 km |
| world B | trunk and spurs | 86 | 60.1 km | 61.8 km | 137 | 139 | 47 | **30** | 2.0 km |
| world B | POI-to-network | 85 | 59.2 km | 59.2 km | 133 | 136 | 47 | 24 | **0.1 km** |
| world C | shipped plan | 96 | 54.5 km | 55.5 km | 141 | **146** | 60 | 0 | 1.1 km |
| world C | routed-cost MST | 94 | 55.6 km | 56.4 km | 136 | 142 | 58 | 0 | 1.0 km |
| world C | trunk and spurs | 81 | 48.2 km | 49.1 km | 124 | 128 | 49 | **25** | 1.3 km |
| world C | POI-to-network | 81 | 45.7 km | 45.7 km | 120 | 123 | 49 | 20 | **0.1 km** |

**Shape generalises**: POI-to-network keeps summed route length equal to
distinct road on every world — almost nothing built twice — and holds alongside
road at 0.1 km against the shipped plan's 1.1 to 4.8 km. **Coverage does not**:
+1 on world A, −3 on world B, **−23** on world C.

### The same counts are not the same places

| world | plan | served (+0.5 m) | retained | gained | lost |
|---|---|---|---|---|---|
| Issue7 | routed-cost MST | 134 | 131 | 3 | 0 |
| Issue7 | trunk and spurs | 128 | 118 | 10 | 13 |
| Issue7 | POI-to-network | 132 | 121 | 11 | 10 |
| world B | routed-cost MST | 141 | 136 | 5 | 3 |
| world B | trunk and spurs | 139 | 125 | 14 | 14 |
| world B | POI-to-network | 136 | 122 | 14 | 17 |
| world C | routed-cost MST | 142 | 139 | 3 | 7 |
| world C | trunk and spurs | 128 | 118 | 10 | 28 |
| world C | POI-to-network | 123 | 114 | 9 | 32 |

On world A POI-to-network and the shipped plan both serve about 130 places, and
**twenty-one of them are different places** — a different network, not the same
one drawn more tidily. What it drops includes required destinations: every boss
altar on a selected island is selected, so a boss without a road is a road the
generator tried to build and failed:

| plan | bosses served, Issue7 | world B | world C |
|---|---|---|---|
| shipped plan | 14 of 19 | 15 of 19 | 14 of 19 |
| routed-cost MST | 14 | 15 | 13 |
| trunk and spurs | 9 | 14 | 9 |
| POI-to-network | 12 | 15 | 10 |

![places served by category, four planners](https://raw.githubusercontent.com/tvongaza/ProceduralRoads/605e9620ec9469955fc91018dda72c29a9abef1f/validation-results/screenshots/study-2026-09-10/chart-coverage.png)

### What each plan costs

| world | plan | connections | build searches | planning searches | total searches | failed builds | generate (median of 3) |
|---|---|---|---|---|---|---|---|
| Issue7 | shipped plan | 158 | 158 | 0 | **158** | 70 | **3.19 s** (3.18–3.37) |
| Issue7 | routed-cost MST | 90 | 90 | 261 | 351 | **0** | 7.46 s (7.42–7.62) |
| Issue7 | trunk and spurs | 135 | 135 | 261 | 396 | 53 | 9.36 s (9.16–9.69) |
| Issue7 | POI-to-network | 116 | 157 | 261 | **418** | 30 | 14.24 s (14.20–14.24) |
| world B | shipped plan | 163 | 163 | 0 | 163 | 70 | 3.28 s |
| world B | routed-cost MST | 93 | 93 | 283 | 376 | 0 | 7.52 s |
| world B | trunk and spurs | 142 | 142 | 283 | 425 | 56 | 9.48 s |
| world B | POI-to-network | 109 | 149 | 283 | 432 | 24 | 13.46 s |
| world C | shipped plan | 171 | 171 | 0 | 171 | 75 | 3.14 s |
| world C | routed-cost MST | 96 | 96 | 299 | 395 | 2 | 7.42 s |
| world C | trunk and spurs | 141 | 141 | 299 | 440 | 60 | 10.26 s |
| world C | POI-to-network | 120 | 155 | 299 | 454 | 39 | 12.07 s |

**Pricing is work, not saving.** A plan that prices its edges runs 261, 283 or
299 planning searches — that range is across the three worlds, not within one —
on top of its builds, and none is reused: the edge is routed to price it, the
route is thrown away, and the committed edge is routed again. Routed-cost MST
spends 351 searches on world A where the shipped plan spends 158. "No failed
builds" is a tidier log, not a saving.

**Time.** Generation only, terrain already loaded, all in the same Release
build; loading the dump (2.8–3.2 s) and measuring the network (0.14–0.24 s) are
the same in all 48 runs. POI-to-network costs **3.8 to 4.5× the shipped plan**.
Build configuration matters more than that: Debug is 6.4× Release here, which
is why every manifest records it.

**These ratios are properties of this harness, not of the algorithms** — the
search settles invented ground past the world's rim, and unevenly: routed-cost
MST pays none of it and every other plan pays some. Appendix C. The runtime
ordering is the part of this study most likely to move when that is
corrected.

Per island — which is what the mod's single-island regeneration path actually
does — the cost is small in absolute terms:

| plan | median island | slowest island (Issue7 / B / C) |
|---|---|---|
| shipped plan | 0.02 s | 1.19 / 0.96 / 0.55 s |
| routed-cost MST | 0.04 s | 2.47 / 2.38 / 1.56 s |
| trunk and spurs | 0.06–0.08 s | 2.17 / 2.27 / 2.16 s |
| POI-to-network | 0.06–0.13 s | 3.12 / 3.26 / 2.40 s |

These are offline seconds on this harness. They say the reverse search's
per-island cost is small **offline**; they do not say what regenerating an
island costs in game, where the work competes with a running frame loop, and no
in-game per-island timing has been taken. The offline whole-world figure is the
only reason the plans differ at all in runtime here.

### The maps

![the world under four connection plans](https://raw.githubusercontent.com/tvongaza/ProceduralRoads/605e9620ec9469955fc91018dda72c29a9abef1f/validation-results/screenshots/study-2026-09-10/world-planners.png)

Three islands, chosen on measured terrain rather than by eye. Of the nineteen
islands over 3 km² on world A: **42** has the lowest mean height gradient (0.18
against a median 0.30), **58** the highest (0.37, and the most compact of those
tied at the top, so its steepness is not also fragmentation), **54** the most
coast per unit area (0.74 edge cells per land cell against 0.53). Each is drawn
under all four plans at identical bounds and scale.

![island 42, dense and flat, under four plans](https://raw.githubusercontent.com/tvongaza/ProceduralRoads/605e9620ec9469955fc91018dda72c29a9abef1f/validation-results/screenshots/study-2026-09-10/island42-planners.png)

![island 58, steep, under four plans](https://raw.githubusercontent.com/tvongaza/ProceduralRoads/605e9620ec9469955fc91018dda72c29a9abef1f/validation-results/screenshots/study-2026-09-10/island58-planners.png)

![island 54, water-fragmented, under four plans](https://raw.githubusercontent.com/tvongaza/ProceduralRoads/605e9620ec9469955fc91018dda72c29a9abef1f/validation-results/screenshots/study-2026-09-10/island54-planners.png)

And one junction close up, because the tee count is the whole case for
POI-to-network and the world sheets are too coarse to show one. The same 1.24 km
of world A under both plans, with one spot ringed in each.

Under POI-to-network, the 437 m branch's endpoint is **7.7 m from the nearest
sampled point on the 5.4 km road**, which qualifies as a tee under the study's
12 m proximity metric. That is a measured separation, not physical contact, and
whether a player can step from one to the other is untested.

Two things about the shipped panel are both true and are not in tension: one
shipped road does cross the frame, and the shipped plan lays **no** road within
290 m of the ringed spot. The plans route this ground differently, so the ring
marks a junction in one panel and empty ground in the other.

![one junction under the shipped plan and under POI-to-network](https://raw.githubusercontent.com/tvongaza/ProceduralRoads/605e9620ec9469955fc91018dda72c29a9abef1f/validation-results/screenshots/study-2026-09-10/tee-example.png)

The sheets make a point the tables bury: **on these three example islands the
plans are almost the same picture.** All three get the same places served under
every plan; what differs is the searches spent — four against one on the steep
island. Islands chosen for contrast say what happens on them, not how
representative they are. The world table's differences are many small islands where the plan
changed nothing plus a few large ones where it changed a great deal.

### Fallbacks, re-measured

A failed link can be retried against the nearest point on a road, or the
nearest place already on the network. Measured with each candidate list
excluding the place the failed leg started from, which is the search that had
just failed:

| fallback after a failed link | roads | served | served (+0.5 m) | components | alongside | searches |
|---|---|---|---|---|---|---|
| none (the baseline) | 88 | 123 | 131 | 49 | 4.8 km | 158 |
| the nearest point on a road | 93 | 123 | 131 | 49 | 5.2 km | 208 |
| the nearest place already connected | 99 | **126** | **134** | **50** | 8.5 km | 196 |
| the road, then the place | 99 | 126 | 134 | 50 | 7.5 km | 243 |

The place fallback serves three more places, for 38 searches more than the
baseline's 158 — the drop to 196 in an earlier draft was against the faulty
version's 228, not against the baseline. It is not free either way: 3.7 km more
road running alongside other road, which is the shape problem made worse. The
road fallback buys nothing at all.

**It does not improve connectivity.** The component count rises 49 → 50, which
is one *more* disconnected piece, and a road-by-road membership comparison
(Appendix C) shows no pair of the baseline's roads changing component. It
recovers places and joins nothing.

## 5. Player-facing policy

A control has to belong to exactly one of three decisions.

| decision | the question | levers measured here |
|---|---|---|
| **selection** | how many places, which categories, clustered or spread | the per-island quota; priority-then-nearest against truncated against a fixed draw; the priority presets; what share of islands get roads |
| **planning** | how the selected places are connected | everything in section 4 |
| **routing** | what the pathfinder may enter and at what price | fords, bridges, the span cap, the Mistlands bridge ban, the road-sharing discount, the iteration budget |

**The largest coverage effects are selection effects, not planning effects**,
and the shape effects are planning effects. The tables below use the original
served metric; they were not repeated.

| places per island | roads | summed | served | attempts failing | metres per place served |
|---|---|---|---|---|---|
| `2 + area/2 km²` (baseline) | 88 | 58.5 km | 123 | 44 % | 476 |
| 8 | 258 | 99.4 km | 319 | 28 % | 312 |
| 16 | 466 | 154.1 km | 557 | 25 % | 277 |
| 32 | 838 | 224.0 km | 959 | 20 % | 234 |
| every eligible place | 2 080 | 410.7 km | 2 322 | 16 % | 177 |

![places served against distinct road built](https://raw.githubusercontent.com/tvongaza/ProceduralRoads/605e9620ec9469955fc91018dda72c29a9abef1f/validation-results/screenshots/study-2026-09-09/chart-quota.png)

The quota is not protecting generation from failure: it sits where failure is
*likeliest*, because places added later are near ones already connected and
short roads over known-good ground are the easiest to build. It is also the
least efficient point on the curve for road per place reached.

| arrangement, at the same count | roads | summed | served | metres per place |
|---|---|---|---|---|
| priority, truncated (baseline) | 88 | 58.5 km | 123 | 476 |
| priority, then nearest | 101 | 43.3 km | **142** | **305** |
| priority, then farthest | 77 | 84.1 km | 115 | 731 |
| a fixed draw, ignoring priority | 72 | 58.2 km | 109 | 534 |

Arrangement alone moves coverage from 109 to 142, a wider spread than any
planner change measured here: choosing destinations near one another is worth
more than choosing cleverly between them, and deliberately spreading them —
which sounds like what a road network wants — spends the most road per place
reached of the four, 731 m against 305. The fewest places served is the fixed
draw's 109.

| preset | selected | roads | summed | served | connect rate |
|---|---|---|---|---|---|
| the built-in table (baseline) | 158 | 88 | 58.5 km | 123 | 78 % |
| bosses only | 19 | 9 | 11.4 km | 12 | 63 % |
| bosses and half the dungeons | 139 | 77 | 53.6 km | 108 | 78 % |
| settlements raised to compete | 146 | 67 | 71.5 km | 100 | 68 % |

**A preset cannot be judged on total coverage**: the total favours whichever
category is most abundant and easiest to reach, which here is dungeons. Aiming
at where people live selects about as many places, connects ten points fewer,
and spends more road doing it. The category breakdown that would settle it
exists for the planner runs and not for the presets — the cheapest outstanding
measurement in the study.

| islands selected | roads | served | networks |
|---|---|---|---|
| 10 % | 24 | 34 | 12 |
| 25 % | 49 | 68 | 24 |
| 50 % (default) | 73 | 101 | 41 |
| 100 % | 88 | 123 | 49 |

| offline | roads | summed | served |
|---|---|---|---|
| neither crossing | 47 | 20.9 km | 72 |
| fords only | 78 | 42.8 km | 111 |
| bridges only | 57 | 30.6 km | 86 |
| both | 88 | 58.5 km | 123 |

Crossings are worth more than any other **routing** switch measured — the quota
in the table above moves coverage far further. In game, on one
world state: neither, 49 roads; fords only, 90 roads with **three river fords in
the whole world**; both, 98 roads with 15 crossings. Fords add 41 roads and
almost none of it is river crossing — it is the swamp wading that comes with the
same flag.

**Can a road follow another one?** Not in this build: only a river crossing
costs less when both banks already carry road. Added as a lever and swept, a
discount does nothing at the baseline — 88 roads and 123 served at every price
— because an island with two or three roads leaving one anchor has no common
stretch to reuse. Where there is something to share it works: with every
eligible place selected, a quarter price within 4 m takes distinct road from
383.4 km to 371.5 km while summed route length rises from 410.7 to 458.4. **It
builds the same network out of less road; it does not reach more places.**

## 6. Recommendation, and what would change it

**Improve selection first.** Arrangement at a fixed count moves coverage by 33
places on world A, where on that world no planner moved it by more than 5. If
one change is made, make the quota choose priority-then-nearest.

**Advance two planners, for different reasons.** Routed-cost MST is the
strongest candidate for preserving destination coverage among those tested —
not strictly better than the shipped plan: 4 fewer places on world C, almost no
tees (0, 1 and 0), and 2.3× the time on this harness. POI-to-network is the only
thing that fixes the network's shape, but as a whole plan it drops required
destinations on two worlds of three, so it belongs on *branches* off a backbone
chosen some other way. That combination is the first experiment to run, and it
has not been run.

**Deprioritise trunk-and-spurs**, which buys junctions by not reaching things.

**Test 30 000 as a candidate iteration default.** On world A, with every other
study setting held constant, it added ten roads and ten strictly served places
over the shipped 10 000 — where 33 of 158 attempts stop at the cap, against one
at the study baseline's 100 000, which is the figure quoted everywhere else
here. Above 30 000 the returns are small: 30 000 → 100 000 adds four roads and
five places. Two limits on that: the sweep varies the iteration value inside
the study configuration (every island, both crossings), not a whole shipped
default; and no budget reaches the ~93 % of eligible places selection never
chose. **Selection remained the dominant source of sparseness in the worlds
tested; the reporter's world remains untested.**

**What evidence would change this.** One table, in order of how much each would
move the recommendation.

| # | outstanding evidence | what it would settle | cost |
|---|---|---|---|
| 1 | **the hybrid, measured**: a routed-cost backbone with POI-to-network branches, on the same three worlds and the same selected places as both standalone runs | whether it keeps the MST's destinations *and* the reverse search's junctions. If it does, it replaces both rows of the shortlist | a planner to write, then one sweep |
| 2 | **the gameplay checklist below**, on the three islands named in section 4 | whether a tee is cart-traversable, whether a road end is an entrance, and whether two roads counted as one network are walkable — three metrics this study leans on | a session in game, on a stated build |
| 3 | **the issue's own seed, `nRleKzu9bI`** | whether anything here describes the reporter's world. Nothing in this document does, so the answer to the issue is currently an argument about the rules rather than a measurement of their map | one dump, then one sweep |
| 4 | **bound the dump's valid domain and rerun the comparison** | how much of the runtime ordering is the harness. The offline search settles invented ground past the world's rim, unevenly across plans (Appendix C), and the effect on runtime, coverage and geometry is unestablished. Deferred, not resolved: the rankings here are provisional on it | a bound in the harness, then one sweep |
| 5 | **a fourth and fifth world** | whether POI-to-network's collapse on world C is the terrain or the sample. Every generalisation here rests on three worlds | one dump each, then one sweep |
| 6 | **coverage by category for the three presets** | whether "settlements raised to compete" does what it was asked to do. The breakdown exists for the four planner runs and not for the presets | three runs, no new code |
| 7 | **routing rerun at each span cap** (192, 256, 512 m) | whether a longer bridge recovers destinations, which Appendix B's straight-line table cannot say | four runs, no new code |
| 8 | **the priced route, cached** | how much of routed-cost MST's 2.3× is redundant work: it routes every committed edge twice | a change to the planner, then one sweep |

### The validation sequence, if it is run

The shortest sequence that would turn the geometry into a gameplay
recommendation, on the three islands of section 4, against a stated build —
the terrain here is pre-1.0.

| # | what to check | acceptance criterion |
|---|---|---|
| 1 | drive a cart through each reported tee | a cart takes it without dismounting |
| 2 | walk each served place's road end | the road ends on ground a player would walk in on |
| 3 | walk between roads counted as one network | the join is walkable, not just within 24 m |
| 4 | time a few road journeys against the same journey overland | the road is worth taking |

Appendix C's height screen gives check 3 a starting order — the ten joins over
2 m — not a shorter list.

**This validation has not been run**, so every recommendation above is a
candidate for gameplay testing rather than a conclusion about play. Two further
things the offline model cannot settle whatever else is run: **fords**, whose
depth is judged from interpolated heights (the game found fords on world A and
the offline run found none), and **any single road**, since 7 of 88 deviate
from the game's line and one existed only in game.

## Appendix A. The experiment inventory

| idea | what changed | evidence scope | result | disposition |
|---|---|---|---|---|
| routed-cost MST | plan edges priced by routing them | 3 worlds, 3 runs each | +3 / +2 / −4 served, 0 / 0 / 2 boss altars lost, 0 / 1 / 0 tees, 2.3× time | **advance** |
| POI-to-network search | destination-free search from the place to the network | 3 worlds, 3 runs each | +1 / −3 / −23 served, 3 / 0 / 6 boss altars lost, 28 tees, 0.1 km alongside, 4.3× time | **advance as a branch mechanism** |
| trunk and spurs | one road on the island's routed long axis, spurs onto it | 3 worlds, 3 runs each | −3 / 0 / −18 served, 5 / 1 / 6 boss altars lost, 29 tees | deprioritise |
| nearest-connected-place fallback | retry a failed link from the nearest place on the network | world A, corrected | +3 served, one additional disconnected component, +38 searches, +3.7 km alongside | conditional |
| nearest-road fallback | retry against the nearest point on a road | world A, corrected | no coverage change | deprioritise |
| tree grown outward with retries | PR #16's plan | world A | 90 roads, 126 served, 208 attempts | untested at the new metrics |
| hub and spoke | anchor serves near places, distant clusters get their own hub | world A | 89 roads, 126 served, close to shipped | deprioritise |
| grow from the network | nearest waiting place attaches to the nearest point on the road | world A | 82 roads, 116 served, 15 tees, 1.6 km alongside | deprioritise |
| candidate-set width for routed MST | 3, 6, 12, 24 neighbours priced | world A | flat from 6 onward; the water binds, not the candidates | settled |
| anchor walked onto land | first point above the waterline | world A | 32 stillborn attempts become 5, +19 % road, +1 served | advance, cheap |
| anchor on a place | highest-priority place on the island | world A | 0 stillborn, a third fewer attempts (109 against 158), +1 served | advance, cheap |
| quota by priority-then-nearest | selection, not planning | world A | 123 → 142 served | **advance first** |
| road-sharing discount | existing road costs less to walk | world A, two densities | nothing at baseline; −12 km distinct at every place | conditional |
| larger iteration budget | 5 000 → 120 000, inside the study configuration | world A | from the shipped 10 000: +10 roads and +10 strictly served by 30 000, +4 and +5 more by 100 000; flat above | test 30 000 as a candidate |
| longer bridge span | not run — see Appendix B | — | — | untested |
| routed-cost backbone + POI-to-network branches | not built | — | — | **first follow-up** |

## Appendix B. The failure atlas

The whole set: the 27 failed attempts of the place-anchored run, which is the
run with no stillborn anchors, so every failure in it is a search that really
ran. **All 27 report "no reachable path"**; not one hit the iteration cap. They
settled up to 97 290 cells and took up to 2 406 crossings on the way.

| what stopped it | attempts |
|---|---|
| open water wider than a bridge may span | 21 |
| a bridgeable channel, but the destination is in the Mistlands, where bridges are refused outright | 3 |
| a bridgeable channel, but no usable bank | 2 |
| the destination's own cell is under water | 1 |

Twenty-six of twenty-seven are water. The median channel is 256 m and the
widest is 1.6 km. Two of them are rules rather than geography, and both are
ours to change: the Mistlands bridge ban and the 128 m span cap.

**Straight-line channel widths on the failed lines, as a diagnostic.** This is
not a measurement of what raising the span cap would recover, and routing was
not rerun at any of these caps:

| straight-line gap on the failed line | destinations whose gap is still too wide |
|---|---|
| 128 m (the cap in this build) | 21 of 27 |
| 192 m | 17 |
| 256 m | 13 |
| 512 m | 7 |

A wider cap has to find a bank at both ends, at a height difference the
crossing rule allows, on a line the search actually walks, and the channel here
is measured along one straight line rather than over every crossing point that
exists. **Rerunning routing at each cap is the only thing that would turn this
into a result**, and it is worth doing only if a larger cap is being proposed.

**What a failed attempt actually costs is less than the count suggests.**
Twelve of the 27 destinations have a built road ending within 40 m of them
anyway, because the plan carries on from the place it was heading for whether
or not the leg to it was built. The failure costs the link, not the
destination. Five failure case studies are drawn at
[validation-results/screenshots/study-2026-09-09](https://github.com/tvongaza/ProceduralRoads/tree/605e9620ec9469955fc91018dda72c29a9abef1f/validation-results/screenshots/study-2026-09-09):
a coast anchor 150 m out to sea, a search that settled a whole archipelago, the
one attempt that spent its whole budget, a bridgeable channel with no usable
bank, and a harbour whose own cell is below the waterline.

## Appendix C. What the geometry metrics cannot say about themselves

**The category grouping is the study's own.** Bosses and five dungeon prefabs
are named from the generator's priority table and three prefabs are called
settlements; everything left whose name begins `Mistlands_` is grouped as a
Mistlands structure. That last one is a name pattern rather than a
priority-table entry, which is why that row's "in the world" count is far larger
than its eligible count. Every *eligible* place still falls in exactly one
category, and those counts are the generator's.

**What the +0.5 m serving tolerance actually adds.** A road built for a place
is trimmed to that place's exterior radius, so its last point lands *on* the
circle and the strict test becomes a float comparison against the number the
point was constructed to equal. Half a metre of slack settles that — but it is
a wider proximity test, so it also catches places a road merely passes near.
Across the twelve planner runs it adds 68 places:

| what the tolerance adds | count | what they are |
|---|---|---|
| boundary recoveries | 60 | a road was built for the place and ends on its circle |
| incidental proximity | 8 | no road was built for the place; a road end falls 25.1–25.3 m away |

The eight are `Ruin1` and `MountainGrave01` on world C, `StoneHouse3` on world
B and `InfestedTree01` on world A, each recurring across the plans on its own
world; all have a small exterior radius, so their reach is the flat 25 m floor.

**What the component count says about the fallback, and what it does not.** The
nearest-connected-place fallback takes the count from 49 to 50, which is one
*more* disconnected piece. Matching roads by identity across the two runs: all
88 of the baseline's roads are present and identical in the fallback run, and
**not one pair of them changes component**. The eleven extra roads attach to
pieces that already existed or form one of their own. The fallback recovers
three places and joins nothing.

**A junction is a two-dimensional test.** An end within 12 m of another road's
length is a tee, with no regard for height or for what lies between. What
follows is an **endpoint-height screen**, not a walkability check: it reads the
height difference across each junction at the nearest point, on world A.

| plan | tees | median height across | p90 | worst | over 2 m |
|---|---|---|---|---|---|
| shipped plan | 1 | 0.30 m | 0.30 m | 0.30 m | 0 |
| routed-cost MST | 0 | — | — | — | — |
| trunk and spurs | 29 | 0.30 m | 1.00 m | 1.70 m | 0 |
| POI-to-network | 28 | 0.40 m | 1.70 m | 5.40 m | 2 |

End-to-end joins are similar: the worst across all four plans is 5.7 m.

**This does not bound walkability, and an earlier draft said it did.** Two
endpoints at the same height can have a river, a wall or a ravine between them,
and the second component rule — roads meeting at a place — has no distance
limit beyond that place's reach, so a height screen cannot see the gaps that
matter most. It is good for ordering an inspection: the ten joins over 2 m (two
shipped, two routed-cost MST, none trunk-and-spurs, six POI-to-network) are
where to start, **not** the only ones that need walking.

The screen is `scripts/junction-audit.py` on the study branch, so these numbers
can be reproduced rather than taken. Its tee counts match the manifest by
construction; its end-to-end counts do not, because the manifest counts *roads
that have* an end-to-end join while the screen counts junctions.

**The offline search can walk off the edge of the world.** Nothing bounds the
pathfinder to the world disc, and a dump answers a point past the rim with the
rim's own values, so a search that exhausts the land can spend its remaining
budget over an ocean the harness invents. On world A this happens to 5
failed attempts under the shipped plan (179 122 cells), 5 under POI-to-network,
10 under trunk-and-spurs and **none** under routed-cost MST. It inflates the settled-cell counts and the runtimes of the affected plans, and
it does so **unevenly**: routed-cost MST pays none of it and every other plan
pays some. The comparison is not corrected for it.

**An earlier draft argued that the correction could not change the ranking.
Withdraw that.** The artifact adds work to routed-cost MST's competitors and
none to routed-cost MST, so it flatters the recommended plan in exactly the
comparison the recommendation uses. Removing it would narrow the runtime
differences by an unknown amount and could reorder them. Nor is "it cannot turn
a failure into a success" safe: ground the harness invents is traversable, so in
principle it could offer a detour back toward an in-world destination, and
nothing here has checked whether it ever does.

**Read every runtime in this document as a measurement of this harness,
boundary artifact included, rather than of the algorithm.** A
boundary-corrected comparison is follow-up work. Its effect on coverage and
geometry has not been established either: invented ground is traversable, so a
detour through it back toward an in-world destination is possible in principle
and has not been checked.

**The metric definitions are shared, not per-plan.** Tees, ends, alongside,
components, served and planned-and-built are computed by one function over
each run's routes, at the same tolerances (12 m junction, 24 m end margin and
join, 12 m corridor, 1.5 m identity, 25 m served). A difference between two
plans in this document is a difference in their geometry.

## Appendix D. Reproducibility

Every run carries a manifest with a run id, the code commit **and whether the
tree was dirty**, the build configuration, the runtime and platform, a content
hash of every input, the settings, four stage timings and the results. The
manifests, the per-place tables, the per-island tables, the selection and
attempt tables and the crossings are published beside this document:

[validation-results/study-2026-09-10](https://github.com/tvongaza/ProceduralRoads/tree/605e9620ec9469955fc91018dda72c29a9abef1f/validation-results/study-2026-09-10)

Route geometry is not published — several megabytes a run — but it regenerates
from the manifest, which names the code and the inputs exactly.

**Every link in this document is pinned to commit `605e962`**, the revision
that published the current data and images, so a later change on the branch
cannot alter the evidence a claim here rests on. (The runs themselves were
published at `6d3403a` and are unchanged; `605e962` corrected the figures on the
charts and sheets.)

The comparison in section 4 is 48 runs: three worlds × four plans × (one
warm-up + three measured). All were produced by the study branch at
`f3cb226-dirty` in a **Release** build on .NET 10.0.7, macOS arm64, 8
processors. **That dirty tree is now committed as `be34d54`.** Clean-code
reproduction was verified for **one** run — POI-to-network on world A, which
matched every metric in its manifest except the timings; the other 47 were not
re-verified that way.

**This is not yet independently reproducible from the public artifacts alone.**
The terrain dumps the runs read are roughly 250 MB each and are not published;
they are on the machine that made them. What is published is enough to check
every number in this document against the run that produced it, and not enough
for someone else to produce those runs.

## Appendix E. Corrections to the previous draft

Each of these was a claim in the 9 September version of this document. Each is
now either measured or withdrawn.

| claim as it stood | what it is now | how it was resolved |
|---|---|---|
| "POI-to-network costs 84 s against 4, a **21× increase**" | **3.8–4.5×** | the 83.8 s run was a Debug build and the 3.9 s run a Release one. Debug is 6.4× Release on this workload: the same generation, same inputs, same output, 20.5 s against 3.2 s. Rerun in one build. |
| "the difference between the two controls has not been explained" | explained | `check` (3.9 s) and `q4-baseline` (20.7 s) are byte-identical runs in different build configurations. The added junction metrics were the suspect and are not: measuring a finished network costs 0.17 s. |
| "261 to 299 searches has no run behind it" | supported | 261, 283 and 299 are the planning searches on the three worlds. The range was across worlds and had lost its label. |
| "the six places counted as planned-and-built but not served have not been enumerated" | enumerated, and the mechanism was wrong | it is **eight** places, and two go the other way. They are not roads trimmed short of the circle: every one has its nearest road end at *exactly* its own radius, which makes the served test a float comparison against the number the endpoint was constructed to equal. Reported now as `served` and `served (+0.5 m)`, and the tolerant one is **not** a pure boundary repair: of the 68 places it adds across the twelve planner runs, 60 are boundary recoveries and 8 are incidental proximity. |
| "the reverse plan logs a successful connection twice" | quantified | 157 rows are 46 seeds + 70 destination-free searches (41 found the network) + 41 road builds (40 committed). 116 connections, 86 roads, 30 failures. **One search found the network and still built no road** — the path trimmed to nothing — so a successful search does not imply a committed road. |
| "the fallbacks move served count by zero" | **wrong on coverage, right on connectivity** | the nearest-connected-place fallback was choosing the place the failed leg started from and re-running the identical search, 52 times in 70. Fixed, it serves 3 more places in 196 searches rather than 228. It still joins nothing: the component count rises 49 → 50, and no pair of the baseline's 88 roads changes component. |
| "the 120 000-iteration row should be relabelled" | kept, and labelled | the harness sets the field directly and really ran 120 000; the config binds a player to 1 000–100 000. Both rows are shown. |
| `chart-centre.png` "how far roads sit from their island's centre" | the world's centre | the calculation is `Vector2(x, z).magnitude` — distance from (0, 0). The document was right and the asset index was wrong; the index is corrected. |
| bridge spans: "read that as an upper bound on what a longer span could help with" | a straight-line gap diagnostic | it measures water on one straight line and tests neither banks nor routing. The "upper bound" and "recovered destinations" framing is withdrawn. |
| "this document does not record the game build or the seeds" | recorded, **and one of them is not what the study thought** | Valheim 0.221.12, buildid 21981559. World A — the world every earlier draft called "the issue's seed" — is `gqZ5SrFUjk`, and the issue's is `nRleKzu9bI`. They are different maps: the world was named `Issue7` and the name was then read as a measurement. See Appendix F. The other two seeds are on the machine that dumped them and are marked unknown rather than guessed. |
| the strategy table's "attempts" column | split into connections, build searches and planning searches | a row count and a connection count are not the same number, and one plan writes two rows per connection. |
| the study baseline named `FordsEnabled` and `BridgesEnabled` | named as the run's switches | those config keys no longer exist: crossings ship on. |
| "the issue reports 80 islands and detection finds 67 ... most likely a worldgen change since the report" | a different world | there was never anything to reconcile: the two counts are from two different maps. |

### Found in the 10 September review

A second review of the rewritten document found five more, all confirmed
against the code and the published data before being changed.

| claim as it stood | what it is now | how it was resolved |
|---|---|---|
| the +0.5 m serving tolerance is "too small to reach a road that was not built there" | **false as a general claim** | it is a wider proximity test. Across the twelve planner runs it adds 60 boundary recoveries and **8 incidental** places with no road built for them, at 25.1–25.3 m. The metric is renamed `served (+0.5 m)`, the guarantee is withdrawn, and the split is reported. |
| the fallback "joins one more group" | it joins nothing | `Components` counts *disconnected pieces*, so 49 → 50 is one more piece, not one fewer. Comparing membership road by road: all 88 baseline roads are identical in the fallback run and **no pair changes component**. |
| the component definition, "an endpoint within 24 m of another road" | two rules, not one | roads are also unioned when both ends land within a place's serving reach **plus 8 m**, however far apart they finish on its circle. Now documented. |
| "the plan whose runtime it flatters least is the one recommended, so the correction would not change the ranking" | withdrawn | the out-of-world artifact adds work to routed-cost MST's competitors and none to routed-cost MST, so it flatters the recommended plan in the comparison the recommendation uses. Every runtime here is a measurement of this harness. |
| "no junction in this study is a cliff", and only the joins over 2 m need walking | withdrawn | endpoint height is a screen, not a walkability check: two ends at the same height can have a river or a ravine between them, and the second component rule is not screened at all. The screen is now committed as `scripts/junction-audit.py`. |

Smaller ones from the same review: the 1.0 transfer claim is withdrawn
(`GetHeight` reads the biome to choose a height function, so an unchanged
method body does not imply an unchanged result once `GetBiome` changes); "would
hold on a fourth world" is withdrawn as overclaiming an empirical result; the
per-island seconds are labelled offline and do not answer the in-game question;
Appendix E's runtime range is corrected from 4.2–4.5× to 3.8–4.5×; the front
matter now says where in-game numbers appear instead of implying there are
none; the ten-against-eleven joins over 2 m is reconciled at ten; and the
category grouping is described as the study's own, since the Mistlands bucket
is a name prefix rather than a priority-table entry.

### Found in the third review

A third review read the whole document and the images. Five more, all confirmed
against the runs before being changed.

| claim as it stood | what it is now | how it was resolved |
|---|---|---|
| the summary's "Appendix C … finds none worse than 5.7 m, which bounds the risk" | withdrawn | the summary was still making the walkability claim Appendix C had already withdrawn, and described connectivity by the 24 m rule alone. It now names both rules and says the height screen prioritises inspection rather than bounding risk. |
| routed-cost MST "makes no junctions at all" | **very few tees: 0, 1 and 0** | it makes one tee on world B, and it has 40, 25 and 23 end-to-end joins. The absolute claim appeared in the shortlist, the hybrid rationale, section 6 and Appendix A. |
| the fallback serves 3 more places "for fewer searches" | **for 38 more** | 196 against the baseline's 158. The reduction to 196 was against the *faulty* fallback's 228, not against the baseline. |
| "the budget binds one attempt in 158", and a blanket no to raising it | **that is the study's 100 000, not the shipped 10 000** | at the shipped default 33 of 158 attempts stop at the cap, and 10 000 → 30 000 is worth 10 roads and 10 more places served on world A. The recommendation is now to test 30 000 as a candidate, and it is measured inside the study configuration rather than on a whole shipped default. |
| "Nothing else in this document is left open" | withdrawn | Appendix C leaves the boundary-corrected runtime, coverage and geometry unresolved. It is now row 4 of the outstanding-evidence table, explicitly deferred, with the rankings marked provisional on it. |

Smaller ones from the same review: the charts plotted the +0.5 m counts and
labelled the axis "places served"; the trade-off chart ordered its panels A, C,
B by sorting the run names; the sheets leaned on a legend too small to read;
"a typical island" became "these three example islands"; Appendix D claimed the
committed code reproduces the published runs when one run was verified that way,
and now also says the terrain dumps are not published, so this is not
independently reproducible from the public artifacts alone; and Appendix F
grouped the 27-failure census with the three-world findings when it is world A
alone.

### Found in the fourth review

A fourth review read the document and every embedded image at reading size.

| claim as it stood | what it is now | how it was resolved |
|---|---|---|
| "every chart and map plots the +0.5 m count, 5 to 8 higher than strict" | wrong on both halves | the quota chart is strict, the funnel is not a coverage figure, and three charts measure no coverage at all; the tolerance adds **2 to 10**, not 5 to 8. Each figure now names its own metric. |
| the funnel figure | regenerated | re-pinning its URL had preserved an image titled "issue seed", reporting 129 where the table beside it says 128, with its largest bar's value clipped. Rebuilt from the table's numbers; 128 against 129 is now reconciled in the text. |
| "do not raise the iteration budget", then "raise the shipped default" | **test 30 000 as a candidate** | the sweep varies the iteration value inside the study configuration — every island, both crossings — not a whole shipped default, and the document now says so. |
| selection is "the issue's actual cause" | withdrawn | it is the dominant source of sparseness *in the worlds tested*, and the reporter's world was never tested. |
| farthest-first is "the worst of the four" | worst **metres per place** | it serves 115; the fixed draw serves 109 and is the worst on coverage. |
| crossings are "worth more than any other single lever measured" | any other **routing** switch | the quota sweep moves coverage far further. |
| "no planner moved coverage by more than 5" | **on world A** | world C's reverse plan loses 23. |
| place anchoring uses "a third of the attempts" | a third **fewer** | 109 against 158. |
| priority-then-nearest, "3 worlds" | world A | every section-5 sweep is world A alone. |

Captions and legends that ran off the edge of their own images are wrapped into
reserved space, and the junction figure now carries a ring on the spot it is
about — which caught its caption describing a road that is 290 m away.

### Found in the fifth review

| claim as it stood | what it is now | how it was resolved |
|---|---|---|
| the budget recommendation read three ways: "worth raising from the shipped default", "raise the default", and section 6's "test 30 000 as a candidate" | one wording | "test 30 000 as a candidate", measured inside the study configuration, in all three places. |
| the junction figure and text said the branch ends **on** the longer road | a proximity result | the endpoint is 7.7 m from the nearest sampled point on that road, which is a tee under the study's 12 m metric and is not physical contact. Traversability is untested, and the figure now says so. |
| the shipped panel read as though "one road crosses the frame" and "no road near the ringed spot" were in tension | both stated | they are both true: the ring marks a junction in one panel and empty ground in the other, because the plans route this ground differently. |

### Raised in the first review, fixed since

Raised in review. Fixed before the 10 September draft, kept here so the record
is plain: the MST compared route length rather than the cost the search
accumulated; spurs joined places rather than roads; the road-sharing experiment
discounted only the moves that survived the early returns, with a heuristic
that stopped being admissible once moves were cheap; "connected" carried several
meanings at once; and length is now reported both ways.

## Appendix F. Not reproduced

**The issue's own seed was never generated.** The issue reports 80 islands;
detection finds 67 on world A. An earlier draft of this document put that down
to a worldgen change since the report. It is simpler than that: world A is
seed `gqZ5SrFUjk` and the issue's is `nRleKzu9bI`, so the two are different
maps and there is nothing to reconcile. Island detection depends on no setting,
so a count that differs between two different worlds says nothing at all.

What that does and does not cost the study. Least affected are the findings
about the *rules* — the quota arithmetic and the priority table — which are
properties of the code. Next are the funnel and the planner comparison,
measured on three worlds; "measured on three" is not "true of all", and they
are empirical findings rather than consequences of the arithmetic. **Appendix
B's 27-failure census is world A alone**, as are the clustering, the priority
breakdown and every sweep in section 5. What the wrong seed costs outright is
the right to say anything about the reporter's world in particular: whether it
has 80 islands, whether its bosses are reachable, whether a plan would serve it
better. **Generating `nRleKzu9bI` and running the section-4 comparison on it is
one dump and one sweep**, and until that is done this document should not be
quoted at the reporter as though it were about their map.

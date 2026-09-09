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

### And the quota is spent on dungeons

Of the 2 470 eligible places, which ones win the 158 slots:

| priority | offered | selected | share |
|---|---|---|---|
| 100 (bosses) | 19 | 19 | 100 % |
| 80 (crypts, sunken crypts, mountain caves, anything registered through the API) | 517 | 106 | 20.5 % |
| 75 (Mistlands town entrances, older crypts) | 421 | 24 | 5.7 % |
| 70 and below | 1 513 | 9 | 0.6 % |

The network connects bosses and dungeons and almost nothing else. Villages,
farms, towers, ruins - the great majority of what is on a map - share nine
road ends across a whole world, and no setting changes that, because they are
never attempted.

It is also why registering a location through the API matters more than it
looks: a registered location gets priority 80, straight into the band that
wins slots.

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

## Do roads cluster at the edges?

That can mean the shoreline of an island or the outer parts of the world, so
both were measured for all 57 862 centreline points - against the land itself,
because most land in these worlds is near a shore and roads near shores prove
nothing on their own.

| | road points | the land itself |
|---|---|---|
| distance to open water, median | 97 m | 74 m |
| distance from the world's centre, median | 5 146 m | 7 037 m |

Neither holds. Roads sit *further* from open water than the land does and
*nearer* the world's centre, and connected places are further inland than
unconnected ones. The same is true inside every biome taken separately.

What roads do favour is swamp:

| biome | share of road | share of land |
|---|---|---|
| Swamp | 35.1 % | 7.3 % |
| BlackForest | 24.1 % | 13.9 % |
| Mistlands | 13.4 % | 29.2 % |
| DeepNorth | 1.2 % | 11.0 % |

A third of the network is in swamp, which is a fourteenth of the land, and
18.8 % of eligible swamp places get a road against 2.4 % in the Mistlands. The
pathfinder is following its cost model - swamp is flat and waded for a modest
penalty, the Mistlands and the mountains are steep and dear - and the result
is a network that runs through the biome players like least to travel. That is
a gameplay question rather than a routing one, and it may be the strongest
argument here for changing the cost model rather than the routing.

It is also why roads look like they run into the sea on a map: a swamp sits at
and below the waterline, and a road wading one is doing what it was told to.

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

## The levers, and what each is worth

Everything below is one change at a time from what ships today, on the issue's
seed, with crossings on. "Served" counts places a road end reaches.

### How many places each island may have

| places per island | roads | length | served | attempts failing | metres per place served |
|---|---|---|---|---|---|
| `2 + area/2 km²` (today) | 88 | 58.5 km | 123 | 44 % | 476 |
| 8 | 258 | 99.4 km | 319 | 28 % | 312 |
| 16 | 466 | 154.1 km | 557 | 25 % | 277 |
| 32 | 838 | 224.0 km | 959 | 20 % | 234 |
| every eligible place | 2 080 | 410.7 km | 2 322 | 16 % | 177 |

Two things run against intuition here. The quota is not protecting generation
from failure - it sits where failure is *likeliest*, because the places added
later are near ones already connected and short roads over known-good ground
are the easiest to build. And it is the least efficient point on the curve for
road spent per place reached: 476 metres today against 177 with everything
selected.

What it buys is a very different world - 411 km of road instead of 58 - and
about twenty-five seconds of generation offline instead of three. Whether a
land webbed with roads is the game anyone wants is not a question these
numbers can answer.

### Where those places sit, at the same count

| arrangement | roads | length | served | metres per place |
|---|---|---|---|---|
| priority, truncated (today) | 88 | 58.5 km | 123 | 476 |
| priority, then nearest (PR #16) | 101 | 43.3 km | **142** | **305** |
| priority, then farthest | 77 | 84.1 km | 115 | 731 |
| a fixed draw, ignoring priority | 72 | 58.2 km | 109 | 534 |

Arrangement alone moves coverage from 109 to 142 places - a wider spread than
any routing change below produces. Choosing destinations near one another is
worth more than choosing cleverly between them. Deliberately spreading them,
which sounds like what a road network wants, is the worst of the four.

### Fords and bridges

| | roads | length | served |
|---|---|---|---|
| neither | 47 | 20.9 km | 72 |
| fords only | 78 | 42.8 km | 111 |
| bridges only | 57 | 30.6 km | 86 |
| both | 88 | 58.5 km | 123 |

Crossings are worth more than any other single lever measured: 72 places
served becomes 123. The split needs care, though - the fords flag both jumps
rivers and wades swamps, and only the wading can be measured offline. How much
of that 39 is river fords needs the game.

### How many islands get roads

| islands selected | roads | served | networks |
|---|---|---|---|
| 10 % | 24 | 34 | 12 |
| 25 % | 49 | 68 | 24 |
| 50 % (default) | 73 | 101 | 41 |
| 100 % | 88 | 123 | 54 |

Near enough linear to three quarters and then flat, because the largest
islands are taken first. The default gives up about a fifth of the network the
same world would support.

### What the network is for

Three presets, bosses required in each, the rest drawn per place from the
world seed:

| preset | selected | roads | length | served | connect rate |
|---|---|---|---|---|---|
| the built-in table (today) | 158 | 88 | 58.5 km | 123 | 78 % |
| bosses only | 19 | 9 | 11.4 km | 12 | 63 % |
| bosses and half the dungeons | 139 | 77 | 53.6 km | 108 | 78 % |
| settlements raised to compete | 146 | 67 | 71.5 km | 100 | 68 % |

Bosses alone are barely a network: nine roads in a world, because most bosses
are alone on their island and a road needs two ends. The last row is the
interesting one - aiming at where people live selects about as many places,
connects ten points fewer of them, and spends more road doing it, because
settlements sit in scattered awkward spots. Worth wanting, but not free.

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

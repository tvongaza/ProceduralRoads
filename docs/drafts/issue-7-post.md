<!--
DRAFT. Not posted. For issue jneb802/ProceduralRoads#7, on explicit go only.
Images resolve from the fork's docs branch; check they render before posting.
-->

I went looking for why roads come out sparse and ended up somewhere other than
the iteration limit. Everything below is measured on generated networks, on
three worlds including your seed `nRleKzu9bI`, and checked against a real
in-game run. None of it has been played — I've marked what that means at the
end.

**The short version: the per-island quota and the priority table decide almost
everything, and the iteration budget decides almost nothing.**

## Where the places go

Taking every place in the world and following what becomes of it. Of the 2 470
that are eligible for a road:

| outcome | places |
|---|---|
| lost the island's quota | 2 312 |
| connected | 128 |
| attempted, no route exists | 29 |
| attempted, iteration budget spent | 1 |

Ninety-three per cent never get an attempt. The same holds on the two other
worlds I checked.

The quota is `2 + area / 2 km²`, and that seed's median island is under
2 km², so more than half the islands are capped at two places — one road. That
is the "2-3 roads per island" in the title, and it is arithmetic rather than a
bug. `MaxLocationsPerIsland` is not the lever it looks like: 12 → 100 moves
the world from 88 roads to 91, because the area formula binds long before the
ceiling.

And the slots that exist go almost entirely to dungeons:

| priority | offered | selected |
|---|---|---|
| 100 (bosses) | 19 | 19 |
| 80 (crypts, sunken crypts, mountain caves, anything registered via the API) | 517 | 106 |
| 75 | 421 | 24 |
| 70 and below | 1 513 | **9** |

Villages, farms, towers and ruins share nine road ends across a whole world.

## The plateau

It reproduces, and splitting failures by the pathfinder's own two reasons says
why:

| iterations | roads | budget spent | no route exists |
|---|---|---|---|
| 5 000 | 62 | 48 | 48 |
| 20 000 | 78 | 19 | 61 |
| 30 000 | 84 | 8 | 66 |
| 120 000 | 88 | 1 | 69 |

As the budget grows, attempts that used to stop at the cap instead run to
completion and report the destination unreachable over land. Past ~30 000
there is nothing left for a bigger budget to rescue.

## Roads don't cluster at the edges — they cluster in swamp

Measured for every centreline point, against the land itself (most land here
is near a shore, so roads near shores prove nothing on their own):

| | road points | the land |
|---|---|---|
| distance to open water, median | 97 m | 74 m |
| distance from world centre, median | 5 146 m | 7 037 m |

Roads are further from water and nearer the centre than the ground they are
drawn on — the opposite of both readings of "edges". What they do favour is
swamp: **35 % of the network on 7 % of the land**, and 18.8 % of eligible
swamp places get a road against 2.4 % in the Mistlands. The cost model makes
swamp cheap and mountains dear, so the network runs through the biome players
like least. That may be the strongest argument for touching the cost model
rather than the routing.

## What each lever is worth

One change at a time, crossings on, your seed. "Served" = places a road end
reaches.

| change | served | road length |
|---|---|---|
| as it ships | 123 | 58.5 km |
| quota by priority-then-nearest (your PR #16) | **142** | 43.3 km |
| anchor on a location instead of a coast cell | 124 | 58.1 km (and a third of the wasted attempts) |
| all of PR #16 together | 138 | 34.6 km |
| every eligible place instead of the quota | 2 322 | 410.7 km |
| fords and bridges off | 72 | 20.9 km |
| islands 50 % (default) instead of 100 % | 101 | 52.6 km |

Two notes on your PR. Its quota change is the single biggest gain of anything
I measured. Its ring-balanced island selection is the one part that costs
coverage on these worlds — at 50 % of islands it serves 86 where largest-first
serves 101 — and it also stops an island after 24 failed edges, which is
generous for a dozen places and stops an island of four hundred long before it
is connected.

Off the starter island, 36 of 158 attempts begin on the coast-cell anchor and
die on the first step, because the anchor is not ground a road can stand on.
They don't cost connections, but they waste a quarter of all attempts.

## What a player gets

The largest island on that seed — 26.7 km², 426 eligible places — gets 12
selected, 6 roads, 5.2 km, in three separate networks. Across the world 22 of
67 islands get no road and 22 get exactly one. The longest connected run of
road anywhere is 4-6 roads, on every world and under every routing plan I
tried (roads that meet at the same place count as joined): these worlds are archipelagos, and a road crosses water only where a
bridge can span it — 128 m at most, where most channels here are wider.

![the world as it generates today](https://raw.githubusercontent.com/tvongaza/ProceduralRoads/docs/validation-gap/validation-results/screenshots/study-2026-09-09/issue7-shipped.png)

## Alternative connection plans

Same islands, same places, same anchor, same budget:

| plan | roads | length | served | separate networks |
|---|---|---|---|---|
| chain/MST by island parity (today) | 88 | 58.5 km | 123 | 49 |
| tree grown outward with retries (#16) | 90 | 61.9 km | 126 | 50 |
| MST on routed cost | 90 | 57.6 km | 126 | 50 |
| trunk and spurs | 85 | 62.1 km | 116 | 46 |

Planning on routed cost reaches as much for the least road. Trunk and spurs
makes the fewest separate networks — one road along the island with things
joined to it — and reaches about a tenth fewer places. The pictures show it
better than the table:

![short branches, today](https://raw.githubusercontent.com/tvongaza/ProceduralRoads/docs/validation-gap/validation-results/screenshots/study-2026-09-09/island-parity.png)
![one road along the island](https://raw.githubusercontent.com/tvongaza/ProceduralRoads/docs/validation-gap/validation-results/screenshots/study-2026-09-09/island-trunk.png)

## Two things about crossings and junctions

Three runs in game on one world state:

| | roads | length | crossings |
|---|---|---|---|
| neither | 49 | 21.7 km | 0 |
| fords only | 90 | 41.5 km | **3, all fords** |
| both | 98 | 56.8 km | 15 (13 bridges, 2 fords) |

Fords add 41 roads, and only three of them are river crossings — the rest is
the swamp wading the same flag enables. Bridges then add eight more with
thirteen bridges. If ford geometry is due for work, that ratio is worth
knowing first.

Separately: nothing in the cost model lets a road follow an existing one. The
only place an existing road is consulted is a shared river crossing at half
price; ordinary road has no discount, so two roads to nearby places run side
by side and junctions only happen by accident. I tried adding the discount and
it does nothing, even making existing road free within 40 m — an 8 m step over
ordinary ground costs about 8 while the penalties shaping a route are 1 000 to
100 000, so discounting the cheap part cannot pull a route sideways. Junctions
would need a plan that attaches to a road, or a cost model where being
off-road is dear.

## Two bugs

Generating a whole world with bridges on threw and left the world with no
roads: two crossings on one road can overlap on the path — a bridge's banks
walk out to the bank tops, a swamp bridge's on to dry ground — and the
painting step assumed they never did.

Regenerating roads in a world that already has them skips the reset, because
the guard asks whether roads were *generated* this session and a loaded world
has roads without having generated them. Both networks end up in the terrain
and the old network's bridges keep their sites.

Both fixed with tests, the first on the bridges branch and the second in the
base code.

## What this can't tell you

Terrain is read back from a dump, so move costs are exact but ford depth is
interpolated — on your seed the game found 5 fords where the offline model
found none, so I would not conclude anything about fords from these numbers.
Aggregates match a real run to about one per cent and 92 % of roads follow the
same line, but individual roads can differ. And nothing here was played:
whether any of these networks is better to travel is a question for a session
in game.

Happy to run any configuration you want to see, or hand over the numbers.

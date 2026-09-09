<!--
DRAFT. Not posted. For issue jneb802/ProceduralRoads#7, on explicit go only.
Images resolve from the fork's docs branch; check they render before posting.
-->

I went looking for why roads come out sparse, and ended up somewhere other
than the iteration limit. Everything below is measured on generated networks,
not played — I've marked what that can and cannot settle.

**The short version: the per-island quota decides almost everything, and the
iteration budget decides almost nothing.**

Taking every place in the world on your seed `nRleKzu9bI`, default settings
otherwise, and following what becomes of it — of the 2 470 places that are
eligible for a road:

| outcome | places |
|---|---|
| lost the island's quota | 2 312 |
| connected | 128 |
| attempted, no route exists | 29 |
| attempted, iteration budget spent | 1 |

Ninety-three per cent never get an attempt at all. The same holds on two other
worlds I checked.

The quota is `2 + area / 2 km²`. That seed's median island is under 2 km², so
more than half the islands are capped at two places — one road. That is the
"2-3 roads per island" in the title, and it is arithmetic rather than a bug.
`MaxLocationsPerIsland` is not the lever it looks like: raising it from 12 to
100 took the world from 88 roads to 91, because the area formula binds long
before the ceiling does.

**The plateau around 30 000 iterations is real, and here is what causes it.**
Splitting failures by the pathfinder's own two reasons:

| iterations | roads | budget spent | no route exists |
|---|---|---|---|
| 5 000 | 62 | 48 | 48 |
| 20 000 | 78 | 19 | 61 |
| 30 000 | 84 | 8 | 66 |
| 120 000 | 88 | 1 | 69 |

As the budget grows, attempts that used to stop at the cap instead run to
completion and report that the destination cannot be reached over land. Past
about 30 000 there is nothing left for a bigger budget to rescue.

**What a player actually gets.** The largest island on that seed is 26.7 km²
with 426 eligible places on it. It gets 12 selected, 6 roads, 5.2 km of road,
in three separate networks. Across the world, 22 of 67 islands get no road, 22
get exactly one, and the longest connected run of road anywhere is 4-6 roads.
These worlds are archipelagos and roads do not cross open sea, so no routing
change moves that ceiling much.

![the world as it generates today](https://raw.githubusercontent.com/tvongaza/ProceduralRoads/docs/validation-gap/validation-results/screenshots/study-2026-09-09/issue7-shipped.png)

**On your PR #16.** I ported it onto the same base so it could be measured on
the same terrain, then changed one thing at a time. The endpoint quota
(priority, then nearest) is the biggest single gain: 123 places served → 142.
Anchoring on a location instead of a coast cell serves the same number with a
third of the wasted attempts — today 36 of 158 attempts start on a coast cell
and die on the first step, because the anchor is not ground a road can stand
on. Ring-balanced island selection is the one part that costs coverage on
these worlds: at 50 % of islands it serves 86 where largest-first serves 101.

**Some alternative connection plans**, all on identical inputs — same islands,
same selected places, same anchor, same budget:

| plan | roads | length | places served | separate networks |
|---|---|---|---|---|
| chain/MST by island parity (today) | 88 | 58.5 km | 123 | 54 |
| tree grown outward with retries (#16) | 90 | 61.9 km | 126 | 56 |
| MST on routed cost | 90 | 57.6 km | 126 | 56 |
| trunk and spurs | 85 | 62.1 km | 116 | 47 |

Planning on routed cost rather than straight-line distance reaches as much for
the least road. Trunk and spurs makes the fewest separate networks — one road
along the island with things joined to it — and reaches about a tenth fewer
places. The pictures show the difference better than the table does:

![short branches, today](https://raw.githubusercontent.com/tvongaza/ProceduralRoads/docs/validation-gap/validation-results/screenshots/study-2026-09-09/island-parity.png)
![one road along the island](https://raw.githubusercontent.com/tvongaza/ProceduralRoads/docs/validation-gap/validation-results/screenshots/study-2026-09-09/island-trunk.png)

**One bug, separately.** Generating a whole world with bridges on threw and
left the world with no roads: two crossings on one road can overlap on the
path, and the painting step assumed they never did. Fixed with tests in the
bridges branch.

**What this cannot tell you.** The terrain is read back from a dump, so move
costs are exact but ford depth is interpolated — on that seed the game found 5
fords and the offline model found none, so I would not conclude anything about
fords from it. Aggregates match a real run to about one per cent and 92 % of
roads follow the same line, but individual roads can differ. And nothing here
was played: whether any of these networks is better to travel is a question
for a session in game, not for a table.

Happy to run any configuration you want to see, or to hand over the numbers.

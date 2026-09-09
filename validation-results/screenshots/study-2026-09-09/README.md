# Road network study, 9 September 2026

Pictures for `docs/road-network-strategies.md`. All from the issue #7 seed,
generated offline on terrain dumped at the pathfinder's own 8 m spacing, with
fords and bridges enabled and every island selected.

Land is drawn one pixel per 8 m cell. Water is three different things: a
swamp's shallows, a river, and the sea — a swamp sits at and below the
waterline and is where the mod wades on purpose, so drawing it as ocean made
every swamp road look like a road into the sea.

Roads: black where the road is ordinary, green where it wades a swamp, orange
where it stands in water anywhere else, teal for a waded ford, dashed purple
for the unpainted span of a bridge.

| file | what it shows |
|---|---|
| issue7-shipped.png | the whole world under the policy that ships today: 88 roads, 54.0 km of distinct road |
| issue7-reachable.png | the same world under PR #16's policy |
| island-parity.png | one island, chain/MST by island parity — short branches around the start |
| island-trunk.png | the same island, trunk and spurs — one road down the length of the chain |
| island-routed-mst.png | the same island, MST on the search's own cost |
| island-sheet.png | four connection plans on one island, at identical bounds and scale |
| island-all-places.png | the same island with the quota lifted: every eligible place |
| six-islands.png | six islands at identical bounds and scale, study baseline |
| world-sheet.png | the whole world under six configurations, at identical bounds and scale |
| island-diagnostic.png | places drawn by what became of them, and failed attempts drawn to where the search stopped |
| failure-anchor.png | a coast anchor 150 m out to sea: one cell settled, sixteen moves refused, no road |
| failure-frontier.png | a search that settled a whole archipelago and still found no way to its destination |
| failure-budget.png | the one attempt that hit the iteration cap, including the part it spent over open ocean |
| chart-quota.png | places served against the per-island quota |
| chart-plateau.png | roads and road length against the iteration budget |
| chart-funnel.png | eligible places, selected places, places served |
| chart-shore.png | how far roads sit from open water |
| chart-centre.png | how far roads sit from their island's centre |

The charts and the failure views carry the SVG they were rendered from
beside them; the world and island views do not, because those SVGs run to
several megabytes each.

The island views share bounds and scale, so they can be read side by side. The
failure views do not: each is framed on the attempt it shows, and its caption
carries the coordinates and the numbers.

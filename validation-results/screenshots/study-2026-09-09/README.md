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
| issue7-shipped.png | the whole world under the policy that ships today: 88 roads, 58.5 km |
| issue7-reachable.png | the same world under PR #16's policy: 96 roads, 34.6 km |
| island-parity.png | one island, chain/MST by island parity — short branches around the start |
| island-trunk.png | the same island, trunk and spurs — one road down the length of the chain |
| island-routed-mst.png | the same island, MST on routed cost |

The island views share bounds and scale, so they can be read side by side.

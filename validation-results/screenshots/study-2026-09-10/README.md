# Planner comparison, 10 September 2026

Pictures for section 4 of `docs/road-network-strategies.md`. All from world A
(seed `gqZ5SrFUjk`; the world is named `Issue7` in the run files but it is
**not** issue #7's own seed), generated offline on terrain dumped at the
pathfinder's own 8 m spacing (Valheim 0.221.12, buildid 21981559), with fords
and bridges enabled and every island selected.

Land is drawn one pixel per 8 m cell. Water is three different things: a
swamp's shallows, a river, and the sea. Roads: black where ordinary, green
where wading a swamp, orange where standing in water elsewhere, teal for a
waded ford, dashed purple for the unpainted span of a bridge.

| file | what it shows |
|---|---|
| world-planners.png | the whole world under the four shortlisted plans, identical bounds, scale and legend |
| island42-planners.png | the flattest island over 3 km2 (mean gradient 0.18) under all four plans |
| island58-planners.png | the steepest island over 3 km2 (mean gradient 0.37) under all four plans |
| island54-planners.png | the island with the most coast for its area (0.74 edge cells per land cell) under all four plans |
| chart-tradeoff.png | places served against distinct road, per plan and per world, with generation time in its own panel |
| chart-coverage.png | places served by category, four plans, with boss altars marked as the required ones |

The three islands were chosen on the measured terrain in
`../../study-2026-09-10/Issue7-island-terrain.txt`, not by eye. The charts
carry the SVG they were rendered from; the world and island views do not,
because those SVGs run to several megabytes each.

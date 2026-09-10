# Study data, 10 September 2026

The runs behind the planner comparison in `docs/road-network-strategies.md`, so
a claim in it can be checked rather than taken. This directory supersedes
`study-2026-09-09` for section 4 and the corrections in Appendix E; the earlier
directory still holds the funnel, plateau, clustering and quota data that were
not re-run.

## What is here

| file | what it holds |
|---|---|
| `runs/<world>.<plan>.<rep>.manifest.json` | one run: its id, the code commit **and whether the tree was dirty**, the build configuration, the runtime and platform, a content hash of every input, the settings, four stage timings and every result metric |
| `runs/<world>.<plan>.r1.places.csv` | one row per place in the world: category, priority, island, and whether it was eligible, selected, planned-and-built, served and served-with-the-boundary-resolved, with the distance to the nearest road end |
| `runs/<world>.<plan>.r1.attempts.csv` | every search, with the connection it belongs to, the role it played, the island it was on, and the search's own account of what stopped it |
| `runs/<world>.<plan>.r1.islands.csv` | one row per island that generated: area, ring, candidates, selected, roads, searches, cells settled and **seconds** |
| `runs/<world>.<plan>.r1.selection.csv` | what each island offered its quota and what the quota took |
| `runs/<world>.<plan>.r1.crossings.csv` | the river crossings of that run, kind, style, banks and depth |
| `runs/iters-<n>.*` | the iteration sweep, 5 000 to 120 000 |
| `runs/fb-<kind>.*` | the fallback sweep, with the duplicate search removed |
| `Issue7-island-terrain.txt` | mean height gradient, bounding-box fill and coast-per-area for every island over 3 km2, and the three the comparison sheets use |

Worlds are `Issue7` (the issue's seed, `gqZ5SrFUjk`), `RoadTestMac2` ("world B")
and `RoadTestAuto1` ("world C"). Plans are `parity` (the shipped plan),
`routed-mst`, `trunk` and `reverse` (POI-to-network search). Repetitions are
`warm` (a warm-up, never timed) and `r1`-`r3` (measured); only `r1` publishes
its tables, because the three repetitions are byte-identical.

## Reading a runtime

`seconds` and `generateSeconds` cover **generation only**. `loadSeconds` is
reading the terrain dump, `analysisSeconds` measuring the finished network and
`exportSeconds` writing these files. A runtime from one manifest may only be
compared with a runtime from another when `buildConfiguration`, `runtime` and
`platform` agree: on this workload a Debug build is 6.4x a Release one, and two
runs that did not record it were once compared as though it were free.

## Counting searches

Three different numbers, and the study needs all three. `connectionCount` is
decisions the plan made. `buildSearches` is rows in the attempt log — one plan
writes two rows for one connection, so this is not a connection count.
`planningSearches` is searches a plan ran to price a candidate edge; they lay
no road and appear in no attempt row. `totalSearches` is the work.

Route geometry is not published: several megabytes per run, and it regenerates
from the manifest, which names the code and the inputs by content hash.

# Study data, 9 September 2026

The tables behind `docs/road-network-strategies.md`, so a claim in it can be
checked rather than taken.

| file | what it holds |
|---|---|
| `Issue7-outcomes.csv` | one row per place in the world: where it is, its priority, its island, and whether it was eligible, selected, attempted, connected, with the reason when it was not |
| `Issue7-per-island.csv` | one row per island: area, ring, places offered and selected, roads, length, places served, joined groups, attempts and how they ended |
| `Issue7-islands.csv` | island detection alone: cells, area, ring, eligible places, the per-island cap |
| `Issue7-clustering.csv` | distance to open water and from the world centre, for road points and for the land itself |
| `Issue7-dump-provenance.txt` | which machine, build and plugin set produced the terrain dumps |
| `runs/<run>.manifest.json` | a run's id, the study commit, content hashes of every input, its settings and its results |
| `runs/<run>.attempts.csv` | every connection attempted in that run and how it ended, with the search's own account |
| `runs/<run>.selection.csv` | what each island offered its quota and what the quota took |
| `runs/<run>.crossings.csv` | the river crossings of that run, kind, style, banks and depth |

Route geometry is not published: it is several megabytes per run and can be
regenerated from the manifest, which names the code commit and the exact
inputs by content hash.

The `q4-*` runs answer four questions about the plan: whether a plan can see
water (`q4-plan-routed-mst`, `q4-rmst-n*`), whether roads can join by design
(`q4-plan-trunk`, `q4-plan-grow`), whether a place can reach for the network
instead of the other way round (`q4-plan-reverse`), and whether a failed link
can fall back to something nearer (`q4-fb-*`). `q4-baseline` is the control
they share. Their manifests carry three metrics the others do not:
`teeJunctions` (roads ending on another road's length), `endToEndJoins`, and
`parallelRoadMeters` (road running within 12 m of other road without joining).

Runs here: `check` is the study baseline (crossings on, every island),
`v2-*` are the five connection plans on identical inputs, and `anchor-land`
and `anchor-poi` are the two alternative anchor rules against that same
baseline. The plan runs use
the corrected implementations — an MST on the search's accumulated cost, and
spurs that attach to a point on a road rather than to another place.

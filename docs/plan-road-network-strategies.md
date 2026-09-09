# Plan: road network strategies study

Received 8 Sep 2026 from another agent, revised the same evening against
the code (master 718f4ef, the #19-#24 stack at 5832a91, the author's PR
#16), the harness, the existing dumps and tooling, and the directive in
TODO.md item 5. The deliverable is a STUDY for the author (jneb802),
labelled "routing study; gameplay validation pending", ready to post on
issue #7 and in the Discord dev channel on explicit go: pictures at world
and island scale, numbers per island, strategies presented as options.
Not mod code. Mod changes come later, in their own PRs, after the
author's preference has been checked in-game.

The revision keeps the received structure. Changes are marked "Revised:"
where the reasoning is not obvious; the rest is tightened in place. A
second review round (same evening) corrected six points: the offline
terrain model is approximate and says so; branch equivalence is a
canonical diff, not a hash; budget-limited attempts are measured
outcomes, not discarded runs; the plateau reading and the failure
instrumentation are specified; controls and identical-input experiments
are two tracks; the POI priority contract is written out.

## Objective

Build a reproducible study of road-network strategies, with world-scale
and island-scale maps, per-island measurements, and an explanation for
every missing connection. Present two to four strategies as options with
their tradeoffs (gameplay quality, routing success, cost); the author
decides.

Separate three decisions throughout:

1. Which islands and POIs receive roads?
2. How are the selected destinations connected?
3. Are the resulting connections useful and traversable in-game?

Every strategy comparison uses the same selected destinations, terrain,
crossing rules and search budget. A strategy must not look better because
it chose easier destinations.

## 0. What the current code does (measured baseline, master 718f4ef)

Recorded here so the study measures the real thing, not a paraphrase.
Every item below is a candidate cause for the #7 observations and each is
a one-line change; none of them is changed inside the study.

| Stage | Master behaviour | Where |
|---|---|---|
| Island detection | `GetBaseHeight >= 0.05` on a 128 m grid over the 20 km world, flood fill, islands under 10 cells dropped, ids assigned in scan order | IslandDetector.cs |
| Island selection | sort by area, take `round(count * IslandRoadPercentage/100)` largest | RoadNetworkGenerator.GenerateRoads |
| Eligible POIs | boss list + a fixed priority table + registered/custom names; anything else is not a road location | LocationPriorities, IsRoadLocation |
| Per-island cap | `2 + area / 2 km²`, clamped to [2, MaxLocationsPerIsland=12] | GetMaxLocationsForIsland |
| POI selection | by priority descending, ties in ZoneSystem order, truncated to the cap. Bosses 100, Crypt/SunkenCrypt/MountainCave 80, custom 80, Mistlands town/harbour/towers/excavations 45-75, WoodVillage 60, WoodFarm 55, StoneTower 40, ruins/henges/swamp 25-30, default 20 | SelectLocations, GetLocationPriority |
| Anchor | spawn (StartTemple) on the starter island; on every other island the island cell nearest its own bounding box, radius 0. That is a road that ends on a coast cell with no POI | GenerateIslandRoads, Island.GetEdgePoint |
| Strategy | `island.Id % 2 == 0` -> Prim MST on straight-line distance from the anchor; odd -> nearest-neighbour Chain from the anchor, advancing to the next POI even when the road failed | GenerateMSTRoads, GenerateChainRoads |
| Route search | A* on an 8 m grid, 16 directions (knight moves), `PathfindingMaxIterations` default 10000 (config), raw `GetHeight` for slope and water, `GetRiverWeight` weight for the river block (cost >= RiverPenalty 100000 = impassable). The failure log already distinguishes `no reachable path` (frontier exhausted) from `max iterations reached` (budget) | RoadPathfinder.FindPath |
| Failure handling | none: a failed edge is dropped, no alternate is tried | GenerateRoad callers |

The author's open PR #16 (feature/warp-71, `GenerateReachableRoads`)
already changes several rows: island selection balanced over three world
rings (largest first inside a ring), endpoints filtered to POIs with
nearby pathable land, `MaxRoadLinkDistance` 2200 m, at most 24 failed edge
attempts per island, tree grown outward from the anchor with retries, no
arbitrary edge anchors, `ProceduralRoadsAPI` priorities. It is the author's
own answer to #7 and it is in the study as a candidate, not ignored (see
section 4). The harness already knows it (`StrategySupport`,
`ReachableRoadsTests`, skipped on bases without it).

## 1. Reproducible baseline and fixtures

Revised: the received plan started from "the reviewed bridge branch at
7686491". That commit is dangling (the pre-amend version of c43711e); the
bridges tip is 5832a91. More important, the author ships master, so the
study's control is master.

- Code bases, each recorded by commit in every run manifest:
  - **master 718f4ef**: the control and the base for reproducing #7.
  - **#24 stack tip 5832a91** (pr/wood-bridges, contains #19-#24; Fords
    and Bridges off by default): the crossings axis. Before using it as a
    stand-in for master, compare the two NETWORKS canonically with both
    flags off: island list and assignments, selected POIs, attempted
    edges in order, and per route the point positions, heights and
    widths (routes.csv diff with a tolerance stated in metres). Version
    hashes are not the test: the fords PR hashes `paintOnly`, master does
    not, so the hashes differ on identical geometry. One fixture is a
    smoke test; run it on all three seeds before calling the stack a
    control, and if anything differs, master is the control everywhere
    except the crossings runs.
  - **#16 feature/warp-71**: the author's candidate. Build it in its own
    worktree; if it does not build on today's Valheim assemblies, record
    that and rebase a copy privately for the study only (never pushed as
    ours).
- Record in every manifest: mod commit and DLL hash, game version, world
  seed and fixture md5, POI list source (vanilla / MWL / custom config),
  cfg values as READ BACK from the file after the run (BepInEx clamps
  silently), strategy and its parameters, `PathfindingMaxIterations`,
  any per-island attempt cap, generation order, crossings flags, and the
  generation scope (global / one island / point-to-point).
- Reproduce issue #7: seed `nRleKzu9bI`, `IslandRoadPercentage = 100`,
  default cfg otherwise, then the issue's CustomLocations list, then
  `PathfindingMaxIterations` 20000 and 100000. This world does not exist
  yet: create it once (Mac or PC, `world-fixture.sh`), dump it, keep the
  pristine .db/.fwl as a fixture. The literallybyronic numbers (80
  islands, 24 roads) were with MWL POIs registered; reproduce that only if
  MWL installs cleanly on the station, else state it as not reproduced.
- Two more seeds for generalisation: **RoadTestMac2** and **RoadTestAuto1**
  (both already dumped at 50 m in validation-results/, both with selftest
  baselines and hashes). No new worlds needed for these.
- Historical vs current: #7 is from March 2026. `PathfindingMaxIterations`
  was added on 2 Mar 2026 (987daa8, the day the author replied on #7) with
  `AcceptableValueRange(1000, 100000)`, unchanged since. So the reporters'
  100K setting was NOT clamped; only values above 100K are (our own 200K
  runs were, until pc-run pinned the cfg). Report historical and
  current-code results as separate rows.
- Synthetic fixtures stay (SyntheticWorld): flat, deep and shallow river,
  mountain, 16 m and 40 m gullies (the pathfinder's knight move steps over
  anything under 16 m), narrow land bridge, isolated POI, shoreline POI,
  a POI with two usable entrances.
- Fixtures are immutable. Each strategy run starts from an empty
  RoadSpatialGrid; a run that reuses another run's roads is a different
  experiment and says so.

### Terrain export (Revised twice: what is exact, what is approximate)

What the mod samples, and where:

| Consumer | Query | Positions |
|---|---|---|
| IslandDetector | `GetBaseHeight` | fixed 128 m grid, `x*128 - 10000` |
| Pathfinder move cost | raw `GetHeight`, `GetRiverWeight` (weight, width) | 8 m cell centres, multiples of 8 m |
| Pathfinder terrain variance | raw `GetHeight` | ring of 8 at radius 16 m around the cell: four on-grid, four at +-11.3 m (off-grid) |
| Ford / bridge depth | `BiomeBlendedHeight` | every 2 m along the crossing (off-grid) |

So the offline world is exact for island detection and for cell-centre
costs if the dumps are aligned to those grids, and APPROXIMATE for the
variance ring and every crossing-depth judgement. Consequences:

- `cli_world_dump [step] [dir]` (valheimCLI, the author's tool) writes
  `x,z,height,biome,river` over the whole map, unaligned. Add
  `base_height` and `river_width` columns and a `--window cx,cz,half`
  option in a small valheimCLI PR (generic tooling belongs there); until
  it merges, run the fork build on the station. Dumps: the 128 m island
  grid at its own sample positions (exact detection); each study island
  as an 8 m window aligned to multiples of 8 (exact cell centres); the
  whole map at 50 m for pictures only.
- Harness side: `CsvWorld : WorldGenerator` in the test project. On-grid
  queries return the sample; off-grid queries interpolate bilinearly and
  the run manifest carries `terrain=approx`. Outside the tile is an
  error, never a guess.
- What approximate buys and what it does not: approximate runs COMPARE
  strategies (same terrain model for every strategy, so the comparison is
  fair). They do not ESTABLISH the cause of an in-game failure; a causal
  claim about a specific in-game route or crossing needs the same case
  confirmed in-game (one island regenerated on the station) or the
  in-game log line quoted.
- Calibration, not proof: on RoadTestMac2 run the master baseline on
  CsvWorld and diff against the in-game routes.csv of the same network
  version (canonical diff, tolerance in metres, per-route). Report the
  agreement rate and the largest deviations with their cause (variance
  ring, biome blending, crossing depth). A high agreement rate is what
  lets the approximate runs stand in for the game in the comparisons;
  the disagreeing routes are excluded from causal claims.

## 2. Island and POI selection controls

Unchanged in intent; the controls are study-side (the harness picks the
set and hands it to every strategy), not mod config.

| Control | Choices |
|---|---|
| Island scope | one island, explicit list, all, percentage (master's rule), ring-balanced (#16's rule) |
| POI quantity | exact count, percentage, all eligible, master's `2 + area/2 km²` cap |
| Selection pattern | nearest pair, farthest pair, spread, clustered, priority first (master), seeded random |
| Manual | pin required, exclude specific |
| Anchor | none, spawn, a selected POI, master's edge cell, #16's nearest pathable point |
| Failure policy | keep the set; optional substitution, recorded |

- Stable POI identity: `name#index` by ZoneSystem order plus position, so
  two `WoodVillage1` are two rows.
- Requested, selected, attempted, connected: four counts, never merged.
- Nearest/farthest by horizontal distance; routed distance reported
  separately.
- No silent substitution of an unreachable destination.
- Zero POIs: nothing to do, row still in the report. One POI: needs an
  anchor or it is a row with "no partner".
- Selection is fixed while comparing strategies; selection policies are
  their own experiment (section 7 needs it).

## 3. POI importance and selection frequency

Revised: written out so the document stands alone. Master's priority
table (section 0) is already a category policy; the study's presets are
overrides on that table so the author can read them as config.

Three controls, kept distinct:

- **Required**: the POI must enter the selected set (bosses in every
  preset). Required beats the count cap: three bosses with a cap of two
  means three selected POIs, and the row says why.
- **Selection frequency**: whether an optional POI enters the candidate
  pool this run (for example dungeons 50 %, small builds 10 %). A
  deterministic draw per POI, see identity below. A POI that lost the
  draw is recorded as such, distinct from one cut by the quota.
- **Priority**: which optional candidates fill the remaining quota, and
  which selected POIs get connection attempts first. Priority never
  changes the per-attempt search budget; if a later experiment gives
  required POIs more retries, that is a named policy with its own runs.

Selection order: island scope and eligible POIs; explicit exclusions and
required-category rules; every required POI; frequency draws over the
optional POIs; fill the quota by pattern and priority. Exact manual
selection is a separate mode that bypasses the category rules for
controlled experiments and is labelled as such.

Rules:

- "Always" means always selected and attempted, never guaranteed
  connected. A required POI that ends disconnected is the first line of
  the island's row and of the per-run summary, not a footnote.
- Required categories do not widen island scope; an explicit "all
  islands with a boss" preset does.
- POI identity for draws and for tables is `type + rounded position`
  (metre resolution) with a collision suffix, NOT the ZoneSystem index:
  inserting or reordering locations (MWL, a config change) would reroll
  every destination otherwise. The draw value is a hash of (world seed,
  identity), so changing one threshold does not reroll the rest.
- Unknown and modded location types fall into a visible default category
  (master's default priority 20) with a per-type override.

Presets compared: boss routes only; bosses plus some dungeons; broad
exploration network. Results reported separately for required, high
priority and optional POIs.

## 4. Strategies to compare

Revised: the received table missed the author's #16 and listed "grow the
network" as if new; #16 is a grow-the-network with retries and endpoint
filtering. Present it as the author's candidate and our variants as
variants of it where they are.

| Strategy | Study implementation | Question |
|---|---|---|
| Master Chain/MST (control) | the real code through reflection (`RoadTopologyTests` pattern), edge anchor and parity kept | what improves over today? |
| Author's #16 reachable roads (control 2) | the real code of feature/warp-71 through reflection | how much of #7 does the author's own PR already fix? |
| Trunk + spurs | route the two farthest routable anchors, attach the rest to the nearest trunk point (junction on the road, connectivity checked in the grid, not by picture) | does the island get the long road and the T/X junctions users asked for? |
| POI graph on routed costs | route candidate pairs (Gabriel or RNG neighbours plus k nearest, capped), MST/forest on the ROUTED cost, not straight-line | can destinations be connected economically without straight-line lies? |
| Hub and spoke | spawn or boss as hub, k nearest major POIs, secondary hub for a far cluster (Saulc's "spawn -> one boss, other links free") | navigation vs backtracking |
| Tree + useful loops (stretch) | add an edge to a tree when it shortens a journey by more than X% | which loops pay for their footprint? |

Two tracks, never mixed in one table (Revised):

- **Track A, as shipped / as proposed**: master and #16 run unchanged
  through reflection, each with its own island selection, entrances,
  link limits and retries. Their difference measures the whole policy
  package, and is reported as that.
- **Track B, identical inputs**: the topology strategies (and master's
  Chain and MST as topologies) run on the SAME selected POIs, anchors,
  routing rules, crossing flags, per-attempt budget and retry policy.
  Only the connection plan differs. Gains are attributed to a factor
  only through the one-factor experiments of section 7, never by
  assigning the Track A difference to "grow the network".

Rules (unchanged in substance): one routing engine and cost config;
selection / planning / search / realisation as separate steps; candidates
and their outcomes exported before the network is committed; a connection
counts only when the route exists and both ends attach; alternates tried
after failure; components preserved; shared segments counted once;
deterministic ordering; candidate-generation limits reported; on small
diagnostic islands evaluate all pairs to expose what the sparse graph
missed.

Where the code lives: in the harness (ProceduralRoads.Tests or a sibling
ProceduralRoads.Study console project on the same shims), calling
`RoadPathfinder` and `IslandDetector` directly, the two controls through
reflection. Nothing in ProceduralRoads/Src changes for the study. If a
strategy later becomes a PR it is rewritten for the mod under the PR
hygiene rules; the study code is evidence, not the product.

## 5. Explain every missing POI and failed route

Unchanged in intent. The decision trail per POI:

`discovered -> island -> eligible -> selected -> attempted -> routed -> component -> in-game`

with the received outcome list. Two additions:

- The pathfinder's own two reasons (`no reachable path`, `max iterations
  reached`) are the primary split; everything finer (crossing rejected by
  depth / span / bank / biome / flag, geometry rejected after trimming) is
  read from the crossing detector and RoadEndReport, not inferred.
- Budget semantics (Revised, replaces the earlier "TRUNCATED runs are
  not comparable"):
  - An attempt that hit the per-attempt budget is a MEASURED outcome:
    `budget exhausted`, reported as its own column beside `connected`
    and `frontier exhausted`. Strategies are compared under equal
    budgets, and the comparison shows both success and exhausted
    attempts.
  - A run stopped by an experiment-level safety limit (total work, wall
    clock, memory) is INCOMPLETE and is not compared.
  - "Unreachable" and "no violations" are claimed only for searches that
    exhausted their frontier under the stated rules (the 2 Sep lesson: a
    budget-limited PASS hid 13 violations a larger budget exposed). A
    budget-exhausted attempt says nothing about reachability.

Per attempt keep: endpoints, snapped 8 m cells, entry candidates,
expansions, remaining frontier, elapsed, closest approach, rejection
counters by cause, route cost, termination reason. Full traces only for
selected failures. Diagnostic retries change one factor at a time and are
labelled diagnostic.

## 6. The ~30K iteration plateau

Order of work (Revised: cheapest evidence first):

1. Config history: checked. The range has been 1000-100000 since the
   option appeared (987daa8), so a 100K setting reaches the pathfinder;
   hypothesis 6 (configured vs effective limit) is out for the reporters
   (BepInEx clamps out-of-range values to the bound, silently; 100000 is
   the bound, so it passes). Keep the read-back-cfg rule anyway.
2. Read the failure logs of a current 100K run on the #7 world. A
   `no reachable path after N` line means the frontier emptied after N
   pops; N is an upper bound on the unique cells expanded, and N x 64 m²
   is a nominal grid footprint, not measured land (expanded cells include
   steep and penalised ones). If the Ns cluster near 30K the reporters
   were most likely watching frontier exhaustion (hypothesis 1); confirm
   by recording unique expanded cells and the bounding region of the
   closed set with the probe below, and by drawing that region on the
   island inset.
3. Then the sweep as received: fixed POIs and fixed pairs at 5K, 10K,
   20K, 30K, 60K, 120K per attempt, plus a total-work cap that reports when
   it truncates. Pairs first (independent), whole network second (earlier
   roads legitimately change later costs). Plot connected POIs, successful
   routes, frontier- vs budget-exhausted, expansions and time, and the
   identities of routes that flip.
4. Hypotheses 2-5 as received (no alternates tried; bad entrances;
   rivers/terrain/biome separate the destinations; island or POI filtering
   removes them before routing).

`RoadTimings` / `road_timings` (#22) already count attempts, failures by
reason and iterations; use them for the aggregate numbers.

Instrumentation at the decision points (Revised): the crossing detector
runs on SUCCESSFUL paths, so it cannot explain a jump that
`TryGetRiverCrossing` rejected (depth, span, bank delta, biome, flag) or a
move rejected as impassable. Those rejections have to be counted where
they happen, inside the search. How, without changing production
behaviour: a study-only probe interface on `RoadPathfinder` (a static
field, null by default, one null check per decision point; no allocation,
no behaviour change when null) on a study branch of the mod, never part
of a PR unless the author asks for it. Neutrality is verified, not
assumed: routes on the three seeds identical with the probe installed and
absent (canonical diff), and the per-attempt iteration counts identical.
The probe records per attempt: unique expanded cells, closed-set bounds,
closest approach, and rejection counters by cause. If the plateau does
not reproduce on current code, name the build, fixture or setting that
differs.

## 7. Edge clustering and missing island coverage

Two different "edges" (unchanged): shoreline vs world boundary. Measure
distance to shore and distance from world centre for every road point.

Candidate contributors from section 0, tested one at a time with the rest
fixed:

- Edge anchor on every non-starter island (roads to a coast cell).
- Largest-first island selection (outer-ring biomes have the largest
  landmasses).
- The priority table: twelve Mistlands entries at 45-75 vs a handful of
  Meadows/BlackForest entries; with MWL registered everything custom is 80.
- Per-island cap `2 + area/2 km²` (why "2-3 roads per island").
- Priority truncation with ZoneSystem-order ties.
- Parity strategy choice.
- 128 m island sampling and the 10-cell minimum; POI-to-island assignment
  on shores and narrow necks (match islands across resolutions by overlap,
  not id).
- No alternates after a failed edge.

Because #16 changes the first two and adds a failed-attempt cap, the first
comparison is master vs #16 on the #7 world; the study then attributes the
remaining gap to the other rows. Normalise coverage by available, eligible
and selected POIs, island area and biome; separate intended bias (boss
first) from accidental.

## 8. Maps

Revised: the received plan specified an interactive viewer with click
diagnostics, synchronised panels and local rerouting. That is a product,
not a study, and it would eat the budget before a single picture reaches
the author. The deliverable is static pictures; interactivity is a stretch
after the author engages.

- Renderer: `scripts/world-svg.py` (world view + `--zoom` insets from
  world.csv / locations.csv / routes.csv). Extend it, do not replace it:
  POI state by glyph (available / eligible / selected / connected / failed,
  required vs optional), failed attempts as dashed lines to the closest
  approach, components by colour, candidate graph edges distinct from
  routed roads, fords and bridges marked, a caption with run id and cfg.
- Layout: one world image per strategy; one island inset per strategy per
  study island with identical bounds and legend, arranged side by side by a
  small script (SVG -> PNG via rsvg-convert or sips). Same six islands in
  every row.
- Static maps are the first study's deliverable. Routes, attempts,
  candidate edges and per-POI outcomes are saved as CSV/JSON per run so
  an interactive comparison can be built later from the same data
  without re-running anything.
- Stretch (only after the author asks): a single static HTML that shows
  the PNGs side by side with the per-POI CSV rows on hover. No rerouting,
  no alternatives browser.

## 9. Per-island numbers and gameplay evaluation

One row per island per run plus POI and attempt tables. Dimensions as
received (selection, connectivity, network shape, travel, terrain impact,
performance). Rules:

- Metric definitions are fixed and committed before any comparison run;
  a definition change is its own commit and re-baselines everything
  (the 40b7351 lesson: a metric change bundled with a generation change is
  unattributable forever).
- Reuse the selftest metrics where they exist (routeCount,
  networkComponents, fordCount, pointsHash) so the study's numbers line
  up with the gate's.
- Never average only successful journeys; disconnected pairs are a
  column. Connectivity broken down by required / high / optional.
- Every run states its scope (global / island / point-to-point) and
  whether roads were painted or only routed.

In-game (Revised: shortlist only, after the author has seen the pictures):
two or three strategies on matching sites with the readiness-based station
runner (`pc-run.sh`, one island regenerated, changed sites only). Judge
navigation, cart travel, approaches, visual plausibility, boat passage.
Distinguish planned / walking / cart / repair-required connectivity. Count
and explain every game-log warning in the round summary. Present tradeoffs,
not a single weighted score.

## 10. Efficient workflow

- Six study islands: starter, ordinary inland, river-divided,
  mountainous, shoreline-heavy, difficult outer-world; plus every island
  with zero eligible POIs in the coverage table.
- Diagnostics (sections 5-7) before the strategy matrix.
- Cache CsvWorld tiles and routed pairs keyed by (tile hash, cost config,
  crossings flags, endpoints, existing-network hash when costs depend on
  roads).
- Independent cases run concurrently; no shared mutable network state
  (RoadSpatialGrid is static: one process per case, or reset between cases
  and never in parallel inside one process).
- Cold and warm timings separate.
- Save routes and attempts so maps regenerate without re-pathfinding.
- Verify by exit code, never by grepping build output.

## 11. Milestones

Revised order: the study is the deliverable; mod code and the in-game
shortlist come after the author's reaction.

1. **Diagnostics and reproduction.** #7 world created and dumped; CsvWorld
   validated by route equality on RoadTestMac2; master and #16 controls
   running headless; per-POI outcome tables for the three seeds; the
   plateau explained (or the exact non-reproduction stated); clustering
   contributors measured one at a time.
2. **Strategies and pictures.** Selection controls; trunk + spurs, routed
   MST, hub and spoke (loops if cheap) in the harness; world images and
   six-island insets per strategy; the per-island table;
   `docs/road-network-strategies.md` drafted.
3. **Draft handoff.** The document is titled "routing study; gameplay
   validation pending" and presents candidates for in-game validation,
   not a strategy to adopt. Leak grep over the doc and images (no
   machines, names, sessions, agents). Posting on #7 and Discord is an
   outward action: it happens on explicit go, not as part of the
   milestone; the author's reaction, once there is one, goes to TODO.md.
4. **After the author's preference** (separate thread, own PRs): the
   preference selects two or three candidates for the in-game shortlist
   on the station; it does not finalise a strategy before that
   validation. Then the chosen strategy rewritten for the mod under the
   PR hygiene rules, tested in the harness; the one-line baseline fixes
   (edge anchor, cap, alternates) each as their own small PR if the
   author wants them independent of the strategy.

## Acceptance criteria

- Any reported run reproduces from its manifest and fixtures; the manifest
  names commit, DLL hash, seed, fixture md5, read-back cfg, scope.
- The CsvWorld calibration on RoadTestMac2 reports its agreement rate
  and every deviating route with a cause; runs are labelled
  `terrain=approx`; no causal claim about an in-game failure rests on an
  approximate run alone.
- Branch equivalence (master vs the stack with flags off) is shown by a
  canonical network diff on the three seeds, not by a hash.
- Every island and POI on the three seeds has an explicit outcome.
- Two nearby POIs, two distant POIs, all eligible, and intermediate
  selections compare directly on identical inputs.
- Master and #16 appear as Track A controls in every table and picture;
  Track B tables share identical inputs and say so in the caption.
- Budget-exhausted attempts are a reported column under equal budgets;
  incomplete runs (safety limit) are excluded; reachability claims cite
  frontier exhaustion.
- Required, frequency and priority controls behave as section 3 states;
  disconnected required POIs head their island's row.
- The plateau and the clustering each have an evidence-backed cause or a
  precise statement of what did not reproduce.
- World and island side-by-side images and the per-island CSV exist for
  every strategy on the six islands.
- The document contains no internals (grep list in memory
  "upstream-pr-hygiene"), is labelled "routing study; gameplay validation
  pending", and phrases no recommendation as a decision.

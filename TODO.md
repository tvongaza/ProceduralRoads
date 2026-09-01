# Fresh-Start Plan (2026-08-31)

Strategy: upstream now owns network topology (anchor/endpoint selection,
location priority API, route export). We own **path quality and crossings**
(`RoadPathfinder`, `RoadTerrainModifier`, debug tooling) — areas upstream is
not touching, so everything below stays merge-friendly and independently
shippable, potentially as upstream PRs.

## Design constraint: vanilla-data compatibility (Xbox / unmodded clients)

Everything the mod writes must be **vanilla world data** so unmodded clients
(Xbox crossplay) see the results. This already holds today:
`RoadTerrainModifier` writes `m_levelDelta` / `m_modifiedHeight` /
`m_paintMask` into `_TerrainCompiler` ZDOs — the same format as hoe/pickaxe
edits, which sync and persist for all clients. Keep it that way:

- Bridges/ruins (Tier 3): **vanilla prefabs only** (ruined stone/wood
  pieces). Custom prefabs are invisible or broken for unmodded clients.
- Road surfaces (Tier 2): **vanilla paint channels only**
  (paved/dirt/cultivated) — conveniently exactly what the biome-surface
  idea needs.
- Vegetation clearing must land as ZDO state from a modded peer generating
  the zone first (upstream `RoadVegetationCleaner` does this) — an unmodded
  client generating a virgin zone spawns trees on the road corridor.

## Base

DECIDED (2026-08-31): build feature work off warp-71. Probe results: both
upstream branches merge cleanly onto master with zero conflicts; the harness
compiles against the merged code with minor shim additions (committed); the
pathfinder tests pass unchanged. `integration/upstream-prs` = this branch +
PR #16 + PR #17 — Tier 1 work happens there. The harness-first PR still
targets master (tests what master has today); its tests are base-agnostic
(legacy Chain/MST tests no-op post-warp-71, GenerateReachableRoads tests
no-op pre-warp-71). Watch PRs #16/#17 for rebases before building further.

- [x] Branch from `origin/master`, then evaluate merging upstream branches:
  - `origin/feature/warp-71-mwl-road-api` — island anchor selection,
    nearest-pathable-point, priority-registered locations (Jul 2026).
    Supersedes our old "POI as island root" / "safe start point" work.
  - `origin/feature/road-route-export` — `RoadRoute` ordered centerlines,
    route persistence, vegetation clearing, `road_pins` commands (Jul 2026).
  - `origin/fix/WARP-54-road-copy-regeneration`, `origin/fix/ptb-zone-system-vector2s`
    — small fixes worth carrying.

Reference for porting: branch `wip/june-2026-pathfinding-terrain` holds the
June work. Do NOT port its two known bugs:
1. A* open-set update in `RoadPathfinder.FindPath` re-reads `gCosts` after
   overwriting it, so stale open-set entries are never removed (use the
   already-captured `existingG`).
2. `GetSafeIslandStartPoint` calls `island.ContainsPoint(Vector2)`, which
   implicitly converts to `Vector3` with z=0 — tests the wrong world point.
   (Moot anyway: superseded by warp-71 anchors.)

## Test harness (spiked 2026-08-31 — `ProceduralRoads.Tests/`)

Headless xunit project (net48 under Mono + net10.0) that compiles the real mod sources against
tiny shims for Unity math, `Vector2i`/`Heightmap.Biome`/`WorldGenerator`, and
BepInEx logging — no game, no Unity, runs in <1 s. `SyntheticWorld` provides a
deterministic pseudo-Valheim island (dome + mountain ridge + meandering
river); `WorldRenderer` draws world + paths + markers to a BMP for visual
inspection. Run via `ProceduralRoads.Tests/run-tests.sh` (both runtimes) or
`dotnet test -f net10.0` (fast loop; bare `dotnet test` aborts on the net48
target on macOS). net48 runs under Homebrew Mono 6.14 — closest available
match to Valheim's in-game Unity Mono runtime. Includes a characterization test pinning the current "river makes
far side unreachable" behavior — flip it when the cost-model rework lands.

First-PR scope decision (2026-08-31): the first upstream PR is this harness
characterizing existing behavior — before any new road code. Now also
compiles `RoadNetworkGenerator`/`RoadSpatialGrid`/`BiomeBlendedHeight`/
`IslandDetector` and pins the Chain/MST topology strategies (hub/spoke vs
path, greedy-vs-MST length, orphan roads after failed edges).

- [ ] Extend harness to cover `RoadSpatialGrid` smoothing and
  `RoadTerrainModifier` blend math (add shims as needed).
- [ ] Grow `SyntheticWorld` scenarios per feature (swamp lowlands for wading,
  narrow-vs-wide river for crossings, steep dome for switchbacks).
- [ ] **Deferred until debugging needs it — real-terrain fidelity, staged:**
  1. `road_dump_world <center> <size> <step>` console command sampling real
     `GetHeight`/`GetBiome`/`GetRiverWeight` over a grid to a file, plus a
     `FixtureWorld : WorldGenerator` shim replaying it with bilinear interp.
     ~2–4 m step suffices for 8 m pathfinder cells. Turns any bug report
     into a permanent regression test (e.g. issue #7's seed `nRleKzu9bI`).
  2. Only if tuning-by-statistics over many seeds becomes the goal: port the
     real worldgen pipeline (Unity PerlinNoise = classic Perlin, Unity
     Random = Xorshift128; parity proven feasible by valheim-map.world)
     behind the same shim, validated against the step-1 fixture dumps.
  Keep the synthetic world for feature tests either way — constructed
  scenarios (narrow river, steep dome) stay more legible than real terrain.

## Tier 1 — differentiated core

- [ ] **Port `road_pin_start` / `road_test` console commands first**
  (from wip branch, `ConsoleCommands.cs` + `GenerateTestRoad`). The
  30-second in-game test loop makes every other item cheap to iterate on.
- [x] **Pathfinder cost-model rework** — DONE on `integration/upstream-prs`
  (06abd0d): additive costs, `float.PositiveInfinity` blockers, swamp
  wading, mountain slopes expensive-not-impassable, ford crossings <= 48m
  at RiverCrossingPenalty, A* open-set fix, RiverPenalty 100000 -> 4000.
  21/21 tests. This is the PR 2 payload (targets master; pathfinder files
  identical on both bases). Terrain-quality follow-up (ea37bca) after the
  first real-world selftest: waterline clearance + move interior sampling
  (roads keep feet dry between cell samples) and along-path grade shaping
  (MaxTraversableGrade cap + per-meter quadratic cost → switchbacks).
- [ ] **River crossings with `RoadCrossing` metadata.** June version proved
  A* can jump rivers (≤6 cells / 48 m at a penalty). Missing piece: record
  `{ type, center, fromBank, toBank, direction, width, biome }` and **split
  the painted road at the banks** instead of carving a causeway through the
  water. Road-ends-at-bank / resumes-at-other-bank reads as a ruined
  crossing even before bridge prefabs exist.
- [ ] **Road cross-section + endpoint ramps** (port from wip branch):
  flat core (~65% width, `RoadFlatCoreRatio`) with smoothstep shoulders
  replacing `pow(x, 0.1)` blend; 40 m endpoint ramps blending raw→smoothed
  height. The change players actually see. Key design detail from the June
  code (confirmed 2026-08-31): **level wider than you paint** — leveling
  extends to halfWidth + 2 m blend margin, full paint only in the flat
  core, paint fading across the shoulders, so roads read as a solid dirt
  strip with smoothed grass verges. Make the unused `RoadShoulderOuterRatio`
  a real knob for shoulder extent, and consider painting even narrower
  than the core for wider RoadWidth configs. Fix in passing:
  `ResampleTrimmedEndpointHeights` moves endpoint X/Z (misnamed, and
  grid-snap + 8 m trim buffer can leave visible gaps at locations); drop
  or rework the snap.

- [ ] **`road_bake` command — pre-bake the network for unmodded clients.**
  Iterate every zone with `RoadSpatialGrid` points and write its
  `_TerrainCompiler` ZDO (terrain deltas + paint) directly into the world
  save, without a player visiting — plus corridor-only vegetation clearing.
  JereKuusela's Upgrade World proves headless ZDO writes on unloaded zones
  work; its `generate` command is also the zero-code interim workflow
  (modded crossplay server, pre-generate road zones once, Xbox players see
  roads forever after). Keeps the save small vs. full-world pregeneration.

## Tier 2 — high visual payoff, builds on Tier 1

- [x] **Switchback shaping** — DONE (ea37bca): MaxTraversableGrade cap +
  per-meter quadratic grade cost. Real-world result: cliff-climb violations
  eliminated (24 -> 9 total), one network component per island.
- [x] **Stair runs on steep sections** — DONE 2026-09-01: detection at
  spline-scale grading, layout solver (terrain-tracking, support columns,
  deterministic ruin), terrain untouched under runs, validator exemption.
  Result: FIRST CLEAN PASS on the fixture world (0 violations, hash
  6d63bd64, 83 routes) — the NAS loop is now a binary regression gate.
  Emergent bonus: steep riverbanks become staircases descending to fords.
  Placement in-game ships with the bridge placement feature.
- [ ] (superseded design notes) **Stair runs**: three grade
  bands — 0-0.35 road (terrain modified), 0.35-0.5 STAIRS (no terrain mod:
  record StairRun metadata, place stair pieces hugging the slope at zone
  spawn — vanilla stairs are exactly 2m run / 1m rise = grade 0.5), >1.0
  impassable. Cost stairs above road, below long detours, so they appear on
  final approaches only. Reuses the bridge architecture (pure layout solver
  + SpawnZone placement + WearNTear ruin states — missing steps are jumps
  or cheap repairs). Kits by progression: wood stairs (Meadows/BF), stone
  stairs (Mountain), dvergr stairs/spiral (Mistlands — also the answer to
  the remaining 8 sub-cell grade violations on jagged Mistlands routes).
  Aesthetic bar: player builds using the Gizmo mod for fine rotations along
  the contour — procedural placement writes ZDO transforms directly, so we
  get exact per-piece yaw/height alignment for free (no build-UI snap), and
  unmodded clients render arbitrary rotations fine.
  Support grammar (2026-08-31): contour stairs cantilever on the downhill
  edge by construction (piece is level, terrain falls away) and WearNTear
  would demolish floating pieces — solver samples terrain under each
  piece's corners: uphill edge may clip into the slope, and any downhill
  gap > ~0.5m emits a support column to ground (wood poles / stacked stone
  / dvergr supports by kit). Ruin removal takes column AND the stairs it
  carried together — never leave a floating span. Tall columns on cliff
  faces are the drama: stilted flights are the signature epic-build look.
- [ ] **Mountain boulders vs roads** (designed 2026-08-31): boulders are
  per-zone vegetation spawned AFTER road generation, so pathfind-time
  avoidance requires replicating zone RNG — deferred as a research item
  (deterministic vegetation prediction via DecompilerServer; would enable
  road geometry reacting to rocks, e.g. a switchback at a monolith). Now:
  selective clearing at zone spawn via existing clear-area machinery —
  remove rocks intersecting the road CORE only, keep shoulder-overlap rocks
  (roadside monoliths are character), position-seeded keep-bias for edge
  cases so regeneration stays deterministic.
- [ ] **Biome road surfaces** — dirt (Meadows/Black Forest/Swamp), stone
  (Mountain), mossy stone (Mistlands). Mostly a paint-type lookup at
  terrain-mod time. Perlin-blended transitions = stretch goal.
- [ ] **PNG world/road renderer** — nearly free on top of upstream
  `RoadRoute`: headless dump + small script drawing islands, routes, roots,
  failures, crossings. This is the tuning tool for the two items above.
  (CSV exporter as originally planned is reduced to a thin dump command —
  upstream's route export did the hard part.)

## Tier 3 — later, once crossings exist

- [ ] **Ruined bridge foundations** (designed 2026-08-31) — vanilla pieces
  as ZDOs so unmodded clients see them, and players can REBUILD the deck on
  surviving piers:
  1. PR 3 prerequisite: `RoadCrossing { center, fromBank, toBank, direction,
     width, waterLevel, riverbedHeight, biome }` recorded at ford acceptance,
     persisted like routes (#17 pattern), surfaced in the selftest report.
  2. `BridgeLayout.Solve(crossing, seed)` — pure, harness-tested layout
     solver returning (prefab, position, rotation, healthFraction). Support
     grammar: piers are vertical stone stacks from the riverbed (ground-
     supported — WearNTear collapses floating stone on zone load), deck
     plates only directly on pier tops, no cantilevers. Ruin = deterministic
     missing pieces (mid-span deck preferentially gone, abutments mostly
     intact, pier bases never removed) + WearNTear health 30-70% for the
     cracked/worn vanilla damage visuals (health lives in the ZDO — syncs).
  3. Placement via the existing ZoneSystem.SpawnZone patch: instantiate with
     ZNetView on first zone spawn, set health, register clear-areas.
  4. Blending: abutment floors sunk ~0.3m below road surface at the banks so
     terrain/paint lap onto the stone; paint paved onto the abutment
     footprint.
  5. SAILING IS SACRED (decided 2026-08-31): do NOT modify the riverbed —
     no causeway, no rubble line; the channel keeps full depth. `RoadCrossing`
     gains a `fairway` (channel center + width from the deepest part of the
     river profile) and the solver keeps piers AND debris out of it; the
     missing deck section goes over the fairway (bridge collapsed exactly
     where boats pass). Ruin debris = tilted stone blocks in random
     orientations, settled into the bed near pier bases/banks only —
     visible underwater, never in the fairway (they are real colliders).
     Road users wade/swim the gap or rebuild the deck on the piers.
  6. Progression-aligned styles (decided 2026-08-31): material follows
     player progression via (biome, world ring) -> BridgeStyle lookup
     (warp-71 already computes rings; crossings record biome). Start with
     the SIMPLEST kit first: Meadows wood — pole pilings + plank deck,
     heavily decayed (rebuildable with day-one materials). Then:
     Black Forest log-on-stone-footings; Swamp rotten piling stubs only;
     Mountain/Plains stone piers + arches (monumental, half-toppled);
     Mistlands black marble, least ruined (dvergr still maintain theirs —
     vanilla dvergr marble bridges are the reference look, as Draugr
     village wood bridges are for Meadows). Decay scales inward: humblest
     and most-decayed at the center, grander and better-preserved outward —
     fits the ancient-network premise, and rebuild cost tracks progression.
  7. Verify exact vanilla prefab names + WearNTear thresholds against game
     data first (DecompilerServer).
- [ ] Gully crossings: detect via height-profile dip along the path (reuse
  crossing machinery, not a separate terrain scan).
- [ ] Edge landings as optional destinations → future docks/harbours.

## Contributing upstream (observed conventions — no CONTRIBUTING.md exists)

- Single-author project (jneb802/"warp"); all 13 PRs to date are self-PRs,
  so we'd be the first external contributor. Issues are answered actively.
- The two feature branches in our base are open PRs
  ([#16](https://github.com/jneb802/ProceduralRoads/pull/16),
  [#17](https://github.com/jneb802/ProceduralRoads/pull/17)) — unreviewed,
  unmerged as of Aug 2026. Watch for changes before/while building on them.
- Branch naming: `feature/<slug>`, `fix/<slug>`.
- PR body style: `## Summary` bullets + `## Validation` section listing
  `git diff --check`, `dotnet build ProceduralRoads.sln`, and in-game
  world-test results (world name + `road_pins` count).
- Commits: short lowercase imperative, occasional `fix:` prefix.
- Community validation for Tier 1: issue
  [#7](https://github.com/jneb802/ProceduralRoads/issues/7) (most islands
  get no roads; users burning 30k–100k pathfinder iterations for partial
  results) is exactly what the cost-model rework + A* fix address. Issue
  [#8](https://github.com/jneb802/ProceduralRoads/issues/8) (show full road
  path on map) aligns with the route-export/renderer work.

## Dropped from the old plan

- Island root / safe start point selection — superseded by warp-71.
- Minimap pin debugging — dead end; upstream `road_pins` covers basics.
- Standalone CSV exporter design — superseded by upstream route export.

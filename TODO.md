# Fresh-Start Plan (2026-08-31)

Strategy: upstream now owns network topology (anchor/endpoint selection,
location priority API, route export). We own **path quality and crossings**
(`RoadPathfinder`, `RoadTerrainModifier`, debug tooling) — areas upstream is
not touching, so everything below stays merge-friendly and independently
shippable, potentially as upstream PRs.

## Base

- [ ] Branch from `origin/master`, then evaluate merging upstream branches:
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

- [ ] Extend harness to cover `RoadSpatialGrid` smoothing and
  `RoadTerrainModifier` blend math (add shims as needed).
- [ ] Grow `SyntheticWorld` scenarios per feature (swamp lowlands for wading,
  narrow-vs-wide river for crossings, steep dome for switchbacks).
- [ ] Later: dump real-seed height/river/biome samples from the game once and
  replay as a fixture world for real-world regression tests.

## Tier 1 — differentiated core

- [ ] **Port `road_pin_start` / `road_test` console commands first**
  (from wip branch, `ConsoleCommands.cs` + `GenerateTestRoad`). The
  30-second in-game test loop makes every other item cheap to iterate on.
- [ ] **Pathfinder cost-model rework** (port from wip branch, minus bug #1):
  additive costs instead of first-match-wins; `float.PositiveInfinity` for
  true blockers (deep water, wide river); swamp shallow-water wading at a
  penalty. Fixes the real root cause of islands failing to generate roads.
- [ ] **River crossings with `RoadCrossing` metadata.** June version proved
  A* can jump rivers (≤6 cells / 48 m at a penalty). Missing piece: record
  `{ type, center, fromBank, toBank, direction, width, biome }` and **split
  the painted road at the banks** instead of carving a causeway through the
  water. Road-ends-at-bank / resumes-at-other-bank reads as a ruined
  crossing even before bridge prefabs exist.
- [ ] **Road cross-section + endpoint ramps** (port from wip branch):
  flat core (~65% width) with smoothstep shoulders replacing `pow(x, 0.1)`
  blend; 40 m endpoint ramps blending raw→smoothed height. The change
  players actually see. Fix in passing: `ResampleTrimmedEndpointHeights`
  moves endpoint X/Z (misnamed, and grid-snap + 8 m trim buffer can leave
  visible gaps at locations); drop or rework the snap.

## Tier 2 — high visual payoff, builds on Tier 1

- [ ] **Switchback shaping** — cost shaping, not geometry: raise the
  mountain slope ceiling; make cost scale steeply with grade *along the
  path* so contouring beats direct ascent. A* produces switchbacks on its
  own. Depends on the cost-model rework.
- [ ] **Biome road surfaces** — dirt (Meadows/Black Forest/Swamp), stone
  (Mountain), mossy stone (Mistlands). Mostly a paint-type lookup at
  terrain-mod time. Perlin-blended transitions = stretch goal.
- [ ] **PNG world/road renderer** — nearly free on top of upstream
  `RoadRoute`: headless dump + small script drawing islands, routes, roots,
  failures, crossings. This is the tuning tool for the two items above.
  (CSV exporter as originally planned is reduced to a thin dump command —
  upstream's route export did the hard part.)

## Tier 3 — later, once crossings exist

- [ ] Ruined bridge foundations: prefab placement on `RoadCrossing` metadata.
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

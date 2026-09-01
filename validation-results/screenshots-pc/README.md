# Windows visual validation — RoadTestPC1 (2026-09-01)

Native Windows client, fresh world `RoadTestPC1`, character `RoadTesterPC`.
Config: IslandRoadPercentage=100, DebugValidation=true, ForceRegenerate=true.
Mod v1.4.3 built from `integration/upstream-prs`, harness 55/55 on net10.0 AND net48 (native).

## Selftest

```
[SELFTEST] FAIL: 76 routes, 23904m total, 57 network component(s), 9 ford(s),
9 crossing(s), 308 stair run(s), 3076 ruin piece(s) planned, hash c4d6271b, 3 violation(s)
VIOLATION slope: MountainCave02 -> MountainCave02 point 94 grade 1.55 over 1.6m
VIOLATION slope: SunkenCrypt4 -> Mistlands_DvergrTownEntrance2 point 131 grade 1.59 over 1.0m
VIOLATION dry-land: Mistlands_DvergrTownEntrance2 -> Mistlands_Harbour1 point 83 (4685,-5475) height 29.2
[PREFABS] missing: stone_stairs, dvergrprops_stairs
```

Identical numbers and hash on a second load (ForceRegenerate) — regeneration is
deterministic on this world. `c4d6271b` is the Windows-station baseline hash.

## Shots

| file | what it shows |
|---|---|
| crossing0-ground.jpg | Crossing 0 (BlackForest 6512,-5152) from the water: lone tall pole mid-river, stair/post assembly on bank |
| crossing0-road-approach.jpg | Standing on the plank deck; T-post ahead, paved road resumes on far bank |
| crossing0-side-profile.jpg | Side view: isolated goalpost frames + lone poles, no coherent bridge mass |
| stairs0-mountain-uphill.jpg | Long continuous stone stair run climbing snowfield (reads well) |
| stairs0-mountain-closeup.jpg | Same run close: straight fall-line climb, ruin blocks off to the side, second run on ridge |
| stairs0-mountain-side.jpg | KEY EXHIBIT: along the ridge the stair segments are a sawtooth of disconnected blocks, floating over terrain dips |
| swamp-crossing3-along.jpg | Crossing 3 (Swamp -1392,5832): piers stacked 2-3 pole segments (~8-10 m), deck plates far above head height, road passes at grade below |
| swamp-crossing3-side.jpg | Wide profile: row of portal frames + lone poles marching across the channel; disconnected stone stair segments descend far bank |
| swamp-crossing3-station-closeup.jpg | Bare pole cluster at one station: no beams, no deck; persistent white square particles hover around ruin pieces |
| mistlands-crossing5-stairs3.jpg | Mistlands ravine: ruin blocks wedged between rock spires; path threads a dark crevice |

## Round 2 shots (decay investigation + vegetation clearing)

| file | what it shows |
|---|---|
| crossing4-arrival.jpg | Virgin-zone arrival seconds after spawn (87/87 pieces standing); gorge ramp reads semi-plausibly |
| wear-test-t0.jpg / wear-test-t3min.jpg | Same swamp station minutes apart; compare right-side frame group |
| crossing0-clear-34view.jpg | Crossing 0 after `cli_destroy_nearby_prefabs` cleared 138 trees/shrubs: open view of the bank, T-post + leaning pole + downed pole where session 1's stair assembly stood |
| crossing0-clear-remains.jpg | DEFINITIVE side exhibit, clear sightline: ruined approach ramp (left, reads well), pier march with descending pole heights, lone T-frames, no deck spans |

Vegetation clearing for photography: `cli_destroy_nearby_prefabs <pattern> [radius]`
(new valheimCLI command, branch pc/teleport-instant-and-state).

## Snap-chain rework verification (2026-09-01, branch pc/snap-point-composition)

Fixture world RoadTestPC3 (hash c9c3014e, 81 routes, 5 crossings, 296 stair
runs, 2146 pieces, 1 dry-land violation). Anchored censuses (cli_prefabs_at):

- Wood crossing (BlackForest 9105,-822): **108/108 surviving** after 10+ min
  loaded across a session restart — zero attrition, surviving a fresh support
  evaluation. Old grammar lost 44% of stairs in 6 min at a comparable site.
- Stone crossing (Mistlands 1664,7268): **168/168 surviving** after a 5-min
  dwell, positionally byte-identical.
- The RoadTestPC2 "stone kit lost 8" number is RETRACTED: player-relative
  census sphere drifted during the dwell. Anchored measurement kills that
  error class.

| file | what it shows |
|---|---|
| freecam-topdown3.jpg | Top-down (freefly camera): road -> stair cascade -> continuous deck on piers -> far bank, one coherent structure |
| freecam-bridge-side.jpg | THE exhibit: complete ruined trestle in profile — deck on marching post pairs, collapsed span at the far abutment |
| newbridge-wood-ondeck.jpg | Station assembly close-up: portal frame (paired posts + beam) with the deck plank seated through it |

## Finding: creation-load generation differs from reload generation

RoadTestPC3, same DLL, ForceRegenerate on: the WORLD-CREATION load generated
hash c9c3014e (81 routes, 2146 planned pieces); every reload generates hash
49748415 (84 routes, 2949 planned) — and reload-to-reload is byte-identical.
Crossing/ford/stair-run counts are stable across all loads; only route
success and therefore ruin plans shift. The mod's own rng is all seeded
System.Random and biome/height sampling is pure WorldGenerator, so the
suspect is upstream input state that differs between freshly-generated and
loaded-from-save worlds (location instance list provenance/order).
RoadTestPC1 did NOT show this (creation == reload, c4d6271b), so a condition
is unpinned. Not composition scope — needs its own investigation.

RULE UNTIL FIXED: take fixture/regression baselines from a RELOAD, never
from the creation session. RoadTestPC3's baseline is 49748415.

## Visual assessment (blunt)

1. **Scaffolding, confirmed on every crossing.** Piers are single skinny poles
   (stacked wood_pole2), often bare, sometimes with a floor plate or one beam on
   top. Nothing reads as a bridge ruin; the swamp crossing reads as an aqueduct
   skeleton at 2-3x sensible deck height (bank-height max is too high when banks
   are hills, deck should grade from bank contact points).
2. **Stairs on open slopes are the best-looking output** — long continuous runs
   look genuinely good from below/above (stairs0-mountain-uphill).
3. **Stairs break where heading/grade changes.** Pieces are placed at computed
   positions, not snapped to the previous piece's snap points, so runs drift into
   sawtooth gaps and floaters (stairs0-mountain-side). Tys's direction: snap
   consecutive pieces — inside-corner snaps on convex contours, outside snaps on
   concave — and fill the wedge between differently-angled steps with blocks.
4. **Persistent particle noise.** White square particles hover around ruin
   pieces (visible in most shots). If that's the placement effect re-firing every
   load under ForceRegenerate it will not appear for players, but verify it isn't
   the wisp/spawn effect attached to the ZDOs permanently.
5. **Selftest FAIL on a fresh organic world** (3 violations, above). Two slope
   violations sit exactly at stair-run grades (1.55/1.59 vs presumably a 1.5
   cap); the dry-land violation at (4685,-5475) height 29.2 wants a look at the
   Mistlands harbour route.
6. **[PREFABS] missing: stone_stairs, dvergrprops_stairs** — two kit names don't
   exist in the live game; whatever assembly references them silently loses
   pieces. (Superseded in part by the NAS enumeration: piece_dvergr_spiralstair,
   piece_dvergr_spiralstair_right, dvergrprops_wood_stair, blackmarble_stair_corner
   and blackmarble_stair_corner_left all exist and were spawn-verified on the
   native client. The prefix mapping is inconsistent — enumerate, never guess.)
7. **CONFIRMED: unsupported pieces self-demolish in front of the player.**
   Controlled test at crossing 4 (BlackForest 3976,2720, virgin zone): zones
   (62,43)+(62,42) spawned 87 pieces; immediate census within 40 m found all 87
   (61 wood_pole2, 18 wood_stair, 8 wood_floor). After ~6 minutes of loaded
   time: 61 poles, **10** wood_stair, 8 wood_floor — 8 stairs collapsed. The
   white square particles in the tour shots are vanilla WearNTear support-
   collapse debris, and players will watch (and hear) assemblies crumble on
   first visit. Poles survive because they ground; stairs placed floating die.
   Fix direction is the same snap-point work: snapped, grounded chains are how
   vanilla support propagates. Health-fraction ruining is fine; support-invalid
   placement is not.

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
   pieces.

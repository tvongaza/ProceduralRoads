# Validation cycle baseline (8 Sep 2026)

Step 1 of docs/plan-validation-cycles.md: instrument the whole cycle, then
measure before optimizing. Every number below comes from
`<private>/scripts/pc-run.sh` (the Mac's clock, one line per stage)
merged with the mod's own `road_timings` (branch pr/validation-cycles,
stacked on #19). Machine: the gaming PC, world RoadTestPC4, build
1b234c19 (e151a6c's parent), RoadWidth 4, IslandRoadPercentage 100,
PathfindingMaxIterations 100000.

## The runs

| run | what | total | where it went |
|---|---|---|---|
| t1-cold | game closed, fixture restored, new dll, global generation at load, 5 sites / 20 shots | **285 s** | world.load 74 (generation 58 inside it), sites 87, shots 94, launch+menu 13, capture 7, quit/restore/install 4 |
| t1-warm | same 5 sites / 20 shots, game already in-world, nothing regenerated | **192 s** | sites 85, shots 94, capture 6; mod side: 35 zones re-entered, 0 written (all stamped) |
| t1-slow-global | 1 site / 5 shots, `road_generate` in-world (SITE_APPLY=global) | 163 s | the generate 59.5 s, and a second 59 s because the station asked twice for the late CLI output (fixed, see below); shots 22 |
| t4-warm-island | 1 site / 5 shots, `road_regen_island` in-world (SITE_APPLY=island-each) at W4 | 44 s | generation 0.5 s (W4's island: 4 locations, 4 attempts, 3 roads, 4624 iterations), apply 25 ms; the site's 13 s is the fixed env/clear sleeps; shots 24 |
| t2-cold-roads | as t1-cold but on the roads-baked fixture (FIXTURE=roads, build dd3e5ae) | **220 s** | world.load 14 (roads loaded from the save, no generation), sites 83, shots 94, launch+menu 14; 30 zones written at the sites in 0.3 s; 9 warning lines, none from generation |

## Where the time goes

**Cold start (285 s).** The world load is 74 s and 58 s of it is the road
network generation, which runs synchronously when the player spawns
(marks: zone system start 9.8 s after process start, locations ready
9.8 s, player spawn 18.4 s = generation start, generation done 76.3 s).
The loading screen stays up through it, so `wait --for localplayer`
returns only afterwards. The game itself needs about 18 s from process
start to a player.

**Generation (58 s) is pathfinding.** From the mod's stages, one global
generation on RoadTestPC4:

| stage | calls | total | longest |
|---|---|---|---|
| gen.pathfind | 146 attempts | 57.7 s | 5.5 s (Dragonqueen -> Crypt4) |
| gen.road_add (dense heights, smoothing, merge; 46 roads) | 46 | 0.15 s | 10 ms |
| gen.islands | 1 | 11 ms | |
| gen.locations, gen.finalize, gen.canvas_update (146x) | | < 10 ms each | |
| terrain.apply_loaded at load (43 zones written) | 1 | 78 ms | |

100 of the 146 attempts fail with "no reachable path": the search
exhausts the reachable region before giving up, and those 100 failures are
the bulk of the 601 287 A* iterations (about 96 us per iteration, which
points at the terrain sampling in the move cost, not the queue). The
per-road `Canvas.ForceUpdateCanvases()` the plan wanted measured costs
2.7 ms in total: harmless, not worth a change on its own.

**Terrain is free.** 43 zones written at load in 0.4 s (heights 0.38 s,
paint 3 ms, save 15 ms, heightmap rebuild 0 ms). In the warm run every
re-entered zone was stamped and skipped. Nothing to optimize here.

**Per site (about 17 s each, 5 sites = 85 s), all station-side waits:**

| stage | per site | what it is |
|---|---|---|
| env | 6.6 s | `cli_set_tod`, `env Clear`, then a fixed 6 s settle for the weather blend |
| clear | 3.7 s | `cli_clear_view` twice with a fixed 3 s between (removed 0 objects at every site this run) |
| teleport | 3.4 s | teleport plus polling `cli_player_state` until the player lands (1 s poll) |
| debug | 1.7 s | `road_debug` (ask, flush) |
| zones | 0.4 to 3.2 s | `cli_zone_ready` polled at 1 s |

**Per shot (4.7 s, 20 shots = 94 s):** `cli_freefly_pose` + fixed 2.5 s
settle + `cli_screenshot` + fixed 1.5 s. Both sleeps are guesses from
2 Sep; the screenshot files were all present within 1 s of the last shot.

**Frame stalls.** Cold: 111 frames over 100 ms, the longest 58.1 s (the
generation). Warm: 99 stalls, longest 144 ms (teleport zone loads).

**Fixed overheads:** Steam launch 5.5 s, main menu 5 s, quit/restore/install
4 s, screenshot transfer (20 png, 139 MB) 4 s, previews 2 s.

## Findings that change the plan

1. **The station asked twice for late CLI output and re-ran the command.**
   `road_generate` costs 58 s, so every SITE_APPLY=global site paid
   twice (the old pc-shoot.sh had the same pattern: the 7 Sep rounds paid
   it too). Fixed in pc-run.sh: flush with a cheap command
   (`road_timings run <id>`) instead. Generic fix belongs in valheimCLI:
   return the output with the call that produced it.
2. **A roads-baked fixture removes the 58 s for every PR that does not
   change routes.** The mod persists the network in the save; a fixture
   saved right after generation loads it (`load.road_data`) and skips
   generation. Valid for one network version only (the owner, 8 Sep: bridges
   will change reachability); the run record carries the fixture name
   and the build. Sites must be unvisited in the fixture: a visited zone
   is stamped and a new build's terrain code never touches it.
   `pc-bake-fixture.sh` builds it; measured below.
3. **Per-site and per-shot fixed sleeps are half of a warm run** (about
   6 + 3 + 2.5 + 1.5 s per shot-and-site). Plan step 3 replaces them
   with specific readiness (weather blend done, zones and compilers
   alive, screenshot file written), each bounded.
4. **Generation itself, for route-changing PRs:** the two candidates are
   a reachability precheck derived from the pathfinder's own passability
   rule (not a same-island test, so bridges keep working) to skip the
   100 hopeless searches, and a cached height grid per island if the
   96 us per iteration is WorldGenerator sampling. Measure the
   iteration cost split first.
5. **The generation runs on the main thread inside the loading screen.**
   Batching it (plan step 5) would not shorten the cold start on its own;
   the baked fixture and the pathfinding cost do.

## Budgets (first cut, from the runs above)

| cycle | today | budget after steps 2-3 | what gets it there |
|---|---|---|---|
| cold start, routes unchanged (baked fixture), 5 sites / 20 shots | 220 s | 130 s | fixed sleeps -> readiness checks (~ -60 s), launch+menu skip (~ -10 s), batch nearby sites |
| cold start, routes regenerated (base fixture) | 285 s | 190 s | as above plus the pathfinding work (step 6) |
| warm capture, 5 sites / 20 shots | 192 s | 100 s | readiness checks; sites 17 s -> ~8 s, shots 4.7 s -> ~2.5 s |
| slow case: one in-world `road_generate` | 60 s | 30 s | pathfinding: skip hopeless searches, cheaper move cost |
| one in-world `road_regen_island` (small island) | 0.5 s | | already the way to iterate on one site; the start island (7 Sep) was ~10 s |

The plan's initial target (halve 285 s) is 143 s: the baked fixture alone
took the representative cycle to 220 s; the rest is the station's fixed
waits and, for route-changing PRs, the pathfinder.

Warnings seen in the baked run (9 lines): two "Large height spread"
lines are `road_debug` output at sites where roads meet (the command's
own check, not a terrain write), the rest are the game's usual audio,
log-writer and character-ID lines. Generation runs add 100 "no reachable
path" and their per-road "Could not find path" labels (213 lines).

## How to run it

    # cold: quit, restore fixture, install dll, launch, load, sites, shots, fetch
    SITE_APPLY=none scripts/pc-run.sh cold <tag> <ProceduralRoads.dll> [World] [sites]
    # warm: game already in-world with the right dll
    SITE_APPLY=none scripts/pc-run.sh warm <tag> [World] [sites]
    # roads baked in (no generation): FIXTURE=roads, after scripts/pc-bake-fixture.sh <dll>

Output in <private>/shots/<World>-<tag>/: stages.tsv, run.json
(station stages + the mod's JSON), mod-timings.txt, log.txt (verbose),
png + -small.jpg. Stdout is one line per stage plus a summary; read
log.txt only for a FAIL. In game: `road_timings` (summary), `road_timings
reset [id]`, `road_timings run <id>`, `road_timings json`.

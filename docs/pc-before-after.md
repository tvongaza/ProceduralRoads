# Before/after screenshots on the gaming PC (station procedure)

Written 7 Sep 2026 after the first cross-section run thrashed for an hour on
road generation. Read this before any before/after round; the private
station repo carries the scripts (`pc-cycle.sh`, `pc-shoot.sh`) that
implement it.

## What went wrong on 7 Sep 2026, and the rule that follows

1. **The spawn zone never shows roads.** Zones around the spawn point are
   generated during the loading screen, before the road network exists, so
   the road is in the data and nothing is on the ground there. Rule: never
   judge terrain or paint within ~200 m of the spawn; pick sites in zones
   the player has not visited.
2. **On master-based builds most freshly generated zones ALSO get no road
   terrain.** Valheim first creates the outer ring of zones around the
   player as ghost zones (ZDOs only), then re-spawns them in
   `SpawnMode.Client` when the player gets close. The `ZoneSystem.SpawnZone`
   postfix on master returns early for Client mode, so the road terrain is
   applied only to the few zones that happened to be spawned Full directly
   (on RoadTestPC4 that was 3 of ~15 zones around the first site). The lead
   branch (pc/snap-point-composition) already fixes this: it applies terrain
   for every non-Client spawn and only cleans vegetation on Client spawns.
   Until that fix is upstream, master-based PR builds need step 6 below.
   Rule: after teleporting to a site and waiting for `cli_zone_ready`, run
   `road_generate`; it regenerates the (deterministic) network and
   re-applies terrain to every loaded zone ("Applied roads to N visible
   zones"). The apply is idempotent because the level delta is computed
   from the world-generator height, not from the current heightmap.
3. **`IslandRoadPercentage = 0` does not disable roads on master**
   (`Mathf.Max(1, ...)` keeps one island). Rule: create the fixture world
   with the ProceduralRoads dll moved out of `BepInEx/plugins`, so the world
   has its locations placed and no road data at all.
4. **CLI output arrives one call late** (`road_generate`, `road_debug`,
   `road_debug_log`, `cli_world_dump`): ask twice, keep the second answer;
   `road_generate` also trips the CLI's own timeout while the game works,
   which is harmless.
5. **`road_debug_log` lists at most ~20 points and `road_debug` only 15 m**,
   so a road cannot be followed from the log. Hop: teleport ~60 m along
   the last known direction and list again. `cli_world_dump` gives every
   location with coordinates; roads from `Start` (spawn) and to the nearest
   `Eikthyrnir` are the cheapest sites to find.
6. **Each build starts from the pristine post-location fixture.** Road
   terrain and paint live in visited zones' TerrainComp ZDOs and survive a
   regeneration, so a second build on the same save shows the first
   build's ground in every visited zone. Restore the fixture, then install
   the dll, then launch (a dll only loads at launch).

7. **One Steam account = one running client.** A PC launch while the Mac
   client is in-world stalls inside Steam at "KickingOtherSession" (visible
   only in Steam/logs/console_log.txt); the scripted launch then looks like
   a CLI timeout, and Steam keeps the stalled action so later launch
   commands are swallowed until Steam is restarted. Rule: close the other
   client first (`cli_logout_save`, then kill the process), or run a
   second Steam account on the station (the owner, 7 Sep 2026: later). The
   station's `pc-launch.sh` now names this state within two minutes, and
   `SteamStart` / `ValheimApplaunch` scheduled tasks run wrapper .cmd files
   (schtasks quoting breaks on the spaced account name). If Steam has
   swallowed a launch: `steam.exe -shutdown`, `schtasks /run /tn SteamStart`,
   wait for "Logged On" in Steam/logs/connection_log.txt, launch again.
8. **Seeing the PC desktop from SSH:** `schtasks /run /tn StationShot`
   (a hidden wscript wrapper around a DPI-aware PowerShell capture) writes
   C:\Users\Public\station\desktop.png plus windows.log (every top-level
   window title). Session 0 cannot see session 1 any other way.

## Say what a run is (the owner, 7 Sep 2026)

Every round states three things, in the log, the exhibit README and the
PR text:

- **Generation scope**: global (every selected island; RoadTestPC4 = 61
  islands, 46 roads, 20 626 points, ~60 s on the PC, 259 s on the Mac) /
  one island (`road_regen_island`, lead branch only so far) / nearby
  loaded zones (no such mode yet; `road_generate` only re-applies terrain
  to loaded zones, it still generates globally) / point to point (not
  built; the earlier ChatGPT prototype had it).
- **Painting**: which terrain/paint code ran (master profile, cross-section
  profile, ramp), and whether terrain was applied at zone spawn or
  re-applied by `road_generate`.
- **World**: the pristine fixture (RoadTestPC4, restored per build) or an
  already generated world (roads loaded from the save).

Use the smallest scope that answers the question: a cross-section or
ramp shot needs one island, not the world. Master-based PR builds only
have global generation today; porting `road_regen_island` into a test
tooling overlay (zone-hook fix + road_ends + regen_island, applied on top
of any branch for test builds only) is the planned fix.

## Procedure (measured on the gaming PC, 7 Sep 2026)

Preconditions: PC awake (`pc.sh status`), Steam running in the desktop
session, the `ValheimLaunch` scheduled task registered
(`schtasks /create /tn ValheimLaunch /tr "cmd /c start steam://rungameid/<appid>" /sc once /st 00:00 /it /f`),
valheimCLI dll from PR #26+ (needs `cli_screenshot`, `cli_zone_ready`,
`cli_freefly_pose`, `cli_teleport ... instant`), an SSH tunnel
`ssh -f -N -L 5556:localhost:5555 <pc>` and `valheim-cli -p 5556`.

1. Fixture (once per world): move `ProceduralRoads.dll` out of plugins,
   launch, `cli_select_character <character>`, `cli_start_local_world RoadTestPC4`
   (65 s to spawn), `cli_logout_save`, stop the process, copy
   `RoadTestPC4.{fwl,db}` to `%USERPROFILE%\station\fixtures`. RoadTestPC4
   fixture md5: db 1dd0862b…, fwl 3c851695….
2. Per build (`pc-cycle.sh <tag> <dll>`): logout+save, stop valheim,
   restore the two fixture files (delete `.old` too), scp the dll to
   plugins, `schtasks /run /tn ValheimLaunch`, wait mainmenu, select
   character, start world (73 s), `cli_set_player_safety true`,
   `devcommands`.
3. Per site (`pc-shoot.sh <tag>`): `cli_teleport x 60 z instant`, poll
   `cli_player_state` until the position changed, poll `cli_zone_ready x z
   64`, `road_generate` twice (record "Total road points" — it must match
   across builds, 20626 on RoadTestPC4), `cli_clear_view x z 45`,
   `road_debug` twice into a text file (numeric heights per build),
   `cli_set_tod 0.45`, `env Clear`, wait 6 s, then for each fixed pose
   `cli_freefly_pose` + 2.5 s + `cli_screenshot <tag>-<site>-<pose>` +
   1.5 s. scp the PNGs back from
   `the game's user folder/valheimCLI/screenshots/<world>/`.
4. Poses are world coordinates fixed per site and reused verbatim for
   every build, so the images differ only by the build. A whole cycle
   (restore, relaunch, load, two sites, eight shots) is about 5 minutes.

## Sites on RoadTestPC4 (Meadows, start island)

| site | what | centre | road dir | ground |
|---|---|---|---|---|
| E | NE road end at the Eikthyrnir altar, 10 m from the altar centre | 331.1, -515.6 | 0.38, 0.92 | 51.5 |
| M | mid-road bend 55 m further along the same road | 348.7, -466.9 | 0.78, -0.62 | 45.8 |
| I | cross-slope west of spawn, road on fill 0.7-1.4 m above natural ground (the owner, on foot, master build) | -375.6, -280.9 | 0.42, -0.91 | 51.4 (natural 50.5) |
| J | road end at the second Eikthyrnir altar, arriving from the south, last points 0.8-1.0 m above natural ground (the owner, on foot, master build) | -324.9, 226.2 | 0.03, 1.0 | 37.0 (natural 36.2) |
| H | road across a west-facing hillside 95 m south of spawn (the owner, on foot, master build): master paints down the slope, the profile should level the section | 190.8, -354.3 | 0.51, 0.86 | 38.3 (west of road 37.5) |

## Ideas to make this cheaper (the owner, 7 Sep 2026)

- A test mode that generates roads for a single island only, or one road
  between two arbitrary points (as the earlier ChatGPT prototype did):
  seconds instead of a full-world generation, and the site is known in
  advance. Would live behind a [Debug] config key on a validation branch,
  not in a feature PR.
- Fix the Client-mode skip upstream (small PR, see 2 above) so
  `road_generate` is no longer needed on master builds.

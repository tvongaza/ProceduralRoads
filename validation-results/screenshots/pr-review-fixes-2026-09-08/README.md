# Review-fix round for PRs #19-#21 (8 Sep 2026)

Gaming PC, RoadTestPC4 fixture restored per build. Generation scope: GLOBAL
at load; painting: applied at zone spawn by the tooling hook (one terrain
compiler per zone, de6d4ee); world: fixture. Poses fixed per site.

- `<site>-<pose>-before.jpg`: pr/road-endpoint-ramp without its two fix
  commits (1883d67: ramp only, on the fixed #19/#20 base).
- `<site>-<pose>-after.jpg`: daa6874 (one-sided smoothing windows fit a
  line; terrain fits a line along the road).
- Sites: E and J (road ends at the two Eikthyrnir altars, Meadows), W4
  (Crypt4 end of median severity, Black Forest). the owner: judge ordinary ends,
  not the mountain extremes.

Ground at the end, 3 m beyond and 4 m inside (ZoneSystem.GetGroundHeight
via cli_player_state, before -> after; WorldGenerator height in brackets):

| site | end | 3 m beyond | 4 m inside |
|---|---|---|---|
| E (51.13) | 51.0 -> 51.1 | 51.3 -> 51.5 | 50.8 -> 50.7 |
| J (36.20) | 36.1 -> 36.2 | 34.9 -> 35.0 | 35.9 -> 36.0 |
| W4 (53.09) | 52.7 -> 52.5 | 52.5 -> 52.3 | 53.1 -> 53.2 |

W4 sits ~0.5 m below the WorldGenerator height in both builds (the crypt's
own terrain op levels the ground there); the same at MountainCave02 ends
(+9 m at W3). Stored heights along the W1 ramp: 0.18-0.80 m off the slope
before the smoothing fix, 0.00-0.03 m after (road_debug).

The first "after" build used a free plane fit and produced 1.5 m dips one
metre off the centreline 3-4 m inside J's end (a ramped end on a bend);
the line fit along the road (daa6874) removed them: all J differences
within 0.2 m.

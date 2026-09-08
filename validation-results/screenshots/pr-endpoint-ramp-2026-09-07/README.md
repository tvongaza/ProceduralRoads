# Endpoint-ramp PR exhibits (pr/road-endpoint-ramp 6fd038d vs its base pr/validation-tooling 159be6e)

Round 4, gaming PC, 8 Sep 2026 00:31-00:44 UTC. Generation scope: ONE
island (island 25, 12 locations, 3 roads, 1865 m, 9.5 s) via
road_regen_island with GenerateRoadsOnLoad=false; painting: applied at zone
spawn by the base branch's hook; world: RoadTestPC4 fixture restored per
build. Poses fixed per site (private station repo).

- harness-endpoint-ramp.png: road-point height minus natural terrain against
  distance from the road end, base vs PR, through the xunit harness.
- round4/<site>-<pose>.jpg: base | PR. Sites: J (road end at the second
  Eikthyrnir altar), E (road end at the first).
- site-J-owner-view.jpg: the view the owner found on foot (master build, Mac).
- worst-ends/W<n>-<pose>.jpg: the three worst road ends from the tooling
  PR's road_ends table (all MountainCave02 ends on other islands), base | PR.
  Generation scope: GLOBAL at load (the table came from the global network;
  island regeneration reached different roads there, see the handoff);
  painting at zone spawn; world: fixture restored per build. Poses are
  generic (top-down plus four compass views 22 m out), so some tiles look
  into rock. road_debug at the end point:
    W1 (-1287,6413): base road 16.70 m ABOVE the ground, PR 0.00 m
    W2 (691,7417):   base road 13.25 m BELOW the ground, PR -0.01 m
    W3 (-1459,7111): base road  8.42 m BELOW the ground, PR 0.00 m

# Cross-section PR exhibits (pr/road-cross-section 98e87fa vs its base pr/validation-tooling 159be6e)

Round 4, gaming PC, 8 Sep 2026 00:31-00:44 UTC. Generation scope: ONE
island (island 25, 12 locations, 3 roads, 1865 m, 9.5 s) via
road_regen_island with GenerateRoadsOnLoad=false; painting: applied at zone
spawn by the base branch's hook; world: RoadTestPC4 fixture restored per
build; clutter: cli_clutter off requested (grass still visible: open).
Poses fixed per site (private station repo, sites/RoadTestPC4-round3.txt).

- harness-cross-section.png: leveling and paint across a 4 m road on flat
  ground, base vs PR, through the xunit harness.
- harness-sections.png: the same section on a side hill, a mound and a
  valley (road at the natural centreline height).
- round4/<site>-<pose>.jpg: base | PR. Sites: E (Eikthyrnir road end),
  M (mid-road bend), H (hillside south of spawn), I (fill section west of
  spawn).
- site-H/I-owner-view.jpg: the views the owner found on foot (master build, Mac).

# Cross-section / endpoint-ramp exhibits (7 Sep 2026)

Gaming PC, fixture world RoadTestPC4 (post-location, no road data), three
builds from the same fixture with `road_generate` re-applied per site
(see docs/pc-before-after.md): master a33f3c0, pr/road-cross-section
5bf3ef1, pr/road-endpoint-ramp aaf66b7. Same roads on every build
(20 626 road points). Sites: E = NE road end at the Eikthyrnir altar
(331.1,-515.6), M = mid-road bend (348.7,-466.9). Poses in the private
station repo, sites/RoadTestPC4.txt.

- harness-cross-section.png, harness-endpoint-ramp.png: measured through
  the xunit harness (RoadTerrainModifier on a shimmed zone; AddRoadPath on
  the synthetic slope), old code vs new.
- <site>-<pose>.jpg: master | cross-section only | cross-section + ramp,
  HUD corners cropped.
- RoadTestPC4-map.png: biome map with bosses, spawn and the two sites.
- site-H-owner-view.jpg: the owner's own view (master build, Mac) of site H, a
  cross-slope road 95 m south of spawn where master paints down the hill;
  not yet shot on the other builds.
- site-I-owner-view.jpg: the owner's view (master, Mac) of site I, a cross-slope fill
  section west of spawn; not yet shot on the other builds.
- site-J-owner-view.jpg: the owner's view (master, Mac) of site J, the road ending
  above the second Eikthyrnir altar; the endpoint-ramp case.
- RoadTestPC4-road-ends.csv: every road end (nearest road point to each
  placed location, 142 of them) with the road height, the natural height at
  the end, and the mean/min/max natural height on an 8 m ring, from the
  scratch `road_ends` command (master build). Sorted by |end - ring mean|;
  the mountain caves top the list (up to +16.7 m).
- round2-cross-section/ (H, I: master | cross-section profile) and
  round2-endpoint-ramp/ (J: master | profile only | profile + ramp), shot
  on the PC 7 Sep 2026 late: generation GLOBAL (46 roads, 20 626 points,
  same on every build), terrain re-applied per site by road_generate,
  world = RoadTestPC4 fixture restored per build. H-along on master is
  blocked by a tree that cli_clear_view missed on that run.

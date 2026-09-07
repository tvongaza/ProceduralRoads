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

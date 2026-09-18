# Road approach validation and follow-ups

## Accepted scope — PR #21

The implementation seeks near-level exterior arrivals, protects location terrain and paint, limits planned road grades, and gives climbing switchbacks room to turn. A site may remain disconnected when a safe route cannot be built. Stairs are a separate future feature.

Manual inspection on 18 September 2026 used a combined integration build carrying this approach code alongside the network and bridge work:

- Frost cave: the user accepted the approach from the more level side. A small natural bump inside the protected location remains intentionally unchanged.
- Tar pit: the user confirmed the road skirts the pit.
- Troll cave: the user accepted the approach.

The regenerated island had 9 roads / 6,215 m, compared with 15 / 14,174 m before the turn constraints. Generation took 608.1 s versus 305.9 s at the same retained high-budget settings (100,000 search iterations, 30 locations per island). This is one island on the combined build, not a PR-only benchmark or a default-settings performance claim. Fewer connected sites were accepted; alternative-search cost is a follow-up.

Automated tests cover turn geometry, landing grades, the terrain-height fitter on a synthetic plane, refusal of pinched turns, protected sites, location/road terrain composition and rock eligibility. The PR branch passes 185 tests on each runtime; the combined integration build passes 378. Individual switchback terrain and rock removal/reload behavior are not comprehensively validated in game.

## TODO — natural boulder clearance

The current implementation checks four stock mountain boulder prefabs against the loaded road corridor. It protects ores, player pieces, location footprints and remotely owned objects. It is partial coverage, not complete boulder clearance.

- Extend coverage to all large natural boulders in Mountains, Plains and Black Forest, using actual vegetation registrations and preserving POI scenery and ore.
- Diagnose the observed mountain obstruction: a `rock2_mountain`, already eligible by name, remained beside/over the road near X1176.5, Z4323.1. Do not assume adding prefab names fixes this case. Check collider overlap, protection and ownership decisions.
- Verify a blocking boulder is removed, a nearby nonblocking boulder and protected scenery survive, and behavior persists or repeats correctly after reload.

## TODO — approaches and turn coverage

- Compare a route through the col toward the front of Hildir's cave when reliable entrance data is available. Location centre alone does not establish a doorway.
- Check terrain overlap between separate roads; current switchback separation checks legs of one reshaped path.
- Profile alternative-search work after turn rejection if further generation speed work is needed. Do not restore unsafe turns merely to increase connectivity.
- Keep the accepted cave's protected natural bump intact. A closer entrance connection needs real entrance/collision data.

## TODO — bridge PR #26: limit height to structurally supported spans

User observation, 18 September 2026: a very tall bridge on the island just toured in RoadSeedE had structural supports fail. Exact crossing coordinates and the failure mechanism have not yet been captured; height is the suspected cause, not a confirmed diagnosis.

- Identify the crossing and check vanilla support propagation for the actual deck, pier and beam prefabs, including terrain contact and ruined support gaps.
- Establish a supported height/span limit from those rules; reject or lower a bridge that cannot support its intended intact layout, while preserving POI terrain and usable approaches. Do not force a crossing when no valid layout fits.
- Add boundary tests for the limit and a regression for this crossing. Verify in game that the intended surviving structure remains stable after physics settles and after reload; deliberate ruin must remain distinct from unintended collapse.

This belongs to bridge PR #26, separately from the current #21 approach and boulder work.

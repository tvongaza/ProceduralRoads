# Fresh-Start Plan (2026-08-31)

## Operating model (2026-09-01)

- **NAS = hub**: development AND coordination run from the always-on NAS
  (dev happens in a container: dotnet SDK, git, harness; publicize
  assemblies from the server depot in /data/server as ci/validate.yml
  does). The NAS also runs headless regression validation natively and
  wakes the gaming PC on demand (touch /data/wake-requests/gaming-pc;
  marker consumed = packet sent; ~45s boot; verify via ARP not ping).
- **Gaming PC = visual station**: native Windows client runs the game,
  photographs ruins, iterates on visual/composition work. Pushes pc/*
  branches to the fork. May sleep; NAS wakes it.
- **Mac = occasional cockpit**: not required online. Historical context
  lives in this file, the tests, and the git history — not in any session.
- **Upstream (jneb802/ProceduralRoads)**: PR #18 (test harness) is open
  with the enriched core-behavior tests; a comment on #16 asks about
  branch stability. HOLD further upstream PRs/comments until the author
  responds, then follow the PR ladder (cost model+fords -> crossings ->
  cross-section -> stairs -> placement), each from its feature branch.
- **Baselines**: fixture world RoadTestAuto1 selftest hash 6d63bd64
  (0 violations, PASS). The NAS's RoadHeadless1 and the PC's RoadTestPC1
  worlds carry their own hashes as machine-local baselines.
- **Secrets discipline**: private infra details (tailnet names, hosts,
  paths, schedules, MACs) stay in gitignored files (scripts/.nas.env
  pattern) — never in tracked files of this public fork.

## 2026-09-02 round 2: bridges as a last resort, one waterline floor, honest crossing measure (df5ae66)

NAS review of 9dd8f2f (validator cap relaxed) asked the right question:
were all suppressed ford-length lines wide bridges? Checked against the
99cdf54 selftest + routes CSV + road_spots: **4 of 9 were** (crossings 0
and 2, 81/94 m, fairways 48/39). **5 were not**: 70–106 m jumps across
river-core bands that are mostly dry valley (10–41 m of water). Not road
defects (the road is on dry land) but two real findings:
1. the validator measured river CORE, not water → now measures
   consecutive points over water (< 31.25), label "spans N m of water",
   cap 128 m. Violation counts are not comparable across 9dd8f2f/df5ae66.
2. a 20000 bridge penalty ≈ 160 m of rough terrain in this cost model: not
   a last resort. Tys: a bridge must be far more expensive than going
   around. Now 50000 + 400/m (`BridgeIsALastResortEvenWhenTheDetourIsRough`:
   a 600 m rough detour beats a 96 m bridge; an island-splitting river is
   still bridged).

Endpoint pattern (NAS): three reports of route ends below the waterline
were one cause — `IsPathablePoint` used the 30.5 shallow line while banks
and road points use 31.25. Same floor everywhere now
(`RouteEndpointsUseTheWaterlineFloor`). The Bonemass → GoblinKing case was
different: the route jumped a 111 m river (crossing 3) and jumped back
87 m forty metres later (crossing 4) with 57 points under water between —
a double crossing the last-resort cost should remove.

Live (RoadTestMac1 @ 99cdf54, unread by Claude except c16 top-down): the
cobbled road now runs to an abutment plate at each water's edge and the
deck spans water only — the c0/c7 defect is gone. But the 52 m wood span
at c16 keeps only a stub or two per side after ruin + the 20 m gap: reads
as two jetties, not a collapsed bridge. **Style question for Tys: long
spans need a survival rule that keeps piers marching (piers outlive deck)
rather than per-station coin flips.** Shots delivered for verdicts.

Tys's ruling on artifacts: screenshots stay out of GitHub and off the NAS —
they are DERIVED (fixture + DLL commit + camera = regenerable); the world
states (~1 MB) and written findings are what to keep. Consequence: the pc
README cites 18 JPEGs that no longer exist; rewrite it as findings-only
(pointer to this file) at the docs/validation-gap merge.

### Instrument work forced by the NAS review (dcb4fbd, a83ed1c, c980a8e)

- **Blind spot (rev 4, a83ed1c):** the dry-land exemption was "in river
  core", so any crossing (spurious or not) hid its own underwater points;
  and the 12-line cap had no total, so historical counts at 12 are floors.
  Now: exempt only inside a recorded crossing span of the route; totals
  emitted for every capped class (dcb4fbd).
- **Two-load control:** df5ae66 network scored twice, hash 95020c0c both
  times. rev 3: dry-land 12 (cap). rev 4: 9 true. The 9 = 7 knee-deep ford
  points (by design) + 2 at a route START in water.
- **Route start in water** = TrimPathToRadii's interpolated radius-edge
  point. Fixed in c980a8e (`TrimmedRouteEndsObeyTheWaterlineFloor`); with
  the IsPathablePoint floor this closes the endpoint class.
- **rev 5 (c980a8e, direction down):** knee-deep terrain (≥ waterline −
  0.8) is a leveled ford, not a road in water; FordCount counts runs ≥ one
  cell (df5ae66 had inflated it to 173). Every validator change now
  records the direction it can move counts (NAS standing instruction).
- **df5ae66 network:** 98 routes, 69 components, 17 crossings (the
  Bonemass → GoblinKing double crossing is gone), six wide bridges
  (63/63/82/80/92/121 m). Cost change worked as intended.

Still open after this round: crossing 14 (BlackForest wood, 43 m) has a
6.2 m bank delta at the SHORES (from 37.5) although the cell delta passed —
a cliff bank; needs a shore-delta check or scenario. Crossing 1 (Swamp
wade route, 7.9 m delta) unchanged. Crossing 8 fromY 30.2 (old c6 site)
still below the floor — re-check after c980a8e.

### NAS gate on f42428d (RoadTestAuto1): PASS, hash moved, one reading that matters

82/82. pointsHash f0d424fc → **87b4ba7e** (second run in flight; recorded
only when reproduced), routes 81 → 87, components 54 → 69, crossings 3,
fords 61 (era 3), pieces 2256 → 2660 across 210 zones, planned == spawned,
violations 1 → 0.

**The zero is not the standing violation being fixed.** The log carries
`Could not find path: MountainCave02 -> Mistlands_DvergrTownEntrance2`:
the route that held the dry-land point no longer paths at all, a
legitimate consequence of the last-resort bridge cost, not evidence the
dry-land problem was solved. A passing gate cannot tell "clean roads"
from "road gone"; the NAS went and looked. Recorded as the rule: a
violation that disappears must be traced to its route.

Two product readings for Tys (both worlds now agree):
- **3 crossings against 61 water runs** on the fixture: the last-resort
  cost produced three bridges and fords everywhere else. Intended balance?
- **Components rise when wet route ends are dropped** (54 → 69 here,
  69 → 78 on RoadTestMac1): systematic, not one map. Trim to
  disconnection, or re-route to a dry terminus — different products.

### Where the branch settled (c980a8e) and what the ford check found

RoadTestMac1 regenerated at c980a8e: **hash e8ae009d**, 98 routes, 78
components, 16 crossings, 363 stair runs, 3936 pieces planned, violations:
the one standing slope point. Backed up under
`validation-results/worlds/RoadTestMac1-regen-c980a8e/`. This is the Mac
baseline; the causing change is the range a63f6fb..c980a8e (tip c980a8e).
Components rose 69 → 78: dropping wet route ends can disconnect a route
that used to end in the water (harbour-type locations). Open: decide
whether such routes should end at the last dry point (current) or be
dropped. fordCount now means "water runs ≥ one cell" (119 here);
comparable only from c980a8e on.

**NAS reservation on rev 5, checked live** (stand on the point, read the
ground): at (−4590,−1448), a zone generated after the road existed,
leveling raised the ground 29.6 → 30.8 — above the water (30.0) but below
the 31.25 clearance: a dry, marginal ford. At (404,571) the ground is
still raw 29.2: that zone was generated on an earlier visit and **terrain
edits do not refresh when the network regenerates** — the old crossing's
zone never received the new ford's leveling. Two consequences:
1. rev 5's assumption holds only on fresh zones. The validator cannot
   check road height (route points carry blended terrain height), so the
   observable proxy lives one layer down: `FordRoadSurfaceStaysAboveTheWater`
   (f42428d) asserts the smoothed road grid keeps a knee-deep ford ≥ 0.5 m
   above the waterline; it passed without a generator change, and the two
   live readings (30.55, 30.8) agree. Rev 5 stays, now justified by a
   measured guarantee rather than an assumption. Branch tip f42428d, 82/82.
2. **World-reuse caveat (time economy):** a reused world is fine for
   routes, crossings and ruins (`road_ruins_reset`), but terrain leveling
   and paint in previously visited zones are stale after a regeneration.
   Terrain verdicts need a fresh world or unvisited zones.
Also observed: the road spline through a knee-deep ford sits at
30.55–30.8, i.e. the ford deck is only 0.6–0.8 m above the water. Tys to
judge whether a ford should be raised to bank height (embankment) or stay
low (wet-footed ford).


## 2026-09-02 wood-bridge feedback round 1 + wide sailable rivers (pc/snap-point-composition 6f1dc31..9dd8f2f)

Tys's feedback on the RoadTestMac1 wood sites, and what each became:

| feedback | root cause | change (harness test) |
|---|---|---|
| c0: bridge not connecting to the painted road; deck into the hillside | crossing spanned bank-to-bank between the path's last DRY cells, 8–10 m up each bank | banks = last point that can legally carry road, walked in from the dry cell (`CrossingSpansOnlyTheWater`); painting and stair runs extended to the abutments (`PaintedRoadReachesBothAbutments`) |
| c7 ("c9"): road missing beyond the bridge | same: far dry cell 8 m from the water, nothing painted in between | same |
| c4: bridge where a land route looked possible | there was no dry route: the channel was continuous; the visible "land" was the dry approach inside a 36 m crossing over a 15 m pond | same trimming; `LandRouteBeatsBridgeWhenAGapExists` characterises A*: a dry gap always beats a ford (penalty 5000 vs 1 per metre) |
| c6: bridge over land | 16 m gully with a knee-deep trickle | bed within 0.8 m of the waterline and no fairway → not a crossing, road leveled through as a ford (`ShallowGullyIsAFordNotACrossing`) |
| no 60–100 m sailable rivers crossed | ford cap 48 m; wider rivers split the network | bridge jump ≤ 128 m at penalty 20000, bank delta ≤ 2.5 m; 20 m navigation gap kept clear of piers and deck (`WideRiverBridgeTests`, 4 tests + render) |
| (found in the render) bridge crossing obliquely | scan only started from river CORE and knight-move jumps dodged the bank cell's rough-ground cost | scans start from any water neighbour, cross shallow margins, must pass over river core, principal directions only (`BridgeTakesTheShortestPerpendicularJump`) |

Also: the character died during the reload check because that script
skipped `cli_set_player_safety`; every launch script now sets god + ghost
and the PassiveMobs world key right after the player spawns.

RoadTestMac1 regenerated at 99cdf54 (quit → relaunch → regenerate):
hash **6b13b3d1**, 98 routes, 68 components, **19 crossings** (was 9),
eight of them 81–121 m sailable-river bridges (fairways 39–90 m), 349
stair runs. Backed up under `validation-results/worlds/RoadTestMac1-regen-99cdf54/`.
This world's baseline moves to 6b13b3d1 with cause 99cdf54 recorded.

Open after this round (live, from `road_spots` / selftest):
- Two banks still below the waterline: crossing 4 toY 29.3 (two dry-land
  points at 29.2, Bonemass → GoblinKing) and crossing 12 fromY 30.2. The
  above-water walk stops at the path end / previous crossing; likely a
  route ending at the water. Needs a scenario.
- Crossing 5 (Swamp, 63 m, bank delta 7.9 m) is a wading route through
  swamp water, not a jump, so the bank-delta rules never saw it. The
  bridge there will stilt. Directive-1 scenario for swamp-wade crossings.
- A leveled ford through a knee-deep gully is an embankment up to ~3 m
  high (road spline at bank height); Tys to judge the look.
- Design choice to confirm: the 20 m navigation gap (vs the whole deep
  bed) is what makes a wide-river ruin still read as a bridge.
- Live decay census for a wood bridge (ledger 9) still owed.

## 2026-09-02 pass (Mac cockpit): 659fb63 merged, 2f04fca gated + seen, crossing-site selection landed

State after this pass:
- `integration/upstream-prs` = 659fb63 (fast-forward, gated tree) + ae2627b
  (`scripts/redo-shots.sh`). Pushed. Harness 62/62 both runtimes. No
  re-gate requested (same tree the NAS gated as f0d3cf7).
- `pc/snap-point-composition` = 2f04fca + 2e81447 (`road_clear_view`,
  richer `road_spots`) + a63f6fb (crossing-site selection). Pushed.
  Harness 67/67 both runtimes.
- valheimCLI: `cli_freefly_pose` committed locally only (b897179), no fork.
- **Scope decision (Tys, 2026-09-02): wooden bridges first.** Iterate the
  MeadowsWood kit to "reads well" with Tys's style feedback between
  rounds; stone kit and arches parked, findings below. Crossing-site
  selection stayed in because it is kit-independent and a wood site (c6)
  showed the defect.
- **Time economy (Tys, 2026-09-02):** every in-game step must be cheap.
  RoadTestMac1 is backed up post-generation (untracked) and
  `ForceRegenerate = false` is set, so later sessions RELOAD it (crossings
  and stair runs persist) and use `road_ruins_reset` for layout iteration;
  regenerate only when the network itself changes (the next live run must,
  because a63f6fb moves routes). Capture: 3 fixed shots per site, ~25 s
  per site, soak overlapped with image reading, reads capped at 15.

## 2f04fca gate (NAS, 2026-09-02) — PASS, weak arch coverage

Requested from the Mac, run by the NAS against the fixture world restored
from the pristine master (build `gate/2f04fca`, 0 warnings):

- Harness 63/63 (net10.0), +1 vs 62/62 at f0d3cf7 = the SnapChainTests
  arch test.
- pointsHash `f0d424fc` UNCHANGED (routes 81, points 5948, components 54,
  fords 2). Arches touch assemblies, not geometry — confirmed.
- Pieces 2254 → 2256 (+2) across 178 zones (zone count unchanged).
  Ruins check PASS: planned 2256 == spawned 2256, per zone and aggregate.
  Existence only, NOT survival.
- `stone_arch` resolves (in the mod's `[PREFABS] found` list); zero
  prefab-not-found warnings.
- Known violation unchanged (dry-land MountainCave02 →
  Mistlands_DvergrTownEntrance2, point 103, 29.9 vs 30.25) → passed:false
  for that alone.
- Bookkeeping closed on the NAS side (homelab 5f01118): baselines now
  record piece count with provenance (2254 @ f0d3cf7, 2256 @ 2f04fca) and
  the coverage caveat, so both verification axes are machine-checkable.

**Caveat:** the fixture yields TWO arches, so the gate exercises the arch
path twice. It proves the path runs and the prefab resolves; it does not
cover varied bank geometries or the near-ford guard. Visual verification on
the Mac carries the arch-correctness claim, not this run.

## 2f04fca visual verification — Mac, RoadTestMac1 (2026-09-02)

World: RoadTestMac1 (fresh; hash d49047f1; 92 routes, 9 crossings,
292 stair runs, 2745 pieces planned; 1 slope violation Crypt4 →
MountainCave02 point 314 grade 1.93). This world's own baseline — never
compare its numbers with RoadTestAuto1's. **Baseline provenance: a RELOAD
with ForceRegenerate=true (quit, relaunch, regenerate) reproduced
d49047f1 byte-for-byte — creation == reload on this world, the second
negative case for the divergence after RoadTestPC1 (RoadTestPC3 and the
NAS fixture diverge; trigger still unpinned).** Reload state backed up
under `validation-results/worlds/RoadTestMac1-reload-2f04fca/`. Cost of
that check: ~5 min wall-clock incl. relaunch. Backed up post-generation under
`validation-results/worlds/RoadTestMac1-postgen-2f04fca/` (untracked) so
later sessions RELOAD it with ForceRegenerate=false instead of regenerating
(crossings and stair runs persist on the metadata ZDO).

Coverage caveat, same shape as the NAS's: this world has ONE stone-kit
crossing (crossing 3, Plains), so the arch path ran once here and twice on
the NAS fixture. Two worlds, three arch instances. Not broad coverage.

Sites (3 of 9 crossings; 13 of the 15-image budget read):

| site | kit | width | banks (from/to) | dY | verdict |
|---|---|---|---|---|---|
| c0 Meadows (476,628) | wood | 34 | 37.1 / 36.4 | 0.6 | GOOD — coherent trestle |
| c6 BlackForest (6292,5201) | wood | 16 | 29.0 / 34.4 | 5.3 | UNREAD (framing); 26 pieces stand; from-bank below water |
| c3 Plains (-6592,-3560) | stone | 16 | 29.4 / 33.6 | 4.3 | ARCH UNREADABLE; from-bank below water; soak 15/15 |

Checklist, c0 (wood, the control): deck grades bank-to-bank down from the
rocky bank on paired posts — PASS; no stilt tower — PASS; fairway clear
(collapsed span is over the channel, far-bank posts stand alone) — PASS;
seams closed at this distance — PASS; ruin reads intentional — PASS; no
floating pieces under raking light — PASS. The snap-chain composition
(659fb63) holds up on a level, wide wood crossing.

Checklist, c3 (stone, the arch site): census within 30 m — 8 stone_wall_2x1,
3 stone_floor_2x2, 2 stone_stair, 1 stone_wall_1x1, **1 stone_arch at
(-6592, 33.0, -3567)** = the tall (to) bank, y exactly bankGround 33.6 −
0.1 − 0.5. The low bank (29.4 < water + 0.8) correctly emitted no arch:
the near-ford guard fired. Existence and placement math: PASS.
- Springs from abutment top — **FAIL as a read**: the arch is embedded to
  0.1 m below grade by design, so in-game it is a ~1 m dark lump beside the
  abutment slab, hidden by Plains grass and a cloudberry bush. Nothing
  reads as an arch, let alone a broken arch bridge.
- Geometry matches span / no clipping — cannot judge; nothing visible to clip.
- Deck grades — the "deck" here is pier stubs (stone_wall_2x1 at 28.8–31.9)
  rising from a stream 16 m wide with one bank under water; no deck to grade.
- Fairway clear — N/A (stream too shallow to carry a fairway).
- Ruin state — reads as scattered blocks, not a collapsed bridge.

Interpretation (directive 1): c3 and c6 both have a bank point BELOW water
level (29.4 and 29.0 vs 30.0) and 4–5 m bank deltas. These are
crossing-site defects, not composition defects: the detector accepted a
"bank" that is in the water, so the low abutment sits in the stream and the
deck grades from water level up the far bank. Do not polish arches at these
sites. Recorded for the harness-first fix: bank-height delta 4.3 / 5.3,
low bank −0.6 / −1.0 relative to water.

Composition notes for the next arch iteration (independent of site fix):
1. A single 2 m stone_arch sunk to grade cannot read at bridge scale. The
   arch must spring from the abutment/pier TOP and rise above the deck
   line: seat it on the abutment slab (or the first pier column), tapered
   end outward and UP, and chain 2–3 arches for spans ≥ 12 m. Grounding
   should come from the pier stack below it, not from burial.
2. Harness assertion to add alongside: "exposed fraction" — the arch's top
   edge must sit ≥ 1.0 m above the bank surface at the tip position
   (readability), not just at-or-below grade at the face (grounding).
3. Rather than a maximum, the existing wood trestle grammar (c0) is the
   reference for what "reads": pieces stacked on each other from ground up.

c6 correction: the three shots showed a pool and two posts, but the census
(30 m) lists 15 wood_pole2, 5 wood_beam, 4 wood_floor, 3 wood_stair — a
trestle climbing from 27.3 to 34.2 inside a sunken gully. The camera at
mean-bank + 6 m looked OVER the gully rim; the structure was below the line
of sight. Framing lesson (in the script now): sunken sites need the look-at
point at the LOW bank + 2 m and a standoff scaled to the crossing width.
c6's composition is therefore unverified, not failed. Site verdict stands.

**Decay soak (c3, stone, the only arch site):** player within 10 m for
8.7 min loaded (12:49 → 12:58). Census t0 == t8, position-level identical:
8 stone_wall_2x1, 3 stone_floor_2x2, 2 stone_stair, 1 stone_wall_1x1,
1 stone_arch. **Survival 15/15** for the stone kit incl. the buried arch —
the first live survival claim for stone. (Wood survival at c6: 26 pieces
standing ~12 min after first spawn; planned-count for the zone was 27+2+1,
so at most one piece is unaccounted for and it may simply be outside the
30 m census sphere. Not a clean claim — record as "no observed collapse".)

## Harness-first work from this pass (branch pc/snap-point-composition)

Landed (commit "crossing-site selection", 67/67 net10.0 + net48):
- Ford acceptance refuses bank deltas > 4 m (`MaxFordBankDelta`) and
  charges `FordBankDeltaPenalty` (1250/m²) on accepted fords, so the search
  seeks near-level banks. `CrossingSiteTests.FordSeeksLevelBanks…` is the
  directive-1 scenario: stepped-bank river, level crossing only 150 m
  south of the straight line — the route detours there.
- `RoadCrossingDetector` records banks as the first path points ≥ 31.25
  (waterline + clearance), fixing the abutment-in-the-water defect
  (`CrossingBanksStandAboveTheWaterline`).
- **pointsHash WILL change** on every world at this commit (routes move).
  Re-baseline + record the commit in each world's `measured` field (NAS
  `_expected_hash_changes`). Keep the axes straight: this moves the hash
  with pieces following routes; the arch commit moved pieces only.
- NOT yet verified live (needs regeneration; batched into the next live
  run with the questions below). Deploy at the next session start.
- Generation-time cost of the change: none measurable on the harness — the
  pathfinder tests time the same before and after (same load conditions,
  game running), and the four new scenarios take 2–40 ms each.

Also landed: `road_clear_view <x> <z> [r]` (debug-gated vegetation/rock
clearing for photography) and `road_spots` now prints kit, direction, both
bank heights and their delta — the delta is the site-selection signal.
valheimCLI gained `cli_freefly_pose` (local commit, no fork).

Questions only the next live run can answer (arrive with the list):
1. Does the RoadTestMac1 network still reach the same islands after fords
   with > 4 m deltas are refused, or do routes drop out? (route count vs 92)
2. Where does crossing 6's route cross now, and is the new site's bank
   delta < 2 m in `road_spots`?
3. Do the moved banks put every abutment on dry ground (`road_spots`
   fromY/toY ≥ 31.25 for all crossings)?
4. Wood kit only: does the c0-style trestle still read well at the new
   sites, and does it survive an 8-minute soak with a player present?

## Mock-gap ledger (2026-09-02, seeded by NAS + Mac)

Rule (directive 3): after EVERY live run, add an entry — what live revealed
that the harness failed to predict, and the shim/test/scenario that closes
the gap. Live validation is scarce; the mocks must improve with each run.

1. **OPEN (permanent boundary) — headless spawn proves EXISTENCE, not
   DECAY.** No players → no WearNTear evaluation, so headless survival
   always reads 100%. Survival claims require a live client with a player
   within range. This entry defines the boundary of everything below it:
   any "survives" claim in this file must cite a live-client soak.
2. **CLOSED — the gate once scored plans, not pieces.** `[RUINS] planned
   1592 / 0 spawned` still passed. Closed by `proads-ruins-check.py`
   asserting planned == spawned per zone and in aggregate (NAS).
3. **OPEN — the harness cannot tell you a feature was under-exercised**
   (NAS, 2f04fca gate). 63/63 and a clean ruins check look identical whether
   the fixture instantiates 2 arches or 200; the piece delta is the only
   signal and it is easy to read as merely "the expected change". Closing
   move: per-feature instance counts in the gate output (arches, beams,
   stair supports) with a minimum-coverage assertion per kit, and a
   SyntheticWorld crossing scenario per guard (near-ford bank, mismatched
   banks) so each code path is exercised headlessly regardless of what the
   fixture world happens to contain.
4. **OPEN — the harness asserted grounding, not readability.** The arch
   test proved the piece top sits at/below grade (grounded) and live showed
   exactly that: a lump under grass. Closing move (when the stone kit is
   re-opened): an "exposed fraction" assertion per decorative piece — top
   edge ≥ 1.0 m above the ground at the piece's outward end — alongside
   the grounding check. Grounding and reading are two assertions.
5. **CLOSED — bank points below the waterline.** Live: crossings 3 and 6
   recorded banks at 29.4 / 29.0 with water at 30.0; the harness's
   SyntheticWorld banks happened to be dry. Closed by
   `CrossingBanksStandAboveTheWaterline` (marshy-shelf path) and the
   detector fix.
6. **CLOSED — no scenario had mismatched banks.** Live: 4.3 / 5.3 m
   deltas at two of three sites. Closed by the stepped-bank river scenario
   and the ford delta guard/penalty (`CrossingSiteTests`).
7. **OPEN — the harness has no notion of a sunken site.** c6's trestle sits
   in a gully whose rim hides it from a rim-height camera; nothing in the
   harness models "can this be seen". Not a solver gap — a capture-process
   gap, closed on the script side (look-at at low bank + 2 m, standoff by
   width). Recorded so nobody re-learns it.
8. **OPEN (tooling, not solver)** — `tod 0.25` is pre-dawn in this build,
   weather takes ~5 s to blend after `env Clear`, HUD hiding needs an
   Accessibility grant, and the CLI returns each command's output one call
   late. All four cost a wasted batch; all four are in the script now.

9. **OPEN — a scope change inverted which evidence is load-bearing** (NAS
   observation). The parked stone kit has the clean survival datapoint
   (15/15, 8.7 min, player present); the prioritised wood kit has the soft
   one (26 standing at c6, not a clean count). Nothing about the evidence
   changed; the priority did. Closing move: the next live session takes a
   clean wood decay census EARLY (anchored census at t0 and t8 at one wood
   site, player within 10 m), before any wood iteration is judged.

10. **OPEN (permanent boundary) — a restored fixture is a reused world.**
    The NAS gate restores RoadTestAuto1 from a pristine master and
    regenerates on load; terrain edits from the master's own generation
    are not re-applied, so the gate can validate routes, crossings, ruin
    plans and spawns, but NEVER leveling or paint on that fixture, no
    matter what assertion is added (a leveling check would go green on
    stale terrain and say nothing). Terrain verdicts need a freshly
    generated world or unvisited zones. Recorded beside entry 1; both are
    invisible from a passing run.

## Public-surface audit (directive 2, 2026-09-02, read-only — HOLD respected)

Inventory of what is visible upstream and on public fork branches:

- **PR #18 body** (jneb802/ProceduralRoads): synthetic-world renders only
  (`debug-topology.png`, `debug-world.png`, served raw from fork branch
  `assets/pr-images`). Test counts and harness description. No world names,
  seeds, hashes, machine names, or infra. **Clean.**
- **PR #18 comment (2026-09-01)**: two characterization findings
  (TotalRoadPoints 552→628 across round trip; TrimPathToRadii untrimmed
  case). Test-scenario numbers only. **Clean.**
- **#16 comment**: intent + two questions. **Clean.**
- **`assets/pr-images`**: full source snapshot at the harness commit plus
  the two synthetic PNGs. Nothing world-specific. **Clean.**
- **`integration/upstream-prs` (public, tracked)**:
  `validation-results/RoadTestAuto1.routes.csv` (409 KB, every route
  centerline of the fixture world) and `RoadTestAuto1.selftest.json`
  (pointsHash 9651c22d, 24 violations with world coordinates). These were
  committed at/before a363e83 despite `validation-results/` being in
  `.gitignore` (tracked files override the ignore). **Finding: the
  "validation-results stays local" rule is already violated by two files.
  Recommend `git rm --cached` both on integration and let the ignore hold.**
  Internal-test detail level: fixture-world route dump; no infra. Low
  sensitivity, but it is exactly the class we said stays local.
- **`pc/snap-point-composition` / `pc/screenshots` (public)**:
  `validation-results/screenshots-pc/README.md` — world names
  (RoadTestPC1/2/3), hashes (c4d6271b, c9c3014e, 49748415), piece censuses,
  the 8/18 stair decay measurement, the creation-vs-reload divergence
  finding, and a blunt visual assessment. No infra details. The 18 JPEGs it
  references were purged by the PC's rebase (confirmed: no image blobs on any
  live fork branch). **Finding: durable findings parked in a results README
  (divergence account, decay numbers, prefab-name mismatch) — moved into
  TODO.md per the reconcile rule; the README should become a pointer once
  docs/validation-gap merges.**
- **`docs/validation-gap` TODO.md**: mentions a NAS wake-request path
  (`/data/wake-requests/gaming-pc`) and the wake mechanism. Not an address
  or credential, but it is an internal path on a public branch. **Minor;
  consider generalizing the wording.**
- Infra grep (Tailscale/4via6 addresses, 192.168.*, MACs, `/Volume1`,
  ssh/PermitRootLogin) across all seven public fork branches: **no hits.**
- No image blobs (jpg/jpeg/png) on any live fork branch except the two
  synthetic renders on `assets/pr-images`.

### Flagged for Tys, unstarted by design
- Tiebreak fix go/no-go (weakened-invalidation context: RoadTestPC1 showed
  creation == reload, so the fix may invalidate only a subset of baselines
  and the stable-sort hypothesis may not be the whole mechanism).
- valheimCLI fork (cli_freefly_pose sits in a local commit).
- Physical PC BIOS visit; NAS `PermitRootLogin yes` (no sshd change made).
- Scheduling `docs/validation-gap`: it is NOT docs-only (ReachableRoadsTests.cs,
  RoadTopologyTests.cs changes) and is stranded until merged; when it
  merges, `validation-results/screenshots-pc/README.md` becomes a pointer
  to this file (TODO.md wins).
- Public-surface audit findings above: the two tracked
  `validation-results/` reports on `integration/upstream-prs` (recommend
  `git rm --cached`), and the README parked on the pc/* branches.
- Arch composition: parked. When re-opened, the direction is "spring from
  the abutment TOP, rise above the deck line, chain 2–3 per span", with the
  exposed-fraction harness assertion first.

## Validation gap: the gate scores the plan, never the survivors (2026-09-01)

The NAS selftest can report PASS with 0 violations on a network that
visibly falls apart in game. Measured, not suspected:

- The Windows station instrumented a virgin Black Forest crossing:
  87 pieces spawned across two zones, and after ~6 minutes of loaded
  time 8 of 18 `wood_stair` pieces had self-demolished. The 61 poles
  (grounded) all survived. A crossing built in an earlier session had
  decayed to a T-post and two downed poles. This is vanilla WearNTear
  support collapse on the spawned ZDOs -- NOT gated behind
  DebugValidation, so unmodded clients get the same demolition show, and
  the network gets uglier on every visit.
- The headless gate cannot see any of it. A NAS run logs
  `[RUINS] planned 1592 pieces across 149 zones` and then zero
  `[RUINS] zone N: spawned ...` lines, because that message fires only
  from the `ZoneSystem.SpawnZone` postfix and a dedicated server with no
  players never spawns a zone. The gate observes a PLAN for 1592 pieces
  and never observes one existing, let alone surviving.

Stated plainly: "NAS selftest PASS" currently means "the layout solver
produced a plan it is happy with". It is not evidence that anything
stands up. Necessary, not sufficient -- keep requiring a visual pass
from the Windows station before believing a crossing or stair run is
good.

Two follow-ups, and they converge rather than compete:

1. The decay fix IS the snap-point work already queued below. Snapped,
   grounded chains are how vanilla propagates support, so supporting the
   pieces and aligning them are the same job, not two.
2. Closing the gate's blindness needs headless zone spawning -- the SAME
   capability `road_bake` needs in Tier 1 (iterate zones and write ZDOs
   with no player present). Once zones spawn headlessly, a piece census
   becomes a real assertion: spawn the zones a route crosses, hold them
   loaded, and require the survivor count to match the planned count.

Also note the selftest hash is seed-dependent, not a coverage measure:
the RoadTestAuto1 fixture and the NAS organic world RoadHeadless1 both
pass with 0 violations, while the Windows organic world RoadTestPC1
fails with 3 (two slope, one dry-land). One world is a sample. Catching
this class needs several seeds, not a different world type.

## Creation-vs-reload hash divergence (2026-09-01, mechanism open)

A world's CREATION load can produce a different road network than every
subsequent RELOAD of the same world, with reload-to-reload byte-identical.
Measured on the Windows station: RoadTestPC3 creation c9c3014e (81 routes,
2146 planned pieces) against reload 49748415 (84 routes, 2949), verified
twice. Crossings, fords and stair runs are identical across all loads --
only which route attempts SUCCEED shifts. RoadTestPC1 did not diverge at
all, so the triggering condition is unpinned.

The NAS fixture shows the same shape in the other direction: the recorded
baseline is 6d63bd64 / 83 routes, while a reload from a pristine restore
gives f0d424fc / 81 routes, reproduced twice on a known commit. A code
change is RULED OUT -- the only commits between the two measurements are
d5f3ca3 (no mod source at all) and a363e83 (33 lines in ConsoleCommands,
no generation path).

Directions differ (+3 routes there, -2 here), which rules out a monotone
mechanism but NOT a single one -- order-instability produces shifts in
either direction depending on which marginal candidates consume the
budget first.

**A concrete mechanism exists in the code, though it is not yet proven to
fire.** `GatherLocationData` builds `allLocations` by iterating
`ZoneSystem.instance.GetLocationList()` with no sort applied. Everything
downstream orders with LINQ `OrderByDescending` on location priority or
island area -- and LINQ's ordering is STABLE, so locations sharing a
priority tier (the common case) retain their INPUT order. If
GetLocationList returns a different order on a freshly generated world
than on one loaded from a save, that difference survives every sort and
changes the sequence in which the per-island pathfinder budget and
MaxLocationsPerIsland are consumed. Marginal routes then flip either way.

Two follow-ups, in order of value:

1. **Candidate fix, not just a diagnostic**: give the ordering an explicit
   deterministic tiebreak -- e.g. `.ThenBy(position.x).ThenBy(position.z)`
   or by prefab name -- so a tie can never inherit input order. If the
   divergence disappears, the mechanism is confirmed and fixed in one
   change. This is cheap and worth trying BEFORE instrumenting.
   **Every baseline resets the moment this lands.** A deterministic
   tiebreak changes the network for any world where priority ties exist,
   which is probably all of them -- so the Windows station's 49748415,
   the NAS fixture's f0d424fc, and any historical number all become
   stale simultaneously. A hash change after that commit is the fix
   WORKING, not a regression, and the first person to run the gate
   afterwards will see a mismatch warning that means the opposite of
   what it usually means. Correct post-fix procedure: apply, generate a
   fresh world, RELOAD it, take the reload hash as the new baseline, and
   then verify creation and reload now AGREE. That agreement is the
   actual proof the fix worked -- it is a stronger claim than any single
   hash value, because it tests the invariant rather than a sample of
   it.

2. **Discriminating experiment** (Windows station's design): log a digest
   of the location list at generation time -- count plus an ordered
   position hash -- and compare creation against reload. Digest differs
   while the SET is identical means order; a differing set means
   provenance, which the tiebreak above would not fix.

Upstream-relevant: determinism claims in issue discussions assume
load-invariance, and this is a counter-example with a reproducible
witness.

## Next up (2026-09-01)

- **Bridge & stair composition pass** (from first in-game screenshots):
  solver emits per-station ASSEMBLIES, not single prefabs — paired posts
  + cross-beam + deck resting on beam + side rails; deck height GRADES
  bank-to-bank (no stilt towers from bank-height max); stairs hug terrain
  (allow narrow terrain-cut ribbon under steps, cap stair grade ~0.8,
  switchback the stair path itself when steeper); dress stairs with side
  posts/rails. Iterate on the PC with road_regen_island + close-up
  screenshots; gate merges on NAS selftest PASS (hash may change — routes
  don't, assemblies do not touch geometry, so 6d63bd64 should hold).


Strategy: upstream now owns network topology (anchor/endpoint selection,
location priority API, route export). We own **path quality and crossings**
(`RoadPathfinder`, `RoadTerrainModifier`, debug tooling) — areas upstream is
not touching, so everything below stays merge-friendly and independently
shippable, potentially as upstream PRs.

## Design constraint: vanilla-data compatibility (Xbox / unmodded clients)

Everything the mod writes must be **vanilla world data** so unmodded clients
(Xbox crossplay) see the results. This already holds today:
`RoadTerrainModifier` writes `m_levelDelta` / `m_modifiedHeight` /
`m_paintMask` into `_TerrainCompiler` ZDOs — the same format as hoe/pickaxe
edits, which sync and persist for all clients. Keep it that way:

- Bridges/ruins (Tier 3): **vanilla prefabs only** (ruined stone/wood
  pieces). Custom prefabs are invisible or broken for unmodded clients.
- Road surfaces (Tier 2): **vanilla paint channels only**
  (paved/dirt/cultivated) — conveniently exactly what the biome-surface
  idea needs.
- Vegetation clearing must land as ZDO state from a modded peer generating
  the zone first (upstream `RoadVegetationCleaner` does this) — an unmodded
  client generating a virgin zone spawns trees on the road corridor.

## Base

DECIDED (2026-08-31): build feature work off warp-71. Probe results: both
upstream branches merge cleanly onto master with zero conflicts; the harness
compiles against the merged code with minor shim additions (committed); the
pathfinder tests pass unchanged. `integration/upstream-prs` = this branch +
PR #16 + PR #17 — Tier 1 work happens there. The harness-first PR still
targets master (tests what master has today); its tests are base-agnostic
(legacy Chain/MST tests no-op post-warp-71, GenerateReachableRoads tests
no-op pre-warp-71). Watch PRs #16/#17 for rebases before building further.

- [x] Branch from `origin/master`, then evaluate merging upstream branches:
  - `origin/feature/warp-71-mwl-road-api` — island anchor selection,
    nearest-pathable-point, priority-registered locations (Jul 2026).
    Supersedes our old "POI as island root" / "safe start point" work.
  - `origin/feature/road-route-export` — `RoadRoute` ordered centerlines,
    route persistence, vegetation clearing, `road_pins` commands (Jul 2026).
  - `origin/fix/WARP-54-road-copy-regeneration`, `origin/fix/ptb-zone-system-vector2s`
    — small fixes worth carrying.

Reference for porting: branch `wip/june-2026-pathfinding-terrain` holds the
June work. Do NOT port its two known bugs:
1. A* open-set update in `RoadPathfinder.FindPath` re-reads `gCosts` after
   overwriting it, so stale open-set entries are never removed (use the
   already-captured `existingG`).
2. `GetSafeIslandStartPoint` calls `island.ContainsPoint(Vector2)`, which
   implicitly converts to `Vector3` with z=0 — tests the wrong world point.
   (Moot anyway: superseded by warp-71 anchors.)

## Test harness (spiked 2026-08-31 — `ProceduralRoads.Tests/`)

Headless xunit project (net48 under Mono + net10.0) that compiles the real mod sources against
tiny shims for Unity math, `Vector2i`/`Heightmap.Biome`/`WorldGenerator`, and
BepInEx logging — no game, no Unity, runs in <1 s. `SyntheticWorld` provides a
deterministic pseudo-Valheim island (dome + mountain ridge + meandering
river); `WorldRenderer` draws world + paths + markers to a BMP for visual
inspection. Run via `ProceduralRoads.Tests/run-tests.sh` (both runtimes) or
`dotnet test -f net10.0` (fast loop; bare `dotnet test` aborts on the net48
target on macOS). net48 runs under Homebrew Mono 6.14 — closest available
match to Valheim's in-game Unity Mono runtime. Includes a characterization test pinning the current "river makes
far side unreachable" behavior — flip it when the cost-model rework lands.

First-PR scope decision (2026-08-31): the first upstream PR is this harness
characterizing existing behavior — before any new road code. Now also
compiles `RoadNetworkGenerator`/`RoadSpatialGrid`/`BiomeBlendedHeight`/
`IslandDetector` and pins the Chain/MST topology strategies (hub/spoke vs
path, greedy-vs-MST length, orphan roads after failed edges).

- [ ] Extend harness to cover `RoadSpatialGrid` smoothing and
  `RoadTerrainModifier` blend math (add shims as needed).
- [ ] Grow `SyntheticWorld` scenarios per feature (swamp lowlands for wading,
  narrow-vs-wide river for crossings, steep dome for switchbacks).
- [ ] **Deferred until debugging needs it — real-terrain fidelity, staged:**
  1. `road_dump_world <center> <size> <step>` console command sampling real
     `GetHeight`/`GetBiome`/`GetRiverWeight` over a grid to a file, plus a
     `FixtureWorld : WorldGenerator` shim replaying it with bilinear interp.
     ~2–4 m step suffices for 8 m pathfinder cells. Turns any bug report
     into a permanent regression test (e.g. issue #7's seed `nRleKzu9bI`).
  2. Only if tuning-by-statistics over many seeds becomes the goal: port the
     real worldgen pipeline (Unity PerlinNoise = classic Perlin, Unity
     Random = Xorshift128; parity proven feasible by valheim-map.world)
     behind the same shim, validated against the step-1 fixture dumps.
  Keep the synthetic world for feature tests either way — constructed
  scenarios (narrow river, steep dome) stay more legible than real terrain.

## Tier 1 — differentiated core

- [ ] **Port `road_pin_start` / `road_test` console commands first**
  (from wip branch, `ConsoleCommands.cs` + `GenerateTestRoad`). The
  30-second in-game test loop makes every other item cheap to iterate on.
- [x] **Pathfinder cost-model rework** — DONE on `integration/upstream-prs`
  (06abd0d): additive costs, `float.PositiveInfinity` blockers, swamp
  wading, mountain slopes expensive-not-impassable, ford crossings <= 48m
  at RiverCrossingPenalty, A* open-set fix, RiverPenalty 100000 -> 4000.
  21/21 tests. This is the PR 2 payload (targets master; pathfinder files
  identical on both bases). Terrain-quality follow-up (ea37bca) after the
  first real-world selftest: waterline clearance + move interior sampling
  (roads keep feet dry between cell samples) and along-path grade shaping
  (MaxTraversableGrade cap + per-meter quadratic cost → switchbacks).
- [ ] **River crossings with `RoadCrossing` metadata.** June version proved
  A* can jump rivers (≤6 cells / 48 m at a penalty). Missing piece: record
  `{ type, center, fromBank, toBank, direction, width, biome }` and **split
  the painted road at the banks** instead of carving a causeway through the
  water. Road-ends-at-bank / resumes-at-other-bank reads as a ruined
  crossing even before bridge prefabs exist.
- [ ] **Road cross-section + endpoint ramps** (port from wip branch):
  flat core (~65% width, `RoadFlatCoreRatio`) with smoothstep shoulders
  replacing `pow(x, 0.1)` blend; 40 m endpoint ramps blending raw→smoothed
  height. The change players actually see. Key design detail from the June
  code (confirmed 2026-08-31): **level wider than you paint** — leveling
  extends to halfWidth + 2 m blend margin, full paint only in the flat
  core, paint fading across the shoulders, so roads read as a solid dirt
  strip with smoothed grass verges. Make the unused `RoadShoulderOuterRatio`
  a real knob for shoulder extent, and consider painting even narrower
  than the core for wider RoadWidth configs. Fix in passing:
  `ResampleTrimmedEndpointHeights` moves endpoint X/Z (misnamed, and
  grid-snap + 8 m trim buffer can leave visible gaps at locations); drop
  or rework the snap.

- [ ] **`road_bake` command — pre-bake the network for unmodded clients.**
  Iterate every zone with `RoadSpatialGrid` points and write its
  `_TerrainCompiler` ZDO (terrain deltas + paint) directly into the world
  save, without a player visiting — plus corridor-only vegetation clearing.
  JereKuusela's Upgrade World proves headless ZDO writes on unloaded zones
  work; its `generate` command is also the zero-code interim workflow
  (modded crossplay server, pre-generate road zones once, Xbox players see
  roads forever after). Keeps the save small vs. full-world pregeneration.

## Tier 2 — high visual payoff, builds on Tier 1

- [x] **Switchback shaping** — DONE (ea37bca): MaxTraversableGrade cap +
  per-meter quadratic grade cost. Real-world result: cliff-climb violations
  eliminated (24 -> 9 total), one network component per island.
- [x] **Stair runs on steep sections** — DONE 2026-09-01: detection at
  spline-scale grading, layout solver (terrain-tracking, support columns,
  deterministic ruin), terrain untouched under runs, validator exemption.
  Result: FIRST CLEAN PASS on the fixture world (0 violations, hash
  6d63bd64, 83 routes) — the NAS loop is now a binary regression gate.
  Emergent bonus: steep riverbanks become staircases descending to fords.
  Placement in-game ships with the bridge placement feature.
- [ ] (superseded design notes) **Stair runs**: three grade
  bands — 0-0.35 road (terrain modified), 0.35-0.5 STAIRS (no terrain mod:
  record StairRun metadata, place stair pieces hugging the slope at zone
  spawn — vanilla stairs are exactly 2m run / 1m rise = grade 0.5), >1.0
  impassable. Cost stairs above road, below long detours, so they appear on
  final approaches only. Reuses the bridge architecture (pure layout solver
  + SpawnZone placement + WearNTear ruin states — missing steps are jumps
  or cheap repairs). Kits by progression: wood stairs (Meadows/BF), stone
  stairs (Mountain), dvergr stairs/spiral (Mistlands — also the answer to
  the remaining 8 sub-cell grade violations on jagged Mistlands routes).
  Aesthetic bar: player builds using the Gizmo mod for fine rotations along
  the contour — procedural placement writes ZDO transforms directly, so we
  get exact per-piece yaw/height alignment for free (no build-UI snap), and
  unmodded clients render arbitrary rotations fine.
  Support grammar (2026-08-31): contour stairs cantilever on the downhill
  edge by construction (piece is level, terrain falls away) and WearNTear
  would demolish floating pieces — solver samples terrain under each
  piece's corners: uphill edge may clip into the slope, and any downhill
  gap > ~0.5m emits a support column to ground (wood poles / stacked stone
  / dvergr supports by kit). Ruin removal takes column AND the stairs it
  carried together — never leave a floating span. Tall columns on cliff
  faces are the drama: stilted flights are the signature epic-build look.
- [ ] **Mountain boulders vs roads** (designed 2026-08-31): boulders are
  per-zone vegetation spawned AFTER road generation, so pathfind-time
  avoidance requires replicating zone RNG — deferred as a research item
  (deterministic vegetation prediction via DecompilerServer; would enable
  road geometry reacting to rocks, e.g. a switchback at a monolith). Now:
  selective clearing at zone spawn via existing clear-area machinery —
  remove rocks intersecting the road CORE only, keep shoulder-overlap rocks
  (roadside monoliths are character), position-seeded keep-bias for edge
  cases so regeneration stays deterministic.
- [ ] **Biome road surfaces** — dirt (Meadows/Black Forest/Swamp), stone
  (Mountain), mossy stone (Mistlands). Mostly a paint-type lookup at
  terrain-mod time. Perlin-blended transitions = stretch goal.
- [ ] **PNG world/road renderer** — nearly free on top of upstream
  `RoadRoute`: headless dump + small script drawing islands, routes, roots,
  failures, crossings. This is the tuning tool for the two items above.
  (CSV exporter as originally planned is reduced to a thin dump command —
  upstream's route export did the hard part.)

## Tier 3 — later, once crossings exist

- [ ] **Ruined bridge foundations** (designed 2026-08-31) — vanilla pieces
  as ZDOs so unmodded clients see them, and players can REBUILD the deck on
  surviving piers:
  1. PR 3 prerequisite: `RoadCrossing { center, fromBank, toBank, direction,
     width, waterLevel, riverbedHeight, biome }` recorded at ford acceptance,
     persisted like routes (#17 pattern), surfaced in the selftest report.
  2. `BridgeLayout.Solve(crossing, seed)` — pure, harness-tested layout
     solver returning (prefab, position, rotation, healthFraction). Support
     grammar: piers are vertical stone stacks from the riverbed (ground-
     supported — WearNTear collapses floating stone on zone load), deck
     plates only directly on pier tops, no cantilevers. Ruin = deterministic
     missing pieces (mid-span deck preferentially gone, abutments mostly
     intact, pier bases never removed) + WearNTear health 30-70% for the
     cracked/worn vanilla damage visuals (health lives in the ZDO — syncs).
  3. Placement via the existing ZoneSystem.SpawnZone patch: instantiate with
     ZNetView on first zone spawn, set health, register clear-areas.
  4. Blending: abutment floors sunk ~0.3m below road surface at the banks so
     terrain/paint lap onto the stone; paint paved onto the abutment
     footprint.
  5. SAILING IS SACRED (decided 2026-08-31): do NOT modify the riverbed —
     no causeway, no rubble line; the channel keeps full depth. `RoadCrossing`
     gains a `fairway` (channel center + width from the deepest part of the
     river profile) and the solver keeps piers AND debris out of it; the
     missing deck section goes over the fairway (bridge collapsed exactly
     where boats pass). Ruin debris = tilted stone blocks in random
     orientations, settled into the bed near pier bases/banks only —
     visible underwater, never in the fairway (they are real colliders).
     Road users wade/swim the gap or rebuild the deck on the piers.
  6. Progression-aligned styles (decided 2026-08-31): material follows
     player progression via (biome, world ring) -> BridgeStyle lookup
     (warp-71 already computes rings; crossings record biome). Start with
     the SIMPLEST kit first: Meadows wood — pole pilings + plank deck,
     heavily decayed (rebuildable with day-one materials). Then:
     Black Forest log-on-stone-footings; Swamp rotten piling stubs only;
     Mountain/Plains stone piers + arches (monumental, half-toppled);
     Mistlands black marble, least ruined (dvergr still maintain theirs —
     vanilla dvergr marble bridges are the reference look, as Draugr
     village wood bridges are for Meadows). Decay scales inward: humblest
     and most-decayed at the center, grander and better-preserved outward —
     fits the ancient-network premise, and rebuild cost tracks progression.
  7. Verify exact vanilla prefab names + WearNTear thresholds against game
     data first (DecompilerServer).
- [ ] Gully crossings: detect via height-profile dip along the path (reuse
  crossing machinery, not a separate terrain scan).
- [ ] Edge landings as optional destinations → future docks/harbours.

## Contributing upstream (observed conventions — no CONTRIBUTING.md exists)

- Single-author project (jneb802/"warp"); all 13 PRs to date are self-PRs,
  so we'd be the first external contributor. Issues are answered actively.
- The two feature branches in our base are open PRs
  ([#16](https://github.com/jneb802/ProceduralRoads/pull/16),
  [#17](https://github.com/jneb802/ProceduralRoads/pull/17)) — unreviewed,
  unmerged as of Aug 2026. Watch for changes before/while building on them.
- Branch naming: `feature/<slug>`, `fix/<slug>`.
- PR body style: `## Summary` bullets + `## Validation` section listing
  `git diff --check`, `dotnet build ProceduralRoads.sln`, and in-game
  world-test results (world name + `road_pins` count).
- Commits: short lowercase imperative, occasional `fix:` prefix.
- Community validation for Tier 1: issue
  [#7](https://github.com/jneb802/ProceduralRoads/issues/7) (most islands
  get no roads; users burning 30k–100k pathfinder iterations for partial
  results) is exactly what the cost-model rework + A* fix address. Issue
  [#8](https://github.com/jneb802/ProceduralRoads/issues/8) (show full road
  path on map) aligns with the route-export/renderer work.

## Dropped from the old plan

- Island root / safe start point selection — superseded by warp-71.
- Minimap pin debugging — dead end; upstream `road_pins` covers basics.
- Standalone CSV exporter design — superseded by upstream route export.

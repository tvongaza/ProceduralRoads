# Plan: shorten in-game validation cycles

Received 8 Sep 2026 from another agent; a new thread of work, own PR if it
pans out. Kept verbatim below. Context from the 8 Sep session: the manual
version of this exists as the private station repo pc-cycle.sh / pc-shoot.sh and
the session's pc-probe.sh (ground height at an exact point via
cli_player_state heightAboveGround), pc-roadcheck.sh (ground vs stored
road height), pc-hookcheck.sh (lifecycle warning/write/skip counts) and
pc-relaunch.sh (reload without fixture restore). Generic pieces belong in
valheimCLI (the author's tool), road-specific ones in the mod. Item 3's
precondition (duplicate compiler, version hash) was resolved in #19 e8acaf0.

---

Objective: make a repeatable screenshot run automatic, observable, and fast. Measure from the agent starting the validation run to the screenshot being available for inspection.

Implement in the following order, using separate commits.

**1. Instrument the complete workflow first**

Reuse existing validation scripts and commands. Give every run a unique ID and record the build commit, loaded DLL hash, fixture, configuration, site, and camera preset.

Use monotonic Stopwatch timings for:

| Stage | Measure |
|---|---|
| Build and deployment | Build, file transfer, restart, confirmation that the intended DLL loaded |
| World loading | Load requested, locations ready, player ready |
| Generation | Island detection, location selection, each pathfinding attempt, smoothing, network finalization |
| Terrain | Waiting for zones/compilers, height calculation, paint, save, terrain rebuild |
| Capture | Teleport, camera positioning, scene readiness, capture, image writing |
| Delivery | Transfer, preview generation, image available to the agent |

Record elapsed time, active processing time where measurable, work counts, and the reason for each wait. Track the longest frame during generation/application.

Write a compact timing summary and machine-readable results. Keep detailed per-point logging disabled by default; aggregate counters instead.

Run one representative cold start and a few warm captures, including a normally slow case. Identify the largest delays before optimizing.

**2. Replace manual screenshot sequences with one validation command**

Add a debug-only command such as:

`road_validate <preset>`

Each preset specifies the fixture requirements, island/site, player streaming position, camera position/rotation/FOV, resolution, time/weather, road settings, and expected terrain checks.

The runner should:

1. Verify the loaded build and fixture.
2. Generate or load the required road data.
3. Move the player to load the target area.
4. Wait for the required zones, terrain compilers, road application, and terrain rebuilds.
5. Apply the camera/environment preset and hide overlays.
6. Capture after a completed render.
7. Write the screenshot, measurements, and completion manifest.

Use the player's position to establish streaming readiness; moving a free camera alone is insufficient.

Provide `status` and `cancel` commands. Report explicit states such as "waiting for zone X," "applying terrain," and "writing screenshot."

The external agent should submit one request and watch a status/result file or log event. Avoid repeated console typing, UI screenshots to check progress, and arbitrary 30-60 second sleeps.

**3. Make readiness specific and waits bounded**

"Roads available" or a matching compiler stamp does not prove the scene has finished rendering the updated terrain.

Define completion conditions for the target area:

- Correct world, build, configuration, and network are active.
- Required zones and their saved terrain compilers are alive.
- Road work for those zones has completed.
- Relevant heightmap rebuilds are complete.
- Camera and environment settings are applied.
- The capture file is fully written and readable.

Use available completion hooks; otherwise use bounded checks of relevant state. A brief stability check can supplement these conditions.

Give every stage a timeout. On timeout, save the current state, missing prerequisites, recent warnings, and an optional diagnostic screenshot. Stop with a useful failure rather than silently waiting for ten minutes.

Resolve the outstanding duplicate-compiler and version-hash findings before relying on these signals.

**4. Reduce work per validation cycle**

Use three distinct validation modes:

- **Geometry:** Existing headless tests and synthetic renders for rapid iteration.
- **Visual:** One island and a small set of fixed viewpoints in-game.
- **Lifecycle:** Fresh load, save/reload, compiler ownership, and persistence checks.

Run lifecycle checks when lifecycle behavior changes and before final approval; avoid repeating a full restart for every camera adjustment.

Keep the game open when the loaded code has not changed. Batch nearby viewpoints so their zones remain loaded. Restart when needed to load a new DLL; do not assume copying it updates the running process.

For cross-section changes, reuse unchanged route data. For smoothing changes, reuse source paths but recompute heights. Regenerate routes when pathfinding changes. Record what was reused so a cached result cannot validate the wrong stage.

Restore clean fixture terrain between comparative applications: forced painting currently accumulates, so repeatedly applying variants to the same terrain is not a valid baseline.

**5. Make long operations cooperative, then consider background work**

First split long operations into resumable batches:

- Island scan rows.
- A* node expansions.
- Road smoothing batches.
- Terrain vertices and paint batches.

Start with a configurable main-thread work budget around 2-5 ms per frame, then tune from measurements. Yield inside expensive operations; yielding only between roads will not help if one road takes seconds.

Coroutines improve responsiveness but do not move CPU work off the main thread. Measure total completion time as well as frame stalls.

Only move demonstrably thread-safe, pure calculations to workers. Snapshot their inputs first. Audit WorldGenerator calls and mutable caches before treating them as thread-safe; keep Unity objects, ZDO access, terrain commits, and screenshot APIs on their required thread.

Preserve deterministic road ordering, including overlap blending. Cancel jobs on world unload, reject stale results, and publish completed network data atomically. Queue affected terrain work until the network is ready.

Avoid introducing a large parallel-generation rewrite unless timings justify it.

**6. Optimize only the measured hotspots**

Likely candidates to investigate:

- Cache island detection within the same world and generation settings.
- Cache repeated terrain samples and zone-corner biome queries.
- Spatially filter nearby road points before each vertex calculation.
- Batch terrain saves and rebuilds after a zone's completed changes.
- Measure the per-road `Canvas.ForceUpdateCanvases()` call and remove or reduce it if unnecessary.
- Separate image transfer, contact-sheet generation, and report writing from the game's capture path.

Invalidate caches whenever their actual inputs change.

**Acceptance criteria**

- One command produces a repeatable screenshot and its supporting measurements.
- Every long wait has an identifiable cause and timeout.
- Repeated runs cannot silently use stale builds, terrain, or images.
- Before/after timing reports show both total cycle time and responsiveness.
- Initial target: halve the measured representative cycle time. Set separate warm-capture and cold-start budgets after collecting the baseline.
- Deliver the runner, presets, timing output, and a short runbook explaining how the agent submits a run and retrieves its results.

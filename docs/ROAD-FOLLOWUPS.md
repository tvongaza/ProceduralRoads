# Road follow-ups

## Current PR: switchback landings and natural mountain rocks

Requested during the Hildir cave inspection on 18 September 2026. Candidate code is implemented locally; the running game still has the previously accepted cave build.

- Climbing or descending switchbacks get a turning radius of road width plus terrain blend margin (6 m at the default width), and a 10% grade ceiling through the turn. A turn that cannot fit is refused rather than squeezed into a V.
- Non-neighbouring legs of the same reshaped road cannot overlap terrain blend footprints at different elevations. This does not yet check conflicts between separately generated roads.
- Tiny endpoint overshoots left by snapping a grid route to an exact bank or POI are ignored when identifying switchbacks; ordinary bank smoothing and road-sharing geometry stay unchanged. Rounded turns must still pass water and protected-site checks. Flat turns and normal bends retain their existing smoothing, and paint-only waded fords retain their existing path.
- A delayed loaded-zone pass checks actual colliders of four stock natural mountain boulder prefabs against the travelled corridor. It preserves ores, player pieces, protected location footprints and remotely owned objects. It destroys whole rocks without damage/drop events.
- Tests cover turn geometry, turn grades, a sample of the real terrain-height fit, refusal of pinched turns, protected sites and the rock eligibility/ownership policy. Collider intersection and rock persistence still require a game check; pure tests do not establish those.

Before calling this visually accepted, rebuild the disposable island with the candidate, inspect the switchbacks and a rock obstruction, then reload to check clearing persists or is repeated correctly for local scenery. Preserve the currently accepted world first. This check has not happened yet.

## Follow-up: approach Hildir's cave through the col

The user observed a possible route through the saddle toward the front entrance. Do not infer entrance direction from the location centre alone. Review this after the switchbacks; protecting authored terrain and maintaining a usable grade still take precedence over reaching every site.

## Accepted cave approach: preserve the small natural bump

The user accepted the new near-level approach on 18 September. The remaining bump is inside the protected site. Do not flatten the cave to chase it. A future improvement could compare the short natural connection from arrival to the entrance, but only with reliable entrance/collision data; root position alone does not establish a walkable doorway.

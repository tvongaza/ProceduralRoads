# Road follow-ups

## Future: clear large natural mountain rocks from road corridors

Requested after accepting the frost-cave approach on 18 September 2026.

- Identify the stock mountain-rock prefabs and how each is spawned/persisted before adding them to road clutter clearing.
- Remove natural rocks intersecting the travelled road corridor, including their collision, rather than clearing everything in a broad radius.
- Preserve POI-authored rocks and protected location footprints, and player structures. Mountain cave scenery must remain intact.
- Use the existing clutter-clearing ownership/persistence path where suitable; verify reload does not resurrect cleared rocks or duplicate drops.
- Add tests for an obstructing natural rock, one beside the road, and an identical prefab belonging to a protected POI. One bounded in-game collision check should finish validation.

This is a future task, not implemented or included in the accepted cave build.

## Accepted cave approach: preserve the small natural bump

The user accepted the new near-level approach on 18 September. The remaining bump is inside the protected site. Do not flatten the cave to chase it. A future improvement could compare the short natural connection from arrival to the entrance, but only with reliable entrance/collision data; root position alone does not establish a walkable doorway.

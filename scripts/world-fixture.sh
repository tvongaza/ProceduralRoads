#!/bin/sh
# Save/restore pristine world fixtures for repeatable road-generation tests.
#
#   ./scripts/world-fixture.sh save <WorldName>     # snapshot current world
#   ./scripts/world-fixture.sh restore <WorldName>  # overwrite from snapshot
#   ./scripts/world-fixture.sh list
#
# Flow: create a world once with road generation effectively off
# (IslandRoadPercentage = 0), quit so it saves — locations are placed but no
# roads or road terrain exist. `save` it. Before each test run, `restore` it,
# set IslandRoadPercentage back, and load the world: generation runs fresh on
# byte-identical terrain, so selftest pointsHash values are comparable across
# code versions. Restoring also discards road terrain baked into visited
# zones' TerrainComp ZDOs, which a force-regen alone cannot undo.
set -e

WORLDS="$HOME/Library/Application Support/Valheim/worlds_local"
FIXTURES="$(cd "$(dirname "$0")/.." && pwd)/validation-fixtures"
CMD="$1"
WORLD="$2"

case "$CMD" in
save)
    [ -n "$WORLD" ] || { echo "usage: world-fixture.sh save <WorldName>"; exit 1; }
    [ -f "$WORLDS/$WORLD.fwl" ] || { echo "world not found: $WORLDS/$WORLD.fwl"; exit 1; }
    mkdir -p "$FIXTURES"
    cp "$WORLDS/$WORLD.fwl" "$FIXTURES/"
    [ -f "$WORLDS/$WORLD.db" ] && cp "$WORLDS/$WORLD.db" "$FIXTURES/" \
        || echo "note: no .db yet (world never saved in-game?); snapshot is .fwl only"
    echo "saved fixture: $FIXTURES/$WORLD.{fwl,db}"
    ;;
restore)
    [ -n "$WORLD" ] || { echo "usage: world-fixture.sh restore <WorldName>"; exit 1; }
    [ -f "$FIXTURES/$WORLD.fwl" ] || { echo "no fixture: $FIXTURES/$WORLD.fwl"; exit 1; }
    if pgrep -f "MacOS/Valheim" >/dev/null; then
        echo "refusing to restore while Valheim is running (it would overwrite on save)"; exit 1
    fi
    mkdir -p "$WORLDS"
    cp "$FIXTURES/$WORLD.fwl" "$WORLDS/"
    rm -f "$WORLDS/$WORLD.db"
    [ -f "$FIXTURES/$WORLD.db" ] && cp "$FIXTURES/$WORLD.db" "$WORLDS/"
    rm -f "$WORLDS/$WORLD.fwl.old" "$WORLDS/$WORLD.db.old"
    echo "restored: $WORLD"
    ;;
list)
    ls -la "$FIXTURES" 2>/dev/null || echo "no fixtures yet"
    ;;
*)
    echo "usage: world-fixture.sh save|restore|list [WorldName]"
    exit 1
    ;;
esac

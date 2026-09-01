#!/bin/sh
# Automated in-game validation of the road network — no manual inspection.
#
#   ./scripts/ingame-validate.sh [WorldName]
#
# Drives the real Valheim client via jneb802's valheimCLI (TCP console
# bridge): launches the game, creates/loads a world, waits for world gen
# (our DebugValidation config auto-runs the self-test during generation),
# runs road_selftest explicitly, and copies the JSON report + routes CSV
# into validation-results/. Exits 0 iff the self-test passed.
#
# Prereqs: Steam running; valheimCLI mod + our mod deployed (./deploy.sh);
# DebugValidation=true in BepInEx/config/warpalicious.ProceduralRoads.cfg.
set -e

export DOTNET_ROOT="${DOTNET_ROOT:-/opt/homebrew/opt/dotnet/libexec}"

REPO_DIR="$(cd "$(dirname "$0")/.." && pwd)"
CLI="${VALHEIM_CLI:-$REPO_DIR/../valheimCLI/CLI/bin/Debug/net10.0/valheim-cli}"
VALHEIM="$HOME/Library/Application Support/Steam/steamapps/common/Valheim"
CONFIG_DIR="$VALHEIM/BepInEx/config"
WORLD="${1:-RoadTestAuto}"
CHAR="${2:-RoadTester}"
OUT_DIR="$REPO_DIR/validation-results"

echo "== 1/6 game status =="
if ! "$CLI" --status | grep -q "process=true"; then
    echo "Launching Valheim..."
    "$CLI" --launch --timeout 240s
fi

echo "== 2/6 waiting for CLI terminal bridge =="
"$CLI" wait --for terminal --timeout 240s

echo "== 3/6 selecting character '$CHAR' =="
"$CLI" cli_select_character "$CHAR" 2>/dev/null | grep -qi "selected" \
    || "$CLI" cli_create_character "$CHAR"

echo "== 4/6 starting local world '$WORLD' (generation runs self-test) =="
"$CLI" cli_start_local_world "$WORLD"
"$CLI" wait --for inworld --timeout 600s

echo "== 5/6 running road_selftest =="
"$CLI" road_selftest

echo "== 6/6 collecting reports =="
mkdir -p "$OUT_DIR"
cp "$CONFIG_DIR/ProceduralRoads.selftest.json" "$OUT_DIR/$WORLD.selftest.json"
cp "$CONFIG_DIR/ProceduralRoads.routes.csv" "$OUT_DIR/$WORLD.routes.csv"

echo "Report: $OUT_DIR/$WORLD.selftest.json"
grep -E '"passed"|"routeCount"|"networkComponents"|"fordCount"|"violations"' \
    "$OUT_DIR/$WORLD.selftest.json" | head -6

grep -q '"passed": true' "$OUT_DIR/$WORLD.selftest.json"

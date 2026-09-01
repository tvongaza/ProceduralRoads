#!/bin/sh
# Headless road-network validation via a dockerized Valheim DEDICATED SERVER.
# No 3D client, no Steam login (server downloads via anonymous steamcmd).
#
#   ./scripts/server-validate.sh [WorldName]
#
# Road generation triggers from ZoneSystem.Start, which runs server-side;
# with DebugValidation=true the self-test runs during world creation and
# writes its JSON report into the mounted config volume. On Apple Silicon
# the linux/amd64 server runs emulated — slower, but fully automated.
set -e

REPO_DIR="$(cd "$(dirname "$0")/.." && pwd)"
WORLD="${1:-RoadHeadless1}"
SRV="$REPO_DIR/server-test"
IMAGE="ghcr.io/lloesche/valheim-server"
NAME="proceduralroads-test-server"
VALHEIM_LOCAL="$HOME/Library/Application Support/Steam/steamapps/common/Valheim"

mkdir -p "$SRV/config/bepinex/plugins" "$SRV/data"

echo "== staging mod + dependencies into server volume =="
cp "$REPO_DIR/ProceduralRoads/bin/Release/ProceduralRoads.dll" "$SRV/config/bepinex/plugins/"
cp "$VALHEIM_LOCAL/BepInEx/core/Jotunn.dll" "$SRV/config/bepinex/plugins/" 2>/dev/null \
    || echo "warning: Jotunn.dll not found locally; download it into $SRV/config/bepinex/plugins"

mkdir -p "$SRV/config/bepinex/config"
cat > "$SRV/config/bepinex/config/warpalicious.ProceduralRoads.cfg" <<CFG
[Roads]
IslandRoadPercentage = 100

[Debug]
DebugValidation = true
CFG

echo "== starting dedicated server (world: $WORLD) =="
docker rm -f "$NAME" >/dev/null 2>&1 || true
docker run -d --name "$NAME" --platform linux/amd64 \
    -v "$SRV/config:/config" -v "$SRV/data:/opt/valheim" \
    -e SERVER_NAME="ProceduralRoads Test" \
    -e WORLD_NAME="$WORLD" \
    -e SERVER_PASS="roadtest123" \
    -e SERVER_PUBLIC=false \
    -e BEPINEX=true \
    -e STATUS_HTTP=false \
    "$IMAGE" >/dev/null

echo "== waiting for [SELFTEST] in server output (first run downloads ~2GB) =="
i=0
until docker logs "$NAME" 2>&1 | grep -q "\[SELFTEST\]"; do
    i=$((i+1))
    if [ $i -gt 720 ]; then echo "TIMEOUT after 60m"; docker logs --tail 30 "$NAME"; exit 1; fi
    if ! docker ps -q -f name="$NAME" | grep -q .; then
        echo "server container exited:"; docker logs --tail 40 "$NAME"; exit 1
    fi
    sleep 5
done

docker logs "$NAME" 2>&1 | grep "\[SELFTEST\]" | head -5

echo "== collecting report =="
mkdir -p "$REPO_DIR/validation-results"
REPORT=$(find "$SRV/config" -name "ProceduralRoads.selftest.json" | head -1)
CSV=$(find "$SRV/config" -name "ProceduralRoads.routes.csv" | head -1)
[ -n "$REPORT" ] && cp "$REPORT" "$REPO_DIR/validation-results/$WORLD.server.selftest.json" \
    && echo "Report: validation-results/$WORLD.server.selftest.json"
[ -n "$CSV" ] && cp "$CSV" "$REPO_DIR/validation-results/$WORLD.server.routes.csv"

echo "== stopping server =="
docker stop "$NAME" >/dev/null

[ -n "$REPORT" ] && grep -q '"passed": true' "$REPORT"

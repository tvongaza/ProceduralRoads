#!/bin/sh
# Headless road-network validation via a Linux dedicated server in Docker.
# No 3D client, no Steam login, no in-container steamcmd (whose 32-bit
# bootstrapper cannot run under Rosetta-for-Linux on Apple Silicon).
#
#   ./scripts/server-validate.sh [WorldName]
#
# Layout (created by this script + the one-time host download):
#   server-test/steamcmd/  host steamcmd (x86_64 macOS, runs under Rosetta)
#   server-test/server/    Linux dedicated server depot (downloaded on host
#                          with +@sSteamCmdForcePlatformType linux)
# BepInEx is staged from the local Valheim install, which ships the LINUX
# doorstop (libdoorstop_x64.so) and start_server_bepinex.sh alongside the
# macOS bits. Road generation triggers from ZoneSystem.Start server-side;
# DebugValidation=true self-tests during world creation.
set -e

REPO_DIR="$(cd "$(dirname "$0")/.." && pwd)"
WORLD="${1:-RoadHeadless1}"
SRV="$REPO_DIR/server-test/server"
NAME="proceduralroads-test-server"
VALHEIM_LOCAL="$HOME/Library/Application Support/Steam/steamapps/common/Valheim"

[ -f "$SRV/valheim_server.x86_64" ] || {
    echo "server depot missing — run the host steamcmd download first"; exit 1; }

echo "== staging BepInEx + mods into server depot =="
mkdir -p "$SRV/BepInEx/plugins" "$SRV/BepInEx/config" "$SRV/doorstop_libs"
cp -R "$VALHEIM_LOCAL/BepInEx/core" "$SRV/BepInEx/" 2>/dev/null || true
cp "$VALHEIM_LOCAL/doorstop_libs/libdoorstop_x64.so" "$SRV/doorstop_libs/"
cp "$VALHEIM_LOCAL/start_server_bepinex.sh" "$SRV/"
chmod +x "$SRV/start_server_bepinex.sh" "$SRV/valheim_server.x86_64"

cp "$REPO_DIR/ProceduralRoads/bin/Release/ProceduralRoads.dll" "$SRV/BepInEx/plugins/"
cp "$VALHEIM_LOCAL/BepInEx/core/Jotunn.dll" "$SRV/BepInEx/plugins/" 2>/dev/null || true

cat > "$SRV/BepInEx/config/warpalicious.ProceduralRoads.cfg" <<CFG
[Roads]
IslandRoadPercentage = 100

[Debug]
DebugValidation = true
CFG

echo "== starting dedicated server (world: $WORLD) =="
docker rm -f "$NAME" >/dev/null 2>&1 || true
docker run -d --name "$NAME" --platform linux/amd64 \
    -v "$SRV:/valheim" -w /valheim \
    -e HOME=/root -e TERM=xterm \
    ubuntu:22.04 \
    bash -c "apt-get update -qq >/dev/null && apt-get install -y -qq libatomic1 ca-certificates >/dev/null && \
        ./start_server_bepinex.sh ./valheim_server.x86_64 \
        -name 'ProceduralRoads Test' -port 2456 -world '$WORLD' \
        -password roadtest123 -public 0 -batchmode -nographics" >/dev/null

echo "== waiting for [SELFTEST] in server output =="
i=0
until docker logs "$NAME" 2>&1 | grep -q "\[SELFTEST\]"; do
    i=$((i+1))
    if [ $i -gt 720 ]; then echo "TIMEOUT after 60m"; docker logs --tail 30 "$NAME"; exit 1; fi
    if ! docker ps -q -f name="$NAME" | grep -q .; then
        echo "server container exited:"; docker logs --tail 40 "$NAME"; exit 1
    fi
    sleep 5
done

docker logs "$NAME" 2>&1 | grep "\[SELFTEST\]" | head -6

echo "== collecting report =="
mkdir -p "$REPO_DIR/validation-results"
REPORT=$(find "$SRV/BepInEx/config" -name "ProceduralRoads.selftest.json" | head -1)
CSV=$(find "$SRV/BepInEx/config" -name "ProceduralRoads.routes.csv" | head -1)
[ -n "$REPORT" ] && cp "$REPORT" "$REPO_DIR/validation-results/$WORLD.server.selftest.json" \
    && echo "Report: validation-results/$WORLD.server.selftest.json"
[ -n "$CSV" ] && cp "$CSV" "$REPO_DIR/validation-results/$WORLD.server.routes.csv"

echo "== stopping server =="
docker stop -t 20 "$NAME" >/dev/null

[ -n "$REPORT" ] && grep -q '"passed": true' "$REPORT"

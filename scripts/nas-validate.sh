#!/bin/sh
# Headless road-network validation on a remote x86_64 Linux box (e.g. a
# TrueNAS SCALE machine) driven over SSH — the private alternative to CI.
# The Valheim dedicated server runs NATIVELY there (it cannot run under
# emulation on Apple Silicon), worlds persist on the box for exact-hash
# regression, and the depot downloads once.
#
#   NAS_HOST=nas ./scripts/nas-validate.sh [WorldName]
#
# Requirements on the box: SSH access, x86_64 Linux (TrueNAS SCALE is
# fine), ~3GB free in $NAS_DIR, outbound https for the one-time depot
# download. Everything is confined to $NAS_DIR.
set -e

REPO_DIR="$(cd "$(dirname "$0")/.." && pwd)"
WORLD="${1:-RoadNas1}"
NAS_HOST="${NAS_HOST:?set NAS_HOST to the ssh host/alias of the validation box}"
NAS_DIR="${NAS_DIR:-proceduralroads-validate}"

echo "== 1/5 building mod =="
dotnet build "$REPO_DIR/ProceduralRoads/ProceduralRoads.csproj" -c Release \
    -p:CopyOutputDLLPath=/nonexistent-skip-copy >/dev/null
echo "built $(ls -la "$REPO_DIR/ProceduralRoads/bin/Release/ProceduralRoads.dll" | awk '{print $5}') bytes"

echo "== 2/5 one-time server setup on $NAS_HOST (skipped if present) =="
ssh "$NAS_HOST" "sh -s" <<'REMOTE'
set -e
cd "$HOME" && mkdir -p proceduralroads-validate && cd proceduralroads-validate
if [ ! -f server/valheim_server.x86_64 ]; then
    echo "downloading DepotDownloader + Valheim server depot (one-time)..."
    curl -sL "$(curl -s https://api.github.com/repos/SteamRE/DepotDownloader/releases/latest \
        | grep browser_download_url | grep linux-x64 | cut -d '"' -f4)" -o dd.zip
    unzip -oq dd.zip -d dd && chmod +x dd/DepotDownloader
    ./dd/DepotDownloader -app 896660 -os linux -dir server
fi
if [ ! -f server/BepInEx/core/BepInEx.dll ]; then
    echo "installing BepInEx pack + Jotunn (one-time)..."
    curl -sL "https://thunderstore.io/package/download/denikson/BepInExPack_Valheim/5.4.2202/" -o bepinex.zip
    unzip -oq bepinex.zip -d bepinex-pack
    cp -R bepinex-pack/BepInExPack_Valheim/* server/
    curl -sL "https://thunderstore.io/package/download/ValheimModding/Jotunn/2.29.1/" -o jotunn.zip
    unzip -oq jotunn.zip -d jotunn-pack
    find jotunn-pack -name "Jotunn.dll" -exec cp {} server/BepInEx/plugins/ \;
    chmod +x server/start_server_bepinex.sh server/valheim_server.x86_64
fi
mkdir -p server/BepInEx/plugins server/BepInEx/config
REMOTE

echo "== 3/5 deploying mod + config =="
scp -q "$REPO_DIR/ProceduralRoads/bin/Release/ProceduralRoads.dll" \
    "$NAS_HOST:$NAS_DIR/server/BepInEx/plugins/"
ssh "$NAS_HOST" "printf '[Roads]\nIslandRoadPercentage = 100\n\n[Debug]\nDebugValidation = true\n' \
    > $NAS_DIR/server/BepInEx/config/warpalicious.ProceduralRoads.cfg"

echo "== 4/5 running server until self-test (world: $WORLD) =="
ssh "$NAS_HOST" "WORLD='$WORLD' NAS_DIR='$NAS_DIR' sh -s" <<'REMOTE'
set -e
cd "$HOME/$NAS_DIR/server"
rm -f selftest-run.log
./start_server_bepinex.sh ./valheim_server.x86_64 \
    -name RoadValidate -port 24560 -world "$WORLD" -password roadtest123 \
    -public 0 -batchmode -nographics \
    -savedir "$HOME/$NAS_DIR/saves" > selftest-run.log 2>&1 &
SERVER_PID=$!
trap 'kill $SERVER_PID 2>/dev/null || true' EXIT
for i in $(seq 1 360); do
    grep -q "\[SELFTEST\]" selftest-run.log && break
    kill -0 $SERVER_PID 2>/dev/null || { echo "SERVER DIED:"; tail -30 selftest-run.log; exit 1; }
    sleep 5
done
grep "\[SELFTEST\]" selftest-run.log | head -4 || { echo "TIMEOUT (30m):"; tail -30 selftest-run.log; exit 1; }
kill $SERVER_PID 2>/dev/null || true
sleep 3
REMOTE

echo "== 5/5 collecting report =="
mkdir -p "$REPO_DIR/validation-results"
scp -q "$NAS_HOST:$NAS_DIR/server/BepInEx/config/ProceduralRoads.selftest.json" \
    "$REPO_DIR/validation-results/$WORLD.nas.selftest.json"
scp -q "$NAS_HOST:$NAS_DIR/server/BepInEx/config/ProceduralRoads.routes.csv" \
    "$REPO_DIR/validation-results/$WORLD.nas.routes.csv" 2>/dev/null || true

echo "Report: validation-results/$WORLD.nas.selftest.json"
grep -E '"passed"|"routeCount"|"networkComponents"|"fordCount"|"pointsHash"' \
    "$REPO_DIR/validation-results/$WORLD.nas.selftest.json"
grep -q '"passed": true' "$REPO_DIR/validation-results/$WORLD.nas.selftest.json"

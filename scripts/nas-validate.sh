#!/bin/sh
# Headless road-network validation on a remote x86_64 Linux box over SSH.
# The Valheim dedicated server cannot run under emulation on Apple Silicon,
# so it runs natively on the remote box; worlds persist there for
# exact-hash regression, and only a DLL + cfg travel per run.
#
#   ./scripts/nas-validate.sh [WorldName]
#
# Connection and layout come from environment variables, optionally loaded
# from scripts/.nas.env (gitignored — keep host names, users, and paths of
# private infrastructure OUT of this repo):
#   NAS_SSH        ssh destination (user@host)
#   NAS_SERVER_DIR remote dir holding the dedicated server + BepInEx
#   NAS_SAVES_DIR  remote dir for persistent worlds
# The box must already hold the server depot + BepInEx + Jotunn.
set -e

REPO_DIR="$(cd "$(dirname "$0")/.." && pwd)"
[ -f "$REPO_DIR/scripts/.nas.env" ] && . "$REPO_DIR/scripts/.nas.env"

WORLD="${1:-RoadNas1}"
NAS="${NAS_SSH:?set NAS_SSH (user@host), e.g. in scripts/.nas.env}"
SRV="${NAS_SERVER_DIR:?set NAS_SERVER_DIR, e.g. in scripts/.nas.env}"
SAVES="${NAS_SAVES_DIR:?set NAS_SAVES_DIR, e.g. in scripts/.nas.env}"

echo "== 1/4 building mod =="
dotnet build "$REPO_DIR/ProceduralRoads/ProceduralRoads.csproj" -c Release \
    -p:CopyOutputDLLPath=/nonexistent-skip-copy >/dev/null

echo "== 2/4 deploying mod + config =="
scp -q "$REPO_DIR/ProceduralRoads/bin/Release/ProceduralRoads.dll" "$NAS:$SRV/BepInEx/plugins/"
ssh "$NAS" "mkdir -p '$SRV/BepInEx/config' && printf '[Roads]\nIslandRoadPercentage = 100\n\n[Debug]\nDebugValidation = true\n' > '$SRV/BepInEx/config/warpalicious.ProceduralRoads.cfg'"

echo "== 3/4 running server until self-test (world: $WORLD) =="
ssh "$NAS" "WORLD='$WORLD' SRV='$SRV' SAVES='$SAVES' sh -s" <<'REMOTE'
set -e
cd "$SRV"
rm -f selftest-run.log
./start_server_bepinex.sh ./valheim_server.x86_64 \
    -name RoadValidate -port 24560 -world "$WORLD" -password roadtest123 \
    -public 0 -batchmode -nographics -savedir "$SAVES" > selftest-run.log 2>&1 &
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
echo "MEMORY_PEAK_BYTES=$(cat /sys/fs/cgroup/memory.peak 2>/dev/null || echo unavailable)"
REMOTE

echo "== 4/4 collecting report =="
mkdir -p "$REPO_DIR/validation-results"
scp -q "$NAS:$SRV/BepInEx/config/ProceduralRoads.selftest.json" \
    "$REPO_DIR/validation-results/$WORLD.nas.selftest.json"
scp -q "$NAS:$SRV/BepInEx/config/ProceduralRoads.routes.csv" \
    "$REPO_DIR/validation-results/$WORLD.nas.routes.csv" 2>/dev/null || true

echo "Report: validation-results/$WORLD.nas.selftest.json"
grep -E '"passed"|"routeCount"|"networkComponents"|"fordCount"|"pointsHash"' \
    "$REPO_DIR/validation-results/$WORLD.nas.selftest.json"
grep -q '"passed": true' "$REPO_DIR/validation-results/$WORLD.nas.selftest.json"

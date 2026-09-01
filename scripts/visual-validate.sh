#!/bin/sh
# In-game VISUAL validation: launch the client, load the fixture world with
# ForceRegenerate (so crossings/stairs/ruin plans exist), teleport to each
# spot of note, let zones spawn (ruins instantiate), aim at the spot, and
# capture a screenshot per spot into validation-results/screenshots/.
#
#   ./scripts/visual-validate.sh [WorldName]
#
# Prereqs: Steam running, valheimCLI installed, ./deploy.sh done.
set -e

export DOTNET_ROOT="${DOTNET_ROOT:-/opt/homebrew/opt/dotnet/libexec}"
REPO_DIR="$(cd "$(dirname "$0")/.." && pwd)"
CLI="${VALHEIM_CLI:-$REPO_DIR/../valheimCLI/CLI/bin/Debug/net10.0/valheim-cli}"
VALHEIM="$HOME/Library/Application Support/Steam/steamapps/common/Valheim"
WORLD="${1:-RoadTestAuto1}"
SHOTS="$REPO_DIR/validation-results/screenshots"
mkdir -p "$SHOTS"

echo "== config: DebugValidation + ForceRegenerate =="
CFG="$VALHEIM/BepInEx/config/warpalicious.ProceduralRoads.cfg"
python3 - "$CFG" <<'EOF'
import sys, re
p = sys.argv[1]
s = open(p).read()
for key in ("DebugValidation", "ForceRegenerate"):
    if re.search(rf'^{key} = ', s, re.M):
        s = re.sub(rf'^{key} = .*$', f'{key} = true', s, flags=re.M)
    else:
        s = s.replace('[Debug]', f'[Debug]\n\n{key} = true', 1)
open(p, 'w').write(s)
EOF

echo "== launching game =="
if ! "$CLI" --status 2>/dev/null | grep -q "process=true"; then
    nohup "$VALHEIM/run_bepinex.sh" > /dev/null 2>&1 &
fi
"$CLI" wait --for terminal --timeout 240s | tail -1
"$CLI" wait --for mainmenu --timeout 240s | tail -1

echo "== loading world (regeneration runs during load) =="
"$CLI" cli_select_character RoadTester | tail -1
"$CLI" cli_start_local_world "$WORLD" | tail -1
"$CLI" wait --for localplayer --timeout 900s | tail -1

echo "== collecting spots =="
"$CLI" cli_set_player_safety true | tail -1
SPOTS_RAW=$("$CLI" road_spots 2>&1)
echo "$SPOTS_RAW" | grep -E "CROSSING|STAIRS|total"

echo "$SPOTS_RAW" | grep -E "^(CROSSING|STAIRS)" | head -6 | while read -r KIND IDX REST; do
    X=$(echo "$REST" | grep -oE 'x=-?[0-9]+' | cut -d= -f2)
    Z=$(echo "$REST" | grep -oE 'z=-?[0-9]+' | cut -d= -f2)
    [ -n "$X" ] && [ -n "$Z" ] || continue
    NAME="$(echo "$KIND" | tr '[:upper:]' '[:lower:]')-$IDX"

    echo "== spot $NAME at ($X, $Z) =="
    # Stand back ~25m so the ruin is in frame, high enough to survive terrain.
    "$CLI" cli_teleport "$((X - 25))" 60 "$((Z - 25))" | tail -1
    sleep 15   # zones spawn -> ruins instantiate; terrain settles; we land
    "$CLI" cli_aim_at "$X" 31 "$Z" | tail -1
    sleep 2
    screencapture -x "$SHOTS/$WORLD-$NAME.png"
    echo "captured $SHOTS/$WORLD-$NAME.png"
done

echo "== saving world =="
"$CLI" save | tail -1
ls -la "$SHOTS" | tail -8

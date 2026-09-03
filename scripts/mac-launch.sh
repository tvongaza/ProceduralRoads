#!/bin/sh
# Relaunch the Mac client on the deployed build and load a world without
# regenerating it (persisted roads):  ./scripts/mac-launch.sh <World>
# Leaves the player in-world with safety on, ready for console commands.
set -e
export DOTNET_ROOT="${DOTNET_ROOT:-/opt/homebrew/opt/dotnet/libexec}"
REPO_DIR="$(cd "$(dirname "$0")/.." && pwd)"
CLI="${VALHEIM_CLI:-$REPO_DIR/../valheimCLI/CLI/bin/Debug/net10.0/valheim-cli}"
VALHEIM="$HOME/Library/Application Support/Steam/steamapps/common/Valheim"
WORLD="${1:?world name}"; CHAR="${CHAR:-RoadTester}"
if "$CLI" --status 2>/dev/null | grep -q "process=true"; then
    "$CLI" cli_logout_save 2>/dev/null | tail -1 || true
    sleep 8
    pkill -x Valheim 2>/dev/null || true
    for _ in 1 2 3 4 5 6 7 8 9 10; do pgrep -x Valheim >/dev/null || break; sleep 2; done
fi
nohup "$VALHEIM/run_bepinex.sh" > /dev/null 2>&1 &
"$CLI" wait --for terminal --timeout 240s | tail -1
"$CLI" wait --for mainmenu --timeout 240s | tail -1
"$CLI" cli_select_character "$CHAR" | tail -1
"$CLI" cli_start_local_world "$WORLD" | tail -1
"$CLI" wait --for localplayer --timeout 900s | tail -1
"$CLI" cli_set_player_safety true | tail -1
"$CLI" devcommands | tail -1
"$CLI" setkey PassiveMobs | tail -1
echo "in-world: $WORLD"

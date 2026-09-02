#!/bin/sh
# Redo visual captures: jump to the game's Space, clear weather, midday
# light, then teleport-aim-capture each spot with the game frontmost.
set -e
export DOTNET_ROOT="${DOTNET_ROOT:-/opt/homebrew/opt/dotnet/libexec}"
REPO_DIR="$(cd "$(dirname "$0")/.." && pwd)"
CLI="$REPO_DIR/../valheimCLI/CLI/bin/Debug/net10.0/valheim-cli"
SHOTS="$REPO_DIR/validation-results/screenshots"
mkdir -p "$SHOTS"

osascript -e 'tell application "System Events" to set frontmost of first process whose name is "Valheim" to true'
sleep 3

"$CLI" env Clear | tail -1
"$CLI" tod 0.5 | tail -1
sleep 2

capture() {  # capture NAME X Z
    NAME=$1; X=$2; Z=$3
    "$CLI" cli_teleport "$((X - 22))" 45 "$((Z - 22))" | tail -1
    sleep 12
    "$CLI" cli_aim_at "$X" 31 "$Z" | tail -1
    osascript -e 'tell application "System Events" to set frontmost of first process whose name is "Valheim" to true'
    sleep 2
    screencapture -x "$SHOTS/redo-$NAME.png"
    echo "captured redo-$NAME"
}

capture crossing-0 200 3200
capture crossing-1 -680 -7208
capture stairs-0 -742 -7333
capture stairs-1 -5544 -6289
capture stairs-2 -7280 -3010
capture stairs-3 7678 2687
capture stairs-4 -6729 -4202

"$CLI" save | tail -1
echo DONE

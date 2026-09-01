#!/bin/sh
# Build the mod and deploy it into the local Valheim install (macOS).
# The csproj's CopyOutputDLL msbuild target does not fire on Mac, so this
# script is the deploy pipeline: dotnet build + cp, same as upstream's
# build.sh. Launch the game with Valheim's start_game_bepinex.sh.
set -e
cd "$(dirname "$0")/ProceduralRoads"

dotnet build -c Release -p:CopyOutputDLLPath=/nonexistent-skip-copy

VALHEIM="$HOME/Library/Application Support/Steam/steamapps/common/Valheim"
PLUGIN_DIR="$VALHEIM/BepInEx/plugins/ProceduralRoads"
mkdir -p "$PLUGIN_DIR"
cp bin/Release/ProceduralRoads.dll "$PLUGIN_DIR/"

echo "Deployed $(ls -la "$PLUGIN_DIR/ProceduralRoads.dll" | awk '{print $5, $6, $7, $8}')"
echo "Self-test report will appear at: $VALHEIM/BepInEx/config/ProceduralRoads.selftest.json"

#!/bin/sh
# Mac cockpit: relaunch the client on the deployed build, regenerate a
# screenshot world, and photograph the crossings that matter for the current
# round: the widest wood bridge plus one ford of each style, side A / side B /
# top per site, with a wood decay census (t0 / t8, player within 10 m).
#
#   ./scripts/mac-shoot.sh <WorldName> <tag> [maxIterations=200000]
#
# Output: validation-results/screenshots/<World>-<tag>/ (png + census + spots),
#         validation-results/<World>.selftest.json + .routes.csv
#
# Every in-game wait here was measured, not copied: zones need ~15 s after a
# teleport, weather ~6 s after `env Clear`, `cli_set_tod 0.25` is pre-dawn
# (0.45 is full day), and the CLI returns some outputs one call late (so
# road_spots is asked twice and the second answer is kept).
set -e

export DOTNET_ROOT="${DOTNET_ROOT:-/opt/homebrew/opt/dotnet/libexec}"
REPO_DIR="$(cd "$(dirname "$0")/.." && pwd)"
CLI="${VALHEIM_CLI:-$REPO_DIR/../valheimCLI/CLI/bin/Debug/net10.0/valheim-cli}"
VALHEIM="$HOME/Library/Application Support/Steam/steamapps/common/Valheim"
CFG="$VALHEIM/BepInEx/config/warpalicious.ProceduralRoads.cfg"
LOG="$VALHEIM/BepInEx/LogOutput.log"
WORLD="${1:?world name}"
TAG="${2:?tag (commit or round)}"
ITER="${3:-200000}"
CHAR="${CHAR:-RoadTester}"
DECAY_SOAK="${DECAY_SOAK:-480}"   # seconds the player dwells at the wood bridge for the t8 census
OUT="$REPO_DIR/validation-results/screenshots/$WORLD-$TAG"
mkdir -p "$OUT"

say() { printf '\n== %s ==\n' "$*"; }

# ---- 1. quit whatever is running (the deployed dll only loads at launch) ----
say "1/8 quitting any running client"
if "$CLI" --status 2>/dev/null | grep -q "process=true"; then
    "$CLI" cli_logout_save 2>/dev/null | tail -1 || true
    sleep 8
    pkill -x Valheim 2>/dev/null || true
    for _ in 1 2 3 4 5 6 7 8 9 10; do
        pgrep -x Valheim >/dev/null || break
        sleep 2
    done
fi

# ---- 2. config: real ceiling, regenerate, selftest on ----
say "2/8 config"
python3 - "$CFG" "$ITER" <<'EOF'
import sys, re
p, it = sys.argv[1], sys.argv[2]
s = open(p).read()
def setkey(s, section, key, value):
    if re.search(rf'^{key} = ', s, re.M):
        return re.sub(rf'^{key} = .*$', f'{key} = {value}', s, flags=re.M)
    return s.replace(f'[{section}]', f'[{section}]\n\n{key} = {value}', 1)
s = setkey(s, 'Debug', 'DebugValidation', 'true')
s = setkey(s, 'Debug', 'ForceRegenerate', 'true')
s = setkey(s, 'Roads', 'PathfindingMaxIterations', it)
# A value written by an earlier build stays in the file and beats the new
# default silently, so the lever defaults are pinned here explicitly
# (RoadConstants.BridgeCrossingPenalty / BridgeCostPerMeter, 40b7351).
s = setkey(s, 'Roads', 'BridgeCostFixed', '30000')
s = setkey(s, 'Roads', 'BridgeCostPerMeter', '300')
open(p, 'w').write(s)
EOF
grep -E '^(ForceRegenerate|DebugValidation|PathfindingMaxIterations|BridgeCost|WetTerminus|PierPersistence|WadeWeight|RaiseWeight|SpanWeight)' "$CFG" || true
# BepInEx truncates LogOutput.log at launch, so everything in it after the
# launch below is this run's.

# ---- 3. launch, load, safety ----
say "3/8 launching"
nohup "$VALHEIM/run_bepinex.sh" > /dev/null 2>&1 &
"$CLI" wait --for terminal --timeout 240s | tail -1
"$CLI" wait --for mainmenu --timeout 240s | tail -1
"$CLI" cli_select_character "$CHAR" | tail -1
T0=$(date +%s)
"$CLI" cli_start_local_world "$WORLD" | tail -1
"$CLI" wait --for localplayer --timeout 900s | tail -1
echo "world loaded in $(( $(date +%s) - T0 )) s"
"$CLI" cli_set_player_safety true | tail -1
"$CLI" devcommands | tail -1
"$CLI" setkey PassiveMobs | tail -1

# ---- 4. read back what the mod actually ran with ----
say "4/8 effective config + selftest"
grep -E '\[CONFIG\]' "$LOG" | tail -1 | tee "$OUT/config-line.txt"
"$CLI" road_ruins_reset | tail -1
sleep 2
"$CLI" road_ruins_reset | tail -1        # output arrives one call late; second call shows the first's result
"$CLI" road_selftest | tail -1
sleep 3
cp "$VALHEIM/BepInEx/config/ProceduralRoads.selftest.json" "$REPO_DIR/validation-results/$WORLD.selftest.json"
cp "$VALHEIM/BepInEx/config/ProceduralRoads.routes.csv" "$REPO_DIR/validation-results/$WORLD.routes.csv"
grep -E '"passed"|"routeCount"|"networkComponents"|"fordCount"|"pointsHash"' "$REPO_DIR/validation-results/$WORLD.selftest.json"

# ---- 5. pick the sites ----
say "5/8 sites"
"$CLI" road_spots > /dev/null 2>&1 || true
sleep 1
"$CLI" road_spots 2>&1 | tee "$OUT/spots.txt" | grep -E '^total'
# Widest wood bridge, and one ford per style (prefer the wood kit, then the widest).
SITES=$(python3 - "$OUT/spots.txt" <<'EOF'
import re, sys
rows = []
for line in open(sys.argv[1]):
    if not line.startswith("CROSSING"): continue
    kv = dict(re.findall(r'(\w+)=(\S+)', line))
    kv['idx'] = line.split()[1]
    rows.append(kv)
def width(r): return float(r['width'])
picks = []
bridges = [r for r in rows if r['kind'] == 'bridge' and r['kit'] == 'wood']
if bridges: picks.append(('bridge', max(bridges, key=width)))
for style in ('wade', 'raise', 'span'):
    fords = [r for r in rows if r['kind'] == 'ford-' + style]
    if fords:
        fords.sort(key=lambda r: (r['kit'] != 'wood', -width(r)))
        picks.append((style, fords[0]))
for label, r in picks:
    print(label, r['idx'], r['x'], r['z'], r['width'], r['kit'], r['biome'], r['dir'], r['fromY'], r['toY'], r['bed'])
EOF
)
echo "$SITES"
[ -n "$SITES" ] || { echo "no sites picked"; exit 1; }

# ---- 6. photograph ----
# Camera: free-fly at a standoff scaled by the crossing width, from each side
# (perpendicular to the crossing direction) at bank + 6 m looking at the
# waterline, then from above. The player is teleported to the near bank first
# so the zone (and its ruin pieces) is loaded.
# Poll the mod's readiness probe instead of sleeping: ready when every zone
# around the point is loaded, its planned ruins spawned and instantiated.
# Falls through after 25 s (a decayed site never reaches "ready").
wait_zone() {  # wait_zone <x> <z>
    out=""
    for i in $(seq 1 25); do
        out=$("$CLI" road_zone_ready "$1" "$2" 40 2>&1 | grep ZONE_READY | tail -1)
        case "$out" in *"ready=true"*) echo "zone ready after ${i} s: $out"; return 0;; esac
        sleep 1
    done
    echo "zone not ready after 25 s: $out"
}

shoot() {  # shoot <name> <x> <z> <width> <dirx> <dirz> <bankY>
    name=$1; x=$2; z=$3; w=$4; dx=$5; dz=$6; by=$7
    "$CLI" cli_teleport "$x" 45 "$z" | tail -1
    wait_zone "$x" "$z"
    "$CLI" road_clear_view "$x" "$z" 50 | tail -1
    "$CLI" cli_set_tod 0.45 | tail -1
    "$CLI" env Clear | tail -1
    sleep 6
    python3 - "$name" "$x" "$z" "$w" "$dx" "$dz" "$by" "$OUT" "$CLI" <<'EOF'
import sys, math, subprocess, time
name, x, z, w, dx, dz, by, out, cli = sys.argv[1:]
x, z, w, dx, dz, by = map(float, (x, z, w, dx, dz, by))
px, pz = -dz, dx                        # perpendicular to the crossing line
stand = max(28.0, w * 0.9)
poses = {
    'sideA': (x + px * stand, by + 6, z + pz * stand),
    'sideB': (x - px * stand, by + 6, z - pz * stand),
    'top':   (x + px * 4,     by + max(35.0, w * 0.6), z + pz * 4),
}
for pose, (cx, cy, cz) in poses.items():
    subprocess.run([cli, 'cli_freefly_pose', f'{cx:.1f}', f'{cy:.1f}', f'{cz:.1f}', f'{x:.1f}', '31', f'{z:.1f}'],
                   stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    time.sleep(2.5)
    subprocess.run(['screencapture', '-x', f'{out}/{name}-{pose}.png'])
    print(f'captured {name}-{pose}.png')
EOF
}

census() {  # census <file> <x> <z>
    "$CLI" cli_teleport "$2" 35 "$3" | tail -1
    wait_zone "$2" "$3"   # a census before the pieces are in counts water
    "$CLI" cli_nearby_prefabs 30 > "$1" 2>&1 || true
    sleep 1
    "$CLI" cli_nearby_prefabs 30 > "$1" 2>&1 || true
    printf '%s pieces: ' "$(basename "$1")"; grep -cE 'wood_(pole2|beam|floor|stair)|stone_(wall|floor|stair|arch)' "$1" || true
}

say "6/8 fords first (short), then the wood bridge with a $DECAY_SOAK s decay soak"
BRIDGE=""
echo "$SITES" | while read -r label idx x z w kit biome dir fromY toY bed; do
    [ "$label" = "bridge" ] && continue
    dx=${dir%,*}; dz=${dir#*,}
    by=$(python3 -c "print(max($fromY, $toY))")
    shoot "c$idx-ford-$label-$kit-w$w-bed$bed" "$x" "$z" "$w" "$dx" "$dz" "$by"
done
BRIDGE=$(echo "$SITES" | grep '^bridge ' || true)
if [ -n "$BRIDGE" ]; then
    set -- $BRIDGE
    idx=$2; x=$3; z=$4; w=$5; kit=$6; dir=$8; fromY=$9; toY=${10}
    dx=${dir%,*}; dz=${dir#*,}
    by=$(python3 -c "print(max($fromY, $toY))")
    name="c$idx-bridge-$kit-w$w"
    census "$OUT/$name-census-t0.txt" "$x" "$z"
    shoot "$name" "$x" "$z" "$w" "$dx" "$dz" "$by"
    "$CLI" cli_teleport "$x" 35 "$z" | tail -1     # dwell within 10 m for the soak
    echo "soaking $DECAY_SOAK s at the bridge (player at the deck)"
    sleep "$DECAY_SOAK"
    census "$OUT/$name-census-t8.txt" "$x" "$z"
    shoot "$name-t8" "$x" "$z" "$w" "$dx" "$dz" "$by"
fi

# ---- 7. save, restore config ----
say "7/8 save"
"$CLI" save | tail -1
sleep 3
python3 - "$CFG" <<'EOF'
import sys, re
p = sys.argv[1]; s = open(p).read()
s = re.sub(r'^ForceRegenerate = .*$', 'ForceRegenerate = false', s, flags=re.M)
open(p, 'w').write(s)
EOF

say "8/8 done"
ls -la "$OUT"

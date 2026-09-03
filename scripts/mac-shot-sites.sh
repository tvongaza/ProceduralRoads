#!/bin/sh
# Photograph chosen crossings of the world the client currently has loaded:
#   ./scripts/mac-shot-sites.sh <World> <tag> <crossing index>...
# Reads the site geometry from road_spots, teleports the player to the near
# bank, waits for the zone's ruin pieces, clears the view, and captures side A,
# side B (camera at bank + 10 m, standoff 1.2 x width, min 30 m) and a top
# view. Output: validation-results/screenshots/<World>-<tag>/c<idx>-*.png.
# The client must already be in-world (mac-shoot.sh leaves it there).
set -e
export DOTNET_ROOT="${DOTNET_ROOT:-/opt/homebrew/opt/dotnet/libexec}"
REPO_DIR="$(cd "$(dirname "$0")/.." && pwd)"
CLI="${VALHEIM_CLI:-$REPO_DIR/../valheimCLI/CLI/bin/Debug/net10.0/valheim-cli}"
WORLD="${1:?world name}"; TAG="${2:?tag}"; shift 2
OUT="$REPO_DIR/validation-results/screenshots/$WORLD-$TAG"
mkdir -p "$OUT"

"$CLI" road_spots > /dev/null 2>&1 || true
sleep 1
"$CLI" road_spots 2>&1 | grep '^CROSSING' > "$OUT/spots.txt"

for idx in "$@"; do
    line=$(grep "^CROSSING $idx " "$OUT/spots.txt" || true)
    [ -n "$line" ] || { echo "no crossing $idx"; continue; }
    eval "$(python3 - "$line" <<'PY'
import re, sys
kv = dict(re.findall(r"(\w+)=(\S+)", sys.argv[1]))
dx, dz = kv["dir"].split(",")
by = max(float(kv["fromY"]), float(kv["toY"]))
print(f'x={kv["x"]} z={kv["z"]} w={kv["width"]} dx={dx} dz={dz} by={by} kind={kv["kind"]} kit={kv["kit"]}')
PY
)"
    name="c$idx-$kind-$kit-w$w"
    echo "== $name at ($x,$z)"
    "$CLI" cli_teleport "$x" 45 "$z" | tail -1
    for i in $(seq 1 25); do
        out=$("$CLI" road_zone_ready "$x" "$z" 40 2>&1 | grep ZONE_READY | tail -1)
        case "$out" in *"ready=true"*) echo "zone ready after ${i} s"; break;; esac
        sleep 1
    done
    "$CLI" road_clear_view "$x" "$z" 60 | tail -1
    "$CLI" cli_set_tod 0.45 | tail -1
    "$CLI" env Clear | tail -1
    sleep 6
    python3 - "$name" "$x" "$z" "$w" "$dx" "$dz" "$by" "$OUT" "$CLI" <<'PY'
import sys, subprocess, time
name, x, z, w, dx, dz, by, out, cli = sys.argv[1:]
x, z, w, dx, dz, by = map(float, (x, z, w, dx, dz, by))
px, pz = -dz, dx
stand = max(30.0, w * 1.2)
poses = {
    'sideA': (x + px * stand, by + 10, z + pz * stand),
    'sideB': (x - px * stand, by + 10, z - pz * stand),
    'endA':  (x - dx * (w * 0.5 + 18), by + 6, z - dz * (w * 0.5 + 18)),
    'top':   (x + px * 4, by + max(40.0, w * 0.7), z + pz * 4),
}
for pose, (cx, cy, cz) in poses.items():
    subprocess.run([cli, 'cli_freefly_pose', f'{cx:.1f}', f'{cy:.1f}', f'{cz:.1f}', f'{x:.1f}', f'{by:.1f}', f'{z:.1f}'],
                   stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    time.sleep(2.5)
    subprocess.run(['screencapture', '-x', f'{out}/{name}-{pose}.png'])
    print(f'captured {name}-{pose}.png')
PY
done
ls "$OUT"

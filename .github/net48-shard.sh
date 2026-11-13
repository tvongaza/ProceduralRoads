#!/usr/bin/env bash
# Throwaway timing script: run one class shard of the net48 tests under Mono, serially within the shard.
#   net48-shard.sh <shard 1-4> <report.xml>
# Shards 1-3 are the classes .github/net48-shards.tsv lists for them; shard 4 is every class it does not list.
set -uo pipefail
shard=$1; report=$2
here=$(cd "$(dirname "$0")" && pwd)
console=$HOME/.nuget/packages/xunit.runner.console/2.8.1/tools/net48/xunit.console.exe
dll=ProceduralRoads.Tests/bin/Release/net48/ProceduralRoads.Tests.dll
args=()
if [ "$shard" = 4 ]; then
  while IFS=$'\t' read -r s c; do args+=(-noclass "$c"); done < "$here/net48-shards.tsv"
else
  while IFS=$'\t' read -r s c; do [ "$s" = "$shard" ] && args+=(-class "$c"); done < "$here/net48-shards.tsv"
fi
start=$(date +%s)
mono "$console" "$dll" -xml "$report" -parallel none -nologo "${args[@]}" > "$report.log" 2>&1; rc=$?
echo "SHARD $shard rc=$rc wall=$(( $(date +%s) - start ))s $(grep -o 'Total: [0-9]*, Errors: [0-9]*, Failed: [0-9]*, Skipped: [0-9]*, Time: [0-9.]*s' "$report.log" | tail -1)"
grep "\[FAIL\]" "$report.log" | head -20 || true
exit $rc

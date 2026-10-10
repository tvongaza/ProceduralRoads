#!/bin/sh
# No game or remote machine is contacted by this test.
set -eu
here=$(cd "$(dirname "$0")" && pwd)
for script in "$here"/*.sh; do
  case "$script" in *legacy-script-guard.sh|*legacy-script-guard-test.sh) continue;; esac
  sed -n '2p' "$script" | grep -q 'legacy-script-guard.sh' || {
    echo "missing default refusal: $script" >&2; exit 1;
  }
done
rc=0
out=$("$here/legacy-script-guard.sh" world-fixture.sh list 2>&1) || rc=$?
[ "$rc" -eq 3 ] && echo "$out" | grep -q 'ValheimTesting#541'
VALHEIM_TEST_LEGACY_DEBUG=1 "$here/legacy-script-guard.sh" world-fixture.sh list >/dev/null 2>&1
echo 'legacy guard: PASS'

#!/bin/sh
# Historical scripts that launch a game or edit a shared install/save refuse by default.
script=${1:?script name required}
verb=${2:-}
if [ "${VALHEIM_TEST_LEGACY_DEBUG:-}" = 1 ]; then
  echo "LEGACY OPT-IN: $script $verb uses shared Valheim state; follow its restore procedure." >&2
  exit 0
fi
cat >&2 <<EOF
REFUSED (exit 3): $script $verb is a historical shared-state test driver.
Use valheim-test start, server-load, --hold, or cli on a disposable copy.
If this workflow is missing, record the command and purpose on ValheimTesting#575
or ProceduralRoads#7 before migrating the script.
Only an intentional historical run may set VALHEIM_TEST_LEGACY_DEBUG=1.
EOF
exit 3

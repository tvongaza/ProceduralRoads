# Historical native-test scripts

The shell scripts in `scripts/` that start Valheim or edit a shared install or
world are historical. They refuse by default before staging anything. Use
`valheim-test start`, `server-load`, `--hold`, and `cli` for new runs on owned
disposable copies.

If a workflow is missing from the toolkit, record the script name, command and
purpose on [ValheimTesting#575](https://github.com/tvongaza/ValheimTesting/issues/575)
or [ProceduralRoads#7](https://github.com/tvongaza/ProceduralRoads/issues/7).
Migrate that workflow when it is next needed. An intentional historical run
can set `VALHEIM_TEST_LEGACY_DEBUG=1`; it remains responsible for backup and
cleanup. `scripts/legacy-script-guard-test.sh` checks that every shell entry
point has a guard.

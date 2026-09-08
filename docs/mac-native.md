# Native Apple Silicon client for validation (private testing)

Trialled 8 Sep 2026 02:06-02:20 UTC from the owner's research (memory
"mac-native-arm64-bepinex"). Package: github.com/Relokk1/valheim-native-arm64
pinned at cd5565a82dbff8332989c812cb141ad5638dbd52 (rebuilt BepInEx
5.4.23.5 core with HarmonyX 2.16.1 / MonoMod 25, universal
libdoorstop.dylib 4.5.0, play.sh = `arch -arm64` with DYLD_* via `arch -e`).

## What was done

1. `install.sh` (Valheim closed): moved the Rosetta install to
   BepInEx.backup-20260907-200611, installed BepInExPack 5.4.2333 with the
   rebuilt core, Doorstop, play.sh, Type = GameObject.
2. Carried over only Jotunn (plugins/Jotunn), ProceduralRoads and
   valheimCLI.dll plus their two configs; NOT ExpandWorldData, PlanBuild,
   MMHOOK / HookGenPatcher, HelloWorldMod (untested on the new MonoMod).
3. LogLevels += Debug (edit the four LogLevels lines; the first sed
   missed), PathfindingMaxIterations 100000, GenerateRoadsOnLoad true,
   ProceduralRoads dll = pr/validation-tooling 159be6e.
4. Pristine RoadTestPC4 fixture copied from the PC, launched with
   `play.sh -console`, driven by valheim-cli on 5555 as before.

## Result (Mac: Apple M2, 8 cores, 16 GB)

| | Rosetta (x86_64 slice) | native (arm64) |
|---|---|---|
| BepInEx | 5.4.23.3 | 5.4.23.5 rebuilt; log says `System platform: OSX Arm64` |
| launch to main menu | minutes | 11 s |
| world load, global generation of RoadTestPC4 | 368 s launch+load; generation 259.4 s | 89 s; generation 75.4 s |
| world reload, roads from the save | 102-109 s | not measured yet |
| mods loaded | Jotunn, ProceduralRoads, valheimCLI | same (Jotunn 2.29.0), same 46 roads / 20 626 points |
| errors | none | one `DllNotFoundException: AppleCoreNativeMac` at startup (Valheim's Apple services plugin; harmless so far) |

Gaming PC (Ryzen 7 9800X3D) for the same fixture: 73 s world load
including generation, 13 s without, so about 60 s of generation. The
native M2 is now within ~25 % of the PC for generation; under Rosetta it
was 4x slower than the PC.

## Switching and rules

- `<private>/scripts/mac-native.sh status|on|off|launch`: the two
  installs are BepInEx (active) and BepInEx.rosetta / BepInEx.native
  (parked). The installer's backup directory must be renamed to
  BepInEx.rosetta once (`mv BepInEx.backup-20260907-200611 BepInEx.rosetta`).
- The fork's mac-launch.sh / mac-shoot.sh still call run_bepinex.sh
  (Rosetta wrapper); with the native install active use `play.sh -console`
  or mac-native.sh launch. TODO: make the Mac scripts pick the launcher.
- uninstall.sh deletes the loader without restoring the backup: never
  run it; use the switch script.
- Mods with native or Intel-only libraries, Metal-less asset bundles
  (pink), or exotic MonoMod hook combinations may fail; test each before
  relying on it (PlanBuild / ExpandWorldData not yet tried natively).
- Private testing only; nothing here goes upstream.
